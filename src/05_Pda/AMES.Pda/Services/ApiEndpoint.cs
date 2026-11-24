using System.Diagnostics;

namespace AMES.Pda.Services;

public sealed record PdaApiServer(string Name, string Url);

/// <summary>
/// The API server the PDA talks to. Chosen on the login screen and remembered on the device;
/// every API request is sent to it by <see cref="ApiBaseHandler"/>.
/// </summary>
public sealed class ApiEndpoint
{
    private const string PreferenceKey = "pda.apiBaseUrl";
    private static readonly HttpClient Probe = new() { Timeout = Timeout.InfiniteTimeSpan };

    public IReadOnlyList<PdaApiServer> Servers { get; }
    public string Current { get; private set; }
    public event Action? Changed;

    public ApiEndpoint(PdaSettings settings)
    {
        Servers = settings.ApiServers;
        var saved = Preferences.Default.Get(PreferenceKey, "");
        Current = Servers.Any(s => SameUrl(s.Url, saved)) ? saved : settings.ApiBaseUrl;
    }

    public PdaApiServer? CurrentServer => Servers.FirstOrDefault(s => SameUrl(s.Url, Current));

    public void Select(string url)
    {
        if (SameUrl(url, Current)) return;
        Current = url;
        Preferences.Default.Set(PreferenceKey, url);
        Changed?.Invoke();
    }

    /// <summary>
    /// Request paths start with "/api/…"; a server URL may carry its own path prefix
    /// (e.g. a reverse proxy at https://host/api), so the path is appended rather than resolved.
    /// </summary>
    public static Uri Combine(string baseUrl, string pathAndQuery) =>
        new(baseUrl.TrimEnd('/') + "/" + pathAndQuery.TrimStart('/'));

    public static async Task<(bool Ok, long Ms, string Message)> TestAsync(string baseUrl)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(6));
            using var response = await Probe.GetAsync(Combine(baseUrl, "/api/health"), timeout.Token);
            sw.Stop();
            return response.IsSuccessStatusCode
                ? (true, sw.ElapsedMilliseconds, "OK")
                : (false, sw.ElapsedMilliseconds, $"HTTP {(int)response.StatusCode}");
        }
        catch (OperationCanceledException) { return (false, sw.ElapsedMilliseconds, "Timeout"); }
        catch (Exception ex) { return (false, sw.ElapsedMilliseconds, ex.GetBaseException().Message); }
    }

    private static bool SameUrl(string a, string b) =>
        string.Equals(a.TrimEnd('/'), b.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
}

public sealed class ApiBaseHandler(ApiEndpoint endpoint) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri is { } uri)
            request.RequestUri = ApiEndpoint.Combine(endpoint.Current, uri.IsAbsoluteUri ? uri.PathAndQuery : uri.OriginalString);
        return base.SendAsync(request, cancellationToken);
    }
}
