using System.Data;
using System.Reflection;
using AMES.Contracts.Dto;
using AMES.Data.Connection;
using AMES.Data.Repositories;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AMES.Data.Tests;

// Explicit opt-in only: unique test documents/locations, never an existing stock LOT.
public sealed class PdaApprovedFlowDbTests
{
    static AmesConnectionFactory Factory()
    {
        var connection = Environment.GetEnvironmentVariable("AMES_PDA_APPROVED_FLOW_DB");
        Skip.If(string.IsNullOrWhiteSpace(connection), "Requires approval and AMES_PDA_APPROVED_FLOW_DB; creates only uniquely named test data.");
        return new(connection!);
    }

    [SkippableFact]
    public void Supplier_box_receive_move_adjust_cancel_and_receive_again_keep_both_balances()
    {
        var factory = Factory();
        using var conn = factory.OpenConnection();
        using var tx = conn.BeginTransaction();
        try
        {
            var f = CreateReceipt(conn, tx);
            Receive(conn, tx, f);
            Balance(conn, tx, f, 30, 30);
            Run(conn, tx, PdaWarehouseSql.InboundMove, ("@ReceiveMode", "LOCAL"), ("@LotBarcode", f.Box),
                ("@LocationId", f.Location), ("@UserId", f.Actor));
            Assert.Equal(f.Location, Value(conn, tx, "SELECT LocationNo FROM dbo.WH_Inventory WHERE LotNo=@Box", ("@Box", f.Box)));
            var reason = (string)Value(conn, tx, "SELECT TOP(1) CodeValue FROM dbo.MD_CodeItem WHERE GroupCode='INV_ADJUST_REASON' AND UseFlag=1 AND CodeValue NOT IN('DAMAGED','DEFECT','DEFECTIVE') ORDER BY CASE WHEN CodeValue='COUNT_DIFF' THEN 0 ELSE 1 END,CodeValue")!;
            Run(conn, tx, PdaWarehouseSql.AdjustSave, ("@ScanText", f.Box), ("@DeltaQty", -5m),
                ("@ReasonCode", reason), ("@ReasonNote", "Approved isolated verification"), ("@UserId", f.Actor), ("@SimulateFailure", false));
            Balance(conn, tx, f, 25, 30);
            Run(conn, tx, PdaWarehouseSql.InboundCancel, ("@ReceiveMode", "LOCAL"), ("@LotBarcode", f.Box),
                ("@UserId", f.Actor), ("@CancelNote", "Approved isolated verification"));
            Balance(conn, tx, f, 0, 0);
            Receive(conn, tx, f);
            Balance(conn, tx, f, 30, 30);
            Assert.Equal(4, Convert.ToInt32(Value(conn, tx,
                "SELECT COUNT(*) FROM dbo.WH_InventoryTransaction WHERE LotNo=@Box AND QtyChange<>0", ("@Box", f.Box))));
        }
        finally { if (tx.Connection is not null) tx.Rollback(); }
    }

    [SkippableFact]
    public async Task Two_receivers_of_the_same_box_cannot_duplicate_stock_or_po_receipts()
    {
        var factory = Factory();
        Receipt? f = null;
        try
        {
            using (var conn = factory.OpenConnection())
            using (var tx = conn.BeginTransaction()) { f = CreateReceipt(conn, tx); tx.Commit(); }
            using var start = new ManualResetEventSlim();
            Task<int> Attempt() => Task.Run(() =>
            {
                start.Wait();
                using var conn = factory.OpenConnection();
                try { Receive(conn, null, f!); return 0; }
                catch (SqlException ex) { return ex.Number; }
            });
            var attempts = new[] { Attempt(), Attempt() };
            start.Set();
            var outcomes = await Task.WhenAll(attempts).WaitAsync(TimeSpan.FromSeconds(40));
            Assert.Single(outcomes, n => n == 0);
            Assert.Single(outcomes, n => n == 51416);
            using var check = factory.OpenConnection();
            Balance(check, null, f!, 30, 30);
            Assert.Equal(1, Convert.ToInt32(Value(check, null,
                "SELECT COUNT(*) FROM dbo.WH_InventoryTransaction WHERE LotNo=@Box AND TransactionType='IN'", ("@Box", f!.Box))));
        }
        finally
        {
            if (f is not null)
            {
                using var conn = factory.OpenConnection();
                Run(conn, null, """
                    DELETE dbo.WH_InventoryTransaction WHERE LotNo=@Box AND CreatedBy=@Actor;
                    DELETE dbo.WH_Inventory WHERE LotNo=@Box;
                    DELETE dbo.SCM_DeliveryBox WHERE DeliveryLineID=@Line;
                    DELETE dbo.SCM_DeliveryNoteDelivery WHERE NoteID=@Note;
                    DELETE dbo.SCM_DeliveryNote WHERE NoteID=@Note AND IssuedBy=@Actor;
                    DELETE dbo.SCM_DeliveryLine WHERE DeliveryLineID=@Line;
                    DELETE dbo.SCM_Delivery WHERE DeliveryID=@Delivery AND CreatedBy=@Actor;
                    DELETE dbo.WH_PurchaseOrder WHERE PoID=@Po AND CreatedBy=@Actor;
                    DELETE dbo.MD_Location WHERE LocationID=@Location AND CreatedBy=@Actor;
                    """, ("@Box", f.Box), ("@Actor", f.Actor), ("@Line", f.Line), ("@Note", f.Note),
                    ("@Delivery", f.Delivery), ("@Po", f.Po), ("@Location", f.Location));
            }
        }
    }

    [SkippableFact]
    public void Empty_pallet_return_rejects_occupied_stale_and_duplicate_confirmations()
    {
        var factory = Factory();
        var actor = "VFY" + Guid.NewGuid().ToString("N")[..10];
        var pallet = actor + "-PALLET";
        var lot = actor + "-CONTENTS";
        using var conn = factory.OpenConnection();
        try
        {
            Run(conn, null, """
                INSERT dbo.MD_Pallet(PalletNo,PalletUse,ShippedFlag,ActiveFlag,CreatedBy,ShippedAt)
                VALUES(@Pallet,'INTERNAL',1,1,@Actor,SYSDATETIME());
                INSERT dbo.WH_Inventory(LotNo,UnitType,PalletNo,Qty,ReceivedAt)
                VALUES(@Pallet,'PALLET',@Pallet,0,SYSDATETIME());
                INSERT dbo.WH_Inventory(LotNo,UnitType,ParentLotNo,PartNo,Qty,ReceivedAt)
                SELECT @Lot,'PART',@Pallet,ItemNo,1,SYSDATETIME() FROM dbo.MD_Item
                WHERE ItemNo=(SELECT TOP(1) ItemNo FROM dbo.MD_Item WHERE ActiveFlag=1 ORDER BY ItemNo);
                """, ("@Pallet", pallet), ("@Lot", lot), ("@Actor", actor));
            var repo = new FinishedGoodsRepository(factory);
            var scan = repo.ScanEmptyPalletReturn(pallet);
            var request = new PalletReturnRequest(pallet, scan.ShippedAt);
            Assert.Equal(52021, Assert.Throws<SqlException>(() => repo.ReceiveEmptyPalletReturn(request, actor)).Number);
            Assert.Equal(1m, Convert.ToDecimal(Value(conn, null, "SELECT Qty FROM dbo.WH_Inventory WHERE LotNo=@Lot", ("@Lot", lot))));
            Run(conn, null, "DELETE dbo.WH_Inventory WHERE LotNo=@Lot AND ParentLotNo=@Pallet", ("@Lot", lot), ("@Pallet", pallet));
            Assert.Throws<InvalidOperationException>(() => repo.ReceiveEmptyPalletReturn(new(pallet, scan.ShippedAt.AddSeconds(-1)), actor));
            Assert.NotNull(repo.ReceiveEmptyPalletReturn(request, actor).ReturnedAt);
            Assert.False((bool)Value(conn, null, "SELECT ShippedFlag FROM dbo.MD_Pallet WHERE PalletNo=@Pallet", ("@Pallet", pallet))!);
            Assert.Throws<InvalidOperationException>(() => repo.ScanEmptyPalletReturn(pallet));
            Assert.Equal(1, Convert.ToInt32(Value(conn, null, "SELECT COUNT(*) FROM dbo.SYS_AuditLog WHERE TargetEntity='MD_Pallet' AND TargetID=@Pallet AND CreatedBy=@Actor", ("@Pallet", pallet), ("@Actor", actor))));
            Assert.Equal(1, Convert.ToInt32(Value(conn, null, """
                SELECT COUNT(*) FROM dbo.WH_InventoryTransaction
                WHERE LotNo=@Pallet AND SourceType='PALLET_RETURN' AND ReasonCode='PALLET_RETURN'
                    AND TransactionType='IN' AND PartNo IS NULL AND LocationNo IS NULL
                    AND QtyBefore=0 AND QtyChange=0 AND QtyAfter=0 AND OperatorID=@Actor;
                """, ("@Pallet", pallet), ("@Actor", actor))));
            Assert.Throws<InvalidOperationException>(() => repo.ReceiveEmptyPalletReturn(request, actor));
            Assert.Single(new PdaTransactionRepository(factory).ReadPage(pallet, scan.ShippedAt.Date, scan.ShippedAt.Date.AddDays(1)).Items,
                row => row.ReasonCode == "PALLET_RETURN" && row.Barcode == pallet && row.LocationNo is null && row.DeltaQty == 0);
        }
        finally
        {
            Run(conn, null, """
                DELETE dbo.WH_InventoryTransaction WHERE LotNo=@Pallet AND SourceType='PALLET_RETURN' AND CreatedBy=@Actor;
                DELETE dbo.WH_Inventory WHERE LotNo=@Lot AND ParentLotNo=@Pallet;
                DELETE dbo.WH_Inventory WHERE LotNo=@Pallet AND PalletNo=@Pallet AND Qty=0;
                DELETE dbo.SYS_AuditLog WHERE TargetEntity='MD_Pallet' AND TargetID=@Pallet AND CreatedBy=@Actor;
                DELETE dbo.MD_Pallet WHERE PalletNo=@Pallet AND CreatedBy=@Actor;
                """, ("@Lot", lot), ("@Pallet", pallet), ("@Actor", actor));
        }
    }

    [SkippableTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void Finished_goods_ship_return_inspect_and_reassign_without_receiving_twice(bool accepted)
    {
        var factory = Factory();
        using var conn = factory.OpenConnection();
        using var tx = conn.BeginTransaction();
        var key = "VFY" + Guid.NewGuid().ToString("N")[..10];
        var lot = key + "-LOT";
        try
        {
            Run(conn, tx, """
                INSERT dbo.MD_Location(LocationID,LocationName,WhCode,AreaCode,LocationType,LocationLevel,Capacity,ActiveFlag,CreatedBy)
                VALUES(@Key,'Isolated FG verification','EOS','FG_AREA','FG',2,1,1,@Key);
                INSERT dbo.tbl_Lot(LotCode,ItemNo,LineID,ProcessCode,BatchSize,RemainingQty,ProducedAt,Status,QualityFlag,InventoryStatus,CreatedBy,CreatedTS)
                VALUES(@Lot,'M2320-PI010NNB','LINE-IMG-01','IMG',1,1,SYSDATETIME(),'CONFIRMED','OK','CREATED',@Key,SYSDATETIME());
                DECLARE @LotID int=CONVERT(int,SCOPE_IDENTITY());
                INSERT dbo.PR_ProductionResult(EntryNo,LotID,LineID,ProcessCode,GoodQty,OperatorID,DefectFlag,EntryAt,ProdDate,CreatedBy,CreatedTS)
                VALUES(@Key,@LotID,'LINE-IMG-01','IMG',1,@Key,0,SYSDATETIME(),CONVERT(date,SYSDATETIME()),@Key,SYSDATETIME());
                """, ("@Key", key), ("@Lot", lot));
            void PutAway() => Run(conn, tx, PdaFinishedGoodsSql.PutAway,
                ("@Barcode", lot), ("@Location", key), ("@OperatorID", key));
            PutAway();
            var customer = Value(conn, tx, "SELECT TOP(1) CustomerID FROM dbo.MD_Customer WHERE UPPER(COALESCE(Status,'ACTIVE')) NOT IN('INACTIVE','DISABLED') ORDER BY CustomerID");
            Run(conn, tx, PdaFinishedGoodsSql.OutboundComplete, ("@Barcode", lot), ("@CustomerID", customer), ("@OperatorID", key), ("@ShippingDeliveryID", null));
            Assert.Equal(0m, Convert.ToDecimal(Value(conn, tx, "SELECT Qty FROM dbo.WH_Inventory WHERE LotNo=@Lot", ("@Lot", lot))));
            var returnReason = Value(conn, tx, "SELECT TOP(1) CodeValue FROM dbo.MD_CodeItem WHERE GroupCode='FG_RETURN_REASON' AND UseFlag=1 ORDER BY CodeValue");
            Assert.NotNull(returnReason);
            Run(conn, tx, PdaFinishedGoodsSql.ReturnReceive, ("@Barcode", lot), ("@ReturnReason", returnReason), ("@Note", "Approved isolated return verification"), ("@OperatorID", key));
            Assert.Equal("Inspecting", Value(conn, tx, "SELECT Status FROM dbo.FG_CustomerReturn WHERE LotNo=@Lot", ("@Lot", lot)));
            Assert.Equal(DBNull.Value, Value(conn, tx, "SELECT LocationNo FROM dbo.WH_Inventory WHERE LotNo=@Lot", ("@Lot", lot)));
            var user = Value(conn, tx, """
                SELECT TOP(1) U.UserId FROM dbo.AspNetUserRoles U
                JOIN dbo.SYS_RolePermission P ON P.RoleID=U.RoleId AND P.ModuleCode='WEB' AND P.ScreenCode='FG-DEFECT'
                JOIN dbo.SYS_Screen S ON S.ScreenCode=P.ScreenCode AND S.ModuleCode='WEB'
                JOIN dbo.AspNetUsers A ON A.Id=U.UserId LEFT JOIN dbo.SYS_UserProfile UP ON UP.UserID=A.Id
                WHERE P.PermissionLevel LIKE '%A%' AND SUBSTRING(S.PermissionCriteria,3,1)<>'X'
                AND (A.LockoutEnd IS NULL OR A.LockoutEnd<=SYSDATETIMEOFFSET()) AND COALESCE(UP.AccountStatus,'ACTIVE')='ACTIVE';
                """);
            Assert.NotNull(user);
            var inspection = (string)typeof(FinishedGoodsRepository).GetField("ReturnInspectionSql", BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue()!;
            Run(conn, tx, inspection, ("@Return", Value(conn, tx, "SELECT ReturnID FROM dbo.FG_CustomerReturn WHERE LotNo=@Lot", ("@Lot", lot))),
                ("@Accepted", accepted), ("@Note", "Isolated inspection test"), ("@User", user), ("@Actor", key));
            Assert.Equal(accepted ? "Closed" : "Rejected", Value(conn, tx, "SELECT Status FROM dbo.FG_CustomerReturn WHERE LotNo=@Lot", ("@Lot", lot)));
            if (accepted)
            {
                PutAway();
                Assert.Equal(key, Value(conn, tx, "SELECT LocationNo FROM dbo.WH_Inventory WHERE LotNo=@Lot", ("@Lot", lot)));
                Assert.Equal(2, Convert.ToInt32(Value(conn, tx, "SELECT COUNT(*) FROM dbo.WH_InventoryTransaction WHERE LotNo=@Lot AND TransactionType='IN'", ("@Lot", lot))));
            }
            Assert.Equal(accepted ? 1m : 0m, Convert.ToDecimal(Value(conn, tx, "SELECT Qty FROM dbo.WH_Inventory WHERE LotNo=@Lot", ("@Lot", lot))));
        }
        finally { if (tx.Connection is not null) tx.Rollback(); }
    }

    [SkippableFact]
    public void Spare_part_release_links_to_the_test_work_order()
    {
        var factory = Factory();
        using var conn = factory.OpenConnection();
        using var tx = conn.BeginTransaction();
        var key = "VFY" + Guid.NewGuid().ToString("N")[..10];
        try
        {
            Run(conn, tx, """
                INSERT dbo.MNT_WorkOrder(WoNumber,Status,CreatedBy) VALUES(@Key,'IN_PROGRESS',@Key);
                INSERT dbo.MD_SparePart(SparePartNo,PartNo,PartName,ZoneCode,Slot,OnHandQty,ActiveFlag,CreatedBy)
                VALUES(@Key,@Key,'Isolated work-order verification','SP_A1','01',0,1,@Key);
                """, ("@Key", key));
            var serial = Value(conn, tx, PdaSparePartsSql.SerialCreate, ("@SparePartNo", key), ("@Count", 1), ("@UserId", key));
            void Move(string type, object? order) => Run(conn, tx, PdaSparePartsSql.Move,
                ("@SerialNo", serial), ("@MoveType", type), ("@Qty", 1), ("@LocationId", null), ("@WorkOrderNo", order), ("@UserId", key), ("@Note", "Isolated work-order verification"));
            Move("IN", null);
            Move("OUT", key);
            Assert.Equal(0, Convert.ToInt32(Value(conn, tx, "SELECT OnHandQty FROM dbo.MD_SparePart WHERE SparePartNo=@Key", ("@Key", key))));
            Assert.Equal(1, Convert.ToInt32(Value(conn, tx,
                "SELECT COUNT(*) FROM dbo.MNT_SparePartsTxn WHERE SparePartNo=@Key AND MoveType='OUT' AND RefType='MWO' AND RefID=@Key", ("@Key", key))));
        }
        finally { if (tx.Connection is not null) tx.Rollback(); }
    }

    [SkippableFact]
    public async Task Competing_replenishment_workers_back_off_while_the_shared_gate_is_owned()
    {
        var factory = Factory();
        using var conn = factory.OpenConnection();
        using var tx = conn.BeginTransaction();
        try
        {
            Assert.True(Convert.ToInt32(Value(conn, tx, """
                DECLARE @Lock int;
                EXEC @Lock=sys.sp_getapplock @Resource='WH_LINE_REPLENISHMENT',
                    @LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=5000;
                SELECT @Lock;
                """)) >= 0);
            var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(() =>
                new WarehouseRepository(factory).GenerateLinePickingOrders()))).WaitAsync(TimeSpan.FromSeconds(15));
            Assert.All(results, result => { Assert.Equal(0, result.Orders); Assert.Equal(0, result.Lines); });
        }
        finally { if (tx.Connection is not null) tx.Rollback(); }
    }

    sealed record Receipt(string Actor, string Box, string Location, int Po, int Delivery, int Line, int Note);

    static Receipt CreateReceipt(SqlConnection conn, SqlTransaction tx)
    {
        var key = "VFY" + Guid.NewGuid().ToString("N")[..10];
        using var cmd = Command(conn, tx, """
            SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON;
            DECLARE @Part varchar(20),@Name nvarchar(200),@Unit varchar(10),@Vendor varchar(20);
            SELECT TOP(1) @Part=I.ItemNo,@Name=I.ItemName,@Unit=I.DefaultUOM,@Vendor=V.VendorID
            FROM dbo.MD_Item I JOIN dbo.SCM_ItemVendor V ON V.ItemNo=I.ItemNo AND V.ActiveFlag=1
            WHERE I.ItemType<>'ASSY' AND I.ActiveFlag=1 ORDER BY I.ItemNo;
            IF @Part IS NULL THROW 51999,'No active supplier material exists for verification.',1;
            INSERT dbo.MD_Location(LocationID,LocationName,WhCode,AreaCode,LocationLevel,Capacity,ActiveFlag,CreatedBy)
            VALUES(@Location,'Isolated verification','EOS','MAT_AREA',2,100,1,@Key);
            INSERT dbo.WH_PurchaseOrder(PoNumber,PoLineNo,VendorID,ItemNo,UnitCode,OrderQty,ReceivedQty,Status,OrderDate,DueDate,CreatedBy)
            VALUES(@Key,1,@Vendor,@Part,@Unit,60,0,'Open',CONVERT(date,SYSDATETIME()),CONVERT(date,SYSDATETIME()),@Key);
            DECLARE @Po int=CONVERT(int,SCOPE_IDENTITY());
            INSERT dbo.SCM_Delivery(DeliveryNumber,RequestID,PoNumber,VendorID,DeliveryDate,Status,CreatedUserID,CreatedBy)
            VALUES(CONCAT(@Key,'-D'),NEWID(),@Key,@Vendor,CONVERT(date,SYSDATETIME()),'Shipped',@Key,@Key);
            DECLARE @Delivery int=CONVERT(int,SCOPE_IDENTITY());
            INSERT dbo.SCM_DeliveryLine(DeliveryID,PoID,Quantity,ReceivedQty,PackingQty) VALUES(@Delivery,@Po,60,0,30);
            DECLARE @Line int=CONVERT(int,SCOPE_IDENTITY());
            INSERT dbo.SCM_DeliveryNote(NoteNumber,VendorID,Snapshot,IssuedAt,IssuedBy,IssuedUserID)
            VALUES(CONCAT(@Key,'-N'),@Vendor,N'{}',SYSDATETIME(),@Key,@Key);
            DECLARE @Note int=CONVERT(int,SCOPE_IDENTITY());
            INSERT dbo.SCM_DeliveryNoteDelivery(NoteID,DeliveryID) VALUES(@Note,@Delivery);
            INSERT dbo.SCM_DeliveryBox(IssuedBoxNumber,DeliveryLineID,BoxSeq,ItemNo,ItemName,UnitCode,Quantity,ActiveFlag)
            VALUES(@Box,@Line,1,@Part,@Name,@Unit,30,1);
            SELECT @Po,@Delivery,@Line,@Note;
            """, ("@Key", key), ("@Box", key + "-BOX"), ("@Location", key + "-LOC"));
        using var reader = cmd.ExecuteReader();
        Assert.True(reader.Read());
        return new(key, key + "-BOX", key + "-LOC", reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3));
    }

    static void Receive(SqlConnection conn, SqlTransaction? tx, Receipt f) => Run(conn, tx, PdaWarehouseSql.InboundReceive,
        ("@ReceiveMode", "LOCAL"), ("@LotBarcode", f.Box), ("@LocationId", null), ("@UserId", f.Actor), ("@SimulateFailure", false));

    static void Balance(SqlConnection conn, SqlTransaction? tx, Receipt f, decimal stock, decimal receipt)
    {
        Assert.Equal(stock, Convert.ToDecimal(Value(conn, tx, "SELECT COALESCE(SUM(Qty),0) FROM dbo.WH_Inventory WHERE LotNo=@Box", ("@Box", f.Box))));
        Assert.Equal(receipt, Convert.ToDecimal(Value(conn, tx, "SELECT ReceivedQty FROM dbo.WH_PurchaseOrder WHERE PoID=@Po", ("@Po", f.Po))));
        Assert.Equal(receipt, Convert.ToDecimal(Value(conn, tx, "SELECT ReceivedQty FROM dbo.SCM_DeliveryLine WHERE DeliveryLineID=@Line", ("@Line", f.Line))));
    }

    static SqlCommand Command(SqlConnection conn, SqlTransaction? tx, string sql, params (string Name, object? Value)[] values)
    {
        var cmd = new SqlCommand(sql, conn, tx) { CommandTimeout = 20 };
        foreach (var (name, value) in values)
        {
            var p = cmd.Parameters.Add(name, value switch { int => SqlDbType.Int, decimal => SqlDbType.Decimal, bool => SqlDbType.Bit, _ => SqlDbType.NVarChar });
            p.Value = value ?? DBNull.Value;
            if (p.SqlDbType == SqlDbType.Decimal) { p.Precision = 18; p.Scale = 3; }
            if (p.SqlDbType == SqlDbType.NVarChar) p.Size = value is string text ? Math.Max(1, text.Length) : 50;
        }
        return cmd;
    }
    static void Run(SqlConnection conn, SqlTransaction? tx, string sql, params (string Name, object? Value)[] values)
    { using var cmd = Command(conn, tx, sql, values); cmd.ExecuteNonQuery(); }
    static object? Value(SqlConnection conn, SqlTransaction? tx, string sql, params (string Name, object? Value)[] values)
    { using var cmd = Command(conn, tx, sql, values); return cmd.ExecuteScalar(); }
}
