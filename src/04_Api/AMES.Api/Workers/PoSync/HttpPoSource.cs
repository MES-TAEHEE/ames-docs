using System.Net.Http.Headers;
using AMES.Api.Workers;
using AMES.Data.Services.PoSync;

namespace AMES.Api.Workers.PoSync;

/// <summary>
/// 고객사 SRM WEBSRV_INQUERY_PO 호출: GET {URL}?CORCD=&amp;BIZCD=&amp;PURC_ORG=&amp;VENDCD=&amp;PO_DATE_BEG=&amp;PO_DATE_TO=,
/// 날짜 창은 발주일(PO_DATE) 기준, 응답 = 커서 행 배열(또는 첫 배열 프로퍼티).
/// 원격은 이 6개 외의 매개변수를 400 으로 거부하므로 다른 값을 붙이지 않는다.
/// 인증은 SW_POSYNC_AUTH(Query:{이름} / Bearer / Basic / Header:{이름}) — 테스트 서버는 Query:APIKEY.
/// 소스 TYPE=EOS 는 Savannah EOS 조회 API — POST {URL} JSON 본문(<see cref="EosPoMapper.BuildBody"/>), 납기일 창,
/// 응답에서 VENDCD 행만 남긴다. 인증 행이 없으면 인증 없이 부른다. 자동 실행은 창 끝 뒤로 원격에 데이터가 있는 데까지 이어 받는다(<see cref="FetchExtendingAsync"/>).
/// </summary>
public sealed class HttpPoSource(IHttpClientFactory http, IConfiguration cfg) : IPoSource
{
    public const string ClientName = "po-sync";

    public async Task<IReadOnlyList<SrmPoRow>> FetchAsync(PoSyncSource s, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var client = Client(s);
        if (!s.IsEos)
            return SrmPoMapper.ParseRows(await SendAsync(client, BuildRequest(s, from, to), ct));
        return (await FetchEosAsync(client, s, from, to, to, ct)).Rows;
    }

    /// <summary>EOS 만 창 끝 뒤로 이어 받는다 — SRM 은 발주일 창이라 미래가 없어 기본 동작(minTo 까지).</summary>
    public async Task<(IReadOnlyList<SrmPoRow> Rows, DateOnly To)> FetchExtendingAsync(
        PoSyncSource s, DateOnly from, DateOnly minTo, DateOnly maxTo, CancellationToken ct)
    {
        var client = Client(s);
        if (!s.IsEos)
            return (SrmPoMapper.ParseRows(await SendAsync(client, BuildRequest(s, from, minTo), ct)), minTo);
        return await FetchEosAsync(client, s, from, minTo, maxTo < minTo ? minTo : maxTo, ct);
    }

    HttpClient Client(PoSyncSource s)
    {
        var client = http.CreateClient(ClientName);
        client.Timeout = TimeSpan.FromSeconds(ScheduledWorkerSettings.TimeoutSec(s.TimeoutSec, cfg));
        return client;
    }

    // EOS 는 한 번에 31일·1,000행까지만 받아(넘으면 HTTP 500 + ORA-20001/20002) ChunkDays 씩 차례로 부른다.
    // [from, minTo] 는 끝까지 받고, 그 뒤 maxTo 까지는 원격 결과(모든 협력사)가 EmptyChunksToStop 구간 연속으로 비면 멈춘다.
    async Task<(IReadOnlyList<SrmPoRow> Rows, DateOnly To)> FetchEosAsync(
        HttpClient client, PoSyncSource s, DateOnly from, DateOnly minTo, DateOnly maxTo, CancellationToken ct)
    {
        var rows    = new List<SrmPoRow>();
        var reached = minTo;
        int empty   = 0;
        foreach (var (f, t) in EosPoMapper.Chunks(from, maxTo, EosPoMapper.ChunkDays))
        {
            if (f > minTo && empty >= EosPoMapper.EmptyChunksToStop) break;
            var raw = await FetchEosChunkAsync(client, s, f, t, ct);
            rows.AddRange(EosPoMapper.ToSrmRows(raw, s.VendCd));
            empty = raw.Count == 0 ? empty + 1 : 0;
            if (t > reached) reached = t;
        }
        return (rows, reached);
    }

    // 1,000행 초과(ORA-20002)면 구간을 반으로 나눠 다시 부른다 — 하루짜리도 넘으면 원격 오류 그대로 낸다
    async Task<IReadOnlyList<EosPoRow>> FetchEosChunkAsync(HttpClient client, PoSyncSource s, DateOnly f, DateOnly t, CancellationToken ct)
    {
        try
        {
            return EosPoMapper.ParseRows(await SendAsync(client, BuildEosRequest(s, f, t), ct));
        }
        catch (HttpRequestException ex) when (t > f && EosPoMapper.ClassifyError(ex.Message) == EosPoMapper.RemoteError.TooManyRows)
        {
            var mid = f.AddDays((t.DayNumber - f.DayNumber) / 2);
            return [.. await FetchEosChunkAsync(client, s, f, mid, ct), .. await FetchEosChunkAsync(client, s, mid.AddDays(1), t, ct)];
        }
    }

    static async Task<string> SendAsync(HttpClient client, HttpRequestMessage request, CancellationToken ct)
    {
        using var req  = request;
        using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}: {Head(text)}");
        return text;
    }

    internal static HttpRequestMessage BuildEosRequest(PoSyncSource s, DateOnly from, DateOnly to)
    {
        var query = new List<KeyValuePair<string, string>>();
        var (header, extraHeader) = SrmAuth.Apply(query, s.AuthScheme, s.AuthValue);
        var url = query.Count == 0 ? s.Url
            : s.Url + (s.Url.Contains('?') ? "&" : "?") + string.Join("&", query.Select(kv => Uri.EscapeDataString(kv.Key) + "=" + Uri.EscapeDataString(kv.Value)));

        var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(EosPoMapper.BuildBody(s.Sql ?? PoSyncConfig.DefaultEosSql, from, to),
                System.Text.Encoding.UTF8, "application/json"),
        };
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.Authorization = header;
        if (extraHeader is { } h) req.Headers.TryAddWithoutValidation(h.Key, h.Value);
        return req;
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
