namespace AMES.Pda.Components.Pages.Fg;

public partial class Fg01QcComplete
{
    private bool IsDetailedTestMode => string.Equals(Auth?.Session?.EmployeeNo, "TEST", StringComparison.OrdinalIgnoreCase);
    private bool IsPptTestMode => IsDetailedTestMode || string.Equals(Auth?.Session?.EmployeeNo, "TEST1", StringComparison.OrdinalIgnoreCase);
    private PptScenarioPanel.Step[] ActiveScenarioSteps => IsDetailedTestMode ? FgDetailedScenarioCatalog.Qc(PptSteps) : PptSteps;
    private string ScenarioModeLabel => IsDetailedTestMode ? "TEST MODE" : "PPT CHECK";
    private bool _pptReady;
    private static readonly PptScenarioPanel.Step[] PptSteps =
    [
        new("QC 대기 목록", "이 단계 시작 후 PPT-FG-QC 품목의 LOT·품번·품명·수량과 Total/초과 대기 건수를 확인합니다."),
        new("경과일·정렬·새로고침", "당일·2일·6일·11일 대기 샘플의 색상과 오래된 순 정렬을 확인하고 REFRESH를 누릅니다.", new PptScenarioPanel.Value("REFRESH", "최신 대기 목록", "REFRESH"))
    ];
    private async Task ResetPptData()
    {
        if (!IsPptTestMode || _isLoading) return;
        await Api.FgResetPptTestAsync("qc");
        _pptReady = true;
        await Load();
    }
    private async Task StartPptStep(int step)
    {
        if (!IsPptTestMode || _isLoading) return;
        if (!_pptReady) await ResetPptData();
        else await Load();
    }
    private Task RunPptValue(string command) => StartPptStep(2);
}
