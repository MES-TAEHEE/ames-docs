using AMES.Contracts.Dto;
using AMES.Pop.Common;

namespace AMES.Pop.Forms;

/// <summary>
/// INJ-07 Production Status — supervisor / shift-handover summary.
///
/// The L3 spec targets a Web/Office PC (light theme); this WinForms version
/// is the read-only POP-side fallback so supervisors can sanity-check at the
/// terminal before the Blazor portal exists.
/// </summary>
public sealed class Inj07ProdStatusForm : PopForm
{
    private readonly PopSessionDto _session;
    private readonly DataGridView  _grid;
    private readonly Label         _lblKpiGood, _lblKpiDef, _lblKpiRate, _lblKpiLines;
    private readonly HourlyBarChart _chart;
    private readonly System.Windows.Forms.Timer _timer;

    public Inj07ProdStatusForm(PopSessionDto session)
    {
        _session = session;
        Text = "A-MES POP · INJ-07 Production Status";

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, BackColor = PopTheme.BgOuter,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 170));   // KPI tiles
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 280));   // hourly chart
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));    // lines grid
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 130));
        root.Controls.Add(PopShell.BuildTopBar("INJ-07 · Prod Status (Supervisor)", session), 0, 0);

        // KPI row (4 tiles)
        var kpi = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1,
            BackColor = PopTheme.BgOuter, Padding = new Padding(20, 12, 20, 6),
        };
        for (var i = 0; i < 4; i++) kpi.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        kpi.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _lblKpiGood  = KpiTile(kpi, 0, "Today Good",   "0",   PopTheme.TextOk);
        _lblKpiDef   = KpiTile(kpi, 1, "Today Defect", "0",   PopTheme.AccentSoft);
        _lblKpiRate  = KpiTile(kpi, 2, "Defect %",     "0.0%", PopTheme.TextOk);
        _lblKpiLines = KpiTile(kpi, 3, "INJ Lines",    "0",   PopTheme.Accent);
        root.Controls.Add(kpi, 0, 1);

        // Hourly chart card
        var chartCard = new Panel { Dock = DockStyle.Fill, BackColor = PopTheme.BgCard, Padding = new Padding(18), Margin = new Padding(20, 6, 20, 6) };
        var chartStack = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Color.Transparent };
        chartStack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        chartStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        chartStack.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        chartStack.Controls.Add(PopShell.SectionHeader("▼ HOURLY OUTPUT  ·  current line: " + session.LineId), 0, 0);
        _chart = new HourlyBarChart { Dock = DockStyle.Fill };
        chartStack.Controls.Add(_chart, 0, 1);
        chartCard.Controls.Add(chartStack);
        root.Controls.Add(chartCard, 0, 2);

        // Lines grid
        var gridCard = new Panel { Dock = DockStyle.Fill, BackColor = PopTheme.BgCard, Padding = new Padding(12), Margin = new Padding(20, 6, 20, 6) };
        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            BackgroundColor = PopTheme.BgCard,
            BorderStyle = BorderStyle.None,
            GridColor = PopTheme.Border,
            AllowUserToAddRows = false, AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false, RowHeadersVisible = false,
            ReadOnly = true,
            EnableHeadersVisualStyles = false,
            ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Color.FromArgb(20, 32, 56),
                ForeColor = PopTheme.Accent,
                Font = PopTheme.MonoBold,
                Alignment = DataGridViewContentAlignment.MiddleLeft,
            },
            DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = PopTheme.BgCard,
                ForeColor = PopTheme.TextWhite,
                SelectionBackColor = Color.FromArgb(28, 42, 68),
                SelectionForeColor = PopTheme.TextWhite,
                Font = new Font("Segoe UI", 14f),
            },
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            ColumnHeadersHeight = 50,
            RowTemplate = { Height = 44 },
            ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single,
        };
        _grid.Columns.Add("LineId",      "Line");
        _grid.Columns.Add("LineName",    "Name");
        _grid.Columns.Add("EquipStatus", "Status");
        _grid.Columns.Add("TodayGood",   "Good (today)");
        _grid.Columns.Add("TodayDefect", "Defect (today)");
        _grid.Columns.Add("Rate",        "Defect %");
        gridCard.Controls.Add(_grid);
        root.Controls.Add(gridCard, 0, 3);

        // nav
        var nav = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = PopTheme.BgCard, Padding = new Padding(16, 10, 16, 10) };
        nav.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        nav.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
        nav.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        nav.Controls.Add(PopShell.BigButton("← Back",    PopTheme.BgKey, Color.White, (_, _) => Close()), 0, 0);
        nav.Controls.Add(PopShell.BigButton("⟳ Refresh", PopTheme.BgKey, Color.White, (_, _) => Reload()), 1, 0);
        root.Controls.Add(nav, 0, 4);

        Controls.Add(root);

        _timer = new System.Windows.Forms.Timer { Interval = 10_000 };
        _timer.Tick += (_, _) => Reload();
        _timer.Start();
        Reload();
    }

    private static Label KpiTile(TableLayoutPanel parent, int col, string caption, string value, Color valueColor)
    {
        var card = new Panel { Dock = DockStyle.Fill, BackColor = PopTheme.BgCard, Padding = new Padding(14), Margin = new Padding(6) };
        var stack = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Color.Transparent };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        stack.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        stack.Controls.Add(new Label
        {
            Text = caption.ToUpperInvariant(), Font = PopTheme.MonoBold,
            ForeColor = PopTheme.AccentSoft, AutoSize = true, Margin = new Padding(2, 0, 0, 4),
        }, 0, 0);
        var v = new Label
        {
            Text = value, Font = new Font("Segoe UI", 40f, FontStyle.Bold),
            ForeColor = valueColor, AutoSize = false, AutoEllipsis = true,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
        };
        stack.Controls.Add(v, 0, 1);
        card.Controls.Add(stack);
        parent.Controls.Add(card, col, 0);
        return v;
    }

    private void Reload()
    {
        try
        {
            var rows = PopServices.Dashboard.GetAllInjectionLines();
            _grid.Rows.Clear();
            int totalGood = 0, totalDef = 0;
            foreach (var r in rows)
            {
                _grid.Rows.Add(r.LineId, r.LineName, r.EquipStatus ?? "—",
                               r.TodayGood, r.TodayDefect, $"{r.DefectRatePct:0.0}%");
                totalGood += r.TodayGood; totalDef += r.TodayDefect;
            }
            _lblKpiGood.Text  = totalGood.ToString("N0");
            _lblKpiDef.Text   = totalDef.ToString("N0");
            var rate = totalGood + totalDef == 0 ? 0 : totalDef * 100.0 / (totalGood + totalDef);
            _lblKpiRate.Text  = $"{rate:0.0}%";
            _lblKpiRate.ForeColor = rate >= 5 ? PopTheme.TextFail
                                  : rate >= 4 ? PopTheme.AccentSoft
                                  : PopTheme.TextOk;
            _lblKpiLines.Text = rows.Count.ToString();
            _chart.SetData(PopServices.Production.GetHourlyToday(_session.LineId));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Inj07 reload] {ex.Message}");
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _timer.Dispose();
        base.Dispose(disposing);
    }
}
