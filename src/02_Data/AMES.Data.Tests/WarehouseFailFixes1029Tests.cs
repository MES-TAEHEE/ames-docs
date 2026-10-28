using System.Data;
using AMES.Data.Connection;
using AMES.Data.Repositories;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AMES.Data.Tests;

public sealed class WarehouseFailFixes1029Tests
{
    static string Read(string path, [System.Runtime.CompilerServices.CallerFilePath] string file = "")
        => File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "../../..", path)));

    [Fact]
    public void Replenishment_groups_locations_by_line_but_keeps_each_destination()
    {
        var rows = new[] {
            new WarehouseRepository.LineStockRow("LI01A1F1", "A", "A", 0, 5, false, LineId:"LINE-1"),
            new WarehouseRepository.LineStockRow("LI02A1F1", "B", "B", 1, 7, false, LineId:"LINE-1"),
            new WarehouseRepository.LineStockRow("LJ01A1F1", "A", "A", 0, 4, false, LineId:"LINE-2"),
            new WarehouseRepository.LineStockRow("LI03A1F1", "C", "C", 0, 9, true, LineId:"LINE-1"),
            new WarehouseRepository.LineStockRow("LI04A1F1", "D", "D", 3, 3, false, LineId:"LINE-1") };
        var groups = WarehouseRepository.PendingLineOrders(rows).ToList();
        Assert.Equal(2, groups.Count);
        Assert.Equal(new[] { "LI01A1F1", "LI02A1F1" }, groups.Single(x => x.Key == "LINE-1").Select(x => x.LineCode));
        var source = Read("src/02_Data/AMES.Data/Repositories/WarehouseRepository.LineStock.cs");
        Assert.Contains(".Value = item.LineCode;", source);
        Assert.Contains("GeneratePickSlipNo(conn, tx, line.Key)", source);
    }

    [Fact]
    public void Fifo_production_date_is_projected_and_returned_not_discarded()
    {
        var source = Read("src/02_Data/AMES.Data/Repositories/WarehouseRepository.LineStock.cs");
        Assert.Contains("COALESCE(CONVERT(date, L.ProducedAt), B.ProductionDate)", source);
        Assert.Contains("ProductionDate AS PROD_DATE", source);
        Assert.Contains("GetDateTime(rdr, \"PROD_DATE\")", source);
        Assert.Contains("x.ProductionDate?.ToString(\"yyyy-MM-dd\"", Read("src/04_Api/AMES.Api/Endpoints/WhEndpoints.cs"));
    }

    [Fact]
    public void Inventory_search_refreshes_quantities_and_does_not_expand_a_lot_to_all_part_locations()
    {
        var page = Read("src/05_Pda/AMES.Pda/Components/Pages/Wh/Wh03InventoryStatus.razor");
        var search = page.Split("private async Task LoadLocationInventory()")[1].Split("private async Task LoadInventoryLocations()")[0];
        Assert.DoesNotContain("if (_inventoryLocations.Count == 0)", search);
        Assert.Contains("_inventoryLocations = await Api.WhLocationsAsync", search);
        Assert.DoesNotContain("WhInventoryLocationsAsync", search);
        Assert.Contains("locationIds.Add(row.LocationId)", search);
        Assert.DoesNotContain("(!IsSpareParts || string.Equals(row.LocationId", page);
        Assert.DoesNotContain("WhLocationMapItemsAsync(location.LocationId, DateFrom, DateTo", page);
    }

    [Theory]
    [InlineData("ALL")]
    [InlineData("GENERAL")]
    [InlineData("FG")]
    public void Location_details_use_the_same_stock_scope_without_dates(string kind)
    {
        var sql = PdaStockScope.Inventory(PdaWarehouseSql.InventoryLocationItems, kind);
        Assert.DoesNotContain("@Date", sql);
        Assert.DoesNotContain("LIKE 'FG%'", sql);
        Assert.Contains(PdaStockScope.Predicate(kind), sql);
        Assert.Contains("Child.ParentLotNo=W.LotNo", sql);
        Assert.Contains("R.Status IN('Open','Inspecting')", sql);
    }

    [Fact]
    public void Defect_defaults_to_the_entire_lot_and_moves_have_a_distinct_color()
    {
        var release = Read("src/05_Pda/AMES.Pda/Components/Pages/Wh/Wh07PdaRelease.razor");
        var change = release.Split("private void OnOutgoingTypeChanged")[1].Split("private async Task LoadSlip")[0];
        Assert.Contains("_directQty = _directLot.Qty;", change);
        Assert.DoesNotContain("Math.Min(1", change);
        var transactions = Read("src/05_Pda/AMES.Pda/Components/Pages/Wh/Wh08TransactionHistory.razor");
        Assert.Contains("\"MOVE\" => \"move\"", transactions);
        Assert.Contains("wh08-kpi move", transactions);
        Assert.Contains("Contains(\"location change\")) return \"move\"", transactions);
        var css = Read("src/05_Pda/AMES.Pda/Components/Pages/Wh/Wh08TransactionHistory.razor.css");
        foreach (var selector in new[] { ".wh08-kpi.move", ".wh08-row-card.move", ".wh08-status.move" }) Assert.Contains(selector, css);
    }

    [Fact]
    public void Primary_location_scans_and_receipt_cancellations_get_specific_classification()
    {
        var api = Read("src/04_Api/AMES.Api/Endpoints/WhEndpoints.cs");
        var query = api.Split("private static LocationRow? QueryDboLocation")[1].Split("private static LocationRow? QueryLineLocation")[0];
        Assert.True(query.IndexOf("THROW 51423", StringComparison.Ordinal) < query.IndexOf("SELECT TOP (1)", StringComparison.Ordinal));
        Assert.Contains("ex is SqlException { Number: 51423 } ? StatusCodes.Status400BadRequest", api);
        var repo = Read("src/02_Data/AMES.Data/Repositories/WarehouseRepository.cs");
        Assert.Contains("T.ReasonCode='INBOUND_CANCEL' THEN 'CANCEL_RECEIPT'", repo);
        Assert.Contains("T.ReasonCode='INBOUND_CANCEL' THEN 'INBOUND'", repo);
        Assert.Contains("T.ReasonCode='INBOUND_CANCEL' THEN 'WH002'", repo);
        var history = Read("src/06_Web/AMES.Web/Components/Pages/Wh/LogHistory.razor");
        Assert.Contains("if (upper == \"CANCEL_RECEIPT\") return \"bad\";", history);
        Assert.Contains("or \"WH.LogHistory.Lbl.CancelInbound\"", history);
    }

    [Fact]
    public void Pick_slip_search_checks_all_lines_and_locations_not_just_the_displayed_first_part()
    {
        var source = Read("src/02_Data/AMES.Data/Repositories/WarehouseRepository.cs");
        var list = source.Split("public List<PickingSlipHeaderRow> ListPickingSlipHeaders")[1].Split("public List<PickingSlipLineRow>")[0];
        Assert.Contains("EXISTS(SELECT 1 FROM Lines Match WHERE Match.PICK_SLIPNO=G.PICK_SLIPNO", list);
        Assert.Contains("Match.ItemNo LIKE @Search OR Match.ItemName LIKE @Search OR Match.REQ_LOCATION LIKE @Search", list);
        Assert.DoesNotContain("OR L.ItemNo LIKE @Search", list);
    }

    const string InventoryFixture = """
        DECLARE @Stock table(LotNo varchar(50),PartNo varchar(50),PartName nvarchar(100),Qty decimal(18,3),
            ReceivedAt datetime2,LocationNo varchar(50),ParentLotNo varchar(50));
        DECLARE @Items table(ItemNo varchar(50),ItemName nvarchar(100),CarType varchar(20),DefaultUOM varchar(10),
            MinStock decimal(18,3),MaxStock decimal(18,3),ItemType varchar(20),ActiveFlag bit);
        DECLARE @Locations table(LocationID varchar(50),LocationName nvarchar(100),AreaCode varchar(20),ActiveFlag bit);
        DECLARE @Lots table(LotCode varchar(50),ItemNo varchar(50),LotID int);
        DECLARE @Returns table(LotNo varchar(50),Status varchar(20));
        INSERT @Items VALUES('PART-A',N'Part A',NULL,'EA',0,0,'RAW',1),('PART-B',N'Part B',NULL,'EA',0,0,'ASSY',1);
        INSERT @Locations VALUES('MA01A1F1',N'First','MAT_AREA',1),('MA02A1F1',N'Second','MAT_AREA',1);
        INSERT @Stock VALUES
            ('LOT-A','PART-A',N'Part A',2,'2020-01-01','MA01A1F1','PAL-1'),
            ('LOT-B','PART-A',N'Part A',3,'2026-10-01','MA01A1F1','PAL-1'),
            ('LOT-A-OTHER','PART-A',N'Part A',7,'2026-10-01','MA02A1F1',NULL),
            ('HOLD','PART-A',N'Part A',9,'2026-10-01','MA02A1F1',NULL),
            ('PAL-1','PART-A',N'Container total',5,'2026-10-01','MA01A1F1',NULL),
            ('UNASSIGNED','PART-A',N'Part A',3,'2026-10-01',NULL,NULL),
            ('ASSY-B','PART-B',N'Part B',4,'2026-10-01','MA02A1F1',NULL);
        INSERT @Returns VALUES('HOLD','Inspecting');
        INSERT @Lots VALUES('PRODUCED-NOT-IN-STOCK','PART-A',1);
        """;

    static string IsolateInventory(string sql)
    {
        foreach (var (table, variable) in new[] { ("WH_Inventory", "Stock"), ("MD_Item", "Items"),
                     ("MD_Location", "Locations"), ("tbl_Lot", "Lots"), ("FG_CustomerReturn", "Returns") })
            sql = sql.Replace("dbo." + table, "@" + variable);
        Assert.DoesNotContain("dbo.", sql, StringComparison.OrdinalIgnoreCase);
        return sql;
    }

    static SqlConnection OpenTestConnection()
    {
        var connection = Environment.GetEnvironmentVariable("AMES_TEST_CONN");
        Skip.If(string.IsNullOrWhiteSpace(connection), "Opt-in SQL table-variable tests; no application rows are written.");
        var conn = new SqlConnection(connection!); conn.Open(); return conn;
    }

    [SkippableTheory]
    [InlineData(null, 4, 19)]
    [InlineData("PART-A", 3, 15)]
    [InlineData("LOT-A", 1, 2)]
    [InlineData("MA01A1F1", 1, 5)]
    [InlineData("PRODUCED-NOT-IN-STOCK", 0, 0)]
    public void Sql_inventory_groups_by_part_and_location_and_exact_lot(string? search, int count, decimal qty)
    {
        using var conn = OpenTestConnection();
        using var cmd = new SqlCommand(InventoryFixture + IsolateInventory(PdaStockScope.Inventory(PdaWarehouseSql.Inventory, "ALL")), conn);
        cmd.Parameters.Add("@SearchText", SqlDbType.NVarChar, 80).Value = (object?)search ?? DBNull.Value;
        cmd.Parameters.Add("@AreaCode", SqlDbType.NVarChar, 20).Value = DBNull.Value;
        cmd.Parameters.Add("@StockDateFrom", SqlDbType.Date).Value = DBNull.Value;
        cmd.Parameters.Add("@StockDateTo", SqlDbType.Date).Value = DBNull.Value;
        using var r = cmd.ExecuteReader();
        var rows = 0; decimal sum = 0;
        while (r.Read()) { rows++; sum += r.GetDecimal(r.GetOrdinal("SUM_QTY")); }
        Assert.Equal(count, rows); Assert.Equal(qty, sum);
    }

    [SkippableTheory]
    [InlineData("ALL", "MA01A1F1", 2, 5)]
    [InlineData("GENERAL", "MA01A1F1", 2, 5)]
    [InlineData("FG", "MA01A1F1", 0, 0)]
    [InlineData("ALL", "MA02A1F1", 2, 11)]
    [InlineData("GENERAL", "MA02A1F1", 1, 7)]
    [InlineData("FG", "MA02A1F1", 1, 4)]
    public void Sql_location_details_keep_old_stock_and_exclude_container_totals_and_inspection(string scope, string location, int count, decimal qty)
    {
        using var conn = OpenTestConnection();
        using var cmd = new SqlCommand(InventoryFixture + IsolateInventory(PdaStockScope.Inventory(PdaWarehouseSql.InventoryLocationItems, scope)), conn);
        cmd.Parameters.Add("@LocationID", SqlDbType.NVarChar, 40).Value = location;
        using var r = cmd.ExecuteReader(); var rows = 0; decimal sum = 0;
        while (r.Read()) { rows++; sum += r.GetDecimal(r.GetOrdinal("QTY")); }
        Assert.Equal(count, rows); Assert.Equal(qty, sum);
    }

    [SkippableTheory]
    [InlineData("LOT-A", "LOT", "LOT-A")]
    [InlineData("PRODUCED-NOT-IN-STOCK", "LOT", "PRODUCED-NOT-IN-STOCK")]
    [InlineData("PART-A", "PART", "PART-A")]
    [InlineData("MA01A1F1", "LOCATION", "MA01A1F1")]
    public void Sql_lookup_keeps_lot_identity(string scan, string kind, string text)
    {
        using var conn = OpenTestConnection();
        using var cmd = new SqlCommand(InventoryFixture + IsolateInventory(PdaWarehouseSql.InventoryLookup), conn);
        cmd.Parameters.Add("@ScanText", SqlDbType.NVarChar, 80).Value = scan;
        using var r = cmd.ExecuteReader(); Assert.True(r.Read());
        Assert.Equal(kind, r["SEARCH_KIND"]); Assert.Equal(text, r["SEARCH_TEXT"]);
    }

    const string TransactionFixture = """
        DECLARE @Transactions table(TransactionID bigint,TransactionTime datetime2,LotNo varchar(50),PartNo varchar(50),
            TransactionType varchar(20),QtyBefore decimal(18,3),QtyChange decimal(18,3),QtyAfter decimal(18,3),LocationNo varchar(50),
            OperatorID varchar(20),ReasonCode varchar(50),Note nvarchar(500),SourceType varchar(30),SourceID int);
        DECLARE @Items table(ItemNo varchar(50),ItemName nvarchar(100),DefaultUOM varchar(10));
        DECLARE @Loads table(DeliveryID int,ShipmentID int);
        DECLARE @Deliveries table(DeliveryID int,DeliveryNo varchar(50),ShippedAt datetime2);
        DECLARE @Shipments table(ShipmentID int,PalletNo varchar(50));
        DECLARE @Stock table(LotNo varchar(50),PalletNo varchar(50),ParentLotNo varchar(50));
        INSERT @Items VALUES('PART-A',N'Part A','EA');
        INSERT @Transactions VALUES
            (1,'2026-10-01','LOT-OUT-A','PART-A','OUT',2,-2,0,'FG01','TEST',NULL,NULL,'FG_OUTBOUND',1),
            (2,'2026-10-01','LOT-OUT-B','PART-A','OUT',3,-3,0,'FG01','TEST',NULL,NULL,'FG_OUTBOUND',2),
            (3,'2026-10-02','LOT-MOVE','PART-A','MOVE',1,0,1,'MA01','TEST',NULL,NULL,NULL,NULL);
        INSERT @Loads VALUES(10,1),(10,2);
        INSERT @Deliveries VALUES(10,'DCR-TEST','2026-10-01');
        INSERT @Shipments VALUES(1,'PAL-HIST-A'),(2,'PAL-HIST-B');
        INSERT @Stock VALUES('LOT-OUT-B','PAL-CURRENT','PARENT-OUT'),('LOT-MOVE','PAL-MOVE','PARENT-MOVE');
        """;

    [SkippableTheory]
    [InlineData("PAL-HIST-A", null, 5)]
    [InlineData("PARENT-OUT", null, 5)]
    [InlineData("PAL-CURRENT", null, 5)]
    [InlineData("PAL-MOVE", null, 1)]
    [InlineData("PARENT-MOVE", null, 1)]
    [InlineData("PAL-HIST-A", 10, 2)]
    public void Sql_pallet_search_preserves_whole_dcr_totals_and_supports_shipped_stock(string search, int? delivery, decimal qty)
    {
        var sql = PdaTransactionRepository.PageSql;
        foreach (var (table, variable) in new[] { ("WH_InventoryTransaction", "Transactions"), ("WH_Inventory", "Stock"),
                     ("MD_Item", "Items"), ("FG_DeliveryLoad", "Loads"), ("FG_Delivery", "Deliveries"), ("FG_Shipment", "Shipments") })
            sql = sql.Replace("dbo." + table, "@" + variable);
        Assert.DoesNotContain("dbo.", sql, StringComparison.OrdinalIgnoreCase);
        using var conn = OpenTestConnection();
        using var cmd = new SqlCommand(TransactionFixture + sql, conn);
        cmd.Parameters.Add("@From", SqlDbType.Date).Value = new DateTime(2026, 1, 1);
        cmd.Parameters.Add("@To", SqlDbType.Date).Value = new DateTime(2026, 12, 31);
        cmd.Parameters.Add("@Take", SqlDbType.Int).Value = 101;
        cmd.Parameters.Add("@Search", SqlDbType.NVarChar, 122).Value = "%" + search + "%";
        cmd.Parameters.Add("@DeliveryID", SqlDbType.Int).Value = (object?)delivery ?? DBNull.Value;
        foreach (var name in new[] { "@Worker", "@Reason", "@Direction", "@PartNo" })
            cmd.Parameters.Add(name, SqlDbType.NVarChar, 120).Value = DBNull.Value;
        cmd.Parameters.Add("@BeforeAt", SqlDbType.DateTime2).Value = DBNull.Value;
        cmd.Parameters.Add("@BeforeID", SqlDbType.BigInt).Value = DBNull.Value;
        using var r = cmd.ExecuteReader(); Assert.True(r.Read());
        Assert.Equal(qty, r.GetDecimal(5)); Assert.False(r.Read());
    }

    [SkippableFact]
    public void Actual_read_queries_compile_without_saving_any_data()
    {
        using var conn = OpenTestConnection();
        var factory = new AmesConnectionFactory(Environment.GetEnvironmentVariable("AMES_TEST_CONN")!);
        var repo = new WarehouseRepository(factory);
        const string missing = "WH-REGRESSION-NO-SUCH-SLIP";
        Assert.Empty(repo.ListPickingSlipFifoLots(missing));
        Assert.Empty(repo.ListPickingSlipHeaders(missing));
        Assert.Empty(repo.ListOperationLogs(missing, "INBOUND"));
        repo.ListLineStock();
        new PdaTransactionRepository(factory).ReadPage(missing, new(2026,1,1), new(2026,12,31));
    }

    [SkippableTheory]
    [InlineData("PRIMARY", 51423, false)]
    [InlineData("DETAIL", null, true)]
    [InlineData("UNKNOWN", null, false)]
    public void Sql_primary_locations_are_rejected_before_stock_lookup(string location, int? error, bool found)
    {
        var source = Read("src/04_Api/AMES.Api/Endpoints/WhEndpoints.cs").Split("private static LocationRow? QueryDboLocation")[1]
            .Split("private static LocationRow? QueryLineLocation")[0];
        var sql = System.Text.RegularExpressions.Regex.Match(source, "new SqlCommand\\(\"\"\"(?<sql>[\\s\\S]*?)\"\"\", conn\\)").Groups["sql"].Value;
        Assert.NotEmpty(sql);
        sql = sql.Replace("dbo.MD_Location", "@Locations").Replace("dbo.MD_CodeItem", "@Codes").Replace("dbo.WH_Inventory", "@Stock");
        Assert.DoesNotContain("dbo.", sql, StringComparison.OrdinalIgnoreCase);
        const string fixture = """
            DECLARE @Locations table(LocationID varchar(50),LocationName nvarchar(100),ZoneCode varchar(20),WhCode varchar(20),
                AreaCode varchar(20),Aisle varchar(20),Bay varchar(20),Slot varchar(20),PlantCode varchar(20),LocationType varchar(20),
                Capacity decimal(18,3),ActiveFlag bit,LocationLevel int);
            DECLARE @Codes table(GroupCode varchar(50),CodeValue varchar(50),CodeName nvarchar(100),ParentCodeID int,CodeID int);
            DECLARE @Stock table(LotNo varchar(50),LocationNo varchar(50),Qty decimal(18,3));
            INSERT @Locations(LocationID,ActiveFlag,LocationLevel) VALUES('PRIMARY',1,1),('DETAIL',1,2);
            """;
        using var conn = OpenTestConnection(); using var cmd = new SqlCommand(fixture + sql, conn);
        cmd.Parameters.Add("@LocationID", SqlDbType.NVarChar, 50).Value = location;
        if (error.HasValue) Assert.Equal(error, Assert.Throws<SqlException>(() => cmd.ExecuteReader()).Number);
        else { using var r = cmd.ExecuteReader(); Assert.Equal(found, r.Read()); }
    }

    [SkippableTheory]
    [InlineData("[]", 1)]
    [InlineData("[{\"PickSlipId\":1,\"Qty\":25}]", 2)]
    [InlineData("[{\"PickSlipId\":1,\"Qty\":25},{\"PickSlipId\":2,\"Qty\":30}]", null)]
    public void Sql_batch_moves_to_the_next_destination_only_after_the_previous_request_is_covered(string allocations, int? expected)
    {
        var source = Read("src/04_Api/AMES.Api/Endpoints/WhEndpoints.cs");
        var join = source.Split("INNER JOIN dbo.WH_PickSlip RS WITH (UPDLOCK, HOLDLOCK)")[1].Split("WHERE W.LotNo=@LotNo")[0];
        const string fixture = """
            DECLARE @Requests table(PickSlipID int,ItemNo varchar(20),PickSlipNo varchar(40),Status varchar(20),
                DemandQty decimal(18,3),PickedQty decimal(18,3),CreatedBy varchar(20));
            DECLARE @Stock table(PartNo varchar(20));
            INSERT @Stock VALUES('PART-A');
            INSERT @Requests VALUES(1,'PART-A','PK01','Open',20,0,'WH-AUTO'),(2,'PART-A','PK01','Open',30,0,'WH-AUTO');
            """;
        using var conn = OpenTestConnection();
        using var cmd = new SqlCommand(fixture + "SELECT TOP(1) RS.PickSlipID FROM @Stock W JOIN @Requests RS " + join + " ORDER BY RS.PickSlipID;", conn);
        Assert.DoesNotContain("dbo.", cmd.CommandText, StringComparison.OrdinalIgnoreCase);
        cmd.Parameters.Add("@Slip", SqlDbType.NVarChar, 40).Value = "PK01";
        cmd.Parameters.Add("@BatchAllocations", SqlDbType.NVarChar, -1).Value = allocations;
        Assert.Equal((object?)expected, cmd.ExecuteScalar());
    }

    [SkippableTheory]
    [InlineData(0)]
    [InlineData(5)]
    public void Sql_pick_counts_do_not_credit_one_location_with_another_locations_stock(int second)
    {
        var source = Read("src/04_Api/AMES.Api/Endpoints/WhEndpoints.cs");
        var start = source.IndexOf(";WITH PickCounts AS", StringComparison.Ordinal);
        var sql = source[start..source.IndexOf("\"\"\"", start, StringComparison.Ordinal)];
        var update = sql.IndexOf("UPDATE RS", StringComparison.Ordinal);
        sql = sql[..update] + "SELECT RS.PickSlipID,Q.PickedQty " + sql[sql.IndexOf("FROM dbo.WH_PickSlip RS", update, StringComparison.Ordinal)..];
        sql = sql.Replace("dbo.WH_PickSlip", "@Requests").Replace("dbo.WH_InventoryTransaction", "@Transactions");
        Assert.DoesNotContain("dbo.", sql, StringComparison.OrdinalIgnoreCase);
        const string fixture = """
            DECLARE @Requests table(PickSlipID int,ItemNo varchar(20),PickSlipNo varchar(40),Status varchar(20),
                DemandQty decimal(18,3),CreatedBy varchar(20),ReqLocation varchar(50));
            DECLARE @Transactions table(SourceID int,LotNo varchar(50),QtyChange decimal(18,3),TransactionType varchar(10),SourceType varchar(30));
            INSERT @Requests VALUES(1,'PART-A','PK01','Open',20,'WH-AUTO','LI01A1F1'),(2,'PART-A','PK01','Open',30,'WH-AUTO','LI02A1F1');
            INSERT @Transactions VALUES(1,'BOX-1',-25,'OUT','PICK_SLIP');
            IF @Second>0 INSERT @Transactions VALUES(2,'BOX-2',-@Second,'OUT','PICK_SLIP');
            """;
        using var conn = OpenTestConnection(); using var cmd = new SqlCommand(fixture + sql, conn);
        cmd.Parameters.Add("@Slip", SqlDbType.NVarChar, 40).Value = "PK01";
        cmd.Parameters.Add("@Second", SqlDbType.Int).Value = second;
        using var r = cmd.ExecuteReader(); var counts = new Dictionary<int, decimal>();
        while (r.Read()) counts.Add(r.GetInt32(0), r.GetDecimal(1));
        Assert.Equal(25, counts[1]); Assert.Equal(second, counts[2]);
    }
}
