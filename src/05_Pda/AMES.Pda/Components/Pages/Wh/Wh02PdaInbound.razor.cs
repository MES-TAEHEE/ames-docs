namespace AMES.Pda.Components.Pages.Wh;

public partial class Wh02PdaInbound
{
    private static readonly InboundTestScenario[] SimpleTestScenarios =
    [
        new(1, "PPT 01", "Initial Screen", null, "LOCAL 또는 CKD를 선택하고 Scan Barcode 입력 영역을 확인합니다.", "", false),
        new(2, "PPT 02", "Barcode Scan", null, "LOT/BOX 단건과 Delivery Note 안의 미입고 BOX 목록이 조회되는지 확인합니다.", "", false),
        new(3, "PPT 03", "Delivery Note Status", null, "Delivery Note를 스캔해 BOX별 입고 상태를 확인합니다. Receive 버튼은 표시되지 않습니다.", "", false),
        new(4, "PPT 04", "Box Receive Ready", null, "미입고 BOX를 스캔해 단건 Receive 버튼이 표시되는지 확인합니다.", "", false),
        new(5, "PPT 05", "Receive Complete", null, "BOX 단건 RECEIVE 후 재고와 Transactions에 입고가 반영되는지 확인합니다.", "", false)
    ];

    private string _simpleMode = "LOCAL";
    private bool _simpleDataReady;
    private bool _simpleBusy;
    private string SimpleDocument => _simpleMode == "CKD" ? "PPT-WH-NOTE-CKD" : "PPT-WH-NOTE-LOCAL";
    private string[] SimpleBoxes => _simpleMode == "CKD"
        ? ["CKD260908800000001", "CKD260908800000002", "CKD260908800000003"]
        : ["5011LL260908800001", "5011LL260908800002", "5011LL260908800003"];

    private InboundTestValue[] SimpleTestValues() => CurrentTestScenario.No switch
    {
        2 => [new("LOT / BOX", SimpleBoxes[0]), new("DELIVERY NOTE", SimpleDocument), new("미등록 바코드", "WH-PPT-UNKNOWN")],
        3 => [new("DELIVERY NOTE", SimpleDocument)],
        4 or 5 => [new("BOX", SimpleBoxes[0])],
        _ => []
    };

    private void SelectSimpleMode(string mode)
    {
        if (_simpleBusy || IsBusy) return;
        _simpleMode = mode;
        ResetAllForms();
        _mode = mode;
    }

    private async Task<bool> EnsureSimpleTestData()
    {
        if (_simpleDataReady) return true;
        try
        {
            await Api.WhResetSimpleInboundTestAsync();
            _simpleDataReady = true;
            return true;
        }
        catch (Exception ex)
        {
            ShowAlert("Test Data Unavailable", ex.Message, "danger");
            return false;
        }
    }

    private async Task ResetSimpleTestData()
    {
        if (!IsSimpleTestMode || _simpleBusy || IsBusy) return;
        _simpleBusy = true;
        CloseTestPanel();
        try
        {
            _simpleDataReady = false;
            if (!await EnsureSimpleTestData()) return;
            ResetAllForms();
            _mode = "";
            _testScenarioIndex = 0;
            _simulateReceiveApiFailure = false;
            ShowAlert("Test Data Ready", "LOCAL / CKD 각 3개 BOX를 미입고 상태로 초기화했습니다.", "success");
        }
        finally { _simpleBusy = false; }
    }

    private async Task StartSimpleTestScenario()
    {
        if (!IsSimpleTestMode || _simpleBusy || IsBusy) return;
        _simpleBusy = true;
        CloseTestPanel();
        try
        {
            if (!await EnsureSimpleTestData()) return;
            ResetAllForms();
            _mode = CurrentTestScenario.No == 1 ? "" : _simpleMode;
            if (CurrentTestScenario.No <= 2) return;
            _barcode = SimpleDocument;
            await Scan();
            if (_document is null || _modalOpen) return;
            if (_document.Yn == "Y")
            {
                ShowAlert("Test Data Reset Required", "이미 입고된 데이터입니다. SCENARIO에서 테스트 데이터를 초기화해 주세요.", "info");
                return;
            }
            if (CurrentTestScenario.No < 4) return;
            foreach (var barcode in SimpleBoxes)
            {
                _barcode = barcode;
                await Scan();
                if (_modalOpen) return;
            }
        }
        finally { _simpleBusy = false; }
    }

    private async Task LoadSimpleTestValue(InboundTestValue value)
    {
        if (!IsSimpleTestMode || _simpleBusy || IsBusy) return;
        _simpleBusy = true;
        CloseTestPanel();
        try
        {
            if (!await EnsureSimpleTestData()) return;
            if (CurrentTestScenario.No == 2)
            {
                ResetAllForms();
                _mode = _simpleMode;
            }
            if (value.IsLocation)
            {
                _loc = value.Value;
                _selectedLocation = null;
                await ScanLocation();
            }
            else
            {
                if (CurrentTestScenario.No == 3 && _document is null)
                {
                    _mode = _simpleMode;
                    _barcode = SimpleDocument;
                    await Scan();
                    if (_document is null || _modalOpen) return;
                }
                _barcode = value.Value;
                await Scan();
            }
        }
        finally { _simpleBusy = false; }
    }
}
