namespace AMES.Pda.Components.Pages.Wh;

public partial class Wh07PdaRelease
{
    private const string PptSlip = "PS-PPT-WH-01";
    private static readonly string[] PptLots = ["5011LL260908810001", "5011LL260908810002", "5011LL260908810003"];
    private static readonly PptScenarioPanel.Step[] PptSteps =
    [
        new("Initial Screen", "Pick Slip과 단독 LOT을 각각 눌러 출고 절차가 구분되는지 확인합니다.",
            new PptScenarioPanel.Value("PICK SLIP", PptSlip, "SLIP"), new PptScenarioPanel.Value("단독 LOT", "5011LL260908810004", "DIRECT")),
        new("Pick Slip Scan", "요청 품목 2개와 Box 수량, LOT·Location·생산일 순서를 확인합니다.",
            new PptScenarioPanel.Value("PICK SLIP", PptSlip, "SLIP")),
        new("LOT Scan", "LOT 1~3을 차례로 눌러 초록색 표시와 수량을 확인합니다. 전체 스캔 전 RELEASE는 비활성입니다.",
            new PptScenarioPanel.Value("LOT 1", PptLots[0], "LOT1"), new PptScenarioPanel.Value("LOT 2", PptLots[1], "LOT2"), new PptScenarioPanel.Value("LOT 3", PptLots[2], "LOT3")),
        new("FIFO Validation", "두 번째 LOT을 먼저 스캔해 차단 알림과 먼저 스캔할 LOT·Location을 확인합니다.",
            new PptScenarioPanel.Value("FIFO 오류", PptLots[1], "FIFO"), new PptScenarioPanel.Value("첫 LOT", PptLots[0], "LOT1")),
        new("Outgoing Type", "전체 스캔 후 Production Line·Other·Defect를 선택하고 RELEASE 활성화를 확인합니다.",
            new PptScenarioPanel.Value("TYPE", "Production Line", "PRODUCTION"), new PptScenarioPanel.Value("TYPE", "Other", "OTHER"), new PptScenarioPanel.Value("TYPE", "Defect", "DEFECT")),
        new("Release Complete", "이 단계 시작 후 화면의 RELEASE를 눌러 완료 알림과 화면 초기화를 확인합니다.",
            new PptScenarioPanel.Value("이력 확인", "Transactions", "HISTORY"))
    ];
    private bool IsPptTestMode => string.Equals(Auth?.Session?.EmployeeNo, "TEST1", StringComparison.OrdinalIgnoreCase);
    private bool _pptOpen;
    private bool _pptReady;
    private async Task EnsurePptData()
    {
        if (!_pptReady)
        {
            await Api.WhResetPptTestAsync("release");
            _pptReady = true;
        }
    }
    private async Task ResetPptData()
    {
        if (!IsPptTestMode || _isBusy) return;
        await Api.WhResetPptTestAsync("release");
        _pptReady = true;
        ClearRelease();
        ShowModal("Test Data Ready", "Pick Slip과 출고용 LOT 4개를 초기화했습니다.", "ok");
    }
    private async Task PreparePptSlip(bool allLots = false)
    {
        ClearRelease();
        await ProcessBarcodeAsync(PptSlip);
        if (!allLots || _modalOpen) return;
        foreach (var barcode in PptLots)
        {
            await ProcessBarcodeAsync(barcode);
            if (_modalOpen) return;
        }
    }
    private async Task StartPptStep(int step)
    {
        if (!IsPptTestMode || _isBusy) return;
        await EnsurePptData();
        if (step == 1) { ClearRelease(); return; }
        await PreparePptSlip(step >= 5);
        if (_modalOpen) return;
        if (step == 4) await ProcessBarcodeAsync(PptLots[1]);
        if (step == 6) OnOutgoingTypeChanged("PRODUCTION");
    }
    private async Task RunPptValue(string command)
    {
        if (!IsPptTestMode || _isBusy) return;
        await EnsurePptData();
        if (command == "HISTORY") { Nav.NavigateTo("/wh/08"); return; }
        if (command == "DIRECT") { ClearRelease(); await ProcessBarcodeAsync("5011LL260908810004"); return; }
        if (command is "SLIP" or "FIFO")
        {
            await PreparePptSlip();
            if (command == "FIFO" && !_modalOpen) await ProcessBarcodeAsync(PptLots[1]);
            return;
        }
        if (command is "PRODUCTION" or "OTHER" or "DEFECT")
        {
            if (!CanChooseOutgoingType) await PreparePptSlip(true);
            if (!_modalOpen) OnOutgoingTypeChanged(command);
            return;
        }
        if (_loadedSlipNo != PptSlip) await PreparePptSlip();
        if (!_modalOpen && command is "LOT1" or "LOT2" or "LOT3")
            await ProcessBarcodeAsync(PptLots[int.Parse(command[3..]) - 1]);
    }
}
