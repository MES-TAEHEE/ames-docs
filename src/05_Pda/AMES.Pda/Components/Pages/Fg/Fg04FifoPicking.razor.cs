namespace AMES.Pda.Components.Pages.Fg;

public partial class Fg04FifoPicking
{
    private bool IsDetailedTestMode => PdaScenarioUsers.IsDetailed(Auth?.Session?.EmployeeNo);
    private bool IsPptTestMode => IsDetailedTestMode || PdaScenarioUsers.IsSimple(Auth?.Session?.EmployeeNo);
    private string ScenarioModeLabel => IsDetailedTestMode ? "TEST MODE" : "PPT CHECK";
    private bool _pptOpen;

    private static readonly PptScenarioPanel.Step[] PptSteps =
    [
        new("Outbound Unit", "DCR을 선택한 뒤 팔렛·박스·개별 LOT를 스캔하여 출하 대기로 준비합니다. 실제 출하는 Trailer Loading의 Confirm Shipment에서 확정합니다.",
            new PptScenarioPanel.Value("Sample Pallet", SamplePalletLot, "PALLET"),
            new PptScenarioPanel.Value("Sample Large Part", SamplePartLot, "PART"))
    ];

    private async Task ResetPptData()
    {
        if (!IsPptTestMode || _isBusy) return;
        await Api.FgResetPptTestAsync("outbound");
        _modalOpen = false;
        Clear();
    }

    private async Task StartPptStep(int _)
    {
        if (!IsPptTestMode || _isBusy) return;
        await ResetPptData();
        _barcode = SamplePalletLot;
        await ScanOutboundUnit();
    }

    private async Task RunPptValue(string command)
    {
        if (command is not ("PALLET" or "PART")) return;
        await ResetPptData();
        _barcode = command == "PALLET" ? SamplePalletLot : SamplePartLot;
        await ScanOutboundUnit();
    }
}
