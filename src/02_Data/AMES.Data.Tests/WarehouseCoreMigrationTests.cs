using AMES.Data.Connection;
using AMES.Data.Repositories;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AMES.Data.Tests;

public class WarehouseCoreMigrationTests
{
    [SqlServerFact]
    public void Indexed_lot_migration_is_repeatable_and_pick_lines_and_long_locations_work()
    {
        var settings = SqlServerFactAttribute.Settings();
        var database = "AMES_WH_TEST_" + Guid.NewGuid().ToString("N");
        using var master = new SqlConnection(settings.ConnectionString);
        master.Open();
        using (var create = new SqlCommand($"CREATE DATABASE [{database}]", master)) create.ExecuteNonQuery();
        try
        {
            settings.InitialCatalog = database;
            using var conn = new SqlConnection(settings.ConnectionString);
            conn.Open();
            void Execute(string sql) { using var cmd = new SqlCommand(sql, conn); cmd.ExecuteNonQuery(); }
            Execute("""
                CREATE TABLE WH_InventoryTransaction(TransactionID int,TransactionType varchar(10),
                    PartNo varchar(50),LocationNo varchar(50),LotNo varchar(50) NULL,
                    QtyBefore decimal(18,3),QtyChange decimal(18,3),QtyAfter decimal(18,3),SourceID int,SourceType varchar(30));
                INSERT WH_InventoryTransaction VALUES(1,'OUT','PART-1','LOC-1','BOX-1',10,-5,5,1,'PICK_SLIP');
                CREATE INDEX IX_WH_InventoryTransaction_LotNo ON WH_InventoryTransaction(LotNo);
                CREATE INDEX IX_WH_InventoryTransaction_Search ON WH_InventoryTransaction(TransactionType,PartNo,LocationNo,LotNo);
                CREATE TABLE WH_PickSlip(PickSlipID int,PickSlipNo nvarchar(40),ItemNo varchar(50),DemandQty decimal(18,3),ReqUserId varchar(20),Status varchar(20));
                INSERT WH_PickSlip VALUES(1,'PICK-1','PART-1',10,'TEST','PARTIAL');
                CREATE TABLE MD_Item(ItemNo varchar(50),ItemName nvarchar(100));
                INSERT MD_Item VALUES('PART-1','Test part');
                CREATE TABLE WH_Inventory(LotNo nvarchar(50),PartNo varchar(50),LocationNo varchar(50),Qty decimal(18,3),ReceivedAt datetime2);
                INSERT WH_Inventory VALUES('BOX-2','PART-1','LOC-1',5,SYSDATETIME());
                CREATE TABLE MD_Location(LocationID varchar(50));
                INSERT MD_Location VALUES(REPLICATE('L',50));
                """);
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName,"dist","migrate_wh_core_tables.sql"))) root=root.Parent;
            Assert.NotNull(root);
            var sql = File.ReadAllText(Path.Combine(root.FullName,"dist","migrate_wh_core_tables.sql"));
            // Normalize newlines so the block boundaries work in both Git checkout modes.
            sql = sql.Replace("\r\n", "\n");
            var start = sql.IndexOf("    IF EXISTS(SELECT 1 FROM sys.indexes", StringComparison.Ordinal);
            var end = sql.IndexOf("    IF EXISTS\n", start, StringComparison.Ordinal);
            Assert.True(start >= 0 && end > start);
            Execute(sql[start..end]);
            Execute(sql[start..end]);
            using (var verify = new SqlCommand("SELECT COUNT(*) FROM sys.indexes WHERE object_id=OBJECT_ID('WH_InventoryTransaction') AND name='IX_WH_InventoryTransaction_LotNo'", conn))
                Assert.Equal(1, Convert.ToInt32(verify.ExecuteScalar()));
            start = sql.IndexOf("CREATE OR ALTER PROCEDURE dbo.WH_PDA_RELEASE_PICK_LINES ", StringComparison.Ordinal);
            end = sql.IndexOf("\nGO", start, StringComparison.Ordinal);
            Execute(sql[start..end]);
            using (var query = new SqlCommand("EXEC dbo.WH_PDA_RELEASE_PICK_LINES N'PICK-1'", conn))
            using (var rows = query.ExecuteReader())
            {
                Assert.True(rows.Read());
                Assert.Equal("PART-1", rows["PARTNO"]);
                Assert.Equal(5m, rows["PICKED_QTY"]);
                Assert.Equal("LOC-1", rows["LOC_01"]);
            }
            var repo = new WarehouseRepository(new AmesConnectionFactory(settings.ConnectionString));
            Assert.True(repo.LocationExists(new string('L',50)));
            repo.DeleteLocation(new string('L',50));
            Assert.False(repo.LocationExists(new string('L',50)));
        }
        finally
        {
            SqlConnection.ClearAllPools();
            using var drop = new SqlCommand($"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}];", master);
            drop.ExecuteNonQuery();
        }
    }
}
