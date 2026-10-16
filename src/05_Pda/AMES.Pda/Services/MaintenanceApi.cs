using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using AMES.Contracts.Dto;

namespace AMES.Pda.Services;

public sealed class MaintenanceApi(HttpClient http, AuthState auth) : PdaApi(http, auth)
{
    public Task<PdaMntBoard> Board(DateTime? from, DateTime? to, bool mine, bool closed)
    {
        var query = $"/board?mine={mine}&includeClosed={closed}";
        if (from is { } start) query += "&from=" + start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (to is { } end) query += "&to=" + end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return Send<PdaMntBoard>(HttpMethod.Get, query);
    }
    public Task<PdaMntDetail> Detail(int id) => Send<PdaMntDetail>(HttpMethod.Get, $"/work-orders/{id}");
    public Task<PdaMntDetail> Start(int id, bool loto) => Send<PdaMntDetail>(HttpMethod.Post, $"/work-orders/{id}/start", new PdaMntStart(loto));
    public Task<PdaMntDetail> SaveTask(int id, PdaMntTask task, string result, string? note)
        => Send<PdaMntDetail>(HttpMethod.Put, $"/work-orders/{id}/tasks/{task.Id}", new PdaMntTaskSave(result, note, task.ModifiedAt));
    public Task<PdaMntSaved> Complete(int id, PdaMntComplete body) => Send<PdaMntSaved>(HttpMethod.Post, $"/work-orders/{id}/complete", body);

    private async Task<T> Send<T>(HttpMethod method, string path, object? body = null)
    {
        Authorize();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
        using var request = new HttpRequestMessage(method, "/api/mnt/pda" + path);
        if (body is not null) request.Content = JsonContent.Create(body, body.GetType());
        try
        {
            using var response = await _http.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(await ReadServiceErrorAsync(response, "Maintenance service is unavailable. Refresh before retrying."));
            return await response.Content.ReadFromJsonAsync<T>(cancellationToken: timeout.Token)
                ?? throw new InvalidOperationException("No maintenance data was returned.");
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException)
        {
            throw new InvalidOperationException(method == HttpMethod.Get
                ? "Unable to load maintenance data. Check the connection and refresh."
                : "The result could not be confirmed. Refresh before retrying; your changes may already be saved.", ex);
        }
    }
}
