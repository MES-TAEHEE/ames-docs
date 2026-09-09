namespace AMES.Pda.Components.Pages.Fg;

public partial class FgRtnReturn
{
    private const string PptStock = "FG-PPT-STK-950001";
    private bool IsDetailedTestMode => string.Equals(Auth?.Session?.EmployeeNo, "TEST", StringComparison.OrdinalIgnoreCase);
    private bool IsPptTestMode => IsDetailedTestMode || string.Equals(Auth?.Session?.EmployeeNo, "TEST1", StringComparison.OrdinalIgnoreCase);
    private PptScenarioPanel.Step[] ActiveScenarioSteps => IsDetailedTestMode ? FgDetailedScenarioCatalog.Returns(PptSteps) : PptSteps;
    private string ScenarioModeLabel => IsDetailedTestMode ? "TEST MODE" : "PPT CHECK";
    private bool _pptOpen, _pptReady;
    private static readonly PptScenarioPanel.Step[] PptSteps =
    [
        new("Initial Screen", "스캔 전 화면과 RECEIVE 차단을 확인하고 반품 바코드를 스캔합니다.", new PptScenarioPanel.Value("반품 가능 STOCK", PptStock, "PRODUCT")),
        new("Product Validation", "품번·품명·고객·출하일시와 RETURN ELIGIBLE을 확인합니다. 사유 미선택 시 RECEIVE가 차단됩니다.", new PptScenarioPanel.Value("사유 미선택 오류", "RECEIVE", "NO_REASON"), new PptScenarioPanel.Value("미출하 제품", "FG-PPT-STK-950002", "NOT_SHIPPED")),
        new("Return Reason & Note", "아래로 펼쳐진 사유 5개 중 하나를 선택하고 Note를 왼쪽 위부터 입력합니다.", new PptScenarioPanel.Value("사유·노트 예시", "Damaged in transit", "REASON")),
        new("Return Received", "이 단계 시작 후 화면의 RECEIVE를 눌러 완료 알림·입력 초기화를 확인합니다.", new PptScenarioPanel.Value("반품 후 재스캔", PptStock, "PRODUCT"))
    ];
    private async Task ResetPptData()
    {
        if (!IsPptTestMode || _isBusy) return;
        await Api.FgResetPptTestAsync("return");
        _pptReady = true;
        _modalOpen = false;
        Clear();
    }
    private async Task PreparePptProduct()
    {
        _modalOpen = false;
        Clear();
        _barcode = PptStock;
        await LookupProduct();
    }
    private async Task StartPptStep(int step)
    {
        if (!IsPptTestMode || _isBusy) return;
        if (!_pptReady) await ResetPptData();
        _modalOpen = false;
        Clear();
        if (step == 1) return;
        await PreparePptProduct();
        if (_product is null) throw new InvalidOperationException("반품 완료된 샘플은 테스트 데이터 초기화 후 다시 시작하세요. " + _modalMessage);
        if (step == 3) _reasonOpen = true;
        if (step == 4) { SelectReason("Damaged in transit"); _note = "PPT return note"; }
    }
    private async Task RunPptValue(string command)
    {
        if (!IsPptTestMode || _isBusy) return;
        if (!_pptReady) await ResetPptData();
        if (command == "NOT_SHIPPED")
        {
            _modalOpen = false; Clear(); _barcode = "FG-PPT-STK-950002"; await LookupProduct(); return;
        }
        if (command == "PRODUCT" || _product is null) await PreparePptProduct();
        if (command == "NO_REASON") { _reason = null; await Submit(); }
        if (command == "REASON") { SelectReason("Damaged in transit"); _note = "PPT return note"; }
    }
}
