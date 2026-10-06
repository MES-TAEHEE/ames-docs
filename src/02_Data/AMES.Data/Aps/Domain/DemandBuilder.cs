using AMES.Data.Aps.Contracts;

namespace AMES.Data.Aps.Domain;

public sealed class DemandBuild
{
    public List<AssemblyRow> Rows { get; } = new();
    public PlanFilters Filters { get; } = new();
    public List<string> Warnings { get; } = new();
}

/// 수요 파일(Daily) → 조회 라인의 완제품 행. 규칙은 docs/reference/oracle-schema.md 「수요(KAGA PLAN) 규칙」.
public static class DemandBuilder
{
    /// injectionLine 이 있으면(사출 라인 조회, plan_H1A100.json) 라인 필터 대신 linkedParents(그 라인 품번을 쓰는 완제품)로 고른다.
    /// 「라인을 판정할 수 있는 품번」 = ACD0021 에 있고 LINECD 가 라인 마스터(lines)에 있는 것. 경고의 건수는 파일 행 수(원본 23건 = 행 23).
    /// lineCd 가 없으면(전체 라인) 모르는 품번까지 모든 행을 쓰고 선택 경고를 내지 않는다 (원본 plan-all 응답).
    public static DemandBuild Build(UploadSet upload, IReadOnlyList<string> dates, string? lineCd, string? model, string? pgn,
                                    IReadOnlyDictionary<string, PartInfo> parts, int limit = 60,
                                    string? injectionLine = null, IReadOnlySet<string>? linkedParents = null,
                                    IReadOnlyDictionary<string, LineInfo>? lines = null, string? baseDate = null,
                                    IApsCalendar? cal = null)
    {
        cal ??= SundayOffCalendar.Instance;               // 달력 미지정 = 원본 규칙(일요일만 휴무)
        baseDate ??= dates.Count > 0 ? dates[0] : null;   // 조회 기준일 — 파일의 D+00 합계가 이 날의 수요
        var b = new DemandBuild();
        var all = upload.Daily?.Rows ?? new List<DemandRow>();
        bool Known(string m) => parts.TryGetValue(m, out var p) && !string.IsNullOrEmpty(p.LineCd) && (lines == null || lines.ContainsKey(p.LineCd));
        var unknown = all.Count(r => !Known(r.Material));

        List<DemandRow> picked;
        if (injectionLine != null)
        {
            picked = all.Where(r => parts.ContainsKey(r.Material) && linkedParents != null && linkedParents.Contains(r.Material)).ToList();
            var found = picked.Select(r => r.Material).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            b.Warnings.Add($"{injectionLine} 는 사출 라인이라, 이 라인의 사출품을 쓰는 완제품 {found}건을 BOM 에서 역으로 찾았습니다.");
            b.Warnings.Add($"완제품 {all.Count}건 중 {picked.Count}건이 이 사출 라인과 연결됩니다.");
        }
        else if (string.IsNullOrEmpty(lineCd))
        {
            picked = all.ToList();
        }
        else
        {
            picked = all.Where(r => Known(r.Material) && string.Equals(parts[r.Material].LineCd, lineCd, StringComparison.OrdinalIgnoreCase)).ToList();
            b.Warnings.Add($"{lineCd} 라인 기준으로 {all.Count}건 중 {picked.Count}건을 골랐습니다 (SIS.ACD0021).");
            if (unknown > 0) b.Warnings.Add($"수요 파일의 품번 {unknown}건은 SIS.ACD0021 에 없어 라인을 판정할 수 없습니다.");
        }

        // 필터 목록은 라인과 무관하게 수요 파일 전체에서 (원본 plan_LQ10.json 의 filters.models = 파일의 6개 차종 전부)
        b.Filters.Models.AddRange(all.Select(r => r.Model ?? "").Where(m => m.Length > 0).Distinct().OrderBy(m => m, StringComparer.Ordinal));
        b.Filters.Pgns.AddRange(all.Select(r => r.Pgn ?? "").Where(m => m.Length > 0).Distinct().OrderBy(m => m, StringComparer.Ordinal));
        b.Filters.Model = string.IsNullOrEmpty(model) ? null : model;
        b.Filters.Pgn = string.IsNullOrEmpty(pgn) ? null : pgn;

        if (!string.IsNullOrEmpty(model)) picked = picked.Where(r => r.Model == model).ToList();
        if (!string.IsNullOrEmpty(pgn)) picked = picked.Where(r => r.Pgn == pgn).ToList();

        var groups = picked.GroupBy(r => r.Material, StringComparer.OrdinalIgnoreCase).ToList();
        var merged = picked.Count - groups.Count;
        if (merged > 0) b.Warnings.Add($"같은 품번이 차종별로 나뉘어 온 {merged}건을 품번 단위로 합쳤습니다 (재고·실적 이중계상 방지).");

        var rows = new List<AssemblyRow>();
        foreach (var g in groups)
        {
            var first = g.First(); parts.TryGetValue(g.Key, out var p);   // 전체 라인 모드는 모르는 품번도 행이 된다
            var row = new AssemblyRow
            {
                // 완제품의 PGN · ALC 는 수요 파일 값(ACD0021 과 같음). 필터도 파일 값으로 건다.
                PartNo = g.Key, PartName = first.Description, Pgn = first.Pgn ?? p?.Pgn, Alc = first.Pac ?? p?.Alc, LineCd = p?.LineCd,
                Model = string.Join("/", g.Select(r => r.Model ?? "").Where(m => m.Length > 0).Distinct()),
                SafetyStock = p?.SafetyStock ?? 0,
            };
            for (var i = 0; i < dates.Count; i++)
            {
                // 첫날 = 파일의 D+00 합계 + (기준일이 아닌) 접힌 날짜의 버킷: 토요일 기준이면 다음 일요일, 일요일 기준이면 첫 근무일(월).
                double demand = i == 0
                    ? g.Sum(r => r.SumD0) + ApsCalendar.FoldedDates(cal, dates[0], dates).Where(d => d != baseDate).Sum(d => g.Sum(r => r.Buckets.GetValueOrDefault(d)))
                    : ApsCalendar.FoldedDates(cal, dates[i], dates).Sum(d => g.Sum(r => r.Buckets.GetValueOrDefault(d)));
                row.Days.Add(new AssemblyDay { Date = dates[i], Demand = demand });
            }
            rows.Add(row);
        }

        if (rows.Count > limit)
        {
            b.Warnings.Add($"품번 {rows.Count}건 중 수요가 많은 {limit}건만 표시합니다. 차종 또는 부품군으로 범위를 좁히면 전체를 볼 수 있습니다.");
            rows = rows.OrderByDescending(r => r.Days.Sum(d => d.Demand)).ThenBy(r => r.PartNo, StringComparer.Ordinal).Take(limit).ToList();
        }
        b.Rows.AddRange(rows.OrderBy(r => r.PartNo, StringComparer.Ordinal));   // 원본은 품번순 (plan-all: 81710-DW000WK(Q011) 앞에 P575 품번이 오지 않는다)
        return b;
    }
}
