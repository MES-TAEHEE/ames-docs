using System.IO.Compression;
using System.Text;

namespace AMES.Web.Services;

/// <summary>
/// 경량 .xlsx(OOXML SpreadsheetML) 라이터 — 외부 패키지 없이 단일 시트를 만든다.
/// 문자열은 inlineStr, 숫자는 숫자 셀, 그 외(날짜 등)는 호출측이 문자열로 포맷해 넘긴다.
/// 화면 목록 내보내기 용도라 스타일은 헤더 굵게 하나만 둔다.
/// </summary>
public static class XlsxWriter
{
    public static byte[] Build(string sheetName, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<object?>> rows, int colWidth = 16)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "[Content_Types].xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
                  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/>
                  <Default Extension="xml" ContentType="application/xml"/>
                  <Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/>
                  <Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                  <Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/>
                </Types>
                """);
            Add(zip, "_rels/.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/>
                </Relationships>
                """);
            Add(zip, "xl/workbook.xml", $"""
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets><sheet name="{Esc(SafeSheetName(sheetName))}" sheetId="1" r:id="rId1"/></sheets>
                </workbook>
                """);
            Add(zip, "xl/_rels/workbook.xml.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
                </Relationships>
                """);
            Add(zip, "xl/styles.xml", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
                  <fonts count="2"><font><sz val="11"/><name val="Calibri"/></font><font><b/><sz val="11"/><name val="Calibri"/></font></fonts>
                  <fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills>
                  <borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders>
                  <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
                  <cellXfs count="2"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="0" fontId="1" fillId="0" borderId="0" xfId="0" applyFont="1"/></cellXfs>
                </styleSheet>
                """);
            Add(zip, "xl/worksheets/sheet1.xml", BuildSheet(headers, rows, colWidth));
        }
        return ms.ToArray();
    }

    private static string BuildSheet(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<object?>> rows, int colWidth)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
        if (headers.Count > 0)
            sb.Append($"<cols><col min=\"1\" max=\"{headers.Count}\" width=\"{colWidth}\" customWidth=\"1\"/></cols>");
        sb.Append("<sheetData>");

        int r = 1;
        sb.Append($"<row r=\"{r}\">");
        for (int c = 0; c < headers.Count; c++)
            sb.Append($"<c r=\"{Ref(c, r)}\" t=\"inlineStr\" s=\"1\"><is><t>{Esc(headers[c])}</t></is></c>");
        sb.Append("</row>");

        foreach (var row in rows)
        {
            r++;
            sb.Append($"<row r=\"{r}\">");
            for (int c = 0; c < row.Count; c++)
            {
                var v = row[c];
                if (v is null) continue;
                if (v is int or long or short or byte or decimal or double or float)
                    sb.Append($"<c r=\"{Ref(c, r)}\"><v>{Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture)}</v></c>");
                else
                    sb.Append($"<c r=\"{Ref(c, r)}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{Esc(v.ToString() ?? "")}</t></is></c>");
            }
            sb.Append("</row>");
        }

        sb.Append("</sheetData></worksheet>");
        return sb.ToString();
    }

    private static void Add(ZipArchive zip, string path, string xml)
    {
        var e = zip.CreateEntry(path, CompressionLevel.Optimal);
        using var s = e.Open();
        var bytes = Encoding.UTF8.GetBytes(xml.TrimStart());
        s.Write(bytes, 0, bytes.Length);
    }

    private static string Ref(int col, int row)
    {
        var sb = new StringBuilder();
        for (int c = col + 1; c > 0; c = (c - 1) / 26)
            sb.Insert(0, (char)('A' + (c - 1) % 26));
        return sb.Append(row).ToString();
    }

    private static string Esc(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    // 시트명 금지문자 제거 + 31자 제한
    private static string SafeSheetName(string s)
    {
        var t = new string(s.Where(ch => "\\/?*[]:".IndexOf(ch) < 0).ToArray());
        return t.Length == 0 ? "Sheet1" : t.Length > 31 ? t[..31] : t;
    }
}
