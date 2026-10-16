using System.Net.Http.Headers;
using AMES.Data.Services.DemandPlan;

namespace AMES.Api.Workers.DemandPlanSync;

/// <summary>
/// 고객사 SRM MM30011(일별 구매계획, JIT) 호출(잠정 계약, 스펙 §6): GET {URL}?CORCD=&amp;BIZCD=&amp;PURC_ORG=&amp;VENDCD=&amp;PLAN_DATE=yyyy-MM-dd,
/// 응답은 <see cref="SrmMipMapper.Parse"/> 가 처리하는 JSON 봉투. 인증은 PoSync 와 같은 규칙(<see cref="SrmAuth"/>).
/// 매개변수 이름 <see cref="PlanDateParam"/> 은 잠정이라 실제 SRM 이름으로 바뀌면 이 상수만 고친다.
/// </summary>
public sealed class HttpDemandPlanSource(IHttpClientFactory http, IConfiguration cfg) : IDemandPlanSource
{
    public const string ClientName = "demand-plan-sync";
    public const string PlanDateParam = "PLAN_DATE";   // 잠정 — SRM 이 정한 이름으로 바꾼다(스펙 §6)

    public async Task<SrmMipResponse> FetchAsync(DemandPlanSource s, DateOnly planDate, CancellationToken ct)
    {
        var client = http.CreateClient(ClientName);
        client.Timeout = TimeSpan.FromSeconds(ScheduledWorkerSettings.TimeoutSec(s.TimeoutSec, cfg));
        using var req  = BuildRequest(s, planDate);
        using var resp = await client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) throw new HttpRequestException($"HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}: {Head(text)}");
        return SrmMipMapper.Parse(text);
    }

    internal static HttpRequestMessage BuildRequest(DemandPlanSource s, DateOnly planDate)
    {
        var query = new List<KeyValuePair<string, string>>
        {
            new("CORCD", s.CorCd), new("BIZCD", s.BizCd), new("PURC_ORG", s.PurcOrg), new("VENDCD", s.VendCd),
            new(PlanDateParam, planDate.ToString("yyyy-MM-dd")),
        };
        var (header, extra) = SrmAuth.Apply(query, s.AuthScheme, s.AuthValue);
        var qs  = string.Join("&", query.Select(kv => Uri.EscapeDataString(kv.Key) + "=" + Uri.EscapeDataString(kv.Value)));
        var req = new HttpRequestMessage(HttpMethod.Get, s.Url + (s.Url.Contains('?') ? "&" : "?") + qs);
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        req.Headers.Authorization = header;
        if (extra is { } h) req.Headers.TryAddWithoutValidation(h.Key, h.Value);
        return req;
    }

    private static string Head(string s) => s.Length > 300 ? s[..300] : s;
}
