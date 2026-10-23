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

    /// <summary>창이 닫혔다(응답·취소)는 신호 — 호스트가 다시 그린다.</summary>
    public event Action? OnClosed;

    public Task<bool> AskAsync(string title, string body, string okLabel = "OK",
                               string cancelLabel = "Cancel", bool destructive = false)
    {
        // 앞선 질문이 아직 떠 있으면 거절로 끝낸다 — 덮어쓰면 그 호출자가 영원히 기다린다.
        _tcs?.TrySetResult(false);
        _tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
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
        OnClosed?.Invoke();
    }

    /// <summary>떠 있는 확인창을 거절로 닫는다 — 로그아웃·유휴 만료 뒤에 남지 않게.</summary>
    public void Cancel()
    {
        if (Pending is not null) Resolve(false);
    }
}
