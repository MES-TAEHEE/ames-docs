using AMES.Data.Connection;
using AMES.Pop.Common;
using AMES.Pop.Services;
using Xunit;

namespace AMES.Pop.Tests;

/// <summary>
/// 연결 상태 저장(10-29) — 끊김 상태를 파일에 남겨 다음 실행이 바로 오프라인으로 시작하고, 정상으로 바뀌면 그것도 남긴다.
/// DbHealth 는 프로세스 전체 상태라 오프라인 테스트들과 같은 직렬 컬렉션에서 돈다.
/// </summary>
[Collection("OfflineAuth")]
public sealed class PopDbStateTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ames-popdb-" + Guid.NewGuid().ToString("N"));
    private readonly string _origDir = OfflineStore.Dir;

    public PopDbStateTests()
    {
        Directory.CreateDirectory(_dir);
        OfflineStore.Dir = _dir;
    }

    public void Dispose()
    {
        DbHealth.RecordSuccess();
        OfflineStore.Dir = _origDir;
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static void Tick() => Thread.Sleep(20);   // 성공·실패 기록 시각이 같은 틱에 겹치지 않게

    [Fact]
    public void A_saved_down_state_starts_the_next_run_offline()
    {
        DbHealth.RecordSuccess(); Tick();
        OfflineStore.Save(PopDb.StateFile, new PopDb.SavedState { Down = true, AtUtc = DateTime.UtcNow });

        PopDb.RestoreSavedState();

        Assert.True(PopDb.IsOffline);
    }

    [Fact]
    public void State_changes_are_saved_and_an_online_state_starts_online()
    {
        DbHealth.RecordSuccess(); Tick();
        DbHealth.RecordFailure();
        PopDb.SaveIfChanged();
        Assert.True(OfflineStore.Load<PopDb.SavedState>(PopDb.StateFile)!.Down);

        Tick(); DbHealth.RecordSuccess();
        PopDb.SaveIfChanged();
        Assert.False(OfflineStore.Load<PopDb.SavedState>(PopDb.StateFile)!.Down);

        PopDb.RestoreSavedState();
        Assert.False(PopDb.IsOffline);
    }

    [Fact]
    public void No_saved_state_leaves_the_connection_state_alone()
    {
        DbHealth.RecordSuccess(); Tick();

        PopDb.RestoreSavedState();

        Assert.False(PopDb.IsOffline);
    }
}
