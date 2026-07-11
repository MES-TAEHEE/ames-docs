using System.IO.Compression;
using System.Xml.Linq;

namespace AMES.Web.Services;

/// <summary>
/// 경량 .xlsx(OOXML SpreadsheetML) 리더 — 외부 패키지 없이 첫 워크시트를 문자열 격자로 읽는다.
/// SRM 내보내기처럼 날짜가 텍스트 셀로 저장된 파일 전제. 날짜 시리얼 값 변환은 하지 않는다.
/// </summary>
public static class XlsxSheetReader
{
    private static readonly XNamespace Ns =
        "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace RNs =
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace RelNs =
        "http://schemas.openxmlformats.org/package/2006/relationships";

    /// <summary>ZIP(PK) 시그니처 여부. 스트림 위치는 원복된다.</summary>
    public static bool IsXlsx(Stream stream)
    {
        var pos = stream.Position;
        int b0 = stream.ReadByte(), b1 = stream.ReadByte();
        stream.Position = pos;
        return b0 == 'P' && b1 == 'K';
    }

    /// <summary>
    /// 첫 워크시트의 모든 행을 셀 텍스트 배열로 반환.
    /// 빈 셀은 "", 모든 행은 시트 최대 열 수로 패딩되어 균일 폭.
    /// </summary>
    public static List<string[]> ReadRows(Stream stream)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        var shared = ReadSharedStrings(zip);
        var sheet  = FindFirstSheet(zip)
            ?? throw new FormatException("xlsx 파일에서 워크시트를 찾을 수 없습니다.");

        XDocument doc;
        using (var s = sheet.Open())
            doc = XDocument.Load(s);

        var rows = new List<List<string>>();
        foreach (var row in doc.Root?.Element(Ns + "sheetData")?.Elements(Ns + "row")
                            ?? Enumerable.Empty<XElement>())
        {
            var cells = new List<string>();
            foreach (var c in row.Elements(Ns + "c"))
            {
                int col = ColIndex((string?)c.Attribute("r")) ?? cells.Count;
                while (cells.Count < col) cells.Add("");
                var text = CellText(c, shared).Trim();
                if (cells.Count == col) cells.Add(text);
                else cells[col] = text;
            }
            rows.Add(cells);
        }

        int width = rows.Count > 0 ? rows.Max(r => r.Count) : 0;
        return rows.Select(r =>
        {
            while (r.Count < width) r.Add("");
            return r.ToArray();
        }).ToList();
    }

    // workbook.xml 시트 순서 첫 번째의 관계 대상 우선, 실패 시 관례 경로 폴백
    private static ZipArchiveEntry? FindFirstSheet(ZipArchive zip)
    {
        var wb   = zip.GetEntry("xl/workbook.xml");
        var rels = zip.GetEntry("xl/_rels/workbook.xml.rels");
        if (wb is not null && rels is not null)
        {
            string? rid;
            using (var s = wb.Open())
                rid = XDocument.Load(s).Root?.Element(Ns + "sheets")?.Elements(Ns + "sheet")
                    .Select(e => (string?)e.Attribute(RNs + "id")).FirstOrDefault();

            if (rid is not null)
            {
                string? target;
                using (var s = rels.Open())
                    target = XDocument.Load(s).Root?.Elements(RelNs + "Relationship")
                        .Where(e => (string?)e.Attribute("Id") == rid)
                        .Select(e => (string?)e.Attribute("Target")).FirstOrDefault();

                if (!string.IsNullOrEmpty(target))
                {
                    var path = target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;
                    var entry = zip.GetEntry(path);
                    if (entry is not null) return entry;
                }
            }
        }
        return zip.GetEntry("xl/worksheets/sheet1.xml");
    }

    private static List<string> ReadSharedStrings(ZipArchive zip)
    {
        var entry = zip.GetEntry("xl/sharedStrings.xml");
        if (entry is null) return new();

        using var s = entry.Open();
        return XDocument.Load(s).Root?.Elements(Ns + "si")
            .Select(si => string.Concat(si.Descendants(Ns + "t").Select(t => t.Value)))
            .ToList() ?? new();
    }

    private static string CellText(XElement c, List<string> shared)
    {
        var t = (string?)c.Attribute("t");
        if (t == "inlineStr")
            return string.Concat(c.Element(Ns + "is")?.Descendants(Ns + "t").Select(x => x.Value)
                                 ?? Enumerable.Empty<string>());

        var v = c.Element(Ns + "v")?.Value ?? "";
        if (t == "s")
            return int.TryParse(v, out var i) && i >= 0 && i < shared.Count ? shared[i] : "";
        return v;
    }

    // "AB12" → 27 (0-based). r 속성이 없으면 null.
    private static int? ColIndex(string? cellRef)
    {
        if (string.IsNullOrEmpty(cellRef)) return null;
        int col = 0, k = 0;
        while (k < cellRef.Length && char.IsAsciiLetterUpper(cellRef[k]))
            col = col * 26 + (cellRef[k++] - 'A' + 1);
        return k > 0 ? col - 1 : null;
    }
}
