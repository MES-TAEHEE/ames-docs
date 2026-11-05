using AMES.Data.Services;
using Xunit;
using static AMES.Data.Services.LinePickingPlanner;

namespace AMES.Data.Tests;

public sealed class WorkOrderPickingTests
{
    [Fact]
    public void Picking_refresh_has_an_edit_permission_slot_without_granting_read_only_roles_edit()
    {
        var migration=Read("../../../../dist/migrate_wh_picking_refresh_permission.sql");
        Assert.Contains("TRIM('/' FROM HRef)='wh/picking-orders'",migration);
        Assert.Contains("STUFF(@Criteria,2,1,'_')",migration);
        Assert.DoesNotContain("UPDATE dbo.SYS_RolePermission",migration);
        Assert.DoesNotContain("INSERT dbo.SYS_RolePermission",migration);
        Assert.Contains("SYS_AuditLog",migration);
        var schema=Read("../../../../dist/AMES_Schema.sql");
        var screen=Assert.Single(schema.Split('\n'),x=>x.StartsWith("INSERT INTO dbo.SYS_Screen ")&&x.Contains("'wh/picking-orders'"));
        Assert.Contains("'__X'",screen);
        Assert.True(PermissionSlots.Has("__X",'E'));
        Assert.DoesNotContain("E",PermissionSlots.Granted("RXX","__X"));
        Assert.Equal("REX",PermissionSlots.Compose("__X","RE"));
        Assert.Contains("migrate_wh_picking_refresh_permission.sql",Read("../../../../dist/rebuild_db.sh"));
    }

    [Fact]
    public void Picking_order_refresh_rechecks_permission_and_generates_before_reloading_without_clearing_search()
    {
        var page=Read("../../../06_Web/AMES.Web/Components/Pages/Wh/PickingOrders.razor");
        var orders=page[..page.IndexOf("<AmesTab Title=\"@L[\"WH.Picking.Tab.LineStock\"]",StringComparison.Ordinal)];
        Assert.Contains("Text=\"@L[\"Btn.Refresh\"].Value\" Icon=\"refresh\" Click=\"Refresh\"",orders);
        Assert.Equal(2,page.Split("Click=\"Refresh\"",StringSplitOptions.None).Length-1);
        Assert.Contains("_refreshing || _cancelling || !Permissions.CanEdit(\"wh/picking-orders\")",page);
        var load=page[page.IndexOf("void Load()",StringComparison.Ordinal)..page.IndexOf("async Task Refresh()",StringComparison.Ordinal)];
        Assert.Contains("WH.ListLineStock()",load);
        Assert.Contains("WH.ListPickingSlipHeaders(_search)",load);
        Assert.DoesNotContain("Generate",load);
        Assert.DoesNotContain("_search =",load);
        var refresh=page[page.IndexOf("async Task Refresh()",StringComparison.Ordinal)..page.IndexOf("void SelectOrder",StringComparison.Ordinal)];
        var permission=refresh.IndexOf("Permissions.RecheckEditAsync(Auth, \"wh/picking-orders\")",StringComparison.Ordinal);
        var generate=refresh.IndexOf("WH.GenerateLinePickingOrders()",StringComparison.Ordinal);
        Assert.True(permission>=0 && permission<generate);
        Assert.True(refresh.IndexOf("Load();",StringComparison.Ordinal)>generate);
        Assert.Contains("if (!_allowed || _refreshing || _cancelling) return;",refresh);
        Assert.Contains("finally { _refreshing = false; }",refresh);
        Assert.Contains("WH.Picking.Err.Refresh",refresh);
        Assert.DoesNotContain("_search =",refresh);
    }

    [Fact]
    public void Picking_page_does_not_show_or_load_the_material_setup_notification()
    {
        var page=Read("../../../06_Web/AMES.Web/Components/Pages/Wh/PickingOrders.razor");
        Assert.DoesNotContain("MaterialSetup",page);
        Assert.DoesNotContain("_materialIssues",page);
        Assert.DoesNotContain("ListWorkOrderMaterialIssues",page);
        Assert.Contains("WH.ListLineStock()",page);
        Assert.Contains("WH.ListPickingSlipHeaders(_search)",page);
    }

    [SkippableFact]
    public void Read_only_database_preview_matches_the_current_schema()
    {
        var connection = Environment.GetEnvironmentVariable("AMES_LINE_PICKING_READONLY_CONNECTION");
        Skip.If(string.IsNullOrWhiteSpace(connection), "Read-only preview connection is not configured.");
        var repository = new AMES.Data.Repositories.WarehouseRepository(new AMES.Data.Connection.AmesConnectionFactory(connection!));
        Assert.NotNull(repository.ListWorkOrderMaterialIssues());
        Assert.NotNull(repository.ListLineMaterialCandidates("LINE-IMG-01"));
        Assert.NotNull(repository.ListPickingSlipLines("NO-SUCH-READONLY-PICK-SLIP"));
        var first = repository.ListPickingSlipHeaders().FirstOrDefault();
        if (first is not null) Assert.NotNull(repository.ListPickingSlipLines(first.PickSlipNo));
    }

    private static readonly DateTime Due = new(2026, 11, 4);
    private static Step Work(int id, decimal qty = 100) => new(id, $"WO-{id}", 20, "IMG1", "PRODUCT", qty, Due);
    private static MrpCalculator.BomLine Edge(string parent = "PRODUCT", string child = "PART", decimal qty = 1, decimal scrap = 0)
        => new(parent, child, qty, scrap);
    private static Location Loc(decimal stock = 0, decimal minimum = 0) => new("W101A1F1", "IMG1", "PART", minimum, stock);

    [Fact]
    public void Work_order_subtracts_stock_and_in_flight_supply()
    {
        var result = Plan([Work(1)], [Edge()], [Loc(20)], [new("W101A1F1", "PART", 30)]);
        Assert.Empty(result.Issues);
        var request = Assert.Single(result.Requests);
        Assert.Equal(1, request.WoId);
        Assert.Equal(50, request.Qty);
        Assert.Equal("W101A1F1", request.LocationId);
    }

    [Fact]
    public void Shared_stock_is_allocated_once_across_work_orders_and_repeat_is_idempotent()
    {
        var result = Plan([Work(1), Work(2)], [Edge()], [Loc(50)], []);
        Assert.Equal(new decimal[] { 50, 100 }, result.Requests.Select(r => r.Qty));
        var pending = result.Requests.Select(r => new Pending(r.LocationId, r.PartNo, r.Qty)).ToList();
        Assert.Empty(Plan([Work(1), Work(2)], [Edge()], [Loc(50)], pending).Requests);
    }

    [Theory]
    [InlineData(100, 20, 10, 80)]
    [InlineData(10, 20, 100, 80)]
    [InlineData(100, 20, 100, 80)]
    [InlineData(0, 20, 100, 80)]
    public void Minimum_is_a_floor_not_an_additional_work_order_requirement(decimal demand, decimal stock, decimal min, decimal expected)
        => Assert.Equal(expected, Plan([Work(1, demand)], [Edge()], [Loc(stock, min)], []).Requests.Sum(r => r.Qty));

    [Fact]
    public void Injection_output_stops_downstream_raw_material_explosion()
    {
        Step[] steps = [new(1, "WO-1", 10, "INJ1", "CORE", 0, Due), Work(1)];
        MrpCalculator.BomLine[] bom = [Edge(child: "CORE"), Edge("CORE", "RESIN", 2), Edge(child: "PART")];
        Location[] locations = [Loc(), new("IMG-CORE", "IMG1", "CORE", 0, 0), new("INJ-RESIN", "INJ1", "RESIN", 0, 0)];
        var result = Plan(steps, bom, locations, []);
        Assert.Empty(result.Issues);
        Assert.Equal(new[] { "CORE", "PART" }, result.Requests.Select(r => r.PartNo).Order());
        Assert.DoesNotContain(result.Requests, r => r.PartNo == "RESIN");
    }

    [Fact]
    public void Assigned_subassembly_is_supplied_as_a_unit_and_not_exploded()
    {
        var result = Plan([Work(1)], [Edge(), Edge("PART", "RAW", 5)], [Loc()], []);
        Assert.Equal("PART", Assert.Single(result.Requests).PartNo);
        Assert.Empty(result.Issues);
    }

    [Fact]
    public void Missing_line_locations_are_reported_and_multiple_locations_share_the_same_line_demand()
    {
        Assert.Single(Plan([Work(1)], [Edge()], [], []).Issues);
        var result = Plan([Work(1)], [Edge()], [Loc(), Loc() with { Id = "OTHER" }], []);
        Assert.Empty(result.Issues);
        Assert.Equal(100,Assert.Single(result.Requests).Qty);
        var covered=Plan([Work(1)],[Edge()],[Loc(60),Loc(40) with{Id="OTHER"}],[]);
        Assert.Empty(covered.Requests);
    }

    [Fact]
    public void Other_stock_does_not_block_line_supply_but_legacy_box_count_requests_cannot_mix_units()
    {
        Assert.Equal(100,Assert.Single(Plan([Work(1)], [Edge()], [Loc() with { WrongPart = true }], []).Requests).Qty);
        var result = Plan([Work(1)], [Edge()], [Loc()], [new("W101A1F1", "PART", 2, false)]);
        Assert.Empty(result.Requests);
        Assert.Single(result.Issues);
    }

    [Fact]
    public void Decimal_uom_and_scrap_are_preserved_and_rounded_up_to_storage_precision()
    {
        var result = Plan([Work(1, 3)], [Edge(qty: 0.1234m, scrap: 10)], [Loc() with { Unit = "G" }], []);
        var request = Assert.Single(result.Requests);
        Assert.Equal(0.408m, request.Qty);
        Assert.Equal("G", request.Unit);
    }

    [Fact]
    public void Released_in_transit_then_delivered_then_consumed_supply_is_not_requested_twice()
    {
        Assert.Empty(Plan([Work(1)],[Edge()],[Loc(20)],[new("W101A1F1","PART",80)]).Requests);
        Assert.Empty(Plan([Work(1)],[Edge()],[Loc(100)],[]).Requests);
        Assert.Empty(Plan([Work(1,60)],[Edge()],[Loc(60)],[]).Requests);
        var request=Assert.Single(Plan([],[Edge()],[Loc(5,20)],[]).Requests);
        Assert.Null(request.WoId);
        Assert.Equal(15,request.Qty);
    }

    [Fact]
    public void Minimum_is_replenished_without_bom_or_work_orders_and_each_location_keeps_its_floor()
    {
        var result=Plan([],[],[Loc(10,20),Loc(3,10) with{Id="OTHER"}],[]);
        Assert.Equal(17,result.Requests.Sum(r=>r.Qty));
        Assert.All(result.Requests,r=>Assert.Null(r.WoId));
        Assert.Empty(Plan([],[],[Loc(10,20),Loc(3,10) with{Id="OTHER"}],
            result.Requests.Select(r=>new Pending(r.LocationId,r.PartNo,r.Qty)).ToList()).Requests);
    }

    [Fact]
    public void Stock_on_a_different_line_cannot_cover_the_work_order()
    {
        var result=Plan([Work(1)],[Edge()],[Loc(20),Loc(1000) with{Id="OTHER",LineId="OTHER"}],[]);
        Assert.Equal(80,Assert.Single(result.Requests).Qty);
    }

    [Fact]
    public void Backflush_shortage_is_covered_as_well_as_the_remaining_work()
        => Assert.Equal(120,Plan([Work(1)],[Edge()],[Loc(-20)],[]).Requests.Sum(r=>r.Qty));

    [Fact]
    public void Demand_reads_only_issued_work_orders_not_production_plan_or_draft_orders()
    {
        var source=Read("WarehouseRepository.WorkOrderPicking.cs");
        Assert.Contains("FROM dbo.PP_WorkOrder W JOIN dbo.PP_WorkOrderRouting",source);
        Assert.Contains("W.Status IN ('Released','In Progress')",source);
        Assert.Contains("COALESCE(W.OrderQty,0)-COALESCE(R.CompletedQty,0)",source);
        Assert.DoesNotContain("PP_ProductionPlan",source);
        Assert.DoesNotContain("PP_LineSchedule",source);
        Assert.Contains("LineTransfersInTransitSql",source);
        foreach(var name in new[]{"InjLotRepository.cs","ImgLotRepository.cs","PntLotRepository.cs","ReworkRepository.cs"})
            Assert.Contains("tx.Commit();\n            WarehouseRepository.RecheckLinePicking(_factory);",Read(name).Replace("\r\n","\n"));
    }

    [Fact]
    public void Cycles_and_missing_bom_are_reported_without_partial_requests()
    {
        var cycle = Plan([Work(1)], [Edge(child: "LOOP"), Edge("LOOP", "PRODUCT")], [Loc()], []);
        Assert.Empty(cycle.Requests);
        Assert.Contains("cycle", Assert.Single(cycle.Issues).Message);
        Assert.Single(Plan([Work(1)], [], [Loc()], []).Issues);
    }

    [Fact]
    public void Identical_output_across_operations_does_not_duplicate_the_full_bom()
    {
        var steps = new[] { Work(1) with { Sequence = 10 }, Work(1) };
        var result = Plan(steps, [Edge()], [Loc()], []);
        Assert.Equal(100, result.Requests.Sum(r => r.Qty));
        Assert.Single(result.Issues);
    }

    [Fact]
    public void Shared_release_path_saves_wo_and_cleanup_preserves_inventory_and_picked_history()
    {
        var release = Read("WorkOrderRepository.cs");
        Assert.Contains("WarehouseRepository.GenerateWorkOrderPicking(conn, tx);", release);
        var source = Read("WarehouseRepository.WorkOrderPicking.cs");
        Assert.Contains("(WoID,PickSlipNo,ReqLocation", source);
        Assert.Contains("P.CloseDate IS NULL", source);
        Assert.Contains("W.Status NOT IN ('Released','In Progress')", source);
        Assert.DoesNotContain("DELETE", source);
        Assert.DoesNotContain("UPDATE dbo.WH_Inventory", source);
    }

    private static string Read(string file, [System.Runtime.CompilerServices.CallerFilePath] string current = "")
        => File.ReadAllText(Path.Combine(Path.GetDirectoryName(current)!, "../AMES.Data/Repositories", file));
}
