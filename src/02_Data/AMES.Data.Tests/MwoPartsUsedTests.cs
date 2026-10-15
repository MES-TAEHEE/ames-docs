using AMES.Data.Services;
using Xunit;
using L = AMES.Data.Services.MwoPartsUsed.Line;

namespace AMES.Data.Tests;

// 10-15 사용자 결정 (b) — 부품 차감은 PDA 출고, 웹은 그 출고를 작업지시의 사용 부품으로 기록만 한다
public class MwoPartsUsedTests
{
    [Fact]
    public void Merge_sums_the_same_part_keeps_first_names_and_orders_by_part_no()
    {
        var m = MwoPartsUsed.Merge(new[]
        {
            new L("SP-B", 1, "PB", "B"), new L(" sp-a ", 1, "PA", "A"), new L("SP-B", 1, "PB2", "B2"), new L("SP-A", 1),
            new L("", 1), new L("SP-C", 0),
        });
        Assert.Equal(new[] { ("sp-a", 2, "PA"), ("SP-B", 2, "PB") }, m.Select(l => (l.SparePartNo, l.Qty, l.PartNo!)));
        Assert.Empty(MwoPartsUsed.Merge(null));
    }

    [Fact]
    public void Serialized_lines_round_trip_and_describe_with_snapshot_names()
    {
        var json = MwoPartsUsed.Serialize(new[] { new L("EOS-SP-C9-260001", 2, "SP-HTR-2KW", "히터 밴드"), new L("EOS-SP-X", 1) });
        Assert.Contains("히터 밴드", json);   // 한글 이스케이프 없이
        Assert.Equal(2, MwoPartsUsed.Parse(json).Count);
        Assert.Equal("SP-HTR-2KW 히터 밴드 × 2, EOS-SP-X × 1", MwoPartsUsed.Describe(json));
        Assert.Null(MwoPartsUsed.Serialize(Array.Empty<L>()));
    }

    [Theory]
    [InlineData("[\"VLV-12\",\"SEAL-3\"]", "VLV-12, SEAL-3")]   // 옛 자유 입력
    [InlineData("not json", "not json")]
    [InlineData(null, "")]
    [InlineData("  ", "")]
    public void Legacy_and_invalid_values_are_shown_as_text(string? json, string expected)
    {
        Assert.Equal(expected, MwoPartsUsed.Describe(json));
        Assert.Empty(MwoPartsUsed.Parse(json));
    }
}
