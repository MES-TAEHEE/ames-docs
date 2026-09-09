namespace AMES.Pda.Components.Pages.Wh;

public partial class Wh02PdaInbound
{
    private static readonly InboundTestScenario[] SimpleTestScenarios =
    [
        new(1, "PPT 01", "Initial Screen", null, "화면에서 LOCAL 또는 CKD를 선택하고 바코드 입력란을 확인합니다.", "", false),
        new(2, "PPT 02", "Barcode Scan", null, "단일 LOT/Box 정보와 Delivery Note/Case의 내부 품목 목록을 확인합니다.", "", false),
        new(3, "PPT 03", "Delivery Note / Case Boxes", null, "BOX 1~3을 차례로 눌러 초록색 표시, 스캔 수와 Location 활성화를 확인합니다.", "", false),
        new(4, "PPT 04", "Location Scan", null, "LOCATION을 눌러 Warehouse, Area, Zone, Rack과 입고 가능 상태를 확인합니다.", "", false),
        new(5, "PPT 05", "Receive Complete", null, "화면의 RECEIVE로 완료 알림을 확인합니다. API ERROR ON으로 오류도 검수합니다.", "", false)
    ];

    private string _simpleMode = "LOCAL";
    private bool _simpleDataReady;
    private bool _simpleBusy;
    private string SimpleDocument => _simpleMode == "CKD" ? "CKD202609080001CASE00001" : "5011202609088001";
    private string[] SimpleBoxes => _simpleMode == "CKD"
        ? ["CKD260908800000001", "CKD260908800000002", "CKD260908800000003"]
        : ["5011LL260908800001", "5011LL260908800002", "5011LL260908800003"];

    private InboundTestValue[] SimpleTestValues() => CurrentTestScenario.No switch
    {
        2 => [new("LOT / BOX", SimpleBoxes[0]), new(_simpleMode == "CKD" ? "CASE" : "DELIVERY NOTE", SimpleDocument), new("미등록 바코드", "WH-PPT-UNKNOWN")],
        3 => SimpleBoxes.Select((barcode, index) => new InboundTestValue($"BOX {index + 1}", barcode)).ToArray(),
        4 => [new("LOCATION", "WH010201", true), new("미등록 LOCATION", "WH999999", true)],
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
            ShowAlert("Test Data Ready", "LOCAL / CKD 각 3개 박스를 미입고 상태로 초기화했습니다.", "success");
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
                ShowAlert("Test Data Reset Required", "입고 완료된 데이터입니다. SCENARIO에서 테스트 데이터 초기화를 눌러주세요.", "info");
                return;
            }
            if (CurrentTestScenario.No < 4) return;
            foreach (var barcode in SimpleBoxes)
            {
                _barcode = barcode;
                await Scan();
                if (_modalOpen) return;
            }
            if (CurrentTestScenario.No == 5)
            {
                _loc = "WH010201";
                await ScanLocation();
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
