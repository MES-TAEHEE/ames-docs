namespace AMES.Pda.Components.Pages.Fg;

public partial class Fg01Stocking
{
    private const string PptLot = "5011FG260908910001";
    private bool IsDetailedTestMode => string.Equals(Auth?.Session?.EmployeeNo, "TEST", StringComparison.OrdinalIgnoreCase);
    private bool IsPptTestMode => IsDetailedTestMode || string.Equals(Auth?.Session?.EmployeeNo, "TEST1", StringComparison.OrdinalIgnoreCase);
    private PptScenarioPanel.Step[] ActiveScenarioSteps => IsDetailedTestMode ? FgDetailedScenarioCatalog.PutAway(PptSteps) : PptSteps;
    private string ScenarioModeLabel => IsDetailedTestMode ? "TEST MODE" : "PPT CHECK";
    private bool _pptOpen, _pptReady;
    private static readonly PptScenarioPanel.Step[] PptSteps =
    [
        new("첫 화면·LOT 스캔", "LOT 스캔 전 입력부와 CLEAR/CONFIRM을 확인합니다. 필수 입력 전 CONFIRM은 처리를 차단합니다.", new PptScenarioPanel.Value("LOT", PptLot, "LOT")),
        new("QC 완료 제품", "LOT·Part No·Part Name·Qty·QC Passed Date와 Storage 선택 영역을 확인합니다.", new PptScenarioPanel.Value("LOT", PptLot, "LOT")),
        new("보관 방법", "Box/Pallet/Rack/Location Only를 선택하고 다음 스캔 항목이 바뀌는지 확인합니다.", new PptScenarioPanel.Value("STORAGE", "Box", "BOX"), new PptScenarioPanel.Value("STORAGE", "Pallet", "PALLET"), new PptScenarioPanel.Value("STORAGE", "Rack", "RACK"), new PptScenarioPanel.Value("STORAGE", "Location Only", "LOCATION")),
        new("보관 단위 스캔", "선택한 보관 단위를 스캔하면 Location 입력부로 이동하며 강조되는지 확인합니다.", new PptScenarioPanel.Value("BOX", "BOX:PPT-FG-01", "CONTAINER")),
        new("Location·적재 확정", "Zone/Bay/Slot/Current/Free를 확인한 뒤 화면의 CONFIRM으로 적재하고 CLEAR도 검수합니다.", new PptScenarioPanel.Value("LOCATION", "FG-PPT-A1", "SCAN_LOCATION"))
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
        if (!ReadyForStorage) throw new InvalidOperationException("적재 완료된 샘플은 테스트 데이터 초기화 후 다시 시작하세요. " + _msg);
    }
    private async Task StartPptStep(int step)
    {
        if (!IsPptTestMode || IsBusy) return;
        await EnsurePptData();
        _modalOpen = false;
        ResetForm();
        if (step == 1) return;
        await PreparePptLot();
        if (step >= 4) OnStorageChanged("BOX");
        if (step == 5)
        {
            await ScanPptContainer();
            _locationNo = "FG-PPT-A1";
            await ScanLocation();
        }
    }
    private async Task ScanPptContainer()
    {
        if (!RequiresContainer) OnStorageChanged("BOX");
        _containerBarcode = $"{_storageMethod}:PPT-FG-01";
        await ScanContainer();
    }
    private async Task RunPptValue(string command)
    {
        if (!IsPptTestMode || IsBusy) return;
        await EnsurePptData();
        if (!ReadyForStorage || command == "LOT") await PreparePptLot();
        switch (command)
        {
            case "BOX": case "PALLET": case "RACK": case "LOCATION": OnStorageChanged(command); break;
            case "CONTAINER": await ScanPptContainer(); break;
            case "SCAN_LOCATION":
                if (string.IsNullOrEmpty(_storageMethod)) OnStorageChanged("LOCATION");
                if (RequiresContainer && !ContainerScanned) await ScanPptContainer();
                _locationNo = "FG-PPT-A1"; await ScanLocation(); break;
        }
    }
}
