using AMES.Data.Aps.Contracts;

namespace AMES.Data.Aps.Domain;

/// 프론트 stageFor · offsetOf · usesStock · batchOf 와 같은 순서: 체인 전용 → 라인 개별(공통) → 유형 기본값 → 0.
/// <param name="foldPreHorizon">true = 선행일 때문에 기준일 앞으로 떨어지는 사출 소요를 첫 사출일에 몬다(AMES 경로, 10-06 사용자 결정). 골든 경로(원본)는 false — 조용히 버린다.</param>
public sealed class StageRules(Settings s, IReadOnlyDictionary<string, LineInfo> lines, bool foldPreHorizon = false)
{
    public bool FoldPreHorizon => foldPreHorizon;

    bool IsInjection(string lineCd) => lines.TryGetValue(lineCd, out var l) && l.Type == "injection";

    LineStage? For(string lineCd, string? rootLine)
    {
        LineStage? common = null;
        foreach (var st in s.LineStages)
        {
            if (!string.Equals(st.LineCd, lineCd, StringComparison.OrdinalIgnoreCase)) continue;
            if (string.IsNullOrEmpty(st.RootLine)) { common ??= st; continue; }
            if (rootLine != null && string.Equals(st.RootLine, rootLine, StringComparison.OrdinalIgnoreCase)) return st;
        }
        return common;
    }

    StageDefault Default(string lineCd) => IsInjection(lineCd) ? s.StageDefaults.Injection : s.StageDefaults.Assembly;

    public int OffsetDays(string lineCd, string? rootLine)
    {
        if (string.Equals(lineCd, rootLine, StringComparison.OrdinalIgnoreCase)) return 0;
        return For(lineCd, rootLine)?.OffsetDays ?? Default(lineCd).OffsetDays;
    }

    public bool UsesStock(string lineCd, string? rootLine)
    {
        if (string.Equals(lineCd, rootLine, StringComparison.OrdinalIgnoreCase)) return true;
        var st = For(lineCd, rootLine);
        return st != null ? st.UseStock || st.BatchDays > 1 : Default(lineCd).UseStock;
    }

    public int BatchDays(string lineCd, string? rootLine) => Math.Max(1, For(lineCd, rootLine)?.BatchDays ?? 1);
}
