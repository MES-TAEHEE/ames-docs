using System.Data;
using System.Net.Http.Json;
using AMES.Contracts.Dto;
using AMES.Data.Repositories;
using AMES.Pda.Services;
using Microsoft.Data.SqlClient;
using Xunit;
using Xunit.Abstractions;

namespace AMES.Api.Tests;

// Requires both server and isolated-data approval. Never resets a database or starts workers.
public sealed class PdaLiveStockFlowTests(ITestOutputHelper output)
{
    [LiveStockFact]
    public async Task SP_receive_release_cancel_and_stale_cancel_preserve_the_selected_transaction()
    {
        using var db = Open();
        var key = Key();
        using var http = PdaLiveApiAuditTests.Client();
        var auth = await PdaLiveApiAuditTests.SignIn(http);
        var api = new SparePartsApi(http, auth);
        string? serial = null;
        try
        {
            Exec(db, """
                INSERT dbo.MD_SparePart(SparePartNo,PartNo,PartName,ZoneCode,Slot,OnHandQty,ActiveFlag,CreatedBy)
                VALUES(@Key,@Key,'PDA API isolated audit','SP_A1','01',0,1,@Key);
                INSERT dbo.MNT_WorkOrder(WoNumber,Status,CreatedBy) VALUES(@Key,'IN_PROGRESS',@Key);
                """, ("@Key", key));
            Assert.NotNull(await api.SpItemAsync(key)); // Proves API and fixture use the same DB before any stock write.
            var labels = await api.CreateLabelsAsync(key, 1);
            serial = Assert.Single(labels).SerialNo;
            Assert.False(string.IsNullOrWhiteSpace(serial));
            var received = await api.SpReceiveAsync(new(serial!));
            Assert.True(received.Success, received.Message);
            Assert.Equal(1m, received.Row!.Qty);
            var duplicate = await api.SpReceiveAsync(new(serial!));
            Assert.False(duplicate.Success);
            var release = await api.SpReleaseAsync(new(serial!, WorkOrderNo: key));
            Assert.True(release.Success, release.Message);
            var first = LastTxn(db, key, "OUT");
            Assert.True((await api.CancelMoveAsync(serial!, "OUT", transactionId: first)).Success);
            Assert.True((await api.SpReleaseAsync(new(serial!, WorkOrderNo: key))).Success);
            var second = LastTxn(db, key, "OUT"); Assert.NotEqual(first, second);
            var stale = await api.CancelMoveAsync(serial!, "OUT", transactionId: first);
            Assert.False(stale.Success, "Deployed API cancelled the latest release when the user selected an older transaction.");
            Assert.Equal(0, Convert.ToInt32(Scalar(db, "SELECT OnHandQty FROM dbo.MD_SparePart WHERE SparePartNo=@Key", ("@Key", key))));
            Assert.True((await api.CancelMoveAsync(serial!, "OUT", transactionId: second)).Success);
            Assert.True((await api.CancelMoveAsync(serial!, "IN", transactionId: LastTxn(db, key, "IN"))).Success);
            Assert.Equal("CREATED", Scalar(db, "SELECT StatusCode FROM dbo.MNT_SparePartItem WHERE SerialNo=@Serial", ("@Serial", serial)));
            output.WriteLine("SP API: create serial, receipt, duplicate receipt rejection, linked release, cancellation, stale transaction protection and inbound reversal passed.");
        }
        finally
        {
            Exec(db, """
                DELETE dbo.SYS_AuditLog WHERE TargetEntity='MNT_SparePartItem' AND TargetID=@Serial;
                DELETE dbo.MNT_SparePartsTxn WHERE SparePartNo=@Key AND ReversalOfTxnID IS NOT NULL;
                DELETE dbo.MNT_SparePartsTxn WHERE SparePartNo=@Key;
                DELETE dbo.MNT_SparePartItem WHERE SparePartNo=@Key;
                DELETE dbo.MD_SparePart WHERE SparePartNo=@Key AND CreatedBy=@Key;
                DELETE dbo.MNT_WorkOrder WHERE WoNumber=@Key AND CreatedBy=@Key;
                """, ("@Key", key), ("@Serial", serial));
            using var logout = await http.PostAsync("/api/auth/logout", null);
        }
    }

    [LiveStockFact]
    public async Task WH_API_receives_moves_cancels_and_receives_again_without_duplicating_PO_balances()
    {
        using var db = Open();
        var key = Key(); var box = key + "-BOX"; var location = key + "-LOC";
        using var http = PdaLiveApiAuditTests.Client();
        var auth = await PdaLiveApiAuditTests.SignIn(http);
        var wh = new WarehouseApi(http, auth, new(http, auth), new(http, auth));
        try
        {
            Exec(db, """
                DECLARE @Part varchar(20),@Name nvarchar(200),@Unit varchar(10),@Vendor varchar(20);
                SELECT TOP(1) @Part=I.ItemNo,@Name=I.ItemName,@Unit=I.DefaultUOM,@Vendor=V.VendorID
                FROM dbo.MD_Item I JOIN dbo.SCM_ItemVendor V ON V.ItemNo=I.ItemNo AND V.ActiveFlag=1
                WHERE I.ItemType<>'ASSY' AND I.ActiveFlag=1 ORDER BY I.ItemNo;
                IF @Part IS NULL THROW 51999,'No supplier material available for isolated audit.',1;
                INSERT dbo.MD_Location(LocationID,LocationName,WhCode,AreaCode,LocationLevel,Capacity,ActiveFlag,CreatedBy)
                VALUES(@Location,'PDA API isolated audit','EOS','MAT_AREA',2,100,1,@Key);
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
                INSERT dbo.SCM_DeliveryNoteDelivery(NoteID,DeliveryID) VALUES(CONVERT(int,SCOPE_IDENTITY()),@Delivery);
                INSERT dbo.SCM_DeliveryBox(IssuedBoxNumber,DeliveryLineID,BoxSeq,ItemNo,ItemName,UnitCode,Quantity,ActiveFlag)
                VALUES(@Box,@Line,1,@Part,@Name,@Unit,30,1);
                """, ("@Key", key), ("@Box", box), ("@Location", location));
            Assert.Equal(box, (await wh.WhScanInboundAsync("LOCAL", box))!.Barcode);
            async Task Receive()
            {
                using var response = await http.PostAsJsonAsync("/api/wh/inbound/receive-lot", new { mode = "LOCAL", barcode = box, locationId = "" });
                response.EnsureSuccessStatusCode();
                var result = await response.Content.ReadFromJsonAsync<PdaApi.InboundReceiveResult>();
                Assert.True(result!.Success, result.Message);
            }
            void Balance(decimal qty)
            {
                Assert.Equal(qty, Convert.ToDecimal(Scalar(db, "SELECT COALESCE(SUM(Qty),0) FROM dbo.WH_Inventory WHERE LotNo=@Box", ("@Box", box))));
                Assert.Equal(qty, Convert.ToDecimal(Scalar(db, "SELECT ReceivedQty FROM dbo.WH_PurchaseOrder WHERE PoNumber=@Key", ("@Key", key))));
                Assert.Equal(qty, Convert.ToDecimal(Scalar(db, "SELECT L.ReceivedQty FROM dbo.SCM_DeliveryLine L JOIN dbo.SCM_Delivery D ON D.DeliveryID=L.DeliveryID WHERE D.CreatedBy=@Key", ("@Key", key))));
            }
            await Receive(); Balance(30);
            using (var move = await http.PostAsJsonAsync("/api/wh/inbound/move-location", new { mode = "LOCAL", barcode = box, locationId = location }))
            {
                move.EnsureSuccessStatusCode();
                var result = await move.Content.ReadFromJsonAsync<PdaApi.InboundReceiveResult>(); Assert.True(result!.Success, result.Message);
            }
            Assert.Equal(location, Scalar(db, "SELECT LocationNo FROM dbo.WH_Inventory WHERE LotNo=@Box", ("@Box", box)));
            using (var cancel = await http.PostAsJsonAsync("/api/wh/inbound/cancel", new { mode = "LOCAL", barcode = box }))
            {
                cancel.EnsureSuccessStatusCode();
                var result = await cancel.Content.ReadFromJsonAsync<PdaApi.InboundReceiveResult>(); Assert.True(result!.Success, result.Message);
            }
            Balance(0); await Receive(); Balance(30);
            output.WriteLine("WH API: receipt, location assignment, cancel and re-receipt; stock, PO and delivery received balances all matched.");
        }
        finally
        {
            Exec(db, """
                DELETE dbo.WH_InventoryTransaction WHERE LotNo=@Box;
                DELETE dbo.WH_Inventory WHERE LotNo=@Box;
                DELETE B FROM dbo.SCM_DeliveryBox B JOIN dbo.SCM_DeliveryLine L ON L.DeliveryLineID=B.DeliveryLineID
                    JOIN dbo.SCM_Delivery D ON D.DeliveryID=L.DeliveryID WHERE D.CreatedBy=@Key;
                DELETE N FROM dbo.SCM_DeliveryNoteDelivery N JOIN dbo.SCM_Delivery D ON D.DeliveryID=N.DeliveryID WHERE D.CreatedBy=@Key;
                DELETE dbo.SCM_DeliveryNote WHERE IssuedBy=@Key AND NoteNumber=CONCAT(@Key,'-N');
                DELETE L FROM dbo.SCM_DeliveryLine L JOIN dbo.SCM_Delivery D ON D.DeliveryID=L.DeliveryID WHERE D.CreatedBy=@Key;
                DELETE dbo.SCM_Delivery WHERE CreatedBy=@Key AND DeliveryNumber=CONCAT(@Key,'-D');
                DELETE dbo.WH_PurchaseOrder WHERE CreatedBy=@Key AND PoNumber=@Key;
                DELETE dbo.MD_Location WHERE CreatedBy=@Key AND LocationID=@Location;
                """, ("@Key", key), ("@Box", box), ("@Location", location));
            using var logout = await http.PostAsync("/api/auth/logout", null);
        }
    }

    [LiveStockTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FG_API_checks_pallet_contents_capacity_shipping_returns_and_empty_pallet_recovery(bool useDcr)
    {
        using var db = Open();
        var key = Key(); var pallet = key + "-P"; var lotA = key + "-A"; var lotB = key + "-B";
        using var http = PdaLiveApiAuditTests.Client();
        var auth = await PdaLiveApiAuditTests.SignIn(http);
        var api = new FinishedGoodsApi(http, auth);
        try
        {
            Exec(db, """
                DECLARE @Part varchar(20)=(SELECT TOP(1) ItemNo FROM dbo.MD_Item WHERE ItemType='ASSY' AND ActiveFlag=1 ORDER BY ItemNo);
                IF @Part IS NULL THROW 51999,'No active ASSY part is available for isolated audit.',1;
                INSERT dbo.MD_Location(LocationID,LocationName,WhCode,AreaCode,LocationType,LocationLevel,Capacity,ActiveFlag,CreatedBy)
                VALUES(@Key,'PDA isolated FG audit','EOS','FG_AREA','FG',2,1,1,@Key);
                INSERT dbo.MD_Pallet(PalletNo,PalletUse,ActiveFlag,ShippedFlag,CreatedBy) VALUES(@Pallet,'INTERNAL',1,0,@Key);
                INSERT dbo.tbl_Lot(LotCode,ItemNo,LineID,ProcessCode,BatchSize,RemainingQty,ProducedAt,Status,QualityFlag,InventoryStatus,CreatedBy,CreatedTS)
                VALUES(@A,@Part,'LINE-IMG-01','IMG',1,1,SYSDATETIME(),'CONFIRMED','OK','CREATED',@Key,SYSDATETIME()),
                      (@B,@Part,'LINE-IMG-01','IMG',1,1,SYSDATETIME(),'CONFIRMED','OK','CREATED',@Key,SYSDATETIME());
                INSERT dbo.PR_ProductionResult(EntryNo,LotID,LineID,ProcessCode,GoodQty,OperatorID,DefectFlag,EntryAt,ProdDate,CreatedBy,CreatedTS)
                SELECT LotCode,LotID,'LINE-IMG-01','IMG',1,@Key,0,SYSDATETIME(),CONVERT(date,SYSDATETIME()),@Key,SYSDATETIME()
                FROM dbo.tbl_Lot WHERE LotCode IN(@A,@B) AND CreatedBy=@Key;
                INSERT dbo.WH_Inventory(LotNo,UnitType,PalletNo,Qty,ReceivedAt) VALUES(@Pallet,'PALLET',@Pallet,0,SYSDATETIME());
                INSERT dbo.WH_Inventory(LotNo,UnitType,ParentLotNo,PartNo,PartName,Qty,ReceivedAt)
                SELECT L.LotCode,'PART',@Pallet,L.ItemNo,I.ItemName,1,SYSDATETIME()
                FROM dbo.tbl_Lot L JOIN dbo.MD_Item I ON I.ItemNo=L.ItemNo WHERE L.LotCode IN(@A,@B) AND L.CreatedBy=@Key;
                """, ("@Key", key), ("@Pallet", pallet), ("@A", lotA), ("@B", lotB));
            var scan = await api.FgPutAwayScanAsync(pallet);
            Assert.True(scan.Success, scan.Message); Assert.Equal(2, scan.Row!.Items!.Count); Assert.Equal(2m, scan.Row.Qty);
            var expected = new Dictionary<string, decimal> { [lotA] = 1, [lotB] = 1 };
            var capacity = await api.FgConfirmPutAwayAsync(new(pallet, key, expected));
            Assert.False(capacity.Success, "A two-item pallet exceeded the location capacity of one.");
            Assert.Equal(0, Convert.ToInt32(Scalar(db, "SELECT COUNT(*) FROM dbo.WH_Inventory WHERE ParentLotNo=@Pallet AND LocationNo IS NOT NULL", ("@Pallet", pallet))));
            Exec(db, "UPDATE dbo.MD_Location SET Capacity=2 WHERE LocationID=@Key AND CreatedBy=@Key", ("@Key", key));
            var assigned = await api.FgConfirmPutAwayAsync(new(pallet, key, expected));
            Assert.True(assigned.Success, assigned.Message);
            Assert.Equal(2, Convert.ToInt32(Scalar(db, "SELECT COUNT(*) FROM dbo.WH_Inventory WHERE ParentLotNo=@Pallet AND LocationNo=@Key", ("@Pallet", pallet), ("@Key", key))));
            var customers = await api.FgOutboundCustomersAsync(); Assert.NotEmpty(customers);
            if (useDcr)
            {
                Exec(db, """
                    DECLARE @Part varchar(20)=(SELECT PartNo FROM dbo.WH_Inventory WHERE LotNo=@A);
                    INSERT dbo.PP_CustomerOrder(SoNumber,SoLineNo,CustomerID,ItemNo,OrderQty,ShippedQty,RequestedDeliveryDate,Status,CreatedBy)
                    VALUES(@Key,1,@Customer,@Part,2,0,CONVERT(date,SYSDATETIME()),'Open',@Key);
                    DECLARE @So int=CONVERT(int,SCOPE_IDENTITY());
                    INSERT dbo.FG_Delivery(DeliveryNo,CustomerID,ShipDate,Status,CreatedBy)
                    VALUES(CONCAT('DCR-',@Key),@Customer,CONVERT(date,SYSDATETIME()),'Open',@Key);
                    DECLARE @Delivery int=CONVERT(int,SCOPE_IDENTITY());
                    INSERT dbo.FG_ShippingOrder(OrderNo,CustomerID,ShipDate,Status,DeliveryID,CreatedBy)
                    VALUES(CONCAT('ORD-',@Key),@Customer,CONVERT(date,SYSDATETIME()),'Open',@Delivery,@Key);
                    INSERT dbo.FG_ShippingOrderLine(OrderID,SoID,PartNo,Qty) VALUES(CONVERT(int,SCOPE_IDENTITY()),@So,@Part,2);
                    """, ("@Key", key), ("@A", lotA), ("@Customer", customers[0].CustomerId));
                var dcr = "DCR-" + key;
                Assert.Contains(await api.OpenShippingDeliveriesAsync(), d => d.DeliveryNo == dcr);
                Assert.True((await api.ShippingDeliveryAsync(dcr)).Success);
                await Assert.ThrowsAsync<InvalidOperationException>(() => api.CompleteShippingDeliveryAsync(dcr, true));
                var loaded = await api.LoadShippingDeliveryAsync(dcr, pallet, true);
                Assert.True(loaded.Success, loaded.Message);
                await Assert.ThrowsAsync<InvalidOperationException>(() => api.LoadShippingDeliveryAsync(dcr, pallet, true));
                Assert.Equal(2m, Convert.ToDecimal(Scalar(db, "SELECT SUM(Qty) FROM dbo.WH_Inventory WHERE ParentLotNo=@Pallet", ("@Pallet", pallet))));
                Assert.True((await api.UnloadShippingDeliveryAsync(dcr, pallet)).Success);
                Assert.True((await api.LoadShippingDeliveryAsync(dcr, pallet, true)).Success);
                var completed = await api.CompleteShippingDeliveryAsync(dcr, true);
                Assert.True(completed.Success, completed.Message); Assert.Equal("Shipped", completed.Delivery!.Status);
                Assert.Equal(2m, Convert.ToDecimal(Scalar(db, "SELECT ShippedQty FROM dbo.PP_CustomerOrder WHERE SoNumber=@Key AND CreatedBy=@Key", ("@Key", key))));
                Assert.True((await api.CompleteShippingDeliveryAsync(dcr, true)).Success);
                Assert.Equal(2m, Convert.ToDecimal(Scalar(db, "SELECT ShippedQty FROM dbo.PP_CustomerOrder WHERE SoNumber=@Key AND CreatedBy=@Key", ("@Key", key))));
                output.WriteLine("DCR API: list, scan, incomplete shipment rejection, pallet load, duplicate load rejection, unload, reload, complete, SO accumulation and idempotent completion passed.");
            }
            else
            {
                var shipped = await api.FgOutboundAsync(new(pallet, customers[0].CustomerId, AllowNonFifo: true));
                Assert.True(shipped.Success, shipped.Message);
            }
            Assert.Equal(0m, Convert.ToDecimal(Scalar(db, "SELECT SUM(Qty) FROM dbo.WH_Inventory WHERE LotNo=@Pallet OR ParentLotNo=@Pallet", ("@Pallet", pallet))));
            Assert.False((await api.FgOutboundAsync(new(pallet, customers[0].CustomerId, AllowNonFifo: true))).Success);
            var reason = Convert.ToString(Scalar(db, "SELECT TOP(1) CodeValue FROM dbo.MD_CodeItem WHERE GroupCode='FG_RETURN_REASON' AND UseFlag=1 ORDER BY CodeValue"));
            var returned = await api.FgReturnAsync(new(lotA, reason!, "Isolated PDA API audit"));
            Assert.True(returned.Success, returned.Message);
            Assert.Equal("Inspecting", Scalar(db, "SELECT Status FROM dbo.FG_CustomerReturn WHERE LotNo=@A", ("@A", lotA)));
            Assert.False((await api.FgConfirmPutAwayAsync(new(lotA, key))).Success);
            var empty = await api.ScanEmptyPalletReturnAsync(pallet);
            var recovered = await api.ReceiveEmptyPalletReturnAsync(new(pallet, empty.ShippedAt));
            Assert.NotNull(recovered.ReturnedAt);
            Assert.Equal(1, Convert.ToInt32(Scalar(db, "SELECT COUNT(*) FROM dbo.WH_InventoryTransaction WHERE LotNo=@Pallet AND SourceType='PALLET_RETURN' AND LocationNo IS NULL", ("@Pallet", pallet))));
            await Assert.ThrowsAsync<InvalidOperationException>(() => api.ScanEmptyPalletReturnAsync(pallet));
            output.WriteLine("FG API: two-LOT ASSY pallet scan, capacity rejection/rollback, full location assignment, pallet outbound, duplicate rejection, return inspection hold and empty pallet transaction passed.");
        }
        finally
        {
            Exec(db, """
                DELETE dbo.WH_InventoryTransaction WHERE LotNo IN(@Pallet,@A,@B);
                DELETE dbo.FG_CustomerReturn WHERE LotNo IN(@A,@B);
                DELETE dbo.FG_Shipment WHERE LotNo IN(@Pallet,@A,@B);
                DELETE L FROM dbo.FG_DeliveryLoad L JOIN dbo.FG_Delivery D ON D.DeliveryID=L.DeliveryID WHERE D.CreatedBy=@Key;
                DELETE L FROM dbo.FG_ShippingOrderLine L JOIN dbo.FG_ShippingOrder O ON O.OrderID=L.OrderID WHERE O.CreatedBy=@Key;
                DELETE dbo.FG_ShippingOrder WHERE CreatedBy=@Key AND OrderNo=CONCAT('ORD-',@Key);
                DELETE dbo.FG_Delivery WHERE CreatedBy=@Key AND DeliveryNo=CONCAT('DCR-',@Key);
                DELETE dbo.PP_CustomerOrder WHERE CreatedBy=@Key AND SoNumber=@Key;
                DELETE dbo.WH_Inventory WHERE LotNo IN(@Pallet,@A,@B);
                DELETE dbo.PR_ProductionResult WHERE CreatedBy=@Key AND EntryNo IN(@A,@B);
                DELETE dbo.tbl_Lot WHERE CreatedBy=@Key AND LotCode IN(@A,@B);
                DELETE dbo.MD_Pallet WHERE CreatedBy=@Key AND PalletNo=@Pallet;
                DELETE dbo.SYS_AuditLog WHERE TargetEntity='MD_Pallet' AND TargetID=@Pallet;
                DELETE dbo.MD_Location WHERE CreatedBy=@Key AND LocationID=@Key;
                """, ("@Key", key), ("@Pallet", pallet), ("@A", lotA), ("@B", lotB));
            using var logout = await http.PostAsync("/api/auth/logout", null);
        }
    }

    [LiveStockFact]
    public async Task Maintenance_API_enforces_assignment_step_order_notes_and_completion()
    {
        using var db = Open(); var key = Key();
        using var http = PdaLiveApiAuditTests.Client();
        var auth = await PdaLiveApiAuditTests.SignIn(http);
        var api = new MaintenanceApi(http, auth);
        try
        {
            var board = await api.Board(null, null, false, true);
            Assert.True(board.Access.CanAct && board.Access.CanEdit, "PTEST lacks maintenance write permission; no account/role was changed.");
            Assert.False(string.IsNullOrWhiteSpace(board.Access.UserId));
            Exec(db, """
                INSERT dbo.MNT_WorkOrder(WoNumber,WoType,SourceType,AssignedTechID,Status,ActionDesc,CreatedBy,CreatedTS)
                VALUES(@Key,'CM','CHECK',@Tech,'ASSIGNED','Isolated PDA check execution',@Key,SYSDATETIME());
                DECLARE @Id int=CONVERT(int,SCOPE_IDENTITY());
                INSERT dbo.MNT_WorkOrderTask(WorkOrderID,TaskSeq,TaskName,TaskType,Result,CreatedBy,CreatedTS)
                VALUES(@Id,1,'Isolated first check','CHECK','PENDING',@Key,SYSDATETIME()),
                      (@Id,2,'Isolated second check','CHECK','PENDING',@Key,SYSDATETIME());
                """, ("@Key", key), ("@Tech", board.Access.UserId));
            var id = Convert.ToInt32(Scalar(db, "SELECT WorkOrderID FROM dbo.MNT_WorkOrder WHERE WoNumber=@Key AND CreatedBy=@Key", ("@Key", key)));
            var detail = await api.Detail(id); Assert.Equal(2, detail.Tasks.Count);
            Exec(db, "UPDATE dbo.MNT_WorkOrder SET AssignedTechID=NULL WHERE WoNumber=@Key AND CreatedBy=@Key", ("@Key", key));
            await Assert.ThrowsAsync<InvalidOperationException>(() => api.Start(id, true));
            Exec(db, "UPDATE dbo.MNT_WorkOrder SET AssignedTechID=@Tech WHERE WoNumber=@Key AND CreatedBy=@Key", ("@Key", key), ("@Tech", board.Access.UserId));
            detail = await api.Start(id, true); Assert.Equal("IN_PROGRESS", detail.Work.Status);
            await Assert.ThrowsAsync<InvalidOperationException>(() => api.SaveTask(id, detail.Tasks[1], "PASS", null));
            await Assert.ThrowsAsync<InvalidOperationException>(() => api.SaveTask(id, detail.Tasks[0], "FAIL", null));
            await Assert.ThrowsAsync<InvalidOperationException>(() => api.Complete(id, new("OK", "Isolated audit", null, 1, null)));
            detail = await api.SaveTask(id, detail.Tasks[0], "FAIL", "Isolated failure reason");
            Assert.Equal("FAIL", detail.Tasks[0].Result);
            detail = await api.SaveTask(id, detail.Tasks[0], "PASS", "Corrected during isolated audit");
            detail = await api.SaveTask(id, detail.Tasks[1], "PASS", null);
            Assert.All(detail.Tasks, t => Assert.Equal("PASS", t.Result));
            await api.Complete(id, new("OK", "Isolated PDA completion audit", null, 1, null));
            Assert.Equal("COMPLETED", (await api.Detail(id)).Work.Status);
            await Assert.ThrowsAsync<InvalidOperationException>(() => api.Start(id, true));
            output.WriteLine("Maintenance API: assignment enforcement, start, ordered checks, FAIL note requirement, incomplete completion rejection, PASS saves, completion and duplicate start rejection passed.");
        }
        finally
        {
            Exec(db, """
                DELETE A FROM dbo.SYS_AuditLog A JOIN dbo.MNT_WorkOrderTask T ON A.TargetID=CONVERT(varchar(30),T.WorkOrderTaskID)
                    JOIN dbo.MNT_WorkOrder W ON W.WorkOrderID=T.WorkOrderID
                    WHERE A.TargetEntity='MNT_WorkOrderTask' AND W.WoNumber=@Key AND W.CreatedBy=@Key;
                DELETE dbo.SYS_AuditLog WHERE TargetEntity='MNT_WorkOrder' AND TargetID=@Key;
                DELETE T FROM dbo.MNT_WorkOrderTask T JOIN dbo.MNT_WorkOrder W ON W.WorkOrderID=T.WorkOrderID WHERE W.WoNumber=@Key AND W.CreatedBy=@Key;
                DELETE dbo.MNT_WorkOrder WHERE WoNumber=@Key AND CreatedBy=@Key;
                """, ("@Key", key));
            using var logout = await http.PostAsync("/api/auth/logout", null);
        }
    }

    private static SqlConnection Open()
    {
        var cs = new SqlConnectionStringBuilder(Environment.GetEnvironmentVariable("AMES_PDA_LIVE_DB"));
        if (Environment.GetEnvironmentVariable("AMES_PDA_LIVE_API") == "http://192.168.0.132:5210")
        {
            Assert.Equal("AMES_DEV", cs.InitialCatalog);
            Assert.Contains(cs.DataSource, new[] { "192.168.0.132", "192.168.0.132,1433" });
        }
        else
        {
            Assert.Equal("localhost", cs.DataSource);
            Assert.StartsWith("AMES_TEST_PDA_", cs.InitialCatalog);
        }
        var db = new SqlConnection(cs.ConnectionString); db.Open(); return db;
    }
    private static string Key() => "VPA" + Guid.NewGuid().ToString("N")[..10];
    private static int LastTxn(SqlConnection db, string key, string type)
        => Convert.ToInt32(Scalar(db, "SELECT MAX(SparePartsTxnID) FROM dbo.MNT_SparePartsTxn WHERE SparePartNo=@Key AND MoveType=@Type", ("@Key", key), ("@Type", type)));
    private static SqlCommand Command(SqlConnection db, string sql, params (string Name, object? Value)[] args)
    {
        var cmd = new SqlCommand("SET QUOTED_IDENTIFIER ON; SET ANSI_NULLS ON; " + sql, db) { CommandTimeout = 15 };
        foreach (var (name, value) in args) cmd.Parameters.Add(name, SqlDbType.NVarChar, 80).Value = value ?? DBNull.Value;
        return cmd;
    }
    private static void Exec(SqlConnection db, string sql, params (string Name, object? Value)[] args)
    { using var cmd = Command(db, sql, args); cmd.ExecuteNonQuery(); }
    private static object? Scalar(SqlConnection db, string sql, params (string Name, object? Value)[] args)
    { using var cmd = Command(db, sql, args); return cmd.ExecuteScalar(); }
    private sealed class LiveStockFactAttribute : FactAttribute
    {
        public LiveStockFactAttribute()
        {
            if (!PdaLiveApiAuditTests.Enabled
                || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AMES_PDA_LIVE_DB")))
                Skip = "Requires explicit API and isolated test-data approval via AMES_PDA_LIVE_API and AMES_PDA_LIVE_DB.";
        }
    }
    private sealed class LiveStockTheoryAttribute : TheoryAttribute
    {
        public LiveStockTheoryAttribute()
        {
            if (!PdaLiveApiAuditTests.Enabled
                || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("AMES_PDA_LIVE_DB")))
                Skip = "Requires explicit API and isolated test-data approval.";
        }
    }
}
