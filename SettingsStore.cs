using System.Text.Json;
using System.Windows.Forms;

namespace AutoClicker;

internal sealed class AppSettings
{
    public ClickMode Mode { get; set; } = ClickMode.Mouse;
    public int Cps { get; set; } = 10;
    public int DelaySeconds { get; set; } = 3;
    public MouseButton MouseButton { get; set; } = MouseButton.Left;
    public Keys Key { get; set; } = Keys.Space;
    public Keys StartHotkey { get; set; } = Keys.F6;
    public Keys StopHotkey { get; set; } = Keys.F7;
    public List<TouchPoint> Points { get; set; } = [];

    public ClickSettings Click() => new(Mode, Cps, DelaySeconds, MouseButton, Key, Points.ToArray());

    public void Normalize()
    {
        if (!Enum.IsDefined(Mode)) Mode = ClickMode.Mouse;
        if (!Enum.IsDefined(MouseButton)) MouseButton = MouseButton.Left;
        Cps = Math.Clamp(Cps, 1, 100);
        DelaySeconds = Math.Clamp(DelaySeconds, 0, 10);
        if (!KeyOptions.IsUsable(Key)) Key = Keys.Space;
        Points ??= [];
        Points = Points.Where(point => Enum.IsDefined(point.Button)).Take(10000).ToList();
        if (!KeyOptions.IsUsable(StartHotkey)) StartHotkey = Keys.F6;
        if (!KeyOptions.IsUsable(StopHotkey) || StopHotkey == StartHotkey)
            StopHotkey = StartHotkey == Keys.F7 ? Keys.F8 : Keys.F7;
    }
}

internal static class KeyOptions
{
    public static bool IsUsable(Keys key) => (key & Keys.Modifiers) == Keys.None &&
        (int)(key & Keys.KeyCode) is >= 1 and <= 254;
}

internal static class SettingsStore
{
    public static readonly string PathOnDisk = Path.Combine(
        Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(PathOnDisk)) return new AppSettings();
            using var document = JsonDocument.Parse(File.ReadAllText(PathOnDisk));
            var root = document.RootElement;
            var settings = JsonSerializer.Deserialize<AppSettings>(root.GetRawText(),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new AppSettings();

            // Preserve speed/button/delay from the earlier Python prototype.
            if (root.TryGetProperty("delay", out var delay) && delay.TryGetInt32(out var seconds))
                settings.DelaySeconds = seconds;
            if (root.TryGetProperty("button", out var button) && button.ValueKind == JsonValueKind.String)
                settings.MouseButton = button.GetString() switch
                {
                    "right" => MouseButton.Right,
                    "middle" => MouseButton.Middle,
                    _ => MouseButton.Left
                };
            settings.Normalize();
            return settings;
        }
        catch (Exception) { return new AppSettings(); }
    }

    public static void Save(AppSettings settings)
    {
        var temporary = PathOnDisk + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings,
            new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, PathOnDisk, true);
    }
}
