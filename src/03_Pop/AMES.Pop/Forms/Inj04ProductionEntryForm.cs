using AMES.Contracts.Dto;
using AMES.Pop.Common;

namespace AMES.Pop.Forms;

/// <summary>
/// INJ-04 Production Entry — the most-used POP screen.
/// Operator enters good-qty per cycle via a large keypad. [CT] = recipe std.
/// On Confirm: insert PR_ProductionResult + tbl_Lot, bump WO CompletedQty,
/// +1 mold shot count, close WO when target met.
/// Defect button opens INJ-05 passing the ResultID for traceability.
/// </summary>
public sealed class Inj04ProductionEntryForm : PopForm
{
    private readonly PopSessionDto _session;

    // left panel widgets
    private readonly Label _lblWoNo, _lblItem, _lblProgress, _lblTodayGood, _lblTodayDef;
    private readonly Label _lblCt, _lblMold, _lblLastEntry;
    private readonly ProgressBar _bar;

    // right panel widgets
    private readonly Label _lblInput;

    // state
    private WorkOrderDto?  _wo;
    private MoldDto?       _mold;
    private int?           _recipeCT;
    private readonly System.Text.StringBuilder _buf = new();
    private DateTime       _lastEntryAt = DateTime.MinValue;
    private int            _lastResultId;

    public Inj04ProductionEntryForm(PopSessionDto session)
    {
        _session = session;
        Text = "A-MES POP · INJ-04 Production Entry";

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
            BackColor = PopTheme.BgOuter,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 130));
        root.Controls.Add(PopShell.BuildTopBar("INJ-04 · Production Entry", session), 0, 0);

        // body 2-column: 50% info | 50% input keypad
        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            BackColor = PopTheme.BgOuter, Padding = new Padding(20, 14, 20, 6),
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        // ── left: WO info ──────────────────────────────────────────────────
        var leftCard = NewCard();
        var leftStack = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8, BackColor = Color.Transparent };
        leftStack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 8; i++) leftStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        leftStack.Controls.Add(PopShell.SectionHeader("▼ CURRENT WO"), 0, 0);
        _lblWoNo = NewBig("(loading)", 34f, PopTheme.Accent);
        leftStack.Controls.Add(_lblWoNo, 0, 1);
        _lblItem = new Label
        {
            Text = "", Font = PopTheme.BodyBold, ForeColor = PopTheme.TextDim,
            AutoSize = true, Margin = new Padding(4, 0, 0, 16),
        };
        leftStack.Controls.Add(_lblItem, 0, 2);
        leftStack.Controls.Add(PopShell.SectionHeader("▼ PROGRESS"), 0, 3);
        _lblProgress = NewBig("0 / 0", 52f, PopTheme.TextOk);
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
            BackColor = Color.Transparent, Margin = new Padding(4, 0, 16, 0),
        };
        statsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        statsGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        for (var i = 0; i < 3; i++) statsGrid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _lblTodayGood = StatCell("Today Good",     "0",   PopTheme.TextOk);
        _lblTodayDef  = StatCell("Today Defect",   "0",   PopTheme.TextFail);
        _lblCt        = StatCell("CT (recipe)",    "—",   PopTheme.AccentSoft);
        _lblMold      = StatCell("Mold Shots",     "—",   PopTheme.AccentSoft);
        _lblLastEntry = StatCell("Last Entry",     "—",   PopTheme.TextDim);
        var blank     = StatCell("",               "",    PopTheme.TextDim);
        statsGrid.Controls.Add(_lblTodayGood.Parent!, 0, 0);
        statsGrid.Controls.Add(_lblTodayDef.Parent!,  1, 0);
        statsGrid.Controls.Add(_lblCt.Parent!,        0, 1);
        statsGrid.Controls.Add(_lblMold.Parent!,      1, 1);
        statsGrid.Controls.Add(_lblLastEntry.Parent!, 0, 2);
        statsGrid.Controls.Add(blank.Parent!,         1, 2);
        leftStack.Controls.Add(statsGrid, 0, 7);
        leftCard.Controls.Add(leftStack);
        body.Controls.Add(leftCard, 0, 0);

        // ── right: numpad ──────────────────────────────────────────────────
        var rightCard = NewCard();
        var rightStack = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Color.Transparent };
        rightStack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        rightStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rightStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 170));
        rightStack.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        rightStack.Controls.Add(PopShell.SectionHeader("▼ GOOD QTY THIS CYCLE  ·  EA"), 0, 0);
        _lblInput = new Label
        {
            Text = "0", Font = new Font("Segoe UI", 96f, FontStyle.Bold),
            ForeColor = PopTheme.Accent, BackColor = Color.Black, BorderStyle = BorderStyle.FixedSingle,
            TextAlign = ContentAlignment.MiddleCenter, Dock = DockStyle.Fill,
            Margin = new Padding(4, 0, 4, 0),
        };
        rightStack.Controls.Add(_lblInput, 0, 1);
        rightStack.Controls.Add(BuildKeypad(), 0, 2);
        rightCard.Controls.Add(rightStack);
        body.Controls.Add(rightCard, 1, 0);

        root.Controls.Add(body, 0, 1);

        // ── bottom: nav ────────────────────────────────────────────────────
        var nav = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1,
            BackColor = PopTheme.BgCard, Padding = new Padding(16, 10, 16, 10),
        };
        nav.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        nav.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        nav.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        nav.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        nav.Controls.Add(PopShell.BigButton("← Back",                    PopTheme.BgKey, Color.White, (_, _) => Close()), 0, 0);
        nav.Controls.Add(PopShell.BigButton("❌ Defect → INJ-05",         Color.FromArgb(180, 70, 30), Color.White, (_, _) => OpenDefect()), 1, 0);
        nav.Controls.Add(PopShell.BigButton("✓ Confirm Good Qty",         PopTheme.BgKeyOk, Color.White, (_, _) => Confirm()), 2, 0);
        root.Controls.Add(nav, 0, 2);

        Controls.Add(root);

        KeyPreview = true;
        KeyPress += (_, e) => { if (char.IsDigit(e.KeyChar)) AppendDigit(e.KeyChar); };
        KeyDown  += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) Confirm();
            if (e.KeyCode == Keys.Back)  Backspace();
            if (e.KeyCode == Keys.Escape) Close();
        };

        ReloadContext();
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
                Text = text, Font = PopTheme.KeyDigit,
                ForeColor = Color.White, BackColor = bg, FlatStyle = FlatStyle.Flat,
                Dock = DockStyle.Fill, Margin = new Padding(6), Cursor = Cursors.Hand, TabStop = false,
            };
            b.FlatAppearance.BorderColor = PopTheme.Border;
            b.FlatAppearance.BorderSize  = 1;
            b.FlatAppearance.MouseOverBackColor = ControlPaint.Light(bg, 0.15f);
            b.Click += (_, _) => h();
            pad.Controls.Add(b);
        }
        return pad;
    }

    // ── data loading / refresh ─────────────────────────────────────────────
    private void ReloadContext()
    {
        _wo = PopServices.WorkOrders.GetActiveForTerminal(_session.LineId, _session.TerminalId);
        if (_wo is null)
        {
            // Show whatever's available so the screen isn't empty
            _wo = PopServices.WorkOrders.ListForLine(_session.LineId).FirstOrDefault();
        }
        if (_wo is null)
        {
            _lblWoNo.Text = "(no WO)";
            _lblItem.Text = "Open INJ-03 to accept a WO first.";
            _lblProgress.Text = "—";
            _bar.Value = 0;
            return;
        }
        _lblWoNo.Text = _wo.WoNumber;
        _lblItem.Text = $"{_wo.ItemName}  ·  {_wo.ItemNo}";
        _lblProgress.Text = $"{_wo.CompletedQty:0}  /  {_wo.OrderQty:0}";
        _bar.Value = Math.Clamp((int)_wo.ProgressPct, 0, 100);

        if (!string.IsNullOrEmpty(_wo.MoldId))
        {
            _mold = PopServices.Molds.GetById(_wo.MoldId);
            if (_mold is not null)
                SetStat(_lblMold, $"{_mold.CurrentShots:N0} / {_mold.RatedShots:N0}  ({_mold.LifePct:0}%)",
                        _mold.Severity is "warn" or "alert" or "hard" ? PopTheme.TextFail : PopTheme.AccentSoft);
        }

        if (!string.IsNullOrEmpty(_wo.RecipeId))
        {
            _recipeCT = PopServices.Master.GetRecipeCycleTime(_wo.RecipeId);
            SetStat(_lblCt, _recipeCT is null ? "—" : $"{_recipeCT}s  · [CT] key = 1 EA", PopTheme.AccentSoft);
        }

        SetStat(_lblTodayGood, PopServices.Production.GetTodayGoodForWo(_wo.WoId).ToString("N0"), PopTheme.TextOk);
        var (good, defect) = PopServices.Defects.GetWoTotals(_wo.WoId);
        SetStat(_lblTodayDef, $"{defect:N0}  ({(good + defect == 0 ? 0 : defect * 100.0 / (good + defect)):0.0}%)", PopTheme.TextFail);
    }

    // ── keypad handlers ─────────────────────────────────────────────────────
    private void AppendDigit(char d)
    {
        if (_buf.Length >= 2) return;       // max 99 (BR-INJ-04)
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

    /// <summary>[CT] key — auto-fills with "1" (one cycle = 1 EA per cavity simplification).</summary>
    private void LoadCT()
    {
        _buf.Clear();
        _buf.Append('1');
        _lblInput.Text = "1";
    }

    private void Confirm()
    {
        if (_wo is null)
        {
            MessageBox.Show(this, "No WO to record against. Accept a WO in INJ-03 first.",
                "No WO", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (!int.TryParse(_buf.ToString(), out var qty) || qty <= 0)
        {
            MessageBox.Show(this, "Enter a quantity between 1 and 99.",
                "Bad qty", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // sanity check (BR-INJ-05) — if recipeCT available and qty > 1.5x
        if (_recipeCT is not null && qty > 9)
        {
            var ok = MessageBox.Show(this, $"Confirm {qty} EA in this cycle? That looks unusually high.",
                "Sanity check", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (ok != DialogResult.Yes) return;
        }

        try
        {
            var cycleSec = _recipeCT ?? 30;
            var (resultId, _, newCompleted) = PopServices.Production.RecordCycle(
                _wo.WoId, _wo.ItemNo, _wo.LineId, qty, cycleSec, _wo.MoldId,
                _session.OperatorId, _session.SessionId, _session.EmployeeNo, defectFlag: false);

            _lastResultId = resultId;
            _lastEntryAt  = DateTime.Now;
            SetStat(_lblLastEntry, _lastEntryAt.ToString("HH:mm:ss"), PopTheme.TextDim);

            // Notify when WO closed
            if (newCompleted >= _wo.OrderQty)
                MessageBox.Show(this, $"WO {_wo.WoNumber} target reached and closed.",
                    "WO complete", MessageBoxButtons.OK, MessageBoxIcon.Information);

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
            MessageBox.Show(this, "Confirm at least one cycle first; defects attach to the most recent cycle entry.",
                "No cycle", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using var f = new Inj05DefectForm(_session, _wo, _lastResultId);
        f.ShowDialog(this);
        ReloadContext();
    }

    // ── helpers ────────────────────────────────────────────────────────────
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
            BackColor = Color.Transparent, Margin = new Padding(0, 2, 8, 6),
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
    private static void SetStat(Label v, string value, Color color)
    {
        v.Text = value; v.ForeColor = color;
    }
}
