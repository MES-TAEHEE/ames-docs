using AMES.Data.Services.DemandPlan;
using Xunit;

namespace AMES.Data.Tests.DemandPlan;

/// <summary>MM30011 웹서비스 응답(실측 2026-09-29, 협력사 310471) → DemandPlanCell. dates 봉투가 날짜를 주므로 앵커 규칙이 없다.</summary>
public class SrmMipMapperTests
{
    static string Sample() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "DemandPlan", "MIP_PLAN_7700_310471_20260929_api.json"));

    [Fact]
    public void Maps_measured_response()
    {
        var r = SrmMipMapper.Map(SrmMipMapper.Parse(Sample()));
        Assert.Equal(57, r.Rows);
        Assert.Equal(0, r.Skipped);
        Assert.Equal(new DateOnly(2026, 9, 29), r.From);
        Assert.Equal(new DateOnly(2026, 10, 30), r.To);
        var c = Assert.Single(r.Cells, x => x.ItemNo == "81720-PI000NNB" && x.PlanDate == new DateOnly(2026, 10, 13));
        Assert.Equal((108m, 0m, 108m, "TRIM ASSY-TAILGATE UPR", "EA"), (c.ScheduledQty, c.PoQty, c.PackQty, c.PartName, c.Unit));
        Assert.DoesNotContain(r.Cells, x => x.ItemNo == "81720-PI000NNB" && x.PlanDate == new DateOnly(2026, 9, 29));   // PR 0, PO 108 → 셀 없음
        Assert.Equal(10, r.Cells.Count(x => x.ItemNo == "81720-PI000NNB"));
        Assert.Contains(r.Cells, x => x.ItemNo == "82322-TD000" && x.PlanDate == new DateOnly(2026, 9, 29) && x.ScheduledQty == 250m);
        Assert.Empty(r.Warnings);
    }

    [Fact]
    public void Parse_rejects_non_json_and_failed_envelope()
    {
        Assert.Throws<FormatException>(() => SrmMipMapper.Parse("<html>"));
        Assert.Throws<FormatException>(() => SrmMipMapper.Parse("""{"success":false,"message":"no auth"}"""));
        Assert.Throws<FormatException>(() => SrmMipMapper.Parse("""{"success":true,"data":[]}"""));
    }

    [Fact]
    public void Map_skips_bad_part_numbers_and_unknown_day_keys()
    {
        var json = """
            {"success":true,"plan_date":"2026-09-29","dates":{"D0":"2026-09-29","D1":"2026-09-30","D2":"bad"},"count":3,
             "data":[{"PARTNO":"A","PACK_QTY":10,"D0_PR_QTY":5,"D1_PR_QTY":"7","D1_PO_QTY":2,"D2_PR_QTY":9,"D9_PR_QTY":4},
                     {"PARTNO":"","D0_PR_QTY":1},
                     {"PARTNO":"XXXXXXXXXXXXXXXXXXXXX","D0_PR_QTY":1},
                     {"PARTNO":"B","D0_PR_QTY":-1,"D1_PR_QTY":0}]}
            """;
        var r = SrmMipMapper.Map(SrmMipMapper.Parse(json));
        Assert.Equal(4, r.Rows);
        Assert.Equal(2, r.Skipped);
        Assert.Equal(new DateOnly(2026, 9, 29), r.From); Assert.Equal(new DateOnly(2026, 9, 30), r.To);
        Assert.Equal(2, r.Cells.Count);   // A D0=5, A D1=7(PO 2); D2 bad·D9 미정의·B 음수/0 은 셀 없음
        Assert.Equal(2m, r.Cells.Single(c => c.PlanDate == new DateOnly(2026, 9, 30)).PoQty);
        Assert.Contains(r.Warnings, w => w.Contains("D2"));
        Assert.Contains(r.Warnings, w => w.Contains("음수"));
    }
}
