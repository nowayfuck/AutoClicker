using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AutoClicker;

internal enum ClickMode { Mouse, Keyboard, Points }
internal enum MouseButton { Left, Right, Middle }
internal readonly record struct TouchPoint(int X, int Y, MouseButton Button);

internal sealed record ClickSettings(
    ClickMode Mode,
    int Cps,
    int DelaySeconds,
    MouseButton MouseButton,
    Keys Key,
    TouchPoint[] Points);

internal readonly record struct ClickSnapshot(bool Running, long Count, TimeSpan Remaining, string? Error);

internal sealed class ClickEngine
{
    private readonly object gate = new();
    private readonly Action<ClickSettings, TouchPoint?> send;
    private ClickSettings settings;
    private CancellationTokenSource? source;
    private bool running;
    private long count;
    private DateTimeOffset firstClickAt;
    private string? error;

    public ClickEngine(ClickSettings initial, Action<ClickSettings, TouchPoint?>? sendInput = null)
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
        DateTimeOffset firstAt;
        lock (gate)
        {
            if (running) return;
            current = new CancellationTokenSource();
            source = current;
            running = true;
            count = 0;
            error = null;
            firstClickAt = DateTimeOffset.UtcNow.AddSeconds(settings.DelaySeconds);
            firstAt = firstClickAt;
        }
        _ = Task.Run(() => Run(current, firstAt));
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

    private void Run(CancellationTokenSource current, DateTimeOffset firstAt)
    {
        var timerResolutionEnabled = timeBeginPeriod(1) == 0;
        try
        {
            var initialDelay = Math.Max(0, (firstAt - DateTimeOffset.UtcNow).TotalSeconds);
            var nextAt = Stopwatch.GetTimestamp() + (long)(initialDelay * Stopwatch.Frequency);
            var pointIndex = 0;
            while (WaitUntil(nextAt, current.Token))
            {
                ClickSettings currentSettings;
                lock (gate)
                {
                    if (!running || source != current || current.IsCancellationRequested) break;
                    currentSettings = settings;
                    TouchPoint? point = null;
                    if (currentSettings.Mode == ClickMode.Points)
                    {
                        if (currentSettings.Points.Length == 0)
                            throw new InvalidOperationException("Grave ao menos um ponto antes de iniciar.");
                        point = currentSettings.Points[pointIndex % currentSettings.Points.Length];
                        pointIndex++;
                    }
                    else pointIndex = 0;
                    send(currentSettings, point);
                    count++;
                }
                nextAt += (long)(Stopwatch.Frequency / (double)currentSettings.Cps);
                var now = Stopwatch.GetTimestamp();
                if (nextAt < now) nextAt = now;
            }
        }
        catch (Exception exception)
        {
            lock (gate)
                if (source == current) error = exception.Message;
        }
        finally
        {
            if (timerResolutionEnabled) timeEndPeriod(1);
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

    private static bool WaitUntil(long target, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            var remainingMs = (target - Stopwatch.GetTimestamp()) * 1000.0 / Stopwatch.Frequency;
            if (remainingMs <= 0) return true;
            if (remainingMs > 2)
                token.WaitHandle.WaitOne(Math.Max(1, (int)(remainingMs - 1)));
            else if (remainingMs > 0.3)
                Thread.Yield();
            else
                Thread.SpinWait(32);
        }
        return false;
    }

    [DllImport("winmm.dll")]
    private static extern uint timeBeginPeriod(uint period);

    [DllImport("winmm.dll")]
    private static extern uint timeEndPeriod(uint period);
}

internal static class WinInput
{
    private const uint MouseInputType = 0;
    private const uint KeyboardInputType = 1;
    private const uint KeyUp = 0x0002;
    private const uint ExtendedKey = 0x0001;
    private const uint MouseMove = 0x0001;
    private const uint MouseAbsolute = 0x8000;
    private const uint MouseVirtualDesk = 0x4000;

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

    public static void Send(ClickSettings settings, TouchPoint? point)
    {
        Input[] inputs;
        if (settings.Mode is ClickMode.Mouse or ClickMode.Points)
        {
            var button = point?.Button ?? settings.MouseButton;
            var (down, up) = button switch
            {
                MouseButton.Right => (0x0008u, 0x0010u),
                MouseButton.Middle => (0x0020u, 0x0040u),
                _ => (0x0002u, 0x0004u)
            };
            var press = new Input { Type = MouseInputType, Data = new InputUnion { Mouse = new MouseInput { Flags = down } } };
            var release = new Input { Type = MouseInputType, Data = new InputUnion { Mouse = new MouseInput { Flags = up } } };
            if (point is { } position)
            {
                var screen = SystemInformation.VirtualScreen;
                var x = (int)Math.Round(Math.Clamp((position.X - screen.Left) /
                    (double)Math.Max(1, screen.Width - 1), 0, 1) * 65535);
                var y = (int)Math.Round(Math.Clamp((position.Y - screen.Top) /
                    (double)Math.Max(1, screen.Height - 1), 0, 1) * 65535);
                inputs =
                [
                    new Input { Type = MouseInputType, Data = new InputUnion { Mouse = new MouseInput
                        { Dx = x, Dy = y, Flags = MouseMove | MouseAbsolute | MouseVirtualDesk } } },
                    press, release
                ];
            }
            else inputs = [press, release];
        }
        else
        {
            var key = (ushort)(settings.Key & Keys.KeyCode);
            var extended = settings.Key is Keys.Left or Keys.Right or Keys.Up or Keys.Down
                or Keys.Insert or Keys.Delete or Keys.Home or Keys.End or Keys.PageUp or Keys.PageDown;
            var flags = extended ? ExtendedKey : 0u;
            inputs =
            [
                new Input { Type = KeyboardInputType, Data = new InputUnion { Keyboard = new KeyboardInput { VirtualKey = key, Flags = flags } } },
                new Input { Type = KeyboardInputType, Data = new InputUnion { Keyboard = new KeyboardInput { VirtualKey = key, Flags = flags | KeyUp } } }
            ];
        }

        if (SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>()) != inputs.Length)
        {
            var code = Marshal.GetLastWin32Error();
            throw code == 0 ? new InvalidOperationException("O Windows bloqueou a entrada simulada.")
                : new Win32Exception(code, "O Windows bloqueou a entrada simulada.");
        }
    }
}
