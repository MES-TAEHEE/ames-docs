using AMES.Data.Services.Upload;
using Xunit;

namespace AMES.Data.Tests;

// 수주 수동 업로드 전 검사 — 같은 수주 번호에 이미 WO 가 있을 때의 판정
public class UploadWoConflictRulesTests
{
    [Theory]
    [InlineData(WoConflictLevel.None)]
    public void No_work_order_is_not_a_conflict(WoConflictLevel expected)
        => Assert.Equal(expected, UploadWoConflictRules.Classify([]));

    [Fact]
    public void Open_work_order_asks_for_a_new_number()
        => Assert.Equal(WoConflictLevel.Issued, UploadWoConflictRules.Classify(["Released", "In Progress"]));

    [Theory]
    [InlineData("Closed")]
    [InlineData("Completed")]
    [InlineData("Stocked")]
    public void Finished_work_order_blocks_the_row(string status)
        => Assert.Equal(WoConflictLevel.Completed, UploadWoConflictRules.Classify(["Released", status]));

    [Fact]
    public void Cancelled_work_orders_are_ignored()
        => Assert.Equal(WoConflictLevel.None, UploadWoConflictRules.Classify(["Cancelled"]));

    [Fact]
    public void ByOrder_groups_per_line_and_lists_work_orders()
    {
        var r = UploadWoConflictRules.ByOrder([
            new("SO-1", 10, "WO-A", "Closed"), new("SO-1", 20, "WO-B", "Released"), new("SO-1", 20, "WO-C", "Cancelled")]);
        Assert.Equal(WoConflictLevel.Completed, r[UploadWoConflictRules.Key("SO-1", 10)].Level);
        Assert.Equal(WoConflictLevel.Issued, r[UploadWoConflictRules.Key("so-1", 20)].Level);
        Assert.Equal(2, r[UploadWoConflictRules.Key("SO-1", 20)].WoNumbers.Count);
    }
}
