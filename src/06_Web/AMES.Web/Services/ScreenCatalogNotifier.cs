namespace AMES.Web.Services;

public sealed class ScreenCatalogNotifier
{
    long _version;

    public event Action? Changed;

    /// <summary>화면 마스터·권한이 바뀔 때마다 1씩 오른다 — PermissionService 가 다시 읽을 때가 됐는지 판단한다.</summary>
    public long Version => Interlocked.Read(ref _version);

    public void Notify()
    {
        Interlocked.Increment(ref _version);
        Changed?.Invoke();
    }
}
