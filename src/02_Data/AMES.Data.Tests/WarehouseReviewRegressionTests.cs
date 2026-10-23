using System.Text.RegularExpressions;
using AMES.Data.Repositories;
using AMES.Data.Services;
using Microsoft.Data.SqlClient;
using Xunit;

namespace AMES.Data.Tests;

public sealed class WarehouseReviewRegressionTests
{
    [Fact]
    public void Portal_cancellation_rejects_line_moves_consumption_and_unaccounted_quantity_before_deleting_stock()
    {
        var sql=PdaWarehouseSql.InboundCancel;
        var portal=sql[..sql.IndexOf("GOTO PdaInboundResult;",StringComparison.Ordinal)];
        foreach(var guard in new[]{"TransactionID>@PortalReceiptID","SourceType IN('BACKFLUSH','BACKFLUSH_NET')",
            "QtyChange<>0 AND COALESCE(TransactionType,'')<>'ADJ'","LocationID=@PortalLocation AND LineID IS NOT NULL",
            "LEN(LotPrefix)=2","IF @PortalHistoryQty<>@PortalQty","ReceivedQty>=@PortalReceiptQty"})
            Assert.Contains(guard,portal);
        Assert.True(portal.IndexOf("IF @PortalHistoryQty<>@PortalQty",StringComparison.Ordinal)
            < portal.IndexOf("DELETE FROM dbo.WH_Inventory",StringComparison.Ordinal));
        Assert.Contains("TransactionType='ADJ'",portal); // Preserve adjustment -> cancel -> re-receive workflow.
    }

    [Fact]
    public void Primary_locations_cannot_receive_move_or_gain_stock_or_become_picking_sources()
    {
        foreach(var sql in new[]{PdaWarehouseSql.InboundReceive,PdaWarehouseSql.InboundMove,PdaWarehouseSql.AdjustSave,PdaFinishedGoodsSql.AdjustSave})
        {
            Assert.Contains("LocationLevel=1",sql);
            Assert.Contains("Primary locations cannot hold stock",sql);
        }
        Assert.Contains("LocationType='FG' AND LocationLevel=2",PdaFinishedGoodsSql.PutAway);
        Assert.Contains("L.LocationLevel=1",WarehouseRepository.WarehouseBoxPredicate);
        var api=Read("src/04_Api/AMES.Api/Endpoints/WhEndpoints.cs");
        Assert.Contains("ISNULL(l.ActiveFlag,1) = 1 AND l.LocationLevel=2",api);
        Assert.Contains("COALESCE(L.ActiveFlag, 1) = 1 AND L.LocationLevel=2",api);
    }

    [Fact]
    public void Putaway_includes_repaired_goods_but_still_requires_assy_and_preserves_existing_quantity()
    {
        Assert.Contains("OR P.ProcessCode='RWK'",PdaFinishedGoodsSql.PutAway);
        Assert.Contains("I.ItemType='ASSY'",PdaFinishedGoodsSql.PutAway);
        Assert.Contains("SET @Qty=COALESCE(@StoredQty,@ProductionQty)",PdaFinishedGoodsSql.PutAway);
        var api=Read("src/04_Api/AMES.Api/Endpoints/FgEndpoints.cs");
        Assert.DoesNotContain("gsLot[12..]",api);
        Assert.Contains("NULLIF(LTRIM(RTRIM(S.LocationNo)),'') IS NOT NULL",api);
    }

    [Fact]
    public void Backflush_reads_detail_and_legacy_line_locations_and_logs_the_actual_source()
    {
        var sql=BackflushRepository.ReadLineLotsSql;
        Assert.Contains("W.LocationNo=@Loc OR EXISTS",sql);
        Assert.Contains("L.LineID=@Line AND L.LocationLevel=2",sql);
        Assert.Contains("C.ParentLotNo=W.LotNo",sql);
        Assert.Contains("ORDER BY W.ReceivedAt,W.CreatedAt,W.LotNo",sql);
        var source=Read("src/02_Data/AMES.Data/Repositories/BackflushRepository.cs");
        Assert.Contains("locations[n.LotNo]",source);
        Assert.Contains("locations[t.LotNo]",source);
        var fifo=new[]{new BackflushLot("OLD",2,"I1"),new BackflushLot("DETAIL",3,"I101A1F1")};
        var allocated=BackflushRules.Allocate(4,fifo,0);
        Assert.Equal(new[]{new BackflushTake("OLD",2),new BackflushTake("DETAIL",2)},allocated.Takes);
        Assert.Equal(0,allocated.Shortage);
    }

    [Fact]
    public void Child_outbound_does_not_ship_the_physical_pallet_and_product_return_detaches_it()
    {
        Assert.Contains("IF @UnitType='PALLET' AND @PalletNo IS NOT NULL",PdaFinishedGoodsSql.OutboundComplete);
        Assert.Contains("ParentLotNo=NULL,PalletNo=NULL",PdaFinishedGoodsSql.ReturnReceive);
        var sql=PdaFinishedGoodsSql.OutboundComplete;
        Assert.True(sql.IndexOf("FROM dbo.MD_Pallet WITH(UPDLOCK,HOLDLOCK)",StringComparison.Ordinal)
            < sql.IndexOf("FROM dbo.WH_Inventory WITH(UPDLOCK,HOLDLOCK)",StringComparison.Ordinal));
    }

    [Fact]
    public void Import_and_shipment_share_the_gate_and_import_cannot_reduce_locally_shipped_totals()
    {
        var source=Read("src/02_Data/AMES.Data/Repositories/PpRepository.cs");
        source=source[source.IndexOf("public (int Inserted, int Updated) UpsertCustomerOrders",StringComparison.Ordinal)..];
        source=source[..source.IndexOf("// ── PP-003",StringComparison.Ordinal)];
        Assert.Contains("@ShippedQty < COALESCE(S.ShippedQty,0)",source);
        Assert.Contains("WHERE L.SoID=S.SoID AND D.Status='Shipped'",source);
        Assert.Contains("THEN S.ShippedQty ELSE @ShippedQty END",source);
        Assert.True(source.IndexOf("FinishedGoodsRepository.Gate(conn, tx)",StringComparison.Ordinal)
            < source.IndexOf("ReadSoAutoConfirm(conn, tx)",StringComparison.Ordinal));
    }

    [Fact]
    public void Generic_supplier_receipt_cannot_credit_production_lots_ambiguous_vendors_or_over_receipts()
    {
        var sql=PdaWarehouseSql.InboundReceive;
        foreach(var guard in new[]{"L.WoID IS NOT NULL","I.ItemType='ASSY'","COUNT(DISTINCT COALESCE(VendorID,''))",
            "COALESCE(ReceivedQty,0)+@Qty>OrderQty","The linked purchase order is closed or cancelled"})
            Assert.Contains(guard,sql);
        Assert.DoesNotContain("THEN 'Complete'",sql);
        Assert.Contains("ELSE 'Partial'",sql);
    }

    [Fact]
    public void Unlocated_stock_remains_visible_without_counting_container_totals_twice()
    {
        var source=Read("src/02_Data/AMES.Data/Repositories/WarehouseRepository.cs");
        source=source[source.IndexOf("public List<LocationInventoryRow> ListLocationInventory",StringComparison.Ordinal)..];
        Assert.Contains("LEFT JOIN dbo.MD_Location L ON L.LocationID=S.LocationNo",source);
        Assert.Contains("C.ParentLotNo=S.LotNo",source);
        var report=Read("src/02_Data/AMES.Data/Repositories/RptRepository.cs");
        Assert.Contains("OR NULLIF(LTRIM(RTRIM(w.LocationNo)),'') IS NULL",report);
        Assert.Contains("C.ParentLotNo=w.LotNo",report);
    }

    [Fact]
    public void Rebuild_does_not_reuse_generated_defaults_duplicate_indexes_or_destructive_injection_bootstrap()
    {
        var schema=Read("dist/AMES_Schema.sql");
        Assert.DoesNotMatch(@"CONSTRAINT \[DF__[^\]]+__[0-9A-Fa-f]{8}\]",schema);
        Assert.Single(Regex.Matches(schema,@"CREATE UNIQUE NONCLUSTERED INDEX \[UX_PR_PopSession_BearerHash\]").Cast<Match>());
        Assert.DoesNotMatch(@"(?m)^\s+migrate_inj_agent\.sql\s",Read("dist/rebuild_db.sh"));
        Assert.Contains("omitLegacyDefaultNames: true",Read("tools/schema_from_db/Program.cs"));
        Assert.Contains("!RetiredPdaProcedure(p.Name)",Read("tools/schema_from_db/Program.cs"));
        var screens=Read("dist/migrate_scm_portal_screens.sql");
        foreach(var route in new[]{"portal/due-orders","portal/delivery-notes","portal/receipts","portal/packing-quantities","scm/delivery-progress","scm/portal-users"})
            Assert.Contains(route,screens);
        Assert.DoesNotContain("'portal/delivery-entry'",screens);
        Assert.Contains("'SCM-005' AS ScreenCode",Read("dist/migrate_scm_portal_user_login.sql"));
    }

    public static IEnumerable<object[]> CompileBatches()
    {
        yield return [PdaWarehouseSql.InboundReceive,"DECLARE @ReceiveMode nvarchar(10),@LotBarcode nvarchar(50),@LocationId varchar(50),@UserId nvarchar(40),@SimulateFailure bit;"];
        yield return [PdaWarehouseSql.InboundMove,"DECLARE @ReceiveMode nvarchar(10),@LotBarcode nvarchar(50),@LocationId varchar(50),@UserId nvarchar(40);"];
        yield return [PdaWarehouseSql.InboundCancel,"DECLARE @ReceiveMode nvarchar(10),@LotBarcode nvarchar(50),@UserId nvarchar(40),@CancelNote nvarchar(500);"];
        yield return [PdaFinishedGoodsSql.PutAway,"DECLARE @Barcode nvarchar(50),@Location varchar(50),@OperatorID varchar(20);"];
        yield return [PdaFinishedGoodsSql.OutboundComplete,"DECLARE @Barcode nvarchar(50),@CustomerID varchar(20),@OperatorID varchar(20),@ShippingDeliveryID int;"];
        yield return [BackflushRepository.ReadLineLotsSql,"DECLARE @Part varchar(20),@Ph nvarchar(50),@Loc varchar(50),@Line varchar(20);"];
        var pp=Read("src/02_Data/AMES.Data/Repositories/PpRepository.cs");
        pp=pp[pp.IndexOf("public (int Inserted, int Updated) UpsertCustomerOrders",StringComparison.Ordinal)..];
        yield return [Regex.Match(pp,"const string updSql = \"\"\"(?<sql>[\\s\\S]*?)\"\"\";").Groups["sql"].Value,
            "DECLARE @Item varchar(20),@OrderQty decimal(14,3),@ShippedQty decimal(14,3),@OrderDate date,@ReqDate date,@Actor varchar(20),@So varchar(20),@Line int,@Cust varchar(20);"];
        var fg=Read("src/04_Api/AMES.Api/Endpoints/FgEndpoints.cs");
        fg=fg[fg.IndexOf("g.MapGet(\"/putaway/waiting\"",StringComparison.Ordinal)..];
        yield return [Regex.Match(fg,"const string sql = \"\"\"(?<sql>[\\s\\S]*?)\"\"\";").Groups["sql"].Value,""];
    }

    [SkippableTheory]
    [MemberData(nameof(CompileBatches))]
    public void Changed_batches_compile_without_executing_stock_mutations(string sql,string declarations)
    {
        var connection=Environment.GetEnvironmentVariable("AMES_REVIEW_READ_DB");
        Skip.If(string.IsNullOrEmpty(connection),"Set AMES_REVIEW_READ_DB for compile-only SQL verification.");
        using var conn=new SqlConnection(connection);
        conn.Open();
        Assert.False(string.IsNullOrWhiteSpace(sql));
        using var cmd=new SqlCommand("SET NOEXEC ON;\n"+declarations+"\n"+sql+"\nSET NOEXEC OFF;",conn);
        cmd.ExecuteNonQuery();
    }

    private static string Read(string path,[System.Runtime.CompilerServices.CallerFilePath] string file="")
        =>File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!,"../../..",path)));
}
