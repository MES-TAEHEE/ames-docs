using AMES.Data.Repositories;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests;

// 10-20 — 재고·실적(자동 차감 포함)이 있는 품목은 기본 단위(MD_Item.DefaultUOM)를 바꿀 수 없다. 다른 값 수정은 그대로
public class ItemUomLockTests
{
    const string WriteSkip = "set AMES_TEST_CONN (local DB) to run — creates and removes a test item, stock and a transaction";
    const string Actor = "itest-itemuom";
    const string Item  = "ITEST-ITEMUOM";
    const string Lot   = "ITEST-ITEMUOM-LOT";

    static void Cleanup(Connection.AmesConnectionFactory f) => Exec(f, """
        DELETE dbo.WH_InventoryTransaction WHERE LotNo = @Lot;
        DELETE dbo.WH_Inventory            WHERE LotNo = @Lot;
        DELETE dbo.MD_Item                 WHERE ItemNo = @I;
        """, ("@Lot", Lot), ("@I", Item));

    static string? Uom(Connection.AmesConnectionFactory f) =>
        Scalar(f, "SELECT DefaultUOM FROM dbo.MD_Item WHERE ItemNo = @I", ("@I", Item)) as string;

    static void Update(MasterDataRepository md, string uom, string name = "itest item uom") =>
        md.UpdateItem(Item, name, "MATERIAL", null, false, null, uom, null, null, null, null, null, null, null, null,
            null, null, false, true, Actor);

    [SkippableFact]
    public void Unit_locks_while_the_item_has_stock_or_backflush_records()
    {
        Skip.If(Environment.GetEnvironmentVariable("AMES_TEST_CONN") is null, WriteSkip);
        var f = TryFactory(); Skip.If(f is null, WriteSkip);
        var md = new MasterDataRepository(f!);
        try
        {
            Cleanup(f!);
            md.InsertItem(Item, "itest item uom", "MATERIAL", null, false, null, "EA", null, null, null, null, null, null, null, null,
                null, null, false, true, Actor);

            Assert.False(md.ItemUomInUse(Item));                // 재고·실적 없음 — 단위 변경 가능
            Update(md, "KG");
            Assert.Equal("KG", Uom(f!));
            Update(md, "EA");

            Exec(f!, "INSERT dbo.WH_Inventory (LotNo, PartNo, Qty, ReceivedAt) VALUES (@Lot, @I, 5, SYSDATETIME())", ("@Lot", Lot), ("@I", Item));
            Assert.True(md.ItemUomInUse(Item));                 // 재고 있음 — 잠김
            Assert.Throws<MasterDataRepository.ItemUomLockedException>(() => Update(md, "KG"));
            Assert.Equal("EA", Uom(f!));
            Update(md, "EA", "itest item uom 2");               // 단위 그대로 다른 값 수정은 된다
            Assert.Equal("itest item uom 2", Scalar(f!, "SELECT ItemName FROM dbo.MD_Item WHERE ItemNo = @I", ("@I", Item)) as string);

            Exec(f!, "UPDATE dbo.WH_Inventory SET Qty = 0 WHERE LotNo = @Lot", ("@Lot", Lot));
            Assert.False(md.ItemUomInUse(Item));                // 재고 0 은 잠그지 않는다
            Exec(f!, """
                INSERT dbo.WH_InventoryTransaction (TransactionType, QtyChange, LotNo, PartNo, SourceType, CreatedBy)
                VALUES ('OUT', -1, @Lot, @I, 'BACKFLUSH', @By)
                """, ("@Lot", Lot), ("@I", Item), ("@By", Actor));
            Assert.True(md.ItemUomInUse(Item));                 // 자동 차감 기록 — 잠김
            Assert.Throws<MasterDataRepository.ItemUomLockedException>(() => Update(md, "G"));
            Assert.Equal("EA", Uom(f!));
        }
        finally { Cleanup(f!); }
    }
}
