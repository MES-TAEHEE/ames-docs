using AMES.Data.Repositories;
using Xunit;

namespace AMES.Data.Tests;

// PP 단위테스트 결과서 FAIL(10-29) — PP003-TC-032 · PP006-TC-017·028 · PPOTD-TC-015
public sealed class PpFailFixes1029Tests
{
    static string Read(string path,[System.Runtime.CompilerServices.CallerFilePath]string file="")
        =>File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!,"../../..",path)));

    static WorkOrderRepository.RoutingStepPreview Step(bool required, string[] candidates, string[]? noPrefix)
        => new(1, "INJ", null, required,
               candidates.Select(c => new WorkOrderRepository.LineOption(c, c)).ToList(),
               NoPrefixLines: noPrefix);

    [Fact]
    public void Step_whose_lines_all_lack_a_lot_prefix_is_blocked_even_without_a_chosen_line()
    {
        var onlyNoPrefix = Step(true, [], ["LINE-INJ-09"]);
        Assert.True(onlyNoPrefix.OnlyNoPrefixLines);
        Assert.True(onlyNoPrefix.BlockedByPrefix(null));
        Assert.True(onlyNoPrefix.BlockedByPrefix(""));
        Assert.True(onlyNoPrefix.BlockedByPrefix("LINE-INJ-09"));

        var mixed = Step(true, ["LINE-INJ-01"], ["LINE-INJ-09"]);
        Assert.False(mixed.OnlyNoPrefixLines);
        Assert.False(mixed.BlockedByPrefix("LINE-INJ-01"));
        Assert.True(mixed.BlockedByPrefix("LINE-INJ-09"));

        // 라인이 아예 없는 공정(라인 불필요)·접두어 문제가 아닌 빈 후보는 LOT PREFIX 사유가 아니다
        Assert.False(Step(false, [], null).BlockedByPrefix(null));
        Assert.False(Step(true, [], null).BlockedByPrefix(null));
    }

    [Fact]
    public void Plan_confirm_dialog_excludes_lot_prefix_rows_and_counts_exclusions_by_reason()
    {
        var dlg = Read("src/06_Web/AMES.Web/Components/Pages/Pp/PlanConfirmBatchDialog.razor");
        Assert.Contains("public bool Excluded => NoMold || NoCore || NoLotPrefix;", dlg);
        Assert.Contains("NoLotPrefix = template.Any(t => t.OnlyNoPrefixLines)", dlg);
        Assert.Contains("PpRepository.RejectNoLotPrefix", dlg);

        var page = Read("src/06_Web/AMES.Web/Components/Pages/Pp/PlanConfirm.razor");
        Assert.Contains("PP.PlanConfirm.Ok.Rejected.NoLotPrefix", page);

        var repo = Read("src/02_Data/AMES.Data/Repositories/PpRepository.cs");
        Assert.Contains("t.BlockedByPrefix(plan.Steps", repo);
    }

    [Fact]
    public void Po_approval_needs_only_the_approve_permission()
    {
        var src = Read("src/06_Web/AMES.Web/Components/Pages/Pp/PurchaseReq.razor");
        int run = src.IndexOf("void Run(Action action", StringComparison.Ordinal);
        var runBody = src[run..src.IndexOf("void SendOne()", run, StringComparison.Ordinal)];
        Assert.DoesNotContain("_canEdit", runBody);
        Assert.Contains("if (_sel is null || !_canApprove) return;", src);
        Assert.Contains("void SendOne()\n    {\n        if (_sel is null || !_canEdit) return;", src.Replace("\r\n", "\n"));
        Assert.Contains("void FailOne()\n    {\n        if (_sel is null || !_canEdit) return;", src.Replace("\r\n", "\n"));
        Assert.Contains("void SaveVendor()\n    {\n        if (_sel is null || !_canEdit) return;", src.Replace("\r\n", "\n"));
    }

    [Theory]
    [InlineData(100, 0, 0)]
    [InlineData(100, 100, 100)]
    [InlineData(100, 110, 110)]
    public void Otd_progress_is_not_capped_at_100(int order, int shipped, int expected)
    {
        var r = new PpRepository.OtdRow(1, "SO", 1, null, null, null, null, "ITEM", null, null,
                                         order, shipped, null, null, null, 0, null, "Open");
        Assert.Equal(expected, r.ProgressPct);
    }

    [Fact]
    public void Otd_progress_without_order_quantity_has_no_ratio()
    {
        var r = new PpRepository.OtdRow(1, "SO", 1, null, null, null, null, "ITEM", null, null,
                                         0, 5, null, null, null, 0, null, "Open");
        Assert.Null(r.ProgressPct);
        Assert.Contains("Math.Min(100m, r.ProgressPct ?? 0m)", Read("src/06_Web/AMES.Web/Components/Pages/Pp/Delivery.razor"));
    }
}
