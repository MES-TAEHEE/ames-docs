namespace AMES.Pop.Services;

/// <summary>
/// 시리얼 스캐너 → 화면 이벤트 버스. 화면은 포트를 모르고 이것만 구독한다.
/// 호스트(PopBlazorForm)만 Publish/SetConnected 를 부른다.
/// 구독자 예외는 개별로 삼킨다 — 한 화면의 실패가 다른 구독자와 리더 스레드를 죽이면 안 된다.
/// </summary>
public sealed class ScannerService
{
    private readonly Action<string> _log;

    public ScannerService(Action<string>? log = null) => _log = log ?? (_ => { });

    /// <summary>설정에 포트가 있어 리더가 떠 있는지. false 면 화면은 칩을 숨긴다.</summary>
    public bool IsEnabled { get; set; }

    public bool IsConnected { get; private set; }

    /// <summary>프레임 하나당 1회. 리더 스레드에서 발생 — 화면은 InvokeAsync 로 넘겨야 한다.</summary>
    public event Action<string>? OnScan;

    /// <summary>연결 상태가 바뀔 때만. 리더 스레드에서 발생.</summary>
    public event Action<bool>? ConnectionChanged;

    public void Publish(string code)
    {
        var handlers = OnScan;
        if (handlers is null) return;
        foreach (var d in handlers.GetInvocationList())
        {
            try { ((Action<string>)d)(code); }
            catch (Exception ex) { _log($"scan subscriber failed: {ex.Message}"); }
        }
    }

    public void SetConnected(bool connected)
    {
        if (IsConnected == connected) return;
        IsConnected = connected;
        try { ConnectionChanged?.Invoke(connected); }
        catch (Exception ex) { _log($"connection subscriber failed: {ex.Message}"); }
    }
}
