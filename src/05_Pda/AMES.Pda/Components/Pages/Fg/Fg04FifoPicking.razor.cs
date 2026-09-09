namespace AMES.Pda.Components.Pages.Fg;

public partial class Fg04FifoPicking
{
    private const string PptSlip = "2609089001";
    private static readonly string[] PptLots = ["5011FG260908930001", "5011FG260908930002", "5011FG260908930003"];
    private bool IsDetailedTestMode => string.Equals(Auth?.Session?.EmployeeNo, "TEST", StringComparison.OrdinalIgnoreCase);
    private bool IsPptTestMode => IsDetailedTestMode || string.Equals(Auth?.Session?.EmployeeNo, "TEST1", StringComparison.OrdinalIgnoreCase);
    private PptScenarioPanel.Step[] ActiveScenarioSteps => IsDetailedTestMode ? FgDetailedScenarioCatalog.Picking(PptSteps) : PptSteps;
    private string ScenarioModeLabel => IsDetailedTestMode ? "TEST MODE" : "PPT CHECK";
    private bool _pptOpen, _pptReady;
    private static readonly PptScenarioPanel.Step[] PptSteps =
    [
        new("Outgoing Slip Scan", "출고전표의 고객·목적지·출고일과 24/16 EA 요구수량을 확인합니다.", new PptScenarioPanel.Value("출고전표", PptSlip, "SLIP"), new PptScenarioPanel.Value("전표 없이 LOT", PptLots[0], "LOT_FIRST"), new PptScenarioPanel.Value("없는 출고전표", "2609089999", "UNKNOWN_SLIP")),
        new("Partial LOT Scan", "첫 LOT 10 EA를 스캔한 PARTIAL 10/24와 COMPLETE 비활성화를 확인합니다.", new PptScenarioPanel.Value("LOT 1 · 10 EA", PptLots[0], "LOT1"), new PptScenarioPanel.Value("LOT 2 · 14 EA", PptLots[1], "LOT2")),
        new("Part Scan Progress", "첫 파트는 10/24 PARTIAL, 두 번째는 16/16 CHECKED·초록색인지 확인합니다.", new PptScenarioPanel.Value("LOT 3 · 16 EA", PptLots[2], "LOT3")),
        new("All Parts Scanned", "모두 초록색이고 COMPLETE가 활성화되는지 확인합니다. CANCEL 후 재스캔 시 0부터 시작합니다.", new PptScenarioPanel.Value("CANCEL 후 재조회", PptSlip, "SLIP")),
        new("FIFO Validation", "늦은 LOT을 먼저 스캔해 오류창의 선행 LOT·Location 안내를 확인합니다.", new PptScenarioPanel.Value("FIFO 위반", PptLots[1], "FIFO")),
        new("Release Complete", "이 단계 시작 후 화면의 COMPLETE를 눌러 완료 알림·초기화와 Loading 가능 상태를 확인합니다.")
    ];
    private async Task ResetPptData()
    {
        if (!IsPptTestMode || _isBusy) return;
        await Api.FgResetPptTestAsync("release");
        _pptReady = true;
        _modalOpen = false;
        Clear();
    }
    private async Task PreparePptSlip()
    {
        _modalOpen = false;
        Clear();
        _slipBarcode = PptSlip;
        await LoadOutgoingSlip();
        if (_selectedSlip is null) throw new InvalidOperationException("출고 완료된 샘플은 테스트 데이터 초기화 후 다시 시작하세요. " + _modalMessage);
    }
    private async Task ScanPptLot(int index, bool dismiss = true)
    {
        _modalOpen = false;
        _lotBarcode = PptLots[index];
        await ScanLot();
        if (dismiss && _modalKind == "success") _modalOpen = false;
    }
    private async Task StartPptStep(int step)
    {
        if (!IsPptTestMode || _isBusy) return;
        if (!_pptReady) await ResetPptData();
        await PreparePptSlip();
        if (step == 5) { await ScanPptLot(1, false); return; }
        if (step >= 2) await ScanPptLot(0);
        if (step >= 3) await ScanPptLot(2);
        if (step >= 4) await ScanPptLot(1);
    }
    private async Task RunPptValue(string command)
    {
        if (!IsPptTestMode || _isBusy) return;
        if (!_pptReady) await ResetPptData();
        _modalOpen = false;
        if (command is "LOT_FIRST" or "UNKNOWN_SLIP")
        {
            Clear();
            _slipBarcode = command == "LOT_FIRST" ? PptLots[0] : "2609089999";
            await LoadOutgoingSlip();
            return;
        }
        if (command == "FIFO") { await StartPptStep(5); return; }
        if (command == "SLIP" || _selectedSlip is null) await PreparePptSlip();
        if (command.StartsWith("LOT")) await ScanPptLot(int.Parse(command[3..]) - 1, false);
    }
}
