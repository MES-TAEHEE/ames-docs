using AMES.Data.Connection;
using AMES.Data.Repositories;
using Xunit;

namespace AMES.Data.Tests;

public sealed class LocationStorageTests
{
    [Theory]
    [InlineData(-1)]
    [InlineData(1.5)]
    [InlineData(100000000000)]
    public void Invalid_line_minimum_is_rejected_before_opening_a_database(double value)
    {
        var repository = new MasterDataRepository(new AmesConnectionFactory("invalid connection string"));
        var error = Assert.Throws<ArgumentException>(() => repository.UpdateLocation(
            "I101A1F1", "Line", "EOS", "I1", null, "A1", "01", "F1", null, "LINE", null, true, "TEST",
            minimumQty: (decimal)value));
        Assert.Equal("minimumQty", error.ParamName);
    }

    [Fact]
    public void Location_editor_saves_line_minimum_with_existing_edit_permission_and_confirmation()
    {
        var line = Read("src/06_Web/AMES.Web/Components/Pages/Md/Rp/Line.razor");
        Assert.DoesNotContain("<LineLocationEditor", line);
        Assert.DoesNotContain("LineLocationMinimum", line);
        var page = Read("src/06_Web/AMES.Web/Components/Pages/Md/Fd/Location.razor");
        Assert.Contains("!string.IsNullOrWhiteSpace(_sel.LineId)", page);
        Assert.Contains("Name=\"EditMinimumQty\" TValue=\"decimal\" @bind-Value=\"_f.MinimumQty\"", page);
        Assert.Contains("MinimumQty = r.MinimumQty ?? 0", page);
        Assert.Contains("PermSvc.RecheckEditAsync(AuthPrvd, \"md/fd/location\")", page);
        Assert.Contains("_f.MinimumQty != (_sel.MinimumQty ?? 0) ? _f.MinimumQty : null", page);
        var repo = Read("src/02_Data/AMES.Data/Repositories/MasterDataRepository.cs");
        var update = repo[repo.IndexOf("public void UpdateLocation(", StringComparison.Ordinal)..repo.IndexOf("private static void AddLocationPart", StringComparison.Ordinal)];
        Assert.Contains("decimal? minimumQty = null", update);
        Assert.Contains("MinimumQty=COALESCE(@Min,MinimumQty)", update);
        Assert.Contains("LocationLevel=2 AND NULLIF(LTRIM(RTRIM(LineID)),'') IS NOT NULL", update);
        Assert.Contains("minimumParameter.Precision = 18; minimumParameter.Scale = 3", update);
        Assert.Contains("AddLocationPart(cmd,assignedPartNo)", update);
        Assert.DoesNotContain("WH_Inventory SET", update);
        Assert.DoesNotContain("INSERT dbo.WH_PickSlip", update);
    }

    [Theory]
    [InlineData("", "MA", "GRID")]
    [InlineData("EOS", " ", "GRID")]
    [InlineData("EOS", "MA", "BULK")]
    [InlineData("EOS", "A", "OPEN")]
    [InlineData("EOS", "MAT_AREA", "OPEN")]
    [InlineData("EOS", "ma", "OPEN")]
    public void Invalid_storage_settings_are_rejected_before_opening_a_database(
        string warehouse, string area, string mode)
    {
        var repository = new WarehouseRepository(new AmesConnectionFactory("invalid connection string"));
        Assert.Throws<ArgumentException>(() => repository.CreateLocationMapArea(warehouse, area, "Area", mode));
    }

    [Fact]
    public void Existing_areas_default_to_grid()
    {
        var area = new WarehouseRepository.WarehouseAreaRow("MAT_AREA", "Materials", true, 0, 0, "EOS", "EOS", "MA");
        Assert.Equal("GRID", area.StorageMode);
    }

    [Fact]
    public void New_grid_areas_start_with_floor_one_and_its_first_cell()
    {
        var create = Read("src/02_Data/AMES.Data/Repositories/WarehouseRepository.LocationStorage.cs");
        Assert.Contains("IF @Mode='GRID'", create);
        Assert.Contains("@Area,'A1','01','F1',2,@Parent,1,@Actor", create);
        Assert.Contains("LocationMapNaming.LocationNo(areaCode, \"01\", \"A1\", \"F1\")", create);
        var page = Read("src/06_Web/AMES.Web/Components/Shared/LocationMapExplorer.razor");
        var area = page[page.IndexOf("Warehouse.CreateLocationMapArea", StringComparison.Ordinal)..];
        Assert.Contains("_floor = _storageMode == \"GRID\" ? \"F1\" : null", area);
        Assert.Contains("_selectingFloor = false", area);
        var repo = Read("src/02_Data/AMES.Data/Repositories/WarehouseRepository.cs");
        Assert.Contains("Cannot delete the last floor.", repo);
        Assert.Contains("NULLIF(Slot,'') IS NOT NULL AND Slot<>@Floor", repo);
        Assert.Equal(2, repo.Split("insert.Parameters.Add(\"@Parent\", SqlDbType.VarChar, 20).Value = $\"{prefix}AREA\"").Length - 1);
    }

    [Fact]
    public void Storage_mode_is_created_once_with_parent_and_child_without_changing_existing_data()
    {
        var source = Read("src/02_Data/AMES.Data/Repositories/WarehouseRepository.LocationStorage.cs");
        source = source[..source.IndexOf("public void RenameLocationMapArea", StringComparison.Ordinal)];
        Assert.Contains("BeginTransaction(IsolationLevel.Serializable)", source);
        Assert.Contains("Storage mode can only be set when creating a new area", source);
        Assert.True(source.IndexOf("Area already exists.", StringComparison.Ordinal) < source.IndexOf("INSERT dbo.MD_CodeItem", StringComparison.Ordinal));
        Assert.Contains("@Area,1,1,@Actor", source);
        Assert.Contains("@Area,2,@Parent,1,@Actor", source);
        Assert.Contains("Attribute1,Attribute2", source);
        Assert.DoesNotContain("DELETE ", source);
        Assert.DoesNotContain("UPDATE dbo.", source);
        Assert.DoesNotContain("INSERT dbo.WH_Inventory", source);
        Assert.DoesNotContain("ActiveFlag=0", source);
        Assert.Equal(2, Read("src/02_Data/AMES.Data/Repositories/WarehouseRepository.cs")
            .Split("COALESCE(Attribute2,'GRID')<>'OPEN'").Length - 1);
    }

    [Fact]
    public void Map_excludes_parents_and_keeps_gridless_and_existing_grid_locations_visible()
    {
        Assert.Contains("x.LocationLevel == MasterDataRepository.LocationLevelDetail",
            Read("src/06_Web/AMES.Web/Components/Shared/LocationMapDatabase.cs"));
        var source = Read("src/06_Web/AMES.Web/Components/Shared/LocationMapExplorer.razor");
        Assert.Contains("RenderStorageCards(UngridLocations)", source);
        Assert.Contains("Data=\"GridLocations\"", source);
        Assert.Contains("(IsOpenStorage || Same(x.Floor, _floor))", source);
        Assert.Contains("GridLocations.FirstOrDefault", source);
        Assert.Contains("!CanEdit || !CanAddHierarchy", source);
        Assert.DoesNotContain("Storage Settings", source);
        Assert.DoesNotContain("SaveLocationMapStorage", source);
        Assert.Contains("@if (_dialog == \"Area\")", source);
        Assert.Contains("Storage mode cannot be changed after creation.", source);
        Assert.Contains("Warehouse.CreateLocationMapArea(_warehouse!, code, name, _storageMode)", source);
    }

    [Theory]
    [InlineData("", "MAT_AREA", "Materials")]
    [InlineData("EOS", "", "Materials")]
    [InlineData("EOS", "MAT_AREA", "")]
    [InlineData("EOS", "MAT_AREA", " ")]
    public void Invalid_area_names_are_rejected_without_a_database(string warehouse, string area, string name)
    {
        var repository = new WarehouseRepository(new AmesConnectionFactory("invalid connection string"));
        Assert.Throws<ArgumentException>(() => repository.RenameLocationMapArea(warehouse, area, name));
        Assert.Throws<ArgumentException>(() => repository.RenameLocationMapArea("EOS", "MAT_AREA", new string('A', 61)));
    }

    [Fact]
    public void Rename_only_updates_the_area_name_and_audit_fields_in_the_selected_warehouse()
    {
        var source = Read("src/02_Data/AMES.Data/Repositories/WarehouseRepository.LocationStorage.cs");
        var rename = source[source.IndexOf("public void RenameLocationMapArea", StringComparison.Ordinal)..];
        Assert.Contains("CodeName=@Name,ModifiedBy=@Actor,ModifiedTS=SYSDATETIME()", rename);
        Assert.Contains("W.CodeID=A.ParentCodeID", rename);
        Assert.Contains("A.CodeValue=@Area AND W.CodeValue=@Wh", rename);
        Assert.DoesNotContain("Attribute1", rename);
        Assert.DoesNotContain("Attribute2", rename);
        Assert.DoesNotContain("MD_Location", rename.Replace("MD_Location.CreatedBy", "Actor"));
        Assert.DoesNotContain("WH_Inventory", rename);
        var page = Read("src/06_Web/AMES.Web/Components/Shared/LocationMapExplorer.razor");
        Assert.Contains("OpenHierarchyName(row)", page);
        Assert.Contains("!_editing || !CanEdit || !CanAddHierarchy", page);
        Assert.Contains("Warehouse.RenameLocationMapArea(_warehouse!, _code, _name)", page);
        Assert.Contains("Audit.Updated(\"WH-003\", \"MD_CodeItem\"", page);
    }

    [Theory]
    [InlineData("", "Warehouse")]
    [InlineData(" ", "Warehouse")]
    [InlineData("EOS", "")]
    [InlineData("EOS", " ")]
    public void Invalid_warehouse_names_are_rejected_without_a_database(string warehouse, string name)
    {
        var repository = new WarehouseRepository(new AmesConnectionFactory("invalid connection string"));
        Assert.Throws<ArgumentException>(() => repository.RenameLocationMapWarehouse(warehouse, name));
        Assert.Throws<ArgumentException>(() => repository.RenameLocationMapWarehouse("EOS", new string('A', 61)));
    }

    [Fact]
    public void Warehouse_rename_is_name_only_and_uses_the_shared_edit_dialog()
    {
        var source = Read("src/02_Data/AMES.Data/Repositories/WarehouseRepository.LocationStorage.cs");
        var rename = source[source.IndexOf("public void RenameLocationMapWarehouse", StringComparison.Ordinal)..];
        Assert.Contains("SET CodeName=@Name,ModifiedBy=@Actor,ModifiedTS=SYSDATETIME()", rename);
        Assert.Contains("WHERE GroupCode='WH_CODE' AND CodeValue=@Wh", rename);
        Assert.DoesNotContain("Attribute1", rename);
        Assert.DoesNotContain("Attribute2", rename);
        Assert.DoesNotContain("ParentCodeID", rename);
        Assert.DoesNotContain("INSERT", rename);
        Assert.DoesNotContain("DELETE", rename);
        Assert.DoesNotContain("WH_Inventory", rename);
        var page = Read("src/06_Web/AMES.Web/Components/Shared/LocationMapExplorer.razor");
        Assert.Contains("Warehouse.RenameLocationMapWarehouse(_code, _name)", page);
        Assert.Contains("_dialog == \"WarehouseName\" && (!_editing || _stage != MapStage.Warehouse)", page);
        Assert.Contains("Edit Warehouse Name", page);
    }

    [Fact]
    public void Delete_and_update_wait_for_confirmation_before_writes()
    {
        var page = Read("src/06_Web/AMES.Web/Components/Shared/LocationMapExplorer.razor");
        var delete = page[page.IndexOf("private async Task DeleteCard", StringComparison.Ordinal)..page.IndexOf("private void OpenDialog", StringComparison.Ordinal)];
        Assert.Contains("Are you sure you want to delete", delete);
        Assert.Contains("}) != true) return;", delete);
        Assert.True(delete.IndexOf("impact.StockRows > 0", StringComparison.Ordinal) < delete.IndexOf("Dialog.Confirm", StringComparison.Ordinal));
        Assert.True(delete.IndexOf("Dialog.Confirm", StringComparison.Ordinal) < delete.IndexOf("Warehouse.DeleteLocationMapScope", StringComparison.Ordinal));
        Assert.Contains("|| _saving) return;", delete);
        Assert.Contains("finally { _saving = false; }", delete);
        var save = page[page.IndexOf("private async Task SaveDialog", StringComparison.Ordinal)..page.IndexOf("private void SelectStageRow", StringComparison.Ordinal)];
        Assert.Contains("_dialog is \"WarehouseName\" or \"AreaName\" or \"Grid\"", save);
        Assert.Contains("}) != true) return;", save);
        Assert.True(save.IndexOf("Dialog.Confirm", StringComparison.Ordinal) < save.IndexOf("Warehouse.RenameLocationMapWarehouse", StringComparison.Ordinal));
        Assert.True(save.IndexOf("Dialog.Confirm", StringComparison.Ordinal) < save.IndexOf("Warehouse.SetLocationMapGrid", StringComparison.Ordinal));
        var master = Read("src/06_Web/AMES.Web/Components/Pages/Md/Fd/Location.razor");
        var edit = master[master.IndexOf("async Task SaveEdit()", StringComparison.Ordinal)..];
        Assert.Contains("if (!saveConfirmed && await Dialog.Confirm", edit);
        Assert.Contains("saveConfirmed = true", edit);
        Assert.True(edit.LastIndexOf("Dialog.Confirm", StringComparison.Ordinal) < edit.IndexOf("MD.UpdateLocation", StringComparison.Ordinal));
    }

    [Fact]
    public void Scope_deletion_removes_locations_and_codes_without_deleting_stock_or_history()
    {
        var source = Read("src/02_Data/AMES.Data/Repositories/WarehouseRepository.cs");
        var delete = source[source.IndexOf("public void DeleteLocationMapScope", StringComparison.Ordinal)..source.IndexOf("public bool LocationExists", StringComparison.Ordinal)];
        Assert.Contains("BeginTransaction(IsolationLevel.Serializable)", delete);
        Assert.Contains("DELETE l", delete);
        Assert.DoesNotContain("ActiveFlag=0", delete);
        Assert.Contains("DELETE FROM dbo.MD_CodeItem", delete);
        foreach (var guard in new[] { "THROW 51022", "THROW 51023", "THROW 51030", "THROW 51031", "THROW 51032" })
            Assert.True(delete.IndexOf(guard, StringComparison.Ordinal) < delete.IndexOf("DELETE l", StringComparison.Ordinal));
        Assert.DoesNotContain("DELETE FROM dbo.WH_Inventory", delete);
        Assert.DoesNotContain("DELETE FROM dbo.WH_InventoryTransaction", delete);
        Assert.Contains("tx.Commit()", delete);
    }

    [Fact]
    public void Edit_mode_ends_after_successful_actions_but_not_after_cancellation_or_failure()
    {
        var page = Read("src/06_Web/AMES.Web/Components/Shared/LocationMapExplorer.razor");
        var delete = page[page.IndexOf("private async Task DeleteCard", StringComparison.Ordinal)..page.IndexOf("private void OpenDialog", StringComparison.Ordinal)];
        var save = page[page.IndexOf("private async Task SaveDialog", StringComparison.Ordinal)..page.IndexOf("private void SelectStageRow", StringComparison.Ordinal)];
        Assert.True(delete.IndexOf("Warehouse.DeleteLocationMapScope", StringComparison.Ordinal) < delete.IndexOf("_editing = false;", StringComparison.Ordinal));
        Assert.True(save.IndexOf("Warehouse.SetLocationMapGrid", StringComparison.Ordinal) < save.IndexOf("_editing = false;", StringComparison.Ordinal));
        foreach (var action in new[] { delete, save })
        {
            Assert.Equal(1, action.Split("_editing = false;").Length - 1);
            Assert.True(action.IndexOf("}) != true) return;", StringComparison.Ordinal) < action.IndexOf("_editing = false;", StringComparison.Ordinal));
            Assert.True(action.IndexOf("_editing = false;", StringComparison.Ordinal) < action.IndexOf("await Refresh()", StringComparison.Ordinal));
            Assert.DoesNotContain("_editing = false;", action[action.IndexOf("catch (Exception", StringComparison.Ordinal)..]);
        }
    }

    [SkippableFact]
    public void Area_storage_modes_and_parent_choices_load_from_the_database_without_writes()
    {
        var connection = Environment.GetEnvironmentVariable("AMES_TEST_CONN");
        Skip.If(string.IsNullOrWhiteSpace(connection), "Set AMES_TEST_CONN for read-only location verification.");
        var factory = new AmesConnectionFactory(connection!);
        var areas = new WarehouseRepository(factory).ListWarehouseAreas();
        Assert.All(areas, area => Assert.Contains(area.StorageMode, new[] { "GRID", "OPEN" }));
        var locations = new MasterDataRepository(factory).ListLocations();
        Assert.All(locations, location => Assert.InRange(location.LocationLevel, (byte)1, (byte)2));
    }

    private static string Read(string path, [System.Runtime.CompilerServices.CallerFilePath] string current = "")
        => File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(current)!, "../../..", path)));
}
