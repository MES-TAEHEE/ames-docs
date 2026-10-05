#:project ../src/04_Api/AMES.Api/AMES.Api.csproj
#:property PublishAot=false

// dotnet run --file tools/check_wh_line_replenishment.cs
// An isolated, randomly named LocalDB database only. Never reads application connection settings.
using System.Reflection;
using System.Text.RegularExpressions;
using AMES.Api.Endpoints;
using AMES.Api.Workers;
using AMES.Data.Connection;
using AMES.Data.Repositories;
using Microsoft.Data.SqlClient;

static void Check(bool pass, string message) { if (!pass) throw new Exception(message); }
static object Invoke(string method, params object?[] args) => typeof(WhEndpoints)
    .GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args)!;

Check(LineReplenishmentWorker.Interval == TimeSpan.FromMinutes(10), "10-minute interval");
var rounding = new WarehouseRepository.LineStockRow("I1", "ITEM", "Item", 45, 100, false);
Check(rounding.RequiredQty == 55 && rounding.State == "Pending", "Request the exact 55 EA shortage, not three boxes");
Check((rounding with { HasOpenOrder = true }).State == "Requested", "Do not repeat an open request");
Check((rounding with { Qty = 100 }).State == "Sufficient", "No request at safety threshold");
Check((rounding with { Qty = 45.5m }).RequiredQty == 55, "Request whole EA units");

var database = "AMES_LineTest_" + Guid.NewGuid().ToString("N");
const string local = "Server=(localdb)\\MSSQLLocalDB;Integrated Security=true;Encrypt=false;Connect Timeout=30;";
using var admin = new SqlConnection(local + "Database=master");
admin.Open();
using (var create = new SqlCommand($"CREATE DATABASE [{database}] COLLATE SQL_Latin1_General_CP1_CI_AS;", admin)) create.ExecuteNonQuery();
var factory = new AmesConnectionFactory(local + "Database=" + database);
void Exec(string sql)
{
    using var connection = factory.OpenConnection();
    using var command = new SqlCommand(sql, connection);
    command.ExecuteNonQuery();
}
decimal Scalar(string sql)
{
    using var connection = factory.OpenConnection();
    using var command = new SqlCommand(sql, connection);
    return Convert.ToDecimal(command.ExecuteScalar());
}
try
{
    Exec("""
        CREATE TABLE MD_Item(ItemNo varchar(20) COLLATE Korean_Wansung_CI_AS PRIMARY KEY,ItemName nvarchar(200) COLLATE Korean_Wansung_CI_AS,
            SafetyStock decimal(14,4),DefaultUOM varchar(10),ActiveFlag bit);
        CREATE TABLE MD_PackagingSpec(ItemID varchar(20) COLLATE Korean_Wansung_CI_AS,QtyPerInner int,ActiveFlag bit);
        CREATE TABLE MD_Location(LocationID varchar(50) COLLATE Korean_Wansung_CI_AS,AreaCode varchar(20),LocationName nvarchar(100),ZoneCode varchar(20));
        CREATE TABLE MD_Line(LineID varchar(20) COLLATE Korean_Wansung_CI_AS,LineName nvarchar(100) COLLATE Korean_Wansung_CI_AS,
            LotPrefix char(2) COLLATE Korean_Wansung_CI_AS,Status varchar(20));
        CREATE UNIQUE INDEX UX_MD_Line_LotPrefix ON MD_Line(LotPrefix) WHERE LotPrefix IS NOT NULL;
        CREATE TABLE MD_CodeItem(GroupCode varchar(30),CodeValue varchar(30),UseFlag bit);
        CREATE TABLE WH_Inventory(LotNo nvarchar(50) PRIMARY KEY,UnitType varchar(10),ParentLotNo nvarchar(50),PartNo varchar(50),PartName nvarchar(200),
            LocationNo varchar(50),Qty decimal(18,3),ReceivedAt datetime2 DEFAULT SYSDATETIME(),CreatedAt datetime2 DEFAULT SYSDATETIME(),UpdatedAt datetime2,
            CaseNo nvarchar(50),InvoiceNo nvarchar(50),DeliveryNoteNo nvarchar(30));
        CREATE TABLE WH_PickSlip(PickSlipID int IDENTITY PRIMARY KEY,PickSlipNo nvarchar(40) COLLATE Korean_Wansung_CI_AS,
            ReqLocation nvarchar(40) COLLATE Korean_Wansung_CI_AS,ReqSeqNo int,ReqUserId nvarchar(80),ItemNo varchar(20) COLLATE Korean_Wansung_CI_AS,
            DemandQty decimal(14,3),PickedQty decimal(14,3),RequiredAt datetime2,Priority tinyint,Status varchar(20),CreatedBy varchar(20),CreatedTS datetime2,
            ModifiedBy varchar(20),ModifiedTS datetime2,CloseDate datetime2,CloseUserId nvarchar(80),PrintDate datetime2);
        CREATE TABLE WH_InventoryTransaction(TransactionID int IDENTITY PRIMARY KEY,TransactionTime datetime2,TransactionType varchar(10),PartNo varchar(50),
            LocationNo varchar(50),LotNo nvarchar(50),QtyBefore decimal(18,3),QtyChange decimal(18,3),QtyAfter decimal(18,3),ReasonCode varchar(30),
            SourceType varchar(20),SourceID int,OperatorID nvarchar(80),ApproverID nvarchar(80),Note nvarchar(500),CreatedBy varchar(20),CreatedTS datetime2);
        INSERT MD_Item VALUES ('ITEM',N'Material',100,'EA',1),('MISSING',N'Unconfigured',100,'EA',1),('CONFLICT',N'Conflict',100,'EA',1);
        INSERT MD_PackagingSpec VALUES ('ITEM',20,1),('CONFLICT',10,1),('CONFLICT',20,1);
        INSERT MD_CodeItem VALUES ('INV_ADJUST_REASON','COUNT',1);
        INSERT MD_Location VALUES ('RACK','MAT_AREA',N'Rack','A'),('FG','FG_AREA',N'FG','A');
        INSERT MD_Line VALUES ('LINE-INJ-01',N'Injection 1','I1','ACTIVE'),('LINE-IMG-01',N'Wrapping 1','W1','ACTIVE'),
            ('LINE-PNT-01',N'Painting 1','P1','ACTIVE'),('LINE-INJ-02',N'Injection 2','I2','ACTIVE'),
            ('LINE-INJ-03',N'Injection 3','I3','ACTIVE'),('LINE-OLD',N'Inactive line','X1','INACTIVE'),
            ('LINE-NUMERIC',N'Registered numeric prefix','07','ACTIVE'),('LINE-NONE',N'No prefix',NULL,'ACTIVE');
        INSERT WH_Inventory(LotNo,UnitType,ParentLotNo,PartNo,PartName,LocationNo,Qty) VALUES
            ('LINE-01','BOX',NULL,'ITEM',N'Material','I1',0),('LINE-02','PART',NULL,'ITEM',N'Material','W1',80),
            ('CONTAINER','CASE',NULL,'ITEM',N'Material','P1',999),('CHILD','BOX','CONTAINER','ITEM',N'Material','P1',90),
            ('UNKNOWN','BOX',NULL,'MISSING',N'Unconfigured','I2',0),('AMBIGUOUS','BOX',NULL,'CONFLICT',N'Conflict','I3',0),
            ('NOT-A-LINE','PART',NULL,'ITEM',N'Material','99',0),('FULL-LINE-ID','PART',NULL,'ITEM',N'Material','LINE-INJ-01',0),
            ('INACTIVE-LINE','BOX',NULL,'ITEM',N'Material','X1',20),
            ('FG-BOX','BOX',NULL,'ITEM',N'FG','FG',20),('BOX-1','BOX',NULL,'ITEM',N'Material','RACK',20),
            ('BOX-2','BOX',NULL,'ITEM',N'Material','RACK',20),('BOX-3','BOX',NULL,'ITEM',N'Material','RACK',20),
            ('BOX-4','BOX',NULL,'ITEM',N'Material','RACK',20),('BOX-5','BOX',NULL,'ITEM',N'Material','RACK',20);
        UPDATE WH_Inventory SET ReceivedAt='20260101',CreatedAt='20260101';
        """);
    var repository = new WarehouseRepository(factory);
    using (var connection = factory.OpenConnection())
    {
        foreach (var code in new[] { "I1", "W1", "P1", "07" })
            Check(WarehouseRepository.ResolveLineLocation(connection, code) == code, "Resolve registered LotPrefix " + code);
        foreach (var code in new string?[] { null, "", "01", "99", "ZZ", "LINE-INJ-01", "X1" })
            Check(WarehouseRepository.ResolveLineLocation(connection, code) is null, "Reject unregistered/inactive line " + code);
    }
    var stock = repository.ListLineStock();
    Check(stock.Count == 5 && stock.All(x => x.LineCode is "I1" or "W1" or "P1" or "I2" or "I3"), "Only registered active LotPrefix locations");
    Check(stock.Single(x => x.LineCode == "P1").Qty == 90, "Exclude container aggregate");
    Check(stock.Single(x => x.PartNo == "CONFLICT").State == "Pending", "Packing settings do not affect EA replenishment");
    var results = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => Task.Run(repository.GenerateLinePickingOrders)));
    Check(results.Sum(x => x.Orders) == 5 && Scalar("SELECT COUNT(*) FROM WH_PickSlip") == 5, "Concurrent ticks generate each line once, including items without packing settings");
    Check(repository.GenerateLinePickingOrders().Orders == 0, "Repeated tick does not duplicate");
    var header = repository.ListPickingSlipHeaders().Single(x => x.ReqLocation == "I1");
    Check(Regex.IsMatch(header.PickSlipNo, @"^PA-I1-\d{8}-\d{3}$"), "Readable automatic Pick Slip number");
    Check(header.ReqBoxQty == 100 && header.QuantityUnit == "EA" && repository.ListPickingSlipLines(header.PickSlipNo).Count == 1, "Web reads EA order and detail");
    var apiLines = (List<WhEndpoints.ReleasePickLineRow>)Invoke("QueryReleasePickLines", factory, header.PickSlipNo);
    Check(apiLines.Single().RequestBoxQty == 100 && apiLines.Single().QuantityUnit == "EA", "PDA receives EA demand and explicit unit");
    var fifo = (List<WhEndpoints.ReleaseFifoLotRow>)Invoke("QueryReleaseFifoLots", factory, header.PickSlipNo);
    Check(fifo.Count == 5 && fifo.All(x => x.LotNo.StartsWith("BOX-")), "FIFO excludes FG, line stock and containers");
    Exec("UPDATE WH_Inventory SET LocationNo='RACK-2' WHERE LotNo IN ('BOX-3','BOX-4'); UPDATE WH_Inventory SET LocationNo=NULL WHERE LotNo='BOX-5';");
    var printSources = repository.ListPickingSlipFifoLots(header.PickSlipNo);
    Check(printSources.Count(x => x.LocationNo == "RACK") == 2
        && printSources.Count(x => x.LocationNo == "RACK-2") == 2
        && printSources.Count(x => x.LocationNo is null) == 1, "Print sources retain multiple locations and unassigned boxes");
    var pdaSources = (List<WhEndpoints.ReleaseFifoLotRow>)Invoke("QueryReleaseFifoLots", factory, header.PickSlipNo);
    Check(printSources.Select(x => (x.LotNo, x.LocationNo)).SequenceEqual(pdaSources.Select(x => (x.LotNo, x.LocationNo))),
        "Print and PDA use the same FIFO source list");
    Exec("UPDATE WH_Inventory SET Qty=0 WHERE LotNo='BOX-5';");
    Check(repository.ListPickingSlipFifoLots(header.PickSlipNo).Count == 4, "Insufficient stock does not invent a fifth source");
    Exec("UPDATE WH_Inventory SET LocationNo='RACK',Qty=20 WHERE LotNo IN ('BOX-3','BOX-4','BOX-5');");
    Exec("UPDATE WH_PickSlip SET DemandQty=80 WHERE ReqLocation='I1'; UPDATE WH_Inventory SET Qty=30 WHERE LotNo IN ('BOX-1','BOX-2','BOX-3');");
    Check(repository.ListPickingSlipFifoLots(header.PickSlipNo).Select(x => x.LotNo).SequenceEqual(new[] { "BOX-1", "BOX-2", "BOX-3" }),
        "80 EA demand recommends three 30 EA boxes, including only the final excess box");
    var ninety = Enumerable.Range(1, 3).Select(i => new WhEndpoints.ReleasePickInput("BOX-" + i, 30)).ToList();
    Check(!((WhEndpoints.ReleaseCompleteResult)Invoke("ExecuteReleaseBatch", factory, header.PickSlipNo, ninety.Take(2).ToList(), "LINE", "TEST", "TEST", false)).Success,
        "Reject multiple boxes in one release");
    foreach (var lot in ninety.Take(2))
    {
        var partial = (WhEndpoints.ReleaseCompleteResult)Invoke("ExecuteReleaseBatch", factory, header.PickSlipNo, new[] { lot }, "LINE", "TEST", "TEST", false);
        Check(partial.Success && partial.Message.Contains("Partial"), "Release one box and keep the order Partial: " + partial.Message);
    }
    var extraComplete = (WhEndpoints.ReleaseCompleteResult)Invoke("ExecuteReleaseBatch", factory, header.PickSlipNo, new[] { ninety[2] }, "LINE", "TEST", "TEST", false);
    Check(extraComplete.Success && extraComplete.Message.Contains("10 EA above") && extraComplete.Message.Contains("completed"),
        "Whole final box completes the order with excess notification: " + extraComplete.Message);
    Check(Scalar("SELECT PickedQty FROM WH_PickSlip WHERE ReqLocation='I1'") == 90, "Persist actual 90 EA, not three boxes");
    Check(Scalar("SELECT SUM(Qty) FROM WH_Inventory WHERE LocationNo='I1'") == 90, "Transfer all 90 EA to line");
    // Reset isolated fixture only, then run exact-demand, rollback and adjustment checks below.
    Exec("DELETE WH_InventoryTransaction; UPDATE WH_Inventory SET LocationNo='RACK',Qty=20 WHERE LotNo LIKE 'BOX-%'; UPDATE WH_PickSlip SET DemandQty=100,PickedQty=0,Status='Open',CloseDate=NULL,CloseUserId=NULL WHERE ReqLocation='I1';");
    using (var connection = factory.OpenConnection())
    {
        var scan = (WhEndpoints.ReleaseLotRow)Invoke("ValidateReleaseLot", connection, null, header.PickSlipNo, "BOX-1");
        Check(scan.IsValid, "Scan warehouse box: " + scan.Message);
        var reject = (WhEndpoints.ReleaseLotRow)Invoke("ValidateReleaseLot", connection, null, header.PickSlipNo, "LINE-02");
        Check(!reject.IsValid, "Cannot replenish from another production line");
    }
    var lots = Enumerable.Range(1, 5).Select(i => new WhEndpoints.ReleasePickInput("BOX-" + i, 20)).ToList();
    var staleBoxes = lots.Select(x => x with { Qty = 19 }).ToList();
    Exec("UPDATE WH_PickSlip SET ReqLocation='99' WHERE ReqLocation='I1';");
    Check(!((WhEndpoints.ReleaseCompleteResult)Invoke("ExecuteReleaseBatch", factory, header.PickSlipNo, new[] { lots[0] }, "LINE", "TEST", "TEST", false)).Success,
        "Unregistered two-digit destination cannot receive line stock");
    Exec("UPDATE WH_PickSlip SET ReqLocation='I1' WHERE ReqLocation='99';");
    Check(!((WhEndpoints.ReleaseCompleteResult)Invoke("ExecuteReleaseBatch", factory, header.PickSlipNo, new[] { staleBoxes[0] }, "LINE", "TEST", "TEST", false)).Success,
        "Reject stale box quantity before line transfer");
    var rollback = (WhEndpoints.ReleaseCompleteResult)Invoke("ExecuteReleaseBatch", factory, header.PickSlipNo, new[] { lots[0] }, "LINE", "TEST", "TEST", true);
    Check(!rollback.Success && Scalar("SELECT COUNT(*) FROM WH_InventoryTransaction") == 0, "Failed completion rolls back all changes");
    WhEndpoints.ReleaseCompleteResult complete = null!;
    foreach (var lot in lots)
    {
        complete = (WhEndpoints.ReleaseCompleteResult)Invoke("ExecuteReleaseBatch", factory, header.PickSlipNo, new[] { lot }, "LINE", "TEST", "TEST", false);
        Check(complete.Success, "Release one replenishment box: " + complete.Message);
    }
    Check(complete.Message.Contains("completed"), "Final box completes replenishment");
    Check(repository.ListPickingSlipFifoLots(header.PickSlipNo).Count == 0, "Completed order has no pending pickup locations");
    Check(Scalar("SELECT SUM(Qty) FROM WH_Inventory WHERE LocationNo='I1'") == 100, "Move boxes to registered LotPrefix without consuming stock");
    Check(Scalar("SELECT COUNT(*) FROM WH_InventoryTransaction") == 10, "Source OUT and line IN history");
    Check(repository.GenerateLinePickingOrders().Orders == 0, "Filled line does not regenerate");
    Check(!((WhEndpoints.ReleaseCompleteResult)Invoke("ExecuteReleaseBatch", factory, header.PickSlipNo, new[] { lots[0] }, "LINE", "TEST", "TEST", false)).Success,
        "Duplicate completion rejected");

    var schema = File.ReadAllText(Path.Combine("dist", "AMES_Schema.sql"));
    foreach (var name in new[] { "WH_PDA_ADJUST_SCAN_STOCK", "WH_PDA_ADJUST_SAVE_QTY" })
    {
        var procedure = Regex.Match(schema, @"CREATE\s+PROCEDURE\s+dbo\." + name + @"\b[\s\S]*?(?=\r?\nGO\s*\r?\n)", RegexOptions.IgnoreCase).Value;
        Check(procedure.Length > 0, "Find canonical adjustment procedure " + name);
        Exec(procedure);
    }
    var stale = new WhEndpoints.AdjustSaveReq("LOCAL", "BOX-1", -5, "COUNT", null, ExpectedQty: 21, ExpectedLocation: "I1");
    Check(!((WhEndpoints.InboundReceiveResult)Invoke("ExecuteAdjustSave", factory, stale, "TEST", false)).Success, "Reject stale quantity");
    var moved = stale with { ExpectedQty = 20, ExpectedLocation = "W1" };
    Check(!((WhEndpoints.InboundReceiveResult)Invoke("ExecuteAdjustSave", factory, moved, "TEST", false)).Success, "Reject stale location");
    var failedAdjust = (WhEndpoints.InboundReceiveResult)Invoke("ExecuteAdjustSave", factory, stale with { ExpectedQty = 20 }, "TEST", true);
    Check(!failedAdjust.Success && Scalar("SELECT Qty FROM WH_Inventory WHERE LotNo='BOX-1'") == 20,
        "Adjustment failure rolls back both quantity and transaction history");
    var adjusted = (WhEndpoints.InboundReceiveResult)Invoke("ExecuteAdjustSave", factory, stale with { ExpectedQty = 20 }, "TEST", false);
    Check(adjusted.Success && adjusted.Row?.Qty == 15, "Save line adjustment with canonical procedure: " + adjusted.Message);
    Check(repository.GenerateLinePickingOrders().Orders == 1, "A new shortage after completed replenishment generates next order");
    Console.WriteLine("PASS: EA demand, readable numbering, grouping, concurrency, FIFO, single-box partial release, final-box excess, line transfer, rollback, history and adjustment concurrency.");
}
finally
{
    SqlConnection.ClearAllPools();
    // The only deletion target is the GUID-named LocalDB database created above.
    Check(Regex.IsMatch(database, "^AMES_LineTest_[a-f0-9]{32}$"), "Validate disposable database name");
    using var drop = new SqlCommand($"ALTER DATABASE [{database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{database}];", admin);
    drop.ExecuteNonQuery();
}
