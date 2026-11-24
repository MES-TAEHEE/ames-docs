using System.IO.Compression;
using System.Text;
using System.Xml.Linq;

namespace AMES.Web.Services;

/// <summary>
/// 경량 .xlsx(OOXML SpreadsheetML) 라이터 — 외부 패키지 없이 단일 시트를 만든다.
/// 문자열은 inlineStr, 숫자는 숫자 셀, 그 외(날짜 등)는 호출측이 문자열로 포맷해 넘긴다.
/// 화면 목록 내보내기 용도라 스타일은 헤더 굵게 하나만 둔다.
/// </summary>
public static class XlsxWriter
{
    /// <summary>둘째 시트에 그림으로 넣을 차트 — 제목 + PNG 바이트(화면의 SVG 를 브라우저에서 그린 것).</summary>
    public sealed record ChartImage(string Title, byte[] Png);
    public sealed record DataSheet(string Name,IReadOnlyList<string> Headers,IEnumerable<IReadOnlyList<object?>> Rows,int ColumnWidth=20,
        IReadOnlySet<int>? ChangeColumns=null,IReadOnlySet<int>? PercentColumns=null);

    public static byte[] BuildWorkbook(params DataSheet[] sheets)
    {
        if(sheets.Length==0) throw new ArgumentException("At least one sheet is required.",nameof(sheets));
        sheets=sheets.Select(sheet=>sheet with{Rows=sheet.Rows.ToArray()}).ToArray();
        var first=sheets[0];
        using var ms=new MemoryStream();
        ms.Write(Build(first.Name,first.Headers,first.Rows,first.ColumnWidth));
        ms.Position=0;
        using(var zip=new ZipArchive(ms,ZipArchiveMode.Update,leaveOpen:true))
        {
            XDocument Read(string path) { using var stream=zip.GetEntry(path)!.Open(); return XDocument.Load(stream); }
            var workbook=Read("xl/workbook.xml");
            var relations=Read("xl/_rels/workbook.xml.rels");
            var types=Read("[Content_Types].xml");
            var styles=Read("xl/styles.xml");
            XNamespace s="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            XNamespace r="http://schemas.openxmlformats.org/officeDocument/2006/relationships";
            var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase){SafeSheetName(first.Name)};
            styles.Root!.AddFirst(new XElement(s+"numFmts",new XAttribute("count",1),new XElement(s+"numFmt",
                new XAttribute("numFmtId",164),new XAttribute("formatCode","+#,##0.###;-#,##0.###;0"))));
            var fonts=styles.Root.Element(s+"fonts")!;
            foreach(var color in new[]{"FF15803D","FFB91C1C"}) fonts.Add(new XElement(s+"font",new XElement(s+"sz",new XAttribute("val",11)),
                new XElement(s+"name",new XAttribute("val","Calibri")),new XElement(s+"color",new XAttribute("rgb",color))));
            fonts.SetAttributeValue("count",4);
            var formats=styles.Root.Element(s+"cellXfs")!;
            foreach(var format in new[]{(Num:10,Font:0),(Num:164,Font:2),(Num:164,Font:3)})
                formats.Add(new XElement(s+"xf",new XAttribute("numFmtId",format.Num),new XAttribute("fontId",format.Font),
                    new XAttribute("fillId",0),new XAttribute("borderId",0),new XAttribute("xfId",0),
                    new XAttribute("applyNumberFormat",1),new XAttribute("applyFont",1)));
            formats.SetAttributeValue("count",5);
            zip.GetEntry("xl/worksheets/sheet1.xml")!.Delete();
            Add(zip,"xl/worksheets/sheet1.xml",BuildSheet(first.Headers,first.Rows,first.ColumnWidth,first.ChangeColumns,first.PercentColumns,true));
            for(var i=1;i<sheets.Length;i++)
            {
                var sheet=sheets[i];
                var name=SafeSheetName(sheet.Name);
                var suffix=2;
                while(!names.Add(name)) name=SafeSheetName(sheet.Name)[..Math.Min(26,SafeSheetName(sheet.Name).Length)]+$" ({suffix++})";
                var id=$"rId{i+2}";
                workbook.Root!.Element(s+"sheets")!.Add(new XElement(s+"sheet",new XAttribute("name",name),new XAttribute("sheetId",i+1),new XAttribute(r+"id",id)));
                relations.Root!.Add(new XElement(relations.Root.Name.Namespace+"Relationship",new XAttribute("Id",id),
                    new XAttribute("Type",r.NamespaceName+"/worksheet"),new XAttribute("Target",$"worksheets/sheet{i+1}.xml")));
                types.Root!.Add(new XElement(types.Root.Name.Namespace+"Override",new XAttribute("PartName",$"/xl/worksheets/sheet{i+1}.xml"),
                    new XAttribute("ContentType","application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")));
                Add(zip,$"xl/worksheets/sheet{i+1}.xml",BuildSheet(sheet.Headers,sheet.Rows,sheet.ColumnWidth,sheet.ChangeColumns,sheet.PercentColumns,true));
            }
            foreach(var entry in new[]{("xl/workbook.xml",workbook),("xl/_rels/workbook.xml.rels",relations),("[Content_Types].xml",types),("xl/styles.xml",styles)})
            {
                zip.GetEntry(entry.Item1)!.Delete();
                Add(zip,entry.Item1,entry.Item2.ToString(SaveOptions.DisableFormatting));
            }
        }
        return ms.ToArray();
    }

    public static byte[] Build(string sheetName, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<object?>> rows, int colWidth = 16)
        => Build(sheetName, headers, rows, colWidth, null, null);

    /// <summary>차트가 있으면 둘째 시트(chartSheetName)에 세로로 쌓아 넣는다. 데이터 시트는 그대로.</summary>
    public static byte[] Build(string sheetName, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<object?>> rows, int colWidth,
        IReadOnlyList<ChartImage>? charts, string? chartSheetName)
    {
        bool withCharts = charts is { Count: > 0 };
        string chartSheet = SafeSheetName(chartSheetName ?? "Charts");
        if (withCharts && string.Equals(chartSheet, SafeSheetName(sheetName), StringComparison.OrdinalIgnoreCase)) chartSheet = SafeSheetName(chartSheet + " 2");
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
                """ + (withCharts ? """
                  <Default Extension="png" ContentType="image/png"/>
                  <Override PartName="/xl/worksheets/sheet2.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/>
                  <Override PartName="/xl/drawings/drawing1.xml" ContentType="application/vnd.openxmlformats-officedocument.drawing+xml"/>
                """ : "") + """
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
                  <sheets><sheet name="{Esc(SafeSheetName(sheetName))}" sheetId="1" r:id="rId1"/>{(withCharts ? $"<sheet name=\"{Esc(chartSheet)}\" sheetId=\"2\" r:id=\"rId3\"/>" : "")}</sheets>
                </workbook>
                """);
            Add(zip, "xl/_rels/workbook.xml.rels", """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                  <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/>
                  <Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/>
                """ + (withCharts ? """
                  <Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet2.xml"/>
                """ : "") + """
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
            if (withCharts) AddChartSheet(zip, charts!);
        }
        return ms.ToArray();
    }

    // ── 차트 시트: 제목 셀 + 그림(oneCellAnchor)을 세로로 쌓는다. 그림 크기는 PNG 헤더의 픽셀 크기 그대로(1px = 9525 EMU). ──
    const int RowPx = 20;          // 기본 행 높이 15pt ≈ 20px — 다음 그림을 놓을 행을 계산할 때 쓴다
    const long EmuPerPx = 9525;

    private static void AddChartSheet(ZipArchive zip, IReadOnlyList<ChartImage> charts)
    {
        var cells = new StringBuilder();
        var anchors = new StringBuilder();
        var rels = new StringBuilder();
        int row = 0;   // 0-based
        for (int i = 0; i < charts.Count; i++)
        {
            var (w, h) = PngSize(charts[i].Png);
            cells.Append($"<row r=\"{row + 1}\"><c r=\"A{row + 1}\" t=\"inlineStr\" s=\"1\"><is><t xml:space=\"preserve\">{Esc(charts[i].Title)}</t></is></c></row>");
            int picRow = row + 1;
            anchors.Append($"<xdr:oneCellAnchor><xdr:from><xdr:col>0</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>{picRow}</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from>"
                + $"<xdr:ext cx=\"{w * EmuPerPx}\" cy=\"{h * EmuPerPx}\"/>"
                + $"<xdr:pic><xdr:nvPicPr><xdr:cNvPr id=\"{i + 2}\" name=\"Chart {i + 1}\"/><xdr:cNvPicPr><a:picLocks noChangeAspect=\"1\"/></xdr:cNvPicPr></xdr:nvPicPr>"
                + $"<xdr:blipFill><a:blip r:embed=\"rId{i + 1}\"/><a:stretch><a:fillRect/></a:stretch></xdr:blipFill>"
                + $"<xdr:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"{w * EmuPerPx}\" cy=\"{h * EmuPerPx}\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></xdr:spPr></xdr:pic>"
                + "<xdr:clientData/></xdr:oneCellAnchor>");
            rels.Append($"<Relationship Id=\"rId{i + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"../media/image{i + 1}.png\"/>");
            AddBytes(zip, $"xl/media/image{i + 1}.png", charts[i].Png);
            row = picRow + (h + RowPx - 1) / RowPx + 1;   // 그림 아래 한 줄 띄움
        }
        Add(zip, "xl/worksheets/sheet2.xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
            + "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">"
            + "<sheetData>" + cells + "</sheetData><drawing r:id=\"rId1\"/></worksheet>");
        Add(zip, "xl/worksheets/_rels/sheet2.xml.rels", """
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing" Target="../drawings/drawing1.xml"/>
            </Relationships>
            """);
        Add(zip, "xl/drawings/drawing1.xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
            + "<xdr:wsDr xmlns:xdr=\"http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">"
            + anchors + "</xdr:wsDr>");
        Add(zip, "xl/drawings/_rels/drawing1.xml.rels",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
            + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" + rels + "</Relationships>");
    }

    // PNG IHDR: 16~19 바이트 = 너비, 20~23 = 높이(빅엔디언). 아니면 기본 크기.
    private static (int W, int H) PngSize(byte[] png)
    {
        if (png.Length < 24 || png[1] != (byte)'P' || png[2] != (byte)'N' || png[3] != (byte)'G') return (640, 240);
        int w = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
        int h = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
        return w > 0 && h > 0 && w < 20000 && h < 20000 ? (w, h) : (640, 240);
    }

    private static void AddBytes(ZipArchive zip, string path, byte[] bytes)
    {
        var e = zip.CreateEntry(path, CompressionLevel.Fastest);   // PNG 는 이미 압축돼 있다
        using var s = e.Open();
        s.Write(bytes, 0, bytes.Length);
    }

    private static string BuildSheet(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<object?>> rows, int colWidth,
        IReadOnlySet<int>? changeColumns=null,IReadOnlySet<int>? percentColumns=null,bool autoWidth=false)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sb.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
        if(autoWidth)
        {
            var data=rows.ToArray();
            rows=data;
            int Width(string value)=>value.Sum(c=>c>127?2:1);
            sb.Append("<cols>");
            for(var c=0;c<headers.Count;c++)
            {
                var column=c;
                var width=Math.Clamp(Math.Max(Width(headers[c]),data.Select(row=>column<row.Count?Width(row[column]?.ToString()??""):0).DefaultIfEmpty(0).Max())+3,12,60);
                sb.Append($"<col min=\"{c+1}\" max=\"{c+1}\" width=\"{width}\" customWidth=\"1\"/>");
            }
            sb.Append("</cols>");
        }
        else if (headers.Count > 0)
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
                {
                    var style=percentColumns?.Contains(c)==true?2:changeColumns?.Contains(c)==true
                        ? Convert.ToDecimal(v)>0?3:Convert.ToDecimal(v)<0?4:0 : 0;
                    sb.Append($"<c r=\"{Ref(c, r)}\"{(style>0?$" s=\"{style}\"":"")}><v>{Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture)}</v></c>");
                }
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
