using AMES.Data.Connection;
using AMES.Data.Repositories;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AMES.Data.Tests;

public sealed class DispatchPreparationTests
{
    [SqlServerFact]
    public async Task Preparation_loading_and_shipping_preserve_stock_until_final_confirmation()
    {
        var database="AMES_DISPATCH_TEST_"+Guid.NewGuid().ToString("N");
        var settings=SqlServerFactAttribute.Settings();
        using var master=new SqlConnection(settings.ConnectionString);master.Open();
        using(var create=new SqlCommand($"CREATE DATABASE [{database}]",master))create.ExecuteNonQuery();
        try
        {
            settings.InitialCatalog=database;var factory=new AmesConnectionFactory(settings.ConnectionString);
            void Sql(string text){using var c=factory.OpenConnection();using var cmd=new SqlCommand(text,c);cmd.ExecuteNonQuery();}
            decimal Number(string text){using var c=factory.OpenConnection();using var cmd=new SqlCommand(text,c);return Convert.ToDecimal(cmd.ExecuteScalar());}
            Sql("""
                CREATE TABLE MD_Item(ItemNo varchar(20),ItemName nvarchar(200));
                CREATE TABLE MD_Customer(CustomerID varchar(20),CustomerCode varchar(20),CustomerName nvarchar(100),Status varchar(20));
                CREATE TABLE MD_Location(LocationID varchar(20),LocationLevel int,ActiveFlag bit,LocationType varchar(20),LocationName nvarchar(100));
                CREATE TABLE MD_Pallet(PalletNo varchar(50) PRIMARY KEY,ShippedFlag bit,ShippedAt datetime2,ShippedCustomerID varchar(20),ReturnedAt datetime2,ModifiedBy varchar(20),ModifiedTS datetime2);
                CREATE TABLE tbl_Lot(LotID int,LotCode nvarchar(50),Status varchar(20));
                CREATE TABLE PR_ImgLot(LotID int,CustomerCode varchar(20));
                CREATE TABLE PR_PntLot(LotID int,CustomerCode varchar(20));
                CREATE TABLE FG_CustomerReturn(LotNo nvarchar(50),Status varchar(20));
                CREATE TABLE WH_Inventory(LotNo nvarchar(50) PRIMARY KEY,UnitType varchar(10),ParentLotNo nvarchar(50),PartNo varchar(20),PartName nvarchar(200),LocationNo varchar(20),Qty decimal(18,3),PalletNo varchar(50),ReceivedAt datetime2 DEFAULT SYSDATETIME(),UpdatedAt datetime2);
                CREATE TABLE FG_Shipment(ShipmentID int IDENTITY PRIMARY KEY,LotNo nvarchar(50),UnitType varchar(10),PalletNo varchar(50),CustomerID varchar(20),ShippedAt datetime2,ShippedBy varchar(20));
                CREATE TABLE WH_InventoryTransaction(TransactionID int IDENTITY,TransactionTime datetime2,TransactionType varchar(20),PartNo varchar(20),LocationNo varchar(20),LotNo nvarchar(50),QtyBefore decimal(18,3),QtyChange decimal(18,3),QtyAfter decimal(18,3),ReasonCode varchar(30),SourceType varchar(30),SourceID int,OperatorID varchar(20),Note nvarchar(200),CreatedBy varchar(20),CreatedTS datetime2);
                CREATE TABLE FG_Delivery(DeliveryID int PRIMARY KEY,DeliveryNo varchar(30),CustomerID varchar(20),ShipDate date,TrailerNo nvarchar(50),Status varchar(20),ShippedBy varchar(20),ShippedAt datetime2);
                CREATE TABLE FG_ShippingOrder(OrderID int PRIMARY KEY,OrderNo varchar(30),DeliveryID int,Status varchar(20),ModifiedBy varchar(20),ModifiedTS datetime2);
                CREATE TABLE FG_ShippingOrderLine(OrderLineID int,OrderID int,SoID int,PartNo varchar(20),Qty decimal(18,3));
                CREATE TABLE PP_CustomerOrder(SoID int,ItemNo varchar(20),CustomerID varchar(20),OrderQty decimal(18,3),ShippedQty decimal(18,3),Status varchar(20),ModifiedBy varchar(20),ModifiedTS datetime2);
                CREATE TABLE FG_DeliveryLoad(LoadID int IDENTITY PRIMARY KEY,DeliveryID int NOT NULL,RootLotNo nvarchar(50) NOT NULL,LotNo nvarchar(50) NOT NULL,PartNo varchar(20) NOT NULL,Qty decimal(18,3) NOT NULL,LoadedBy varchar(20) NOT NULL,LoadedAt datetime2 NOT NULL DEFAULT SYSDATETIME(),ShipmentID int NULL);
                CREATE UNIQUE INDEX UX_FG_DeliveryLoad_Lot ON FG_DeliveryLoad(LotNo) WHERE ShipmentID IS NULL;
                INSERT FG_DeliveryLoad(DeliveryID,RootLotNo,LotNo,PartNo,Qty,LoadedBy,ShipmentID) VALUES(99,'OLD','OLD','A',1,'TEST',99);
                INSERT MD_Item VALUES('A','Part A'),('B','Part B');
                INSERT MD_Customer VALUES('C1','C1','Customer one','ACTIVE'),('C2','C2','Customer two','ACTIVE');
                INSERT MD_Location(LocationID,LocationLevel,ActiveFlag,LocationType) VALUES('FG01',2,1,'FG'),('WH01',2,1,'WH');
                INSERT MD_Pallet(PalletNo,ShippedFlag) VALUES('PAL-1',0),('PAL-2',0);
                INSERT WH_Inventory(LotNo,UnitType,ParentLotNo,PartNo,PartName,LocationNo,Qty,PalletNo) VALUES
                  ('PAL-1','PALLET',NULL,NULL,NULL,'FG01',2,'PAL-1'),('A-1','PART','PAL-1','A','Part A','FG01',1,'PAL-1'),('A-2','PART','PAL-1','A','Part A','FG01',1,'PAL-1'),
                  ('PAL-2','PALLET',NULL,NULL,NULL,'FG01',2,'PAL-2'),('A-3','PART','PAL-2','A','Part A','FG01',1,'PAL-2'),('A-4','BOX','PAL-2','A','Part A','FG01',1,'PAL-2'),
                  ('A-5','BOX',NULL,'A','Part A','FG01',1,NULL),('A-6','PART',NULL,'A','Part A','FG01',1,NULL),('B-1','PART',NULL,'B','Part B','FG01',1,NULL),('WH-A','PART',NULL,'A','Part A','WH01',1,NULL),('OTHER-C','PART',NULL,'A','Part A','FG01',1,NULL);
                INSERT tbl_Lot VALUES(1,'OTHER-C','CONFIRMED');INSERT PR_ImgLot VALUES(1,'C2');
                INSERT FG_Delivery(DeliveryID,DeliveryNo,CustomerID,ShipDate,Status) VALUES(1,'DCR-1','C1',GETDATE(),'Open'),(2,'DCR-2','C1',GETDATE(),'Open'),(3,'DCR-3','C1',GETDATE(),'Open'),(4,'DCR-4','C1',GETDATE(),'Open'),(5,'DCR-5','C1',GETDATE(),'Open'),(6,'DCR-6','C1',GETDATE(),'Open'),(7,'DCR-7','C1',GETDATE(),'Open');
                INSERT FG_ShippingOrder(OrderID,OrderNo,DeliveryID,Status) VALUES(1,'ORD-1',1,'Open'),(2,'ORD-2',2,'Open'),(3,'ORD-3',3,'Open'),(4,'ORD-4',4,'Open'),(5,'ORD-5',5,'Open'),(6,'ORD-6',6,'Open'),(7,'ORD-7',7,'Open');
                INSERT FG_ShippingOrderLine VALUES(1,1,1,'A',2),(2,2,2,'A',1),(3,3,3,'A',2),(4,4,4,'A',1),(5,5,5,'A',1),(6,6,6,'A',1),(7,7,7,'A',1);
                INSERT PP_CustomerOrder(SoID,ItemNo,CustomerID,OrderQty,ShippedQty,Status) VALUES(1,'A','C1',2,0,'Open'),(2,'A','C1',1,0,'Open'),(3,'A','C1',2,0,'Open'),(4,'A','C1',1,0,'Open'),(5,'A','C1',1,0,'Open'),(6,'A','C1',1,0,'Open'),(7,'A','C1',1,0,'Open');
                UPDATE WH_Inventory SET ReceivedAt=CASE WHEN LotNo IN('A-1','A-2') THEN '20261101' WHEN LotNo IN('A-3','A-4') THEN '20261102' ELSE '20261103' END;
                """);
            var root=new DirectoryInfo(AppContext.BaseDirectory);while(root is not null&&!File.Exists(Path.Combine(root.FullName,"dist","migrate_fg_dispatch_preparation.sql")))root=root.Parent;
            Assert.NotNull(root);var migration=File.ReadAllText(Path.Combine(root.FullName,"dist","migrate_fg_dispatch_preparation.sql"));Sql(migration);Sql(migration);
            Assert.Equal(1,Number("SELECT COUNT(*) FROM FG_DeliveryLoad WHERE LotNo='OLD' AND LoadState='Loaded' AND ShipmentID=99"));
            var repo=new FinishedGoodsRepository(factory);
            var stock=repo.ListShippableStock();
            Assert.Equal(7m,stock["A"].Loadable);Assert.Equal(1m,stock["A"].Unlocated);Assert.Equal(2,stock["A"].Pallets);
            Assert.Throws<SqlException>(()=>repo.LoadShippingDelivery("DCR-1","A-1","TEST",true));
            Assert.Throws<InvalidOperationException>(()=>repo.PrepareShippingDelivery("DCR-1","B-1","TEST",true));
            Assert.Throws<SqlException>(()=>repo.PrepareShippingDelivery("DCR-1","WH-A","TEST",true));
            Assert.Throws<InvalidOperationException>(()=>repo.PrepareShippingDelivery("DCR-1","OTHER-C","TEST",true));
            var pick=Assert.Single(repo.GetShippingDelivery("DCR-1").Recommendations!);
            Assert.Equal("PAL-1",pick.Barcode);Assert.Equal("FG01",pick.LocationNo);Assert.Equal(2m,pick.Qty);Assert.Equal(new DateTime(2026,11,1),pick.ReceivedAt);
            var prepared=repo.PrepareShippingDelivery("DCR-1","PAL-1","TEST",true);
            Assert.Equal(2,Assert.Single(prepared.Parts).PreparedQty);Assert.Empty(prepared.Loads);Assert.Equal(2,prepared.Preparations!.Count);Assert.Empty(prepared.Recommendations!);
            Assert.Equal(2m,Assert.Single(repo.GetShippingDelivery("DCR-2").Parts).StagingQty);
            Assert.Equal(2,Number("SELECT SUM(Qty) FROM WH_Inventory WHERE LotNo IN('A-1','A-2')"));Assert.Equal(0,Number("SELECT COUNT(*) FROM FG_Shipment"));
            Assert.Equal(0,Number("SELECT COUNT(*) FROM WH_InventoryTransaction"));Assert.Equal(0,Number("SELECT ShippedQty FROM PP_CustomerOrder WHERE SoID=1"));
            Assert.Throws<SqlException>(()=>repo.PrepareShippingDelivery("DCR-1","A-1","TEST",true));
            Assert.Throws<SqlException>(()=>repo.PrepareShippingDelivery("DCR-2","A-1","TEST",true));
            var first=repo.LoadShippingDelivery("DCR-1","A-1","TEST",true);Assert.Equal(1,Assert.Single(first.Parts).LoadedQty);
            Assert.Throws<InvalidOperationException>(()=>repo.CompleteShippingDelivery("DCR-1","TEST",true));
            Sql("UPDATE WH_Inventory SET Qty=2 WHERE LotNo='A-2'");
            Assert.Equal(2m,Assert.Single(repo.GetShippingDelivery("DCR-1").Parts).StagingQty);
            Assert.Throws<InvalidOperationException>(()=>repo.LoadShippingDelivery("DCR-1","A-2","TEST",true));
            Sql("UPDATE WH_Inventory SET Qty=1 WHERE LotNo='A-2'");
            var attempts=await Task.WhenAll(Enumerable.Range(0,2).Select(_=>Task.Run(()=>{try{repo.LoadShippingDelivery("DCR-1","A-2","TEST",true);return true;}catch(Exception e)when(e is SqlException or InvalidOperationException){return false;}})));
            Assert.Single(attempts,x=>x);
            Assert.Equal(2,repo.GetShippingDelivery("DCR-1").Loads.Count);
            var unloaded=repo.UnloadShippingDelivery("DCR-1","A-1","TEST");Assert.Equal(1,Assert.Single(unloaded.Parts).LoadedQty);Assert.Single(unloaded.Preparations!);
            repo.LoadShippingDelivery("DCR-1","A-1","TEST",true);
            Assert.Equal(0,Number("SELECT COUNT(*) FROM FG_Shipment"));
            Sql("UPDATE PP_CustomerOrder SET OrderQty=1 WHERE SoID=1");
            Assert.Throws<SqlException>(()=>repo.CompleteShippingDelivery("DCR-1","TEST",true));
            Assert.Equal(0,Number("SELECT COUNT(*) FROM FG_Shipment"));Assert.Equal(2,Number("SELECT SUM(Qty) FROM WH_Inventory WHERE LotNo IN('A-1','A-2')"));
            Sql("UPDATE PP_CustomerOrder SET OrderQty=2 WHERE SoID=1");
            var shipped=repo.CompleteShippingDelivery("DCR-1","TEST",true);Assert.Equal("Shipped",shipped.Status);
            Assert.Equal(0,Number("SELECT Qty FROM WH_Inventory WHERE LotNo='PAL-1'"));Assert.Equal(0,Number("SELECT SUM(Qty) FROM WH_Inventory WHERE LotNo IN('A-1','A-2')"));
            Assert.Equal(2,Number("SELECT COUNT(*) FROM FG_Shipment"));Assert.Equal(2,Number("SELECT COUNT(*) FROM WH_InventoryTransaction WHERE TransactionType='OUT'"));Assert.Equal(2,Number("SELECT ShippedQty FROM PP_CustomerOrder WHERE SoID=1"));
            Assert.Equal(0,Number("SELECT CONVERT(int,ShippedFlag) FROM MD_Pallet WHERE PalletNo='PAL-1'"));
            repo.CompleteShippingDelivery("DCR-1","TEST",true);Assert.Equal(2,Number("SELECT COUNT(*) FROM FG_Shipment"));
            repo.PrepareShippingDelivery("DCR-5","A-5","TEST",true);repo.LoadShippingDelivery("DCR-5","A-5","TEST",true);
            repo.UnloadShippingDelivery("DCR-5","A-5","TEST");repo.ReleaseShippingDelivery(5,"TEST");
            Assert.Equal(1,Number("SELECT Qty FROM WH_Inventory WHERE LotNo='A-5'"));
            Assert.Equal(1,Number("SELECT COUNT(*) FROM FG_DeliveryLoad WHERE LotNo='A-5' AND LoadState='Prepared'"));
            var over=repo.PrepareShippingDelivery("DCR-2","PAL-2","TEST",true);
            Assert.Equal(3m,Assert.Single(over.Parts).PreparedQty);Assert.Equal(1m,Assert.Single(over.Parts).RequiredQty);
            Assert.Empty(over.Recommendations!);
            Assert.Throws<InvalidOperationException>(()=>repo.LoadShippingDelivery("DCR-2","PAL-2","TEST",true));
            repo.LoadShippingDelivery("DCR-3","PAL-2","TEST",true);repo.CompleteShippingDelivery("DCR-3","TEST",true);
            Assert.Equal(1,Number("SELECT CONVERT(int,ShippedFlag) FROM MD_Pallet WHERE PalletNo='PAL-2'"));
            Assert.Equal(1m,Assert.Single(repo.GetShippingDelivery("DCR-4").Parts).StagingQty);
            repo.LoadShippingDelivery("DCR-4","A-5","TEST",true);repo.CompleteShippingDelivery("DCR-4","TEST",true);
            var reservations=await Task.WhenAll(new[]{"DCR-6","DCR-7"}.Select(d=>Task.Run(()=>{try{repo.PrepareShippingDelivery(d,"A-6","TEST",true);return d;}catch(SqlException){return null;}})));
            var winner=Assert.Single(reservations,x=>x is not null)!;
            Assert.Equal(1,Number("SELECT COUNT(*) FROM FG_DeliveryLoad WHERE LotNo='A-6' AND ShipmentID IS NULL"));
            repo.ReleaseShippingDelivery(winner=="DCR-6"?6:7,"TEST");
            Assert.Equal(1,Number("SELECT COUNT(*) FROM FG_DeliveryLoad WHERE LotNo='A-6' AND LoadState='Prepared' AND ShipmentID IS NULL"));
            Assert.Equal(1,Number("SELECT Qty FROM WH_Inventory WHERE LotNo='A-6'"));
            var next=winner=="DCR-6"?"DCR-7":"DCR-6";
            repo.LoadShippingDelivery(next,"A-6","TEST",true);repo.CompleteShippingDelivery(next,"TEST",true);
            Assert.Equal(0,Number("SELECT Qty FROM WH_Inventory WHERE LotNo='A-6'"));
            Assert.Empty(repo.GetShippingDelivery("DCR-2").Recommendations!);
            Sql("""
                INSERT MD_Pallet(PalletNo,ShippedFlag) VALUES('PAL-16',0),('PAL-20',0);
                INSERT WH_Inventory(LotNo,UnitType,PartNo,LocationNo,Qty,PalletNo) VALUES('PAL-16','PALLET',NULL,'FG01',16,'PAL-16'),('PAL-20','PALLET',NULL,'FG01',20,'PAL-20');
                ;WITH N AS(SELECT V.n FROM(VALUES(1),(2),(3),(4),(5),(6),(7),(8),(9),(10),(11),(12),(13),(14),(15),(16),(17),(18),(19),(20))V(n))
                INSERT WH_Inventory(LotNo,UnitType,ParentLotNo,PartNo,PartName,LocationNo,Qty,PalletNo,ReceivedAt)
                SELECT CONCAT('P16-',RIGHT(CONCAT('0',n),2)),'PART','PAL-16','A','Part A','FG01',1,'PAL-16','20261101' FROM N WHERE n<=16
                UNION ALL SELECT CONCAT('P20-',RIGHT(CONCAT('0',n),2)),'PART','PAL-20','A','Part A','FG01',1,'PAL-20','20261102' FROM N;
                INSERT FG_Delivery(DeliveryID,DeliveryNo,CustomerID,ShipDate,Status) VALUES(8,'DCR-8','C1',GETDATE(),'Open'),(9,'DCR-9','C1',GETDATE(),'Open');
                INSERT FG_ShippingOrder(OrderID,OrderNo,DeliveryID,Status) VALUES(8,'ORD-8',8,'Open'),(9,'ORD-9',9,'Open');
                INSERT FG_ShippingOrderLine VALUES(8,8,8,'A',20),(9,9,9,'A',16);
                INSERT PP_CustomerOrder(SoID,ItemNo,CustomerID,OrderQty,ShippedQty,Status) VALUES(8,'A','C1',20,0,'Open'),(9,'A','C1',16,0,'Open');
                """);
            var oldest=repo.GetShippingDelivery("DCR-8").Recommendations!;
            Assert.Equal(new[]{"PAL-16","PAL-20"},oldest.Select(r=>r.Barcode));
            var sixteen=repo.PrepareShippingDelivery("DCR-8","PAL-16","TEST",true);
            Assert.Equal(16m,Assert.Single(sixteen.Parts).StagingQty);
            var nextPallet=Assert.Single(sixteen.Recommendations!);Assert.Equal("PAL-20",nextPallet.Barcode);Assert.Equal(20m,nextPallet.Qty);
            var thirtySix=repo.PrepareShippingDelivery("DCR-8","PAL-20","TEST",true);
            Assert.Equal(36m,Assert.Single(thirtySix.Parts).PreparedQty);Assert.Equal(20m,Assert.Single(thirtySix.Parts).RequiredQty);Assert.Empty(thirtySix.Recommendations!);
            repo.LoadShippingDelivery("DCR-8","PAL-16","TEST",true);
            Assert.Throws<InvalidOperationException>(()=>repo.LoadShippingDelivery("DCR-8","PAL-20","TEST",true));
            for(var i=1;i<=4;i++)repo.LoadShippingDelivery("DCR-8",$"P20-{i:D2}","TEST",true);
            repo.CompleteShippingDelivery("DCR-8","TEST",true);
            Assert.Equal(20m,Number("SELECT ShippedQty FROM PP_CustomerOrder WHERE SoID=8"));
            Assert.Equal(16m,Number("SELECT Qty FROM WH_Inventory WHERE LotNo='PAL-20'"));
            var carry=repo.GetShippingDelivery("DCR-9");Assert.Equal(16m,Assert.Single(carry.Parts).StagingQty);Assert.Empty(carry.Recommendations!);
            repo.LoadShippingDelivery("DCR-9","PAL-20","TEST",true);repo.CompleteShippingDelivery("DCR-9","TEST",true);
            Assert.Equal(16m,Number("SELECT ShippedQty FROM PP_CustomerOrder WHERE SoID=9"));Assert.Equal(0m,Number("SELECT Qty FROM WH_Inventory WHERE LotNo='PAL-20'"));

            Sql("""
                INSERT MD_Item VALUES('C','No stock');
                INSERT FG_Delivery(DeliveryID,DeliveryNo,CustomerID,ShipDate,Status) VALUES(10,'DCR-10','C1',GETDATE(),'Open');
                INSERT FG_ShippingOrder(OrderID,OrderNo,DeliveryID,Status) VALUES(10,'ORD-10',10,'Open');
                INSERT FG_ShippingOrderLine VALUES(10,10,10,'B',4),(11,10,11,'C',10);
                """);
            var shortage=repo.GetShippingDelivery("DCR-10");
            Assert.Equal(3m,shortage.Parts.Single(p=>p.PartNo=="B").StockShortageQty);
            Assert.Equal(10m,shortage.Parts.Single(p=>p.PartNo=="C").StockShortageQty);
            Assert.Single(shortage.Recommendations!);
            Assert.All(thirtySix.Parts,p=>Assert.Equal(0m,p.StockShortageQty));
            Sql("""
                ;WITH N AS(SELECT 1 N UNION ALL SELECT N+1 FROM N WHERE N<50)
                INSERT WH_Inventory(LotNo,UnitType,PartNo,PartName,LocationNo,Qty)
                SELECT CONCAT('B-MANY-',N),'PART','B','Part B','FG01',1 FROM N;
                UPDATE FG_ShippingOrderLine SET Qty=100 WHERE OrderID=10 AND PartNo='B';
                """);
            var capped=repo.GetShippingDelivery("DCR-10");
            Assert.Equal(50,capped.Recommendations!.Count);
            Assert.Equal(49m,capped.Parts.Single(p=>p.PartNo=="B").StockShortageQty);

        }
        finally
        {
            SqlConnection.ClearAllPools();using var drop=new SqlCommand($"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}]",master);drop.ExecuteNonQuery();
        }
    }
}
