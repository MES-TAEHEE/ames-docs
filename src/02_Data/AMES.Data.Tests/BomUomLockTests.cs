using AMES.Data.Repositories;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests;

// 10-20 — 구성품에 재고·실적(자동 차감 포함)이 있으면 BOM 라인 단위를 바꿀 수 없다. 다른 필드·다른 구성품으로 바꾸기는 그대로
public class BomUomLockTests
{
    const string WriteSkip = "set AMES_TEST_CONN (local DB) to run — creates and removes test items, a BOM version, a line, stock and a transaction";
    const string Actor = "itest-uomlock";
    const string Parent = "ITEST-UOMLOCK-P";
    const string Comp   = "ITEST-UOMLOCK-C";
    const string Comp2  = "ITEST-UOMLOCK-C2";
    const string Ver    = "ITEST-UOMLOCK-V";
    const string Line   = "ITEST-UOMLOCK-L";
    const string Lot    = "ITEST-UOMLOCK-LOT";

    static void Cleanup(Connection.AmesConnectionFactory f) => Exec(f, """
        DELETE dbo.WH_InventoryTransaction WHERE LotNo = @Lot;
        DELETE dbo.WH_Inventory            WHERE LotNo = @Lot;
        DELETE dbo.MD_Bom        WHERE VersionID = @V;
        DELETE dbo.MD_BomVersion WHERE VersionID = @V;
        DELETE dbo.MD_Item       WHERE ItemNo IN (@P, @C, @C2);
        """, ("@Lot", Lot), ("@V", Ver), ("@P", Parent), ("@C", Comp), ("@C2", Comp2));

    static string? Uom(Connection.AmesConnectionFactory f) =>
        Scalar(f, "SELECT UOM FROM dbo.MD_Bom WHERE BOMID = @B", ("@B", Line)) as string;

    static bool Update(MasterDataRepository md, string comp, string uom, decimal qty = 1m) =>
        md.UpdateBomLine(Line, Parent, comp, 1, qty, uom, null, 10, null, Actor);

    [SkippableFact]
    public void Unit_locks_while_the_component_has_stock_or_backflush_records()
    {
        Skip.If(Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null, WriteSkip);
        var f = TryFactory(); Skip.If(f is null, WriteSkip);
        var md = new MasterDataRepository(f!);
        try
        {
            Cleanup(f!);
            foreach (var (no, type) in new[] { (Parent, "ASSY"), (Comp, "MATERIAL"), (Comp2, "MATERIAL") })
                md.InsertItem(no, "itest uom lock", type, null, false, null, "EA", null, null, null, null, null, null, null, null,
                    null, null, false, true, Actor);
            md.InsertBomVersion(Ver, Parent, "V1.0", new DateOnly(2099, 1, 1), null, null, null, Actor);
            Assert.True(md.InsertBomLine(Line, Parent, Comp, 1, 1m, "EA", null, Ver, 10, null, Actor));

            Assert.False(md.BomUomLocked(Comp));                 // 재고·실적 없음 — 단위 변경 가능
            Assert.True(Update(md, Comp, "KG"));
            Assert.Equal("KG", Uom(f!));
            Assert.True(Update(md, Comp, "EA"));

            Exec(f!, "INSERT dbo.WH_Inventory (LotNo, PartNo, Qty, ReceivedAt) VALUES (@Lot, @C, 5, SYSDATETIME())", ("@Lot", Lot), ("@C", Comp));
            Assert.True(md.BomUomLocked(Comp));                  // 재고 있음 — 잠김
            Assert.Throws<MasterDataRepository.BomUomLockedException>(() => Update(md, Comp, "KG"));
            Assert.Equal("EA", Uom(f!));
            Assert.True(Update(md, Comp, "EA", 2m));             // 단위 그대로 다른 값 수정은 된다
            Assert.True(Update(md, Comp2, "KG"));                // 다른 구성품으로 바꾸면 그 구성품 기준
            Assert.True(Update(md, Comp, "EA"));

            Exec(f!, "UPDATE dbo.WH_Inventory SET Qty = 0 WHERE LotNo = @Lot", ("@Lot", Lot));
            Assert.False(md.BomUomLocked(Comp));                 // 재고 0 은 잠그지 않는다
            Exec(f!, """
                INSERT dbo.WH_InventoryTransaction (TransactionType, QtyChange, LotNo, PartNo, SourceType, CreatedBy)
                VALUES ('OUT', -1, @Lot, @C, 'BACKFLUSH', @By)
                """, ("@Lot", Lot), ("@C", Comp), ("@By", Actor));
            Assert.True(md.BomUomLocked(Comp));                  // 자동 차감 기록 — 잠김
            Assert.Throws<MasterDataRepository.BomUomLockedException>(() => Update(md, Comp, "G"));
            Assert.Equal("EA", Uom(f!));
        }
        finally { Cleanup(f!); }
    }
}
