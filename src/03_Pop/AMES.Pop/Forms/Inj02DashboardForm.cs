using AMES.Contracts.Dto;
using AMES.Pop.Common;

namespace AMES.Pop.Forms;

/// <summary>
/// INJ-02 POP Dashboard — landing page after login.
/// Shows current WO progress, equipment LED, defect-rate + mold + OEE gauges,
/// hourly bar chart, and 6 big nav buttons to INJ-03 .. INJ-08 + Logout.
///
/// Refresh: 5-second timer re-queries the DB. PLC bridge is mocked.
/// </summary>
public sealed class Inj02DashboardForm : PopForm
{
    private readonly PopSessionDto _session;
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private readonly System.Windows.Forms.Timer _clockTimer;

    // mutable widgets refreshed every tick
    private readonly Label _lblWoNumber, _lblWoItem, _lblWoProgress, _lblWoPct;
    private readonly Label _lblEquipState, _lblEquipName;
    private readonly Label _lblDefectRate, _lblMoldPct, _lblOee, _lblClock;
    private readonly Panel _eqLed;
    private readonly ProgressBar _woBar;
    private readonly HourlyBarChart _chart;

    public Inj02DashboardForm(PopSessionDto session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        Text = "A-MES POP · INJ-02 Dashboard";

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4,
            BackColor = PopTheme.BgOuter,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));    // topbar
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 32));     // top cards (WO + Equip)
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 56));     // gauges + chart
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 130));   // bottom nav

        // top bar
        var topBar = PopShell.BuildTopBar("INJ-02 · Dashboard", session);
        _lblClock = (Label)topBar.Controls.Find("lblClock", true)[0];
        root.Controls.Add(topBar, 0, 0);

        // ── row 1: WO progress (60%) + Equipment status (40%) ──────────────
        var row1 = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            BackColor = PopTheme.BgOuter, Padding = new Padding(28, 18, 28, 8),
        };
        row1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        row1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        row1.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var woCard = BuildCard();
        var woStack = NewStack(8);
        woStack.Controls.Add(PopShell.SectionHeader("▼ CURRENT WO"));
        _lblWoNumber = BigLabel("(no WO)", 28f, PopTheme.Accent);
        _lblWoNumber.AutoEllipsis = true;
        _lblWoNumber.MaximumSize  = new Size(900, 0);
        woStack.Controls.Add(_lblWoNumber);
        _lblWoItem = new Label
        {
            Text = "", Font = PopTheme.BodyBold, ForeColor = PopTheme.TextDim,
            AutoSize = true, AutoEllipsis = true, MaximumSize = new Size(900, 0),
            Margin = new Padding(4, 0, 0, 18),
        };
        woStack.Controls.Add(_lblWoItem);

        var progRow = new TableLayoutPanel
        {
            Dock = DockStyle.Top, Height = 130, ColumnCount = 2, RowCount = 1,
            BackColor = PopTheme.BgCard, Margin = new Padding(0),
        };
        progRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        progRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        progRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _lblWoProgress = BigLabel("—", 48f, PopTheme.TextOk);
        _lblWoProgress.Anchor = AnchorStyles.Left;
        progRow.Controls.Add(_lblWoProgress, 0, 0);
        _lblWoPct = BigLabel("0%", 32f, PopTheme.Accent);
        _lblWoPct.Anchor = AnchorStyles.Right;
        _lblWoPct.Margin = new Padding(0, 28, 8, 0);
        progRow.Controls.Add(_lblWoPct, 1, 0);
        woStack.Controls.Add(progRow);

        _woBar = new ProgressBar
        {
            Dock = DockStyle.Top, Height = 24, Margin = new Padding(4, 0, 8, 0),
            Style = ProgressBarStyle.Continuous, Minimum = 0, Maximum = 100, Value = 0,
        };
        woStack.Controls.Add(_woBar);

        woCard.Controls.Add(woStack);
        row1.Controls.Add(woCard, 0, 0);

        // equip card
        var eqCard = BuildCard();
        var eqStack = NewStack(3);
        eqStack.Controls.Add(PopShell.SectionHeader("▼ EQUIPMENT STATUS"));
        _eqLed = new Panel
        {
            Width = 120, Height = 120, BackColor = PopTheme.TextOk,
            Anchor = AnchorStyles.None, Margin = new Padding(0, 20, 0, 12),
        };
        _eqLed.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var br = new SolidBrush(_eqLed.BackColor);
            e.Graphics.FillEllipse(br, 0, 0, _eqLed.Width, _eqLed.Height);
        };
        var eqLedHolder = new Panel { Dock = DockStyle.Top, Height = 144, BackColor = Color.Transparent };
        _eqLed.Location = new Point(eqLedHolder.Width / 2 - 60, 12);
        eqLedHolder.Resize += (_, _) => _eqLed.Location = new Point(eqLedHolder.Width / 2 - 60, 12);
        eqLedHolder.Controls.Add(_eqLed);
        eqStack.Controls.Add(eqLedHolder);
        _lblEquipState = new Label
        {
            Text = "—", Font = new Font("Segoe UI", 36f, FontStyle.Bold),
            ForeColor = PopTheme.TextOk, AutoSize = false,
            Dock = DockStyle.Top, Height = 64,
            TextAlign = ContentAlignment.MiddleCenter,
        };
        eqStack.Controls.Add(_lblEquipState);
        _lblEquipName = new Label
        {
            Text = "", Font = PopTheme.Mono, ForeColor = PopTheme.TextDim,
            AutoSize = false, AutoEllipsis = true,
            Dock = DockStyle.Top, Height = 32, TextAlign = ContentAlignment.MiddleCenter,
        };
        eqStack.Controls.Add(_lblEquipName);
        eqCard.Controls.Add(eqStack);
        row1.Controls.Add(eqCard, 1, 0);

        root.Controls.Add(row1, 0, 1);

        // ── row 2: 3 gauges + hourly chart ─────────────────────────────────
        var row2 = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
            BackColor = PopTheme.BgOuter, Padding = new Padding(28, 8, 28, 8),
        };
        row2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row2.RowStyles.Add(new RowStyle(SizeType.Percent, 38));
        row2.RowStyles.Add(new RowStyle(SizeType.Percent, 62));

        var gaugeGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = PopTheme.BgOuter,
        };
        for (var i = 0; i < 3; i++) gaugeGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34f));
        gaugeGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _lblDefectRate = BuildGauge("Defect Rate", "0.0%", PopTheme.TextOk, out var g1);
        _lblMoldPct    = BuildGauge("Mold Shot",   "0%",   PopTheme.TextOk, out var g2);
        _lblOee        = BuildGauge("OEE",         "—",    PopTheme.TextDim,out var g3);
        gaugeGrid.Controls.Add(g1, 0, 0);
        gaugeGrid.Controls.Add(g2, 1, 0);
        gaugeGrid.Controls.Add(g3, 2, 0);
        row2.Controls.Add(gaugeGrid, 0, 0);

        var chartCard = BuildCard();
        var chartStack = NewStack(2);
        chartStack.Controls.Add(PopShell.SectionHeader("▼ HOURLY OUTPUT  ·  Good vs Defect"));
        _chart = new HourlyBarChart { Dock = DockStyle.Fill };
        chartStack.Controls.Add(_chart);
        chartCard.Controls.Add(chartStack);
        row2.Controls.Add(chartCard, 0, 1);

        root.Controls.Add(row2, 0, 2);

        // ── row 3: bottom nav (6 big buttons) ──────────────────────────────
        var nav = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 7, RowCount = 1,
            BackColor = PopTheme.BgCard, Padding = new Padding(24, 16, 24, 16),
        };
        for (var i = 0; i < 7; i++) nav.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 7));
        nav.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        nav.Controls.Add(PopShell.BigButton("▶  INJ-03\nWO Confirm",     PopTheme.BgKey,    Color.White, (_, _) => Open<Inj03WoConfirmForm>(), fontSize:18f), 0, 0);
        nav.Controls.Add(PopShell.BigButton("▶  INJ-04\nProd Entry",     PopTheme.AccentDeep, Color.White, (_, _) => Open<Inj04ProductionEntryForm>(), fontSize:18f), 1, 0);
        nav.Controls.Add(PopShell.BigButton("▶  INJ-05\nDefect",         Color.FromArgb(180,70,30), Color.White, (_, _) => Open<Inj05DefectForm>(), fontSize:18f), 2, 0);
        nav.Controls.Add(PopShell.BigButton("▶  INJ-06\nMold Change",    Color.FromArgb(170,120,20), Color.White, (_, _) => Open<Inj06MoldChangeForm>(), fontSize:18f), 3, 0);
        nav.Controls.Add(PopShell.BigButton("▶  INJ-07\nProd Status",    PopTheme.BgKey,    Color.White, (_, _) => Open<Inj07ProdStatusForm>(), fontSize:18f), 4, 0);
        nav.Controls.Add(PopShell.BigButton("🚨  INJ-08\nANDON",          Color.FromArgb(180,30,30), Color.White, (_, _) => Open<Inj08AndonForm>(), fontSize:18f), 5, 0);
        nav.Controls.Add(PopShell.BigButton("◀  LOGOUT",                  Color.FromArgb(60,60,60), Color.White, (_, _) => ConfirmLogout(), fontSize:18f), 6, 0);
        root.Controls.Add(nav, 0, 3);

        Controls.Add(root);

        // Logout via Escape key (simple, since the nav row is full).
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) ConfirmLogout(); };
        FormClosing += (_, _) => CloseSessionBestEffort();

        // timers
        _clockTimer   = new System.Windows.Forms.Timer { Interval =  1_000 };
        _refreshTimer = new System.Windows.Forms.Timer { Interval =  5_000 };
        _clockTimer  .Tick += (_, _) => _lblClock.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        _refreshTimer.Tick += (_, _) => Refresh_();
        _clockTimer.Start();
        _refreshTimer.Start();

        Refresh_();   // first paint
    }

    // ── Refresh ────────────────────────────────────────────────────────────
    private void Refresh_()
    {
        try
        {
            // WO tile — most recent In Progress; else first Released on the line.
            var wo = PopServices.WorkOrders.GetActiveForTerminal(_session.LineId, _session.TerminalId)
                  ?? PopServices.WorkOrders.ListForLine(_session.LineId).FirstOrDefault();
            if (wo is not null)
            {
                _lblWoNumber.Text = wo.WoNumber;
                _lblWoItem.Text   = $"{wo.ItemNameEn ?? wo.ItemName}  ({wo.ItemNo})";
                _lblWoProgress.Text = $"{wo.CompletedQty:0}  /  {wo.OrderQty:0}";
                _lblWoPct.Text    = $"{wo.ProgressPct:0}%";
                _woBar.Value      = Math.Clamp((int)wo.ProgressPct, 0, 100);

                if (!string.IsNullOrEmpty(wo.MoldId))
                {
                    var mold = PopServices.Molds.GetById(wo.MoldId);
                    if (mold is not null)
                    {
                        _lblMoldPct.Text = $"{mold.LifePct:0}%";
                        _lblMoldPct.ForeColor = mold.Severity switch
                        {
                            "warn"  => PopTheme.AccentSoft,
                            "alert" => PopTheme.TextFail,
                            "hard"  => PopTheme.TextFail,
                            _       => PopTheme.TextOk,
                        };
                    }
                }
                var (good, defect) = PopServices.Defects.GetWoTotals(wo.WoId);
                var pct = (good + defect) == 0 ? 0.0 : defect * 100.0 / (good + defect);
                _lblDefectRate.Text = $"{pct:0.0}%";
                _lblDefectRate.ForeColor = pct >= 5 ? PopTheme.TextFail
                                          : pct >= 4 ? PopTheme.AccentSoft
                                          : PopTheme.TextOk;
            }
            else
            {
                _lblWoNumber.Text = "(no active WO)";
                _lblWoItem.Text   = "Open INJ-03 to accept a WO";
                _lblWoProgress.Text = "—";
                _lblWoPct.Text    = "0%";
                _woBar.Value      = 0;
            }

            var eq = PopServices.Equipment.GetPrimaryForLine(_session.LineId);
            if (eq is not null)
            {
                _lblEquipState.Text = eq.Status ?? "OFFLINE";
                _lblEquipName.Text  = $"{eq.EquipName}  ·  uptime {FormatUptime(eq.Uptime)}";
                var col = (eq.Status ?? "").ToUpperInvariant() switch
                {
                    "RUN"      => PopTheme.TextOk,
                    "IDLE"     => PopTheme.AccentSoft,
                    "STOP"     => PopTheme.TextFail,
                    "ALARM"    => PopTheme.TextFail,
                    _          => PopTheme.TextDim,
                };
                _eqLed.BackColor = col; _eqLed.Invalidate();
                _lblEquipState.ForeColor = col;
            }
            else
            {
                _lblEquipState.Text = "OFFLINE";
                _lblEquipName.Text  = "no equipment registered";
                _eqLed.BackColor    = PopTheme.TextDim; _eqLed.Invalidate();
            }

            _chart.SetData(PopServices.Production.GetHourlyToday(_session.LineId));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Inj02 refresh] {ex.Message}");
        }
    }

    private static string FormatUptime(TimeSpan? t) =>
        t is null ? "—" : $"{(int)t.Value.TotalHours:00}:{t.Value.Minutes:00}:{t.Value.Seconds:00}";

    // ── Helpers ────────────────────────────────────────────────────────────
    private static Panel BuildCard() => new()
    {
        Dock = DockStyle.Fill, BackColor = PopTheme.BgCard, Padding = new Padding(18),
        Margin = new Padding(6),
    };
    private static FlowLayoutPanel NewStack(int _) => new()
    {
        Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
        WrapContents = false, BackColor = Color.Transparent, AutoSize = false,
    };
    private static Label BigLabel(string text, float size, Color fore) => new()
    {
        Text = text,
        Font = new Font("Segoe UI", size, FontStyle.Bold),
        ForeColor = fore,
        AutoSize = true,
        Margin   = new Padding(4, 0, 0, 0),
    };

    private static Label BuildGauge(string caption, string value, Color valueColor, out Panel container)
    {
        container = BuildCard();
        container.Margin = new Padding(6);
        var stack = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
            BackColor = Color.Transparent,
        };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        stack.RowStyles.Add(new RowStyle(SizeType.Percent, 70));
        stack.RowStyles.Add(new RowStyle(SizeType.Percent, 30));
        var v = new Label
        {
            Text = value, Font = PopTheme.GaugeValue,
            ForeColor = valueColor, AutoSize = false, Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
        };
        var c = new Label
        {
            Text = caption.ToUpperInvariant(), Font = PopTheme.MonoBold,
            ForeColor = PopTheme.AccentSoft, AutoSize = false, Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
        };
        stack.Controls.Add(v, 0, 0);
        stack.Controls.Add(c, 0, 1);
        container.Controls.Add(stack);
        return v;
    }

    // ── Navigation ────────────────────────────────────────────────────────
    private void Open<TForm>() where TForm : Form
    {
        // Try (PopSessionDto) constructor first.
        Form? f = null;
        try
        {
            var ctor = typeof(TForm).GetConstructor(new[] { typeof(PopSessionDto) });
            if (ctor != null) f = (Form)ctor.Invoke(new object[] { _session });
            else f = Activator.CreateInstance<TForm>();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Cannot open screen", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        if (f is null) return;
        f.ShowDialog(this);
        Refresh_();   // pick up any state changes
    }

    private void ConfirmLogout()
    {
        var r = MessageBox.Show(this, "Log out and return to login?", "Logout",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
        if (r == DialogResult.Yes) Close();
    }

    private void CloseSessionBestEffort()
    {
        try { PopServices.Sessions.CloseSession(_session.SessionId, "ManualLogout"); }
        catch { /* offline-safe */ }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _refreshTimer.Dispose(); _clockTimer.Dispose(); }
        base.Dispose(disposing);
    }
}
