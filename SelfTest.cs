using System.Windows.Forms;

namespace AutoClicker;

internal static class SelfTest
{
    public static int Run()
    {
        try
        {
            var settings = new AppSettings { Cps = 100, DelaySeconds = -2 };
            settings.Normalize();
            if (settings.Cps != 30 || settings.DelaySeconds != 0) return 1;

            var clicks = 0;
            var engine = new ClickEngine(
                new ClickSettings(ClickMode.Mouse, 30, 0, MouseButton.Left, Keys.Space),
                _ => Interlocked.Increment(ref clicks));
            engine.Start();
            Thread.Sleep(180);
            if (Volatile.Read(ref clicks) < 3 || !engine.Snapshot().Running) return 2;
            engine.Stop();
            var stoppedAt = Volatile.Read(ref clicks);
            Thread.Sleep(80);
            if (Volatile.Read(ref clicks) != stoppedAt || engine.Snapshot().Running) return 3;

            var delayed = new ClickEngine(
                new ClickSettings(ClickMode.Keyboard, 10, 2, MouseButton.Left, Keys.Space),
                _ => Interlocked.Increment(ref clicks));
            delayed.Start();
            delayed.Stop();
            Thread.Sleep(80);
            if (Volatile.Read(ref clicks) != stoppedAt) return 4;
            return 0;
        }
        catch (Exception) { return 5; }
    }
}
