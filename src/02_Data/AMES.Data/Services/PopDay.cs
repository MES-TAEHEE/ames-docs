using AMES.Data.Connection;

namespace AMES.Data.Services;

/// <summary>
/// POP 화면의 "하루" — 웹 보고서와 같은 전기일(SYS_Config.DAY_CUTOFF_TIME, 이 시각 전은 전날, 10-27).
/// 경계 시각은 로그인·DB 시각 동기화 때 읽어 두고(<see cref="TryLoad"/>), 오프라인 사본에도 남겨 정전 중 재시작해도 쓴다.
/// 읽은 적이 없으면 자정(종전 동작). POP 프로시저는 같은 값을 <c>dbo.FN_POP_DayCutoffMin()</c> 으로 읽는다.
/// </summary>
public static class PopDay
{
    static int _cutoffMin;
    static int _loaded;

    public static TimeSpan Cutoff => TimeSpan.FromMinutes(Volatile.Read(ref _cutoffMin));
    public static bool IsLoaded => Volatile.Read(ref _loaded) == 1;

    /// <summary>지금(DB 시각 기준)의 전기일.</summary>
    public static DateTime Today => ProdCalendar.ProdDateOf(DbClock.Now, Cutoff);

    /// <summary>시각 <paramref name="ts"/> 의 전기일 — LOT 생성 시각 등.</summary>
    public static DateTime Of(DateTime ts) => ProdCalendar.ProdDateOf(ts, Cutoff);

    public static void Set(TimeSpan cutoff)
    {
        Volatile.Write(ref _cutoffMin, (int)Math.Clamp(cutoff.TotalMinutes, 0, 1439));
        Volatile.Write(ref _loaded, 1);
    }

    /// <summary>DB 에서 경계 시각을 읽는다. 실패하면 지금 값을 그대로 두고 false.</summary>
    public static bool TryLoad(AmesConnectionFactory factory)
    {
        try
        {
            using var conn = factory.OpenConnection();
            Set(ProdCalendar.ReadCutoff(conn, null));
            return true;
        }
        catch { return false; }
    }
}
