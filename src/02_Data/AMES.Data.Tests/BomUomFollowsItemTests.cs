using AMES.Data.Repositories;
using AMES.Data.Services;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests;

// 10-23 — BOM 라인 단위는 구성품의 기본 단위를 그대로 저장한다(추가·수정·엑셀 올리기 모두 입력 단위 없음).
// 품목 단위를 바꾸면 그 품목을 구성품으로 쓰는 BOM 라인(모든 버전)의 단위도 같이 바뀐다. 재고·실적이 있는 품목은 단위를 바꿀 수 없다(51832)
public class BomUomFollowsItemTests
{
    const string WriteSkip = "set AMES_TEST_CONN (local DB) to run — creates and removes test items, BOM versions, lines and stock";
    const string Actor  = "itest-uomfollow";
    const string Parent = "ITEST-UOMF-P";
    const string Comp   = "ITEST-UOMF-C";
    const string Comp2  = "ITEST-UOMF-C2";
    const string Ver    = "ITEST-UOMF-V1";
    const string Ver2   = "ITEST-UOMF-V2";
    const string Ver3   = "ITEST-UOMF-V3";
    const string Line   = "ITEST-UOMF-L1";
    const string Line2  = "ITEST-UOMF-L2";
    const string Lot    = "ITEST-UOMF-LOT";

    static void Cleanup(Connection.AmesConnectionFactory f) => Exec(f, """
        DELETE dbo.WH_Inventory  WHERE LotNo = @Lot;
        DELETE dbo.MD_Bom        WHERE VersionID IN (@V1, @V2, @V3);
        DELETE dbo.MD_BomVersion WHERE VersionID IN (@V1, @V2, @V3);
        DELETE dbo.MD_Item       WHERE ItemNo IN (@P, @C, @C2);
        """, ("@Lot", Lot), ("@V1", Ver), ("@V2", Ver2), ("@V3", Ver3), ("@P", Parent), ("@C", Comp), ("@C2", Comp2));

    static string? LineUom(Connection.AmesConnectionFactory f, string bomId) =>
        Scalar(f, "SELECT UOM FROM dbo.MD_Bom WHERE BOMID = @B", ("@B", bomId)) as string;

    static int SetItemUom(MasterDataRepository md, string item, string uom, string name = "itest uom follow") =>
        md.UpdateItem(item, name, "MATERIAL", null, false, null, uom, null, null, null, null, null, null, null, null,
            null, null, false, true, Actor);

    [SkippableFact]
    public void Bom_line_unit_comes_from_the_component_and_follows_item_unit_changes()
    {
        Skip.If(Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null, WriteSkip);
        var f = TryFactory(); Skip.If(f is null, WriteSkip);
        var md = new MasterDataRepository(f!);
        try
        {
            Cleanup(f!);
            foreach (var (no, type, uom) in new[] { (Parent, "ASSY", "EA"), (Comp, "MATERIAL", "KG"), (Comp2, "MATERIAL", "G") })
                md.InsertItem(no, "itest uom follow", type, null, false, null, uom, null, null, null, null, null, null, null, null,
                    null, null, false, true, Actor);

            // 추가·수정 — 단위는 구성품 품목에서
            md.InsertBomVersion(Ver, Parent, "V1.0", new DateOnly(2099, 1, 1), null, null, null, Actor);
            Assert.True(md.InsertBomLine(Line, Parent, Comp, 1, 1m, null, Ver, 10, null, Actor));
            Assert.Equal("KG", LineUom(f!, Line));
            Assert.True(md.UpdateBomLine(Line, Parent, Comp2, 1, 2m, null, 10, null, Actor));
            Assert.Equal("G", LineUom(f!, Line));
            Assert.True(md.UpdateBomLine(Line, Parent, Comp, 1, 2m, null, 10, null, Actor));
            Assert.Equal("KG", LineUom(f!, Line));

            // 승인된 다른 버전의 라인도 품목 단위를 따라간다
            md.InsertBomVersion(Ver2, Parent, "V2.0", new DateOnly(2099, 6, 1), null, null, null, Actor);
            Assert.True(md.InsertBomLine(Line2, Parent, Comp, 1, 3m, null, Ver2, 10, null, Actor));
            Exec(f!, "UPDATE dbo.MD_BomVersion SET Status = 'APPROVED' WHERE VersionID = @V", ("@V", Ver2));

            Assert.Equal(2, SetItemUom(md, Comp, "EA"));
            Assert.Equal("EA", LineUom(f!, Line));
            Assert.Equal("EA", LineUom(f!, Line2));
            Assert.Equal(Actor, Scalar(f!, "SELECT ModifiedBy FROM dbo.MD_Bom WHERE BOMID = @B", ("@B", Line2)) as string);
            Assert.Equal(0, SetItemUom(md, Comp, "EA", "itest uom follow 2"));   // 단위 그대로 — BOM 은 건드리지 않음

            // 엑셀 올리기 — 단위 열 없이 구성품 단위로
            var plan = new BomImport.Version(Ver3, true, Parent, "V3.0", new DateOnly(2099, 9, 1), null, null, null,
                [new BomImport.Line(2, Comp2, 1m, null, 10, null)]);
            Assert.Equal(1, md.ImportBomVersion(plan, Actor));
            Assert.Equal("G", Scalar(f!, "SELECT UOM FROM dbo.MD_Bom WHERE VersionID = @V AND CompItemNo = @C", ("@V", Ver3), ("@C", Comp2)) as string);

            // 재고가 있는 품목은 단위를 바꿀 수 없고 BOM 도 그대로
            Exec(f!, "INSERT dbo.WH_Inventory (LotNo, PartNo, Qty, ReceivedAt) VALUES (@Lot, @C, 5, SYSDATETIME())", ("@Lot", Lot), ("@C", Comp));
            Assert.Throws<MasterDataRepository.ItemUomLockedException>(() => SetItemUom(md, Comp, "KG"));
            Assert.Equal("EA", Scalar(f!, "SELECT DefaultUOM FROM dbo.MD_Item WHERE ItemNo = @C", ("@C", Comp)) as string);
            Assert.Equal("EA", LineUom(f!, Line));
            Assert.Equal("EA", LineUom(f!, Line2));
        }
        finally { Cleanup(f!); }
    }
}
