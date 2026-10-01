using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AutoClicker;

internal enum ClickMode { Mouse, Keyboard }
internal enum MouseButton { Left, Right, Middle }

internal sealed record ClickSettings(
    ClickMode Mode,
    int Cps,
    int DelaySeconds,
    MouseButton MouseButton,
    Keys Key);

internal readonly record struct ClickSnapshot(bool Running, long Count, TimeSpan Remaining, string? Error);

internal sealed class ClickEngine
{
    private readonly object gate = new();
    private readonly Action<ClickSettings> send;
    private ClickSettings settings;
    private CancellationTokenSource? source;
    private bool running;
    private long count;
    private DateTimeOffset firstClickAt;
    private string? error;

    public ClickEngine(ClickSettings initial, Action<ClickSettings>? sendInput = null)
    {
        settings = initial;
        send = sendInput ?? WinInput.Send;
    }

    public void Update(ClickSettings next)
    {
        lock (gate) settings = next;
    }

    public void Start()
    {
        CancellationTokenSource current;
        lock (gate)
        {
            if (running) return;
            current = new CancellationTokenSource();
            source = current;
            running = true;
            count = 0;
            error = null;
            firstClickAt = DateTimeOffset.UtcNow.AddSeconds(settings.DelaySeconds);
        }
        _ = Task.Run(() => RunAsync(current));
    }

    public void Stop()
    {
        lock (gate)
        {
            running = false;
            source?.Cancel();
        }
    }

    public ClickSnapshot Snapshot()
    {
        lock (gate)
            return new ClickSnapshot(running, count,
                running ? firstClickAt - DateTimeOffset.UtcNow : TimeSpan.Zero, error);
    }

    private async Task RunAsync(CancellationTokenSource current)
    {
        try
        {
            var delay = Math.Max(0, (firstClickAt - DateTimeOffset.UtcNow).TotalMilliseconds);
            await Task.Delay(TimeSpan.FromMilliseconds(delay), current.Token);
            while (true)
            {
                ClickSettings currentSettings;
                lock (gate)
                {
                    if (!running || source != current || current.IsCancellationRequested) break;
                    currentSettings = settings;
                    send(currentSettings);
                    count++;
                }
                await Task.Delay(TimeSpan.FromMilliseconds(1000.0 / currentSettings.Cps), current.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            lock (gate) error = exception.Message;
        }
        finally
        {
            lock (gate)
            {
                if (source == current)
                {
                    running = false;
                    source = null;
                }
            }
            current.Dispose();
        }
    }
}

internal static class WinInput
{
    private const uint MouseInputType = 0;
    private const uint KeyboardInputType = 1;
    private const uint KeyUp = 0x0002;
    private const uint ExtendedKey = 0x0001;

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx;
        public int Dy;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort VirtualKey;
        public ushort ScanCode;
        public uint Flags;
        public uint Time;
        public UIntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mouse;
        [FieldOffset(0)] public KeyboardInput Keyboard;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public uint Type;
        public InputUnion Data;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint count, [In] Input[] inputs, int size);

    public static void Send(ClickSettings settings)
    {
        Input[] inputs;
        if (settings.Mode == ClickMode.Mouse)
        {
            var (down, up) = settings.MouseButton switch
            {
                MouseButton.Right => (0x0008u, 0x0010u),
                MouseButton.Middle => (0x0020u, 0x0040u),
                _ => (0x0002u, 0x0004u)
            };
            inputs =
            [
                new Input { Type = MouseInputType, Data = new InputUnion { Mouse = new MouseInput { Flags = down } } },
                new Input { Type = MouseInputType, Data = new InputUnion { Mouse = new MouseInput { Flags = up } } }
            ];
        }
        else
        {
            var key = (ushort)settings.Key;
            var extended = settings.Key is Keys.Left or Keys.Right or Keys.Up or Keys.Down
                or Keys.Insert or Keys.Delete or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown;
            var flags = extended ? ExtendedKey : 0u;
            inputs =
            [
                new Input { Type = KeyboardInputType, Data = new InputUnion { Keyboard = new KeyboardInput { VirtualKey = key, Flags = flags } } },
                new Input { Type = KeyboardInputType, Data = new InputUnion { Keyboard = new KeyboardInput { VirtualKey = key, Flags = flags | KeyUp } } }
            ];
        }

        if (SendInput(2, inputs, Marshal.SizeOf<Input>()) != 2)
        {
            var code = Marshal.GetLastWin32Error();
            throw code == 0 ? new InvalidOperationException("O Windows bloqueou a entrada simulada.")
                : new Win32Exception(code, "O Windows bloqueou a entrada simulada.");
        }
    }
}
