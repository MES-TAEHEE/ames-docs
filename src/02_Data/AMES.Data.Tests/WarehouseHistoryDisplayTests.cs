using AMES.Data.Repositories;
using Xunit;
using HistoryPage = AMES.Web.Components.Pages.Wh.LogHistory;

namespace AMES.Data.Tests;

public sealed class WarehouseHistoryDisplayTests
{
    [Fact]
    public void Receipt_cancellation_is_in_all_events_but_not_in_receipt_or_adjustment_counts()
    {
        WarehouseRepository.OperationLogRow[] rows =
        [
            Row("RECEIVE"), Row("RECEIVE"), Row("CANCEL_RECEIPT"), Row("RELEASE_PICK"),
            Row("ADJUST_SAVE"), Row("ADJUST_SAVE", "Defective quantity"), Row("MOVE_LOCATION"),
        ];

        Assert.Equal(7, rows.Length);
        Assert.Equal(2, HistoryPage.CountEvents(rows, "INBOUND"));
        Assert.Equal(0, HistoryPage.CountEvents(rows, "OUTBOUND"));
        Assert.Equal(1, HistoryPage.CountEvents(rows, "RELEASE"));
        Assert.Equal(2, HistoryPage.CountEvents(rows, "ADJUSTMENT"));
        Assert.Equal(0, HistoryPage.CountEvents([Row("MOVE_LOCATION")], "INBOUND"));
    }

    [Theory]
    [InlineData("CANCEL_RECEIPT", null, "WH.LogHistory.Lbl.CancelInbound")]
    [InlineData("STATUS_CHANGE", "RECEIVED -> RECEIPT_CANCELLED", "WH.LogHistory.Lbl.InboundCancelled")]
    public void Both_cancellation_labels_are_excluded_from_KPIs(string type, string? message, string label)
    {
        var row = Row(type, message);
        Assert.Equal(label, HistoryPage.DisplayEvent(row));
        Assert.Equal("bad", HistoryPage.EventClass(row));
        foreach (var kpi in new[] { "INBOUND", "OUTBOUND", "ADJUSTMENT" })
            Assert.Equal(0, HistoryPage.CountEvents([row], kpi));
    }

    [Theory]
    [InlineData("RECEIVED")]
    [InlineData("STORED")]
    [InlineData("RETURN_RECEIVED")]
    public void Existing_receipt_status_labels_are_still_counted(string status)
        => Assert.Equal(1, HistoryPage.CountEvents([Row("STATUS_CHANGE", "CREATED -> " + status)], "INBOUND"));

    [Fact]
    public void Five_activity_types_use_distinct_shared_theme_aware_badges()
    {
        var styles = new[] { "RECEIVE", "CANCEL_RECEIPT", "RELEASE_PICK", "ADJUST_SAVE", "MOVE_LOCATION" }
            .Select(type => HistoryPage.EventClass(Row(type))).ToArray();
        Assert.Equal(new[] { "ok", "bad", "warn", "orange", "info" }, styles);
        Assert.Equal(5, styles.Distinct().Count());

        var css = Read("src/06_Web/AMES.Web/wwwroot/app.css");
        foreach (var style in styles)
        {
            var rule = System.Text.RegularExpressions.Regex.Match(css, @"\.pill\." + style + @"\s*\{[^}]+\}").Value;
            Assert.NotEmpty(rule);
            Assert.Contains("color: var(--ames-tx-", rule);
        }
    }

    [Theory]
    [InlineData("receive", "ok")]
    [InlineData("cancel_receipt", "bad")]
    [InlineData("release_pick", "warn")]
    [InlineData("adjust_save", "orange")]
    [InlineData("move_location", "info")]
    public void Badge_mapping_is_case_insensitive(string type, string expected)
        => Assert.Equal(expected, HistoryPage.EventClass(Row(type)));

    [Fact]
    public void Page_uses_the_tested_counter_and_badge_mapping()
    {
        var page = Read("src/06_Web/AMES.Web/Components/Pages/Wh/LogHistory.razor");
        Assert.Contains("int CountEvent(string type) => CountEvents(_rows, type);", page);
        Assert.Contains("class=\"pill @EventClass(r)\"", page);
    }

    [Fact]
    public void Picking_requests_releases_and_customer_shipments_are_distinct_events_and_counts()
    {
        WarehouseRepository.OperationLogRow[] rows =
        [Row("PICKING_REQUEST"),Row("RELEASE_PICK"),Row("OUTBOUND"),Row("SHIPPED")];
        Assert.Equal("WH.LogHistory.Lbl.PickingRequest",HistoryPage.DisplayEvent(rows[0]));
        Assert.Equal("WH.LogHistory.Lbl.Release",HistoryPage.DisplayEvent(rows[1]));
        Assert.Equal("WH.LogHistory.Lbl.Outbound",HistoryPage.DisplayEvent(rows[2]));
        Assert.Equal(1,HistoryPage.CountEvents(rows,"PICKING_REQUEST"));
        Assert.Equal(1,HistoryPage.CountEvents(rows,"RELEASE"));
        Assert.Equal(2,HistoryPage.CountEvents(rows,"OUTBOUND"));
        Assert.Equal(0,HistoryPage.CountEvents([Row("STATUS_CHANGE","STORED -> RELEASED")],"OUTBOUND"));
        Assert.Equal(1,HistoryPage.CountEvents([Row("STATUS_CHANGE","STORED -> RELEASED")],"RELEASE"));
        var page=Read("src/06_Web/AMES.Web/Components/Pages/Wh/LogHistory.razor");
        foreach(var type in new[]{"PICKING_REQUEST","RELEASE"})
        {
            Assert.Contains($"Value=\"{type}\"",page);
            Assert.Contains($"@CountEvent(\"{type}\")",page);
        }
        var sql=WarehouseRepository.OperationHistorySql;
        Assert.Contains("FROM dbo.WH_PickSlip P",sql);
        Assert.Contains("WHERE P.CreatedBy='WH-REQUEST'",sql);
        Assert.Contains("CONCAT('REQUEST:',P.PickSlipID)",sql);
        Assert.Contains("T.SourceType IN('PICK_SLIP','DIRECT_OUTGOING')",sql);
        Assert.Contains("THEN 'RELEASE'",sql);
        Assert.DoesNotContain("P.CloseDate",sql);
    }

    [Fact]
    public void Request_and_release_labels_are_present_once_in_every_supported_language()
    {
        foreach(var suffix in new[]{"",".en",".ko",".es"})
        {
            var resources=System.Xml.Linq.XDocument.Parse(Read($"src/06_Web/AMES.Web/Resources/SharedResources{suffix}.resx"));
            foreach(var key in new[]{"WH.LogHistory.Lbl.PickingRequest","WH.LogHistory.Lbl.Release"})
                Assert.False(string.IsNullOrWhiteSpace(Assert.Single(resources.Root!.Elements("data"),e=>(string?)e.Attribute("name")==key).Element("value")?.Value));
        }
    }

    [Theory]
    [InlineData("SHIPPED", "WH.LogHistory.Lbl.Shipped", "warn")]
    [InlineData("CUSTOMER_RETURN", "WH.LogHistory.Lbl.CustomerReturn", "bad")]
    [InlineData("PALLET_RETURN", "WH.LogHistory.Lbl.PalletReturn", "info")]
    public void Finished_goods_events_have_explicit_labels_and_do_not_become_receipts(string type,string label,string style)
    {
        var row=Row(type);
        Assert.Equal(label,HistoryPage.DisplayEvent(row));
        Assert.Equal(style,HistoryPage.EventClass(row));
        Assert.Equal(0,HistoryPage.CountEvents([row],"INBOUND"));
        Assert.Equal(type=="SHIPPED"?1:0,HistoryPage.CountEvents([row],"OUTBOUND"));
    }

    [Fact]
    public void Grouped_shipments_have_stable_keys_distinct_from_transactions_and_returns()
    {
        var tx=Row("RECEIVE");
        var dcr=tx with { EventType="SHIPPED",HistoryKey="DCR:1",DeliveryId=1,ShipmentId=1 };
        var ret=tx with { EventType="CUSTOMER_RETURN",HistoryKey="RETURN:1" };
        Assert.Equal(3,new[]{tx.RowKey,dcr.RowKey,ret.RowKey}.Distinct().Count());
        Assert.True(dcr.HasShipmentDetails);
        Assert.False(ret.HasShipmentDetails);
        Assert.False(tx.HasShipmentDetails);
    }

    [Fact]
    public void History_deduplicates_header_backed_shipments_and_returns_but_preserves_orphan_transactions()
    {
        var sql=WarehouseRepository.OperationHistorySql;
        Assert.Contains("COALESCE(T.SourceType,'')='FG_OUTBOUND'",sql);
        Assert.Contains("EXISTS(SELECT 1 FROM dbo.FG_Shipment S WHERE S.ShipmentID=T.SourceID)",sql);
        Assert.Contains("EXISTS(SELECT 1 FROM dbo.FG_CustomerReturn R WHERE R.ReturnID=T.SourceID)",sql);
        Assert.Contains("FROM dbo.FG_CustomerReturn R",sql);
        Assert.Contains("'PALLET_RETURN'",sql);
        Assert.Contains("T.SourceType='FG_PUTAWAY' AND T.TransactionType='IN'",sql);
        Assert.Contains("THEN 'MOVE_LOCATION'",sql);
        Assert.DoesNotContain("Qty>0",sql);
        Assert.DoesNotContain("PUT AWAY",sql);
    }

    [Fact]
    public void Legacy_FG_history_redirects_to_Warehouse_and_is_removed_from_menus()
    {
        var old=Read("src/06_Web/AMES.Web/Components/Pages/Fg/History.razor");
        Assert.Contains("Nav.NavigateTo(\"/wh/log-history\", replace: true)",old);
        Assert.DoesNotContain("ListHistory",old);
        var catalog=Read("src/06_Web/AMES.Web/Services/MenuCatalog.cs");
        Assert.Contains("path.Equals(\"fg/history\", StringComparison.OrdinalIgnoreCase)",catalog);
        Assert.DoesNotContain("new(\"fg\", \"fg/history\"",catalog);
    }

    [SkippableFact]
    public void Unified_history_matches_read_only_transaction_return_and_DCR_data()
    {
        var connection=Environment.GetEnvironmentVariable("AMES_HISTORY_READ_TEST_DB");
        Skip.If(string.IsNullOrWhiteSpace(connection),"Opt-in SELECT-only unified history verification.");
        var factory=new AMES.Data.Connection.AmesConnectionFactory(connection!);
        var repo=new WarehouseRepository(factory);
        var rows=repo.ListOperationLogs();
        Assert.Equal(rows.Count,rows.Select(r=>r.RowKey).Distinct().Count());
        Assert.DoesNotContain(rows,r=>r.EventType=="PUT AWAY");
        Assert.All(repo.ListOperationLogs(eventType:"RETURN"),r=>Assert.Equal("CUSTOMER_RETURN",r.EventType));
        Assert.All(repo.ListOperationLogs(eventType:"PALLET_RETURN"),r=>Assert.Equal("PALLET_RETURN",r.EventType));
        Assert.All(repo.ListOperationLogs(eventType:"RELOCATION"),r=>Assert.Equal("MOVE_LOCATION",r.EventType));
        Assert.All(repo.ListOperationLogs(eventType:"RELEASE"),r=>Assert.Equal("RELEASE_PICK",r.EventType));
        Assert.All(repo.ListOperationLogs(eventType:"PICKING_REQUEST"),r=>
        {
            Assert.Equal("PICKING_REQUEST",r.EventType);
            Assert.StartsWith("REQUEST:",r.RowKey);
            Assert.Null(r.LotNo);
            Assert.NotNull(r.ReferenceNo);
            Assert.NotNull(r.PartNo);
            Assert.NotNull(r.LocationId);
        });
        Assert.DoesNotContain(repo.ListOperationLogs(eventType:"OUTBOUND"),r=>r.EventType=="RELEASE_PICK");
        var fg=new FinishedGoodsRepository(factory);
        foreach(var shipment in fg.ListShipmentHistory(limit:1000))
        {
            var row=Assert.Single(repo.ListOperationLogs(shipment.ReferenceNo,"OUTBOUND"),r=>r.RowKey==shipment.Key);
            Assert.True(row.HasShipmentDetails);
            Assert.Equal(shipment.Qty,row.Qty);
            Assert.Equal(shipment.ItemCount,row.ItemCount);
            Assert.Equal(row.Qty,fg.ListShipmentHistoryParts(row.DeliveryId,row.ShipmentId!.Value).Sum(x=>x.Qty));
        }
        Assert.Empty(repo.ListOperationLogs("no-match-history-42e60bd2"));
    }

    [SkippableFact]
    public void History_SQL_preserves_zero_quantity_pallets_and_null_sources_without_duplicate_return_rows()
    {
        var connection=Environment.GetEnvironmentVariable("AMES_HISTORY_READ_TEST_DB");
        Skip.If(string.IsNullOrWhiteSpace(connection),"Opt-in SELECT-only history SQL fixture.");
        const string fixture="""
            WITH TestTransactions AS
            (
                SELECT V.TransactionID,CONVERT(datetime2,'2026-11-02') TransactionTime,V.TransactionType,
                    CAST(V.SourceType AS varchar(30)) SourceType,V.SourceID,V.LotNo,'PART' PartNo,
                    CAST(NULL AS varchar(50)) LocationNo,CONVERT(decimal(18,3),V.QtyChange) QtyChange,
                    CONVERT(decimal(18,3),0) QtyBefore,CONVERT(decimal(18,3),V.QtyChange) QtyAfter,
                    'USER' OperatorID,CAST(NULL AS nvarchar(300)) Note,
                    CAST(CASE WHEN V.TransactionID=9 THEN 'RELEASE_PICK' END AS varchar(50)) ReasonCode
                FROM (VALUES(1,'IN',NULL,NULL,'LOT-A',1),(2,'OUT',NULL,NULL,'LOT-B',-1),
                    (3,'IN','FG_PUTAWAY',NULL,'LOT-C',1),(4,'IN','PALLET_RETURN',NULL,'PALLET-1',0),
                    (5,'IN','FG_RETURN',1,'LOT-R',1),(6,'OUT','FG_OUTBOUND',1,'LOT-S',-1),
                    (7,'OUT','PICK_SLIP',101,'LOT-PICK',-2),(8,'OUT','DIRECT_OUTGOING',NULL,'LOT-DIRECT',-3),
                    (9,'OUT',NULL,NULL,'LOT-LEGACY-PICK',-1))
                    V(TransactionID,TransactionType,SourceType,SourceID,LotNo,QtyChange)
            ), TestItems AS (SELECT 'PART' ItemNo,'Part' ItemName,'EA' DefaultUOM),
            TestShipments AS (SELECT 1 ShipmentID),
            TestReturns AS
            (
                SELECT 1 ReturnID,CONVERT(datetime2,'2026-11-02') ReceivedAt,CONVERT(datetime2,'2026-11-02') CreatedTS,
                    'LOT-R' LotNo,'PART' PartNo,CONVERT(decimal(18,3),1) ReturnQty,'USER' ReceivedBy,'USER' CreatedBy,
                    'Inspecting' Status,'RETURN' ReturnReason,CAST(NULL AS nvarchar(300)) Note,'CUSTOMER' CustomerID,'Part' PartName
            ), TestCustomers AS (SELECT 'CUSTOMER' CustomerID,'Customer' CustomerName),
            TestPickSlips AS
            (
                SELECT V.PickSlipID,V.PickSlipNo,CONVERT(datetime2,'2026-11-02') CreatedTS,'PART' ItemNo,
                    'LINE-LOCATION' ReqLocation,CONVERT(decimal(18,3),12.75) DemandQty,'REQUESTER' ReqUserId,
                    V.CreatedBy,'Cancelled' Status
                FROM (VALUES(101,'PK-REQUEST','WH-REQUEST'),(102,'PK-AUTO','WH-AUTO')) V(PickSlipID,PickSlipNo,CreatedBy)
            ), Entries AS
            """;
        var sql=WarehouseRepository.OperationHistorySql.Replace("WITH Entries AS",fixture)
            .Replace("dbo.WH_InventoryTransaction","TestTransactions").Replace("dbo.FG_CustomerReturn","TestReturns")
            .Replace("dbo.WH_PickSlip","TestPickSlips")
            .Replace("dbo.FG_Shipment","TestShipments").Replace("dbo.MD_Item","TestItems").Replace("dbo.MD_Customer","TestCustomers");
        using var conn=new AMES.Data.Connection.AmesConnectionFactory(connection!).OpenConnection();
        using var cmd=new Microsoft.Data.SqlClient.SqlCommand(sql,conn);
        cmd.Parameters.Add("@Like",System.Data.SqlDbType.NVarChar,130).Value=DBNull.Value;
        cmd.Parameters.Add("@OperationType",System.Data.SqlDbType.VarChar,40).Value=DBNull.Value;
        cmd.Parameters.Add("@DateFrom",System.Data.SqlDbType.Date).Value=DBNull.Value;
        cmd.Parameters.Add("@DateTo",System.Data.SqlDbType.Date).Value=DBNull.Value;
        using var reader=cmd.ExecuteReader();
        var rows=new Dictionary<string,(string Event,decimal Qty,bool NoLocation)>();
        while(reader.Read())rows.Add((string)reader["RowKey"],((string)reader["EventType"],(decimal)reader["Qty"],reader["LocationID"] is DBNull));
        Assert.Equal(9,rows.Count);
        Assert.Equal("RECEIVE",rows["TX:1"].Event);
        Assert.Equal("OUTBOUND",rows["TX:2"].Event);
        Assert.Equal("MOVE_LOCATION",rows["TX:3"].Event);
        Assert.Equal(("PALLET_RETURN",0m,true),rows["TX:4"]);
        Assert.Equal("CUSTOMER_RETURN",rows["RETURN:1"].Event);
        Assert.False(rows.ContainsKey("TX:5"));
        Assert.False(rows.ContainsKey("TX:6"));
        Assert.Equal("RELEASE_PICK",rows["TX:7"].Event);
        Assert.Equal("RELEASE_PICK",rows["TX:8"].Event);
        Assert.Equal("RELEASE_PICK",rows["TX:9"].Event);
        Assert.Equal(("PICKING_REQUEST",12.75m,false),rows["REQUEST:101"]);
        Assert.False(rows.ContainsKey("REQUEST:102"));

        reader.Close();
        cmd.Parameters["@OperationType"].Value="PICKING_REQUEST";
        using(var requests=cmd.ExecuteReader())
        {
            Assert.True(requests.Read());
            Assert.Equal("PK-REQUEST",requests["ReferenceNo"]);
            Assert.Equal("REQUESTER",requests["WorkerID"]);
            Assert.True(requests["LotNo"] is DBNull);
            Assert.False(requests.Read());
        }
        cmd.Parameters["@OperationType"].Value="RELEASE";
        using(var releases=cmd.ExecuteReader())
        {
            var count=0;
            while(releases.Read()){Assert.Equal("RELEASE_PICK",releases["EventType"]);count++;}
            Assert.Equal(3,count);
        }
        cmd.Parameters["@OperationType"].Value="OUTBOUND";
        using(var outbound=cmd.ExecuteReader())
        {
            Assert.True(outbound.Read());
            Assert.Equal("OUTBOUND",outbound["EventType"]);
            Assert.False(outbound.Read());
        }
        cmd.Parameters["@OperationType"].Value=DBNull.Value;
        cmd.Parameters["@Like"].Value="%PK-REQUEST%";
        using(var searched=cmd.ExecuteReader())
        {
            Assert.True(searched.Read());
            Assert.Equal("REQUEST:101",searched["RowKey"]);
            Assert.False(searched.Read());
        }
        cmd.Parameters["@DateFrom"].Value=new DateTime(2026,11,3);
        using(var dated=cmd.ExecuteReader())Assert.False(dated.Read());
    }

    [Fact]
    public void Expanded_part_names_are_capped_at_fifty_characters_without_changing_the_full_value()
    {
        var name=new string('A',50);
        Assert.Equal(name,HistoryPage.PartNamePreview(name));
        Assert.Equal(name+"…",HistoryPage.PartNamePreview(name+"B"));
        Assert.Equal("—",HistoryPage.PartNamePreview(null));
        var page=Read("src/06_Web/AMES.Web/Components/Pages/Wh/LogHistory.razor");
        Assert.Contains("title=\"@item.PartName\">@PartNamePreview(item.PartName)",page);
        Assert.Contains("Width=\"400px\" MinWidth=\"260px\"",page);
    }

    [Fact]
    public void Warehouse_menu_shows_picking_third_and_history_fourth_without_rewriting_permission_codes()
    {
        var catalog=Read("src/06_Web/AMES.Web/Services/MenuCatalog.cs");
        Assert.Contains(".OrderBy(s => s.SortOrder ?? 999)",catalog);
        Assert.Contains("s.LidLabel ?? s.ScreenCode",catalog);
        var sql=Read("dist/migrate_wh_fg_history_screens.sql");
        Assert.Contains("N'Picking Orders','WH-03',3,1",sql);
        Assert.Contains("N'History','WH-04',4,1",sql);
        Assert.Contains("TRIM('/' FROM HRef) IN('fg/history'",sql);
        Assert.DoesNotContain("SYS_RolePermission",sql);
        Assert.DoesNotContain("SET ScreenCode",sql);
        Assert.DoesNotContain("DELETE",sql);
        Assert.Contains("migrate_wh_fg_history_screens.sql",Read("dist/rebuild_db.sh"));
    }

    static WarehouseRepository.OperationLogRow Row(string type, string? message = null)
        => new(1, null, type, null, null, null, null, null, null, null, null, null,
            "SUCCESS", message, null, null, null, null, null, null, null);

    static string Read(string path, [System.Runtime.CompilerServices.CallerFilePath] string file = "")
        => File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "../../..", path)));
}
