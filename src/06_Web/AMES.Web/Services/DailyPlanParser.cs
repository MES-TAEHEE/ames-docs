using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using AMES.Data.Services.DemandPlan;

namespace AMES.Web.Services;

/// <summary>SRM MM30011 파일(.xlsx OOXML / .xls EUC-KR HTML) → 격자 → DailyPlanGrid.Parse. 규칙은 전부 DailyPlanGrid(AMES.Data, 테스트 있음).</summary>
public static class DailyPlanParser
{
    private static readonly Regex TrRx = new(@"<tr[^>]*>(.*?)</tr>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TdRx = new(@"<t[dh][^>]*>(.*?)</t[dh]>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static DailyPlan Parse(Stream stream, DateOnly today)
    {
        var rows = XlsxSheetReader.IsXlsx(stream) ? XlsxSheetReader.ReadRows(stream) : ReadHtmlRows(stream);
        return DailyPlanGrid.Parse(rows, today);
    }

    private static List<string[]> ReadHtmlRows(Stream stream)
    {
        string html;
        using (var rd = new StreamReader(stream, Encoding.GetEncoding(51949))) html = rd.ReadToEnd();
        return TrRx.Matches(html)
            .Select(m => TdRx.Matches(m.Groups[1].Value).Select(c => WebUtility.HtmlDecode(Regex.Replace(c.Groups[1].Value, "<[^>]+>", "")).Trim()).ToArray())
            .ToList();
    }
}
