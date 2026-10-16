namespace AMES.Pda.Components.Pages.Fg;

public partial class Fg02Inventory
{
    private bool IsDetailedTestMode => PdaScenarioUsers.IsDetailed(Auth?.Session?.EmployeeNo);
    private bool IsPptTestMode => IsDetailedTestMode || PdaScenarioUsers.IsSimple(Auth?.Session?.EmployeeNo);
    private PptScenarioPanel.Step[] ActiveScenarioSteps => IsDetailedTestMode ? FgDetailedScenarioCatalog.Inventory(PptSteps) : PptSteps;
    private string ScenarioModeLabel => IsDetailedTestMode ? "TEST MODE" : "PPT CHECK";
    private bool _pptOpen, _pptReady;
    private static readonly PptScenarioPanel.Step[] PptSteps =
    [
        new("By Location", "재고가 있는 위치와 Qty 목록을 확인하고 REFRESH/CLEAR를 검수합니다.", new PptScenarioPanel.Value("검색", "PPT-FG-INV-01", "SEARCH")),
        new("Location Parts", "FG-PPT-B1의 Part No·Part Name·Qty를 확인하고 품목을 누릅니다.", new PptScenarioPanel.Value("LOCATION", "FG-PPT-B1", "LOCATION")),
        new("Location LOT Details", "같은 품목 LOT 2개의 수량 30/20 EA와 위치를 확인합니다.", new PptScenarioPanel.Value("LOT 상세", "PPT-FG-INV-01", "LOCATION_LOTS")),
        new("By Part", "동일 품목·위치가 50 EA로 합산되는지 확인합니다.", new PptScenarioPanel.Value("PART", "PPT-FG-INV-01", "PART")),
        new("Part LOT Details", "Part를 선택해 LOT별 수량·단위·위치를 확인하고 BACK으로 돌아옵니다.", new PptScenarioPanel.Value("LOT 상세", "PPT-FG-INV-01", "PART_LOTS")),
        new("API Error", "조회 실패를 빈 재고로 처리하지 않고 오류 안내가 표시되는지 확인합니다.", new PptScenarioPanel.Value("API 오류", "연결 오류 표시", "API_ERROR"))
    ];
    private async Task ResetPptData()
    {
        if (!IsPptTestMode || _isLoading) return;
        await Api.FgResetPptTestAsync("inventory");
        _pptReady = true;
        await StartPptStep(1);
    }
    private async Task StartPptStep(int step)
    {
        if (!IsPptTestMode || _isLoading) return;
        if (!_pptReady) { await Api.FgResetPptTestAsync("inventory"); _pptReady = true; }
        SetMode(step >= 4 ? "Part" : "Location");
        ClearSearch();
        await LoadInventory();
        if (step is 2 or 3)
        {
            OpenLocation(LocationRows.First(row => row.Location == "FG-PPT-B1"));
            if (step == 3) OpenPart(LocationParts.First(row => row.PartNo == "PPT-FG-INV-01"));
        }
        if (step >= 4)
        {
            _query = "PPT-FG-INV-01";
            await Search();
            if (step == 5) OpenPart(PartRows.First(row => row.PartNo == _query));
        }
    }
    private async Task RunPptValue(string command)
    {
        if (IsPptTestMode && command == "API_ERROR")
        {
            _simulateApiFailure = true;
            await LoadInventory();
            _simulateApiFailure = false;
            return;
        }
        await StartPptStep(command switch { "LOCATION" => 2, "LOCATION_LOTS" => 3, "PART" => 4, "PART_LOTS" => 5, _ => 1 });
        if (IsPptTestMode && command == "SEARCH") { _query = "PPT-FG-INV-01"; await Search(); }
    }
}
