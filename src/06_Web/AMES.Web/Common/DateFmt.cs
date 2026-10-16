using System.Globalization;

namespace AMES.Web;

/// <summary>
/// UI 언어에 따른 날짜 표시 포맷 — 한국어 = ISO(yyyy-MM-dd), 영어 = 미국식(MM/dd/yyyy), 스페인어 = 일/월(dd/MM/yyyy).
/// 화면에 보이는 날짜는 **항상 연도를 넣는다**(10-10 사용자 결정 — 연말·연초나 지난해 기록이 섞이면 "12-28" 만으로는 어느 해인지 모른다).
/// 표시 전용 — 저장·파싱·CSV/엑셀 내보내기·감사 키·인쇄 문서처럼 값이 그대로 쓰이는 곳에는 쓰지 않는다(그곳은 ISO 고정).
/// </summary>
public static class DateFmt
{
    private static string Lang => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
    private static string Pick(string ko, string en, string es) => Lang switch { "ko" => ko, "es" => es, _ => en };

    public static string Date        => Pick("yyyy-MM-dd",          "MM/dd/yyyy",          "dd/MM/yyyy");
    public static string DateShort   => Pick("yy-MM-dd",            "MM/dd/yy",            "dd/MM/yy");
    public static string DateWeekday => Pick("yyyy-MM-dd ddd",      "MM/dd/yyyy ddd",      "dd/MM/yyyy ddd");
    public static string DateTime    => Pick("yyyy-MM-dd HH:mm",    "MM/dd/yyyy HH:mm",    "dd/MM/yyyy HH:mm");
    public static string DateTimeSec => Pick("yyyy-MM-dd HH:mm:ss", "MM/dd/yyyy HH:mm:ss", "dd/MM/yyyy HH:mm:ss");
    public static string Month       => Pick("yyyy-MM",             "MM/yyyy",             "MM/yyyy");
    public static string MonthShort  => Pick("yy-MM",               "MM/yy",               "MM/yy");

    /// <summary>Radzen FormatString 용 — FormatString="@DateFmt.Fmt(DateFmt.Date)" → "{0:yyyy-MM-dd}"</summary>
    public static string Fmt(string format) => "{0:" + format + "}";
}
