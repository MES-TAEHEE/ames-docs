namespace AMES.Pda.Components.Pages.Fg;

public partial class Fg01Stocking
{
    private const string PptLot = "5011FG260908910001";
    private bool IsDetailedTestMode => PdaScenarioUsers.IsDetailed(Auth?.Session?.EmployeeNo);
    private bool IsPptTestMode => IsDetailedTestMode || PdaScenarioUsers.IsSimple(Auth?.Session?.EmployeeNo);
    private PptScenarioPanel.Step[] ActiveScenarioSteps => IsDetailedTestMode ? FgDetailedScenarioCatalog.PutAway(PptSteps) : PptSteps;
    private string ScenarioModeLabel => IsDetailedTestMode ? "TEST MODE" : "PPT CHECK";
    private bool _pptOpen, _pptReady;
    private static readonly PptScenarioPanel.Step[] PptSteps =
    [
        new("첫 화면·LOT 스캔", "LOT 스캔 전 입력부와 CLEAR/CONFIRM을 확인합니다. 필수 입력 전 CONFIRM은 처리를 차단합니다.", new PptScenarioPanel.Value("LOT", PptLot, "LOT")),
        new("생산 완료 제품", "LOT·Part No·Part Name·Qty·Ready Date와 Location 스캔 영역을 확인합니다.", new PptScenarioPanel.Value("LOT", PptLot, "LOT")),
        new("Location·적재 확정", "Area/Aisle/Bay/Slot/Current/Load Capacity를 확인한 뒤 화면의 CONFIRM으로 적재하고 CLEAR도 검수합니다.", new PptScenarioPanel.Value("LOCATION", "FG-PPT-A1", "SCAN_LOCATION"))
    ];
    private async Task EnsurePptData()
    {
        if (_pptReady) return;
        await Api.FgResetPptTestAsync("putaway");
        _pptReady = true;
    }
    private async Task ResetPptData()
    {
        if (!IsPptTestMode || IsBusy) return;
        await Api.FgResetPptTestAsync("putaway");
        _pptReady = true;
        _modalOpen = false;
        ResetForm();
    }
    private async Task PreparePptLot()
    {
        _modalOpen = false;
        ResetForm();
        _barcode = PptLot;
        await ScanFgLot();
        if (!ReadyForLocation) throw new InvalidOperationException("적재 완료된 샘플은 테스트 데이터 초기화 후 다시 시작하세요. " + _msg);
    }
    private async Task StartPptStep(int step)
    {
        if (!IsPptTestMode || IsBusy) return;
        await EnsurePptData();
        _modalOpen = false;
        ResetForm();
        if (step == 1) return;
        await PreparePptLot();
        if (step == 3)
        {
            _locationNo = "FG-PPT-A1";
            await ScanLocation();
        }
    }
    private async Task RunPptValue(string command)
    {
        if (!IsPptTestMode || IsBusy) return;
        await EnsurePptData();
        if (!ReadyForLocation || command == "LOT") await PreparePptLot();
        switch (command)
        {
            case "SCAN_LOCATION":
                _locationNo = "FG-PPT-A1"; await ScanLocation(); break;
        }
    }
}
