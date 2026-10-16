using AMES.Data.Aps;
using AMES.Data.Aps.Contracts;
using AMES.Data.Aps.Domain;
using System.Text.Json;
using Xunit;

namespace AMES.Data.Tests.Aps;

public class DemandBuilderTests
{
    // upload_current.json(1.48MB, Excel 전용) 은 반입하지 않는다 — 파일이 있을 때만 두 건을 돌리고 없으면 skip (Lazy 라 클래스 초기화는 살아 있다).
    static readonly Lazy<UploadSet> Upload = new(() => JsonSerializer.Deserialize<UploadSet>(File.ReadAllText(Fixtures.Path("upload_current.json")), ApsJson.Options)!);
    static readonly Lazy<PlanResponse> Lq10 = new(() => JsonSerializer.Deserialize<PlanResponse>(File.ReadAllText(Fixtures.Path("plan_LQ10.json")), ApsJson.Options)!);
    static bool HasUpload => File.Exists(Fixtures.Path("upload_current.json"));

    /// ACD0021 대신: 픽스처의 60품번은 LQ10, 원본이 상위 60 에서 잘라낸 9품번 자리는 수요가 가장 작은 9품번으로 채우고,
    /// 나머지는 다른 라인, 마지막 23품번은 없음(원본 경고 「23건」).
    static Dictionary<string, PartInfo> FakeParts()
    {
        var d = new Dictionary<string, PartInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in Lq10.Value.Bundle.Assembly) d[r.PartNo] = new PartInfo(r.PartNo, "LQ10", r.Alc, r.Pgn, r.SafetyStock, r.PartName);
        var dates = Lq10.Value.Bundle.Dates;
        var others = Upload.Value.Daily!.Rows.Where(r => !d.ContainsKey(r.Material)).GroupBy(r => r.Material)
            .OrderBy(g => g.Sum(r => r.SumD0 + dates.Skip(1).Sum(dt => r.Buckets.GetValueOrDefault(dt)))).ThenBy(g => g.Key).Select(g => g.Key).ToList();
        foreach (var m in others.Take(9)) d[m] = new PartInfo(m, "LQ10", "0000", "Z999", 0, null);
        foreach (var m in others.Skip(9).Take(others.Count - 9 - 23)) d[m] = new PartInfo(m, "ZZ99", null, null, 0, null);
        return d;
    }

    [SkippableFact]
    public void LQ10_rows_match_fixture_order_demand_and_attributes()
    {
        Skip.If(!HasUpload, "upload_current.json 미반입 (Notes #1)");
        var b = DemandBuilder.Build(Upload.Value, Lq10.Value.Bundle.Dates, "LQ10", null, null, FakeParts());
        Assert.Equal(Lq10.Value.Bundle.Assembly.Select(r => r.PartNo), b.Rows.Select(r => r.PartNo));
        foreach (var (got, exp) in b.Rows.Zip(Lq10.Value.Bundle.Assembly))
        {
            Assert.Equal(exp.Days.Select(d => d.Demand), got.Days.Select(d => d.Demand));
            Assert.Equal(exp.Model, got.Model);
            Assert.Equal(exp.Alc, got.Alc); Assert.Equal(exp.Pgn, got.Pgn); Assert.Equal(exp.PartName, got.PartName);
        }
        Assert.Equal(Lq10.Value.Filters.Models, b.Filters.Models);
        Assert.Equal(Lq10.Value.Filters.Pgns, b.Filters.Pgns);
        Assert.Contains(b.Warnings, w => w.StartsWith("LQ10 라인 기준으로 367건 중 ") && w.EndsWith("건을 골랐습니다 (SIS.ACD0021)."));
        var unknownRows = Upload.Value.Daily!.Rows.Count(r => !FakeParts().ContainsKey(r.Material));   // 원본의 「23건」은 품번이 아니라 파일 행 수
        Assert.Contains($"수요 파일의 품번 {unknownRows}건은 SIS.ACD0021 에 없어 라인을 판정할 수 없습니다.", b.Warnings);
        Assert.Contains(b.Warnings, w => w.StartsWith("같은 품번이 차종별로 나뉘어 온 ") && w.EndsWith("건을 품번 단위로 합쳤습니다 (재고·실적 이중계상 방지)."));
        Assert.Contains("품번 69건 중 수요가 많은 60건만 표시합니다. 차종 또는 부품군으로 범위를 좁히면 전체를 볼 수 있습니다.", b.Warnings);
        Assert.Equal(4, b.Warnings.Count);
    }

    [SkippableFact]
    public void Model_and_pgn_filters_narrow_rows_and_skip_limit_when_under_60()
    {
        Skip.If(!HasUpload, "upload_current.json 미반입 (Notes #1)");
        var byModel = DemandBuilder.Build(Upload.Value, Lq10.Value.Bundle.Dates, "LQ10", "HL", null, FakeParts());
        Assert.All(byModel.Rows, r => Assert.Contains("HL", r.Model!));
        Assert.Equal("HL", byModel.Filters.Model);

        var byPgn = DemandBuilder.Build(Upload.Value, Lq10.Value.Bundle.Dates, "LQ10", null, "Q019", FakeParts());
        Assert.All(byPgn.Rows, r => Assert.Equal("Q019", r.Pgn));
        Assert.True(byPgn.Rows.Count < 60);
        Assert.Equal("Q019", byPgn.Filters.Pgn);
        Assert.DoesNotContain(byPgn.Warnings, w => w.Contains("60건만"));
    }

    [Fact]
    public void Sunday_demand_folds_into_saturday()
    {
        var daily = new DemandFile { Rows = { new DemandRow { Material = "X-1", Model = "5L", Pgn = "P1", Pac = "0001", SumD0 = 7,
            Buckets = { ["2026-09-24"] = 3, ["2026-09-25"] = 4, ["2026-09-26"] = 5, ["2026-09-27"] = 6, ["2026-09-28"] = 1 } } } };
        var up = new UploadSet { Daily = daily, Files = { daily } };
        var parts = new Dictionary<string, PartInfo> { ["X-1"] = new("X-1", "LQ10", "0001", "P1", 0, "X") };
        var b = DemandBuilder.Build(up, new[] { "2026-09-24", "2026-09-25", "2026-09-26", "2026-09-28" }, "LQ10", null, null, parts);
        Assert.Equal(new double[] { 7, 4, 11, 1 }, b.Rows.Single().Days.Select(d => d.Demand).ToArray());
    }

    /// 리뷰 Important 2: 첫날이 토요일이면 다음 일요일 수요가 첫날에 합쳐지고, 기준일이 일요일이면 첫 근무일(월)의 수요도 읽어야 한다.
    [Fact]
    public void First_day_also_folds_sunday_when_base_date_is_saturday_or_sunday()
    {
        var daily = new DemandFile { Rows = { new DemandRow { Material = "X-1", Model = "5L", Pgn = "P1", Pac = "0001", SumD0 = 10,
            Buckets = { ["2026-09-26"] = 10, ["2026-09-27"] = 5, ["2026-09-28"] = 7, ["2026-09-29"] = 2 } } } };
        var up = new UploadSet { Daily = daily, Files = { daily } };
        var parts = new Dictionary<string, PartInfo> { ["X-1"] = new("X-1", "LQ10", "0001", "P1", 0, "X") };
        var sat = DemandBuilder.Build(up, ApsCalendar.WorkDates(SundayOffCalendar.Instance, "2026-09-26", 3), "LQ10", null, null, parts, baseDate: "2026-09-26");   // 토 · 월 · 화
        Assert.Equal(new double[] { 15, 7, 2 }, sat.Rows.Single().Days.Select(d => d.Demand).ToArray());      // 토요일 첫날 = D+00 10 + 일요일 5
        var sun = DemandBuilder.Build(up, ApsCalendar.WorkDates(SundayOffCalendar.Instance, "2026-09-27", 2), "LQ10", null, null, parts, baseDate: "2026-09-27");   // 월 · 화
        Assert.Equal(new double[] { 17, 2 }, sun.Rows.Single().Days.Select(d => d.Demand).ToArray());         // 일요일 기준 첫날(월) = D+00 10 + 월요일 7
    }
}
