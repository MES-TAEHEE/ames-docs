using System.Diagnostics;
using AMES.Contracts.Auth;
using AMES.Contracts.Dto;
using AMES.Contracts.Enums;
using AMES.Data.Connection;
using AMES.Data.Repositories;
using AMES.Data.Services;
using AMES.Pop.Common;

namespace AMES.Pop.Forms;

/// <summary>
/// INJ-01 POP Login — entry point for shop-floor terminals.
///
/// UI mirrors VOL05_INJ01 spec:
///   • Top bar: A-MES logo · terminal/line · ONLINE · clock
///   • Card:    title · badge-scan zone · OR · PIN display · 4×3 keypad
///   • Footer:  Line / Shift / Equipment from AppConfig
///
/// Badge scan behaves like a USB-HID keyboard: characters arrive in
/// rapid succession, terminated by Enter (CR). Anything that decodes
/// before Enter is treated as an EmployeeNo and sent through PopAuthService
/// with AuthMethod.Badge.
///
/// PIN path: tap the keypad to fill 4 digits, then tap ✓ to submit.
/// </summary>
public sealed class LoginForm : Form
{
    private readonly PopAuthService _authService;

    // Theme — kept inline so the file is self-contained until we extract a Theme class.
    private static readonly Color BgOuter      = Color.Black;
    private static readonly Color BgCard       = Color.FromArgb(10, 22, 40);    // #0A1628
    private static readonly Color BgKey        = Color.FromArgb(28, 38, 60);
    private static readonly Color BgKeyHover   = Color.FromArgb(37, 99, 235);
    private static readonly Color BgKeyOk      = Color.FromArgb(13, 184, 122);  // #0DB87A
    private static readonly Color BgKeyDel     = Color.FromArgb(70, 30, 30);
    private static readonly Color Accent       = Color.FromArgb(147, 197, 253); // mod-tint
    private static readonly Color AccentDeep   = Color.FromArgb(37, 99, 235);   // mod
    private static readonly Color TextDim      = Color.FromArgb(120, 154, 165);
    private static readonly Color TextOk       = Color.FromArgb(93, 255, 196);
    private static readonly Color TextFail     = Color.FromArgb(252, 165, 165);

    private const int PinLength = 4;

    // Widgets
    private readonly Label   _lblClock;
    private readonly Label   _lblTerminal;
    private readonly Label   _lblOnline;
    private readonly Label   _lblPinDisplay;
    private readonly Label   _lblStatus;
    private readonly Label   _lblLineValue;
    private readonly Label   _lblShiftValue;
    private readonly Label   _lblStationValue;
    private readonly Panel   _badgePanel;
    private readonly System.Windows.Forms.Timer _clockTimer;

    /// <summary>Accumulates printable keystrokes; flushed as a badge scan on Enter.</summary>
    private readonly System.Text.StringBuilder _scanBuf = new();
    private readonly System.Text.StringBuilder _pinBuf  = new();
    private DateTime _lockedUntil = DateTime.MinValue;
    private int      _consecutiveFailures;

    public LoginForm()
    {
        // 1) Build the service stack (will be moved to DI when AMES.Application lands).
        var factory  = new AmesConnectionFactory(AppConfig.Current.ConnectionString);
        var auth     = new AuthRepository(factory);
        var sessions = new PopSessionRepository(factory);
        _authService = new PopAuthService(auth, sessions);

        // 2) Form chrome.
        Text            = "A-MES POP · Shift Login";
        ClientSize      = new Size(720, 820);
        BackColor       = BgOuter;
        ForeColor       = Color.White;
        Font            = new Font("Segoe UI", 10F);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        StartPosition   = FormStartPosition.CenterScreen;
        MaximizeBox     = false;
        KeyPreview      = true;
        DoubleBuffered  = true;

        // ============================== Top bar ==================================
        var topBar = new Panel {
            Dock = DockStyle.Top, Height = 56,
            BackColor = Color.FromArgb(12, 26, 46),
        };
        topBar.Paint += (_, e) => {
            using var pen = new Pen(Color.FromArgb(37, 99, 235, 80));
            e.Graphics.DrawLine(pen, 0, topBar.Height - 1, topBar.Width, topBar.Height - 1);
        };

        var logo = new Label {
            Text      = "A-MES",
            Font      = new Font("Segoe UI", 22F, FontStyle.Bold),
            ForeColor = Accent,
            AutoSize  = true,
            Location  = new Point(20, 10),
        };
        _lblTerminal = new Label {
            Text      = $"INJ-01 · {AppConfig.Current.StationId} · {AppConfig.Current.LineId}",
            Font      = new Font("Consolas", 10F),
            ForeColor = Color.FromArgb(147, 197, 253, 180),
            AutoSize  = true,
            Location  = new Point(140, 22),
        };
        _lblOnline = new Label {
            Text      = "● ONLINE",
            Font      = new Font("Segoe UI", 9F, FontStyle.Bold),
            ForeColor = TextOk,
            AutoSize  = true,
            Location  = new Point(540, 14),
        };
        _lblClock = new Label {
            Text      = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            Font      = new Font("Consolas", 10F),
            ForeColor = TextDim,
            AutoSize  = true,
            Location  = new Point(540, 32),
        };
        topBar.Controls.AddRange([logo, _lblTerminal, _lblOnline, _lblClock]);

        // ============================== Card ====================================
        var card = new Panel {
            Location  = new Point(60, 80),
            Size      = new Size(600, 700),
            BackColor = BgCard,
        };
        card.Paint += (_, e) => {
            using var pen = new Pen(Color.FromArgb(37, 99, 235, 100), 1);
            e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
        };

        var cardTitle = new Label {
            Text      = "SHIFT LOGIN",
            Font      = new Font("Segoe UI", 22F, FontStyle.Bold),
            ForeColor = Accent,
            AutoSize  = true,
            Location  = new Point(180, 24),
        };
        var cardSub = new Label {
            Text      = "Scan your badge or enter your PIN",
            Font      = new Font("Segoe UI", 9F),
            ForeColor = TextDim,
            AutoSize  = true,
            Location  = new Point(180, 64),
        };

        // ---------- Badge scan zone (dashed border, big icon) ----------
        _badgePanel = new Panel {
            Location  = new Point(30, 100),
            Size      = new Size(540, 110),
            BackColor = Color.FromArgb(15, 35, 65),
        };
        _badgePanel.Paint += (_, e) => DrawDashedBorder(e.Graphics, _badgePanel.ClientRectangle,
                                                       Color.FromArgb(37, 99, 235, 140), 2f);
        var badgeIcon = new Label {
            Text      = "[BADGE]",
            Font      = new Font("Consolas", 16F, FontStyle.Bold),
            ForeColor = Accent,
            AutoSize  = true,
            Location  = new Point(24, 36),
        };
        var badgeTitle = new Label {
            Text      = "Scan Employee Badge",
            Font      = new Font("Segoe UI", 13F, FontStyle.Bold),
            ForeColor = Accent,
            AutoSize  = true,
            Location  = new Point(120, 28),
        };
        var badgeSub = new Label {
            Text      = "Code-128 barcode · USB HID scanner",
            Font      = new Font("Segoe UI", 9F),
            ForeColor = Color.FromArgb(147, 197, 253, 150),
            AutoSize  = true,
            Location  = new Point(120, 56),
        };
        _badgePanel.Controls.AddRange([badgeIcon, badgeTitle, badgeSub]);

        // ---------- OR separator ----------
        var orLabel = new Label {
            Text      = "─────  OR · PIN  ─────",
            Font      = new Font("Consolas", 9F),
            ForeColor = TextDim,
            AutoSize  = true,
            Location  = new Point(220, 232),
        };

        // ---------- PIN display ----------
        _lblPinDisplay = new Label {
            Text      = MaskedPin(),
            Font      = new Font("Consolas", 32F, FontStyle.Bold),
            ForeColor = Accent,
            BackColor = Color.Black,
            BorderStyle = BorderStyle.FixedSingle,
            TextAlign = ContentAlignment.MiddleCenter,
            Size      = new Size(280, 66),
            Location  = new Point(160, 262),
        };

        // ---------- Keypad ----------
        var keypad = new TableLayoutPanel {
            Location  = new Point(160, 344),
            Size      = new Size(280, 240),
            ColumnCount = 3,
            RowCount    = 4,
            BackColor   = BgCard,
        };
        for (var i = 0; i < 3; i++) keypad.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        for (var i = 0; i < 4; i++) keypad.RowStyles   .Add(new RowStyle   (SizeType.Percent, 25f));

        var keyLayout = new (string Label, Action Handler, Color? Bg)[] {
            ("1", () => OnDigit('1'), null), ("2", () => OnDigit('2'), null), ("3", () => OnDigit('3'), null),
            ("4", () => OnDigit('4'), null), ("5", () => OnDigit('5'), null), ("6", () => OnDigit('6'), null),
            ("7", () => OnDigit('7'), null), ("8", () => OnDigit('8'), null), ("9", () => OnDigit('9'), null),
            ("<", OnBackspace,         BgKeyDel),
            ("0", () => OnDigit('0'), null),
            ("OK", OnSubmitPin,        BgKeyOk),
        };
        foreach (var (text, handler, bg) in keyLayout)
        {
            var btn = new Button {
                Text      = text,
                Font      = new Font("Segoe UI", 16F, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = bg ?? BgKey,
                FlatStyle = FlatStyle.Flat,
                Dock      = DockStyle.Fill,
                Margin    = new Padding(3),
                Cursor    = Cursors.Hand,
                TabStop   = false,
            };
            btn.FlatAppearance.BorderColor = Color.FromArgb(37, 99, 235, 100);
            btn.FlatAppearance.BorderSize  = 1;
            btn.FlatAppearance.MouseOverBackColor = bg.HasValue
                ? ControlPaint.Light(bg.Value)
                : BgKeyHover;
            btn.Click += (_, _) => handler();
            keypad.Controls.Add(btn);
        }

        // ---------- Status row ----------
        _lblStatus = new Label {
            Text      = " ",
            Font      = new Font("Consolas", 10F, FontStyle.Bold),
            ForeColor = TextDim,
            TextAlign = ContentAlignment.MiddleCenter,
            Size      = new Size(540, 28),
            Location  = new Point(30, 596),
        };

        // ---------- Info grid ----------
        var infoGrid = new Panel {
            Location  = new Point(30, 632),
            Size      = new Size(540, 56),
            BackColor = Color.FromArgb(8, 18, 32),
        };
        infoGrid.Controls.AddRange([
            InfoLabel("LINE",      AppConfig.Current.LineId,       0,   out _lblLineValue),
            InfoLabel("SHIFT",     AppConfig.Current.DefaultShift, 180, out _lblShiftValue),
            InfoLabel("STATION",   AppConfig.Current.StationId,    360, out _lblStationValue),
        ]);

        card.Controls.AddRange([
            cardTitle, cardSub, _badgePanel, orLabel,
            _lblPinDisplay, keypad, _lblStatus, infoGrid,
        ]);

        Controls.AddRange([topBar, card]);

        // ============================== Clock ===================================
        _clockTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        _clockTimer.Tick += (_, _) => _lblClock.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        _clockTimer.Start();

        // Capture scanner keystrokes form-wide (KeyPreview=true above).
        KeyPress += OnFormKeyPress;
        KeyDown  += OnFormKeyDown;
    }

    // ============================ PIN handling ==================================

    private void OnDigit(char d)
    {
        if (IsLocked) return;
        if (_pinBuf.Length >= PinLength) return;
        _pinBuf.Append(d);
        _lblPinDisplay.Text = MaskedPin();
        if (_pinBuf.Length == PinLength) OnSubmitPin();
    }

    private void OnBackspace()
    {
        if (IsLocked || _pinBuf.Length == 0) return;
        _pinBuf.Length--;
        _lblPinDisplay.Text = MaskedPin();
    }

    private void OnSubmitPin()
    {
        if (IsLocked) return;
        if (_pinBuf.Length < PinLength)
        {
            ShowStatus("PIN must be 4 digits", TextFail);
            return;
        }

        // For PIN path, AttemptedId is whatever the user has bound to the badge
        // number; until the keypad supports typing an EmployeeNo we treat the
        // 4-digit PIN as also encoding the user. Real flow: PIN-only login is
        // rare, badge scan is the norm. For now require a *prior* badge scan
        // OR fall back to single-user PIN-only mode for solo dev terminals.
        // → Until a small "Who are you?" prompt is added, default to E001.
        var attemptedId = _lastScannedBadge ?? "E001";
        var pin         = _pinBuf.ToString();

        TryLogin(new LoginRequest {
            AttemptedId = attemptedId,
            Pin         = pin,
            Method      = AuthMethod.Pin,
            TerminalId  = AppConfig.Current.StationId,
            LineId      = AppConfig.Current.LineId,
            ShiftCode   = AppConfig.Current.DefaultShift,
        });

        _pinBuf.Clear();
        _lblPinDisplay.Text = MaskedPin();
    }

    // ============================ Badge scan ====================================

    private string? _lastScannedBadge;

    /// <summary>
    /// USB-HID badge scanners type characters very fast and finish with Enter.
    /// We accumulate alphanumeric KeyPress events into _scanBuf and flush on Enter.
    /// </summary>
    private void OnFormKeyPress(object? sender, KeyPressEventArgs e)
    {
        if (char.IsLetterOrDigit(e.KeyChar))
        {
            _scanBuf.Append(e.KeyChar);
            e.Handled = true;
        }
    }

    private void OnFormKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode != Keys.Enter) return;
        e.SuppressKeyPress = true;

        var scanned = _scanBuf.ToString().Trim();
        _scanBuf.Clear();
        if (string.IsNullOrEmpty(scanned)) return;
        _lastScannedBadge = scanned;

        TryLogin(new LoginRequest {
            AttemptedId = scanned,
            Pin         = null,
            Method      = AuthMethod.Badge,
            TerminalId  = AppConfig.Current.StationId,
            LineId      = AppConfig.Current.LineId,
            ShiftCode   = AppConfig.Current.DefaultShift,
        });
    }

    // ============================ Auth dispatch =================================

    private void TryLogin(LoginRequest req)
    {
        if (IsLocked)
        {
            var wait = (int)Math.Ceiling((_lockedUntil - DateTime.Now).TotalSeconds);
            ShowStatus($"× Locked. Try again in {wait}s.", TextFail);
            return;
        }

        Cursor = Cursors.WaitCursor;
        LoginOutcome outcome;
        try
        {
            outcome = _authService.Login(req);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            ShowStatus($"× DB error: {ex.GetType().Name}", TextFail);
            return;
        }
        finally
        {
            Cursor = Cursors.Default;
        }

        if (outcome.IsSuccess)
        {
            _consecutiveFailures = 0;
            ShowStatus($"OK  Welcome, {outcome.Session!.EmployeeName}", TextOk);
            OpenDashboard(outcome.Session!);
            return;
        }

        _consecutiveFailures++;
        var msg = outcome.Result switch
        {
            AuthResult.BadCredentials    => "× Bad credentials",
            AuthResult.InactiveAccount   => "× Account inactive",
            AuthResult.LineNotAuthorized => $"× Not authorized for {req.LineId}",
            AuthResult.Locked            => "× Account locked",
            _                            => "× Login failed",
        };
        ShowStatus(msg + $"   ({outcome.FailReason})", TextFail);

        if (_consecutiveFailures >= 3)
        {
            _lockedUntil = DateTime.Now.AddSeconds(30);
            _consecutiveFailures = 0;
            ShowStatus("× 3 fails — terminal locked for 30s.", TextFail);
        }
    }

    private void OpenDashboard(PopSessionDto session)
    {
        Hide();
        using var dash = new DashboardForm(session);
        dash.ShowDialog(this);
        // Returning here means the user logged out → clear PIN, show login again.
        _pinBuf.Clear();
        _lastScannedBadge = null;
        _lblPinDisplay.Text = MaskedPin();
        ShowStatus(" ", TextDim);
        Show();
        Activate();
    }

    // ============================ Helpers =======================================

    private bool IsLocked => DateTime.Now < _lockedUntil;
    private string MaskedPin()
    {
        var n = _pinBuf.Length;
        return string.Concat(
            string.Concat(Enumerable.Repeat("● ", n)),
            string.Concat(Enumerable.Repeat("_ ", PinLength - n))).TrimEnd();
    }

    private void ShowStatus(string text, Color color)
    {
        _lblStatus.Text      = text;
        _lblStatus.ForeColor = color;
    }

    private static Control InfoLabel(string caption, string value, int x, out Label valueLabel)
    {
        var box = new Panel {
            Location = new Point(x, 4), Size = new Size(170, 48), BackColor = Color.Transparent,
        };
        var cap = new Label {
            Text      = caption,
            Font      = new Font("Consolas", 8F, FontStyle.Bold),
            ForeColor = Color.FromArgb(147, 197, 253, 150),
            AutoSize  = true,
            Location  = new Point(8, 4),
        };
        valueLabel = new Label {
            Text      = value,
            Font      = new Font("Segoe UI", 11F, FontStyle.Bold),
            ForeColor = Color.White,
            AutoSize  = true,
            Location  = new Point(8, 22),
        };
        box.Controls.AddRange([cap, valueLabel]);
        return box;
    }

    private static void DrawDashedBorder(Graphics g, Rectangle rect, Color color, float width)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var pen = new Pen(color, width) {
            DashStyle = System.Drawing.Drawing2D.DashStyle.Dash,
        };
        g.DrawRectangle(pen, 1, 1, rect.Width - 2, rect.Height - 2);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _clockTimer.Dispose();
        base.Dispose(disposing);
    }
}
