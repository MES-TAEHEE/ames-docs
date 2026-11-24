using System.IO.Compression;
using System.Xml.Linq;
using AMES.Web.Services;
using Xunit;

namespace AMES.Data.Tests;

public sealed class InventoryWorkbookTests
{
    [Fact]
    public void Quantity_daily_change_and_movement_sections_have_independent_sheets_and_numeric_values()
    {
        var bytes=XlsxWriter.BuildWorkbook(
            new("Stock Quantities",["Category","Stock","Target"],[new object?[]{"Resin",125.5m,200m}]),
            new("Daily Category Changes",["Date","Closing Stock"],[new object?[]{"2026-01-01",125.5m}]),
            new("Received Released History",["Received","Released"],[new object?[]{10m,3m}]));
        using var stream=new MemoryStream(bytes);
        using var zip=new ZipArchive(stream,ZipArchiveMode.Read);
        var workbook=Read(zip,"xl/workbook.xml");
        XNamespace s="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        Assert.Equal(new[]{"Stock Quantities","Daily Category Changes","Received Released History"},workbook.Descendants(s+"sheet").Select(e=>(string?)e.Attribute("name")));
        Assert.Equal("125.5",Read(zip,"xl/worksheets/sheet1.xml").Descendants(s+"v").First().Value);
        Assert.Equal(new[]{"Received","Released"},Read(zip,"xl/worksheets/sheet3.xml").Descendants(s+"t").Select(e=>e.Value));
        var relations=Read(zip,"xl/_rels/workbook.xml.rels");
        Assert.Equal(4,relations.Root!.Elements().Count());
        Assert.Equal(4,relations.Root.Elements().Select(e=>(string?)e.Attribute("Id")).Distinct().Count());
        Assert.NotNull(zip.GetEntry("xl/styles.xml"));
    }

    [Fact]
    public void Coverage_is_a_numeric_percentage_and_changes_have_green_and_red_number_formats()
    {
        var bytes=XlsxWriter.BuildWorkbook(new XlsxWriter.DataSheet("Stock Quantities",["Recommended Stock Quantity","Stock / Recommended (%)","Change"],
            [new object?[]{200m,1.25m,20m},new object?[]{400m,.5m,-10m}],ChangeColumns:new HashSet<int>{2},PercentColumns:new HashSet<int>{1}));
        using var stream=new MemoryStream(bytes);
        using var zip=new ZipArchive(stream,ZipArchiveMode.Read);
        XNamespace s="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var sheet=Read(zip,"xl/worksheets/sheet1.xml");
        XElement Cell(string address)=>sheet.Descendants(s+"c").Single(e=>(string?)e.Attribute("r")==address);
        Assert.Equal("1.25",Cell("B2").Element(s+"v")!.Value);
        Assert.Equal("2",(string?)Cell("B2").Attribute("s"));
        Assert.Equal("3",(string?)Cell("C2").Attribute("s"));
        Assert.Equal("4",(string?)Cell("C3").Attribute("s"));
        Assert.True((int)sheet.Descendants(s+"col").First().Attribute("width")!>=29);
        var styles=Read(zip,"xl/styles.xml");
        Assert.Equal(new[]{"FF15803D","FFB91C1C"},styles.Descendants(s+"color").Select(e=>(string?)e.Attribute("rgb")));
        Assert.Equal("10",(string?)styles.Descendants(s+"cellXfs").Single().Elements().ElementAt(2).Attribute("numFmtId"));
        Assert.Equal(zip.Entries.Count,zip.Entries.Select(e=>e.FullName).Distinct().Count());
    }

    [Fact]
    public void Sheet_names_are_unique_after_sanitizing_and_truncating()
    {
        var bytes=XlsxWriter.BuildWorkbook(new("A/B",[],[]),new("AB",[],[]),new("AB",[],[]));
        using var stream=new MemoryStream(bytes);
        using var zip=new ZipArchive(stream,ZipArchiveMode.Read);
        var names=Read(zip,"xl/workbook.xml").Descendants().Where(e=>e.Name.LocalName=="sheet").Select(e=>(string)e.Attribute("name")!).ToArray();
        Assert.Equal(new[]{"AB","AB (2)","AB (3)"},names);
    }

    [Fact]
    public void Existing_single_sheet_exports_remain_single_sheet()
    {
        using var stream=new MemoryStream(XlsxWriter.Build("Existing",["Part"],[new object?[]{"PART-1"}]));
        using var zip=new ZipArchive(stream,ZipArchiveMode.Read);
        Assert.NotNull(zip.GetEntry("xl/worksheets/sheet1.xml"));
        Assert.Null(zip.GetEntry("xl/worksheets/sheet2.xml"));
    }

    static XDocument Read(ZipArchive zip,string path)
    {
        using var stream=zip.GetEntry(path)!.Open();
        return XDocument.Load(stream);
    }
}
