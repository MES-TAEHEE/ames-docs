namespace AMES.Pda.Components.Pages.Wh;

public partial class Wh03InventoryStatus
{
    private const string PptAdjustLot = "5011LL260908830001";
    private static readonly PptScenarioPanel.Step[] PptInventorySteps =
    [
        new("By Location", "재고가 있는 Location·Qty 목록을 확인하고 REFRESH와 CLEAR를 눌러봅니다.", new PptScenarioPanel.Value("검색", "PPT-WH-INV-01", "SEARCH")),
        new("Location Parts", "위치를 열어 Part No·Part Name·Qty를 확인하고 품목을 선택합니다.", new PptScenarioPanel.Value("LOCATION", "B0-10-A1", "LOCATION")),
        new("Location LOT Details", "선택한 위치·품목의 LOT No, Qty·Unit, Location을 확인합니다.", new PptScenarioPanel.Value("PART", "PPT-WH-INV-01", "LOCATION_LOTS")),
        new("By Part", "Part 기준으로 품번·품명·수량·위치가 표시되는지 확인합니다.", new PptScenarioPanel.Value("PART", "PPT-WH-INV-01", "PART")),
        new("Part LOT Details", "Part를 선택해 LOT별 수량·단위·위치를 확인하고 BACK으로 돌아옵니다.", new PptScenarioPanel.Value("LOT 상세", "PPT-WH-INV-01", "PART_LOTS"))
    ];
    private static readonly PptScenarioPanel.Step[] PptAdjustSteps =
    [
        new("LOT 스캔", "LOT을 스캔해 품목·수량·위치를 조회합니다.", new PptScenarioPanel.Value("LOT", PptAdjustLot, "SCAN")),
        new("재고·수량 조정", "변경 후 수량을 정수로 입력하거나 −/+로 1씩 조절합니다.", new PptScenarioPanel.Value("수량", "+3", "PLUS"), new PptScenarioPanel.Value("수량", "−3", "MINUS")),
        new("조정 정보 입력", "수량·사유·Note를 확인하고 CLEAR로 입력 초기화를 검수합니다.", new PptScenarioPanel.Value("입력 예시", "+3 / Damaged / Note", "READY")),
        new("사유 선택", "Reason 목록의 Count Diff·Damaged·Lost·Found·Other와 선택값을 확인합니다.", new PptScenarioPanel.Value("REASON", "Damaged", "REASON")),
        new("정수 입력", "정수를 직접 입력하면 별도 Supervisor 승인 없이 SAVE가 활성화됩니다. 관리자 권한이 필요합니다.", new PptScenarioPanel.Value("수량", "15", "QTY_VALID")),
        new("입력 검증", "소수·음수·빈값 입력 시 SAVE가 비활성화되는지 확인합니다.", new PptScenarioPanel.Value("소수", "1.5", "QTY_DECIMAL"), new PptScenarioPanel.Value("음수", "-1", "QTY_NEGATIVE"), new PptScenarioPanel.Value("빈값", "CLEAR", "QTY_EMPTY")),
        new("저장 완료", "이 단계 시작 후 SAVE를 눌러 Saved 알림과 전체 입력 초기화를 확인합니다.", new PptScenarioPanel.Value("이력 확인", "Transactions", "HISTORY"))
    ];
    private static readonly PptScenarioPanel.Step[] PptFgAdjustSteps = PptAdjustSteps.Select((step, index) => index switch
    {
        0 => step with { Guide = "완제품 LOT을 스캔합니다. 자재 LOT은 Warehouse Adjust 안내로 차단되어야 합니다.", Values = [new("FG LOT", "5011FG260908960001", "SCAN"), new("WH LOT", PptAdjustLot, "WH_LOT")] },
        6 => step with { Values = [new("이력 확인", "TRANSACTIONS", "HISTORY")] },
        _ => step
    }).ToArray();
    private bool IsDetailedFgTestMode => IsFinishedGoodsAdjust
        && string.Equals(Auth?.Session?.EmployeeNo, "TEST", StringComparison.OrdinalIgnoreCase);
    private bool IsPptTestMode => IsDetailedFgTestMode
        || string.Equals(Auth?.Session?.EmployeeNo, "TEST1", StringComparison.OrdinalIgnoreCase);
    private PptScenarioPanel.Step[] ActivePptSteps => IsDetailedFgTestMode
        ? FgDetailedScenarioCatalog.Adjust(PptFgAdjustSteps)
        : IsFinishedGoodsAdjust ? PptFgAdjustSteps : IsAdjustTab ? PptAdjustSteps : PptInventorySteps;
    private string ScenarioModeLabel => IsDetailedFgTestMode ? "TEST MODE" : "PPT CHECK";
    private bool _pptOpen;
    private readonly HashSet<string> _pptReadyScreens = [];
    private async Task EnsurePptData()
    {
        var screen = IsFinishedGoodsAdjust ? "fg-adjust" : IsAdjustTab ? "adjust" : "inventory";
        if (!_pptReadyScreens.Contains(screen))
        {
            if (IsFinishedGoodsAdjust) await Api.FgResetPptTestAsync("adjust");
            else await Api.WhResetPptTestAsync(screen);
            _pptReadyScreens.Add(screen);
        }
    }
    private async Task ResetPptData()
    {
        if (!IsPptTestMode || InventoryBusy || _isLoading) return;
        var screen = IsFinishedGoodsAdjust ? "fg-adjust" : IsAdjustTab ? "adjust" : "inventory";
        if (IsFinishedGoodsAdjust) await Api.FgResetPptTestAsync("adjust");
        else await Api.WhResetPptTestAsync(screen);
        _pptReadyScreens.Add(screen);
        if (IsAdjustTab) ClearInventoryWork();
        else await PreparePptInventory(1);
        ShowAlert("Test Data Ready", IsAdjustTab ? "조정용 LOT을 10 EA로 초기화했습니다." : "조회용 LOT 3개를 초기화했습니다.", "success");
    }
    private async Task PreparePptInventory(int step)
    {
        _workTab = "Search";
        _dateFrom = null;
        _dateTo = null;
        _statusFilter = "ALL";
        _q = "";
        _locationQuery = "";
        _matchedLocationIds = null;
        CloseBrowsePart();
        CloseLocationInventory();
        _inventoryView = step >= 4 ? "Part" : "Location";
        if (step >= 4) _q = "PPT-WH-INV-01";
        await Load();
        if (step is 2 or 3)
        {
            await OpenInventoryLocation("B0-10-A1");
            if (step == 3)
            {
                var part = SelectedLocationParts.FirstOrDefault(row => row.PartNo == "PPT-WH-INV-01")
                    ?? throw new InvalidOperationException("Inventory test part was not found.");
                OpenLocationPart(part);
            }
        }
        if (step == 5)
        {
            var part = BrowsePartRows.FirstOrDefault(row => row.PartNo == "PPT-WH-INV-01")
                ?? throw new InvalidOperationException("Inventory test part was not found.");
            await OpenBrowsePart(part);
        }
    }
    private async Task ScanPptAdjust()
    {
        ClearInventoryWork();
        _invBarcode = IsFinishedGoodsAdjust ? "5011FG260908960001" : PptAdjustLot;
        await ScanInventoryLot();
        if (_invScan is null) throw new InvalidOperationException(_invMsg);
    }
    private async Task StartPptStep(int step)
    {
        if (!IsPptTestMode || InventoryBusy || _isLoading) return;
        await EnsurePptData();
        if (!IsAdjustTab) { await PreparePptInventory(step); return; }
        await ScanPptAdjust();
        if (step >= 3)
        {
            _invAdjustDelta = 3;
            _invAdjustReason = "DAMAGED";
            _invAdjustNote = "PPT adjustment test";
        }
        if (step == 5) OnAdjustmentQuantityInput(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "15" });
        if (step == 6) OnAdjustmentQuantityInput(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "1.5" });
    }
    private async Task RunPptValue(string command)
    {
        if (!IsPptTestMode || InventoryBusy || _isLoading) return;
        await EnsurePptData();
        if (command == "HISTORY") { Nav.NavigateTo(IsFinishedGoodsAdjust ? "/fg/history" : "/wh/08"); return; }
        if (command == "WH_LOT" && IsFinishedGoodsAdjust)
        {
            ClearInventoryWork();
            _invBarcode = PptAdjustLot;
            await ScanInventoryLot();
            return;
        }
        if (!IsAdjustTab)
        {
            var step = command switch { "LOCATION" => 2, "LOCATION_LOTS" => 3, "PART" => 4, "PART_LOTS" => 5, _ => 1 };
            await PreparePptInventory(step);
            if (command == "SEARCH") { _locationQuery = "PPT-WH-INV-01"; await LoadLocationInventory(); }
            return;
        }
        if (_invScan is null || command == "SCAN") await ScanPptAdjust();
        switch (command)
        {
            case "PLUS": _invAdjustDelta = 3; break;
            case "MINUS": _invAdjustDelta = Math.Max(-InventoryBeforeQty, -3); break;
            case "READY": await StartPptStep(3); break;
            case "REASON": _invAdjustReason = "DAMAGED"; break;
            case "QTY_VALID": OnAdjustmentQuantityInput(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "15" }); break;
            case "QTY_DECIMAL": OnAdjustmentQuantityInput(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "1.5" }); break;
            case "QTY_NEGATIVE": OnAdjustmentQuantityInput(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "-1" }); break;
            case "QTY_EMPTY": OnAdjustmentQuantityInput(new Microsoft.AspNetCore.Components.ChangeEventArgs { Value = "" }); break;
        }
    }
}
