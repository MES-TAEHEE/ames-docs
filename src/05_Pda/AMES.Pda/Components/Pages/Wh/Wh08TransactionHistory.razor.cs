namespace AMES.Pda.Components.Pages.Wh;

public partial class Wh08TransactionHistory
{
    private const string PptHistoryLot = "5011LL260908840001";
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
    private bool IsPptTestMode => string.Equals(Auth?.Session?.EmployeeNo, "TEST1", StringComparison.OrdinalIgnoreCase);
    private bool _pptOpen;
    private bool _pptReady;
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
