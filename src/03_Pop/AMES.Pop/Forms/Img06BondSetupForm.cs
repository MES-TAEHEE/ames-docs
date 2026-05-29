using AMES.Contracts.Dto;
using AMES.Pop.Common;

namespace AMES.Pop.Forms;

/// <summary>
/// IMG-06 Bond Setup — show the active PR_BondSetup for the current line
/// (pressure / temperature / hold time) plus a quick "load recipe" action
/// that writes a new APPLIED row. Real-time PLC feedback + supervisor-PIN
/// guarded edits will land in a later iteration; this version is the read-
/// and-load page operators use to verify what's mounted in the bond machine.
/// </summary>
public sealed class Img06BondSetupForm : PopForm
{
    private readonly PopSessionDto _session;
    private readonly Label _lblRecipe, _lblStatus;
    private readonly Label _lblPressureV, _lblPressureS;
    private readonly Label _lblTempV,     _lblTempS;
    private readonly Label _lblHoldV,     _lblHoldS;

    private WorkOrderDto? _wo;
    private BondSetupDto? _setup;

    public Img06BondSetupForm(PopSessionDto session)
    {
        _session = session;
        Text = "A-MES POP · IMG-06 Bond Setup";

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = PopTheme.BgOuter,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 130)); // recipe banner
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));  // 3-card grid
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 130)); // nav

        root.Controls.Add(PopShell.BuildTopBar("IMG-06 · Bond Setup", session), 0, 0);

        // ── recipe banner ───────────────────────────────────────────────
        var banner = NewCardPadded(18);
        var bGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, BackColor = PopTheme.BgCard,
        };
        bGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        bGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        bGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        bGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        bGrid.Controls.Add(new Label
        {
            Text = "▼ ACTIVE RECIPE", Font = PopTheme.MonoBold, ForeColor = PopTheme.AccentSoft,
            AutoSize = true, Margin = new Padding(2, 0, 0, 6),
        }, 0, 0);
        bGrid.Controls.Add(new Label
        {
            Text = "▼ STATUS", Font = PopTheme.MonoBold, ForeColor = PopTheme.AccentSoft,
            AutoSize = true, Anchor = AnchorStyles.Right, Margin = new Padding(0, 0, 4, 6),
        }, 1, 0);
        _lblRecipe = new Label
        {
            Text = "(none loaded)", Font = new Font("Segoe UI", 26f, FontStyle.Bold),
            ForeColor = PopTheme.Accent, AutoSize = true, AutoEllipsis = true,
            MaximumSize = new Size(1100, 0), Margin = new Padding(2, 0, 0, 0),
        };
        bGrid.Controls.Add(_lblRecipe, 0, 1);
        _lblStatus = new Label
        {
            Text = "NO RECIPE", Font = new Font("Segoe UI", 22f, FontStyle.Bold),
            ForeColor = PopTheme.TextDim, AutoSize = true,
            Anchor = AnchorStyles.Right, Margin = new Padding(0, 0, 4, 0),
        };
        bGrid.Controls.Add(_lblStatus, 1, 1);
        banner.Controls.Add(bGrid);
        var bannerWrap = new Panel { Dock = DockStyle.Fill, BackColor = PopTheme.BgOuter, Padding = new Padding(28, 8, 28, 8) };
        bannerWrap.Controls.Add(banner);
        root.Controls.Add(bannerWrap, 0, 1);

        // ── 3 parameter cards ───────────────────────────────────────────
        var paramRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1,
            BackColor = PopTheme.BgOuter, Padding = new Padding(28, 0, 28, 14),
        };
        for (var i = 0; i < 3; i++) paramRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34f));
        paramRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _lblPressureV = BuildParam("Pressure",    "—",  "bar", out var p1, out _lblPressureS);
        _lblTempV     = BuildParam("Temperature", "—",  "°C",  out var p2, out _lblTempS);
        _lblHoldV     = BuildParam("Hold Time",   "—",  "sec", out var p3, out _lblHoldS);
        paramRow.Controls.Add(p1, 0, 0);
        paramRow.Controls.Add(p2, 1, 0);
        paramRow.Controls.Add(p3, 2, 0);
        root.Controls.Add(paramRow, 0, 2);

        // ── nav ─────────────────────────────────────────────────────────
        var nav = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1,
            BackColor = PopTheme.BgCard, Padding = new Padding(16, 10, 16, 10),
        };
        nav.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        nav.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        nav.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        nav.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        nav.Controls.Add(PopShell.BigButton("← Back",            PopTheme.BgKey, Color.White, (_, _) => Close()), 0, 0);
        nav.Controls.Add(PopShell.BigButton("⟳ Refresh",         PopTheme.BgKey, Color.White, (_, _) => LoadAndPaint()), 1, 0);
        nav.Controls.Add(PopShell.BigButton("📥 Load WO Recipe", PopTheme.BgKeyOk, Color.White, (_, _) => LoadRecipe()), 2, 0);
        root.Controls.Add(nav, 0, 3);

        Controls.Add(root);
        LoadAndPaint();
    }

    private void LoadAndPaint()
    {
        _wo    = PopServices.WorkOrders.GetActiveForTerminal(_session.LineId, _session.TerminalId)
              ?? PopServices.WorkOrders.ListForLine(_session.LineId).FirstOrDefault();
        _setup = PopServices.Bond.GetActiveForLine(_session.LineId);

        if (_setup is null)
        {
            _lblRecipe.Text = "(none loaded)";
            _lblStatus.Text = "NO RECIPE"; _lblStatus.ForeColor = PopTheme.TextDim;
            _lblPressureV.Text = "—"; _lblPressureS.Text = "Tap LOAD WO RECIPE to apply MD_Recipe defaults.";
            _lblTempV.Text     = "—"; _lblTempS.Text     = "";
            _lblHoldV.Text     = "—"; _lblHoldS.Text     = "";
            return;
        }
        _lblRecipe.Text = $"{_setup.RecipeId ?? "—"}   ·   WO {_setup.WoId?.ToString() ?? "—"}";
        _lblStatus.Text = _setup.Status ?? "APPLIED";
        _lblStatus.ForeColor = PopTheme.TextOk;
        _lblPressureV.Text = $"{_setup.PressureSp:0.00}";
        _lblPressureS.Text = "Setpoint  ·  tol ±0.30 bar";
        _lblTempV.Text     = $"{_setup.TempSp:0}";
        _lblTempS.Text     = "Setpoint  ·  tol ±5 °C";
        _lblHoldV.Text     = $"{_setup.HoldSecSp}";
        _lblHoldS.Text     = "Setpoint  ·  tol ±10 s";
    }

    private void LoadRecipe()
    {
        if (_wo is null)
        {
            MessageBox.Show(this, "No WO bound to this terminal.", "Load recipe",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        try
        {
            // Hard-coded standard for the demo. Real impl pulls ParamsJSON from MD_Recipe.
            PopServices.Bond.LoadRecipe(_session.LineId, _wo.WoId,
                _wo.RecipeId ?? "RCP-DEFAULT",
                pressure: 4.2m, temp: 90m, holdSec: 120, tension: 20m,
                _session.OperatorId, _session.EmployeeNo);
            LoadAndPaint();
            MessageBox.Show(this, "Recipe applied. IMG-03 production unlocked (if fabric mounted).",
                "Applied", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Load failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ── helpers ─────────────────────────────────────────────────────────
    private static Panel NewCardPadded(int padding) => new()
    {
        Dock = DockStyle.Fill, BackColor = PopTheme.BgCard,
        Padding = new Padding(padding), Margin = new Padding(0),
    };

    private static Label BuildParam(string caption, string value, string unit,
                                    out Panel container, out Label sub)
    {
        container = new Panel
        {
            Dock = DockStyle.Fill, BackColor = PopTheme.BgCard,
            Padding = new Padding(18), Margin = new Padding(6),
        };
        var stack = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = PopTheme.BgCard,
        };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));    // caption
        stack.RowStyles.Add(new RowStyle(SizeType.Percent, 100));// value+unit
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));    // sub
        stack.Controls.Add(new Label
        {
            Text = $"▼ {caption.ToUpperInvariant()}",
            Font = PopTheme.MonoBold, ForeColor = PopTheme.AccentSoft,
            AutoSize = true, Margin = new Padding(2, 0, 0, 6),
        }, 0, 0);
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = PopTheme.BgCard,
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var v = new Label
        {
            Text = value, Font = new Font("Segoe UI", 64f, FontStyle.Bold),
            ForeColor = PopTheme.TextOk, AutoSize = false, Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
        };
        var u = new Label
        {
            Text = unit, Font = new Font("Segoe UI", 18f), ForeColor = PopTheme.AccentSoft,
            AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 28, 0, 0),
        };
        row.Controls.Add(v, 0, 0);
        row.Controls.Add(u, 1, 0);
        stack.Controls.Add(row, 0, 1);
        sub = new Label
        {
            Text = "", Font = PopTheme.Mono, ForeColor = PopTheme.TextDim,
            AutoSize = true, Margin = new Padding(2, 6, 0, 0),
        };
        stack.Controls.Add(sub, 0, 2);
        container.Controls.Add(stack);
        return v;
    }
}
