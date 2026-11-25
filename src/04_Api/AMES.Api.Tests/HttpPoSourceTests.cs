using System.Net;
using System.Text;
using AMES.Api.Workers.PoSync;
using AMES.Data.Services.PoSync;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace AMES.Api.Tests;

/// <summary>고객사 SRM WEBSRV_INQUERY_PO 요청 형식 — GET + 쿼리 6개(+인증). 원격은 모르는 매개변수를 400 으로 거부한다.</summary>
public class HttpPoSourceTests
{
    static readonly DateOnly From = new(2026, 7, 19);
    static readonly DateOnly To   = new(2026, 9, 17);

    static PoSyncSource Src(string url = "http://srm/Service/WEBSRV_INQUERY_PO.ashx", string? scheme = null, string? value = null)
        => new("SEMS", "Seoyon E-Hwa Manufacturing Savannah", "CUS-SAV", url, scheme, value, "7700", "7710", "310471", "1A7700", 30, -60, 0);

    static Dictionary<string, string> Query(HttpRequestMessage req)
        => QueryHelpers.ParseQuery(req.RequestUri!.Query).ToDictionary(kv => kv.Key, kv => kv.Value.ToString());

    [Fact]
    public void Builds_a_get_with_exactly_the_search_parameters()
    {
        using var req = HttpPoSource.BuildRequest(Src(), From, To);

        Assert.Equal(HttpMethod.Get, req.Method);
        Assert.Null(req.Content);
        Assert.Equal("http://srm/Service/WEBSRV_INQUERY_PO.ashx", req.RequestUri!.GetLeftPart(UriPartial.Path));
        Assert.Equal(new Dictionary<string, string>
        {
            ["CORCD"]       = "7700",
            ["BIZCD"]       = "7710",
            ["PURC_ORG"]    = "1A7700",
            ["VENDCD"]      = "310471",
            ["PO_DATE_BEG"] = "2026-07-19",
            ["PO_DATE_TO"]  = "2026-09-17",
        }, Query(req));
        Assert.Null(req.Headers.Authorization);
    }

    [Fact]
    public void Query_scheme_puts_the_key_in_the_query_string_not_a_header()
    {
        using var req = HttpPoSource.BuildRequest(Src(scheme: "Query:APIKEY", value: "k+y&=1"), From, To);

        var q = Query(req);
        Assert.Equal("k+y&=1", q["APIKEY"]);
        Assert.Equal(7, q.Count);
        Assert.Null(req.Headers.Authorization);
    }

    [Fact]
    public void Existing_query_in_the_url_is_kept()
    {
        using var req = HttpPoSource.BuildRequest(Src(url: "http://srm/x.ashx?LANG=EN"), From, To);

        var q = Query(req);
        Assert.Equal("EN", q["LANG"]);
        Assert.Equal("7700", q["CORCD"]);
    }

    [Theory]
    [InlineData("Bearer", "tok", "Bearer", "tok")]
    [InlineData("Basic", "u:p", "Basic", "dTpw")]
    public void Header_schemes_still_use_the_authorization_header(string scheme, string value, string expScheme, string expParam)
    {
        using var req = HttpPoSource.BuildRequest(Src(scheme: scheme, value: value), From, To);

        Assert.Equal(expScheme, req.Headers.Authorization!.Scheme);
        Assert.Equal(expParam, req.Headers.Authorization.Parameter);
        Assert.Equal(6, Query(req).Count);
    }

    [Theory]
    [InlineData("Query:")]
    [InlineData("Token")]
    public void Unusable_scheme_throws(string scheme)
        => Assert.Throws<InvalidOperationException>(() => HttpPoSource.BuildRequest(Src(scheme: scheme, value: "v"), From, To));

    sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Last;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Last = request;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    [Fact]
    public async Task Fetch_parses_the_root_array()
    {
        var handler = new StubHandler(HttpStatusCode.OK,
            """[{"PONO":"4100172432-10","PARTNO":"85875-PI000YGN","PO_QTY":144,"DELI_QTY":0,"PO_DATE":"2026-09-01","PO_DELI_DATE":"2026-09-01"}]""");
        var source = new HttpPoSource(new StubFactory(handler), new ConfigurationBuilder().Build());

        var rows = await source.FetchAsync(Src(scheme: "Query:APIKEY", value: "key"), From, To, CancellationToken.None);

        Assert.Equal("4100172432-10", Assert.Single(rows).PONO);
        Assert.Equal(HttpMethod.Get, handler.Last!.Method);
    }

    [Fact]
    public async Task Fetch_throws_with_the_server_message_on_error_status()
    {
        var handler = new StubHandler(HttpStatusCode.BadRequest, """{"error":"지원하지 않는 검색 매개변수가 있습니다."}""");
        var source = new HttpPoSource(new StubFactory(handler), new ConfigurationBuilder().Build());

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => source.FetchAsync(Src(), From, To, CancellationToken.None));
        Assert.Contains("400", ex.Message);
        Assert.Contains("지원하지 않는 검색 매개변수", ex.Message);
    }

    static PoSyncSource Eos(string? scheme = null, string? value = null)
        => new("SAV", "Savannah EOS", "CUS-SAV", "https://exapi.example/eos/api/data/Query", scheme, value, "", "", "310471", "",
               30, -7, 60, null, PoSyncConfig.TypeEos, "SIS.EOS_GET_PO");

    [Fact]
    public async Task Eos_source_posts_the_json_query_without_auth()
    {
        using var req = HttpPoSource.BuildEosRequest(Eos(), From, To);

        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.Equal("https://exapi.example/eos/api/data/Query", req.RequestUri!.ToString());
        Assert.Null(req.Headers.Authorization);
        Assert.Equal("application/json", req.Content!.Headers.ContentType!.MediaType);
        using var doc = System.Text.Json.JsonDocument.Parse(await req.Content.ReadAsStringAsync());
        Assert.Equal("SIS.EOS_GET_PO", doc.RootElement.GetProperty("SQL").GetString());
        Assert.Equal("2026-07-19", doc.RootElement.GetProperty("Parameters").GetProperty("IN_DATE_FROM").GetProperty("Value").GetString());
        Assert.Equal("2026-09-17", doc.RootElement.GetProperty("Parameters").GetProperty("IN_DATE_TO").GetProperty("Value").GetString());
    }

    [Fact]
    public async Task Eos_fetch_keeps_only_our_vendor_rows()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """
            {"value":[{"PO_NO":"4100172428","PO_SEQ":10,"VENDOR_CODE":"701850","PART_NO":"A","PO_QTY":1,"DELIVERY_DATE":"2026-11-02","PO_DATE":"2026-09-01"},
                      {"PO_NO":"4100181100","PO_SEQ":20,"VENDOR_CODE":"310471","PART_NO":"B","PO_QTY":2,"DELIVERY_DATE":"2026-11-05","PO_DATE":"2026-10-20"}],"count":2}
            """);
        var source = new HttpPoSource(new StubFactory(handler), new ConfigurationBuilder().Build());

        // 7일 창 = 요청 1번(창이 길면 7일씩 나눠 여러 번 부른다)
        var rows = await source.FetchAsync(Eos(), new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 8), CancellationToken.None);

        Assert.Equal("4100181100-20", Assert.Single(rows).PONO);
        Assert.Equal(HttpMethod.Post, handler.Last!.Method);
    }

    // 요청 본문의 납기일 창을 보고 응답을 고르는 원격 흉내 — 호출한 창을 차례로 남긴다
    sealed class EosRemote(Func<DateOnly, DateOnly, (HttpStatusCode, string)> reply) : HttpMessageHandler
    {
        public readonly List<(DateOnly From, DateOnly To)> Calls = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            using var doc = System.Text.Json.JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            var p = doc.RootElement.GetProperty("Parameters");
            var f = DateOnly.Parse(p.GetProperty("IN_DATE_FROM").GetProperty("Value").GetString()!);
            var t = DateOnly.Parse(p.GetProperty("IN_DATE_TO").GetProperty("Value").GetString()!);
            Calls.Add((f, t));
            var (status, body) = reply(f, t);
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    static string OneRow(DateOnly d, string vendor = "310471")
        => $$"""{"value":[{"PO_NO":"41{{d:MMdd}}","PO_SEQ":10,"VENDOR_CODE":"{{vendor}}","PART_NO":"P","PO_QTY":1,"DELIVERY_DATE":"{{d:yyyy-MM-dd}}","PO_DATE":"2026-11-01"}]}""";

    [Fact]
    public async Task Eos_extending_fetch_runs_past_the_window_until_two_empty_chunks()
    {
        // 원격 데이터(다른 협력사 포함)는 12-31 까지 — 창 끝(11-15) 뒤로도 이어 받다가 빈 구간 2개에서 멈춘다
        var last = new DateOnly(2026, 12, 31);
        var remote = new EosRemote((f, t) => (HttpStatusCode.OK, f <= last ? OneRow(f, "999999") : """{"value":[]}"""));
        var source = new HttpPoSource(new StubFactory(remote), new ConfigurationBuilder().Build());

        var (rows, to) = await source.FetchExtendingAsync(Eos(), new(2026, 11, 2), new(2026, 11, 15), new(2027, 6, 1), CancellationToken.None);

        Assert.Empty(rows);                                   // 다른 협력사 행만 있었다
        Assert.True(to > last);                                // 데이터 끝을 지나
        Assert.True(to < new DateOnly(2027, 1, 31));           // 상한(06-01)까지 가지 않고 멈췄다
        Assert.Equal(2, remote.Calls.Count(c => c.From > last));
    }

    [Fact]
    public async Task Eos_extending_fetch_reads_the_whole_window_even_if_it_is_empty()
    {
        var remote = new EosRemote((f, t) => (HttpStatusCode.OK, """{"value":[]}"""));
        var source = new HttpPoSource(new StubFactory(remote), new ConfigurationBuilder().Build());

        var (_, to) = await source.FetchExtendingAsync(Eos(), new(2026, 11, 2), new(2026, 11, 29), new(2027, 6, 1), CancellationToken.None);

        Assert.Equal(new DateOnly(2026, 11, 29), to);          // 창 [11-02, 11-29] 4구간은 비어도 끝까지, 그 뒤로는 안 간다
        Assert.Equal(4, remote.Calls.Count);
    }

    [Fact]
    public async Task Eos_chunk_over_1000_rows_is_split_in_half_and_retried()
    {
        const string tooMany = """{"Error":"InvalidOperationException","Message":"RefCursor 쿼리 실행 중 오류: ORA-20002: More than 1000 rows. Use a shorter date range."}""";
        var remote = new EosRemote((f, t) => t.DayNumber - f.DayNumber + 1 > 2 ? (HttpStatusCode.InternalServerError, tooMany) : (HttpStatusCode.OK, OneRow(f)));
        var source = new HttpPoSource(new StubFactory(remote), new ConfigurationBuilder().Build());

        var rows = await source.FetchAsync(Eos(), new(2026, 11, 2), new(2026, 11, 8), CancellationToken.None);

        // 7일 → 3일+4일 → 2일 이하로 쪼개 받은 구간마다 1행
        var ok = remote.Calls.Where(c => c.To.DayNumber - c.From.DayNumber + 1 <= 2).ToList();
        Assert.Equal(ok.Count, rows.Count);
        Assert.Equal(7, ok.Sum(c => c.To.DayNumber - c.From.DayNumber + 1));   // 모든 날이 한 번씩
    }

    [Fact]
    public async Task Eos_single_day_over_1000_rows_surfaces_the_remote_error()
    {
        const string tooMany = """{"Message":"ORA-20002: More than 1000 rows. Use a shorter date range."}""";
        var source = new HttpPoSource(new StubFactory(new EosRemote((_, _) => (HttpStatusCode.InternalServerError, tooMany))), new ConfigurationBuilder().Build());

        var ex = await Assert.ThrowsAsync<HttpRequestException>(
            () => source.FetchAsync(Eos(), new(2026, 11, 2), new(2026, 11, 3), CancellationToken.None));
        Assert.Equal(EosPoMapper.RemoteError.TooManyRows, EosPoMapper.ClassifyError(ex.Message));
    }
}
