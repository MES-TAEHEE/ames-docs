namespace AMES.Data.Services.SapIf;

/// <summary>SAP B1 인터페이스(EOS_IF) 공통 규칙 — 순수 함수, DB 불필요(정본 테스트 <c>SapIfRulesTests</c>).</summary>
public static class SapIfRules
{
    /// <summary>MD_* 반영 행위자(CreatedBy/ModifiedBy). 정의서: AMES 가 'SAP-IF' 로 설정한다.</summary>
    public const string Actor = "SAP-IF";

    /// <summary>EOS_IF 의 IF_MSG 는 nvarchar(1000).</summary>
    public const int MsgMax = 1000;

    public static string? Truncate(string? s, int max)
        => s is null || s.Length <= max ? s : s[..max];

    /// <summary>예외 → IF_MSG/로그용 한 줄(형식 + 메시지, 1000자 이내).</summary>
    public static string ErrorMessage(Exception ex)
        => Truncate($"{ex.GetType().Name}: {ex.Message}".ReplaceLineEndings(" "), MsgMax)!;

    /// <summary>재시도 간격(분): 1·2·4·8·16, 최대 30.</summary>
    public static int BackoffMinutes(int retryCount)
        => (int)Math.Min(30, Math.Pow(2, Math.Clamp(retryCount, 0, 10)));

    /// <summary>
    /// B1 구매발주 라인 → MES <c>WH_PurchaseOrder.Status</c>(기존 어휘 Open·Partial·Complete·Received·Cancelled).
    /// X(취소) → Cancelled / 라인·헤더 종료(C)인데 잔량이 남음 → Complete / 잔량 0 → Received / 일부 입고 → Partial / 그 밖 → Open.
    /// </summary>
    public static string PoStatus(string? docStatus, string? lineStatus, decimal orderQty, decimal openQty)
    {
        var d = docStatus?.Trim().ToUpperInvariant();
        var l = lineStatus?.Trim().ToUpperInvariant();
        if (d == "X") return "Cancelled";
        if (openQty <= 0m) return "Received";
        if (d == "C" || l == "C") return "Complete";
        return openQty < orderQty ? "Partial" : "Open";
    }

    // ── 비교 정책(11-13 사용자 결정) ─────────────────────────────────────────────────────
    //  1) SAP 와 AMES 의 값이 다르면 SAP 값으로 덮어쓴다(SAP 우선) — 변경 전·후 값을 로그에 남긴다.
    //  2) SAP 에만 있는 행은 새로 등록한다(SAP 우선).
    //  3) AMES 에만 있는 행은 우선 그대로 둔다(삭제·비활성 없음) — 목록만 로그에 남긴다.

    /// <summary>들어올 값과 AMES 기존 값을 컬럼별로 비교한다. 같은 값이면 목록에 넣지 않는다.</summary>
    public static IReadOnlyList<(string Col, object? Old, object? New)> Diff(
        IReadOnlyDictionary<string, object?> existing, IReadOnlyDictionary<string, object?> incoming,
        IEnumerable<string> cols, IReadOnlyCollection<string> defaultTrueWhenNull)
    {
        var list = new List<(string, object?, object?)>();
        foreach (var c in cols)
        {
            var now = incoming.TryGetValue(c, out var v) ? v : null;
            if (now is null && defaultTrueWhenNull.Contains(c)) now = true;   // 적용 시 ActiveFlag NULL → 1 과 같은 규칙
            var old = existing.TryGetValue(c, out var o) ? o : null;
            if (!SameValue(old, now)) list.Add((c, old, now));
        }
        return list;
    }

    private static bool SameValue(object? a, object? b)
    {
        if (a is null || a is DBNull) return b is null || b is DBNull;
        if (b is null || b is DBNull) return false;
        if (a is bool || b is bool) return Equals(a, b);
        if (IsNumber(a) && IsNumber(b)) return Convert.ToDecimal(a) == Convert.ToDecimal(b);
        return string.Equals(a.ToString(), b.ToString(), StringComparison.Ordinal);
    }

    private static bool IsNumber(object o) => o is byte or short or int or long or decimal or float or double;

    /// <summary>"컬럼: 기존 → 새 값; …" (NULL 은 (null)). IF_MSG·로그 길이 안으로 자른다.</summary>
    public static string FormatDiff(IEnumerable<(string Col, object? Old, object? New)> diffs)
    {
        static string S(object? v) => v is null or DBNull ? "(null)" : v is bool b ? (b ? "1" : "0") : v.ToString()!;
        return Truncate(string.Join("; ", diffs.Select(d => $"{d.Col}: {S(d.Old)} → {S(d.New)}")), 900)!;
    }

    /// <summary>AMES 에만 있는 키(SAP 에 없음) — 정렬해 돌려준다. 키 비교는 대소문자 무시(DB 콜레이션과 같음).</summary>
    public static IReadOnlyList<string> AmesOnlyKeys(IEnumerable<string> amesKeys, IEnumerable<string> sapKeys)
    {
        var sap = new HashSet<string>(sapKeys, StringComparer.OrdinalIgnoreCase);
        return amesKeys.Where(k => !sap.Contains(k)).OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// DryRun 결과를 SAP·웅진 쪽이 EOS_IF 에서 바로 볼 수 있게 IF_MSG 에 적는 문구. IF_STATUS 는 N 그대로라 처리 흐름에 영향이 없다.
    /// 본 처리(Y)가 되면 IF_MSG 는 NULL 로 지워진다.
    /// </summary>
    public static string DryRunMessage(string text) => Truncate("[DRYRUN] " + text, MsgMax)!;

    /// <summary>같은 문구를 매 주기 다시 쓰지 않는다(EOS_IF 쓰기를 최소로).</summary>
    public static bool NeedsDryRunWrite(string? currentMsg, string newMsg)
        => !string.Equals(currentMsg, newMsg, StringComparison.Ordinal);

    /// <summary>IF_ACTION 'D' = 삭제가 아니라 비활성(정의서 2.2).</summary>
    public static bool IsDeactivate(string? action) => string.Equals(action, "D", StringComparison.Ordinal);
}
