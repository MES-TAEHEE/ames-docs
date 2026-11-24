using System.Data;
using AMES.Data.Repositories;
using AMES.Web.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using static AMES.Data.Repositories.WarehouseRepository;

namespace AMES.Web.Components.Shared;

public partial class FactoryLayoutMap
{
    private ElementReference _root, _viewport, _canvas;
    private IJSObjectReference? _module;
    private DotNetObjectReference<FactoryLayoutMap>? _reference;
    private List<FactoryMapRegion> _regions = [];
    private List<LocationMapStock> _stock = [];
    private List<LocationMapArea> _areas = [];
    private List<MasterDataRepository.LocationRow> _registered = [];
    private List<MapTarget> _targets = [];
    private bool _loading = true, _mapReady, _canEdit, _editing, _editorOpen, _saving, _confirmDelete, _redrawing, _disposed;
    private string? _error, _editorError, _detailError, _lastJsState;
    private string _name = "", _targetKey = "", _code = "", _stockSearch = "";
    private bool _exampleMode;
    private FactoryMapRegion? _original, _selected;
    private FactoryMapZone? _draft;
    private bool _detailLoading;
    private List<LocationMapStock> _detailStock = [];
    private int _detailRequest;
    private IEnumerable<LocationMapStock> FilteredStock => _detailStock.Where(x => string.IsNullOrWhiteSpace(_stockSearch)
        || new[] {x.LocationNo, x.LotNo, x.PartNo, x.PartName, x.VendorCode}.Any(s => s?.Contains(_stockSearch.Trim(), StringComparison.OrdinalIgnoreCase) == true));
    private int DuplicateCodeCount => _code.Trim().Length == 0 ? 0 : _regions.Count(x => x.Id != _original?.Id && Same(FactoryMapView.Code(x), _code.Trim()));
    private IEnumerable<FactoryMapRegion> LegendRegions => _regions
        .OrderBy(x => FactoryMapView.Code(x).Length).ThenBy(x => FactoryMapView.Code(x));

    private sealed record MapTarget(string Key, string Label, string Warehouse, string Area, string? Parent);
    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    private static string TargetKey(FactoryMapZone zone) => $"{zone.Warehouse}/{zone.Area}/{zone.Parent}";

    protected override async Task OnInitializedAsync()
    {
        _exampleMode = Environment.IsDevelopment() && new Uri(Navigation.BaseUri).IsLoopback;
        await Reload();
    }

    private async Task Reload()
    {
        _loading = true;
        _error = null;
        try
        {
            await Permissions.EnsureAsync(Authentication);
            _canEdit = Permissions.CanEdit("md/fd/location");
            if (!_canEdit) _editing = false;
            await ReadMapData();
            _regions = await Task.Run(Warehouse.ListFactoryMapRegions);
            if (_selected is not null)
            {
                _selected = _regions.FirstOrDefault(r => r.Id == _selected.Id);
                await RefreshDetail(false);
            }
        }
        catch (Exception ex) { _error = ex.Message; }
        finally { _loading = false; }
    }

    private async Task ReadMapData()
    {
        var data = await Task.Run(() =>
        {
            var map = LocationMapDatabase.Load(MD, Warehouse, finishedGoods: true);
            return (map, rows: MD.ListLocations());
        });
        (_areas, _, _stock) = data.map;
        _registered = data.rows;
        _targets = BuildTargets();
    }

    private List<MapTarget> BuildTargets()
    {
        // Separate parent grids: identical X/Y coordinates in different parents must never overlap.
        var parents = _registered.Where(x => x.ActiveFlag && x.LocationLevel == MasterDataRepository.LocationLevelPrimary)
            .ToDictionary(x => x.LocationID, StringComparer.OrdinalIgnoreCase);
        return _registered.Where(x => x.ActiveFlag && x.LocationLevel == MasterDataRepository.LocationLevelDetail && Same(x.LocationType, "FG"))
            .GroupBy(x => (x.WhCode, x.AreaCode, x.ParentLocationID))
            .Where(g => g.Key.ParentLocationID is null || parents.ContainsKey(g.Key.ParentLocationID))
            .Select(g =>
            {
                var area = _areas.FirstOrDefault(a => Same(a.WarehouseCode, g.Key.WhCode) && Same(a.AreaCode, g.Key.AreaCode));
                var parentName = g.Key.ParentLocationID is not null ? parents[g.Key.ParentLocationID].LocationName ?? g.Key.ParentLocationID : null;
                var label = string.Join(" / ", new[] { area?.WarehouseName ?? g.Key.WhCode, area?.AreaName ?? g.Key.AreaCode, parentName }.Where(x => !string.IsNullOrWhiteSpace(x)));
                return new MapTarget($"{g.Key.WhCode}/{g.Key.AreaCode}/{g.Key.ParentLocationID}", label, g.Key.WhCode, g.Key.AreaCode, g.Key.ParentLocationID);
            }).OrderBy(x => x.Label, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_disposed) return;
        try
        {
            if (firstRender)
            {
                _module = await JS.InvokeAsync<IJSObjectReference>("import", "./js/fg-factory-map.js");
                _reference = DotNetObjectReference.Create(this);
                await _module.InvokeVoidAsync("initialize", _root, _viewport, _canvas, _reference);
            }
            var state = $"{_editing && !_loading && !_editorOpen && _selected is null}/{_editorOpen || _selected is not null}";
            if (_module is not null && state != _lastJsState)
            {
                _lastJsState = state;
                await _module.InvokeVoidAsync("setState", _root, _editing && !_loading && !_editorOpen && _selected is null, _editorOpen || _selected is not null);
            }
        }
        catch (JSDisconnectedException) { }
        catch (JSException) { _error = L["FG.FactoryMap.MapError"]; await InvokeAsync(StateHasChanged); }
    }

    [JSInvokable]
    public Task MapReady(bool ready) => InvokeAsync(() =>
    {
        _mapReady = ready;
        if (!ready) _error = L["FG.FactoryMap.MapError"];
        StateHasChanged();
    });

    [JSInvokable]
    public Task RectangleDrawn(decimal x, decimal y, decimal width, decimal height) => InvokeAsync(() =>
    {
        if (!_editing || !_canEdit || _loading || _saving || _editorOpen || _selected is not null) return;
        if (!_redrawing) { _original = null; _name = ""; _targetKey = ""; _code = FactoryMapView.NextCode(_regions); }
        _redrawing = false;
        _draft = new("", "", null, x, y, width, height);
        _editorError = null;
        _confirmDelete = false;
        _editorOpen = true;
        StateHasChanged();
    });

    [JSInvokable]
    public Task CloseOverlay() => InvokeAsync(() =>
    {
        if (_saving) return;
        if (_editorOpen) CloseEditor();
        else if (_selected is not null) CloseDetail();
        else if (_redrawing) { _redrawing = false; _draft = null; }
        StateHasChanged();
    });

    private async Task ToggleEdit()
    {
        if (!_editing && !await CanSave()) return;
        _editing = !_editing;
        if (_editing) CloseDetail();
        _redrawing = false;
        _draft = null;
    }

    private async Task SelectRegion(FactoryMapRegion region)
    {
        if (_loading || _saving) return;
        if (_editing)
        {
            _original = region;
            _draft = region.Zone;
            _name = region.Name;
            _code = FactoryMapView.Code(region);
            _targetKey = TargetKey(region.Zone);
            _editorError = null;
            _confirmDelete = false;
            _redrawing = false;
            _editorOpen = true;
        }
        else
        {
            _selected = region;
            _stockSearch = "";
            await RefreshDetail();
        }
    }

    private Task RefreshDetail() => RefreshDetail(true);
    private async Task RefreshDetail(bool reload)
    {
        var selected = _selected;
        if (selected is null) return;
        var request = ++_detailRequest;
        _detailLoading = true;
        _detailError = null;
        try
        {
            if (reload && !_exampleMode) await ReadMapData();
            if (request != _detailRequest) return;
            if (_exampleMode)
                _detailStock = FactoryMapView.Examples(selected, _stock, AMES.Data.Services.DbClock.Today);
            else
            {
                if (!_targets.Any(t => Same(t.Key, TargetKey(selected.Zone))))
                    throw new InvalidOperationException(L["FG.FactoryMap.TargetChanged"]);
                _detailStock = FactoryMapView.Stock(_stock, _registered, selected);
            }
        }
        catch (Exception ex) { if (request == _detailRequest) _detailError = ex.Message; }
        finally { if (request == _detailRequest) _detailLoading = false; }
    }

    private void CloseDetail() { _selected = null; _detailRequest++; }
    private async Task ChangeExample(bool value) { _exampleMode = value; await RefreshDetail(); }
    private void SeparateCode() => _code = "";
    private void CloseEditor()
    {
        if (_saving) return;
        _editorOpen = _redrawing = _confirmDelete = false;
        _draft = null;
        _original = null;
    }
    private void Redraw() { _editorOpen = false; _redrawing = true; _confirmDelete = false; }

    private async Task<bool> CanSave()
    {
        _canEdit = await Permissions.RecheckEditAsync(Authentication, "md/fd/location");
        if (_canEdit) return true;
        _editing = false;
        _editorError = _error = L["WH.Picking.Err.NoPermission"];
        return false;
    }

    private async Task Save()
    {
        if (_saving || _draft is null) return;
        _saving = true;
        _editorError = null;
        try
        {
            if (!await CanSave()) return;
            var target = _targets.FirstOrDefault(x => Same(x.Key, _targetKey));
            if (target is null) { _editorError = L["FG.FactoryMap.SelectTarget"]; return; }
            var code = NormalizeFactoryMapCode(_code);
            if (DuplicateCodeCount > 0) { _editorError = L["FG.FactoryMap.DuplicateCode", code]; return; }
            if (string.IsNullOrWhiteSpace(_name)) _name = code.Length > 0 ? code : target.Label.Length > 80 ? target.Label[..80] : target.Label;
            if (_original is not null && Same(_name, FactoryMapView.Code(_original)) && code.Length > 0) _name = code;
            var zone = _draft with { Warehouse = target.Warehouse, Area = target.Area, Parent = target.Parent, Code = code };
            var actor = ActorCode.Of((await Authentication.GetAuthenticationStateAsync()).User, "system");
            var original = _original;
            var id = await Task.Run(() => Warehouse.SaveFactoryMapRegion(_name, zone, actor, original));
            Audit.Log("FG-01", original is null ? "CREATE" : "UPDATE", "SYS_Config", id, original, new { Name = _name, Zone = zone });
            _editorOpen = false;
            _draft = null;
            _original = null;
            _regions = await Task.Run(Warehouse.ListFactoryMapRegions);
        }
        catch (DuplicateNameException) { _editorError = L["FG.FactoryMap.DuplicateCode", _code.ToUpperInvariant()]; }
        catch (DBConcurrencyException) { _editorError = L["FG.FactoryMap.Changed"]; }
        catch (Exception ex) { _editorError = ex.Message; }
        finally { _saving = false; }
    }

    private async Task Delete()
    {
        if (_saving || _original is null) return;
        if (!_confirmDelete) { _confirmDelete = true; return; }
        _saving = true;
        _editorError = null;
        try
        {
            if (!await CanSave()) return;
            var original = _original;
            await Task.Run(() => Warehouse.DeleteFactoryMapRegion(original));
            Audit.Deleted("FG-01", "SYS_Config", original.Id, original);
            _editorOpen = false;
            _draft = null;
            _original = null;
            _regions = await Task.Run(Warehouse.ListFactoryMapRegions);
        }
        catch (DBConcurrencyException) { _editorError = L["FG.FactoryMap.Changed"]; }
        catch (Exception ex) { _editorError = ex.Message; }
        finally { _saving = false; }
    }

    private string RegionLabel(FactoryMapRegion region) => FactoryMapView.Code(region) is {Length: >0} code ? code : L["FG.FactoryMap.Unassigned"].Value;
    private bool SelectedGroup(FactoryMapRegion region) => _selected?.Id == region.Id;
    private string RegionTitle(FactoryMapRegion region) => RegionLabel(region) + " · " + region.Name;
    private static string RegionStyle(FactoryMapRegion region)
    {
        var z = region.Zone;
        return FormattableString.Invariant($"left:{z.X}%;top:{z.Y}%;width:{z.Width}%;height:{z.Height}%;--region-color:{FactoryMapView.Color(region)};");
    }
    private async Task Zoom(double factor) { if (_module is not null) await _module.InvokeVoidAsync("zoom", _root, factor); }
    private async Task Fit() { if (_module is not null) await _module.InvokeVoidAsync("fit", _root); }
    private async Task FullScreen() { if (_module is not null) await _module.InvokeVoidAsync("fullScreen", _root); }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        if (_module is not null)
        {
            try { await _module.InvokeVoidAsync("dispose", _root); await _module.DisposeAsync(); }
            catch (JSDisconnectedException) { }
        }
        _reference?.Dispose();
    }
}
