using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace AutoClicker;

internal static class Palette
{
    public static readonly Color Background = Color.FromArgb(15, 20, 25);
    public static readonly Color Panel = Color.FromArgb(25, 32, 40);
    public static readonly Color Frame = Color.FromArgb(37, 47, 57);
    public static readonly Color Border = Color.FromArgb(53, 66, 76);
    public static readonly Color Text = Color.FromArgb(236, 241, 244);
    public static readonly Color Muted = Color.FromArgb(149, 164, 175);
    public static readonly Color Teal = Color.FromArgb(74, 211, 184);
    public static readonly Color TealDark = Color.FromArgb(38, 108, 97);
    public static readonly Color Coral = Color.FromArgb(233, 111, 119);
    public static readonly Color CoralDark = Color.FromArgb(113, 54, 62);
    public static readonly Color Amber = Color.FromArgb(239, 185, 104);
}

internal sealed class MainForm : Form
{
    private const int WmHotkey = 0x0312;
    private const int StartId = 101;
    private const int StopId = 102;
    private const uint ModNoRepeat = 0x4000;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr window, int id);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    private readonly AppSettings settings = SettingsStore.Load();
    private readonly ClickEngine engine;
    private readonly System.Windows.Forms.Timer updateTimer = new() { Interval = 100 };

    private Label status = null!;
    private Label detail = null!;
    private Label count = null!;
    private Label countTitle = null!;
    private Label delayValue = null!;
    private Label targetTitle = null!;
    private ModernButton startButton = null!;
    private ModernButton stopButton = null!;
    private ModernButton mouseMode = null!;
    private ModernButton keyboardMode = null!;
    private ModernButton pointsMode = null!;
    private ModernButton recordButton = null!;
    private ModernButton viewPointsButton = null!;
    private ModernButton clearPointsButton = null!;
    private ModernButton[] mouseButtons = null!;
    private KeyCaptureBox targetKey = null!;
    private KeyCaptureBox startKey = null!;
    private KeyCaptureBox stopKey = null!;
    private string? notice;
    private DateTime noticeUntil;
    private bool hotkeysAvailable;
    private bool tutorialShown;
    private MouseRecorder? recorder;
    private readonly List<TouchPoint> recordedDraft = [];

    public MainForm()
    {
        settings.Normalize();
        engine = new ClickEngine(settings.Click());
        Text = "AutoClicker";
        BackColor = Palette.Background;
        ForeColor = Palette.Text;
        Font = new Font("Segoe UI", 10f);
        ClientSize = new Size(840, 680);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        DoubleBuffered = true;

        BuildUi();
        updateTimer.Tick += (_, _) => RefreshState();
        updateTimer.Start();
        Load += (_, _) =>
        {
            hotkeysAvailable = RegisterPair(settings.StartHotkey, settings.StopHotkey);
            if (!hotkeysAvailable) ShowNotice("Atalhos em uso por outro app. Use os botões da janela.");
        };
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try
        {
            var enabled = 1;
            DwmSetWindowAttribute(Handle, 20, ref enabled, sizeof(int));
        }
        catch (DllNotFoundException) { }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        updateTimer.Stop();
        recorder?.Dispose();
        recorder = null;
        engine.Stop();
        if (IsHandleCreated)
        {
            UnregisterHotKey(Handle, StartId);
            UnregisterHotKey(Handle, StopId);
        }
        base.OnFormClosing(e);
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmHotkey)
        {
            if (message.WParam.ToInt32() == StartId) StartClicking();
            if (message.WParam.ToInt32() == StopId) StopClicking();
            return;
        }
        base.WndProc(ref message);
    }

    private void BuildUi()
    {
        AddLabel(this, "Clique contínuo", 20, 52, 600, 40, 24, FontStyle.Bold, Palette.Text);
        var tutorial = AddButton(this, "?", 776, 49, 44, 40, Palette.Frame, Palette.Text, ShowTutorial);
        new ToolTip().SetToolTip(tutorial, "Como usar os pontos de toque");

        var statusCard = AddCard(20, 123, 800, 128);
        AddLabel(statusCard, "STATUS", 20, 16, 400, 20, 9, FontStyle.Bold, Palette.Muted);
        status = AddLabel(statusCard, "Pronto para iniciar", 20, 42, 460, 31, 17, FontStyle.Bold, Palette.Muted);
        detail = AddLabel(statusCard, "", 20, 80, 465, 28, 9, FontStyle.Regular, Palette.Muted);
        startButton = AddButton(statusCard, "INICIAR", 516, 39, 119, 50, Palette.Teal, Palette.Background, StartClicking);
        stopButton = AddButton(statusCard, "PARAR", 648, 39, 119, 50, Palette.Coral, Palette.Background, StopClicking);

        var modeCard = AddCard(20, 267, 800, 65);
        AddLabel(modeCard, "MODO", 20, 22, 120, 22, 10, FontStyle.Bold, Palette.Muted);
        mouseMode = AddButton(modeCard, "MOUSE", 391, 11, 119, 42, Palette.Frame, Palette.Text, () => SetMode(ClickMode.Mouse));
        keyboardMode = AddButton(modeCard, "TECLADO", 519, 11, 119, 42, Palette.Frame, Palette.Text, () => SetMode(ClickMode.Keyboard));
        pointsMode = AddButton(modeCard, "PONTOS", 647, 11, 121, 42, Palette.Frame, Palette.Text, () => SetMode(ClickMode.Points));

        var settingsCard = AddCard(20, 348, 800, 198);
        AddLabel(settingsCard, "VELOCIDADE", 20, 16, 250, 19, 9, FontStyle.Bold, Palette.Muted);
        var speed = new ModernSlider(500, 5000, settings.Cps)
        {
            Location = new Point(20, 42), Size = new Size(425, 34)
        };
        settingsCard.Controls.Add(speed);
        var speedInput = new NumericUpDown
        {
            Minimum = 500, Maximum = 5000, Increment = 100, Value = settings.Cps,
            Location = new Point(333, 9), Size = new Size(101, 30),
            BackColor = Palette.Frame, ForeColor = Palette.Teal,
            Font = new Font("Segoe UI", 11, FontStyle.Bold),
            BorderStyle = BorderStyle.FixedSingle, TextAlign = HorizontalAlignment.Right
        };
        settingsCard.Controls.Add(speedInput);
        AddLabel(settingsCard, "CPS", 439, 12, 35, 26, 10, FontStyle.Bold, Palette.Teal);
        void ApplyCps(int value)
        {
            if (settings.Cps == value) return;
            settings.Cps = value;
            if (speed.Value != value) speed.Value = value;
            if (speedInput.Value != value) speedInput.Value = value;
            SaveAndUpdate();
        }
        speed.ValueChanged += (_, _) => ApplyCps(speed.Value);
        speedInput.ValueChanged += (_, _) => ApplyCps((int)speedInput.Value);

        AddLabel(settingsCard, "ATRASO INICIAL", 20, 99, 250, 19, 9, FontStyle.Bold, Palette.Muted);
        delayValue = AddLabel(settingsCard, "", 363, 95, 94, 26, 13, FontStyle.Bold, Palette.Amber);
        var delay = new ModernSlider(0, 10, settings.DelaySeconds)
        {
            Location = new Point(20, 125), Size = new Size(425, 34)
        };
        delay.ValueChanged += (_, _) =>
        {
            settings.DelaySeconds = delay.Value;
            delayValue.Text = $"{delay.Value} s";
            SaveAndUpdate();
        };
        settingsCard.Controls.Add(delay);

        var divider = new Panel { Location = new Point(474, 16), Size = new Size(1, 166), BackColor = Palette.Border };
        settingsCard.Controls.Add(divider);
        targetTitle = AddLabel(settingsCard, "BOTÃO DO MOUSE", 494, 16, 260, 19, 9, FontStyle.Bold, Palette.Muted);
        mouseButtons =
        [
            AddButton(settingsCard, "ESQUERDO", 494, 45, 88, 43, Palette.Frame, Palette.Text, () => SetMouseButton(MouseButton.Left)),
            AddButton(settingsCard, "DIREITO", 589, 45, 88, 43, Palette.Frame, Palette.Text, () => SetMouseButton(MouseButton.Right)),
            AddButton(settingsCard, "MEIO", 684, 45, 88, 43, Palette.Frame, Palette.Text, () => SetMouseButton(MouseButton.Middle))
        ];
        targetKey = AddCapture(settingsCard, 494, 45, 278, 43, settings.Key, KeyName);
        targetKey.CaptureStarted += () => BeginCapture("Pressione a tecla que será repetida.");
        targetKey.CaptureCanceled += RestoreRegisteredHotkeys;
        targetKey.KeyCaptured += CaptureTargetKey;
        recordButton = AddButton(settingsCard, "SELECIONAR PONTOS DE TOQUE", 494, 45, 278, 43,
            Palette.TealDark, Palette.Text, ToggleRecording);
        viewPointsButton = AddButton(settingsCard, "VER PONTOS", 494, 98, 130, 32,
            Palette.Frame, Palette.Text, ShowPoints);
        clearPointsButton = AddButton(settingsCard, "LIMPAR", 642, 98, 130, 32,
            Palette.Frame, Palette.Text, ClearPoints);
        countTitle = AddLabel(settingsCard, "CLIQUES NESTA SESSÃO", 494, 114, 270, 19, 9, FontStyle.Bold, Palette.Muted);
        count = AddLabel(settingsCard, "0", 494, 137, 270, 41, 21, FontStyle.Bold, Palette.Teal);

        var hotkeyCard = AddCard(20, 562, 800, 100);
        AddLabel(hotkeyCard, "ATALHOS GLOBAIS", 20, 12, 300, 19, 9, FontStyle.Bold, Palette.Amber);
        AddLabel(hotkeyCard, "INICIAR", 20, 37, 180, 18, 9, FontStyle.Bold, Palette.Muted);
        startKey = AddCapture(hotkeyCard, 20, 59, 180, 31, settings.StartHotkey, key => key.ToString());
        startKey.CaptureStarted += () => BeginCapture("Pressione a tecla para iniciar.");
        startKey.CaptureCanceled += RestoreRegisteredHotkeys;
        startKey.KeyCaptured += (key, modifiers) => CaptureHotkey(true, key, modifiers);
        AddLabel(hotkeyCard, "PARAR", 234, 37, 180, 18, 9, FontStyle.Bold, Palette.Muted);
        stopKey = AddCapture(hotkeyCard, 234, 59, 180, 31, settings.StopHotkey, key => key.ToString());
        stopKey.CaptureStarted += () => BeginCapture("Pressione a tecla para parar.");
        stopKey.CaptureCanceled += RestoreRegisteredHotkeys;
        stopKey.KeyCaptured += (key, modifiers) => CaptureHotkey(false, key, modifiers);
        AddLabel(hotkeyCard, "QUALQUER TECLA", 530, 57, 230, 28, 10, FontStyle.Bold, Palette.Amber);

        delayValue.Text = $"{settings.DelaySeconds} s";
        RefreshMode();
        RefreshState();
    }

    private void SetMode(ClickMode mode)
    {
        if (settings.Mode == mode) return;
        if (recorder is not null) FinishRecording();
        engine.Stop();
        settings.Mode = mode;
        SaveAndUpdate();
        RefreshMode();
        RefreshState();
        if (mode == ClickMode.Points && !tutorialShown)
        {
            tutorialShown = true;
            ShowTutorial();
        }
    }

    private void SetMouseButton(MouseButton button)
    {
        settings.MouseButton = button;
        SaveAndUpdate();
        RefreshMode();
    }

    private void RefreshMode()
    {
        mouseMode.Selected = settings.Mode == ClickMode.Mouse;
        keyboardMode.Selected = settings.Mode == ClickMode.Keyboard;
        pointsMode.Selected = settings.Mode == ClickMode.Points;
        var mouse = settings.Mode == ClickMode.Mouse;
        var points = settings.Mode == ClickMode.Points;
        targetTitle.Text = points ? $"PONTOS GRAVADOS: {(recorder is null ? settings.Points.Count : recordedDraft.Count)}" :
            mouse ? "BOTÃO DO MOUSE" : "TECLA PARA REPETIR";
        targetKey.Visible = !mouse && !points;
        recordButton.Visible = points;
        recordButton.Text = recorder is null ? "SELECIONAR PONTOS DE TOQUE" : "FINALIZAR GRAVAÇÃO";
        viewPointsButton.Visible = points;
        clearPointsButton.Visible = points;
        viewPointsButton.Enabled = recorder is null && settings.Points.Count > 0;
        clearPointsButton.Enabled = recorder is null && settings.Points.Count > 0;
        for (var index = 0; index < mouseButtons.Length; index++)
        {
            mouseButtons[index].Visible = mouse;
            mouseButtons[index].Selected = (MouseButton)index == settings.MouseButton;
        }
        countTitle.Location = new Point(494, points ? 141 : 114);
        count.Location = new Point(494, points ? 160 : 137);
        count.Size = new Size(270, points ? 30 : 41);
        countTitle.Text = settings.Mode == ClickMode.Keyboard ? "TECLAS NESTA SESSÃO" :
            "CLIQUES NESTA SESSÃO";
    }

    private void SaveAndUpdate()
    {
        settings.Normalize();
        engine.Update(settings.Click());
        try { SettingsStore.Save(settings); }
        catch (Exception) { ShowNotice("Não foi possível salvar settings.json."); }
    }

    private void StartClicking()
    {
        if (recorder is not null)
        {
            ShowNotice("Finalize a gravação antes de iniciar.");
            return;
        }
        if (settings.Mode == ClickMode.Points && settings.Points.Count == 0)
        {
            ShowNotice("Grave ao menos um ponto antes de iniciar.");
            return;
        }
        if (settings.Mode == ClickMode.Keyboard &&
            (settings.Key == settings.StartHotkey || settings.Key == settings.StopHotkey))
        {
            ShowNotice("A tecla repetida deve ser diferente dos atalhos.");
            return;
        }
        engine.Start();
        RefreshState();
    }

    private void StopClicking()
    {
        if (recorder is not null)
        {
            FinishRecording();
            return;
        }
        engine.Stop();
        RefreshState();
    }

    private void ToggleRecording()
    {
        if (recorder is not null)
        {
            FinishRecording();
            return;
        }
        engine.Stop();
        recordedDraft.Clear();
        try
        {
            recorder = new MouseRecorder(Handle, point =>
            {
                if (recordedDraft.Count < 10000) recordedDraft.Add(point);
            });
            ShowNotice("Clique nos lugares desejados. Finalize aqui ou em Parar.");
        }
        catch (Exception exception) { ShowNotice(exception.Message); }
        RefreshMode();
        RefreshState();
    }

    private void FinishRecording()
    {
        recorder?.Dispose();
        recorder = null;
        settings.Points = recordedDraft.ToList();
        SaveAndUpdate();
        RefreshMode();
        ShowNotice(settings.Points.Count == 0 ? "Nenhum ponto foi gravado." :
            $"{settings.Points.Count} pontos salvos na ordem dos cliques.");
    }

    private void ClearPoints()
    {
        if (recorder is not null) return;
        engine.Stop();
        settings.Points.Clear();
        SaveAndUpdate();
        RefreshMode();
        RefreshState();
    }

    private void ShowPoints()
    {
        using var dialog = new Form
        {
            Text = "Pontos de toque", BackColor = Palette.Background, ForeColor = Palette.Text,
            Font = Font, ClientSize = new Size(390, 330), FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false, StartPosition = FormStartPosition.CenterParent
        };
        AddLabel(dialog, "Pontos de toque", 18, 16, 350, 32, 17, FontStyle.Bold, Palette.Text);
        var list = new ListBox
        {
            Location = new Point(18, 57), Size = new Size(354, 206),
            BackColor = Palette.Panel, ForeColor = Palette.Text, BorderStyle = BorderStyle.FixedSingle,
            Font = new Font("Consolas", 10)
        };
        foreach (var (point, index) in settings.Points.Select((point, index) => (point, index)))
            list.Items.Add($"{index + 1,4}.  {point.Button,-8}  X {point.X,5}   Y {point.Y,5}");
        dialog.Controls.Add(list);
        AddButton(dialog, "REMOVER", 18, 276, 162, 39, Palette.CoralDark, Palette.Text, () =>
        {
            if (list.SelectedIndex < 0) return;
            engine.Stop();
            settings.Points.RemoveAt(list.SelectedIndex);
            SaveAndUpdate();
            RefreshMode();
            var selected = list.SelectedIndex;
            list.Items.Clear();
            foreach (var (point, index) in settings.Points.Select((point, index) => (point, index)))
                list.Items.Add($"{index + 1,4}.  {point.Button,-8}  X {point.X,5}   Y {point.Y,5}");
            if (list.Items.Count > 0) list.SelectedIndex = Math.Min(selected, list.Items.Count - 1);
        });
        AddButton(dialog, "FECHAR", 210, 276, 162, 39, Palette.Frame, Palette.Text, dialog.Close);
        dialog.ShowDialog(this);
    }

    private void ShowTutorial()
    {
        using var dialog = new Form
        {
            Text = "Como usar pontos de toque", BackColor = Palette.Background, ForeColor = Palette.Text,
            Font = Font, ClientSize = new Size(470, 310), FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false, StartPosition = FormStartPosition.CenterParent
        };
        AddLabel(dialog, "Pontos de toque", 20, 18, 420, 34, 18, FontStyle.Bold, Palette.Text);
        AddLabel(dialog, "1. Clique em Selecionar pontos de toque.", 20, 64, 430, 28, 10, FontStyle.Regular, Palette.Text);
        AddLabel(dialog, "2. Clique nas posições desejadas, na ordem certa.", 20, 105, 430, 28, 10, FontStyle.Regular, Palette.Text);
        AddLabel(dialog, "3. Clique em Finalizar gravação no painel.", 20, 146, 430, 28, 10, FontStyle.Regular, Palette.Text);
        AddLabel(dialog, "4. Ajuste o CPS e clique em Iniciar.", 20, 187, 430, 28, 10, FontStyle.Regular, Palette.Text);
        AddButton(dialog, "ENTENDI", 20, 247, 430, 42, Palette.TealDark, Palette.Text, dialog.Close);
        dialog.ShowDialog(this);
    }

    private void BeginCapture(string instruction)
    {
        UnregisterHotKey(Handle, StartId);
        UnregisterHotKey(Handle, StopId);
        hotkeysAvailable = false;
        ShowNotice(instruction);
    }

    private void RestoreRegisteredHotkeys()
    {
        hotkeysAvailable = RegisterPair(settings.StartHotkey, settings.StopHotkey);
        if (!hotkeysAvailable) ShowNotice("Atalhos em uso por outro app. Use os botões da janela.");
    }

    private void CaptureTargetKey(Keys key, Keys modifiers)
    {
        key &= Keys.KeyCode;
        if (!KeyOptions.IsUsable(key) ||
            key == settings.StartHotkey || key == settings.StopHotkey)
        {
            RestoreRegisteredHotkeys();
            ShowNotice("Escolha uma tecla diferente dos atalhos.");
            return;
        }
        settings.Key = key;
        targetKey.Value = key;
        SaveAndUpdate();
        RestoreRegisteredHotkeys();
    }

    private void CaptureHotkey(bool forStart, Keys key, Keys modifiers)
    {
        key &= Keys.KeyCode;
        var soloModifier = (key == Keys.ShiftKey && modifiers == Keys.Shift) ||
            (key == Keys.ControlKey && modifiers == Keys.Control) ||
            (key == Keys.Menu && modifiers == Keys.Alt);
        if (!KeyOptions.IsUsable(key) || (modifiers != Keys.None && !soloModifier))
        {
            RestoreRegisteredHotkeys();
            ShowNotice("Escolha uma tecla válida, sem combinação.");
            return;
        }
        var requestedStart = forStart ? key : settings.StartHotkey;
        var requestedStop = forStart ? settings.StopHotkey : key;
        if (requestedStart == requestedStop || requestedStart == settings.Key || requestedStop == settings.Key)
        {
            RestoreRegisteredHotkeys();
            ShowNotice("Use teclas diferentes para iniciar, parar e repetir.");
            return;
        }
        hotkeysAvailable = RegisterPair(requestedStart, requestedStop);
        if (!hotkeysAvailable)
        {
            RestoreRegisteredHotkeys();
            ShowNotice("Atalho em uso ou reservado pelo Windows. Escolha outro.");
            return;
        }
        settings.StartHotkey = requestedStart;
        settings.StopHotkey = requestedStop;
        startKey.Value = requestedStart;
        stopKey.Value = requestedStop;
        SaveAndUpdate();
    }

    private bool RegisterPair(Keys start, Keys stop)
    {
        if (!IsHandleCreated) return false;
        UnregisterHotKey(Handle, StartId);
        UnregisterHotKey(Handle, StopId);
        var started = RegisterHotKey(Handle, StartId, ModNoRepeat, (uint)start);
        var stopped = RegisterHotKey(Handle, StopId, ModNoRepeat, (uint)stop);
        if (started && stopped) return true;
        UnregisterHotKey(Handle, StartId);
        UnregisterHotKey(Handle, StopId);
        return false;
    }

    private void ShowNotice(string text)
    {
        notice = text;
        noticeUntil = DateTime.UtcNow.AddSeconds(5);
        RefreshState();
    }

    private void RefreshState()
    {
        if (status is null) return;
        var state = engine.Snapshot();
        var waiting = state.Running && state.Remaining > TimeSpan.Zero;
        status.Text = recorder is not null ? "Gravando pontos" : state.Error is not null ? "Entrada bloqueada" :
            waiting ? "Preparando..." : state.Running ? "Em execução" : "Pronto para iniciar";
        status.ForeColor = recorder is not null ? Palette.Amber : state.Error is not null ? Palette.Coral :
            waiting ? Palette.Amber : state.Running ? Palette.Teal : Palette.Muted;
        detail.Text = notice is not null && DateTime.UtcNow < noticeUntil ? notice :
            recorder is not null ? $"{recordedDraft.Count} pontos capturados" :
            state.Error ?? (waiting ? $"Começa em {state.Remaining.TotalSeconds:0.0} s" : "");
        if (notice is not null && DateTime.UtcNow >= noticeUntil) notice = null;
        if (settings.Mode == ClickMode.Points)
            targetTitle.Text = $"PONTOS GRAVADOS: {(recorder is null ? settings.Points.Count : recordedDraft.Count)}";
        count.Text = state.Count.ToString("N0", new System.Globalization.CultureInfo("pt-BR"));
        startButton.Enabled = recorder is null && !state.Running;
        stopButton.Enabled = recorder is not null || state.Running;
        stopButton.Text = recorder is null ? "PARAR" : "FINALIZAR";
    }

    private CardPanel AddCard(int x, int y, int width, int height)
    {
        var card = new CardPanel { Location = new Point(x, y), Size = new Size(width, height) };
        Controls.Add(card);
        return card;
    }

    private static Label AddLabel(Control parent, string text, int x, int y, int width, int height,
        float size, FontStyle style, Color color)
    {
        var label = new Label
        {
            Text = text, Location = new Point(x, y), Size = new Size(width, height),
            Font = new Font("Segoe UI", size, style), ForeColor = color,
            BackColor = Color.Transparent, TextAlign = ContentAlignment.MiddleLeft
        };
        parent.Controls.Add(label);
        return label;
    }

    private static ModernButton AddButton(Control parent, string text, int x, int y, int width,
        int height, Color fill, Color foreground, Action onClick)
    {
        var button = new ModernButton
        {
            Text = text, Location = new Point(x, y), Size = new Size(width, height),
            Fill = fill, ForeColor = foreground, Font = new Font("Segoe UI", 10, FontStyle.Bold)
        };
        button.Click += (_, _) => onClick();
        parent.Controls.Add(button);
        return button;
    }

    private static KeyCaptureBox AddCapture(Control parent, int x, int y, int width, int height,
        Keys key, Func<Keys, string> label)
    {
        var capture = new KeyCaptureBox(key, label)
        {
            Location = new Point(x, y), Size = new Size(width, height)
        };
        parent.Controls.Add(capture);
        return capture;
    }

    private static string KeyName(Keys key) => key switch
    {
        Keys.Space => "Espaço",
        Keys.Back => "Backspace",
        Keys.Escape => "Esc",
        Keys.Return => "Enter",
        _ => key.ToString()
    };
}

internal sealed class KeyCaptureBox : Control
{
    private readonly Func<Keys, string> labelFor;
    private bool capturing;
    private Keys value;

    public event Action? CaptureStarted;
    public event Action? CaptureCanceled;
    public event Action<Keys, Keys>? KeyCaptured;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Keys Value
    {
        get => value;
        set { this.value = value; Invalidate(); }
    }

    public KeyCaptureBox(Keys initial, Func<Keys, string> labelFor)
    {
        value = initial;
        this.labelFor = labelFor;
        BackColor = Palette.Frame;
        ForeColor = Palette.Text;
        Font = new Font("Segoe UI", 10, FontStyle.Bold);
        Cursor = Cursors.Hand;
        TabStop = true;
        DoubleBuffered = true;
        SetStyle(ControlStyles.Selectable, true);
    }

    protected override bool IsInputKey(Keys keyData) => capturing || base.IsInputKey(keyData);

    protected override bool ProcessCmdKey(ref Message message, Keys keyData)
    {
        if (capturing && KeyOptions.IsUsable(keyData & Keys.KeyCode))
        {
            CompleteCapture(keyData & Keys.KeyCode, keyData & Keys.Modifiers);
            return true;
        }
        return base.ProcessCmdKey(ref message, keyData);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left)
        {
            Focus();
            BeginCapture();
        }
        base.OnMouseDown(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!capturing && (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space))
        {
            BeginCapture();
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }
        if (capturing)
        {
            CompleteCapture(e.KeyCode, e.Modifiers);
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }
        base.OnKeyDown(e);
    }

    protected override void OnLostFocus(EventArgs e)
    {
        if (capturing)
        {
            capturing = false;
            Invalidate();
            CaptureCanceled?.Invoke();
        }
        base.OnLostFocus(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Palette.Frame);
        using var border = new Pen(capturing ? Palette.Teal : Palette.Border,
            capturing ? 2f : 1f);
        e.Graphics.DrawRectangle(border, 1, 1, Width - 3, Height - 3);
        var display = capturing ? "Pressione uma tecla..." : labelFor(Value);
        TextRenderer.DrawText(e.Graphics, display, Font, new Rectangle(10, 0, Width - 20, Height),
            capturing ? Palette.Teal : Palette.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }

    private void BeginCapture()
    {
        if (capturing) return;
        capturing = true;
        Invalidate();
        CaptureStarted?.Invoke();
    }

    private void CompleteCapture(Keys key, Keys modifiers)
    {
        capturing = false;
        Invalidate();
        KeyCaptured?.Invoke(key, modifiers);
    }
}

internal sealed class CardPanel : Panel
{
    public CardPanel()
    {
        BackColor = Palette.Panel;
        DoubleBuffered = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Palette.Border);
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }
}

internal sealed class ModernButton : Button
{
    private bool hover;
    private bool selected;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color Fill { get; set; } = Palette.Frame;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Selected
    {
        get => selected;
        set { selected = value; Invalidate(); }
    }

    public ModernButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer, true);
        Cursor = Cursors.Hand;
        FlatStyle = FlatStyle.Flat;
        TabStop = true;
    }

    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(Parent?.BackColor ?? Palette.Panel);
        var baseColor = Selected ? Palette.TealDark : Fill;
        var fill = !Enabled ? Color.FromArgb(53, 64, 73) :
            hover ? ControlPaint.Light(baseColor, 0.12f) : baseColor;
        using var path = Rounded(new Rectangle(0, 0, Width - 1, Height - 1), 6);
        using var brush = new SolidBrush(fill);
        e.Graphics.FillPath(brush, path);
        if (Selected)
        {
            using var border = new Pen(Palette.Teal, 1.2f);
            e.Graphics.DrawPath(border, path);
        }
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle,
            Enabled ? ForeColor : Palette.Muted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }

    private static GraphicsPath Rounded(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = radius * 2;
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class ModernSlider : Control
{
    private readonly int minimum;
    private readonly int maximum;
    private int value;
    private bool dragging;
    public event EventHandler? ValueChanged;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Value
    {
        get => value;
        set
        {
            var next = Math.Clamp(value, minimum, maximum);
            if (this.value == next) return;
            this.value = next;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public ModernSlider(int minimum, int maximum, int value)
    {
        this.minimum = minimum;
        this.maximum = maximum;
        this.value = Math.Clamp(value, minimum, maximum);
        BackColor = Palette.Panel;
        DoubleBuffered = true;
        Cursor = Cursors.Hand;
        TabStop = true;
        SetStyle(ControlStyles.Selectable, true);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        dragging = true;
        Focus();
        SetFromX(e.X);
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (dragging) SetFromX(e.X);
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        dragging = false;
        base.OnMouseUp(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Down) Value--;
        if (e.KeyCode == Keys.Right || e.KeyCode == Keys.Up) Value++;
        base.OnKeyDown(e);
    }

    private void SetFromX(int x)
    {
        var ratio = Math.Clamp((x - 10.0) / Math.Max(1, Width - 20), 0, 1);
        Value = minimum + (int)Math.Round(ratio * (maximum - minimum));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(Palette.Panel);
        var width = Width - 20;
        var position = 10 + (int)Math.Round(width * (value - minimum) / (double)(maximum - minimum));
        using var track = new SolidBrush(Palette.Frame);
        using var active = new SolidBrush(Palette.Teal);
        e.Graphics.FillRectangle(track, 10, 14, width, 6);
        e.Graphics.FillRectangle(active, 10, 14, position - 10, 6);
        e.Graphics.FillEllipse(active, position - 9, 8, 18, 18);
    }
}
