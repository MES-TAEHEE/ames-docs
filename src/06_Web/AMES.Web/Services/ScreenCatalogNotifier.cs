namespace AMES.Web.Services;

public sealed class ScreenCatalogNotifier
{
    long _version;

    public event Action? Changed;

    /// <summary>화면 마스터·권한이 바뀔 때마다 1씩 오른다 — PermissionService·MenuCatalog 가 다시 읽을 때가 됐는지 판단한다.</summary>
    public long Version => Interlocked.Read(ref _version);

    /// <summary>
    /// 버전만 올리고 구독자(모든 세션의 메뉴·홈·화면 제목)는 스레드 풀에서 각자 부른다(10-07).
    /// 전에는 저장한 사람의 처리 안에서 세션 수만큼 핸들러가 차례로 돌았고, Blazor 는 바쁘지 않은 회로의 InvokeAsync 를
    /// 호출한 스레드에서 바로 실행하므로 다른 세션들의 DB 재조회까지 저장 버튼의 응답 시간에 더해졌다.
    /// 끊긴 회로의 예외는 그 구독자 몫이라 여기서 삼킨다.
    /// </summary>
    public void Notify()
    {
        Interlocked.Increment(ref _version);
        if (Changed is not { } handlers) return;
        foreach (var h in handlers.GetInvocationList().Cast<Action>())
            _ = Task.Run(() => { try { h(); } catch { } });
    }
}
