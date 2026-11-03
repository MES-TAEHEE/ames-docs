using System.Net;
using System.Text.RegularExpressions;

namespace AMES.Data.Services.Srm;

/// <summary>
/// SRM 내보내기 .xls(HTML 테이블)를 격자로 펼친다. 병합(rowspan/colspan)된 칸은 덮는 모든 칸에 같은 값을 복사한다.
/// 날짜 머리글이 colspan=2 로 두 열을 덮는 MM31007 같은 양식을 열 머리글 기준으로 읽기 위해 필요하다.
/// 순수 함수(파일 읽기·인코딩은 호출측이 한다 — 이 파일은 CP949 로 디코딩된 문자열을 받는다).
/// </summary>
public static class SrmHtmlGrid
{
    static readonly Regex TrRx = new(@"<tr[^>]*>(.*?)</tr>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex TdRx = new(@"<t([dh])([^>]*)>(.*?)</t\1>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex TagRx = new("<[^>]+>", RegexOptions.Singleline | RegexOptions.Compiled);
    static readonly Regex RowSpanRx = new(@"rowspan\s*=\s*""?(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex ColSpanRx = new(@"colspan\s*=\s*""?(\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static List<string[]> Read(string html)
    {
        var cells = new Dictionary<(int R, int C), string>();
        var occupied = new HashSet<(int R, int C)>();
        int maxRow = -1, maxCol = -1;

        int r = 0;
        foreach (Match tr in TrRx.Matches(html))
        {
            int c = 0;
            foreach (Match td in TdRx.Matches(tr.Groups[1].Value))
            {
                while (occupied.Contains((r, c))) c++;
                var attrs = td.Groups[2].Value;
                int rs = Span(RowSpanRx, attrs), cs = Span(ColSpanRx, attrs);
                var text = Clean(td.Groups[3].Value);
                for (int dr = 0; dr < rs; dr++)
                for (int dc = 0; dc < cs; dc++)
                {
                    var key = (r + dr, c + dc);
                    cells[key] = text;
                    occupied.Add(key);
                    maxRow = Math.Max(maxRow, key.Item1);
                    maxCol = Math.Max(maxCol, key.Item2);
                }
                c += cs;
            }
            maxRow = Math.Max(maxRow, r);
            r++;
        }

        var result = new List<string[]>(maxRow + 1);
        for (int i = 0; i <= maxRow; i++)
        {
            var row = new string[maxCol + 1];
            for (int j = 0; j <= maxCol; j++) row[j] = cells.TryGetValue((i, j), out var v) ? v : "";
            result.Add(row);
        }
        return result;
    }

    static int Span(Regex rx, string attrs)
    {
        var m = rx.Match(attrs);
        return m.Success && int.TryParse(m.Groups[1].Value, out var n) && n >= 1 ? n : 1;
    }

    static string Clean(string html)
    {
        var s = WebUtility.HtmlDecode(TagRx.Replace(html, ""));
        return s.Replace(' ', ' ').Trim();
    }
}
