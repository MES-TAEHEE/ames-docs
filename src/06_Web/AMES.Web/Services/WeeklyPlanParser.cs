using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace AMES.Web.Services;

/// <summary>
/// SRM ZMM30004 주간 구매계획 파서.
/// 파일은 .xls 확장자지만 실체는 EUC-KR 인코딩 HTML 테이블:
/// 헤더 1행 = NO/PART NO/PART NAME/Unit/Base Inv. + 주 시작일(MM/dd/yyyy) N개,
/// 헤더 2행 = 주차 라벨([28/1W]) N개, 이후 데이터 행.
/// </summary>
public static class WeeklyPlanParser
{
    public sealed record Plan(IReadOnlyList<DateTime> WeekStarts,
                              IReadOnlyList<string> WeekLabels,
                              IReadOnlyList<Item> Items);

    public sealed record Item(string PartNo, string PartName, string Unit,
                              decimal BaseInv, decimal[] Qty);

    private const int FixedCols = 5;   // NO, PART NO, PART NAME, Unit, Base Inv.

    private static readonly Regex TrRx =
        new(@"<tr[^>]*>(.*?)</tr>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex TdRx =
        new(@"<td[^>]*>(.*?)</td>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static Plan Parse(Stream stream)
    {
        string html;
        using (var rd = new StreamReader(stream, Encoding.GetEncoding(51949)))
            html = rd.ReadToEnd();

        var rows = TrRx.Matches(html)
            .Select(m => TdRx.Matches(m.Groups[1].Value)
                .Select(c => WebUtility.HtmlDecode(c.Groups[1].Value).Trim())
                .ToArray())
            .Where(r => r.Length > 0)
            .ToList();

        if (rows.Count < 3 || rows[0].Length < FixedCols + 1)
            throw new FormatException("ZMM30004 주간 구매계획 테이블 형식이 아닙니다.");

        var weekStarts = rows[0].Skip(FixedCols)
            .Select(t => DateTime.ParseExact(t, "MM/dd/yyyy", CultureInfo.InvariantCulture))
            .ToList();
        if (rows[1].Length < weekStarts.Count)
            throw new FormatException("주차 라벨 행이 주 시작일 개수와 맞지 않습니다.");
        var weekLabels = rows[1].Take(weekStarts.Count).ToList();

        var items = new List<Item>(rows.Count - 2);
        foreach (var r in rows.Skip(2))
        {
            if (r.Length != FixedCols + weekStarts.Count)
                throw new FormatException(
                    $"품번 '{(r.Length > 1 ? r[1] : "?")}' 행의 셀 수가 {FixedCols + weekStarts.Count}개가 아닙니다 ({r.Length}개).");
            items.Add(new Item(r[1], r[2], r[3],
                ParseQty(r[4]),
                r.Skip(FixedCols).Select(ParseQty).ToArray()));
        }
        return new Plan(weekStarts, weekLabels, items);
    }

    private static decimal ParseQty(string s) =>
        s.Length == 0 ? 0m : decimal.Parse(s, NumberStyles.Number, CultureInfo.InvariantCulture);
}
