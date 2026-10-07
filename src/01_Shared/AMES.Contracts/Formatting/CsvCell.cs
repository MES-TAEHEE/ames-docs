using System.Globalization;

namespace AMES.Contracts.Formatting;

/// <summary>
/// CSV 셀 한 칸. 엑셀은 = + - @ (와 탭·CR)로 시작하는 칸을 수식으로 실행하므로(수식 주입) 그런 값 앞에 ' 를 붙여 문자로 읽게 한다.
/// 숫자 그대로인 값(-5, +1.5)은 수식이 아니라서 숫자로 남긴다. 쉼표·따옴표·줄바꿈이 있으면 따옴표로 감싼다.
/// </summary>
public static class CsvCell
{
    public static string Escape(string? value)
    {
        var s = value ?? "";
        if (s.Length > 0 && s[0] is '=' or '+' or '-' or '@' or '\t' or '\r'
            && !decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out _))
            s = "'" + s;
        return s.IndexOfAny([',', '"', '\n', '\r']) >= 0 ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
    }
}
