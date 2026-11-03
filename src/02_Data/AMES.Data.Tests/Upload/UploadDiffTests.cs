using AMES.Data.Services.Upload;
using Xunit;

namespace AMES.Data.Tests.Upload;

/// <summary>업로드 전후 비교 규칙(중복 업로드 감지·변경 내역). 순수 함수.</summary>
public class UploadDiffTests
{
    [Fact]
    public void Identical_data_has_no_changes_and_counts_everything_as_unchanged()
    {
        var old = new Dictionary<string, string> { ["A 2026-11-04"] = "예정 240 · P/O 240" };
        var diff = UploadDiffRules.Compare(old, new Dictionary<string, string>(old));

        Assert.False(diff.HasChanges);
        Assert.Equal(1, diff.Unchanged);
        Assert.Empty(diff.Lines);
    }

    [Fact]
    public void Changed_added_and_removed_are_reported_with_old_and_new_values()
    {
        var old = new Dictionary<string, string> { ["A 2026-11-04"] = "예정 240", ["B 2026-11-04"] = "예정 100" };
        var incoming = new Dictionary<string, string> { ["A 2026-11-04"] = "예정 250", ["C 2026-11-04"] = "예정 50" };

        var diff = UploadDiffRules.Compare(old, incoming);

        Assert.True(diff.HasChanges);
        Assert.Equal((1, 1, 1), (diff.Added, diff.Changed, diff.Removed));
        var changed = diff.Lines.Single(l => l.Kind == UploadDiffRules.Changed);
        Assert.Equal(("예정 240", "예정 250"), (changed.Old, changed.New));
        Assert.Equal(UploadDiffRules.Removed, diff.Lines.Single(l => l.Key == "B 2026-11-04").Kind);
        Assert.Equal(UploadDiffRules.Added, diff.Lines.Single(l => l.Key == "C 2026-11-04").Kind);
    }

    [Fact]
    public void Po_key_joins_order_and_line_only_when_line_exists()
    {
        Assert.Equal("4100181400-10", UploadDiffRules.PoKey("4100181400", 10));
        Assert.Equal("4100181400", UploadDiffRules.PoKey("4100181400", null));
    }
}
