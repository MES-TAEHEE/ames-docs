using System.Globalization;

namespace AMES.Contracts.Formatting;

/// <summary>
/// 일시 입력칸에 손으로 친 값을 해석한다 — 화면 표시 형식 그대로, ISO(yyyy-MM-dd HH:mm), 숫자만(yyyyMMdd HHmm·yyyyMMddHHmm),
/// 시각만(HH:mm·HHmm = 오늘 그 시각), 날짜만(그날 00:00). 해석하지 못하면 null. 초는 버린다.
/// </summary>
public static class DateTimeInput
{
    static readonly string[] Full =
    [
        "yyyy-MM-dd HH:mm", "yyyy-MM-dd H:mm", "yyyy-MM-dd HH:mm:ss",
        "yyyy-M-d HH:mm", "yyyy-M-d H:mm",
        "yyyy/MM/dd HH:mm", "yyyy/M/d H:mm", "yyyy.MM.dd HH:mm",
        "yyyyMMdd HHmm", "yyyyMMdd HH:mm", "yyyyMMddHHmm",
        "yyyy-MM-dd", "yyyy/MM/dd", "yyyyMMdd",
    ];
    static readonly string[] TimeOnly = ["HH:mm", "H:mm", "HHmm"];

    public static DateTime? Parse(string? text, DateTime today, string? displayFormat = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var s = string.Join(' ', text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        var inv = CultureInfo.InvariantCulture;

        var formats = new List<string>();
        if (!string.IsNullOrWhiteSpace(displayFormat))
        {
            formats.Add(displayFormat);
            if (displayFormat.Contains(" HH:mm")) formats.Add(displayFormat.Replace(" HH:mm", " H:mm"));
            var datePart = displayFormat.Split(' ')[0];
            if (datePart != displayFormat) formats.Add(datePart);
        }
        formats.AddRange(Full);

        if (DateTime.TryParseExact(s, formats.ToArray(), inv, DateTimeStyles.None, out var d))
            return new DateTime(d.Year, d.Month, d.Day, d.Hour, d.Minute, 0);
        if (s.Length == 3 && s.All(char.IsAsciiDigit)) s = "0" + s;   // 705 → 07:05
        if (DateTime.TryParseExact(s, TimeOnly, inv, DateTimeStyles.None, out var t))
            return today.Date.AddHours(t.Hour).AddMinutes(t.Minute);
        return null;
    }
}
