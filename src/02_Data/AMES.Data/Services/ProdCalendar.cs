using Microsoft.Data.SqlClient;

namespace AMES.Data.Services;

/// <summary>
/// 전기일·교대 판정. 공통코드 두 그룹이 정본이다.
///   DAY_CUTOFF / TIME  — Attribute1 'HH:mm'(또는 'HHMM'). 이 시각 전은 전날 생산분.
///   WORK_SHIFT / *     — Attribute1 'HHMM-HHMM', SortOrder 순으로 첫 매치. 2400 은 자정.
/// 순수 함수는 DB 없이 테스트하고, <see cref="ResolveNow(SqlConnection, SqlTransaction?)"/> 만 DB 를 읽는다.
/// 캐시하지 않는다 — 공통코드 화면에서 고친 값이 다음 실적부터 바로 반영돼야 한다.
/// </summary>
public static class ProdCalendar
{
    public static DateTime ProdDateOf(DateTime ts, TimeSpan cutoff)
        => ts.TimeOfDay < cutoff ? ts.Date.AddDays(-1) : ts.Date;

    public static bool TryParseCutoff(string? attr, out TimeSpan cutoff)
    {
        cutoff = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(attr)) return false;
        var s = attr.Trim();
        int h, m;
        if (s.Contains(':'))
        {
            var p = s.Split(':');
            if (p.Length != 2 || !int.TryParse(p[0], out h) || !int.TryParse(p[1], out m)) return false;
        }
        else
        {
            if (s.Length != 4 || !int.TryParse(s[..2], out h) || !int.TryParse(s[2..], out m)) return false;
        }
        if (h is < 0 or > 23 || m is < 0 or > 59) return false;
        cutoff = new TimeSpan(h, m, 0);
        return true;
    }

    public static string? ShiftOf(DateTime ts, IReadOnlyList<(string Code, string? Window)> shifts)
    {
        int minute = ts.Hour * 60 + ts.Minute;
        foreach (var (code, window) in shifts)
        {
            if (!TryParseWindow(window, out int start, out int end) || start == end) continue;
            bool hit = start < end
                ? minute >= start && minute < end
                : minute >= start || minute < end;     // 자정을 넘는 창
            if (hit) return code;
        }
        return null;
    }

    /// <summary>'HHMM-HHMM'. 끝 2400 은 1440 분(자정)으로 받는다.</summary>
    public static bool TryParseWindow(string? attr, out int startMin, out int endMin)
    {
        startMin = 0; endMin = 0;
        if (string.IsNullOrWhiteSpace(attr)) return false;
        var parts = attr.Split('-');
        return parts.Length == 2 && TryHHMM(parts[0], out startMin) && TryHHMM(parts[1], out endMin);
    }

    private static bool TryHHMM(string s, out int min)
    {
        min = 0; s = s.Trim();
        if (s.Length != 4 || !int.TryParse(s[..2], out var h) || !int.TryParse(s[2..], out var m)) return false;
        if (h < 0 || h > 24 || m < 0 || m > 59) return false;
        min = h * 60 + m;
        return min <= 1440;
    }

    public static (DateTime ProdDate, string? ShiftCode) Resolve(
        DateTime ts, string? cutoffAttr, IReadOnlyList<(string Code, string? Window)> shifts)
    {
        var cutoff = TryParseCutoff(cutoffAttr, out var c) ? c : TimeSpan.Zero;
        return (ProdDateOf(ts, cutoff), ShiftOf(ts, shifts));
    }

    /// <summary>
    /// 서버 시각(SYSDATETIME) 기준으로 공통코드를 읽어 판정. 호출자의 트랜잭션 안에서 실행된다.
    /// 터미널 시계가 아니라 서버 시계를 쓰는 이유: EntryAt 도 SYSDATETIME() 이라 같은 시각을 봐야 한다.
    /// </summary>
    public static (DateTime Now, DateTime ProdDate, string? ShiftCode) ResolveNow(SqlConnection conn, SqlTransaction? tx)
    {
        string? cutoffAttr = null;
        var shifts = new List<(string Code, string? Window)>();

        using var cmd = new SqlCommand("""
            SELECT SYSDATETIME();

            SELECT TOP 1 Attribute1
            FROM   dbo.MD_CodeItem
            WHERE  GroupCode = 'DAY_CUTOFF' AND CodeValue = 'TIME' AND ISNULL(UseFlag,1) = 1;

            SELECT CodeValue, Attribute1
            FROM   dbo.MD_CodeItem
            WHERE  GroupCode = 'WORK_SHIFT' AND ISNULL(UseFlag,1) = 1
            ORDER  BY ISNULL(SortOrder,0), CodeValue;
            """, conn, tx);
        using var rdr = cmd.ExecuteReader();
        var now = rdr.Read() ? rdr.GetDateTime(0) : DateTime.Now;
        if (rdr.NextResult() && rdr.Read()) cutoffAttr = rdr["Attribute1"] as string;
        if (rdr.NextResult())
            while (rdr.Read())
                if (rdr["CodeValue"] as string is { Length: > 0 } code)
                    shifts.Add((code, rdr["Attribute1"] as string));

        var (prodDate, shift) = Resolve(now, cutoffAttr, shifts);
        return (now, prodDate, shift);
    }
}
