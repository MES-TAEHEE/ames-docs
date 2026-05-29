using AMES.Contracts.Dto;
using AMES.Pop.Common;

namespace AMES.Pop.Forms;

/// <summary>
/// IMG-02 Wrapping Dashboard. Same overall layout as INJ-02 but the equipment
/// card is replaced with a Bond Status card (pressure/temp/time) and the
/// "mold shot" gauge is replaced with the mounted fabric roll's remaining
/// meters. Nav row points at IMG-03 .. IMG-07.
/// </summary>
public sealed class Img02DashboardForm : PopForm
{
    private readonly PopSessionDto _session;
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private readonly System.Windows.Forms.Timer _clockTimer;

    private readonly Label _lblWoNumber, _lblWoItem, _lblWoProgress, _lblWoPct, _lblClock;
    private readonly Label _lblBondState, _lblBondParams;
    private readonly StatusLED _bondLed;
    private readonly Label _lblDefectRate, _lblFabricRemain, _lblOee;
    private readonly ProgressBar _woBar;
    private readonly HourlyBarChart _chart;

    public Img02DashboardForm(PopSessionDto session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        Text = "A-MES POP · IMG-02 Dashboard";

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = PopTheme.BgOuter,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 32));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 56));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 130));

        var topBar = PopShell.BuildTopBar("IMG-02 · Dashboard", session);
        _lblClock = (Label)topBar.Controls.Find("lblClock", true)[0];
        root.Controls.Add(topBar, 0, 0);

        // ── row 1: WO progress (60%) + Bond status (40%) ──────────────────
        var row1 = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            BackColor = PopTheme.BgOuter, Padding = new Padding(28, 18, 28, 8),
        };
        row1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        row1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        row1.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        // WO card
        var woCard = NewCardPadded(18);
        var woStack = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown,
            WrapContents = false, BackColor = PopTheme.BgCard,
        };
        woStack.Controls.Add(PopShell.SectionHeader("▼ CURRENT WO"));
        _lblWoNumber = NewBig("(no WO)", 28f, PopTheme.Accent);
        _lblWoNumber.AutoEllipsis = true; _lblWoNumber.MaximumSize = new Size(900, 0);
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
        _lblWoProgress = NewBig("—", 48f, PopTheme.TextOk);
        _lblWoProgress.Anchor = AnchorStyles.Left;
        progRow.Controls.Add(_lblWoProgress, 0, 0);
        _lblWoPct = NewBig("0%", 32f, PopTheme.Accent);
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

        // Bond card
        var bondCard = NewCardPadded(14);
        var bondStack = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = PopTheme.BgCard,
        };
        bondStack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bondStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        bondStack.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        bondStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        bondStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        bondStack.Controls.Add(PopShell.SectionHeader("▼ BOND STATUS"), 0, 0);
        _bondLed = new StatusLED { LampColor = PopTheme.TextDim, Dock = DockStyle.Fill, BackColor = PopTheme.BgCard };
        bondStack.Controls.Add(_bondLed, 0, 1);
        _lblBondState = new Label
        {
            Text = "—", Font = new Font("Segoe UI", 32f, FontStyle.Bold),
            ForeColor = PopTheme.TextDim, AutoSize = false, Height = 50,
            Dock = DockStyle.Top, TextAlign = ContentAlignment.MiddleCenter,
        };
        bondStack.Controls.Add(_lblBondState, 0, 2);
        _lblBondParams = new Label
        {
            Text = "no recipe", Font = PopTheme.Mono, ForeColor = PopTheme.TextDim,
            AutoSize = false, AutoEllipsis = true, Height = 26,
            Dock = DockStyle.Top, TextAlign = ContentAlignment.MiddleCenter,
        };
        bondStack.Controls.Add(_lblBondParams, 0, 3);
        bondCard.Controls.Add(bondStack);
        row1.Controls.Add(bondCard, 1, 0);

        root.Controls.Add(row1, 0, 1);

        // ── row 2: 3 gauges + hourly chart ────────────────────────────────
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
        _lblDefectRate   = BuildGauge("Defect Rate",     "0.0%",  PopTheme.TextOk,   out var g1);
        _lblFabricRemain = BuildGauge("Fabric Left",     "— m",   PopTheme.TextOk,   out var g2);
        _lblOee          = BuildGauge("OEE",             "—",     PopTheme.TextDim,  out var g3);
        gaugeGrid.Controls.Add(g1, 0, 0);
        gaugeGrid.Controls.Add(g2, 1, 0);
        gaugeGrid.Controls.Add(g3, 2, 0);
        row2.Controls.Add(gaugeGrid, 0, 0);

        var chartCard = NewCardPadded(18);
        var chartStack = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = PopTheme.BgCard,
        };
        chartStack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        chartStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        chartStack.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        chartStack.Controls.Add(PopShell.SectionHeader("▼ HOURLY OUTPUT  ·  Good vs Defect"), 0, 0);
        _chart = new HourlyBarChart { Dock = DockStyle.Fill };
        chartStack.Controls.Add(_chart, 0, 1);
        chartCard.Controls.Add(chartStack);
        row2.Controls.Add(chartCard, 0, 1);

        root.Controls.Add(row2, 0, 2);

        // ── nav ───────────────────────────────────────────────────────────
        var nav = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 7, RowCount = 1,
            BackColor = PopTheme.BgCard, Padding = new Padding(24, 16, 24, 16),
        };
        for (var i = 0; i < 7; i++) nav.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / 7));
        nav.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        nav.Controls.Add(PopShell.BigButton("▶  IMG-03\nProd Entry",  PopTheme.AccentDeep, Color.White, (_, _) => Open<Img03ProductionEntryForm>(), fontSize: 18f), 0, 0);
        nav.Controls.Add(PopShell.BigButton("⚡  IMG-04\nFabric Input", Color.FromArgb(120, 50, 170),  Color.White, (_, _) => Open<Img04FabricInputForm>(),    fontSize: 18f), 1, 0);
        nav.Controls.Add(PopShell.BigButton("▶  IMG-05\nDefect",      Color.FromArgb(180, 70, 30),   Color.White, (_, _) => Open<Img05DefectForm>(),         fontSize: 18f), 2, 0);
        nav.Controls.Add(PopShell.BigButton("▶  IMG-06\nBond Setup",  Color.FromArgb(170,120,20),    Color.White, (_, _) => Open<Img06BondSetupForm>(),      fontSize: 18f), 3, 0);
        nav.Controls.Add(PopShell.BigButton("▶  IMG-07\nProd Status", PopTheme.BgKey,                Color.White, (_, _) => Open<Img07ProdStatusForm>(),     fontSize: 18f), 4, 0);
        nav.Controls.Add(PopShell.BigButton("🚨  INJ-08\nANDON",       Color.FromArgb(180, 30, 30),   Color.White, (_, _) => Open<Inj08AndonForm>(),          fontSize: 18f), 5, 0);
        nav.Controls.Add(PopShell.BigButton("◀  LOGOUT",               Color.FromArgb(60, 60, 60),    Color.White, (_, _) => ConfirmLogout(),                 fontSize: 18f), 6, 0);
        root.Controls.Add(nav, 0, 3);

        Controls.Add(root);

        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) ConfirmLogout(); };
        FormClosing += (_, _) => CloseSessionBestEffort();

        _clockTimer   = new System.Windows.Forms.Timer { Interval = 1_000 };
        _refreshTimer = new System.Windows.Forms.Timer { Interval = 5_000 };
        _clockTimer  .Tick += (_, _) => _lblClock.Text = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        _refreshTimer.Tick += (_, _) => Refresh_();
        _clockTimer.Start();
        _refreshTimer.Start();
        Refresh_();
    }

    private void Refresh_()
    {
        try
        {
            var wo = PopServices.WorkOrders.GetActiveForTerminal(_session.LineId, _session.TerminalId)
                  ?? PopServices.WorkOrders.ListForLine(_session.LineId).FirstOrDefault();
            if (wo is not null)
            {
                _lblWoNumber.Text = wo.WoNumber;
                _lblWoItem.Text   = $"{wo.ItemNameEn ?? wo.ItemName}  ({wo.ItemNo})";
                _lblWoProgress.Text = $"{wo.CompletedQty:0}  /  {wo.OrderQty:0}";
                _lblWoPct.Text    = $"{wo.ProgressPct:0}%";
                _woBar.Value      = Math.Clamp((int)wo.ProgressPct, 0, 100);

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
                _lblWoItem.Text   = "Open IMG-03 (or accept a WO in WO Confirm)";
                _lblWoProgress.Text = "—";
                _lblWoPct.Text    = "0%";
                _woBar.Value      = 0;
            }

            var bond = PopServices.Bond.GetActiveForLine(_session.LineId);
            if (bond is not null)
            {
                _bondLed.LampColor       = PopTheme.TextOk;
                _lblBondState.Text       = "APPLIED";
                _lblBondState.ForeColor  = PopTheme.TextOk;
                _lblBondParams.Text      = $"P {bond.PressureSp:0.0}bar · T {bond.TempSp:0}°C · {bond.HoldSecSp}s";
            }
            else
            {
                _bondLed.LampColor       = PopTheme.TextDim;
                _lblBondState.Text       = "NO RECIPE";
                _lblBondState.ForeColor  = PopTheme.TextDim;
                _lblBondParams.Text      = "Open IMG-06 to load a bond recipe";
            }

            var roll = PopServices.Fabric.GetMountedRoll(_session.LineId);
            if (roll is not null)
            {
                _lblFabricRemain.Text = $"{roll.RemainingM:0.0} m";
                _lblFabricRemain.ForeColor = roll.RemainingM <= 5  ? PopTheme.TextFail
                                            : roll.RemainingM <= 10 ? PopTheme.AccentSoft
                                            : PopTheme.TextOk;
            }
            else
            {
                _lblFabricRemain.Text = "— m";
                _lblFabricRemain.ForeColor = PopTheme.TextDim;
            }

            _chart.SetData(PopServices.Production.GetHourlyToday(_session.LineId));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Img02 refresh] {ex.Message}");
        }
    }

    // ── helpers shared with the form ──────────────────────────────────────
    private static Panel NewCardPadded(int padding) => new()
    {
        Dock = DockStyle.Fill, BackColor = PopTheme.BgCard,
        Padding = new Padding(padding), Margin = new Padding(6),
    };
    private static Label NewBig(string text, float size, Color fore) => new()
    {
        Text = text, Font = new Font("Segoe UI", size, FontStyle.Bold),
        ForeColor = fore, AutoSize = true, Margin = new Padding(4, 0, 0, 0),
    };
    private static Label BuildGauge(string caption, string value, Color valueColor, out Panel container)
    {
        container = NewCardPadded(14);
        var stack = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = PopTheme.BgCard,
        };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        stack.RowStyles.Add(new RowStyle(SizeType.Percent, 70));
        stack.RowStyles.Add(new RowStyle(SizeType.Percent, 30));
        var v = new Label
        {
            Text = value, Font = PopTheme.GaugeValue, ForeColor = valueColor,
            AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
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

    private void Open<TForm>() where TForm : Form
    {
        Form? f = null;
        try
        {
            var ctor = typeof(TForm).GetConstructor(new[] { typeof(PopSessionDto) });
            f = ctor != null ? (Form)ctor.Invoke(new object[] { _session }) : Activator.CreateInstance<TForm>();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Cannot open screen", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        if (f is null) return;
        f.ShowDialog(this);
        Refresh_();
    }

    private void ConfirmLogout()
    {
        var r = MessageBox.Show(this, "Log out and return to login?", "Logout",
            MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
        if (r == DialogResult.Yes) Close();
    }
    private void CloseSessionBestEffort()
    {
        try { PopServices.Sessions.CloseSession(_session.SessionId, "ManualLogout"); } catch { }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _refreshTimer.Dispose(); _clockTimer.Dispose(); }
        base.Dispose(disposing);
    }
}
