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
    public async Task Prepared_cases_register_ship_and_receive_without_replacing_box_labels()
    {
        // Deliberately isolated: never reads connection settings or touches AMES_DEV.
        var database="AMES_CASE_TEST_"+Guid.NewGuid().ToString("N");
        var server=new SqlConnectionStringBuilder{DataSource=@".\SQLEXPRESS",InitialCatalog="master",
            IntegratedSecurity=true,TrustServerCertificate=true,ConnectTimeout=5};
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
                CREATE TABLE SCM_ItemVendor(ItemNo varchar(20),VendorID varchar(20),PackingQty decimal(18,3),ActiveFlag bit,PRIMARY KEY(ItemNo,VendorID));
                CREATE TABLE SCM_PortalVendorUser(UserID nvarchar(450),VendorID varchar(20),ActiveFlag bit,LockedFlag bit);
                CREATE TABLE WH_PurchaseOrder(PoID int PRIMARY KEY,PoNumber varchar(30),PoLineNo int,VendorID varchar(20),ItemNo varchar(20),UnitCode varchar(20),
                    ReceivedQty decimal(18,3),OrderQty decimal(18,3),Status varchar(20),OrderDate date,DueDate date,SupplierConfirmedAt datetime2,
                    SupplierConfirmedBy varchar(20),DeliveryDestination varchar(50),UnitPrice decimal(18,3),Currency varchar(3),
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
                CREATE TABLE MD_Location(LocationID varchar(50),ActiveFlag bit,LocationType varchar(20),Capacity decimal(18,3));
                CREATE TABLE WH_Inventory(LotNo nvarchar(50) PRIMARY KEY,UnitType varchar(10),ParentLotNo nvarchar(50),CaseNo varchar(50),PartNo varchar(50),PartName nvarchar(200),
                    Qty decimal(18,3),LocationNo varchar(50),InvoiceNo nvarchar(100),DeliveryNoteNo nvarchar(60),ReceivedAt datetime2,CreatedAt datetime2,UpdatedAt datetime2);
                CREATE TABLE WH_InventoryTransaction(TransactionID bigint IDENTITY PRIMARY KEY,TransactionTime datetime2 DEFAULT SYSDATETIME(),
                    TransactionType varchar(10),PartNo varchar(50),LocationNo varchar(50),LotNo nvarchar(50),QtyBefore decimal(18,3),QtyChange decimal(18,3),QtyAfter decimal(18,3),
                    ReasonCode varchar(30),SourceType varchar(30),SourceID int,OperatorID nvarchar(450),Note nvarchar(500),CreatedBy varchar(50),CreatedTS datetime2);
                INSERT MD_Vendor VALUES('V1',N'Vendor One',1),('V2',N'Vendor Two',1);
                INSERT MD_Item VALUES('PART-1','Part One','MATERIAL','EA',1),('PART-2','Part Two','MATERIAL','EA',1),('PART-3','Part Three','MATERIAL','EA',1);
                INSERT SCM_ItemVendor VALUES('PART-1','V1',4,1),('PART-2','V1',5,1),('PART-3','V2',5,1);
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
            Assert.Null(repo.GetDeliveryCase(first,"other-user"));
            Assert.Equal("Prepared",repo.GetDeliveryCase(first,"case-user")!.Status);
            Assert.Throws<SqlException>(()=>repo.ReceiveDeliveryCase(first,"LOCAL","case-user"));
            Assert.Throws<InvalidOperationException>(()=>repo.CreatePreparedCase("PO-CASE-1",[new(1,4),new(1,4),new(1,4)],"case-user"));
            Assert.Throws<ArgumentException>(()=>repo.RegisterSupplierDelivery("PO-CASE-1",DateTime.Today,[new(1,4)],Versions("PO-CASE-1"),Guid.NewGuid(),"case-user","case-user",[]));
            Assert.Throws<InvalidOperationException>(()=>repo.RegisterSupplierDelivery("PO-CASE-1",DateTime.Today,[new(1,11)],Versions("PO-CASE-1"),Guid.NewGuid(),"case-user","case-user",[first,second]));
            Assert.Throws<SqlException>(()=>repo.RegisterSupplierDelivery("PO-CASE-1",DateTime.Today,[new(1,12)],Versions("PO-CASE-1"),Guid.NewGuid(),"case-user","case-user",[first,rollbackCase]));
            var request=Guid.NewGuid();
            var firstDelivery=repo.RegisterSupplierDelivery("PO-CASE-1",DateTime.Today,[new(1,12)],Versions("PO-CASE-1"),request,"case-user","case-user",[first,second]);
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
            Sql("DELETE MD_Item WHERE ItemNo='PART-3';");
            Assert.Equal("Item master missing",Assert.Single(repo.ListCaseParts("PO-CASE-3","other-user")).Issue);
        }
        finally
        {
            SqlConnection.ClearAllPools();
            using var cmd=new SqlCommand($"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}];",master);
            cmd.ExecuteNonQuery();
        }
    }
}
