using Xunit;

namespace AMES.Data.Tests;

public sealed class WarehouseLegacyRetirementTests
{
    static string Read(string path, [System.Runtime.CompilerServices.CallerFilePath] string file="")
        => File.ReadAllText(Path.Combine(Path.GetDirectoryName(file)!, "../../..", path));

    [Fact]
    public void Fresh_schema_and_export_do_not_recreate_retired_release_schedule()
    {
        Assert.DoesNotContain("CREATE TABLE [dbo].[WH_ReleaseSchedule]",Read("dist/AMES_Schema.sql"));
        Assert.Contains("name.Equals(\"WH_ReleaseSchedule\", StringComparison.OrdinalIgnoreCase)",Read("tools/schema_from_db/Program.cs"));
        Assert.Contains("CREATE TABLE [dbo].[WH_PickSlip]",Read("dist/AMES_Schema.sql"));
    }

    [Fact]
    public void Retirement_defaults_to_preview_and_requires_empty_unreferenced_legacy_table()
    {
        var sql=Read("dist/retire_wh_release_schedule.sql");
        Assert.Contains("DECLARE @Apply bit=0",sql);
        Assert.Contains("IF @Apply=0",sql);
        Assert.Contains("IF OBJECT_ID(N'dbo.WH_PickSlip',N'U') IS NULL",sql);
        Assert.Contains("WITH(TABLOCKX,HOLDLOCK)",sql);
        Assert.Contains("IF @Rows<>0",sql);
        Assert.Contains("sys.foreign_keys",sql);
        Assert.Contains("sys.sql_expression_dependencies",sql);
        Assert.Contains("ROLLBACK TRANSACTION",sql);
        Assert.DoesNotContain("DELETE FROM",sql,StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP TABLE dbo.WH_PickSlip",sql,StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Retired_demo_seeder_cannot_connect_or_write_to_any_database()
    {
        var source=Read("tools/seed_wh_demo/Program.cs");
        Assert.Contains("return 1;",source);
        Assert.DoesNotContain("SqlConnection",source);
        Assert.DoesNotContain("DELETE FROM",source);
        Assert.DoesNotContain("INSERT INTO",source);
    }

    [Fact]
    public void Unused_material_reservations_cannot_be_called_or_recreated()
    {
        var repository=Read("src/02_Data/AMES.Data/Repositories/WarehouseRepository.cs");
        Assert.DoesNotContain("ListPickingSlipCandidates",repository);
        Assert.DoesNotContain("PickingSlipCandidateRow",repository);
        Assert.DoesNotContain("PP_MaterialReservation",repository);
        Assert.Contains("WH_PickSlip",repository);
        var schema=Read("dist/AMES_Schema.sql");
        Assert.DoesNotContain("CREATE TABLE [dbo].[PP_MaterialReservation]",schema);
        Assert.DoesNotContain("DROP TABLE IF EXISTS [dbo].[PP_MaterialReservation]",schema);
        Assert.Contains("name.Equals(\"PP_MaterialReservation\", StringComparison.OrdinalIgnoreCase)",Read("tools/schema_from_db/Program.cs"));
        Assert.DoesNotContain("PP_MaterialReservation",Read("tools/test_pp_pop/test_pp_pop_reset_dev.sql"));
        Assert.Contains("repo.ListSupplyPlans",Read("src/04_Api/AMES.Api/Endpoints/PpEndpoints.cs"));
    }

    [Fact]
    public void Targeted_cleanup_requires_backup_and_keeps_inventory_and_supply_plans()
    {
        var sql=Read("dist/retire_unused_planning_mock_tables.sql");
        foreach(var guard in new[]{"DECLARE @Apply bit=0","IF @Apply=0","@BackupManifest","@BackupDirectory",
            "DESKTOP-POEDIBS","DB_NAME()<>","WITH(TABLOCKX,HOLDLOCK)","@Rows<>@Expected",
            "sys.sql_expression_dependencies","sys.foreign_keys","sys.synonyms","ROLLBACK TRANSACTION","SYS_AuditLog"})
            Assert.Contains(guard,sql);
        Assert.True(sql.IndexOf("SET UseFlag=0",StringComparison.Ordinal)<sql.IndexOf("DROP TABLE IF EXISTS dbo.TEST_SRM_ShipmentItem",StringComparison.Ordinal));
        Assert.Contains("o.parent_object_id=OBJECT_ID(N'dbo.'+p.Name)",sql);
        Assert.True(sql.IndexOf("DROP TABLE IF EXISTS dbo.TEST_SRM_ShipmentItem;",StringComparison.Ordinal)<sql.IndexOf("DROP TABLE IF EXISTS dbo.TEST_SRM_Shipment;",StringComparison.Ordinal));
        foreach(var retained in new[]{"WH_Inventory","WH_InventoryTransaction","WH_PickSlip","PP_SupplyPlan","PP_SupplyPlanDetail"})
        {
            Assert.DoesNotContain("DROP TABLE IF EXISTS dbo."+retained+";",sql);
            Assert.DoesNotContain("DELETE FROM dbo."+retained,sql);
        }
    }
}
