using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AMES.Contracts.Dto;
using AMES.Pda.Services;
using Xunit;
using Xunit.Abstractions;

namespace AMES.Api.Tests;

// Explicit opt-in, no seed/reset/worker invocation. Only login/logout and PDA reads.
public sealed class PdaLiveApiAuditTests(ITestOutputHelper output)
{
    [LivePdaFact]
    public async Task Deployed_API_matches_the_actual_PDA_client_contracts()
    {
        using var http = Client();
        var auth = await SignIn(http);
        var failures = new List<string>();
        async Task Check(string name, Func<Task> action)
        {
            try { await action(); output.WriteLine("PASS " + name); }
            catch (Exception ex) { failures.Add(name + ": " + ex.Message); output.WriteLine("FAIL " + name); }
        }
        try
        {
            var sp = new SparePartsApi(http, auth);
            var fg = new FinishedGoodsApi(http, auth);
            var wh = new WarehouseApi(http, auth, sp, fg);
            var mnt = new MaintenanceApi(http, auth);
            var today = (await wh.DatabaseTimeAsync()).Date;
            await Check("UOM list", async () => Assert.NotEmpty(await wh.UomsAsync()));
            await Check("Adjustment reasons", async () => Assert.NotEmpty(await wh.CodeItemsAsync("INV_ADJUST_REASON")));
            await Check("Supplier deliveries", async () => Assert.NotNull(await wh.SupplierDeliveriesAsync(today.AddMonths(-1), today.AddMonths(1))));
            await Check("WH inventory and part drilldown", async () =>
            {
                var rows = await wh.WhInventoryAsync(); Assert.NotNull(rows);
                foreach (var row in rows.Take(3))
                {
                    Assert.NotNull(await wh.WhInventoryPartLotsAsync(row.ItemNo));
                    Assert.NotNull(await wh.WhInventoryLocationsAsync(row.ItemNo));
                }
            });
            await Check("FG inventory scope", async () => Assert.NotNull(await wh.WhInventoryAsync(stockKind: "FG")));
            await Check("WH location list and contents", async () =>
            {
                var rows = await wh.WhLocationsAsync(); Assert.NotNull(rows);
                foreach (var row in rows.Where(r => r.TotalQty > 0).Take(3))
                    Assert.NotNull(await wh.WhLocationMapItemsAsync(row.LocationId));
            });
            await Check("Picking orders and lines", async () =>
            {
                var orders = await wh.WhOpenPickingOrdersAsync(); Assert.NotNull(orders);
                foreach (var order in orders.Take(3))
                {
                    Assert.NotNull(await wh.WhReleaseLinesAsync(order.PickSlipNo));
                    Assert.NotNull(await wh.WhReleaseFifoLotsAsync(order.PickSlipNo));
                }
                Assert.NotNull(await wh.WhOpenPickingPartsAsync());
            });
            await Check("Transactions page and DCR parts", async () =>
            {
                var page = await wh.TransactionPageAsync(null, today.AddMonths(-1), today.AddMonths(1));
                Assert.NotNull(page); Assert.InRange(page.Items.Count, 0, 100);
                foreach (var row in page.Items.Where(i => i.DeliveryId.HasValue).Take(3))
                    Assert.NotNull(await wh.TransactionPartsAsync(row.DeliveryId!.Value));
            });
            await Check("SP master and serial drilldown", async () =>
            {
                var rows = await sp.MastersAsync(); Assert.NotNull(rows);
                foreach (var row in rows.Take(3))
                {
                    Assert.NotNull(await sp.SpItemAsync(row.EosSpNo));
                    Assert.NotNull(await sp.LabelsAsync(row.EosSpNo));
                    Assert.NotNull(await sp.InventoryLocationsAsync(row.EosSpNo));
                }
            });
            await Check("SP inventory", async () => Assert.NotNull(await sp.InventoryAsync()));
            await Check("SP LOTs", async () => Assert.NotNull(await sp.LotStatusesAsync()));
            await Check("SP locations", async () => Assert.NotNull(await sp.LocationsAsync()));
            await Check("SP work orders", async () => Assert.NotNull(await sp.OpenWorkOrdersAsync()));
            await Check("SP transactions", async () => Assert.NotNull(await sp.TransactionsAsync()));
            await Check("ASSY Ready", async () => Assert.NotNull(await fg.AssyReadyPalletsAsync()));
            await Check("FG waiting", async () => Assert.NotNull(await fg.FgPutAwayWaitingAsync()));
            await Check("FG inventory", async () => Assert.NotNull(await fg.FgInventoryAsync()));
            await Check("FG transactions", async () => Assert.NotNull(await fg.TransactionsAsync()));
            await Check("FG customers", async () => Assert.NotNull(await fg.FgOutboundCustomersAsync()));
            await Check("FG returns", async () => Assert.NotNull(await fg.FgReturnsAsync()));
            await Check("DCR list", async () => Assert.NotNull(await fg.OpenShippingDeliveriesAsync()));
            await Check("Maintenance board and work detail", async () =>
            {
                var board = await mnt.Board(today.AddMonths(-1), today.AddMonths(1), false, true);
                Assert.NotNull(board);
                foreach (var work in board.WorkOrders.Take(5)) Assert.Equal(work.Id, (await mnt.Detail(work.Id)).Work.Id);
            });
        }
        finally { using var logout = await http.PostAsync("/api/auth/logout", null); logout.EnsureSuccessStatusCode(); }
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [LivePdaFact]
    public async Task Deployed_API_rejects_bad_ranges_missing_barcodes_and_anonymous_requests_without_500s()
    {
        using var http = Client();
        var auth = await SignIn(http);
        var failures = new List<string>();
        var missing = "AUDIT-MISSING-" + Guid.NewGuid().ToString("N")[..12];
        var paths = new (string Path, int[] Allowed)[]
        {
            ("/api/wh/transaction-feed?pageSize=0", [400]),
            ("/api/wh/transaction-feed?pageSize=101", [400]),
            ("/api/wh/transaction-feed?beforeId=1", [400]),
            ("/api/wh/transaction-feed?from=2026-10-29&to=2026-10-01", [400]),
            ("/api/mnt/pda/board?from=2026-10-29&to=2026-10-01", [400]),
            ("/api/mnt/pda/board?from=2020-01-01&to=2026-10-29", [400]),
            ("/api/mnt/pda/work-orders/-1", [400, 404]),
            ("/api/wh/sp/item?eosSpNo=" + missing, [404]),
            ("/api/wh/sp/adjust/scan?scanText=" + missing, auth.Session!.IsAdmin ? [404] : [403, 404]),
            ("/api/wh/location/scan?locationId=" + missing, [400, 404]),
            ("/api/wh/putaway/scan?barcode=" + missing, [400, 404]),
            ("/api/fg/putaway/scan?barcode=" + missing, [200, 400, 404]),
            ("/api/fg/outbound/scan?barcode=" + missing, [400, 404]),
            ("/api/fg/return/scan?barcode=" + missing, [200, 400, 404]),
            ("/api/fg/shipping/delivery?barcode=" + missing, [400, 404]),
            ("/api/fg/pallet-return/scan?barcode=" + missing, [400, 404])
        };
        try
        {
            foreach (var (path, allowed) in paths)
            {
                using var response = await http.GetAsync(path);
                output.WriteLine($"{(int)response.StatusCode} {path.Split('?')[0]}");
                if (!allowed.Contains((int)response.StatusCode)) failures.Add($"{path.Split('?')[0]}: HTTP {(int)response.StatusCode}");
                if (response.IsSuccessStatusCode)
                {
                    using var body = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                    Assert.False(body.RootElement.GetProperty("success").GetBoolean());
                }
            }
            using var anonymous = Client();
            foreach (var path in new[] { "/api/wh/inventory", "/api/fg/inventory", "/api/wh/sp/master", "/api/mnt/pda/board", "/api/wh/transaction-feed" })
            {
                using var response = await anonymous.GetAsync(path);
                if (response.StatusCode != HttpStatusCode.Unauthorized) failures.Add($"Anonymous {path}: HTTP {(int)response.StatusCode}");
            }
        }
        finally { using var logout = await http.PostAsync("/api/auth/logout", null); logout.EnsureSuccessStatusCode(); }
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    internal static bool Enabled => Environment.GetEnvironmentVariable("AMES_PDA_LIVE_API") is
        "http://192.168.0.132:5210" or "http://localhost:5211" or "http://127.0.0.1:5211";
    internal static HttpClient Client()
    {
        if (!Enabled) throw new InvalidOperationException("Explicit approved PDA API target is required.");
        return new() { BaseAddress = new(Environment.GetEnvironmentVariable("AMES_PDA_LIVE_API")!), Timeout = TimeSpan.FromSeconds(20) };
    }
    internal static async Task<AuthState> SignIn(HttpClient http)
    {
        using var response = await http.PostAsJsonAsync("/api/auth/barcode-login", new { barcode = "EOS*PTEST*", terminalId = "PDA-AUDIT-1029" });
        response.EnsureSuccessStatusCode();
        var login = await response.Content.ReadFromJsonAsync<PdaApi.LoginRes>();
        Assert.False(string.IsNullOrWhiteSpace(login?.Token), "PTEST test login failed.");
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.Token);
        var session = await http.GetFromJsonAsync<PopSessionDto>("/api/auth/me");
        var auth = new AuthState(); auth.SignIn(login.Token, session!); return auth;
    }
    private sealed class LivePdaFactAttribute : FactAttribute
    {
        public LivePdaFactAttribute()
        {
            if (!Enabled)
                Skip = "Explicit approved server or isolated local API via AMES_PDA_LIVE_API is required.";
        }
    }
}
