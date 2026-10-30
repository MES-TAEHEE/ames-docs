using System.Reflection;
using System.Text.RegularExpressions;
using AMES.Data.Repositories;
using Xunit;

namespace AMES.Data.Tests;

public class PdaDirectSqlTests
{
    [Fact]
    public void Live_retirement_is_explicit_preview_first_and_preserves_resets_and_stock()
    {
        var sql=Read("dist/retire_pda_business_procedures.sql");
        var targets=Regex.Matches(sql,@"\(N'(?<name>(?:WH|FG|SP)_PDA_[^']+)',N'[0-9A-F]{64}'\)");
        Assert.Equal(27,targets.Count);
        Assert.All(targets.Cast<Match>(),m=>Assert.DoesNotContain("TEST_RESET",m.Groups["name"].Value));
        Assert.Contains("DECLARE @Apply bit=0;",sql);
        Assert.Contains("IF @Apply=0",sql);
        Assert.Contains("HASHBYTES('SHA2_256'",sql);
        Assert.Contains("sys.sql_expression_dependencies",sql);
        Assert.Contains("ROLLBACK TRANSACTION",sql);
        Assert.DoesNotMatch(@"(?i)\b(DROP|TRUNCATE)\s+TABLE\b|\bDELETE\s+(FROM\s+)?dbo\.",sql);
    }

    public static IEnumerable<object[]> Operations() => new[]
        { typeof(PdaWarehouseSql), typeof(PdaFinishedGoodsSql), typeof(PdaSparePartsSql) }
        .SelectMany(type => type.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => new object[] { type.Name + "." + field.Name, (string)field.GetRawConstantValue()! }));

    [Theory]
    [MemberData(nameof(Operations))]
    public void Operational_batches_need_no_installed_application_procedures_or_retired_tables(string operation, string sql)
    {
        Assert.False(string.IsNullOrWhiteSpace(sql), operation);
        Assert.DoesNotMatch(@"(?i)\b(?:CREATE|ALTER|DROP|TRUNCATE)\s+(?:TABLE|PROC(?:EDURE)?)\b", sql);
        Assert.DoesNotMatch(@"(?i)\bEXEC(?:UTE)?\s+(?:dbo\.)?(?:WH_|FG_|SP_PDA_)\w+", sql);
        Assert.DoesNotMatch(@"(?i)\b(?:FROM|JOIN|UPDATE|INTO|MERGE|DELETE)\s+(?:dbo\.)?(?:WH_OLD_Inventory|WH_Receiving|WH_InboundPackage|WH_ReleasePicking|FG_Inventory|FG_Putaway|FG_ShipmentOrder)\b", sql);
        Assert.Matches(@"\ASET QUOTED_IDENTIFIER ON;\s+SET ANSI_NULLS ON;", sql.TrimStart());
    }

    [Fact]
    public void Schema_and_migrations_cannot_recreate_retired_pda_procedures()
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(ReadPath("dist/AMES_Schema.sql")))!;
        var count = 0;
        foreach (var file in Directory.EnumerateFiles(directory, "*.sql", SearchOption.AllDirectories))
        {
            var definitions = Regex.Matches(File.ReadAllText(file),
                @"(?im)^CREATE\s+(?:OR\s+ALTER\s+)?PROC(?:EDURE)?\s+(?:\[?dbo\]?\.)\[?(?<name>(?:WH_PDA_|FG_PDA_|SP_PDA_)\w+)");
            foreach (Match definition in definitions)
            {
                count++;
                Assert.Contains("TEST_RESET", definition.Groups["name"].Value);
            }
        }
        Assert.True(count >= 6, "Still-used development reset procedures must remain available.");
    }

    [Fact]
    public void Adjustment_seeks_the_case_insensitive_LOT_key_instead_of_locking_unrelated_stock_ranges()
    {
        Assert.DoesNotContain("UPPER(W.LotNo)", PdaWarehouseSql.AdjustSave);
        Assert.Contains("WHERE W.LotNo = @Scan", PdaWarehouseSql.AdjustSave);
        Assert.Contains("WHERE W.LotNo = @ResultScan", PdaWarehouseSql.AdjustSave);
        Assert.Contains("WITH (UPDLOCK, HOLDLOCK)", PdaWarehouseSql.AdjustSave);
    }

    [Fact]
    public void Cancel_sets_required_options_before_writes_without_calling_legacy_procedure()
    {
        var sql = PdaWarehouseSql.InboundCancel.TrimStart();
        Assert.StartsWith("SET QUOTED_IDENTIFIER ON;", sql);
        Assert.True(sql.IndexOf("SET ANSI_NULLS ON;", StringComparison.Ordinal)
            < sql.IndexOf("BEGIN TRANSACTION;", StringComparison.Ordinal));
        Assert.DoesNotContain("WH_PDA_INBOUND_CANCEL_RECEIPT", sql);
        Assert.Contains("new SqlCommand(PdaWarehouseSql.InboundCancel, conn)",
            Read("src/04_Api/AMES.Api/Endpoints/WhEndpoints.cs"));
    }

    [Fact]
    public void Cancellation_reverses_original_receipt_balance_but_removes_only_remaining_stock()
    {
        var sql=PdaWarehouseSql.InboundCancel;
        Assert.Contains("@PortalReceiptQty = B.Quantity",sql);
        Assert.Contains("PO.ReceivedQty - @PortalReceiptQty",sql);
        Assert.Contains("COALESCE(ReceivedQty, 0) - @PortalReceiptQty",sql);
        Assert.Contains("@PortalQty, -@PortalQty, 0",sql);
        Assert.DoesNotContain("PO.ReceivedQty - @PortalQty",sql);
    }

    [Fact]
    public void Re_receipt_reuses_only_cancelled_zero_stock_under_the_inventory_lock()
    {
        var sql=PdaWarehouseSql.InboundReceive;
        Assert.Contains("SELECT @PortalExistingQty=Qty",sql);
        Assert.Contains("FROM dbo.WH_Inventory WITH (UPDLOCK, HOLDLOCK)\n",sql.Replace("\r\n","\n"));
        Assert.Contains("IF @PortalExistingQty <> 0",sql);
        Assert.Contains("IF @PortalExistingQty = 0 AND NOT EXISTS",sql);
        Assert.Contains("TransactionType='IN' OR ReasonCode='INBOUND_CANCEL'",sql);
        Assert.Contains("SourceType='SCM_DeliveryLine' AND SourceID=@PortalDeliveryLineID",sql);
        Assert.Contains("IF @PortalExistingQty IS NULL\n",sql.Replace("\r\n","\n"));
        Assert.Contains("WHERE LotNo=@Barcode AND Qty=0",sql);
        Assert.Contains("Zero stock does not cancel a receipt",sql);
    }

    [Fact]
    public void Normal_lot_cancel_uses_original_receipt_and_blocks_consumed_or_inconsistent_stock()
    {
        var sql=PdaWarehouseSql.InboundCancel;
        Assert.Contains("@ReceiptQty=QtyChange",sql);
        Assert.Contains("ReceivedQty=ReceivedQty-@ReceiptQty",sql);
        Assert.Contains("@Qty,-@Qty,0,'INBOUND_CANCEL','LOT'",sql);
        Assert.Contains("TransactionID>@ReceiptID",sql);
        Assert.Contains("QtyChange<>0 AND COALESCE(TransactionType,'')<>'ADJ'",sql);
        Assert.Contains("IF @ExpectedQty<>@Qty",sql);
        Assert.Contains("COALESCE(ReceivedQty,0)>=@ReceiptQty",sql);
        Assert.Contains("Status IN('Cancelled','Canceled') THEN Status",sql);
        Assert.True(sql.IndexOf("IF @ExpectedQty<>@Qty",StringComparison.Ordinal)<sql.LastIndexOf("DELETE dbo.WH_Inventory",StringComparison.Ordinal));
        Assert.Contains("This LOT has a receipt history that was not cancelled",PdaWarehouseSql.InboundReceive);
    }

    [Fact]
    public void Inbound_paths_no_longer_use_retired_procedure_parameter_metadata()
    {
        foreach(var path in new[]{"src/04_Api/AMES.Api/Endpoints/WhEndpoints.cs","src/02_Data/AMES.Data/Repositories/ScmDeliveryCases.cs"})
            Assert.DoesNotMatch(@"(?:WH|FG|SP)_PDA_\w+\.@\w+",Read(path));
        var endpoint=Read("src/04_Api/AMES.Api/Endpoints/WhEndpoints.cs");
        Assert.Contains("\"@ScheduleYear\", SqlDbType.Int",endpoint);
        Assert.Contains("\"@ScheduleQuarter\", SqlDbType.Int",endpoint);
        Assert.Contains("\"@SupplierCode\", SqlDbType.VarChar, 20",endpoint);
    }

    [Fact]
    public void Receipt_preserves_portal_balance_capacity_history_and_rollback_guards()
    {
        var sql = PdaWarehouseSql.InboundReceive;
        Assert.Contains("SCM_DeliveryLine DL WITH (UPDLOCK, HOLDLOCK)", sql);
        Assert.Contains("received quantity would exceed", sql);
        Assert.Contains("Location capacity would be exceeded", sql);
        Assert.Contains("SCM_DeliveryLine', @PortalDeliveryLineID", sql);
        Assert.Contains("IF @SimulateFailure", sql);
        Assert.Contains("GOTO PdaInboundResult;", sql);
        Assert.Single(Regex.Matches(sql, @"DECLARE @ResultMode\b").Cast<Match>());
        Assert.Contains("WH_InventoryTransaction", sql);
    }

    [Fact]
    public void Return_validates_latest_shipment_in_the_same_batch_without_insert_exec()
    {
        var sql = PdaFinishedGoodsSql.ReturnReceive;
        Assert.DoesNotMatch(@"(?i)INSERT\s+@\w+\s+EXEC", sql);
        Assert.Contains("INSERT @P SELECT @ReturnScanB Barcode", sql);
        Assert.Contains("latest shipment changed", sql);
        Assert.Contains("WH_Inventory WITH(UPDLOCK,HOLDLOCK)", sql);
        Assert.Contains("FG_CustomerReturn WITH(UPDLOCK,HOLDLOCK)", sql);
    }

    [Fact]
    public void Pda_endpoints_only_keep_procedure_calls_in_guarded_development_reset_handlers()
    {
        foreach (var file in new[] { "WhEndpoints.cs", "FgEndpoints.cs" })
        {
            var source = Read("src/04_Api/AMES.Api/Endpoints/" + file);
            Assert.DoesNotContain("ProcedureExists(", source);
            var calls = Regex.Matches(source, @"new SqlCommand\((?<argument>[^;{}]+?)\)\s*\{\s*CommandType\s*=\s*CommandType.StoredProcedure");
            Assert.Equal(file == "WhEndpoints.cs" ? 4 : 1, calls.Count);
            foreach (Match call in calls)
                Assert.True(call.Groups["argument"].Value.Contains("TEST_RESET") || call.Groups["argument"].Value.Trim() == "procedure, connection");
            Assert.Contains("PdaTestDatabaseGuard.AllowsReset(app, factory)", source);
        }
        var wh = Read("src/04_Api/AMES.Api/Endpoints/WhEndpoints.cs");
        Assert.DoesNotContain("ExecuteReleasePickStoredProcedure", wh);
        Assert.Contains("ExecuteReleaseBatch(factory, body.PickSlipNo", wh);
        Assert.Contains("PdaWarehouseSql.FifoEarlier", wh);
        Assert.Contains("SqlDbType.NVarChar, 50).Value = locationId.Trim()", wh);
        Assert.Contains("new SqlCommand(PdaWarehouseSql.InboundReceive,c,tx)", Read("src/02_Data/AMES.Data/Repositories/ScmDeliveryCases.cs"));
    }

    private static string Read(string relative, [System.Runtime.CompilerServices.CallerFilePath] string file = "")
        => File.ReadAllText(ReadPath(relative, file));

    private static string ReadPath(string relative, [System.Runtime.CompilerServices.CallerFilePath] string file = "")
        => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!, "../../..", relative));
}
