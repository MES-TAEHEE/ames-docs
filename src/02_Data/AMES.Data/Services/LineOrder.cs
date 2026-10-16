namespace AMES.Data.Services;

/// <summary>
/// 라인 표시 순서 — 공정 순(사출 → 감싸기 → 도장 → 재작업) 다음 라인 ID.
/// 공정 순위는 라인 작업장(WC)의 ProcessCode 를 공통코드 PROCESS 의 SortOrder 로 바꾼 값이라
/// 순서를 바꾸려면 MD-26 에서 PROCESS 정렬 순서를 고친다. 공정을 모르는 라인은 맨 뒤.
/// SQL 정렬은 <see cref="RankSql"/>, 화면 정렬은 MasterDataRepository.LineProcessOrder() 사전 + <see cref="Rank"/>.
/// </summary>
public static class LineOrder
{
    public const int Unknown = int.MaxValue;

    /// <summary>ORDER BY 에 넣는 공정 순위 식. <paramref name="lineIdExpr"/> 는 라인 ID 컬럼 식(신뢰된 상수만).</summary>
    public static string RankSql(string lineIdExpr) => $"""
        ISNULL((SELECT TOP 1 lo_c.SortOrder
                FROM   dbo.MD_Line lo_l
                JOIN   dbo.MD_WorkCenter lo_w ON lo_w.WCID = lo_l.WCID
                JOIN   dbo.MD_CodeItem   lo_c ON lo_c.GroupCode = 'PROCESS' AND lo_c.CodeValue = lo_w.ProcessCode
                WHERE  lo_l.LineID COLLATE DATABASE_DEFAULT = {lineIdExpr} COLLATE DATABASE_DEFAULT), {Unknown})
        """;

    public static int Rank(IReadOnlyDictionary<string, int> processOrder, string? lineId)
        => lineId is not null && processOrder.TryGetValue(lineId, out var o) ? o : Unknown;
}
