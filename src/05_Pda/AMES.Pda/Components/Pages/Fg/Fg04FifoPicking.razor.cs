namespace AMES.Pda.Components.Pages.Fg;

public partial class Fg04FifoPicking
{
    private bool IsDetailedTestMode => PdaScenarioUsers.IsDetailed(Auth?.Session?.EmployeeNo);
    private bool IsPptTestMode => IsDetailedTestMode || PdaScenarioUsers.IsSimple(Auth?.Session?.EmployeeNo);
    private string ScenarioModeLabel => IsDetailedTestMode ? "TEST MODE" : "PPT CHECK";
    private bool _pptOpen;

    private static readonly PptScenarioPanel.Step[] PptSteps =
    [
        new("Pallet Outbound", "Sample pallet LOT을 스캔해 내부 파트 목록과 전체 Outbound 처리를 확인합니다.",
            new PptScenarioPanel.Value("Sample Pallet", SamplePalletLot, "PALLET"))
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
        await ScanPallet();
    }

    private async Task RunPptValue(string command)
    {
        if (command == "PALLET") await StartPptStep(1);
    }
}
