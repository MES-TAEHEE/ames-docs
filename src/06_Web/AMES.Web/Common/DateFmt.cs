using System.Globalization;

namespace AMES.Web;

/// <summary>
/// UI 언어에 따른 날짜 표시 포맷. 한국어=ISO(yyyy-MM-dd), 영어=미국식(MM/dd/yyyy).
/// 표시 전용 — 저장/파싱용 왕복 값에는 사용하지 않는다.
/// </summary>
public static class DateFmt
{
    private static bool Ko => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ko";

    public static string Date            => Ko ? "yyyy-MM-dd"          : "MM/dd/yyyy";
    public static string DateShort       => Ko ? "yy-MM-dd"            : "MM/dd/yy";
    public static string DateWeekday     => Ko ? "yyyy-MM-dd ddd"      : "MM/dd/yyyy ddd";
    public static string DateTime        => Ko ? "yyyy-MM-dd HH:mm"    : "MM/dd/yyyy HH:mm";
    public static string DateTimeSec     => Ko ? "yyyy-MM-dd HH:mm:ss" : "MM/dd/yyyy HH:mm:ss";
    public static string MonthDay        => Ko ? "MM-dd"               : "MM/dd";
    public static string MonthDayTime    => Ko ? "MM-dd HH:mm"         : "MM/dd HH:mm";
    public static string MonthDayTimeSec => Ko ? "MM-dd HH:mm:ss"      : "MM/dd HH:mm:ss";
}
