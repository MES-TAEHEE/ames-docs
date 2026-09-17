using AMES.Data.Services.PoSync;
using Xunit;

namespace AMES.Data.Tests;

/// <summary>MM31006 INQUERY 커서 행 → PP_CustomerOrder 업서트 행. PONO 분리·제외 규칙·중복 처리의 정본.</summary>
public class SrmPoMapperTests
{
    static SrmPoRow Row(string pono = "4100172316-10", string part = "81720-PI000NNB",
        decimal qty = 216, decimal deli = 0, string? poDate = "2026-08-31", string? deliDate = "2026-09-01",
        string? loekz = null)
        => new(pono, poDate, deliDate, part, loekz, null, qty, deli);

    [Theory]
    [InlineData("4100172316-10", "4100172316", 10)]
    [InlineData("4100172316-120", "4100172316", 120)]
    [InlineData("4100172316", "4100172316", null)]
    [InlineData("41001-72316-AB", "41001-72316-AB", null)]
    [InlineData("  4100172316-10 ", "4100172316", 10)]
    [InlineData("4100172316-", "4100172316-", null)]
    public void SplitPoNo_splits_on_last_dash_only_when_tail_is_integer(string input, string so, int? line)
        => Assert.Equal((so, line), SrmPoMapper.SplitPoNo(input));

    [Fact]
    public void Map_copies_fields()
    {
        var r = SrmPoMapper.Map([Row(deli: 100)]);
        var m = Assert.Single(r.Rows);
        Assert.Equal(0, r.Skipped);
        Assert.Equal("4100172316", m.SoNumber);
        Assert.Equal(10, m.SoLineNo);
        Assert.Equal("81720-PI000NNB", m.ItemNo);
        Assert.Equal(216m, m.OrderQty);
        Assert.Equal(100m, m.ShippedQty);
        Assert.Equal(new DateTime(2026, 8, 31), m.OrderDate);
        Assert.Equal(new DateTime(2026, 9, 1), m.RequestedDeliveryDate);
    }

    [Theory]
    [InlineData("0000-00-00")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("2026/09/01")]
    public void Map_turns_unparseable_dates_into_null(string? d)
    {
        var m = Assert.Single(SrmPoMapper.Map([Row(poDate: d, deliDate: d)]).Rows);
        Assert.Null(m.OrderDate);
        Assert.Null(m.RequestedDeliveryDate);
    }

    [Fact]
    public void Map_skips_negative_qty_deleted_blank_pono_and_too_long()
    {
        var rows = new[]
        {
            Row(qty: -5),
            Row(loekz: "L"),
            Row(pono: ""),
            Row(pono: "   "),
            Row(pono: new string('9', 21) + "-10"),
            Row(part: new string('A', 21)),
            Row(pono: "4100172316-20"),          // 유일한 정상 행
        };
        var r = SrmPoMapper.Map(rows);
        Assert.Equal(6, r.Skipped);
        Assert.Equal(20, Assert.Single(r.Rows).SoLineNo);
    }

    [Fact]
    public void Map_keeps_delivery_complete_rows_and_zero_qty()
    {
        var r = SrmPoMapper.Map([new SrmPoRow("4100172316-10", "2026-08-31", "2026-09-01", "P1", null, "X", 0, 216)]);
        Assert.Single(r.Rows);
        Assert.Equal(0, r.Skipped);
    }

    [Fact]
    public void Map_last_duplicate_wins()
    {
        var r = SrmPoMapper.Map([Row(qty: 100), Row(qty: 250)]);
        Assert.Equal(250m, Assert.Single(r.Rows).OrderQty);
        Assert.Equal(0, r.Skipped);
    }

    [Fact]
    public void Map_treats_null_qty_as_zero()
    {
        var m = Assert.Single(SrmPoMapper.Map([new SrmPoRow("1-1", null, null, "P", null, null, null, null)]).Rows);
        Assert.Equal(0m, m.OrderQty);
        Assert.Equal(0m, m.ShippedQty);
    }

    [Fact]
    public void ParseRows_accepts_root_array_or_first_array_property()
    {
        const string arr = """[{"PONO":"1-1","PO_QTY":3}]""";
        const string obj = """{"status":"ok","OUT_CURSOR":[{"PONO":"2-1","PO_QTY":4}]}""";
        Assert.Equal("1-1", Assert.Single(SrmPoMapper.ParseRows(arr)).PONO);
        Assert.Equal("2-1", Assert.Single(SrmPoMapper.ParseRows(obj)).PONO);
        Assert.Throws<FormatException>(() => SrmPoMapper.ParseRows("""{"status":"ok"}"""));
        Assert.Throws<FormatException>(() => SrmPoMapper.ParseRows("not json"));
    }

    [Fact]
    public void Sample_file_maps_all_426_rows()
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "PO_7700_310471_EN_data.json"));
        var rows = SrmPoMapper.ParseRows(json);
        Assert.Equal(426, rows.Count);
        var r = SrmPoMapper.Map(rows);
        Assert.Equal(426, r.Rows.Count);
        Assert.Equal(0, r.Skipped);
        Assert.All(r.Rows, m => Assert.NotNull(m.SoLineNo));
        Assert.All(r.Rows, m => Assert.NotNull(m.RequestedDeliveryDate));
    }
}
