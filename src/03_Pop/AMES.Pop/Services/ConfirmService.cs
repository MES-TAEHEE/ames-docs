namespace AMES.Pop.Services;

public sealed record ConfirmRequest(string Title, string Body, string OkLabel, string CancelLabel, bool Destructive);

/// <summary>
/// Bridges a page's `await Confirm.AskAsync(...)` to the on-screen
/// modal hosted by ConfirmHost. Singleton — one prompt at a time.
/// </summary>
public sealed class ConfirmService
{
    private TaskCompletionSource<bool>? _tcs;
    public event Action<ConfirmRequest>? OnAsk;
    public ConfirmRequest? Pending { get; private set; }

    public Task<bool> AskAsync(string title, string body, string okLabel = "OK",
                               string cancelLabel = "Cancel", bool destructive = false)
    {
        _tcs = new TaskCompletionSource<bool>();
        Pending = new ConfirmRequest(title, body, okLabel, cancelLabel, destructive);
        OnAsk?.Invoke(Pending);
        return _tcs.Task;
    }

    public void Resolve(bool result)
    {
        Pending = null;
        var tcs = _tcs;
        _tcs = null;
        tcs?.TrySetResult(result);
    }
}
