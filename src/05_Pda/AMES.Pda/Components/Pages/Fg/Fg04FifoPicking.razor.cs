namespace AMES.Pda.Components.Pages.Fg;

public partial class Fg04FifoPicking
{
    private bool IsDetailedTestMode => PdaScenarioUsers.IsDetailed(Auth?.Session?.EmployeeNo);
    private bool IsPptTestMode => IsDetailedTestMode || PdaScenarioUsers.IsSimple(Auth?.Session?.EmployeeNo);
    private string ScenarioModeLabel => IsDetailedTestMode ? "TEST MODE" : "PPT CHECK";
    private bool _pptOpen;

    private static readonly PptScenarioPanel.Step[] PptSteps =
    [
        new("Outbound Unit", "팔렛 또는 대형 단품 바코드를 스캔해 실제 출고 대상을 확인합니다.",
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
