using AMES.Contracts.Dto;
using AMES.Pop.Common;

namespace AMES.Pop.Forms;

/// <summary>
/// INJ-05 Defect entry — pick one of the MD_DefectCode codes for the
/// process, key in the qty, and persist. Live preview of resulting defect
/// rate; auto-fires Andon if rate would land at ≥ 5%.
///
/// Two constructors:
///   (PopSessionDto)                                  → standalone (uses active WO)
///   (PopSessionDto, WorkOrderDto, int resultId)      → invoked from INJ-04
/// </summary>
public sealed class Inj05DefectForm : Form
{
    private readonly PopSessionDto _session;
    private WorkOrderDto?          _wo;
    private int                    _resultId;
    private List<DefectCodeDto>    _codes = new();
    private DefectCodeDto?         _selected;

    private readonly FlowLayoutPanel _grid;
    private readonly Label _lblSelectedCode, _lblSelectedDetail;
    private readonly Label _lblRateNow, _lblRateAfter, _lblInput;
    private readonly System.Text.StringBuilder _buf = new();

    public Inj05DefectForm(PopSessionDto session) : this(session, null, 0) {}

    public Inj05DefectForm(PopSessionDto session, WorkOrderDto? wo, int resultId)
    {
        _session  = session;
        _wo       = wo;
        _resultId = resultId;

        Text            = "A-MES POP · INJ-05 Defect";
        ClientSize      = new Size(1180, 720);
        BackColor       = PopTheme.BgOuter;
        ForeColor       = PopTheme.TextWhite;
        Font            = PopTheme.Body;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        StartPosition   = FormStartPosition.CenterParent;
        MaximizeBox     = false;
        AutoScaleMode   = AutoScaleMode.Dpi;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = PopTheme.BgOuter,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));

        root.Controls.Add(PopShell.BuildTopBar("INJ-05 · Defect", session), 0, 0);

        // Rate banner
        var banner = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(35, 25, 10), Padding = new Padding(20, 12, 20, 12), Margin = new Padding(0) };
        var bGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, BackColor = Color.Transparent,
        };
        bGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70));
        bGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        bGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        bGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        bGrid.Controls.Add(new Label
        {
            Text = "▼ Current Defect Rate — WO cumulative (threshold 5%)",
            Font = PopTheme.MonoBold, ForeColor = PopTheme.AccentSoft, AutoSize = true,
        }, 0, 0);
        _lblRateNow = new Label
        {
            Text = "0.0%", Font = new Font("Segoe UI", 26f, FontStyle.Bold),
            ForeColor = PopTheme.TextOk, AutoSize = true, Anchor = AnchorStyles.Right,
        };
        bGrid.Controls.Add(_lblRateNow, 1, 0);
        bGrid.SetRowSpan(_lblRateNow, 2);
        _lblRateAfter = new Label
        {
            Text = "", Font = PopTheme.BodyBold, ForeColor = PopTheme.TextDim, AutoSize = true,
        };
        bGrid.Controls.Add(_lblRateAfter, 0, 1);
        banner.Controls.Add(bGrid);
        root.Controls.Add(banner, 0, 1);

        // Body: 6-card grid + input column
        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, BackColor = PopTheme.BgOuter,
            Padding = new Padding(20, 14, 20, 6),
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var gridCard = NewCard();
        var gStack = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Color.Transparent };
        gStack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        gStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        gStack.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        gStack.Controls.Add(PopShell.SectionHeader("▼ DEFECT TYPES (INJ)"), 0, 0);
        _grid = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, BackColor = PopTheme.BgCard, AutoScroll = true,
            FlowDirection = FlowDirection.LeftToRight, WrapContents = true, Padding = new Padding(4),
        };
        gStack.Controls.Add(_grid, 0, 1);
        gridCard.Controls.Add(gStack);
        body.Controls.Add(gridCard, 0, 0);

        var inputCard = NewCard();
        var iStack = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, BackColor = Color.Transparent };
        iStack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        iStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        iStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
        iStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        iStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
        iStack.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        iStack.Controls.Add(PopShell.SectionHeader("▼ SELECTED"), 0, 0);
        _lblSelectedCode = new Label
        {
            Text = "(pick a defect type)", Font = new Font("Segoe UI", 16f, FontStyle.Bold),
            ForeColor = PopTheme.Accent, AutoSize = false, Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleCenter,
        };
        iStack.Controls.Add(_lblSelectedCode, 0, 1);
        _lblSelectedDetail = new Label
        {
            Text = "", Font = PopTheme.BodySmall, ForeColor = PopTheme.TextDim,
            AutoSize = false, Dock = DockStyle.Fill, Height = 50, TextAlign = ContentAlignment.MiddleCenter,
        };
        iStack.Controls.Add(_lblSelectedDetail, 0, 2);
        _lblInput = new Label
        {
            Text = "0", Font = new Font("Segoe UI", 48f, FontStyle.Bold),
            ForeColor = PopTheme.Accent, BackColor = Color.Black, BorderStyle = BorderStyle.FixedSingle,
            TextAlign = ContentAlignment.MiddleCenter, Dock = DockStyle.Fill, Margin = new Padding(4, 4, 4, 8),
        };
        iStack.Controls.Add(_lblInput, 0, 3);
        iStack.Controls.Add(BuildKeypad(), 0, 4);
        inputCard.Controls.Add(iStack);
        body.Controls.Add(inputCard, 1, 0);
        root.Controls.Add(body, 0, 2);

        // Bottom nav
        var nav = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            BackColor = PopTheme.BgCard, Padding = new Padding(16, 10, 16, 10),
        };
        nav.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        nav.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        nav.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        nav.Controls.Add(PopShell.BigButton("← Back",                   PopTheme.BgKey, Color.White, (_, _) => Close()), 0, 0);
        nav.Controls.Add(PopShell.BigButton("✓ Confirm Defect",         PopTheme.BgKeyOk, Color.White, (_, _) => Confirm()), 1, 0);
        root.Controls.Add(nav, 0, 3);

        Controls.Add(root);
        KeyPreview = true;
        KeyPress += (_, e) => { if (char.IsDigit(e.KeyChar)) Append(e.KeyChar); };

        Reload();
    }

    private void Reload()
    {
        _codes = PopServices.Defects.ListForProcess("INJ");
        _grid.Controls.Clear();
        foreach (var c in _codes)
        {
            var btn = new Button
            {
                Text      = $"{c.DefectCode}\n{c.DefectNameEn}\n{c.DefectName}",
                Font      = new Font("Segoe UI", 11f, FontStyle.Bold),
                ForeColor = Color.White,
                BackColor = PopTheme.BgKey,
                FlatStyle = FlatStyle.Flat,
                Width     = 200,
                Height    = 110,
                Margin    = new Padding(6),
                Cursor    = Cursors.Hand,
                Tag       = c,
                TextAlign = ContentAlignment.MiddleCenter,
            };
            btn.FlatAppearance.BorderColor        = PopTheme.Border;
            btn.FlatAppearance.BorderSize         = 1;
            btn.FlatAppearance.MouseOverBackColor = ControlPaint.Light(PopTheme.BgKey, 0.2f);
            btn.Click += (s, _) =>
            {
                _selected = (DefectCodeDto)((Button)s!).Tag!;
                foreach (Button b in _grid.Controls) b.BackColor = b.Tag == _selected ? PopTheme.AccentDeep : PopTheme.BgKey;
                _lblSelectedCode.Text   = $"{_selected.DefectCode}  ·  {_selected.DefectNameEn}";
                _lblSelectedDetail.Text = $"{_selected.DefectName}    Severity={_selected.SeverityLevel}";
                UpdateLivePreview();
            };
            _grid.Controls.Add(btn);
        }

        if (_wo is null)
            _wo = PopServices.WorkOrders.GetActiveForTerminal(_session.LineId, _session.TerminalId)
               ?? PopServices.WorkOrders.ListForLine(_session.LineId).FirstOrDefault();
        UpdateLivePreview();
    }

    private TableLayoutPanel BuildKeypad()
    {
        var pad = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 4,
            BackColor = Color.Transparent, Margin = new Padding(0, 8, 0, 0),
        };
        for (var i = 0; i < 3; i++) pad.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34f));
        for (var i = 0; i < 4; i++) pad.RowStyles   .Add(new RowStyle   (SizeType.Percent, 25f));

        void AddKey(string text, Action handler, Color bg)
        {
            var b = new Button
            {
                Text = text, Font = new Font("Segoe UI", 18f, FontStyle.Bold),
                ForeColor = Color.White, BackColor = bg, FlatStyle = FlatStyle.Flat,
                Dock = DockStyle.Fill, Margin = new Padding(4), Cursor = Cursors.Hand, TabStop = false,
            };
            b.FlatAppearance.BorderColor = PopTheme.Border;
            b.FlatAppearance.BorderSize  = 1;
            b.FlatAppearance.MouseOverBackColor = ControlPaint.Light(bg, 0.15f);
            b.Click += (_, _) => handler();
            pad.Controls.Add(b);
        }

        for (var i = 1; i <= 9; i++) { var d = (char)('0' + i); AddKey(d.ToString(), () => Append(d), PopTheme.BgKey); }
        AddKey("DEL", Back, PopTheme.BgKeyDel);
        AddKey("0",   () => Append('0'), PopTheme.BgKey);
        AddKey("OK",  Confirm, PopTheme.BgKeyOk);
        return pad;
    }

    private void Append(char d)
    {
        if (_buf.Length >= 2) return;
        if (_buf.Length == 0 && d == '0') return;
        _buf.Append(d);
        _lblInput.Text = _buf.ToString();
        UpdateLivePreview();
    }

    private void Back()
    {
        if (_buf.Length == 0) return;
        _buf.Length--;
        _lblInput.Text = _buf.Length == 0 ? "0" : _buf.ToString();
        UpdateLivePreview();
    }

    private void UpdateLivePreview()
    {
        if (_wo is null) { _lblRateNow.Text = "—"; return; }
        var (good, defect) = PopServices.Defects.GetWoTotals(_wo.WoId);
        var nowPct = good + defect == 0 ? 0 : defect * 100.0 / (good + defect);
        _lblRateNow.Text      = $"{nowPct:0.0}%";
        _lblRateNow.ForeColor = nowPct >= 5 ? PopTheme.TextFail
                              : nowPct >= 4 ? PopTheme.AccentSoft
                              : PopTheme.TextOk;

        if (int.TryParse(_buf.ToString(), out var add) && add > 0)
        {
            var afterPct = good + defect + add == 0 ? 0
                          : (defect + add) * 100.0 / (good + defect + add);
            _lblRateAfter.Text = $"After confirm: {afterPct:0.0}%   (delta +{afterPct - nowPct:0.0})";
            _lblRateAfter.ForeColor = afterPct >= 5 ? PopTheme.TextFail : PopTheme.AccentSoft;
        }
        else _lblRateAfter.Text = "";
    }

    private void Confirm()
    {
        if (_wo is null) { MessageBox.Show(this, "No WO context.", "Defect", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        if (_selected is null) { MessageBox.Show(this, "Pick a defect type.", "Defect", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
        if (!int.TryParse(_buf.ToString(), out var qty) || qty <= 0)
        { MessageBox.Show(this, "Enter qty 1–99.", "Defect", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }

        try
        {
            PopServices.Defects.RecordDefect(
                _resultId, _wo.WoId, lotId: null, _selected.DefectCode, qty,
                _session.OperatorId, _session.EmployeeNo);

            var (g, d) = PopServices.Defects.GetWoTotals(_wo.WoId);
            var pct    = g + d == 0 ? 0 : d * 100.0 / (g + d);
            if (pct >= 5)
            {
                MessageBox.Show(this, $"Defect rate {pct:0.0}% ≥ 5% — Andon triggered (BR-INJ-03).",
                    "Andon auto-fire", MessageBoxButtons.OK, MessageBoxIcon.Stop);
                PopServices.Andon.Raise(_session.LineId, equipId: null,
                    triggerSource: "INJ-05", ruleId: "BR-INJ-03", severity: "HIGH", _session.EmployeeNo);
            }

            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Defect failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static Panel NewCard() => new()
    {
        Dock = DockStyle.Fill, BackColor = PopTheme.BgCard, Padding = new Padding(18), Margin = new Padding(6),
    };
}
