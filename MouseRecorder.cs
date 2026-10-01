using System.ComponentModel;
using System.Runtime.InteropServices;

namespace AutoClicker;

internal sealed class MouseRecorder : IDisposable
{
    private const int WhMouseLl = 14;
    private const int WmLeftDown = 0x0201;
    private const int WmRightDown = 0x0204;
    private const int WmMiddleDown = 0x0207;
    private const uint Injected = 0x00000001;
    private const uint GaRoot = 2;

    private readonly IntPtr panel;
    private readonly Action<TouchPoint> recorded;
    private readonly HookProc callback;
    private IntPtr hook;

    [StructLayout(LayoutKind.Sequential)]
    private struct Point { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseData
    {
        public Point Position;
        public uint Data;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    private delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc procedure, IntPtr module, uint threadId);

    [DllImport("user32.dll")]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr message, IntPtr data);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(Point point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr window, uint flags);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);

    public MouseRecorder(IntPtr panel, Action<TouchPoint> recorded)
    {
        this.panel = panel;
        this.recorded = recorded;
        callback = OnMouseEvent;
        hook = SetWindowsHookEx(WhMouseLl, callback, GetModuleHandle(null), 0);
        if (hook == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error(),
            "Não foi possível iniciar a gravação de pontos.");
    }

    private IntPtr OnMouseEvent(int code, IntPtr message, IntPtr data)
    {
        if (code >= 0)
        {
            MouseButton? button = message.ToInt32() switch
            {
                WmLeftDown => MouseButton.Left,
                WmRightDown => MouseButton.Right,
                WmMiddleDown => MouseButton.Middle,
                _ => null
            };
            if (button is not null)
            {
                var click = Marshal.PtrToStructure<MouseData>(data);
                var window = GetAncestor(WindowFromPoint(click.Position), GaRoot);
                GetWindowThreadProcessId(window, out var processId);
                if ((click.Flags & Injected) == 0 && window != panel && processId != Environment.ProcessId)
                    recorded(new TouchPoint(click.Position.X, click.Position.Y, button.Value));
            }
        }
        return CallNextHookEx(hook, code, message, data);
    }

    public void Dispose()
    {
        if (hook == IntPtr.Zero) return;
        UnhookWindowsHookEx(hook);
        hook = IntPtr.Zero;
    }
}
