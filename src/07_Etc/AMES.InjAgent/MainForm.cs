using System.Collections.Concurrent;
using AMES.InjAgent.Core;

namespace AMES.InjAgent;

/// <summary>
/// 호기별 상태 그리드 + 로그 창. 폴링은 호기당 백그라운드 태스크로 돌고
/// (100ms 주기), UI 는 500ms 타이머로 상태/로그를 끌어와 표시만 한다.
/// </summary>
public sealed class MainForm : Form
{
    private static readonly ConcurrentQueue<string> _logQueue = new();
    public static void EnqueueLog(string msg)
        => _logQueue.Enqueue($"{DateTime.Now:HH:mm:ss.fff} {msg}");

    private readonly List<PollerRunner> _runners;
    private readonly DataGridView _grid;
    private readonly ListBox _logBox;
    private readonly List<string> _logHistory = new();   // 전체 로그(최신 우선) — 필터 전환 시 재렌더용
    private string _logSel = "ALL";                       // "ALL" 또는 설비 EquipId
    private readonly System.Windows.Forms.Timer _uiTimer;

    public MainForm(List<MachinePoller> pollers, int pollingMs)
    {
        _runners = pollers.Select(p => new PollerRunner(p, pollingMs, EnqueueLog)).ToList();

        Text = "AMES InjAgent — Injection PLC Collector";
        Width = 1000;
        Height = 640;
        StartPosition = FormStartPosition.CenterScreen;

        _grid = new DataGridView
        {
            Dock = DockStyle.Top,
            Height = 220,
            ReadOnly = true,
            AllowUserToAddRows = false,
            RowHeadersVisible = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        };
        _grid.Columns.Add("EquipId",    "Equipment");
        _grid.Columns.Add(new DataGridViewButtonColumn { Name = "Ctrl", HeaderText = "Control", FlatStyle = FlatStyle.Flat });
        _grid.Columns.Add("Machine",    "Machine (Modbus)");
        _grid.Columns.Add("Robot",      "Robot (FEnet)");
        _grid.Columns.Add("Shot",       "Shot Count");
        _grid.Columns.Add("Mold",       "Mold Code");
        _grid.Columns.Add("LotLh",      "1st LOT (LH)");
        _grid.Columns.Add("LotRh",      "2nd LOT (RH)");
        _grid.Columns.Add("Inspection", "Last Inspection");
        // 자동 시작 안 함 — 각 호기는 정지 상태(START 버튼)로 시작.
        foreach (var p in pollers)
        {
            int r = _grid.Rows.Add(p.Status.EquipId, "START", "Stopped", "Stopped", "-", "-", "-", "-", "-");
            PaintCtrl(_grid.Rows[r].Cells["Ctrl"], running: false);
        }

        _logBox = new ListBox { Dock = DockStyle.Fill, Font = new Font("Consolas", 9f) };

        // 로그 필터 바 — ALL 또는 설비별.
        var logBar = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden };
        logBar.Items.Add(new ToolStripLabel("Log filter:"));
        var logFilter = new ToolStripComboBox { DropDownStyle = ComboBoxStyle.DropDownList, AutoSize = false, Width = 220 };
        logFilter.Items.Add("ALL");
        foreach (var p in pollers) logFilter.Items.Add(p.Status.EquipId);
        logFilter.SelectedIndex = 0;
        logFilter.SelectedIndexChanged += (_, _) =>
        {
            _logSel = logFilter.SelectedItem as string ?? "ALL";
            RenderLog();
        };
        logBar.Items.Add(logFilter);

        Controls.Add(_logBox);   // Fill — 남는 공간
        Controls.Add(logBar);    // Top
        Controls.Add(_grid);     // Top — 최상단

        _uiTimer = new System.Windows.Forms.Timer { Interval = 500 };
        _uiTimer.Tick += (_, _) => RefreshUi();
        _uiTimer.Start();

        _grid.CellContentClick += (_, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= _runners.Count) return;
            if (_grid.Columns[e.ColumnIndex].Name != "Ctrl") return;
            var runner = _runners[e.RowIndex];
            if (runner.IsRunning) runner.Stop(); else runner.Start();
            RefreshUi();
        };

        Load += (_, _) =>
            EnqueueLog($"Ready — {_runners.Count} machine(s), interval {pollingMs}ms. Press START on each machine to begin.");
        FormClosing += (_, _) =>
        {
            _uiTimer.Stop();
            foreach (var r in _runners) r.Dispose();
        };
    }

    // START = 초록(정지 상태 → 누르면 시작), STOP = 빨강(실행 중 → 누르면 정지).
    static void PaintCtrl(DataGridViewCell cell, bool running)
    {
        cell.Value = running ? "STOP" : "START";
        var back = running ? Color.FromArgb(200, 55, 55) : Color.FromArgb(40, 160, 70);
        cell.Style.BackColor = back;
        cell.Style.SelectionBackColor = back;
        cell.Style.ForeColor = Color.White;
        cell.Style.SelectionForeColor = Color.White;
    }

    void RefreshUi()
    {
        try
        {
            for (int i = 0; i < _runners.Count; i++)
            {
                var runner = _runners[i];
                var s = runner.Poller.Status;
                var row = _grid.Rows[i];
                row.Cells["Machine"].Value    = s.MachineConnected ? "Connected" : runner.IsRunning ? "Disconnected" : "Stopped";
                row.Cells["Robot"].Value      = s.RobotConnected   ? "Connected" : runner.IsRunning ? "Disconnected" : "Stopped";
                row.Cells["Shot"].Value       = s.ShotCount?.ToString() ?? "-";
                row.Cells["Mold"].Value       = s.MoldRaw;
                row.Cells["LotLh"].Value      = s.LotLh ?? "-";
                row.Cells["LotRh"].Value      = s.LotRh ?? "-";
                row.Cells["Inspection"].Value = s.LastInspection;
                PaintCtrl(row.Cells["Ctrl"], runner.IsRunning);
            }

            while (_logQueue.TryDequeue(out var msg))
            {
                _logHistory.Insert(0, msg);
                if (_logHistory.Count > 1000) _logHistory.RemoveAt(_logHistory.Count - 1);
                if (MatchesFilter(msg))
                {
                    _logBox.Items.Insert(0, msg);
                    if (_logBox.Items.Count > 1000) _logBox.Items.RemoveAt(_logBox.Items.Count - 1);
                }
            }
        }
        catch (ObjectDisposedException) { }
    }

    // 설비 로그는 "[EquipId] ..." 로 접두되므로 그 토큰으로 필터. ALL 은 전체 통과.
    bool MatchesFilter(string line)
        => _logSel == "ALL" || line.Contains($"[{_logSel}]", StringComparison.Ordinal);

    // 필터 전환 시 전체 로그를 다시 그린다 (최신 우선 순서 유지).
    void RenderLog()
    {
        _logBox.BeginUpdate();
        _logBox.Items.Clear();
        foreach (var line in _logHistory)
            if (MatchesFilter(line))
                _logBox.Items.Add(line);
        _logBox.EndUpdate();
    }
}
