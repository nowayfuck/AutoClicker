using System.Windows.Forms;

namespace AutoClicker;

internal static class SelfTest
{
    public static int Run()
    {
        try
        {
            var settings = new AppSettings
            {
                Cps = 6000, DelaySeconds = -2, Key = Keys.MediaPlayPause,
                StartHotkey = Keys.Q, StopHotkey = Keys.MediaNextTrack
            };
            settings.Normalize();
            if (settings.Cps != 5000 || settings.DelaySeconds != 0 || settings.Key != Keys.MediaPlayPause ||
                settings.StartHotkey != Keys.Q || settings.StopHotkey != Keys.MediaNextTrack) return 1;
            settings.Cps = 100;
            settings.Normalize();
            if (settings.Cps != 500) return 8;

            var clicks = 0;
            var engine = new ClickEngine(
                new ClickSettings(ClickMode.Mouse, 500, 0, MouseButton.Left, Keys.Space, []),
                (_, _) => Interlocked.Increment(ref clicks));
            engine.Start();
            Thread.Sleep(180);
            if (Volatile.Read(ref clicks) < 40 || !engine.Snapshot().Running) return 2;
            engine.Stop();
            var stoppedAt = Volatile.Read(ref clicks);
            Thread.Sleep(80);
            if (Volatile.Read(ref clicks) != stoppedAt || engine.Snapshot().Running) return 3;

            var delayed = new ClickEngine(
                new ClickSettings(ClickMode.Keyboard, 500, 2, MouseButton.Left, Keys.Space, []),
                (_, _) => Interlocked.Increment(ref clicks));
            delayed.Start();
            delayed.Stop();
            Thread.Sleep(80);
            if (Volatile.Read(ref clicks) != stoppedAt) return 4;
            var sequence = new List<TouchPoint>();
            var points = new[]
            {
                new TouchPoint(12, 34, MouseButton.Left),
                new TouchPoint(-56, 78, MouseButton.Right),
                new TouchPoint(90, 12, MouseButton.Middle)
            };
            var replay = new ClickEngine(
                new ClickSettings(ClickMode.Points, 5000, 0, MouseButton.Left, Keys.Space, points),
                (_, point) => { lock (sequence) sequence.Add(point!.Value); });
            replay.Start();
            Thread.Sleep(75);
            replay.Stop();
            lock (sequence)
            {
                if (sequence.Count < 100) return 5;
                for (var index = 0; index < sequence.Count; index++)
                    if (sequence[index] != points[index % points.Length]) return 6;
            }
            using var recorder = new MouseRecorder(IntPtr.Zero, _ => { });
            return 0;
        }
        catch (Exception) { return 7; }
    }
}
