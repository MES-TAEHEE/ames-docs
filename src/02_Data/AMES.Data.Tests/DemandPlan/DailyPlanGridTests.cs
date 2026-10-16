using System.Text.Json;
using AMES.Data.Services.DemandPlan;
using Xunit;

namespace AMES.Data.Tests.DemandPlan;

/// <summary>SRM MM30011 엑셀 격자 → DailyPlan. 실측 파일(2026-09-29 내려받음)의 격자 JSON 이 정본.
/// 헤더 날짜가 DD/MM 로 뒤집혀 저장된 결함(10/01~10/12 → 1월 10일…12월 10일) 때문에 위치 앵커 규칙을 쓴다.</summary>
public class DailyPlanGridTests
{
    static readonly DateOnly Today = new(2026, 9, 29);

    static List<string[]> Grid()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "DemandPlan", "mip_jit_20260929_grid.json");
        return JsonSerializer.Deserialize<List<string[]>>(File.ReadAllText(path))!;
    }

    [Fact]
    public void Parses_measured_file_57_items_over_32_days()
    {
        var plan = DailyPlanGrid.Parse(Grid(), Today);

        Assert.Equal(new DateOnly(2026, 9, 29), plan.From);
        Assert.Equal(new DateOnly(2026, 10, 30), plan.To);
        Assert.Equal(32, plan.Days);
        Assert.Equal(57, plan.Items.Count);

        var it = plan.Items.Single(i => i.PartNo == "81720-PI000NNB");
        Assert.Equal("TRIM ASSY-TAILGATE UPR", it.PartName);
        Assert.Equal("EA", it.Unit);
        Assert.Equal(108m, it.PackQty);
        Assert.Equal(32, it.Scheduled.Length);
        Assert.Equal(108m, it.Scheduled[14]);   // 10/13
        Assert.Equal(0m,   it.Scheduled[0]);    // 09/29 예정량 없음
        Assert.Equal(108m, it.Po[0]);           // 09/29 P/O
        Assert.Equal(1080m, it.Scheduled.Sum());
    }

    [Fact]
    public void Anchor_uses_first_unambiguous_header()
    {
        // 기준일 10/05: 10/05~10/12 헤더가 전부 DD/MM 로 뒤집혀 5월 10일…12월 10일로 저장된 상황. 10/13 이 첫 신뢰 헤더(k=8).
        var rows = MinimalGrid(headers: new[]
        {
            "46152", "46183", "46213", "46244", "46274", "46305", "46335", "46366",   // 05-10 06-10 07-10 08-10 09-10 10-10 11-10 12-10 (뒤집힘)
            "46308", "46309",                                                       // 10-13 10-14 (정상)
        });
        var plan = DailyPlanGrid.Parse(rows, new DateOnly(2026, 10, 5));
        Assert.Equal(new DateOnly(2026, 10, 5), plan.From);
        Assert.Equal(new DateOnly(2026, 10, 14), plan.To);
    }

    [Fact]
    public void Text_headers_are_accepted()
    {
        var rows = MinimalGrid(headers: new[] { "09/29/2026", "09/30/2026", "10/01" });
        var plan = DailyPlanGrid.Parse(rows, Today);
        Assert.Equal(new DateOnly(2026, 9, 29), plan.From);
        Assert.Equal(new DateOnly(2026, 10, 1), plan.To);
    }

    [Fact]
    public void Ambiguous_headers_that_do_not_run_consecutively_are_rejected()
    {
        // 둘 다 일(day) < 13 이라 어느 쪽도 앵커로 못 믿는다. header[0] 기준으로 하루씩 이어지지 않으면
        // (10/5 다음이 10/6 이 아니라 10/9) DD/MM 결함이 헤더마다 다르게 걸렸을 수 있으므로 거부한다.
        var rows = MinimalGrid(headers: new[] { "10/5", "10/9" });
        var ex = Assert.Throws<FormatException>(() => DailyPlanGrid.Parse(rows, Today));
        Assert.Contains("날짜 헤더가", ex.Message);
    }

    [Fact]
    public void Header_far_from_today_is_rejected()
    {
        var rows = MinimalGrid(headers: new[] { "45658", "45659" });   // 2025-01-01, 01-02
        var ex = Assert.Throws<FormatException>(() => DailyPlanGrid.Parse(rows, Today));
        Assert.Contains("60일", ex.Message);
    }

    [Fact]
    public void Wrong_layout_is_rejected()
    {
        var rows = new List<string[]> { new[] { "NO", "PART NO", "PART NAME", "Unit", "Base Inv.", "09/29/2026" }, new[] { "", "", "", "", "", "" }, new[] { "1", "X", "n", "EA", "0", "5" } };
        Assert.Throws<FormatException>(() => DailyPlanGrid.Parse(rows, Today));
    }

    [Fact]
    public void Rows_without_part_no_are_skipped_and_long_part_no_is_rejected()
    {
        var rows = MinimalGrid(headers: new[] { "46294" }, items: new[] { ("", "0", "0"), ("81720-PI000NNB", "10", "0") });
        Assert.Single(DailyPlanGrid.Parse(rows, Today).Items);

        var bad = MinimalGrid(headers: new[] { "46294" }, items: new[] { (new string('X', 21), "1", "0") });
        Assert.Throws<FormatException>(() => DailyPlanGrid.Parse(bad, Today));
    }

    [Theory]
    [InlineData("46294", 2026, 9, 29)]
    [InlineData("46294.0", 2026, 9, 29)]
    [InlineData("10/13/2026", 2026, 10, 13)]
    [InlineData("2026-10-13", 2026, 10, 13)]
    [InlineData("1/5", 2027, 1, 5)]           // 기준일 9월 → 1월은 다음 해
    public void TryHeaderDate_parses(string s, int y, int m, int d)
    {
        Assert.True(DailyPlanGrid.TryHeaderDate(s, Today, out var dt));
        Assert.Equal(new DateOnly(y, m, d), dt);
    }

    /// <summary>고정 12열 + Sub total 2열 + 날짜쌍. 기본 품번 1개(Scheduled 전부 1, P/O 0).</summary>
    static List<string[]> MinimalGrid(string[] headers, (string part, string sched, string po)[]? items = null)
    {
        var h1 = new List<string> { "NO", "Vendor Code", "Vendor Name", "Car type", "PART NO", "PART NAME", "Unit", "Packing Qty.", "D-1", "Basic Inventory", "Current Inventory", "Open Order Qty.", "Sub total", "" };
        var h2 = new List<string> { "", "", "", "", "", "", "", "", "", "Reference", "Every Hour", "", "Scheduled Qty.", "P/O Qty" };
        foreach (var h in headers) { h1.Add(h); h1.Add(""); h2.Add("Scheduled Qty."); h2.Add("P/O Qty"); }
        var rows = new List<string[]> { h1.ToArray(), h2.ToArray() };
        items ??= new[] { ("81720-PI000NNB", "1", "0") };
        int n = 1;
        foreach (var (part, sched, po) in items)
        {
            var r = new List<string> { (n++).ToString(), "310471", "KRA OPERATIONS, LLC", "NE1A", part, "NAME", "EA", "108", "0", "0", "0", "0", "0", "0" };
            foreach (var _ in headers) { r.Add(sched); r.Add(po); }
            rows.Add(r.ToArray());
        }
        return rows;
    }
}
