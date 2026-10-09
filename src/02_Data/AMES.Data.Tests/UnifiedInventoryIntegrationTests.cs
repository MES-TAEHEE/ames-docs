using AMES.Data.Repositories;
using Microsoft.Data.SqlClient;
using Xunit;
using static AMES.Data.Tests.AmesDevDb;

namespace AMES.Data.Tests;

public sealed class UnifiedInventoryIntegrationTests
{
    const string Item = "ITEST-INV-ITEM";
    const string MatLocation = "ITEST-INV-MAT";
    const string FgLocation = "ITEST-INV-FG";
    const string MntLocation = "ITEST-INV-MNT";

    static void Seed(AMES.Data.Connection.AmesConnectionFactory f)
    {
        Cleanup(f);
        Exec(f, """
            INSERT dbo.MD_Item (ItemNo,ItemName,ItemType,DefaultUOM,ActiveFlag,CreatedBy)
            VALUES (@Item,N'Unified inventory test','MATERIAL','EA',1,'ITEST');
            INSERT dbo.MD_Location
                (LocationID,LocationName,WhCode,AreaCode,ZoneCode,ActiveFlag,CreatedBy,CreatedTS)
            VALUES (@Mat,N'Material','EOS','MAT_AREA','A',1,'ITEST',SYSDATETIME()),
                   (@Fg,N'Finished goods','EOS','FG_AREA','A',1,'ITEST',SYSDATETIME()),
                   (@Mnt,N'Maintenance','EOS','MNT_AREA','A',1,'ITEST',SYSDATETIME());
            INSERT dbo.WH_Inventory
                (LotNo,UnitType,PartNo,PartName,LocationNo,Qty,ReceivedAt,CreatedAt,UpdatedAt)
            VALUES ('ITEST-INV-LOT-MAT','PART',@Item,N'Unified inventory test',@Mat,3,SYSDATETIME(),SYSDATETIME(),SYSDATETIME()),
                   ('ITEST-INV-LOT-FG','PART',@Item,N'Unified inventory test',@Fg,5,SYSDATETIME(),SYSDATETIME(),SYSDATETIME()),
                   ('ITEST-INV-LOT-MNT','PART',@Item,N'Unified inventory test',@Mnt,7,SYSDATETIME(),SYSDATETIME(),SYSDATETIME());
            """, ("@Item", Item), ("@Mat", MatLocation), ("@Fg", FgLocation), ("@Mnt", MntLocation));
    }

    static void Cleanup(AMES.Data.Connection.AmesConnectionFactory f)
        => Exec(f, """
            DELETE dbo.WH_Inventory WHERE LotNo LIKE 'ITEST-INV-LOT-%';
            DELETE dbo.MD_Location WHERE LocationID IN (@Mat,@Fg,@Mnt);
            DELETE dbo.MD_Item WHERE ItemNo=@Item;
            """, ("@Item", Item), ("@Mat", MatLocation), ("@Fg", FgLocation), ("@Mnt", MntLocation));

    [SkippableFact]
    public void Reports_split_unified_inventory_by_area()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Seed(f!);
        try
        {
            var repo = new RptRepository(f!);
            var sku = repo.ListInventorySku().Where(x => x.ItemNo == Item).OrderBy(x => x.Source).ToList();

            Assert.Collection(sku,
                fg => { Assert.Equal("FG", fg.Source); Assert.Equal(5m, fg.Qty); Assert.Equal(FgLocation, fg.Location); },
                wh => { Assert.Equal("WH", wh.Source); Assert.Equal(3m, wh.Qty); Assert.Equal(MatLocation, wh.Location); });
            var fgInventory = Assert.Single(repo.ListInventory(1000), x => x.ItemNo == Item);
            Assert.Equal(5m, fgInventory.Qty);
            Assert.Equal(FgLocation, fgInventory.Location);
        }
        finally { Cleanup(f!); }
    }

    [SkippableFact]
    public void Master_data_delete_is_blocked_while_inventory_exists()
    {
        var f = TryFactory(); Skip.If(f is null, "AMES_DEV unreachable");
        Seed(f!);
        try
        {
            var repo = new MasterDataRepository(f!);
            Assert.Contains(repo.DeleteItem(Item), u => u.Kind == "STOCK");
            Assert.True(repo.ItemExists(Item));
            Assert.Equal(52071, Assert.Throws<SqlException>(() => repo.DeleteLocation(MatLocation)).Number);

            Exec(f!, "DELETE dbo.WH_Inventory WHERE LotNo LIKE 'ITEST-INV-LOT-%';");
            repo.DeleteLocation(MatLocation);
            repo.DeleteLocation(FgLocation);
            repo.DeleteLocation(MntLocation);
            Assert.Empty(repo.DeleteItem(Item));
            Assert.False(repo.ItemExists(Item));
        }
        finally { Cleanup(f!); }
    }
}
