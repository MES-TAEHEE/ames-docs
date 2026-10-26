using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AMES.Tablet.Services;
using AMES.Contracts.Dto;
using Xunit;

namespace AMES.Api.Tests;

public class TabletCompatibilityTests
{
    [Fact]
    public void Tablet_webview_stays_inside_system_bars_and_keyboard_safe_area()
    {
        var page = System.Xml.Linq.XDocument.Parse(ReadTablet("MainPage.xaml"));
        Assert.Equal("All", (string?)page.Root!.Attribute("SafeAreaEdges"));
    }

    [Fact]
    public void Tablet_map_keeps_split_search_list_zoom_floor_selection_and_location_scroll()
    {
        var page = ReadTablet("Components/Pages/Home.razor");
        Assert.Contains("part-search-combobox", page);
        Assert.Contains("result-table", page);
        Assert.Contains("selected-detail", page);
        Assert.Contains("zoom-controls", page);
        Assert.Contains("ZoomIn", page);
        Assert.Contains("ZoomOut", page);
        Assert.Contains("ShowFloorSelector", page);
        Assert.Contains("tabletRack.scrollTo", page);
        Assert.DoesNotContain("Server API update required", page);
        var css = ReadTablet("wwwroot/app.css");
        Assert.Contains("inset: 0 0 0 36%", css);
        Assert.Contains("inset: 0 64% 0 0", css);
    }

    [Fact]
    public void Employee_selection_uses_a_dialog_without_auto_focusing_a_keyboard_input()
    {
        var login = ReadTablet("Components/Pages/Login.razor");
        Assert.Contains("OpenAsync<EmployeePickerDialog>", login);
        Assert.Contains("AutoFocusFirstElement = false", login);
        Assert.DoesNotContain("<RadzenDropDown", login);
        var picker = ReadTablet("Components/EmployeePickerDialog.razor");
        Assert.Contains("RowSelect=\"SelectEmployee\"", picker);
        Assert.Contains("Dialog.Close(employee.EmployeeNo)", picker);
        Assert.DoesNotContain("LoginAsync", picker);
        Assert.DoesNotContain("autofocus", picker, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadTablet(string relative, [System.Runtime.CompilerServices.CallerFilePath] string source = "") =>
        File.ReadAllText(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source)!, "../../08_Tablet/AMES.Tablet", relative)));

    private static TabletInventoryApi.InventoryRow Row(string? x, string? y, string? floor) =>
        new("MA02B1F3", "Storage", "MAT_AREA", "EOS", "MAT_AREA", null, x, y, floor,
            "LOT-1", "PART-1", "Part", 2.5m, "KG");

    [Theory]
    [InlineData("A1", "02")]
    [InlineData("A2", "100")]
    [InlineData("02", "B1")]
    public void Uses_exact_web_axes_without_swapping_coordinates(string x, string y)
    {
        var row = Row(x, y, "3");
        Assert.True(TabletMapCoordinates.HasGrid(row));
        Assert.Equal(x, TabletMapCoordinates.Column(row));
        Assert.Equal(y, TabletMapCoordinates.Row(row));
    }

    [Theory]
    [InlineData("01F", "1")]
    [InlineData("F3", "3")]
    [InlineData("5", "5")]
    [InlineData(null, "-")]
    public void Uses_floor_field_not_floor_like_text_in_location_id(string? floor, string expected) =>
        Assert.Equal(expected, TabletMapCoordinates.Floor(Row("02", "B1", floor)));

    [Fact]
    public void Orders_y_axis_A1_to_Z1_before_A2()
    {
        Assert.True(TabletMapCoordinates.AxisOrder("A1") < TabletMapCoordinates.AxisOrder("B1"));
        Assert.True(TabletMapCoordinates.AxisOrder("Z1") < TabletMapCoordinates.AxisOrder("A2"));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, true)]
    [InlineData(HttpStatusCode.InternalServerError, false)]
    [InlineData(HttpStatusCode.NotFound, false)]
    public async Task Failed_inventory_never_fabricates_demo_stock(HttpStatusCode status, bool expired)
    {
        using var http = Client(_ => Task.FromResult(new HttpResponseMessage(status)));
        var snapshot = await new TabletInventoryApi(http).LoadAsync();
        Assert.Empty(snapshot.Rows);
        Assert.NotNull(snapshot.Message);
        Assert.Equal(expired, snapshot.SessionExpired);
    }

    [Fact]
    public async Task Empty_and_actual_inventory_are_preserved()
    {
        using var emptyHttp = Client(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new TabletLocationMapDto([], [], [])) }));
        Assert.Empty((await new TabletInventoryApi(emptyHttp).LoadAsync()).Rows);
        var map = new TabletLocationMapDto([new("EOS", "EOS", "MAT_AREA", "Materials", "GRID")],
            [new("MA02B1F3", "Storage", "EOS", "MAT_AREA", "B1", "02", "F3", 100)],
            [new("MA02B1F3", "LOT-1", "PART-1", "Part", 2.5m, "KG", 1.5m)]);
        using var http = Client(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(map) }));
        Assert.Equal(2.5m, Assert.Single((await new TabletInventoryApi(http).LoadAsync()).Rows).Qty);
    }

    [Fact]
    public void Preserves_open_area_empty_locations_capacity_and_defect_only_stock()
    {
        var snapshot = TabletInventoryApi.FromMap(new(
            [new("EOS", "EOS", "OPEN", "Bulk Storage", "OPEN")],
            [new("OPEN-1", "Bulk", "EOS", "OPEN", null, null, null, 100), new("EMPTY-1", "Empty", "EOS", "OPEN", null, null, null, null)],
            [new("OPEN-1", "LOT-1", "PART-1", "Part", 0m, "KG", 3m)]));
        var defect = Assert.Single(snapshot.Rows, x => x.LocationNo == "OPEN-1");
        Assert.Equal(3m, defect.DefectQty);
        Assert.Equal(100m, defect.Capacity);
        Assert.Equal("OPEN", defect.StorageMode);
        Assert.False(TabletMapCoordinates.HasGrid(defect));
        Assert.Contains(snapshot.Rows, x => x.LocationNo == "EMPTY-1" && x.LotNo is null);
        Assert.Equal("Bulk Storage", Assert.Single(snapshot.Areas!).AreaName);
    }

    [Fact]
    public async Task Older_server_fallback_excludes_parent_locations_and_does_not_invent_storage_mode()
    {
        using var http = Client(request => Task.FromResult(request.RequestUri!.AbsolutePath switch
        {
            "/api/tablet/location-map" => new HttpResponseMessage(HttpStatusCode.NotFound),
            "/api/tablet/inventory" => new(HttpStatusCode.OK) { Content = JsonContent.Create(new[] { Row("B1", "02", "F3"), Row(null, null, null) with { LocationNo = "TMP-MAT_AREA", LotNo = null, PartNo = null } }) },
            "/api/wh/locations" => new(HttpStatusCode.OK) { Content = JsonContent.Create(new[] { new { locationId = "MA02B1F3", warehouseName = "EOS", areaName = "Material Storage", capacity = 100m } }) },
            _ => throw new Exception("Unexpected API route")
        }));
        var snapshot = await new TabletInventoryApi(http).LoadAsync();
        var row = Assert.Single(snapshot.Rows);
        Assert.Equal(100m, row.Capacity);
        Assert.Null(row.StorageMode);
        Assert.Null(snapshot.Message);
    }

    [Theory]
    [InlineData(100, 100, true)]
    [InlineData(101, 100, true)]
    [InlineData(99, 100, false)]
    [InlineData(1, 0, false)]
    public void Full_location_matches_web_capacity_rule(int qty, int capacity, bool expected)
    {
        Assert.Equal(expected, TabletMapCoordinates.IsFull(qty, capacity));
        Assert.Equal(AMES.Data.Services.LocationCapacity.IsFull(qty, capacity), TabletMapCoordinates.IsFull(qty, capacity));
    }

    [Fact]
    public async Task Employee_picker_uses_existing_public_employee_endpoint()
    {
        using var http = Client(request => {
            Assert.Equal("/api/auth/employees", request.RequestUri!.AbsolutePath);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new[] { new LoginEmployeeDto("PTEST", "Tester") }) });
        });
        Assert.Equal("PTEST", Assert.Single(await new TabletAuthService(http, new()).EmployeesAsync()).EmployeeNo);
    }

    [Fact]
    public async Task Network_failure_is_not_sample_inventory()
    {
        using var http = Client(_ => throw new HttpRequestException("offline"));
        var snapshot = await new TabletInventoryApi(http).LoadAsync();
        Assert.Empty(snapshot.Rows);
        Assert.Contains("Tailscale", snapshot.Message);
    }

    [Fact]
    public async Task Late_logout_does_not_clear_a_new_login()
    {
        var pending = new TaskCompletionSource<HttpResponseMessage>();
        using var http = Client(request => {
            Assert.Equal("old", request.Headers.Authorization?.Parameter);
            Assert.Equal("/api/auth/logout", request.RequestUri!.AbsolutePath);
            return pending.Task;
        });
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "old");
        var logout = new TabletAuthService(http, new()).LogoutAsync();
        Assert.Null(http.DefaultRequestHeaders.Authorization);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "new");
        pending.SetResult(new(HttpStatusCode.NoContent));
        await logout;
        Assert.Equal("new", http.DefaultRequestHeaders.Authorization?.Parameter);
    }

    private static HttpClient Client(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) =>
        new(new Handler(send)) { BaseAddress = new("http://test.local") };

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
}
