using AMES.Contracts.Dto;
using AMES.Pop.Common;

namespace AMES.Pop.Forms;

/// <summary>
/// IMG-03 Production Entry — same shape as INJ-04 but Confirm also deducts
/// fabric metres from the mounted roll and writes a PR_FabricDeductionLog +
/// PR_BondCycleLog row. Confirm is blocked unless a verified roll is mounted
/// (BR-IMG-01).
/// </summary>
public sealed class Img03ProductionEntryForm : PopForm
{
    /// <summary>Fabric metres consumed per produced unit. TODO: pull from MD_Bom.</summary>
    private const decimal MetresPerUnit = 0.25m;

    private readonly PopSessionDto _session;
    private readonly Label _lblWoNo, _lblItem, _lblProgress, _lblTodayGood, _lblTodayDef;
    private readonly Label _lblCt, _lblFabric, _lblBond, _lblLastEntry;
    private readonly ProgressBar _bar;
    private readonly Label _lblInput;

    private WorkOrderDto?   _wo;
    private FabricRollDto?  _roll;
    private BondSetupDto?   _bond;
    private int?            _recipeCT;
    private readonly System.Text.StringBuilder _buf = new();
    private DateTime        _lastEntryAt = DateTime.MinValue;
    private int             _lastResultId;

    public Img03ProductionEntryForm(PopSessionDto session)
    {
        _session = session;
        Text = "A-MES POP · IMG-03 Production Entry";

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = PopTheme.BgOuter,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 104));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 130));
        root.Controls.Add(PopShell.BuildTopBar("IMG-03 · Production Entry", session), 0, 0);

        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            BackColor = PopTheme.BgOuter, Padding = new Padding(20, 14, 20, 6),
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var leftCard = NewCard();
        var leftStack = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 10, BackColor = PopTheme.BgCard,
        };
        leftStack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 10; i++) leftStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        leftStack.Controls.Add(PopShell.SectionHeader("▼ CURRENT WO"), 0, 0);
        _lblWoNo = NewBig("(loading)", 28f, PopTheme.Accent);
        _lblWoNo.AutoEllipsis = true; _lblWoNo.MaximumSize = new Size(600, 0);
        leftStack.Controls.Add(_lblWoNo, 0, 1);
        _lblItem = new Label
        {
            Text = "", Font = PopTheme.BodyBold, ForeColor = PopTheme.TextDim,
            AutoSize = true, Margin = new Padding(4, 0, 0, 16),
        };
        leftStack.Controls.Add(_lblItem, 0, 2);
        leftStack.Controls.Add(PopShell.SectionHeader("▼ PROGRESS"), 0, 3);
        _lblProgress = NewBig("0 / 0", 44f, PopTheme.TextOk);
        leftStack.Controls.Add(_lblProgress, 0, 4);
        _bar = new ProgressBar
        {
            Dock = DockStyle.Top, Height = 14, Margin = new Padding(4, 0, 16, 12),
            Style = ProgressBarStyle.Continuous, Minimum = 0, Maximum = 100, Value = 0,
        };
        leftStack.Controls.Add(_bar, 0, 5);
        leftStack.Controls.Add(PopShell.SectionHeader("▼ STATS"), 0, 6);
        var statsGrid = new TableLayoutPanel
        {
            Dock = DockStyle.Top, ColumnCount = 2, RowCount = 3, AutoSize = true,
            BackColor = PopTheme.BgCard, Margin = new Padding(4, 0, 16, 0),
        };
        statsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        statsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        for (var i = 0; i < 3; i++) statsGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _lblTodayGood = StatCell("Today Good",   "0",   PopTheme.TextOk);
        _lblTodayDef  = StatCell("Today Defect", "0",   PopTheme.TextFail);
        _lblCt        = StatCell("CT (recipe)",  "—",   PopTheme.AccentSoft);
        _lblFabric    = StatCell("Fabric Roll",  "—",   PopTheme.AccentSoft);
        _lblBond      = StatCell("Bond Recipe",  "—",   PopTheme.AccentSoft);
        _lblLastEntry = StatCell("Last Entry",   "—",   PopTheme.TextDim);
        statsGrid.Controls.Add(_lblTodayGood.Parent!, 0, 0);
        statsGrid.Controls.Add(_lblTodayDef.Parent!,  1, 0);
        statsGrid.Controls.Add(_lblCt.Parent!,        0, 1);
        statsGrid.Controls.Add(_lblFabric.Parent!,    1, 1);
        statsGrid.Controls.Add(_lblBond.Parent!,      0, 2);
        statsGrid.Controls.Add(_lblLastEntry.Parent!, 1, 2);
        leftStack.Controls.Add(statsGrid, 0, 7);
        leftCard.Controls.Add(leftStack);
        body.Controls.Add(leftCard, 0, 0);

        var rightCard = NewCard();
        var rightStack = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = PopTheme.BgCard,
        };
        rightStack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        rightStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rightStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 210));
        rightStack.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        rightStack.Controls.Add(PopShell.SectionHeader("▼ GOOD QTY THIS CYCLE  ·  EA"), 0, 0);
        _lblInput = new Label
        {
            Text = "0", Font = PopTheme.DisplayMega,
            ForeColor = PopTheme.Accent, BackColor = Color.Black, BorderStyle = BorderStyle.FixedSingle,
            TextAlign = ContentAlignment.MiddleCenter, Dock = DockStyle.Fill,
            Margin = new Padding(4, 0, 4, 0), AutoSize = false,
        };
        rightStack.Controls.Add(_lblInput, 0, 1);
        rightStack.Controls.Add(BuildKeypad(), 0, 2);
        rightCard.Controls.Add(rightStack);
        body.Controls.Add(rightCard, 1, 0);

        root.Controls.Add(body, 0, 1);

        var nav = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1,
            BackColor = PopTheme.BgCard, Padding = new Padding(16, 10, 16, 10),
        };
        nav.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        nav.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        nav.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        nav.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        nav.Controls.Add(PopShell.BigButton("← Back",                  PopTheme.BgKey, Color.White, (_, _) => Close()), 0, 0);
        nav.Controls.Add(PopShell.BigButton("❌ Defect → IMG-05",       Color.FromArgb(180, 70, 30), Color.White, (_, _) => OpenDefect()), 1, 0);
        nav.Controls.Add(PopShell.BigButton("✓ Confirm + Deduct Fabric", PopTheme.BgKeyOk, Color.White, (_, _) => Confirm()), 2, 0);
        root.Controls.Add(nav, 0, 2);

        Controls.Add(root);

        KeyPreview = true;
        KeyPress += (_, e) => { if (char.IsDigit(e.KeyChar)) AppendDigit(e.KeyChar); };
        KeyDown  += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) Confirm();
            if (e.KeyCode == Keys.Back)  Backspace();
        };

        ReloadContext();
    }

    private TableLayoutPanel BuildKeypad()
    {
        var pad = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 4,
            BackColor = PopTheme.BgCard, Margin = new Padding(0, 8, 0, 0),
        };
        for (var i = 0; i < 3; i++) pad.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34f));
        for (var i = 0; i < 4; i++) pad.RowStyles   .Add(new RowStyle   (SizeType.Percent, 25f));

        var keys = new (string Text, Action Handler, Color Bg)[]
        {
            ("1", () => AppendDigit('1'), PopTheme.BgKey),
            ("2", () => AppendDigit('2'), PopTheme.BgKey),
            ("3", () => AppendDigit('3'), PopTheme.BgKey),
            ("4", () => AppendDigit('4'), PopTheme.BgKey),
            ("5", () => AppendDigit('5'), PopTheme.BgKey),
            ("6", () => AppendDigit('6'), PopTheme.BgKey),
            ("7", () => AppendDigit('7'), PopTheme.BgKey),
            ("8", () => AppendDigit('8'), PopTheme.BgKey),
            ("9", () => AppendDigit('9'), PopTheme.BgKey),
            ("CT", LoadCT, Color.FromArgb(170, 120, 20)),
            ("0", () => AppendDigit('0'), PopTheme.BgKey),
            ("DEL", Backspace, PopTheme.BgKeyDel),
        };
        foreach (var (text, h, bg) in keys)
        {
            var b = new Button
            {
                Text = text, Font = PopTheme.KeyDigit, ForeColor = Color.White, BackColor = bg,
                FlatStyle = FlatStyle.Flat, Dock = DockStyle.Fill,
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

    private void ReloadContext()
    {
        _wo   = PopServices.WorkOrders.GetActiveForTerminal(_session.LineId, _session.TerminalId)
             ?? PopServices.WorkOrders.ListForLine(_session.LineId).FirstOrDefault();
        _roll = PopServices.Fabric.GetMountedRoll(_session.LineId);
        _bond = PopServices.Bond.GetActiveForLine(_session.LineId);

        if (_wo is null)
        {
            _lblWoNo.Text = "(no WO)";
            _lblItem.Text = "Open IMG-02 / WO Confirm first.";
            _lblProgress.Text = "—"; _bar.Value = 0; return;
        }
        _lblWoNo.Text = _wo.WoNumber;
        _lblItem.Text = $"{_wo.ItemName}  ·  {_wo.ItemNo}";
        _lblProgress.Text = $"{_wo.CompletedQty:0}  /  {_wo.OrderQty:0}";
        _bar.Value = Math.Clamp((int)_wo.ProgressPct, 0, 100);

        if (!string.IsNullOrEmpty(_wo.RecipeId))
        {
            _recipeCT = PopServices.Master.GetRecipeCycleTime(_wo.RecipeId);
            SetStat(_lblCt, _recipeCT is null ? "—" : $"{_recipeCT}s  · [CT]=1 EA", PopTheme.AccentSoft);
        }

        SetStat(_lblTodayGood, PopServices.Production.GetTodayGoodForWo(_wo.WoId).ToString("N0"), PopTheme.TextOk);
        var (good, defect) = PopServices.Defects.GetWoTotals(_wo.WoId);
        SetStat(_lblTodayDef, $"{defect:N0}  ({(good + defect == 0 ? 0 : defect * 100.0 / (good + defect)):0.0}%)", PopTheme.TextFail);

        if (_roll is null)
            SetStat(_lblFabric, "(no roll — IMG-04)", PopTheme.TextFail);
        else
            SetStat(_lblFabric,
                $"{_roll.LotCode}  ·  {_roll.ColorCode}  ·  {_roll.RemainingM:0.0}m",
                _roll.RemainingM <= 5 ? PopTheme.TextFail
                : _roll.RemainingM <= 10 ? PopTheme.AccentSoft : PopTheme.TextOk);

        SetStat(_lblBond,
            _bond is null ? "(no recipe — IMG-06)"
                          : $"{_bond.RecipeId}  ·  {_bond.TempSp:0}°C / {_bond.PressureSp:0.0}bar / {_bond.HoldSecSp}s",
            _bond is null ? PopTheme.TextFail : PopTheme.AccentSoft);
    }

    private void AppendDigit(char d)
    {
        if (_buf.Length >= 2) return;
        if (_buf.Length == 0 && d == '0') return;
        _buf.Append(d);
        _lblInput.Text = _buf.ToString();
    }
    private void Backspace()
    {
        if (_buf.Length == 0) return;
        _buf.Length--;
        _lblInput.Text = _buf.Length == 0 ? "0" : _buf.ToString();
    }
    private void LoadCT() { _buf.Clear(); _buf.Append('1'); _lblInput.Text = "1"; }

    private void Confirm()
    {
        if (_wo is null)
        {
            MessageBox.Show(this, "No WO bound to this terminal.", "No WO", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (_roll is null)
        {
            MessageBox.Show(this, "No verified fabric roll mounted (BR-IMG-01).\nOpen IMG-04 first.",
                "Block", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (!int.TryParse(_buf.ToString(), out var qty) || qty <= 0)
        {
            MessageBox.Show(this, "Enter qty 1–99.", "Bad qty", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var requiredM = qty * MetresPerUnit;
        if (_roll.RemainingM < requiredM)
        {
            MessageBox.Show(this,
                $"Fabric roll only has {_roll.RemainingM:0.0}m, need {requiredM:0.0}m for {qty} EA.\nMount a fresh roll via IMG-04.",
                "Block — fabric short", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            var cycleSec = _recipeCT ?? 38;
            var (resultId, _, newCompleted) = PopServices.Production.RecordCycle(
                _wo.WoId, _wo.ItemNo, _wo.LineId, qty, cycleSec, moldId: null,
                _session.OperatorId, _session.SessionId, _session.EmployeeNo, defectFlag: false);

            // fabric deduction
            var newRemaining = PopServices.Fabric.DeductFromRoll(_roll.LotId, requiredM, resultId, _session.EmployeeNo);

            // bond cycle PLC log (mocked — uses setpoint values as the "average")
            if (_bond is not null)
            {
                PopServices.Bond.LogCycle(resultId, _bond.BondSetupId,
                    _bond.PressureSp, _bond.TempSp, _bond.HoldSecSp,
                    _bond.TensionSp, withinSpec: true, _session.EmployeeNo);
            }

            _lastResultId = resultId;
            _lastEntryAt  = DateTime.Now;
            SetStat(_lblLastEntry, _lastEntryAt.ToString("HH:mm:ss"), PopTheme.TextDim);
            if (newCompleted >= _wo.OrderQty)
                MessageBox.Show(this, $"WO {_wo.WoNumber} target reached and closed.",
                    "WO complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
            if (newRemaining <= 5)
                MessageBox.Show(this, $"Roll {_roll.LotCode} remaining {newRemaining:0.0}m — change soon (IMG-04).",
                    "Fabric low", MessageBoxButtons.OK, MessageBoxIcon.Warning);

            _buf.Clear();
            _lblInput.Text = "0";
            ReloadContext();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Confirm failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OpenDefect()
    {
        if (_wo is null || _lastResultId == 0)
        {
            MessageBox.Show(this, "Confirm at least one cycle first.",
                "No cycle", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using var f = new Img05DefectForm(_session, _wo, _lastResultId);
        f.ShowDialog(this);
        ReloadContext();
    }

    private static Panel NewCard() => new()
    {
        Dock = DockStyle.Fill, BackColor = PopTheme.BgCard, Padding = new Padding(18), Margin = new Padding(6),
    };
    private static Label NewBig(string text, float size, Color fore) => new()
    {
        Text = text, Font = new Font("Segoe UI", size, FontStyle.Bold),
        ForeColor = fore, AutoSize = true, Margin = new Padding(4, 0, 0, 8),
    };
    private static Label StatCell(string caption, string value, Color valueColor)
    {
        var wrap = new TableLayoutPanel
        {
            Dock = DockStyle.Top, ColumnCount = 1, RowCount = 2, AutoSize = true,
            BackColor = PopTheme.BgCard, Margin = new Padding(0, 2, 8, 6),
        };
        wrap.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        wrap.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        wrap.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        wrap.Controls.Add(new Label
        {
            Text = caption, Font = PopTheme.InfoCaption, ForeColor = PopTheme.AccentSoft,
            AutoSize = true, Margin = new Padding(0, 0, 0, 2),
        }, 0, 0);
        var v = new Label
        {
            Text = value, Font = PopTheme.InfoValue, ForeColor = valueColor,
            AutoSize = true, Margin = new Padding(0),
        };
        wrap.Controls.Add(v, 0, 1);
        return v;
    }
    private static void SetStat(Label v, string value, Color color) { v.Text = value; v.ForeColor = color; }
}
