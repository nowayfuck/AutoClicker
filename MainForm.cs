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
    private Label speedValue = null!;
    private Label delayValue = null!;
    private Label targetTitle = null!;
    private ModernButton startButton = null!;
    private ModernButton stopButton = null!;
    private ModernButton mouseMode = null!;
    private ModernButton keyboardMode = null!;
    private ModernButton[] mouseButtons = null!;
    private KeyCaptureBox targetKey = null!;
    private KeyCaptureBox startKey = null!;
    private KeyCaptureBox stopKey = null!;
    private string? notice;
    private DateTime noticeUntil;
    private bool hotkeysAvailable;

    public MainForm()
    {
        settings.Normalize();
        engine = new ClickEngine(settings.Click());
        Text = "AutoClicker";
        BackColor = Palette.Background;
        ForeColor = Palette.Text;
        Font = new Font("Segoe UI", 10f);
        ClientSize = new Size(840, 700);
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
        AddLabel(this, "AUTOCLICKER  /  WINDOWS", 21, 17, 500, 23, 9, FontStyle.Bold, Palette.Teal);
        AddLabel(this, "Clique contínuo", 20, 42, 600, 40, 24, FontStyle.Bold, Palette.Text);
        AddLabel(this, "Mouse e teclado sob seu controle", 21, 86, 600, 23, 10, FontStyle.Regular, Palette.Muted);

        var statusCard = AddCard(20, 123, 800, 128);
        AddLabel(statusCard, "STATUS", 20, 16, 400, 20, 9, FontStyle.Bold, Palette.Muted);
        status = AddLabel(statusCard, "Pronto para iniciar", 20, 42, 460, 31, 17, FontStyle.Bold, Palette.Muted);
        detail = AddLabel(statusCard, "Os cliques seguem a posição do cursor.", 20, 80, 465, 28, 9, FontStyle.Regular, Palette.Muted);
        startButton = AddButton(statusCard, "INICIAR", 516, 39, 119, 50, Palette.Teal, Palette.Background, StartClicking);
        stopButton = AddButton(statusCard, "PARAR", 648, 39, 119, 50, Palette.Coral, Palette.Background, StopClicking);

        var modeCard = AddCard(20, 267, 800, 65);
        AddLabel(modeCard, "MODO", 20, 22, 120, 22, 10, FontStyle.Bold, Palette.Muted);
        mouseMode = AddButton(modeCard, "MOUSE", 492, 11, 132, 42, Palette.Frame, Palette.Text, () => SetMode(ClickMode.Mouse));
        keyboardMode = AddButton(modeCard, "TECLADO", 636, 11, 132, 42, Palette.Frame, Palette.Text, () => SetMode(ClickMode.Keyboard));

        var settingsCard = AddCard(20, 348, 800, 198);
        AddLabel(settingsCard, "VELOCIDADE", 20, 16, 250, 19, 9, FontStyle.Bold, Palette.Muted);
        speedValue = AddLabel(settingsCard, "", 352, 12, 105, 26, 13, FontStyle.Bold, Palette.Teal);
        var speed = new ModernSlider(1, 30, settings.Cps)
        {
            Location = new Point(20, 42), Size = new Size(425, 34)
        };
        speed.ValueChanged += (_, _) =>
        {
            settings.Cps = speed.Value;
            speedValue.Text = $"{speed.Value} CPS";
            SaveAndUpdate();
        };
        settingsCard.Controls.Add(speed);

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
        AddLabel(settingsCard, "O atraso vale para a próxima ativação", 20, 165, 430, 20, 9, FontStyle.Regular, Palette.Muted);

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
        countTitle = AddLabel(settingsCard, "CLIQUES NESTA SESSÃO", 494, 114, 270, 19, 9, FontStyle.Bold, Palette.Muted);
        count = AddLabel(settingsCard, "0", 494, 137, 270, 41, 21, FontStyle.Bold, Palette.Teal);

        var hotkeyCard = AddCard(20, 562, 800, 100);
        AddLabel(hotkeyCard, "ATALHOS GLOBAIS", 20, 12, 300, 19, 9, FontStyle.Bold, Palette.Amber);
        AddLabel(hotkeyCard, "INICIAR", 20, 37, 180, 18, 9, FontStyle.Bold, Palette.Muted);
        startKey = AddCapture(hotkeyCard, 20, 59, 180, 31, settings.StartHotkey, key => key.ToString());
        startKey.CaptureStarted += () => BeginCapture("Pressione F2 a F12 para iniciar.");
        startKey.CaptureCanceled += RestoreRegisteredHotkeys;
        startKey.KeyCaptured += (key, modifiers) => CaptureHotkey(true, key, modifiers);
        AddLabel(hotkeyCard, "PARAR", 234, 37, 180, 18, 9, FontStyle.Bold, Palette.Muted);
        stopKey = AddCapture(hotkeyCard, 234, 59, 180, 31, settings.StopHotkey, key => key.ToString());
        stopKey.CaptureStarted += () => BeginCapture("Pressione F2 a F12 para parar.");
        stopKey.CaptureCanceled += RestoreRegisteredHotkeys;
        stopKey.KeyCaptured += (key, modifiers) => CaptureHotkey(false, key, modifiers);
        AddLabel(hotkeyCard, "F2 A F12", 530, 57, 230, 28, 10, FontStyle.Bold, Palette.Amber);

        AddLabel(this, "Sem limite de cliques ou tempo. Fechar a janela encerra a execução.",
            21, 676, 790, 19, 9, FontStyle.Regular, Palette.Muted);
        speedValue.Text = $"{settings.Cps} CPS";
        delayValue.Text = $"{settings.DelaySeconds} s";
        RefreshMode();
        RefreshState();
    }

    private void SetMode(ClickMode mode)
    {
        settings.Mode = mode;
        SaveAndUpdate();
        RefreshMode();
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
        var mouse = settings.Mode == ClickMode.Mouse;
        targetTitle.Text = mouse ? "BOTÃO DO MOUSE" : "TECLA PARA REPETIR";
        targetKey.Visible = !mouse;
        for (var index = 0; index < mouseButtons.Length; index++)
        {
            mouseButtons[index].Visible = mouse;
            mouseButtons[index].Selected = (MouseButton)index == settings.MouseButton;
        }
        countTitle.Text = mouse ? "CLIQUES NESTA SESSÃO" : "TECLAS NESTA SESSÃO";
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
        engine.Stop();
        RefreshState();
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
        if (modifiers != Keys.None || !KeyOptions.All.Any(option => option.Key == key) ||
            key == settings.StartHotkey || key == settings.StopHotkey)
        {
            RestoreRegisteredHotkeys();
            ShowNotice("Escolha uma tecla diferente dos atalhos, sem modificadores.");
            return;
        }
        settings.Key = key;
        targetKey.Value = key;
        SaveAndUpdate();
        RestoreRegisteredHotkeys();
    }

    private void CaptureHotkey(bool forStart, Keys key, Keys modifiers)
    {
        if (modifiers != Keys.None || !KeyOptions.Hotkeys.Contains(key))
        {
            RestoreRegisteredHotkeys();
            ShowNotice("Escolha uma tecla de F2 a F12, sem modificadores.");
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
        settings.StartHotkey = requestedStart;
        settings.StopHotkey = requestedStop;
        startKey.Value = requestedStart;
        stopKey.Value = requestedStop;
        SaveAndUpdate();
        hotkeysAvailable = RegisterPair(requestedStart, requestedStop);
        if (!hotkeysAvailable) ShowNotice("Atalho em uso por outro app. Escolha outra tecla.");
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
        status.Text = state.Error is not null ? "Entrada bloqueada" :
            waiting ? "Preparando..." : state.Running ? "Em execução" : "Pronto para iniciar";
        status.ForeColor = state.Error is not null ? Palette.Coral :
            waiting ? Palette.Amber : state.Running ? Palette.Teal : Palette.Muted;
        detail.Text = notice is not null && DateTime.UtcNow < noticeUntil ? notice :
            state.Error ?? (waiting ? $"Começa em {state.Remaining.TotalSeconds:0.0} s. Posicione o cursor." :
            state.Running ? "Continua até você parar." :
            hotkeysAvailable ? "Use o botão ou o atalho para iniciar." : "Use os botões para controlar.");
        if (notice is not null && DateTime.UtcNow >= noticeUntil) notice = null;
        count.Text = state.Count.ToString("N0", new System.Globalization.CultureInfo("pt-BR"));
        startButton.Enabled = !state.Running;
        stopButton.Enabled = state.Running;
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

    private static string KeyName(Keys key) =>
        KeyOptions.All.FirstOrDefault(option => option.Key == key).Label ?? key.ToString();
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
            capturing = false;
            Invalidate();
            e.Handled = true;
            e.SuppressKeyPress = true;
            KeyCaptured?.Invoke(e.KeyCode, e.Modifiers);
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

    public int Value
    {
        get => value;
        private set
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
