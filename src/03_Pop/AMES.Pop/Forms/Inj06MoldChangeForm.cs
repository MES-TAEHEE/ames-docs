using AMES.Contracts.Dto;
using AMES.Pop.Common;

namespace AMES.Pop.Forms;

/// <summary>
/// INJ-06 Mold Change — show current mold life, pick a replacement, then
/// open a PR_MoldChange row. "Complete change" closes it, resets the new
/// mold to 0 shots, and marks the old mold for Maintenance.
/// </summary>
public sealed class Inj06MoldChangeForm : PopForm
{
    private readonly PopSessionDto _session;
    private readonly Label _lblCurrent, _lblCurrentLife, _lblSeverity;
    private readonly ProgressBar _bar;
    private readonly ComboBox _cboReplacement;
    private readonly Button _btnStart, _btnComplete;
    private readonly Label _lblStatus;

    private WorkOrderDto? _wo;
    private MoldDto?      _current;
    private List<MoldDto> _available = new();
    private int           _openChangeId;     // 0 when no change in progress

    public Inj06MoldChangeForm(PopSessionDto session)
    {
        _session = session;
        Text = "A-MES POP · INJ-06 Mold Change";

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = PopTheme.BgOuter,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 130));
        root.Controls.Add(PopShell.BuildTopBar("INJ-06 · Mold Change", session), 0, 0);

        var body = new Panel { Dock = DockStyle.Fill, BackColor = PopTheme.BgOuter, Padding = new Padding(24) };

        var card = new Panel { Dock = DockStyle.Fill, BackColor = PopTheme.BgCard, Padding = new Padding(24) };

        var stack = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8, BackColor = Color.Transparent };
        stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 8; i++) stack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        stack.Controls.Add(PopShell.SectionHeader("▼ MOUNTED MOLD"), 0, 0);
        _lblCurrent = new Label
        {
            Text = "(loading)", Font = new Font("Segoe UI", 24f, FontStyle.Bold),
            ForeColor = PopTheme.Accent, AutoSize = true, AutoEllipsis = true,
            MaximumSize = new Size(1400, 0), Margin = new Padding(4, 0, 0, 0),
        };
        stack.Controls.Add(_lblCurrent, 0, 1);
        _lblCurrentLife = new Label
        {
            Text = "", Font = new Font("Segoe UI", 44f, FontStyle.Bold), ForeColor = PopTheme.TextOk,
            AutoSize = true, Margin = new Padding(4, 8, 0, 12),
        };
        stack.Controls.Add(_lblCurrentLife, 0, 2);
        _bar = new ProgressBar { Dock = DockStyle.Top, Height = 32, Maximum = 110, Minimum = 0, Style = ProgressBarStyle.Continuous, Margin = new Padding(4, 0, 4, 12) };
        stack.Controls.Add(_bar, 0, 3);
        _lblSeverity = new Label
        {
            Text = "", Font = PopTheme.MonoBold, ForeColor = PopTheme.AccentSoft, AutoSize = true,
            Margin = new Padding(4, 0, 0, 16),
        };
        stack.Controls.Add(_lblSeverity, 0, 4);

        stack.Controls.Add(PopShell.SectionHeader("▼ REPLACEMENT MOLD"), 0, 5);
        _cboReplacement = new ComboBox
        {
            Dock = DockStyle.Top, Margin = new Padding(4, 0, 16, 16),
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = new Font("Segoe UI", 18f, FontStyle.Bold),
            BackColor = Color.Black, ForeColor = PopTheme.Accent, FlatStyle = FlatStyle.Flat,
            Height = 56,
        };
        stack.Controls.Add(_cboReplacement, 0, 6);

        _lblStatus = new Label
        {
            Text = " ", Font = PopTheme.MonoBold, ForeColor = PopTheme.TextDim,
            AutoSize = false, Dock = DockStyle.Top, Height = 28, TextAlign = ContentAlignment.MiddleCenter,
        };
        stack.Controls.Add(_lblStatus, 0, 7);

        card.Controls.Add(stack);
        body.Controls.Add(card);
        root.Controls.Add(body, 0, 1);

        // nav
        var nav = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1, BackColor = PopTheme.BgCard,
            Padding = new Padding(16, 10, 16, 10),
        };
        nav.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        nav.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        nav.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        nav.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        nav.Controls.Add(PopShell.BigButton("← Back", PopTheme.BgKey, Color.White, (_, _) => Close()), 0, 0);
        _btnStart    = PopShell.BigButton("🔧 Start Change (open Downtime)",
                            Color.FromArgb(170,120,20), Color.White, (_, _) => StartChange());
        _btnComplete = PopShell.BigButton("✓ Complete Change (reset to 0)",
                            PopTheme.BgKeyOk, Color.White, (_, _) => CompleteChange());
        _btnComplete.Enabled = false;
        nav.Controls.Add(_btnStart,    1, 0);
        nav.Controls.Add(_btnComplete, 2, 0);
        root.Controls.Add(nav, 0, 2);

        Controls.Add(root);
        Reload();
    }

    private void Reload()
    {
        _wo = PopServices.WorkOrders.GetActiveForTerminal(_session.LineId, _session.TerminalId)
           ?? PopServices.WorkOrders.ListForLine(_session.LineId).FirstOrDefault();
        var moldId = _wo?.MoldId;
        _current = string.IsNullOrEmpty(moldId) ? null : PopServices.Molds.GetById(moldId);

        if (_current is null)
        {
            _lblCurrent.Text = "(no mold mounted)";
            _lblCurrentLife.Text = "—";
            _bar.Value = 0;
            _lblSeverity.Text = "";
        }
        else
        {
            _lblCurrent.Text     = $"{_current.MoldId}   {_current.MoldName}";
            _lblCurrentLife.Text = $"{_current.CurrentShots:N0} / {_current.RatedShots:N0}   ·   {_current.LifePct:0.0}%";
            _bar.Value           = Math.Clamp((int)_current.LifePct, 0, 110);
            (_lblSeverity.Text, _lblSeverity.ForeColor) = _current.Severity switch
            {
                "ok"    => ("Status: OK — within rated life.",            PopTheme.TextOk),
                "info"  => ("Status: 80% — schedule a change soon.",       PopTheme.Accent),
                "warn"  => ("⚠ 90% reached — change recommended this shift.", PopTheme.AccentSoft),
                "alert" => ("🚨 100% reached — change ASAP (BR-INJ-02).",   PopTheme.TextFail),
                "hard"  => ("⛔ 110% — HARD STOP. Change before resuming.", PopTheme.TextFail),
                _       => ("",                                            PopTheme.TextDim),
            };
            (_lblCurrentLife.ForeColor) = _current.Severity is "alert" or "hard"
                ? PopTheme.TextFail
                : _current.Severity is "warn" ? PopTheme.AccentSoft : PopTheme.TextOk;
        }

        _available = PopServices.Molds.ListAvailable()
                        .Where(m => _current is null || m.MoldId != _current.MoldId).ToList();
        _cboReplacement.Items.Clear();
        foreach (var m in _available)
            _cboReplacement.Items.Add($"{m.MoldId}   {m.MoldName}   ({m.LifePct:0}%, {m.Status})");
        if (_cboReplacement.Items.Count > 0) _cboReplacement.SelectedIndex = 0;

        _btnStart.Enabled = _openChangeId == 0 && _cboReplacement.Items.Count > 0;
        _btnComplete.Enabled = _openChangeId != 0;
    }

    private void StartChange()
    {
        if (_cboReplacement.SelectedIndex < 0) return;
        var newMold = _available[_cboReplacement.SelectedIndex];
        var equipId = PopServices.Equipment.GetPrimaryForLine(_session.LineId)?.EquipId ?? "UNKNOWN";
        try
        {
            _openChangeId = PopServices.Molds.StartChange(
                equipId, _session.LineId, _current?.MoldId, newMold.MoldId,
                _current?.CurrentShots ?? 0, _session.OperatorId, _session.EmployeeNo,
                _current?.Severity is "alert" or "hard" ? "WEAR_LIMIT" : "PLANNED");

            PopServices.Equipment.LogStatus(equipId, _session.LineId, "STOP", "MOLD_CHANGE", _wo?.WoId, _session.EmployeeNo);
            _lblStatus.Text      = $"Change started (id #{_openChangeId}). Downtime running. INJ-04 locked until complete.";
            _lblStatus.ForeColor = PopTheme.AccentSoft;
            _btnStart.Enabled = false; _btnComplete.Enabled = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Start failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void CompleteChange()
    {
        if (_openChangeId == 0) return;
        try
        {
            var newMoldId = _available[Math.Max(0, _cboReplacement.SelectedIndex)].MoldId;
            PopServices.Molds.CompleteChange(_openChangeId, _current?.MoldId, newMoldId);
            var equipId = PopServices.Equipment.GetPrimaryForLine(_session.LineId)?.EquipId ?? "UNKNOWN";
            PopServices.Equipment.LogStatus(equipId, _session.LineId, "RUN", "RESUMED", _wo?.WoId, _session.EmployeeNo);
            _lblStatus.Text      = $"Change #{_openChangeId} completed. New mold shots reset to 0.";
            _lblStatus.ForeColor = PopTheme.TextOk;
            _openChangeId = 0;
            Reload();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Complete failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
