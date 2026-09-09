namespace AMES.Pda.Components.Pages.Fg;

public partial class Fg05Loading
{
    private const string PptTruck = "TRUCK:PPT-FG-01", PptOrder = "FG-PPT-SO-LOAD";
    private static readonly string[] PptStocks = ["FG-PPT-STK-940001", "FG-PPT-STK-940002", "FG-PPT-STK-940003"];
    private bool IsDetailedTestMode => PdaScenarioUsers.IsDetailed(Auth?.Session?.EmployeeNo);
    private bool IsPptTestMode => IsDetailedTestMode || PdaScenarioUsers.IsSimple(Auth?.Session?.EmployeeNo);
    private PptScenarioPanel.Step[] ActiveScenarioSteps => IsDetailedTestMode ? FgDetailedScenarioCatalog.Loading(PptSteps) : PptSteps;
    private string ScenarioModeLabel => IsDetailedTestMode ? "TEST MODE" : "PPT CHECK";
    private bool _pptOpen, _pptReady;
    private static readonly PptScenarioPanel.Step[] PptSteps =
    [
        new("Truck Barcode Scan", "트럭을 먼저 스캔합니다. Shipment/Stock을 먼저 입력하면 트럭 형식 오류가 나야 합니다.", new PptScenarioPanel.Value("TRUCK", PptTruck, "TRUCK"), new PptScenarioPanel.Value("순서 오류", PptOrder, "WRONG_ORDER")),
        new("Shipment Order Scan", "트럭 VERIFIED와 Next Scan 전환을 확인하고 Shipment Order를 스캔합니다.", new PptScenarioPanel.Value("SHIPMENT ORDER", PptOrder, "ORDER")),
        new("Products To Load", "고객·목적지·출고일과 Stock 3건의 품번·수량·위치, 자동 스크롤을 확인합니다.", new PptScenarioPanel.Value("SHIPMENT ORDER", PptOrder, "ORDER")),
        new("Stock Scan Progress", "1/3 진행수량과 첫 행 초록색, CONFIRM 비활성화를 확인하고 남은 Stock을 스캔합니다.", new PptScenarioPanel.Value("STOCK 1 · 20 EA", PptStocks[0], "STOCK1"), new PptScenarioPanel.Value("STOCK 2 · 8 EA", PptStocks[1], "STOCK2"), new PptScenarioPanel.Value("STOCK 3 · 6 EA", PptStocks[2], "STOCK3")),
        new("All Stocks Scanned", "3/3과 모든 행 초록색, CONFIRM 활성화·완료 안내를 확인합니다."),
        new("Duplicate Scan Validation", "같은 Stock 재스캔 시 Already Scanned 알림이 뜨고 1/3 수량이 유지되는지 확인합니다.", new PptScenarioPanel.Value("중복 STOCK", PptStocks[0], "DUPLICATE")),
        new("Loading Complete", "이 단계 시작 후 화면의 CONFIRM을 눌러 3건·트럭 번호가 있는 완료 알림과 초기화를 확인합니다.")
    ];
    private async Task ResetPptData()
    {
        if (!IsPptTestMode || _isBusy) return;
        await Api.FgResetPptTestAsync("loading");
        _pptReady = true;
        _modalOpen = false;
        Clear();
    }
    private async Task PreparePptOrder()
    {
        _modalOpen = false;
        Clear();
        _truckBarcode = PptTruck;
        await ScanTruck();
        _orderBarcode = PptOrder;
        await ScanOrder();
        if (_order is null) throw new InvalidOperationException("적재 완료된 샘플은 테스트 데이터 초기화 후 다시 시작하세요. " + _modalMessage);
    }
    private async Task ScanPptStock(int index)
    {
        _modalOpen = false;
        _productBarcode = PptStocks[index];
        await ScanProduct();
    }
    private async Task StartPptStep(int step)
    {
        if (!IsPptTestMode || _isBusy) return;
        if (!_pptReady) await ResetPptData();
        _modalOpen = false;
        Clear();
        if (step == 1) return;
        if (step == 2) { _truckBarcode = PptTruck; await ScanTruck(); return; }
        await PreparePptOrder();
        if (step >= 4) await ScanPptStock(0);
        if (step == 6) { await ScanPptStock(0); return; }
        if (step >= 5) { await ScanPptStock(1); await ScanPptStock(2); }
    }
    private async Task RunPptValue(string command)
    {
        if (!IsPptTestMode || _isBusy) return;
        if (!_pptReady) await ResetPptData();
        _modalOpen = false;
        if (command == "DUPLICATE") { await StartPptStep(6); return; }
        if (command is "TRUCK" or "WRONG_ORDER")
        {
            Clear(); _truckBarcode = command == "TRUCK" ? PptTruck : PptOrder;
            await ScanTruck(); return;
        }
        if (command == "ORDER" || _order is null) await PreparePptOrder();
        if (command.StartsWith("STOCK")) await ScanPptStock(int.Parse(command[5..]) - 1);
    }
}
