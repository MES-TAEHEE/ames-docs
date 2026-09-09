namespace AMES.Pda.Components.Pages.Wh;

public partial class Wh08TransactionHistory
{
    private const string PptHistoryLot = "5011LL260908840001";
    private sealed record TransactionTestValue(string Label, string Value, string Action = "ROWS");
    private sealed record TransactionTestScenario(int No, string TestCaseId, string Title, string Steps,
        params TransactionTestValue[] Values);

    private static readonly TransactionTestScenario[] DetailedTestScenarios =
    [
        new(1, "WH006-TC-001", "기본 기간 최신순 조회", "오늘의 IN, OUT, ADJ 거래가 작업일시 최신순으로 표시되는지 확인합니다.",
            new TransactionTestValue("전체 이력", "3 records")),
        new(2, "WH006-TC-002", "유형별 건수 정합성", "ALL 3건과 IN, OUT, ADJ 각 1건이 목록 집계와 일치하는지 확인합니다.",
            new("ALL", "3", "ALL"), new("IN", "1", "IN"), new("OUT", "1", "OUT"), new("ADJ", "1", "ADJ")),
        new(3, "WH006-TC-003", "유형별 필터 조회", "IN, OUT, ADJ 버튼을 눌렀을 때 선택한 유형 한 건만 표시되는지 확인합니다.",
            new("IN", "1 record", "IN"), new("OUT", "1 record", "OUT"), new("ADJ", "1 record", "ADJ")),
        new(4, "WH006-TC-004", "REFRESH 최신 거래 조회", "REFRESH를 실행해 현재 조건을 유지하면서 다시 생성된 최신 거래가 표시되는지 확인합니다.",
            new TransactionTestValue("REFRESH", "Reload latest data", "REFRESH")),
        new(5, "WH006-TC-005", "조회 기간 APPLY", "오늘 날짜 범위를 적용해 오늘 발생한 거래 3건만 표시되는지 확인합니다.",
            new TransactionTestValue("DATE", "Today", "TODAY")),
        new(6, "WH006-TC-006", "잘못된 조회 기간", "From을 To보다 늦게 설정했을 때 조회하지 않고 날짜 오류 안내가 표시되는지 확인합니다.",
            new TransactionTestValue("DATE", "From > To", "INVALID_DATE")),
        new(7, "WH006-TC-007", "하루 기간 조회", "From과 To를 오늘로 설정해 오늘 00:00부터 23:59까지의 거래가 조회되는지 확인합니다.",
            new TransactionTestValue("DATE", "From = To", "TODAY")),
        new(8, "WH006-TC-008", "거래 없는 기간", "어제 날짜를 조회해 0건과 빈 상태 안내가 표시되고 이전 목록이 제거되는지 확인합니다.",
            new TransactionTestValue("DATE", "Yesterday", "YESTERDAY")),
        new(9, "WH006-TC-009", "LOT No 검색", "테스트 LOT를 검색해 해당 LOT의 IN, OUT, ADJ 거래 3건만 표시되는지 확인합니다.",
            new TransactionTestValue("LOT", PptHistoryLot, "LOT")),
        new(10, "WH006-TC-010", "Part No 검색", "테스트 Part No를 검색해 해당 품목의 거래 3건만 표시되는지 확인합니다.",
            new TransactionTestValue("PART", "PPT-WH-HIST-01", "PART")),
        new(11, "WH006-TC-011", "CLEAR 검색 초기화", "Part 검색 후 CLEAR를 눌러 검색어가 지워지고 기간과 유형 조건의 전체 목록으로 복귀하는지 확인합니다.",
            new("PART", "PPT-WH-HIST-01", "PART"), new("CLEAR", "Clear search", "CLEAR")),
        new(12, "WH006-TC-012", "거래 목록 필드", "각 행에 작업일시, LOT, 품번, Location, 수량, 작업자가 정확히 표시되는지 확인합니다.",
            new TransactionTestValue("ROWS", "IN 20 / OUT 4 / ADJ +2")),
        new(13, "WH006-TC-013", "거래 유형 색상", "IN, OUT, ADJ가 서로 다른 일관된 색상과 라벨로 구분되는지 확인합니다.",
            new("IN", "Inbound color", "IN"), new("OUT", "Outbound color", "OUT"), new("ADJ", "Adjust color", "ADJ")),
        new(14, "WH006-TC-014", "ADJ 상세 열기", "ADJ 행의 DETAIL을 열어 Before 16, Change +2, After 18과 부가정보를 확인합니다.",
            new TransactionTestValue("DETAIL", "Open adjustment", "DETAIL")),
        new(15, "WH006-TC-015", "ADJ 상세 정합성", "상세의 수량, Reason, Note, Worker, Supervisor가 저장된 테스트 거래와 일치하는지 확인합니다.",
            new TransactionTestValue("DETAIL", "16 / +2 / 18", "DETAIL")),
        new(16, "WH006-TC-016", "상세 CLOSE", "DETAIL을 연 뒤 CLOSE를 눌러 기존 기간, 유형, 검색 조건을 유지한 목록으로 복귀하는지 확인합니다.",
            new TransactionTestValue("DETAIL", "Open then CLOSE", "DETAIL")),
        new(17, "WH006-TC-017", "조회 API 오류", "API ERROR를 ON으로 설정하고 시작해 오류 안내와 화면 유지 여부를 확인한 뒤 OFF로 재시도합니다.",
            new TransactionTestValue("RETRY", "API ERROR OFF", "RETRY"))
    ];

    private string ActivePptLot => IsFinishedGoods ? "5011FG260908970001" : PptHistoryLot;
    private static readonly PptScenarioPanel.Step[] FgPptSteps =
    [
        new("유형별 집계", "ALL 5건과 IN·PICK·LOAD·RETURN·ADJ 각 1건, 최신순 목록과 REFRESH를 확인합니다.", new PptScenarioPanel.Value("유형", "IN", "IN"), new PptScenarioPanel.Value("유형", "PICK", "PICK"), new PptScenarioPanel.Value("유형", "LOAD", "LOAD"), new PptScenarioPanel.Value("유형", "RETURN", "RETURN"), new PptScenarioPanel.Value("유형", "ADJ", "ADJ")),
        new("조회 기간·필터", "시작일·종료일과 APPLY를 확인합니다. 샘플은 오늘 5건, 어제 0건이며 Worker·Reason으로도 검색합니다.", new PptScenarioPanel.Value("기간", "오늘", "TODAY"), new PptScenarioPanel.Value("기간", "어제", "YESTERDAY"), new PptScenarioPanel.Value("작업자", "TEST1", "WORKER")),
        new("바코드 검색", "LOT·Stock·Part·출고전표로 이력을 검색하고 CLEAR로 검색어를 초기화합니다.", new PptScenarioPanel.Value("LOT", "5011FG260908970001", "LOT"), new PptScenarioPanel.Value("STOCK", "FG-PPT-STK-970001", "STOCK"), new PptScenarioPanel.Value("PART", "PPT-FG-HIST", "PART"), new PptScenarioPanel.Value("출고전표", "2609089005", "SLIP"), new PptScenarioPanel.Value("검색 없음", "PPT-NOT-FOUND", "UNKNOWN")),
        new("작업 이력 상세", "Put-Away 20, 조정 +2, 피킹·적재·반품 각 22 EA의 일시·LOT·위치·작업자와 DETAIL을 확인합니다.", new PptScenarioPanel.Value("반품 상세", "Reason·Note·Reference", "RETURN_DETAIL"), new PptScenarioPanel.Value("전체", "샘플 5건", "ROWS")),
        new("조정 상세", "DETAIL에서 Before 20, Change +2, After 22와 사유·Note·작업자·승인자를 확인합니다.", new PptScenarioPanel.Value("DETAIL", "조정 상세 열기", "DETAIL"))
    ];
    private static readonly PptScenarioPanel.Step[] PptSteps =
    [
        new("집계와 조회", "ALL·IN·OUT·ADJ 건수와 최신순 목록, REFRESH를 확인합니다.", new PptScenarioPanel.Value("유형", "IN", "IN"), new PptScenarioPanel.Value("유형", "OUT", "OUT"), new PptScenarioPanel.Value("유형", "ADJ", "ADJ"), new PptScenarioPanel.Value("유형", "ALL", "ALL")),
        new("조회 기간·필터", "시작일·종료일을 선택하고 APPLY로 조회합니다. 오늘은 3건, 어제는 0건입니다.", new PptScenarioPanel.Value("기간", "오늘", "TODAY"), new PptScenarioPanel.Value("기간", "어제", "YESTERDAY")),
        new("바코드 검색", "LOT 또는 Part를 눌러 검색한 뒤 화면의 CLEAR로 검색어를 초기화합니다.", new PptScenarioPanel.Value("LOT", PptHistoryLot, "LOT"), new PptScenarioPanel.Value("PART", "PPT-WH-HIST-01", "PART"), new PptScenarioPanel.Value("검색 없음", "PPT-NOT-FOUND", "UNKNOWN")),
        new("거래 목록", "입고 20, 출고 4, 조정 +2의 일시·LOT·품목·위치·색상·작업자를 확인합니다.", new PptScenarioPanel.Value("이력", "샘플 3건 조회", "ROWS")),
        new("조정 상세", "DETAIL에서 Before 16, Change +2, After 18과 Reason·Note·Worker·Supervisor를 확인합니다.", new PptScenarioPanel.Value("DETAIL", "조정 상세 열기", "DETAIL"))
    ];
    private bool IsDetailedWhTestMode => !IsFinishedGoods
        && PdaScenarioUsers.IsDetailed(Auth?.Session?.EmployeeNo);
    private bool IsDetailedFgTestMode => IsFinishedGoods
        && PdaScenarioUsers.IsDetailed(Auth?.Session?.EmployeeNo);
    private bool IsPptTestMode => IsDetailedFgTestMode || PdaScenarioUsers.IsSimple(Auth?.Session?.EmployeeNo);
    private PptScenarioPanel.Step[] ActivePptSteps => IsDetailedFgTestMode
        ? FgDetailedScenarioCatalog.Transactions(FgPptSteps)
        : IsFinishedGoods ? FgPptSteps : PptSteps;
    private string ScenarioModeLabel => IsDetailedFgTestMode ? "TEST MODE" : "PPT CHECK";
    private bool IsScenarioPanelOpen => _pptOpen || _testPanelOpen;
    private bool SimulateHistoryApiFailure => IsDetailedWhTestMode
        && CurrentDetailedTestScenario.No == 17
        && _simulateHistoryApiFailure;
    private TransactionTestScenario CurrentDetailedTestScenario => DetailedTestScenarios[_testScenarioIndex];
    private bool _pptOpen;
    private bool _pptReady;
    private bool _testPanelOpen;
    private bool _simulateHistoryApiFailure;
    private int _testScenarioIndex;

    private void OpenDetailedTestPanel() => _testPanelOpen = true;
    private void CloseDetailedTestPanel() => _testPanelOpen = false;
    private void ToggleHistoryApiFailure() => _simulateHistoryApiFailure = !_simulateHistoryApiFailure;
    private void SelectDetailedTestScenario(int index)
    {
        if (index < 0 || index >= DetailedTestScenarios.Length) return;
        _testScenarioIndex = index;
        if (CurrentDetailedTestScenario.No != 17) _simulateHistoryApiFailure = false;
    }

    private async Task PrepareDetailedHistory(string search = PptHistoryLot)
    {
        _dateFrom = DateTime.Today;
        _dateTo = DateTime.Today;
        _search = search;
        _worker = "";
        _reason = "";
        _type = "";
        await Load();
    }

    private async Task StartDetailedTestScenario()
    {
        if (!IsDetailedWhTestMode || _loading) return;
        CloseDetailedTestPanel();
        await Api.WhResetHistoryTestAsync();
        switch (CurrentDetailedTestScenario.No)
        {
            case 6:
                _dateFrom = DateTime.Today;
                _dateTo = DateTime.Today.AddDays(-1);
                _search = PptHistoryLot;
                _worker = _reason = _type = "";
                await Load();
                break;
            case 8:
                _dateFrom = _dateTo = DateTime.Today.AddDays(-1);
                _search = PptHistoryLot;
                _worker = _reason = _type = "";
                await Load();
                break;
            case 10:
            case 11:
                await PrepareDetailedHistory("PPT-WH-HIST-01");
                break;
            case 14:
            case 15:
            case 16:
                await PrepareDetailedHistory();
                if (_rows.FirstOrDefault(IsAdjust) is { } adjustment) OpenDetail(adjustment);
                break;
            default:
                await PrepareDetailedHistory();
                break;
        }
    }

    private async Task RunDetailedTestValue(TransactionTestValue value)
    {
        if (!IsDetailedWhTestMode || _loading) return;
        CloseDetailedTestPanel();
        if (value.Action is "IN" or "OUT" or "ADJ" or "ALL")
        {
            await PrepareDetailedHistory();
            SetType(value.Action == "ALL" ? "" : value.Action);
            return;
        }
        if (value.Action == "REFRESH")
        {
            await Api.WhResetHistoryTestAsync();
            await PrepareDetailedHistory();
            return;
        }
        if (value.Action == "INVALID_DATE")
        {
            _dateFrom = DateTime.Today;
            _dateTo = DateTime.Today.AddDays(-1);
            _search = PptHistoryLot;
            await Load();
            return;
        }
        if (value.Action == "YESTERDAY")
        {
            _dateFrom = _dateTo = DateTime.Today.AddDays(-1);
            _search = PptHistoryLot;
            await Load();
            return;
        }
        if (value.Action == "PART") { await PrepareDetailedHistory("PPT-WH-HIST-01"); return; }
        if (value.Action == "CLEAR") { await ClearSearch(); return; }
        if (value.Action == "DETAIL")
        {
            await PrepareDetailedHistory();
            if (_rows.FirstOrDefault(IsAdjust) is { } adjustment) OpenDetail(adjustment);
            return;
        }
        if (value.Action == "RETRY")
        {
            _simulateHistoryApiFailure = false;
            await PrepareDetailedHistory();
            return;
        }
        await PrepareDetailedHistory(value.Action == "PART" ? "PPT-WH-HIST-01" : PptHistoryLot);
    }

    private async Task EnsurePptData()
    {
        if (!_pptReady) { await ResetPptHistory(); _pptReady = true; }
    }
    private Task ResetPptHistory() => IsFinishedGoods ? Api.FgResetPptTestAsync("history") : Api.WhResetPptTestAsync("history");
    private async Task PreparePptRows()
    {
        _dateFrom = DateTime.Today;
        _dateTo = DateTime.Today;
        _search = ActivePptLot;
        _worker = "";
        _reason = "";
        _type = "";
        await Load();
        if (_rows.Count != (IsFinishedGoods ? 5 : 3)) throw new InvalidOperationException("PPT history samples are incomplete. Reset test data and retry.");
    }
    private async Task ResetPptData()
    {
        if (!IsPptTestMode || _loading) return;
        await ResetPptHistory();
        _pptReady = true;
        await PreparePptRows();
    }
    private async Task StartPptStep(int step)
    {
        if (!IsPptTestMode || _loading) return;
        await EnsurePptData();
        await PreparePptRows();
        if (step == 2) BeginFilterEdit();
        if (step == 5) OpenDetail(_rows.First(IsAdjust));
    }
    private async Task RunPptValue(string command)
    {
        if (!IsPptTestMode || _loading) return;
        await EnsurePptData();
        await PreparePptRows();
        if (command is "IN" or "OUT" or "PICK" or "LOAD" or "RETURN" or "ADJ" or "ALL") { SetType(command == "ALL" ? "" : command); return; }
        if (command == "YESTERDAY") { _dateFrom = DateTime.Today.AddDays(-1); _dateTo = _dateFrom; }
        if (command == "PART") _search = IsFinishedGoods ? "PPT-FG-HIST" : "PPT-WH-HIST-01";
        if (IsFinishedGoods && command == "STOCK") _search = "FG-PPT-STK-970001";
        if (IsFinishedGoods && command == "SLIP") _search = "2609089005";
        if (IsFinishedGoods && command == "WORKER") _worker = "TEST1";
        if (IsFinishedGoods && command == "RETURN_DETAIL") { OpenDetail(_rows.First(row => row.Direction == "RETURN")); return; }
        if (command == "UNKNOWN") _search = "PPT-NOT-FOUND";
        if (command == "DETAIL") { OpenDetail(_rows.First(IsAdjust)); return; }
        await Load();
    }
}
