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

    public ClickSettings Click() => new(Mode, Cps, DelaySeconds, MouseButton, Key);

    public void Normalize()
    {
        if (!Enum.IsDefined(Mode)) Mode = ClickMode.Mouse;
        if (!Enum.IsDefined(MouseButton)) MouseButton = MouseButton.Left;
        Cps = Math.Clamp(Cps, 1, 30);
        DelaySeconds = Math.Clamp(DelaySeconds, 0, 10);
        if (!KeyOptions.All.Any(option => option.Key == Key)) Key = Keys.Space;
        if (!KeyOptions.Hotkeys.Contains(StartHotkey)) StartHotkey = Keys.F6;
        if (!KeyOptions.Hotkeys.Contains(StopHotkey) || StopHotkey == StartHotkey)
            StopHotkey = StartHotkey == Keys.F7 ? Keys.F8 : Keys.F7;
    }
}

internal static class KeyOptions
{
    public static readonly Keys[] Hotkeys =
    [
        Keys.F2, Keys.F3, Keys.F4, Keys.F5, Keys.F6, Keys.F7,
        Keys.F8, Keys.F9, Keys.F10, Keys.F11, Keys.F12
    ];

    public static readonly (string Label, Keys Key)[] All = Build();

    private static (string, Keys)[] Build()
    {
        var values = new List<(string, Keys)>
        {
            ("Espaço", Keys.Space), ("Enter", Keys.Enter), ("Tab", Keys.Tab),
            ("Backspace", Keys.Back), ("Esc", Keys.Escape),
            ("Seta para cima", Keys.Up), ("Seta para baixo", Keys.Down),
            ("Seta esquerda", Keys.Left), ("Seta direita", Keys.Right),
            ("Insert", Keys.Insert), ("Delete", Keys.Delete),
            ("Home", Keys.Home), ("End", Keys.End),
            ("Page Up", Keys.PageUp), ("Page Down", Keys.PageDown)
        };
        for (var key = Keys.A; key <= Keys.Z; key++) values.Add((key.ToString(), key));
        for (var number = 0; number <= 9; number++)
            values.Add((number.ToString(), Keys.D0 + number));
        for (var key = Keys.F1; key <= Keys.F12; key++) values.Add((key.ToString(), key));
        return values.ToArray();
    }
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
