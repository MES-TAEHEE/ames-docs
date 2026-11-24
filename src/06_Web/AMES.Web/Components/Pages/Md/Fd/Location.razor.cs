using AMES.Data.Repositories;
using Microsoft.AspNetCore.Components.Web;
using Radzen;

namespace AMES.Web.Components.Pages.Md.Fd;

public partial class Location
{
    string _gridDialog="",_gridError="",_gridPrefix="",_createWh="",_createArea="",_createName="",_createType="RACK";
    int _gridX=1,_gridY=1,_gridF=1;
    bool _gridChanged,_gridCompatible;
    List<WarehouseRepository.WarehouseMasterRow> _gridWarehouses=[];
    List<WarehouseRepository.WarehouseAreaRow> _gridAreas=[];
    IEnumerable<DropDownItem> GridWarehouses => _gridWarehouses.Select(w=>new DropDownItem{Value=w.WhCode,Text=$"{w.WhCode} · {w.WhName}"});
    IEnumerable<DropDownItem> GridAreas => _gridAreas.Where(a=>a.UseYn && a.WhCode==_createWh && a.StorageMode!="OPEN").Select(a=>new DropDownItem{Value=a.AreaCode,Text=$"{a.AreaCode} · {a.AreaName}"});
    IEnumerable<DropDownItem> GridTypes => new[]{new DropDownItem{Value="RACK",Text=L["MD.Location.Grid.Material"]},new DropDownItem{Value="FG",Text=L["MD.Location.Grid.Finished"]}};
    IEnumerable<MasterDataRepository.LocationRow> ChildrenOf(string parent) => _allRows.Where(r=>r.ParentLocationID==parent);
    int GridRemovalCount => _sel is null ? 0 : ChildrenOf(_sel.LocationID).Count(r=>!LocationMapNaming.InGrid(r.Aisle,r.Bay,_gridY,_gridX) || LocationMapNaming.FloorNumber(r.Slot)>_gridF);
    void LoadGridFor(MasterDataRepository.LocationRow parent)
    {
        _gridChanged=false;
        var children=ChildrenOf(parent.LocationID).ToList();
        _gridPrefix=parent.LocationID.EndsWith("AREA",StringComparison.OrdinalIgnoreCase) && parent.LocationID.Length==6
            ? parent.LocationID[..2] : _areaCodes.FirstOrDefault(a=>a.CodeValue==parent.AreaCode)?.Attribute1 ?? "";
        _gridCompatible=true;
        foreach(var child in children)
        {
            var suffix=$"{child.Bay}{child.Aisle}{child.Slot}";
            if (string.IsNullOrEmpty(child.Bay) || LocationMapNaming.YOrder(child.Aisle)==0 || LocationMapNaming.FloorNumber(child.Slot)==0
                || !child.LocationID.EndsWith(suffix,StringComparison.Ordinal) || child.LocationID.Length-suffix.Length!=2)
            { _gridCompatible=false; break; }
            var prefix=child.LocationID[..2];
            if(children[0]==child) _gridPrefix=prefix;
            else if(_gridPrefix!=prefix) { _gridCompatible=false; break; }
        }
        _gridX=Math.Max(int.TryParse(parent.Bay,out var px)?px:1,children.Select(c=>int.TryParse(c.Bay,out var x)?x:1).DefaultIfEmpty(1).Max());
        _gridY=Math.Max(LocationMapNaming.YOrder(parent.Aisle),children.Select(c=>LocationMapNaming.YOrder(c.Aisle)).DefaultIfEmpty(1).Max());
        _gridF=Math.Max(LocationMapNaming.FloorNumber(parent.Slot),children.Select(c=>LocationMapNaming.FloorNumber(c.Slot)).DefaultIfEmpty(1).Max());
        _gridY=Math.Max(1,_gridY); _gridF=Math.Max(1,_gridF);
        if(children.Count==0) _gridPrefix="";
    }
    async Task OpenGridCreate()
    {
        if(!_canEdit || _busy) return;
        _busy=true;
        try
        {
            var choices=await Task.Run(()=>(Warehouses:Warehouse.ListWarehouses(),Areas:Warehouse.ListWarehouseAreas()));
            _gridWarehouses=choices.Warehouses; _gridAreas=choices.Areas;
            _createWh=_gridWarehouses.FirstOrDefault()?.WhCode ?? "";
            _createName=""; _createType="RACK"; _gridError=""; _gridDialog="create";
            _gridX=_gridY=_gridF=1;
            ChangeGridWarehouse(null);
        }
        catch(Exception ex) { ShowMsg(ex.Message,true); }
        finally { _busy=false; }
    }
    void ChangeGridWarehouse(object? _) { _createArea=GridAreas.FirstOrDefault()?.Value ?? ""; ChangeGridArea(null); }
    void ChangeGridArea(object? _)
    {
        _gridPrefix=_gridAreas.FirstOrDefault(a=>a.WhCode==_createWh && a.AreaCode==_createArea)?.LocationPrefix ?? "";
        _createType=_createArea.Equals("FG_AREA",StringComparison.OrdinalIgnoreCase) ? "FG" : "RACK";
    }
    void OnGridBgClick() { if(_bgMouseDownOnSelf) CloseGridDialog(); _bgMouseDownOnSelf=false; }
    void CloseGridDialog() { if(!_busy) { _gridDialog=""; _gridError=""; } }
    void GridDialogKeyDown(KeyboardEventArgs e) { if(e.Key=="Escape") CloseGridDialog(); }
    async Task SaveGridDialog()
    {
        if(!_canEdit || _busy) return;
        _gridError=""; _busy=true;
        try
        {
            if(!await PermSvc.RecheckEditAsync(AuthPrvd,"md/fd/location")) throw new InvalidOperationException(L["MD.Location.Grid.Denied"]);
            WarehouseRepository.PlanLocationGrid("AA",_gridX,_gridY,_gridF);
            if(string.IsNullOrWhiteSpace(_createName) || !GridAreas.Any(a=>a.Value==_createArea)) throw new InvalidOperationException(L["MD.Location.Grid.AreaRequired"]);
            if(await Dialog.Confirm(L["MD.Location.Grid.Confirm",_createName,(long)_gridX*_gridY*_gridF],L["Btn.Confirm"],
                new ConfirmOptions{OkButtonText=L["Btn.Confirm"],CancelButtonText=L["Btn.Cancel"]})!=true) return;
            var setup=await Task.Run(()=>Warehouse.CreateLocationGrid(_createName.Trim(),_createWh,_createArea,_gridX,_gridY,_gridF,_createType,_actor));
            Audit.Log("MD-018","CREATE_GRID","MD_Location",setup.ParentId,null,setup);
            _gridDialog=""; await ReloadRestorePage();
            ShowMsg(L["MD.Location.Grid.Saved"],false);
        }
        catch(Microsoft.Data.SqlClient.SqlException ex) { _gridError=SqlErr(ex,""); }
        catch(ArgumentException) { _gridError=L["MD.Location.Grid.Invalid"]; }
        catch(Exception ex) { _gridError=ex.Message; }
        finally { _busy=false; }
    }
}
