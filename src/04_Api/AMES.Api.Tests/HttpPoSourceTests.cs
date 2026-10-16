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
}
