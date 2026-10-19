using AMES.Data.Services;
using Xunit;

namespace AMES.Data.Tests;

// 10-20 — MD-003·MD-004 엑셀 올리기 규칙(DB 없음): 머리글 키, 없는 것만 등록·있는 것은 건너뜀, 문제 행은 등록하지 않음
public class MasterExcelTests
{
    static HashSet<string> S(params string[] xs) => new(xs, StringComparer.OrdinalIgnoreCase);

    [Theory]
    [InlineData("품번 (ItemNo)", "ItemNo")]
    [InlineData("Item No. (ItemNo) ", "ItemNo")]
    [InlineData("ItemNo", "ItemNo")]
    [InlineData("수량 (EA)", "EA")]
    [InlineData("", "")]
    public void Header_key_is_the_bracketed_name(string header, string key) => Assert.Equal(key, MasterExcel.HeaderKey(header));

    static ItemImport.Lookup ItemLookup() => new(
        S("EXIST-1"), S("MATERIAL", "SUB", "ASSY"),
        new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase) { ["RESIN"] = S("MATERIAL"), ["ANY"] = S() },
        S("EA", "KG", "G"), S("A"), S("INHOUSE", "PURCHASE"));

    [Fact]
    public void Items_register_only_new_rows_and_report_problems_by_row()
    {
        var sheet = new List<string[]>
        {
            new[] { "품명 (ItemName)", "품번 (ItemNo)", "유형 (ItemType)", "카테고리 (ItemCategory)", "단위 (DefaultUOM)", "추가 불출율 (IssueAddPct)", "활성 (ActiveFlag)", "모르는 열" },
            new[] { "기존", "exist-1", "BOGUS", "", "", "", "", "x" },               // 2: 있는 품번 — 다른 칸은 보지 않고 건너뜀
            new[] { "새 자재", "new-1", "material", "resin", "kg", "10", "N", "" },   // 3: 등록
            new[] { "", "new-2", "MATERIAL", "", "EA", "", "", "" },                 // 4: 품명 없음
            new[] { "중복", "NEW-1", "MATERIAL", "", "EA", "", "", "" },             // 5: 파일 안 중복
            new[] { "포장", "new-3", "MATERIAL", "", "BOX", "", "", "" },            // 6: 포장 단위
            new[] { "유형", "new-4", "SUB", "RESIN", "EA", "", "", "" },             // 7: 카테고리가 유형에 맞지 않음
            new[] { "율", "new-5", "MATERIAL", "", "EA", "120", "", "" },            // 8: 범위 밖
            new[] { "", "", "", "", "", "", "", "" },                               // 빈 행 무시
        };
        var r = ItemImport.Parse(sheet, ItemLookup());

        var row = Assert.Single(r.New);
        Assert.Equal(("NEW-1", "새 자재", "MATERIAL", "RESIN", "KG", 10m, false), (row.ItemNo, row.ItemName, row.ItemType, row.ItemCategory, row.DefaultUom, row.IssueAddPct, row.ActiveFlag));
        Assert.Equal(3, row.Line);
        Assert.Equal([(2, "EXIST-1")], r.Existing);
        Assert.Equal(
            [(4, "ItemName", MasterExcel.Codes.Required), (5, "ItemNo", MasterExcel.Codes.Duplicate), (6, "DefaultUOM", MasterExcel.Codes.Packaging),
             (7, "ItemCategory", MasterExcel.Codes.OutOfRange), (8, "IssueAddPct", MasterExcel.Codes.OutOfRange)],
            r.Problems.Select(p => (p.Line, p.Key, p.Code)));
    }

    [Fact]
    public void Missing_required_column_is_not_the_template()
    {
        var r = ItemImport.Parse([new[] { "품명 (ItemName)" }, new[] { "x" }], ItemLookup());
        Assert.Empty(r.New);
        var p = Assert.Single(r.Problems);
        Assert.Equal((1, "ItemNo", MasterExcel.Codes.NoHeader), (p.Line, p.Key, p.Code));
    }

    static readonly DateOnly Today = new(2026, 10, 20);

    static BomImport.Lookup BomLookup(Func<string, string, bool>? cycle = null) => new(
        new Dictionary<string, BomImport.ExistingVersion>(StringComparer.OrdinalIgnoreCase)
        {
            ["V-APPROVED"] = new("APPROVED", "P1", S("C1")),
            ["V-DRAFT"] = new("DRAFT", "P2", S("C1")),
        },
        S("P1", "P2", "P3", "C1", "C2", "C3"), S("EA", "G"), Today, cycle ?? ((_, _) => false));

    static readonly string[] BomHeader = ["VersionID", "RootItemNo", "VersionNo", "EffFrom", "EffTo", "Status", "CompItemNo", "QtyPer", "UOM", "Position"];

    [Fact]
    public void Bom_creates_new_versions_adds_missing_lines_to_drafts_and_skips_the_rest()
    {
        var sheet = new List<string[]>
        {
            BomHeader,
            new[] { "v-new", "p3", "1", "2026-10-20", "", "APPROVED", "c1", "2", "ea", "" },    // 새 버전(상태 열은 읽지 않음)
            new[] { "V-NEW", "P3", "1", "2026-10-20", "", "", "C2", "0.5", "G", "" },
            new[] { "V-APPROVED", "P1", "1", "2026-08-28", "", "", "C2", "1", "EA", "" },       // 승인 버전 — 통째로 건너뜀(지난 날짜도 따지지 않음)
            new[] { "V-DRAFT", "P2", "", "", "", "", "C1", "1", "EA", "" },                     // 초안의 있는 구성품 — 건너뜀
            new[] { "V-DRAFT", "P2", "", "", "", "", "C3", "3", "EA", "" },                     // 초안에 없는 구성품 — 더함
            new[] { "V-EMPTY", "P1", "2", "2026-11-01", "", "", "", "", "", "" },               // 라인 없는 새 버전
        };
        var r = BomImport.Parse(sheet, BomLookup());

        Assert.Empty(r.Problems);
        Assert.Equal(["V-NEW", "V-DRAFT", "V-EMPTY"], r.Plans.Select(v => v.VersionId));
        var nv = r.Plans[0];
        Assert.True(nv.IsNew);
        Assert.Equal(("P3", "V1.0", (DateOnly?)Today), (nv.RootItemNo, nv.VersionNo, nv.EffFrom));
        Assert.Equal([("C1", 2m, "EA", 10), ("C2", 0.5m, "G", 20)], nv.Lines.Select(l => (l.CompItemNo, l.QtyPer, l.Uom, l.Position)));
        var dv = r.Plans[1];
        Assert.False(dv.IsNew);
        Assert.Equal(["C3"], dv.Lines.Select(l => l.CompItemNo));
        Assert.Empty(r.Plans[2].Lines);
        Assert.Equal([(4, "V-APPROVED", "APPROVED")], r.SkippedVersions);
        Assert.Equal([(5, "V-DRAFT", "C1")], r.SkippedLines);
    }

    [Fact]
    public void Bom_version_with_any_problem_is_not_registered()
    {
        var sheet = new List<string[]>
        {
            BomHeader,
            new[] { "V-PAST", "P3", "1", "2026-10-19", "", "", "C1", "1", "EA", "" },     // 지난 시작일
            new[] { "V-BAD", "P3", "1", "2026-10-21", "", "", "C1", "1", "EA", "" },
            new[] { "V-BAD", "P3", "1", "2026-10-21", "", "", "C1", "2", "EA", "" },      // 같은 구성품 두 번 → 버전 전체 제외
            new[] { "V-SELF", "P3", "1", "2026-10-21", "", "", "P3", "1", "EA", "" },
            new[] { "V-CYC", "P1", "1", "2026-10-21", "", "", "C2", "1", "EA", "" },
            new[] { "V-HEAD", "P3", "1", "2026-10-21", "", "", "C1", "1", "EA", "" },
            new[] { "V-HEAD", "P3", "2", "2026-10-21", "", "", "C2", "1", "EA", "" },     // 같은 버전인데 버전 번호가 다름
            new[] { "V-ROOT", "P9", "1", "2026-10-21", "", "", "C1", "0", "EA", "" },      // 없는 최상위·수량 0
        };
        var r = BomImport.Parse(sheet, BomLookup((p, c) => p == "P1" && c == "C2"));

        Assert.Empty(r.Plans);
        Assert.Equal(
            [(2, "EffFrom", MasterExcel.Codes.PastDate), (4, "CompItemNo", MasterExcel.Codes.Duplicate), (5, "CompItemNo", MasterExcel.Codes.SelfComp),
             (6, "CompItemNo", MasterExcel.Codes.Cycle), (8, "VersionID", MasterExcel.Codes.HeaderDiff),
             (9, "RootItemNo", MasterExcel.Codes.Unknown), (9, "QtyPer", MasterExcel.Codes.OutOfRange)],
            r.Problems.Select(p => (p.Line, p.Key, p.Code)));
    }

    [Theory]
    [InlineData("2026-10-21")]
    [InlineData("2026/10/21")]
    [InlineData("46316")]   // 엑셀이 날짜로 바꾼 일련번호
    public void Bom_dates_accept_iso_and_excel_serials(string cell)
    {
        var r = BomImport.Parse([BomHeader, new[] { "V-D", "P3", "1", cell, "", "", "C1", "1", "EA", "" }], BomLookup());
        Assert.Empty(r.Problems);
        Assert.Equal(new DateOnly(2026, 10, 21), Assert.Single(r.Plans).EffFrom);
    }
}
