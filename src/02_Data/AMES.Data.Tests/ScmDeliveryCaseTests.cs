using System.Data;
using System.Text.RegularExpressions;
using AMES.Data.Connection;
using AMES.Data.Repositories;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AMES.Data.Tests;

public sealed class ScmDeliveryCaseTests
{
    [Fact]
    public void Receipt_list_groups_whole_notes_and_keeps_unissued_deliveries_separate()
    {
        var first=new ScmRepository.PortalReceiptLine(1,"D-1","NOTE-1","V-1","Vendor","PO-1",
            "PART-1","Part one","EA",new DateTime(2026,10,5),10,10);
        var documents=ScmRepository.GroupReceiptDocuments([
            first,
            first with { Id=2,Delivery="D-2",Note="note-1",Order="PO-2",Item="PART-2",Unit="G",Quantity=20,Received=0 },
            first with { Id=3,Note="",Delivery="D-3",Received=0 },
            first with { Id=4,Note="",Delivery="D-4" }
        ]);
        Assert.Equal(3,documents.Count);
        var note=documents[0];
        Assert.Equal(2,note.Lines.Count);
        Assert.Equal(2,note.ItemCount);
        Assert.Equal("부분입고",note.ReceiptStatus);
        Assert.Equal(20m,note.Lines.Sum(r=>r.Remaining));
        Assert.Equal(2,note.Lines.GroupBy(r=>r.Unit).Count());
        Assert.Equal("미입고",documents[1].ReceiptStatus);
        Assert.Equal("입고완료",documents[2].ReceiptStatus);
    }

    [SqlServerFact]
    public async Task Prepared_cases_register_ship_and_receive_without_replacing_box_labels()
    {
        // Deliberately isolated: never reads connection settings or touches AMES_DEV.
        var database="AMES_CASE_TEST_"+Guid.NewGuid().ToString("N");
        var server=SqlServerFactAttribute.Settings();
        using var master=new SqlConnection(server.ConnectionString);master.Open();
        using(var cmd=new SqlCommand($"CREATE DATABASE [{database}]",master))cmd.ExecuteNonQuery();
        try
        {
            server.InitialCatalog=database;
            var factory=new AmesConnectionFactory(server.ConnectionString);
            var repo=new ScmRepository(factory);
            void Sql(string sql){using var c=factory.OpenConnection();using var cmd=new SqlCommand(sql,c);cmd.ExecuteNonQuery();}
            int Count(string sql){using var c=factory.OpenConnection();using var cmd=new SqlCommand(sql,c);return Convert.ToInt32(cmd.ExecuteScalar());}
            Dictionary<int,string> Versions(string order)
            {
                using var c=factory.OpenConnection();using var cmd=new SqlCommand("SELECT PoID,ScmRowVersion FROM WH_PurchaseOrder WHERE PoNumber=@N",c);
                cmd.Parameters.AddWithValue("@N",order);using var r=cmd.ExecuteReader();var result=new Dictionary<int,string>();
                while(r.Read())result.Add(r.GetInt32(0),Convert.ToHexString((byte[])r[1]));return result;
            }
            string DeliveryVersion(string number)
            {
                using var c=factory.OpenConnection();using var cmd=new SqlCommand("SELECT Version FROM SCM_Delivery WHERE DeliveryNumber=@N",c);
                cmd.Parameters.AddWithValue("@N",number);return Convert.ToHexString((byte[])cmd.ExecuteScalar()!);
            }
            void Update(string delivery,string order,int po,decimal qty,bool cancel=false,bool ship=false)
                =>repo.UpdateSupplierDelivery(delivery,DateTime.Today,[new(po,qty,"VENDOR-LOT",DateTime.Today)],
                    DeliveryVersion(delivery),Versions(order),"case-user","case-user",cancel,ship);
            var root=new DirectoryInfo(AppContext.BaseDirectory);
            while(root is not null&&!File.Exists(Path.Combine(root.FullName,"dist","migrate_scm_delivery_cases.sql")))root=root.Parent;
            Assert.NotNull(root);
            Sql("""
                CREATE TABLE MD_Vendor(VendorID varchar(20) PRIMARY KEY,VendorName nvarchar(100),ActiveFlag bit);
                CREATE TABLE MD_Item(ItemNo varchar(20) PRIMARY KEY,ItemName nvarchar(200),ItemType varchar(20),DefaultUOM varchar(20),ActiveFlag bit);
                CREATE TABLE SCM_ItemVendor(ItemNo varchar(20),VendorID varchar(20),PackingQty decimal(18,3),ActiveFlag bit,
                    CreatedBy varchar(20),ModifiedBy varchar(20),ModifiedTS datetime2,PRIMARY KEY(ItemNo,VendorID));
                CREATE TABLE SCM_PortalVendorUser(UserID nvarchar(450),VendorID varchar(20),ActiveFlag bit,LockedFlag bit);
                CREATE TABLE WH_PurchaseOrder(PoID int PRIMARY KEY,PoNumber varchar(30),PoLineNo int,VendorID varchar(20),ItemNo varchar(20),UnitCode varchar(20),
                    ReceivedQty decimal(18,3),OrderQty decimal(18,3),Status varchar(20),OrderDate date,DueDate date,SupplierConfirmedAt datetime2,
                    SupplierConfirmedBy varchar(20),SupplierConfirmedUserID nvarchar(450),DeliveryDestination varchar(50),UnitPrice decimal(18,3),Currency varchar(3),
                    ModifiedBy varchar(20),ModifiedTS datetime2,ScmRowVersion rowversion);
                CREATE TABLE SCM_Delivery(DeliveryID int IDENTITY PRIMARY KEY,DeliveryNumber varchar(30) UNIQUE,RequestID uniqueidentifier UNIQUE,
                    VendorID varchar(20),PoNumber varchar(30),DeliveryDate date,Status varchar(20) DEFAULT 'Registered',
                    CreatedTS datetime2 DEFAULT SYSDATETIME(),CreatedBy varchar(20),CreatedUserID nvarchar(450),
                    ModifiedBy varchar(20),ModifiedUserID nvarchar(450),ModifiedTS datetime2,Version rowversion,
                    ShipDate date,ShippedAt datetime2,ShippedBy varchar(20),ShippedUserID nvarchar(450),NoteSnapshot nvarchar(max));
                CREATE TABLE SCM_DeliveryLine(DeliveryLineID int IDENTITY PRIMARY KEY,DeliveryID int,PoID int,
                    Quantity decimal(18,3),ReceivedQty decimal(18,3) DEFAULT 0,PackingQty decimal(18,3),VendorLotNo nvarchar(30),ProductionDate date);
                CREATE TABLE SCM_DeliveryNote(NoteID int IDENTITY PRIMARY KEY,NoteNumber varchar(30),VendorID varchar(20),
                    Snapshot nvarchar(max),IssuedAt datetime2,IssuedBy varchar(20),IssuedUserID nvarchar(450));
                CREATE TABLE SCM_DeliveryNoteDelivery(NoteID int,DeliveryID int UNIQUE);
                CREATE TABLE SCM_DeliveryBox(BoxID bigint IDENTITY PRIMARY KEY,IssuedBoxNumber varchar(64),
                    BoxNumber AS (CONVERT(varchar(64),COALESCE(IssuedBoxNumber,'BOX-'+CONVERT(varchar(20),BoxID)))) PERSISTED,
                    DeliveryLineID int NOT NULL,BoxSeq int,ItemNo varchar(20),ItemName nvarchar(200),UnitCode varchar(20),
                    Quantity decimal(18,3),ActiveFlag bit DEFAULT 1,CreatedTS datetime2 DEFAULT SYSDATETIME(),VoidedTS datetime2);
                CREATE UNIQUE INDEX UX_SCM_DeliveryBox_Number ON SCM_DeliveryBox(BoxNumber);
                CREATE UNIQUE INDEX UX_SCM_DeliveryBox_ActiveSequence ON SCM_DeliveryBox(DeliveryLineID,BoxSeq) WHERE ActiveFlag=1;
                CREATE TABLE SCM_BoxNumberSequence(VendorID varchar(20),NumberDate date,LastNumber int,PRIMARY KEY(VendorID,NumberDate));
                CREATE TABLE MD_Location(LocationID varchar(50),ActiveFlag bit,LocationType varchar(20),Capacity decimal(18,3),AreaCode varchar(20));
                CREATE TABLE MD_CodeItem(GroupCode varchar(30),CodeValue varchar(30),UseFlag bit);
                INSERT MD_CodeItem VALUES('INV_ADJUST_REASON','COUNT_DIFF',1);
                CREATE TABLE WH_Inventory(LotNo nvarchar(50) PRIMARY KEY,UnitType varchar(10),ParentLotNo nvarchar(50),CaseNo varchar(50),PartNo varchar(50),PartName nvarchar(200),
                    Qty decimal(18,3),LocationNo varchar(50),InvoiceNo nvarchar(100),DeliveryNoteNo nvarchar(60),ReceivedAt datetime2,CreatedAt datetime2,UpdatedAt datetime2);
                CREATE TABLE WH_InventoryTransaction(TransactionID bigint IDENTITY PRIMARY KEY,TransactionTime datetime2 DEFAULT SYSDATETIME(),
                    TransactionType varchar(10),PartNo varchar(50),LocationNo varchar(50),LotNo nvarchar(50),QtyBefore decimal(18,3),QtyChange decimal(18,3),QtyAfter decimal(18,3),
                    ReasonCode varchar(30),SourceType varchar(30),SourceID int,OperatorID nvarchar(450),ApproverID nvarchar(450),Note nvarchar(500),CreatedBy varchar(50),CreatedTS datetime2);
                INSERT MD_Vendor VALUES('V1',N'Vendor One',1),('V2',N'Vendor Two',1);
                INSERT MD_Item VALUES('PART-1','Part One','MATERIAL','EA',1),('PART-2','Part Two','MATERIAL','EA',1),('PART-3','Part Three','MATERIAL','EA',1);
                INSERT SCM_ItemVendor(ItemNo,VendorID,PackingQty,ActiveFlag) VALUES('PART-1','V1',4,1),('PART-2','V1',5,1),('PART-3','V2',5,1);
                INSERT SCM_PortalVendorUser VALUES('case-user','V1',1,0),('other-user','V2',1,0);
                INSERT WH_PurchaseOrder(PoID,PoNumber,PoLineNo,VendorID,ItemNo,UnitCode,ReceivedQty,OrderQty,Status,OrderDate,DueDate,SupplierConfirmedAt,DeliveryDestination)
                VALUES(1,'PO-CASE-1',1,'V1','PART-1','EA',0,20,'Open',DATEADD(day,-1,SYSDATETIME()),SYSDATETIME(),SYSDATETIME(),'EOS'),
                      (2,'PO-CASE-2',1,'V1','PART-2','EA',0,10,'Open',DATEADD(day,-1,SYSDATETIME()),SYSDATETIME(),SYSDATETIME(),'EOS'),
                      (3,'PO-CASE-3',1,'V2','PART-3','EA',0,10,'Open',DATEADD(day,-1,SYSDATETIME()),SYSDATETIME(),SYSDATETIME(),'EOS');
                """);
            var migration=File.ReadAllText(Path.Combine(root!.FullName,"dist","migrate_scm_delivery_cases.sql"));
            foreach(var pass in Enumerable.Range(0,2))
                foreach(var batch in Regex.Split(migration,@"(?im)^\s*GO\s*$"))
                    if(!string.IsNullOrWhiteSpace(batch))Sql(batch);
            var schema=File.ReadAllText(Path.Combine(root.FullName,"dist","pda","PDA_SCHEMA.sql"));
            var procedure=Regex.Match(schema,@"(?ms)^CREATE OR ALTER PROCEDURE dbo\.WH_PDA_INBOUND_RECEIVE_LOT\b.*?^\s*GO\s*$").Value;
            var portalEnd=procedure.IndexOf("        RETURN;",StringComparison.Ordinal);
            Assert.True(portalEnd>0);
            // Real Portal receipt branch, unchanged. The unrelated legacy LOT branch is not in this fixture.
            procedure=procedure[..(portalEnd+"        RETURN;".Length)]+"\n    END;\n    THROW 51402,'Not a portal box.',1;\nEND;";
            Sql("CREATE PROCEDURE dbo.WH_PDA_INBOUND_SCAN_LOT @ReceiveMode nvarchar(10),@LotBarcode nvarchar(50) AS SELECT @LotBarcode AS LOTNO;");
            Sql(procedure);

            Assert.Equal(2,repo.ListPurchaseOrders(true,"case-user",confirmedOnly:true).Count);
            Sql("""
                INSERT MD_Item VALUES('PART-4','Part Four','MATERIAL','EA',1);
                INSERT WH_PurchaseOrder(PoID,PoNumber,PoLineNo,VendorID,ItemNo,UnitCode,ReceivedQty,OrderQty,Status)
                VALUES(99,'PO-CASE-1',2,'V1','PART-4','EA',0,10,'Open');
                """);
            Assert.DoesNotContain(repo.ListPurchaseOrders(true,"case-user",confirmedOnly:true),p=>p.Number=="PO-CASE-1");
            Assert.Empty(repo.ListCaseParts("PO-CASE-1","case-user"));
            Assert.Contains(repo.ListPortalPackingQuantities("case-user",forCreate:true),p=>p.ItemNo=="PART-4");
            Assert.False(repo.ListPortalPackingQuantities("case-user").Single(p=>p.ItemNo=="PART-4").Linked);
            Assert.DoesNotContain(repo.ListPortalPackingQuantities("other-user",forCreate:true),p=>p.ItemNo=="PART-4");
            Assert.False(repo.CreatePortalPackingQuantity("other-user","PART-4","V1",3));
            Assert.Throws<ArgumentOutOfRangeException>(()=>repo.CreatePortalPackingQuantity("case-user","PART-4","V1",0));
            Assert.True(repo.CreatePortalPackingQuantity("case-user","PART-4","V1",3));
            Assert.False(repo.CreatePortalPackingQuantity("case-user","PART-4","V1",4));
            Assert.DoesNotContain(repo.ListPortalPackingQuantities("case-user",forCreate:true),p=>p.ItemNo=="PART-4");
            Assert.Equal(3m,repo.ListPortalPackingQuantities("case-user").Single(p=>p.ItemNo=="PART-4").PackingQty);
            Sql("UPDATE SCM_ItemVendor SET PackingQty=NULL WHERE ItemNo='PART-4';");
            Assert.True(repo.CreatePortalPackingQuantity("case-user","PART-4","V1",5));
            Sql("UPDATE SCM_ItemVendor SET ActiveFlag=0,PackingQty=NULL WHERE ItemNo='PART-4';");
            Assert.False(repo.CreatePortalPackingQuantity("case-user","PART-4","V1",5));
            Assert.False(repo.ListPortalPackingQuantities("case-user").Single(p=>p.ItemNo=="PART-4").CanSave);
            Sql("DELETE WH_PurchaseOrder WHERE PoID=99; DELETE SCM_ItemVendor WHERE ItemNo='PART-4'; DELETE MD_Item WHERE ItemNo='PART-4';");
            var casePolicyMigration=File.ReadAllText(Path.Combine(root!.FullName,"dist","migrate_md_item_case_receive.sql"));
            Sql(casePolicyMigration);
            Sql(casePolicyMigration); // Reapplying must preserve existing items and settings.
            Assert.Equal(0,Count("SELECT COUNT(*) FROM MD_Item WHERE ScanRequired=1"));
            Sql("""
                INSERT WH_PurchaseOrder(PoID,PoNumber,PoLineNo,VendorID,ItemNo,UnitCode,ReceivedQty,OrderQty,Status,SupplierConfirmedAt)
                VALUES(99,'PO-MISSING',1,'V1','MISSING-PART','EA',0,10,'Open',SYSDATETIME());
                """);
            Assert.DoesNotContain(repo.ListPortalPackingQuantities("case-user",forCreate:true),p=>p.ItemNo=="MISSING-PART");
            Assert.DoesNotContain(repo.ListPortalPackingQuantities("case-user"),p=>p.ItemNo=="MISSING-PART");
            Assert.False(repo.CreatePortalPackingQuantity("case-user","MISSING-PART","V1",5));
            Assert.Empty(repo.ListCaseParts("PO-MISSING","case-user"));
            Assert.False(repo.ListPurchaseOrders(true,"case-user").Single(p=>p.Number=="PO-MISSING").Lines.Single().ItemExists);
            Assert.Throws<InvalidOperationException>(()=>repo.CreatePreparedCase("PO-MISSING",[new(99,5)],"case-user"));
            var missingError=Assert.Throws<SqlException>(()=>repo.ConfirmSupplierOrder("PO-MISSING",Versions("PO-MISSING"),"case-user","case-user"));
            Assert.Contains("Item Master",missingError.Message);
            Assert.False(repo.ConfirmSupplierOrder("PO-CASE-1",Versions("PO-CASE-1"),"case-user","case-user"));
            Sql("DELETE WH_PurchaseOrder WHERE PoID=99;");

            Assert.Throws<InvalidOperationException>(()=>repo.CreatePreparedCase("PO-CASE-1",[new(1,4)],"other-user"));
            Assert.Throws<InvalidOperationException>(()=>repo.CreatePreparedCase("PO-CASE-1",[new(2,4)],"case-user"));
            Assert.Throws<InvalidOperationException>(()=>repo.CreatePreparedCase("PO-CASE-1",[new(1,5)],"case-user"));
            var first=repo.CreatePreparedCase("PO-CASE-1",[new(1,4),new(1,4)],"case-user");
            Assert.Equal("CASE-000001",first);
            Assert.Equal(0,Count("SELECT COUNT(*) FROM sys.columns WHERE object_id=OBJECT_ID('dbo.SCM_DeliveryCase') AND name='CaseID'"));
            var second=repo.CreatePreparedCase("PO-CASE-1",[new(1,4)],"case-user");
            var rollbackCase=repo.CreatePreparedCase("PO-CASE-2",[new(2,5),new(2,5)],"case-user");
            var originalBoxes=repo.ListDeliveryCases("PO-CASE-1","case-user").SelectMany(c=>c.Boxes).Select(b=>b.Number).Order().ToArray();
            Assert.Equal(0,Count("SELECT COUNT(*) FROM SCM_Delivery"));
            Assert.Equal(0,Count("SELECT COUNT(*) FROM SCM_DeliveryNote"));
            Assert.Equal(8,repo.ListCaseParts("PO-CASE-1","case-user").Single().Available);
            Assert.Equal(12,repo.ListCaseParts("PO-CASE-1","case-user").Single().Prepared);
            Assert.Null(repo.GetDeliveryCase(first,"other-user"));
            Assert.Equal("Prepared",repo.GetDeliveryCase(first,"case-user")!.Status);
            Assert.Throws<SqlException>(()=>repo.ReceiveDeliveryCase(first,"LOCAL","case-user"));
            Assert.Throws<InvalidOperationException>(()=>repo.CreatePreparedCase("PO-CASE-1",[new(1,4),new(1,4),new(1,4)],"case-user"));
            Assert.Throws<ArgumentException>(()=>repo.RegisterSupplierDelivery("PO-CASE-1",DateTime.Today,[new(1,4)],Versions("PO-CASE-1"),Guid.NewGuid(),"case-user","case-user",[]));
            Assert.Throws<InvalidOperationException>(()=>repo.RegisterSupplierDelivery("PO-CASE-1",DateTime.Today,[new(1,11)],Versions("PO-CASE-1"),Guid.NewGuid(),"case-user","case-user",[first,second]));
            Assert.Throws<SqlException>(()=>repo.RegisterSupplierDelivery("PO-CASE-1",DateTime.Today,[new(1,12)],Versions("PO-CASE-1"),Guid.NewGuid(),"case-user","case-user",[first,rollbackCase]));
            var request=Guid.NewGuid();
            var firstDelivery=repo.RegisterSupplierDelivery("PO-CASE-1",DateTime.Today,[new(1,12)],Versions("PO-CASE-1"),request,"case-user","case-user",[first,second]);
            var balance=repo.ListCaseParts("PO-CASE-1","case-user").Single();
            Assert.Equal(20,balance.Ordered);Assert.Equal(12,balance.PendingDelivery);Assert.Equal(0,balance.Prepared);Assert.Equal(8,balance.Available);
            Assert.Equal(firstDelivery,repo.RegisterSupplierDelivery("PO-CASE-1",DateTime.Today,[new(1,12)],Versions("PO-CASE-1"),request,"case-user","case-user",[first,second]));
            Assert.Equal(originalBoxes,repo.ListDeliveryCases("PO-CASE-1","case-user").SelectMany(c=>c.Boxes).Select(b=>b.Number).Order().ToArray());
            Assert.Equal(3,Count("SELECT COUNT(*) FROM SCM_DeliveryBox WHERE PoID=1 AND DeliveryLineID IS NOT NULL"));
            Assert.Throws<SqlException>(()=>repo.RegisterSupplierDelivery("PO-CASE-1",DateTime.Today,[new(1,8)],Versions("PO-CASE-1"),Guid.NewGuid(),"case-user","case-user",[first]));
            Assert.Throws<SqlException>(()=>repo.CancelPreparedCase(first,"case-user"));
            Assert.Throws<InvalidOperationException>(()=>Update(firstDelivery,"PO-CASE-1",1,11));
            Update(firstDelivery,"PO-CASE-1",1,12,cancel:true);
            Assert.Empty(repo.ListDeliveryCases("PO-CASE-1","case-user"));
            Assert.Equal(0,Count("SELECT COUNT(*) FROM SCM_DeliveryBox WHERE PoID=1 AND DeliveryLineID IS NULL AND ActiveFlag=1"));
            Assert.Equal(20,repo.ListCaseParts("PO-CASE-1","case-user").Single().Available);
            var extra=repo.CreatePreparedCase("PO-CASE-1",[new(1,4),new(1,4),new(1,4),new(1,4),new(1,4)],"case-user");
            Assert.Equal(0,repo.ListCaseParts("PO-CASE-1","case-user").Single().Available);
            Assert.Throws<InvalidOperationException>(()=>repo.RegisterSupplierDelivery("PO-CASE-1",DateTime.Today,[new(1,4)],Versions("PO-CASE-1"),Guid.NewGuid(),"case-user","case-user"));
            repo.CancelPreparedCase(extra,"case-user");
            Assert.Null(repo.GetDeliveryCase(extra,"case-user"));
            var parallelCases=await Task.WhenAll(Enumerable.Range(0,2).Select(_=>Task.Run(()=>
            {
                try{return repo.CreatePreparedCase("PO-CASE-1",[new(1,4),new(1,4),new(1,4),new(1,4),new(1,4)],"case-user");}
                catch(InvalidOperationException){return "";}
            })));
            var winner=Assert.Single(parallelCases,n=>n.Length>0);
            Assert.Equal(0,repo.ListCaseParts("PO-CASE-1","case-user").Single().Available);
            repo.CancelPreparedCase(winner,"case-user");
            first=repo.CreatePreparedCase("PO-CASE-1",[new(1,4),new(1,4)],"case-user");
            second=repo.CreatePreparedCase("PO-CASE-1",[new(1,4)],"case-user");
            originalBoxes=repo.ListDeliveryCases("PO-CASE-1","case-user").SelectMany(c=>c.Boxes).Select(b=>b.Number).Order().ToArray();
            var delivery=repo.RegisterSupplierDelivery("PO-CASE-1",DateTime.Today,[new(1,12)],Versions("PO-CASE-1"),Guid.NewGuid(),"case-user","case-user",[first,second]);
            // Changing the packing master must not regenerate already printed box labels on Save.
            Sql("UPDATE SCM_ItemVendor SET PackingQty=2 WHERE ItemNo='PART-1';");
            Update(delivery,"PO-CASE-1",1,12);
            Assert.Equal(originalBoxes,repo.ListDeliveryCases("PO-CASE-1","case-user").SelectMany(c=>c.Boxes).Select(b=>b.Number).Order().ToArray());
            Assert.False(repo.GetDeliveryCase(first,"case-user")!.ReadyToReceive);
            Assert.Null(Assert.Single(repo.ListNoteDeliveries("case-user"),x=>x.Number==delivery).NoteNumber);
            Assert.Empty(repo.ListNoteDeliveries("other-user"));
            var note=repo.IssueDeliveryNote([delivery],"case-user","case-user");
            Assert.Equal(DateTime.Today,note.ShipDate);
            Assert.Equal(1,Count($"SELECT COUNT(*) FROM SCM_Delivery WHERE DeliveryNumber='{delivery}' AND Status='Registered' AND ShipDate IS NULL AND ShippedAt IS NULL"));
            Assert.False(repo.GetDeliveryCase(first,"case-user")!.ReadyToReceive);
            Assert.Throws<SqlException>(()=>repo.ReceiveDeliveryCase(first,"LOCAL","case-user"));
            Assert.Throws<InvalidOperationException>(()=>repo.IssueDeliveryNote([delivery],"other-user","other-user"));
            Assert.Throws<InvalidOperationException>(()=>Update(delivery,"PO-CASE-1",1,12));
            Assert.Equal(note.Number,repo.GetDeliveryNote(note.Number,"case-user")!.Number);
            Update(delivery,"PO-CASE-1",1,12,ship:true);
            Assert.Equal(note.Number,Assert.Single(repo.ListNoteDeliveries("case-user"),x=>x.Number==delivery).NoteNumber);
            Assert.Equal(note.Number,repo.IssueDeliveryNote([delivery],"case-user","case-user").Number);
            Assert.Equal(1,Count("SELECT COUNT(*) FROM SCM_DeliveryNote"));
            Assert.Equal(note.Number,repo.GetDeliveryCase(first,"case-user")!.NoteNumber);
            Assert.True(repo.GetDeliveryCase(first,"case-user")!.ReadyToReceive);
            Assert.Throws<InvalidOperationException>(()=>Update(delivery,"PO-CASE-1",1,12,cancel:true));

            var box=repo.GetDeliveryCase(first,"case-user")!.Boxes[0].Number;
            using(var c=factory.OpenConnection())
            using(var tx=c.BeginTransaction())
            {
                using var cmd=new SqlCommand("EXEC dbo.WH_PDA_INBOUND_RECEIVE_LOT 'LOCAL',@B,NULL,'case-user'",c,tx);
                cmd.Parameters.AddWithValue("@B",box);cmd.ExecuteNonQuery();ScmRepository.LinkReceivedCase(c,tx,box);tx.Commit();
            }
            Sql($"UPDATE WH_Inventory SET Qty=0 WHERE LotNo='{box}';");
            Assert.Equal(1,repo.ReceiveDeliveryCase(first,"CKD","case-user"));
            Assert.Throws<InvalidOperationException>(()=>repo.ReceiveDeliveryCase(first,"LOCAL","case-user"));
            Assert.Equal(1,repo.ReceiveDeliveryCase(second,"LOCAL","case-user"));
            Assert.Equal(12,Count("SELECT ReceivedQty FROM WH_PurchaseOrder WHERE PoID=1"));
            Assert.Equal(note.Number,Assert.Single(repo.ListNoteDeliveries("case-user"),x=>x.Number==delivery).NoteNumber);
            Assert.Equal(3,Count("SELECT COUNT(*) FROM WH_InventoryTransaction"));
            Assert.Equal(0,Count($"SELECT Qty FROM WH_Inventory WHERE LotNo='{first}'"));
            Assert.Equal(2,Count($"SELECT COUNT(*) FROM WH_Inventory WHERE ParentLotNo='{first}' AND CaseNo='{first}'"));
            var otherDelivery=repo.RegisterSupplierDelivery("PO-CASE-2",DateTime.Today,[new(2,10)],Versions("PO-CASE-2"),Guid.NewGuid(),"case-user","case-user",[rollbackCase]);
            Update(otherDelivery,"PO-CASE-2",2,10,ship:true);
            var fullyAllocated=repo.ListCaseParts("PO-CASE-2","case-user").Single();
            Assert.Equal(0,fullyAllocated.Available);Assert.Equal(0,fullyAllocated.Received);Assert.Equal(10,fullyAllocated.PendingDelivery);
            repo.IssueDeliveryNote([otherDelivery],"case-user","case-user");
            Sql("UPDATE SCM_DeliveryLine SET Quantity=6 WHERE PoID=2;");
            Assert.Throws<SqlException>(()=>repo.ReceiveDeliveryCase(rollbackCase,"LOCAL","case-user"));
            Assert.Equal(0,Count("SELECT ReceivedQty FROM WH_PurchaseOrder WHERE PoID=2"));
            Assert.Equal(0,Count("SELECT COUNT(*) FROM WH_Inventory WHERE PartNo='PART-2'"));
            Assert.Equal(3,Count("SELECT COUNT(*) FROM WH_InventoryTransaction"));
            Sql("UPDATE SCM_DeliveryLine SET Quantity=10 WHERE PoID=2;");
            var results=await Task.WhenAll(Enumerable.Range(0,2).Select(_=>Task.Run(()=>
            {
                try{return repo.ReceiveDeliveryCase(rollbackCase,"LOCAL","case-user");}
                catch(InvalidOperationException){return 0;}
            })));
            Assert.Equal(2,results.Sum());
            Assert.Equal(10,Count("SELECT ReceivedQty FROM WH_PurchaseOrder WHERE PoID=2"));
            Assert.Equal(5,Count("SELECT COUNT(*) FROM WH_InventoryTransaction"));
            // Preserve the existing non-CASE registration path and its box labels.
            var legacy=repo.RegisterSupplierDelivery("PO-CASE-3",DateTime.Today,[new(3,4)],
                Versions("PO-CASE-3"),Guid.NewGuid(),"other-user","other-user");
            var legacyLabels=repo.ListDeliveryBoxes(legacy,"other-user").Select(b=>b.Number).ToArray();
            Assert.Single(legacyLabels);
            repo.UpdateSupplierDelivery(legacy,DateTime.Today,[new(3,4)],DeliveryVersion(legacy),Versions("PO-CASE-3"),"other-user","other-user");
            Assert.Equal(legacyLabels,repo.ListDeliveryBoxes(legacy,"other-user").Select(b=>b.Number).ToArray());
            // Draft cases are persisted only with a successful delivery, all in one transaction.
            var casesBefore=Count("SELECT COUNT(*) FROM SCM_DeliveryCase");
            var boxesBefore=Count("SELECT COUNT(*) FROM SCM_DeliveryBox");
            IReadOnlyList<IReadOnlyList<ScmRepository.CaseBoxInput>> draftCases=[[new(3,2)],[new(3,3)]];
            Assert.Throws<InvalidOperationException>(()=>repo.RegisterSupplierDelivery("PO-CASE-3",DateTime.Today,[new(3,4)],
                Versions("PO-CASE-3"),Guid.NewGuid(),"other-user","other-user",draftCases:draftCases));
            Assert.Equal(casesBefore,Count("SELECT COUNT(*) FROM SCM_DeliveryCase"));
            Assert.Equal(boxesBefore,Count("SELECT COUNT(*) FROM SCM_DeliveryBox"));
            Assert.Throws<InvalidOperationException>(()=>repo.RegisterSupplierDelivery("PO-CASE-3",DateTime.Today,[new(3,7)],
                Versions("PO-CASE-3"),Guid.NewGuid(),"other-user","other-user",draftCases:[[new(3,2)],[new(3,5)]]));
            Assert.Equal(casesBefore,Count("SELECT COUNT(*) FROM SCM_DeliveryCase"));
            Assert.Equal(boxesBefore,Count("SELECT COUNT(*) FROM SCM_DeliveryBox"));
            var draftRequest=Guid.NewGuid();
            var draftDelivery=repo.RegisterSupplierDelivery("PO-CASE-3",DateTime.Today,[new(3,5)],
                Versions("PO-CASE-3"),draftRequest,"other-user","other-user",draftCases:draftCases);
            Assert.Equal(2,repo.ListDeliveryCases("PO-CASE-3","other-user").Count(c=>c.Delivery==draftDelivery));
            Assert.Equal(casesBefore+2,Count("SELECT COUNT(*) FROM SCM_DeliveryCase"));
            Assert.Equal(boxesBefore+2,Count("SELECT COUNT(*) FROM SCM_DeliveryBox"));
            Assert.Equal(draftDelivery,repo.RegisterSupplierDelivery("PO-CASE-3",DateTime.Today,[new(3,5)],
                Versions("PO-CASE-3"),draftRequest,"other-user","other-user",draftCases:draftCases));
            Assert.Equal(casesBefore+2,Count("SELECT COUNT(*) FROM SCM_DeliveryCase"));
            Assert.Equal(0,Count($"SELECT COUNT(*) FROM SCM_DeliveryBox b JOIN SCM_DeliveryCase k ON k.CaseNo=b.CaseNo JOIN SCM_Delivery d ON d.DeliveryID=k.DeliveryID WHERE d.DeliveryNumber='{draftDelivery}' AND b.DeliveryLineID IS NULL"));
            var cancelledNote=repo.IssueDeliveryNote([draftDelivery],"other-user","other-user");
            Assert.Equal(cancelledNote.Number,repo.IssueDeliveryNote([draftDelivery],"other-user","other-user").Number);
            repo.UpdateSupplierDelivery(draftDelivery,DateTime.Today,[new(3,5)],DeliveryVersion(draftDelivery),Versions("PO-CASE-3"),"other-user","other-user",cancel:true);
            Assert.DoesNotContain(repo.ListNoteDeliveries("other-user"),n=>n.Number==draftDelivery);
            Assert.Throws<InvalidOperationException>(()=>repo.IssueDeliveryNote([draftDelivery],"other-user","other-user"));
            Assert.Throws<SqlException>(()=>repo.GetDeliveryNote(cancelledNote.Number,"other-user"));
            // Mixed-PO cases remain prepared across reloads and are assigned atomically to one note.
            Sql("""
                INSERT WH_PurchaseOrder(PoID,PoNumber,PoLineNo,VendorID,ItemNo,UnitCode,ReceivedQty,OrderQty,Status,OrderDate,DueDate,SupplierConfirmedAt,DeliveryDestination)
                VALUES(4,'PO-MIX-1',1,'V1','PART-1','EA',0,20,'Open',DATEADD(day,-1,SYSDATETIME()),SYSDATETIME(),SYSDATETIME(),'EOS'),
                      (5,'PO-MIX-2',1,'V1','PART-2','EA',0,20,'Open',DATEADD(day,-1,SYSDATETIME()),SYSDATETIME(),SYSDATETIME(),'EOS'),
                      (6,'PO-MIX-3',1,'V1','PART-2','EA',0,20,'Open',DATEADD(day,-1,SYSDATETIME()),SYSDATETIME(),SYSDATETIME(),'OTHER');
                """);
            Assert.Throws<InvalidOperationException>(()=>repo.CreatePreparedCase(["PO-MIX-1","PO-MIX-3"],[new(4,2),new(6,5)],"case-user"));
            Assert.Throws<InvalidOperationException>(()=>repo.CreatePreparedCase(["PO-MIX-1","PO-CASE-3"],[new(4,2),new(3,1)],"case-user"));
            var mixed=repo.CreatePreparedCase(["PO-MIX-1","PO-MIX-2"],[new(4,2),new(5,5)],"case-user");
            var mixedBoxes=repo.GetDeliveryCase(mixed,"case-user")!.Boxes.Select(b=>b.Number).ToArray();
            Assert.Equal(2,repo.GetDeliveryCase(mixed,"case-user")!.Boxes.Select(b=>b.Order).Distinct().Count());
            Assert.Contains(repo.ListDeliveryCases("PO-MIX-1","case-user"),k=>k.Number==mixed);
            Assert.Contains(repo.ListDeliveryCases("PO-MIX-2","case-user"),k=>k.Number==mixed);
            Assert.Equal(18,repo.ListCaseParts("PO-MIX-1","case-user").Single().Available);
            Assert.Equal(15,repo.ListCaseParts("PO-MIX-2","case-user").Single().Available);
            var removed=repo.CreatePreparedCase(["PO-MIX-1","PO-MIX-2"],[new(4,2),new(5,5)],"case-user");
            repo.CancelPreparedCase(removed,"case-user");
            Assert.Null(repo.GetDeliveryCase(removed,"case-user"));
            var differentDestination=repo.CreatePreparedCase("PO-MIX-3",[new(6,5)],"case-user");
            Assert.Throws<InvalidOperationException>(()=>repo.CreateDeliveryNoteFromCases([mixed,differentDestination],DateTime.Today,"case-user","case-user"));
            Assert.Throws<InvalidOperationException>(()=>repo.CreateDeliveryNoteFromCases([mixed],DateTime.Today,"other-user","other-user"));
            // A later PO failure must roll back all earlier delivery registrations.
            Sql("UPDATE WH_PurchaseOrder SET SupplierConfirmedAt=NULL WHERE PoID=5;");
            Assert.Empty(repo.ListCaseParts("PO-MIX-2","case-user"));
            Assert.Throws<InvalidOperationException>(()=>repo.CreatePreparedCase("PO-MIX-2",[new(5,5)],"case-user"));
            var beforeMixed=Count("SELECT COUNT(*) FROM SCM_Delivery");
            Assert.Throws<InvalidOperationException>(()=>repo.CreateDeliveryNoteFromCases([mixed],DateTime.Today,"case-user","case-user"));
            Assert.Equal(beforeMixed,Count("SELECT COUNT(*) FROM SCM_Delivery"));
            Assert.Equal("Prepared",repo.GetDeliveryCase(mixed,"case-user")!.Status);
            Sql("UPDATE WH_PurchaseOrder SET SupplierConfirmedAt=SYSDATETIME() WHERE PoID=5;");
            var additional=repo.CreatePreparedCase("PO-MIX-1",[new(4,2)],"case-user");
            var mixedNote=repo.CreateDeliveryNoteFromCases([mixed,additional],DateTime.Today,"case-user","case-user");
            Assert.Equal(2,mixedNote.Lines.Select(l=>l.OrderNumber).Distinct().Count());
            Assert.Equal(9,mixedNote.Lines.Sum(l=>l.Quantity));
            Assert.Equal(mixedNote.Number,repo.CreateDeliveryNoteFromCases([mixed,additional],DateTime.Today,"case-user","case-user").Number);
            Assert.Throws<InvalidOperationException>(()=>repo.CreateDeliveryNoteFromCases([mixed],DateTime.Today,"case-user","case-user"));
            Assert.Equal(mixedBoxes,repo.GetDeliveryCase(mixed,"case-user")!.Boxes.Select(b=>b.Number).ToArray());
            Assert.Equal("Registered",repo.GetDeliveryCase(mixed,"case-user")!.Status);
            Assert.Throws<SqlException>(()=>repo.CancelPreparedCase(mixed,"case-user"));
            foreach(var line in mixedNote.Lines)
            {
                Assert.Contains(repo.ListDeliveryCases(null,"case-user",delivery:line.DeliveryNumber),k=>k.Number==mixed);
                Assert.Throws<InvalidOperationException>(()=>Update(line.DeliveryNumber,line.OrderNumber,line.PoID,line.Quantity,cancel:true));
                Update(line.DeliveryNumber,line.OrderNumber,line.PoID,line.Quantity,ship:true);
            }
            Assert.True(repo.GetDeliveryCase(mixed,"case-user")!.ReadyToReceive);
            Sql("UPDATE MD_Item SET ScanRequired=1 WHERE ItemNo='PART-1';");
            var requiredBox=repo.GetDeliveryCase(mixed,"case-user")!.Boxes.Single(b=>b.Item=="PART-1").Number;
            var beforePolicyCheck=Count("SELECT COUNT(*) FROM WH_Inventory");
            Assert.Throws<InvalidOperationException>(()=>repo.ReceiveDeliveryCase(mixed,"LOCAL","case-user"));
            Assert.Throws<InvalidOperationException>(()=>repo.ReceiveDeliveryCase(mixed,"LOCAL","case-user",["WRONG-BOX"]));
            Assert.Equal(beforePolicyCheck,Count("SELECT COUNT(*) FROM WH_Inventory"));
            Assert.Equal(2,repo.ReceiveDeliveryCase(mixed,"LOCAL","case-user",[requiredBox]));
            Assert.Equal(2,Count("SELECT ReceivedQty FROM WH_PurchaseOrder WHERE PoID=4"));
            Assert.Equal(5,Count("SELECT ReceivedQty FROM WH_PurchaseOrder WHERE PoID=5"));
            Assert.Equal(2,Count($"SELECT COUNT(*) FROM WH_Inventory WHERE ParentLotNo='{mixed}' AND DeliveryNoteNo='{mixedNote.Number}'"));
            Sql("DELETE MD_Item WHERE ItemNo='PART-3';");
            Assert.Empty(repo.ListCaseParts("PO-CASE-3","other-user"));
            // Exercise the real PDA adjustment procedure and read its history through the supplier portal.
            Sql("CREATE PROCEDURE dbo.WH_PDA_ADJUST_SCAN_STOCK @ScanText nvarchar(100) AS SELECT @ScanText AS LOTNO;");
            var adjustProcedure=Regex.Match(schema,@"(?ms)^CREATE OR ALTER PROCEDURE dbo\.WH_PDA_ADJUST_SAVE_QTY\b.*?(?=^\s*GO\s*$)").Value;
            Assert.NotEmpty(adjustProcedure);Sql(adjustProcedure);
            var adjustedBox=repo.GetDeliveryCase(mixed,"case-user")!.Boxes.First(b=>b.Item=="PART-1").Number;
            var receiptsBeforeAdjust=repo.ListPortalReceiptResults("case-user").Select(r=>(r.Id,r.Received,r.Remaining)).ToArray();
            Sql($"EXEC dbo.WH_PDA_ADJUST_SAVE_QTY @ScanText='{adjustedBox}',@DeltaQty=-1,@ReasonCode='COUNT_DIFF',@ReasonNote=N'One unit short',@UserId='tester';");
            var discrepancy=Assert.Single(repo.ListPortalDeliveryDiscrepancies("case-user"));
            Assert.Equal(adjustedBox,discrepancy.Lot);Assert.Equal(mixedNote.Number,discrepancy.Note);
            Assert.Equal("PART-1",discrepancy.Item);Assert.Equal(2m,discrepancy.Before);Assert.Equal(-1m,discrepancy.Change);Assert.Equal(1m,discrepancy.After);
            Assert.Equal("COUNT_DIFF",discrepancy.Reason);Assert.Equal("One unit short",discrepancy.Comment);
            Assert.Empty(repo.ListPortalDeliveryDiscrepancies("other-user"));
            Assert.Empty(repo.ListPortalDeliveryDiscrepancies("case-user",delivery)); // same part, different delivery
            Assert.Single(repo.ListPortalDeliveryDiscrepancies("case-user",discrepancy.Delivery));
            Assert.Throws<SqlException>(()=>Sql($"EXEC dbo.WH_PDA_ADJUST_SAVE_QTY @ScanText='{adjustedBox}',@DeltaQty=1,@ReasonCode='COUNT_DIFF',@UserId='tester',@SimulateFailure=1;"));
            Assert.Single(repo.ListPortalDeliveryDiscrepancies("case-user"));
            Sql($"EXEC dbo.WH_PDA_ADJUST_SAVE_QTY @ScanText='{adjustedBox}',@DeltaQty=1,@ReasonCode='COUNT_DIFF',@UserId='tester';");
            Assert.Equal(2,repo.ListPortalDeliveryDiscrepancies("case-user").Count);
            Assert.Equal(receiptsBeforeAdjust,repo.ListPortalReceiptResults("case-user").Select(r=>(r.Id,r.Received,r.Remaining)).ToArray());
            Assert.Equal(2,Count("SELECT ReceivedQty FROM WH_PurchaseOrder WHERE PoID=4"));
            Assert.Equal(2,Count("SELECT SUM(ReceivedQty) FROM SCM_DeliveryLine WHERE PoID=4"));
            Sql($"DELETE WH_Inventory WHERE LotNo='{adjustedBox}';");
            Assert.Equal(2,repo.ListPortalDeliveryDiscrepancies("case-user").Count); // historical link survives stock removal
            Sql("UPDATE SCM_PortalVendorUser SET LockedFlag=1 WHERE UserID='case-user';");
            Assert.Empty(repo.ListPortalDeliveryDiscrepancies("case-user"));
        }
        finally
        {
            SqlConnection.ClearAllPools();
            using var cmd=new SqlCommand($"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}];",master);
            cmd.ExecuteNonQuery();
        }
    }
}
