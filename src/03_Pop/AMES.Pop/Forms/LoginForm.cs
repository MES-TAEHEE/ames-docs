using System.Diagnostics;
using System.Drawing.Drawing2D;
using AMES.Contracts.Auth;
using AMES.Contracts.Dto;
using AMES.Contracts.Enums;
using AMES.Data.Services;
using AMES.Pop.Common;

namespace AMES.Pop.Forms;

/// <summary>
/// INJ-01 POP Login — fullscreen entry point. Fills the whole 1920×1080
/// panel; central login card on a dark background.
/// </summary>
public sealed class LoginForm : PopForm
{
    private const int PinLength = 4;

    private readonly PopAuthService  _auth = PopServices.PopAuth;
    private readonly System.Windows.Forms.Timer _clockTimer;

    private readonly Label           _lblClock;
    private readonly Label           _lblStatus;
    private readonly PinDotsDisplay  _pinDots;

    private readonly System.Text.StringBuilder _scanBuf = new();
    private readonly System.Text.StringBuilder _pinBuf  = new();
    private DateTime _lockedUntil = DateTime.MinValue;
    private int      _consecutiveFailures;

    public LoginForm()
    {
        Text = "A-MES POP · Shift Login";

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
            BackColor = PopTheme.BgOuter,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        root.Controls.Add(BuildTopBar(),                                     0, 0);
        root.Controls.Add(BuildBody(out _lblStatus, out _pinDots),           0, 1);
        Controls.Add(root);

        _lblClock = (Label)Controls.Find("lblClock", true)[0];
        _clockTimer = new System.Windows.Forms.Timer { Interval = 1_000 };
        _clockTimer.Tick += (_, _) => _lblClock.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        _clockTimer.Start();

        KeyPress += OnFormKeyPress;
        KeyDown  += OnFormKeyDown;
    }

    /// <summary>Root-level Esc shouldn't quietly close — confirm first.</summary>
    protected override void OnEscapePressed()
    {
        var r = MessageBox.Show(this, "Exit A-MES POP shell?", "Exit",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
        if (r == DialogResult.Yes) Application.Exit();
    }

    // ──────────────────────────────────────────────────────────────────────
    private Panel BuildTopBar()
    {
        var bar = new Panel { Dock = DockStyle.Fill, BackColor = PopTheme.BgTopBar };
        bar.Paint += (_, e) =>
        {
            using var pen = new Pen(PopTheme.Border);
            e.Graphics.DrawLine(pen, 0, bar.Height - 1, bar.Width, bar.Height - 1);
        };

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1,
            Padding = new Padding(28, 6, 28, 6), BackColor = Color.Transparent,
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var logo = new Label
        {
            Text = "A-MES", Font = PopTheme.Logo, ForeColor = PopTheme.Accent,
            AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 32, 0),
        };
        var info = new Label
        {
            Text = $"INJ-01  ·  {AppConfig.Current.StationId}  ·  {AppConfig.Current.LineId}",
            Font = PopTheme.MonoBold, ForeColor = PopTheme.AccentSoft,
            AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 22, 0, 0),
        };

        var right = new TableLayoutPanel
        {
            ColumnCount = 1, RowCount = 2, AutoSize = true,
            BackColor = Color.Transparent, Anchor = AnchorStyles.Right, Margin = new Padding(0),
        };
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        right.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        right.Controls.Add(new Label
        {
            Text = "● ONLINE", Font = PopTheme.BodyBold, ForeColor = PopTheme.TextOk,
            AutoSize = true, Anchor = AnchorStyles.Right,
        }, 0, 0);
        right.Controls.Add(new Label
        {
            Name = "lblClock",
            Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm"),
            Font = PopTheme.Mono, ForeColor = PopTheme.TextDim,
            AutoSize = true, Anchor = AnchorStyles.Right,
            Margin = new Padding(0, 4, 0, 0),
        }, 0, 1);

        grid.Controls.Add(logo,  0, 0);
        grid.Controls.Add(info,  1, 0);
        grid.Controls.Add(right, 2, 0);
        bar.Controls.Add(grid);
        return bar;
    }

    private Panel BuildBody(out Label statusLabel, out PinDotsDisplay pinDots)
    {
        var body = new Panel { Dock = DockStyle.Fill, BackColor = PopTheme.BgOuter };

        // Center a fixed-width card; let it grow vertically with the screen.
        var center = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 3, BackColor = PopTheme.BgOuter,
        };
        center.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        center.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 720));
        center.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        center.RowStyles.Add(new RowStyle(SizeType.Percent, 10));
        center.RowStyles.Add(new RowStyle(SizeType.Percent, 80));
        center.RowStyles.Add(new RowStyle(SizeType.Percent, 10));

        var card = new Panel { Dock = DockStyle.Fill, BackColor = PopTheme.BgCard, Padding = new Padding(32) };
        card.Paint += (_, e) =>
        {
            using var pen = new Pen(PopTheme.Border);
            e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1);
        };

        var stack = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8, BackColor = Color.Transparent,
        };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // 0 title
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // 1 subtitle
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 140)); // 2 badge zone
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // 3 OR separator
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));  // 4 PIN dots
        stack.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // 5 keypad
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));      // 6 status
        stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));  // 7 info grid

        stack.Controls.Add(new Label
        {
            Text = "SHIFT LOGIN", Font = PopTheme.TitleBig, ForeColor = PopTheme.Accent,
            AutoSize = true, Anchor = AnchorStyles.None, Margin = new Padding(0, 8, 0, 0),
        }, 0, 0);
        stack.Controls.Add(new Label
        {
            Text = "Scan your badge or enter your PIN",
            Font = PopTheme.Body, ForeColor = PopTheme.TextDim,
            AutoSize = true, Anchor = AnchorStyles.None, Margin = new Padding(0, 6, 0, 14),
        }, 0, 1);

        stack.Controls.Add(BuildBadgeZone(), 0, 2);

        stack.Controls.Add(new Label
        {
            Text = "─────  OR · ENTER PIN  ─────",
            Font = PopTheme.MonoBold, ForeColor = PopTheme.TextDim,
            AutoSize = true, Anchor = AnchorStyles.None, Margin = new Padding(0, 16, 0, 10),
        }, 0, 3);

        pinDots = new PinDotsDisplay
        {
            Length = PinLength, Filled = 0, Dock = DockStyle.Fill,
            Margin = new Padding(140, 4, 140, 10),
            BackColor = Color.Black, ForeColor = PopTheme.Accent,
        };
        stack.Controls.Add(pinDots, 0, 4);

        stack.Controls.Add(BuildKeypad(), 0, 5);

        statusLabel = new Label
        {
            Text = " ", Font = PopTheme.BodyBold, ForeColor = PopTheme.TextDim,
            AutoSize = false, Dock = DockStyle.Top, Height = 32,
            TextAlign = ContentAlignment.MiddleCenter, Margin = new Padding(0, 8, 0, 4),
        };
        stack.Controls.Add(statusLabel, 0, 6);

        stack.Controls.Add(BuildInfoGrid(), 0, 7);

        card.Controls.Add(stack);
        center.Controls.Add(card, 1, 1);
        body.Controls.Add(center);
        return body;
    }

    private Panel BuildBadgeZone()
    {
        var zone = new Panel
        {
            Dock = DockStyle.Fill, BackColor = PopTheme.BgBadge, Margin = new Padding(0, 6, 0, 0),
        };
        zone.Paint += (_, e) =>
        {
            using var pen = new Pen(PopTheme.AccentDeep, 2f) { DashStyle = DashStyle.Dash };
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.DrawRectangle(pen, 1, 1, zone.Width - 2, zone.Height - 2);
        };

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            BackColor = Color.Transparent, Padding = new Padding(28, 0, 28, 0),
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        grid.Controls.Add(new Label
        {
            Text = "[ BADGE ]",
            Font = new Font("Consolas", 22F, FontStyle.Bold),
            ForeColor = PopTheme.Accent,
            AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 0, 30, 0),
        }, 0, 0);

        var textStack = new TableLayoutPanel
        {
            ColumnCount = 1, RowCount = 2, AutoSize = true,
            BackColor = Color.Transparent, Anchor = AnchorStyles.Left, Margin = new Padding(0),
        };
        textStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        textStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        textStack.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        textStack.Controls.Add(new Label
        {
            Text = "Scan Employee Badge", Font = PopTheme.TitleMid, ForeColor = PopTheme.Accent,
            AutoSize = true, Margin = new Padding(0, 0, 0, 4),
        }, 0, 0);
        textStack.Controls.Add(new Label
        {
            Text = "Code-128 barcode  ·  USB HID scanner",
            Font = PopTheme.BodySmall, ForeColor = PopTheme.AccentSoft,
            AutoSize = true,
        }, 0, 1);
        grid.Controls.Add(textStack, 1, 0);

        zone.Controls.Add(grid);
        return zone;
    }

    private TableLayoutPanel BuildKeypad()
    {
        var pad = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 4,
            BackColor = Color.Transparent, Margin = new Padding(100, 8, 100, 8),
        };
        for (var i = 0; i < 3; i++) pad.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34f));
        for (var i = 0; i < 4; i++) pad.RowStyles.Add(new RowStyle(SizeType.Percent, 25f));

        var keys = new (string Text, Action Handler, Color Bg)[]
        {
            ("1", () => OnDigit('1'), PopTheme.BgKey),
            ("2", () => OnDigit('2'), PopTheme.BgKey),
            ("3", () => OnDigit('3'), PopTheme.BgKey),
            ("4", () => OnDigit('4'), PopTheme.BgKey),
            ("5", () => OnDigit('5'), PopTheme.BgKey),
            ("6", () => OnDigit('6'), PopTheme.BgKey),
            ("7", () => OnDigit('7'), PopTheme.BgKey),
            ("8", () => OnDigit('8'), PopTheme.BgKey),
            ("9", () => OnDigit('9'), PopTheme.BgKey),
            ("DEL", OnBackspace,       PopTheme.BgKeyDel),
            ("0", () => OnDigit('0'), PopTheme.BgKey),
            ("OK", OnSubmitPin,        PopTheme.BgKeyOk),
        };
        foreach (var (text, h, bg) in keys)
        {
            var b = new Button
            {
                Text = text, Font = PopTheme.KeyDigit, ForeColor = Color.White,
                BackColor = bg, FlatStyle = FlatStyle.Flat, Dock = DockStyle.Fill,
                Margin = new Padding(6), Cursor = Cursors.Hand, TabStop = false,
            };
            b.FlatAppearance.BorderColor = PopTheme.Border;
            b.FlatAppearance.BorderSize  = 1;
            b.FlatAppearance.MouseOverBackColor = ControlPaint.Light(bg, 0.15f);
            b.Click += (_, _) => h();
            pad.Controls.Add(b);
        }
        return pad;
    }

    private Panel BuildInfoGrid()
    {
        var wrap = new Panel { Dock = DockStyle.Fill, BackColor = PopTheme.BgInfo, Margin = new Padding(0, 10, 0, 0) };
        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1,
            BackColor = Color.Transparent, Padding = new Padding(24, 12, 24, 12),
        };
        for (var i = 0; i < 3; i++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34f));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        grid.Controls.Add(InfoCell("LINE",    AppConfig.Current.LineId),       0, 0);
        grid.Controls.Add(InfoCell("SHIFT",   AppConfig.Current.DefaultShift), 1, 0);
        grid.Controls.Add(InfoCell("STATION", AppConfig.Current.StationId),    2, 0);
        wrap.Controls.Add(grid);
        return wrap;
    }

    private static TableLayoutPanel InfoCell(string caption, string value)
    {
        var c = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
            BackColor = Color.Transparent, Margin = new Padding(0),
        };
        c.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        c.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        c.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        c.Controls.Add(new Label
        {
            Text = caption, Font = PopTheme.InfoCaption, ForeColor = PopTheme.AccentSoft,
            AutoSize = true, Margin = new Padding(2, 0, 0, 4),
        }, 0, 0);
        c.Controls.Add(new Label
        {
            Text = value, Font = PopTheme.InfoValue, ForeColor = PopTheme.TextWhite,
            AutoSize = true, Margin = new Padding(2, 0, 0, 0),
        }, 0, 1);
        return c;
    }

    // ── Input handlers ────────────────────────────────────────────────────
    private void OnDigit(char d)
    {
        if (IsLocked) return;
        if (_pinBuf.Length >= PinLength) return;
        _pinBuf.Append(d);
        _pinDots.Filled = _pinBuf.Length;
        if (_pinBuf.Length == PinLength) OnSubmitPin();
    }

    private void OnBackspace()
    {
        if (IsLocked || _pinBuf.Length == 0) return;
        _pinBuf.Length--;
        _pinDots.Filled = _pinBuf.Length;
    }

    private void OnSubmitPin()
    {
        if (IsLocked) return;
        if (_pinBuf.Length < PinLength)
        {
            ShowStatus("PIN must be 4 digits", PopTheme.TextFail);
            return;
        }
        var attemptedId = _lastScannedBadge ?? "E001";
        var pin         = _pinBuf.ToString();
        _pinBuf.Clear();
        _pinDots.Filled = 0;
        TryLogin(new LoginRequest
        {
            AttemptedId = attemptedId, Pin = pin, Method = AuthMethod.Pin,
            TerminalId  = AppConfig.Current.StationId,
            LineId      = AppConfig.Current.LineId,
            ShiftCode   = AppConfig.Current.DefaultShift,
        });
    }

    private string? _lastScannedBadge;

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
        TryLogin(new LoginRequest
        {
            AttemptedId = scanned, Pin = null, Method = AuthMethod.Badge,
            TerminalId  = AppConfig.Current.StationId,
            LineId      = AppConfig.Current.LineId,
            ShiftCode   = AppConfig.Current.DefaultShift,
        });
    }

    private void TryLogin(LoginRequest req)
    {
        if (IsLocked)
        {
            var wait = (int)Math.Ceiling((_lockedUntil - DateTime.Now).TotalSeconds);
            ShowStatus($"× Locked. Try again in {wait}s.", PopTheme.TextFail);
            return;
        }
        Cursor = Cursors.WaitCursor;
        LoginOutcome outcome;
        try { outcome = _auth.Login(req); }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
            ShowStatus($"× DB error: {ex.GetType().Name}", PopTheme.TextFail);
            return;
        }
        finally { Cursor = Cursors.Default; }

        if (outcome.IsSuccess)
        {
            _consecutiveFailures = 0;
            ShowStatus($"OK  Welcome, {outcome.Session!.EmployeeName}", PopTheme.TextOk);
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
        ShowStatus($"{msg}   ({outcome.FailReason})", PopTheme.TextFail);
        if (_consecutiveFailures >= 3)
        {
            _lockedUntil          = DateTime.Now.AddSeconds(30);
            _consecutiveFailures  = 0;
            ShowStatus("× 3 fails — terminal locked for 30s.", PopTheme.TextFail);
        }
    }

    private void OpenDashboard(PopSessionDto session)
    {
        Hide();
        // Route to module-specific dashboard. Falls back to INJ if unknown.
        Form dash = AppConfig.Current.ModuleCode switch
        {
            "IMG" => new Img02DashboardForm(session),
            _     => new Inj02DashboardForm(session),
        };
        using (dash)
        {
            dash.ShowDialog(this);
        }
        _pinBuf.Clear(); _scanBuf.Clear();
        _lastScannedBadge = null;
        _pinDots.Filled   = 0;
        ShowStatus(" ", PopTheme.TextDim);
        Show();
        Activate();
    }

    private bool IsLocked => DateTime.Now < _lockedUntil;
    private void ShowStatus(string text, Color color) { _lblStatus.Text = text; _lblStatus.ForeColor = color; }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _clockTimer.Dispose();
        base.Dispose(disposing);
    }
}
