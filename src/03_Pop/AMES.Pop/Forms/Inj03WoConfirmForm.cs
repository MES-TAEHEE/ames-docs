using System.Text.Json;
using AMES.Contracts.Dto;
using AMES.Pop.Common;

namespace AMES.Pop.Forms;

/// <summary>
/// INJ-03 WO Confirm — pick the next Released WO for the line, run the 5-step
/// pre-start checklist, Accept → locks WO to this terminal + transitions
/// status to In Progress.
/// </summary>
public sealed class Inj03WoConfirmForm : PopForm
{
    private readonly PopSessionDto _session;
    private readonly ListBox       _woList;
    private readonly Label         _lblDetail;
    private readonly CheckedListBox _checklist;
    private readonly Button        _btnAccept;
    private List<WorkOrderDto>     _wos = new();
    private WorkOrderDto?          _selected;

    public Inj03WoConfirmForm(PopSessionDto session)
    {
        _session = session;
        Text = "A-MES POP · INJ-03 WO Confirm";

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
            BackColor = PopTheme.BgOuter,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 130));
        root.Controls.Add(PopShell.BuildTopBar("INJ-03 · WO Confirm", session), 0, 0);

        // middle: 2-column = WO list (40%) | detail+checklist (60%)
        var split = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            BackColor = PopTheme.BgOuter, Padding = new Padding(20, 14, 20, 6),
        };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        split.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        // left: WO ListBox inside a card
        var leftCard = new Panel { Dock = DockStyle.Fill, BackColor = PopTheme.BgCard, Padding = new Padding(12), Margin = new Padding(6) };
        var leftStack = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Color.Transparent };
        leftStack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        leftStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        leftStack.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        leftStack.Controls.Add(PopShell.SectionHeader("▼ RELEASED WOs (line: " + session.LineId + ")"), 0, 0);
        _woList = new ListBox
        {
            Dock = DockStyle.Fill, BackColor = PopTheme.BgCard, ForeColor = PopTheme.TextWhite,
            Font = new Font("Segoe UI", 14f, FontStyle.Bold), BorderStyle = BorderStyle.None,
            IntegralHeight = false, ItemHeight = 52,
        };
        _woList.SelectedIndexChanged += (_, _) => OnWoSelected();
        leftStack.Controls.Add(_woList, 0, 1);
        leftCard.Controls.Add(leftStack);
        split.Controls.Add(leftCard, 0, 0);

        // right: detail + checklist
        var rightCard = new Panel { Dock = DockStyle.Fill, BackColor = PopTheme.BgCard, Padding = new Padding(18), Margin = new Padding(6) };
        var rightStack = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Color.Transparent };
        rightStack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        rightStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rightStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 260));
        rightStack.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        rightStack.Controls.Add(PopShell.SectionHeader("▼ WO DETAIL"), 0, 0);
        _lblDetail = new Label
        {
            Text = "Select a WO from the left list.",
            Font = new Font("Consolas", 14f), ForeColor = PopTheme.AccentSoft,
            AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.TopLeft,
            Padding = new Padding(12, 8, 8, 8),
        };
        rightStack.Controls.Add(_lblDetail, 0, 1);

        var clStack = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Color.Transparent };
        clStack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        clStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        clStack.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        clStack.Controls.Add(PopShell.SectionHeader("▼ PRE-START CHECKLIST (BR-INJ-W01)"), 0, 0);
        _checklist = new CheckedListBox
        {
            Dock = DockStyle.Fill, BackColor = PopTheme.BgCard, ForeColor = PopTheme.TextWhite,
            BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 13f),
            CheckOnClick = true, ItemHeight = 44,
        };
        foreach (var item in PreCheckItems()) _checklist.Items.Add(item, false);
        _checklist.ItemCheck += (_, _) => BeginInvoke(UpdateAcceptEnabled);
        clStack.Controls.Add(_checklist, 0, 1);
        rightStack.Controls.Add(clStack, 0, 2);

        rightCard.Controls.Add(rightStack);
        split.Controls.Add(rightCard, 1, 0);

        root.Controls.Add(split, 0, 1);

        // bottom: nav row
        var btns = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1,
            BackColor = PopTheme.BgCard, Padding = new Padding(16, 10, 16, 10),
        };
        btns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        btns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        btns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        btns.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        btns.Controls.Add(PopShell.BigButton("← Back",        PopTheme.BgKey, Color.White, (_, _) => Close()), 0, 0);
        btns.Controls.Add(PopShell.BigButton("⟳ Refresh List", PopTheme.BgKey, Color.White, (_, _) => Reload()),       1, 0);
        _btnAccept = PopShell.BigButton("✓ Accept WO → INJ-04", PopTheme.BgKeyOk, Color.White, (_, _) => Accept());
        _btnAccept.Enabled = false;
        btns.Controls.Add(_btnAccept, 2, 0);
        root.Controls.Add(btns, 0, 2);

        Controls.Add(root);
        Reload();
    }

    private static IEnumerable<string> PreCheckItems() => new[]
    {
        "1. Mold mounted matches WO routing",
        "2. Material lot available + within expiry",
        "3. Recipe loaded into PLC (pressure / temp / time)",
        "4. Safety guards closed + interlock OK",
        "5. Phase-0 master data complete (BR-MD-04)",
    };

    private void Reload()
    {
        _wos = PopServices.WorkOrders.ListForLine(_session.LineId);
        _woList.Items.Clear();
        foreach (var w in _wos)
        {
            var prio = w.Priority <= 2 ? "🔥 HIGH" : "▪ NORMAL";
            var dDay = w.DaysToDue is null ? "" : $"D-{w.DaysToDue}";
            _woList.Items.Add($"{prio}  {dDay,-5}  {w.WoNumber}   {w.ItemName}   ({w.CompletedQty:0}/{w.OrderQty:0})");
        }
        _selected = null;
        _lblDetail.Text = _wos.Count == 0
            ? "No Released WOs for this line.\nPP planner needs to issue a WO first."
            : "Select a WO from the left list.";
        UpdateAcceptEnabled();
    }

    private void OnWoSelected()
    {
        var idx = _woList.SelectedIndex;
        _selected = idx >= 0 && idx < _wos.Count ? _wos[idx] : null;
        if (_selected is null) { _lblDetail.Text = "—"; UpdateAcceptEnabled(); return; }

        var mold = string.IsNullOrEmpty(_selected.MoldId) ? null : PopServices.Molds.GetById(_selected.MoldId);
        var moldLine = mold is null
            ? $"Mold       : {_selected.MoldId ?? "(none)"}"
            : $"Mold       : {mold.MoldId}  ({mold.LifePct:0}%, sev={mold.Severity})";

        _lblDetail.Text = string.Join("\r\n", new[]
        {
            $"WO         : {_selected.WoNumber}",
            $"Item       : {_selected.ItemName}  ({_selected.ItemNameEn ?? "—"})  [{_selected.ItemNo}]",
            $"Qty        : {_selected.CompletedQty:0} / {_selected.OrderQty:0}  ({_selected.ProgressPct:0}%)",
            $"Due        : {(_selected.DueDate?.ToString("yyyy-MM-dd") ?? "—")}  (D-{_selected.DaysToDue?.ToString() ?? "?"})",
            moldLine,
            $"Recipe     : {_selected.RecipeId ?? "—"}",
            $"Status     : {_selected.Status}    Lock: {_selected.TerminalLock ?? "—"}",
        });

        // Reset checklist for the new WO.
        for (var i = 0; i < _checklist.Items.Count; i++) _checklist.SetItemChecked(i, false);
        UpdateAcceptEnabled();
    }

    private void UpdateAcceptEnabled()
    {
        var allChecked = _checklist.CheckedItems.Count == _checklist.Items.Count;
        var available  = _selected is not null
                      && (_selected.TerminalLock == null
                          || _selected.TerminalLock == _session.TerminalId);
        _btnAccept.Enabled = allChecked && available;
    }

    private void Accept()
    {
        if (_selected is null) return;
        var checks = new
        {
            mold          = _checklist.GetItemChecked(0),
            material      = _checklist.GetItemChecked(1),
            recipe        = _checklist.GetItemChecked(2),
            safety        = _checklist.GetItemChecked(3),
            phase0Master  = _checklist.GetItemChecked(4),
            checkedBy     = _session.EmployeeNo,
            checkedAt     = DateTime.Now,
        };
        try
        {
            PopServices.WorkOrders.AcceptWo(
                _selected.WoId, _session.TerminalId, _session.OperatorId,
                _session.EmployeeNo, JsonSerializer.Serialize(checks));
            MessageBox.Show(this, $"WO {_selected.WoNumber} accepted.\r\nOpen INJ-04 to start production.",
                "Accepted", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Accept failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
