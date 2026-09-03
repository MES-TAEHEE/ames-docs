using System.Globalization;
using System.Resources;

namespace AMES.Pop.Common;

/// <summary>
/// POP 화면 언어 선택기. 시작 시 appsettings.json `PopTerminal:Language`(ko/en)로
/// 초기화되고, 화면의 KO/EN 토글로 런타임 변경 가능 (프로세스 전역).
///
/// 문자열은 Resources/PopStrings.resx(중립 = 한국어) + PopStrings.en.resx(영어)에서
/// 키로 조회한다 — AMES.Web 의 SharedResources.resx 컨벤션과 동일.
/// 키가 리소스에 없으면 키 문자열 그대로 표시되므로 누락을 화면에서 바로 알 수 있다.
/// </summary>
internal static class PopLang
{
    private static readonly ResourceManager Res =
        new("AMES.Pop.Resources.PopStrings", typeof(PopLang).Assembly);

    private static readonly CultureInfo KoCulture = CultureInfo.GetCultureInfo("ko");
    private static readonly CultureInfo EnCulture = CultureInfo.GetCultureInfo("en");

    private static string? _lang;

    public static string Lang
    {
        get => _lang ??= LoadDefault();
        set => _lang = value == "en" ? "en" : "ko";
    }

    public static bool IsKo => Lang == "ko";

    /// <summary>리소스 키 → 현재 언어 문자열. args 가 있으면 string.Format 적용.</summary>
    public static string T(string key, params object?[] args)
    {
        var s = Res.GetString(key, IsKo ? KoCulture : EnCulture) ?? key;
        return args.Length == 0 ? s : string.Format(s, args);
    }

    /// <summary>설비 상태 코드 → 표시명 (RUN/IDLE/STOP/ALARM/OFFLINE).</summary>
    public static string EqStatus(string code) => code switch
    {
        "RUN"     => T("EqRun"),
        "IDLE"    => T("EqIdle"),
        "STOP"    => T("EqStop"),
        "ALARM"   => T("EqAlarm"),
        "OFFLINE" => T("EqOffline"),
        _         => code,
    };

    /// <summary>WO 상태 코드 → 표시명 (PP_WorkOrder.Status).</summary>
    public static string WoStatus(string code) => code switch
    {
        "Released"    => T("WoReleased"),
        "In Progress" => T("WoInProgress"),
        "Planned"     => T("WoPlanned"),
        "Closed"      => T("WoClosed"),
        "Cancelled"   => T("WoCancelled"),
        _             => code,
    };

    /// <summary>금형 상태 코드 → 표시명 (MD_Mold.Status).</summary>
    public static string MoldStatus(string code) => code switch
    {
        "Available"   => T("MoldAvailable"),
        "Mounted"     => T("MoldMounted"),
        "Maintenance" => T("MoldMaintenance"),
        _             => code,
    };

    /// <summary>교대 코드 → 표시명. 코드는 공통코드 WORK_SHIFT(A/B/C) 기준.</summary>
    public static string Shift(string code) => code switch
    {
        "A" => T("ShiftA"),
        "B" => T("ShiftB"),
        "C" => T("ShiftC"),
        _   => code,
    };

    private static string LoadDefault()
    {
        try { return AppConfig.Current.Language == "en" ? "en" : "ko"; }
        catch { return "ko"; }
    }
}
