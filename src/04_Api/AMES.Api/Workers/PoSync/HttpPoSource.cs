using System.Net.Http.Headers;
using AMES.Api.Workers;
using AMES.Data.Services.PoSync;

namespace AMES.Api.Workers.PoSync;

/// <summary>
/// 고객사 SRM WEBSRV_INQUERY_PO 호출: GET {URL}?CORCD=&amp;BIZCD=&amp;PURC_ORG=&amp;VENDCD=&amp;PO_DATE_BEG=&amp;PO_DATE_TO=,
/// 날짜 창은 발주일(PO_DATE) 기준, 응답 = 커서 행 배열(또는 첫 배열 프로퍼티).
/// 원격은 이 6개 외의 매개변수를 400 으로 거부하므로 다른 값을 붙이지 않는다.
/// 인증은 SW_POSYNC_AUTH(Query:{이름} / Bearer / Basic / Header:{이름}) — 테스트 서버는 Query:APIKEY.
/// </summary>
public sealed class HttpPoSource(IHttpClientFactory http, IConfiguration cfg) : IPoSource
{
    public const string ClientName = "po-sync";

    public async Task<IReadOnlyList<SrmPoRow>> FetchAsync(PoSyncSource s, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var client = http.CreateClient(ClientName);
        client.Timeout = TimeSpan.FromSeconds(ScheduledWorkerSettings.TimeoutSec(s.TimeoutSec, cfg));

        using var req  = BuildRequest(s, from, to);
        using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}: {Head(text)}");

        return SrmPoMapper.ParseRows(text);
    }

    internal static HttpRequestMessage BuildRequest(PoSyncSource s, DateOnly from, DateOnly to)
    {
        var query = new List<KeyValuePair<string, string>>
        {
            new("CORCD",       s.CorCd),
            new("BIZCD",       s.BizCd),
            new("PURC_ORG",    s.PurcOrg),
            new("VENDCD",      s.VendCd),
            new("PO_DATE_BEG", from.ToString("yyyy-MM-dd")),
            new("PO_DATE_TO",  to.ToString("yyyy-MM-dd")),
        };

        var (header, extraHeader) = SrmAuth.Apply(query, s.AuthScheme, s.AuthValue);

        var qs  = string.Join("&", query.Select(kv => Uri.EscapeDataString(kv.Key) + "=" + Uri.EscapeDataString(kv.Value)));
        var url = s.Url + (s.Url.Contains('?') ? "&" : "?") + qs;

        var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.Authorization = header;
        if (extraHeader is { } h) req.Headers.TryAddWithoutValidation(h.Key, h.Value);
        return req;
    }

    private static string Head(string s) => s.Length > 300 ? s[..300] : s;
}
