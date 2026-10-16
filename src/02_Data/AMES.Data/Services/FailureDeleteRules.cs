namespace AMES.Data.Services;

/// <summary>
/// MNT-002 고장 삭제 허용 규칙의 정본(10-13 사용자 결정 — 사용 중이면 거부).
/// 지울 수 있는 고장 = 아직 열린(OPEN·구 REGISTERED) 웹 등록 고장이고, 연결 작업지시가 없거나 착수 전(발행·배정 — MwoStatusRules.IsNotStarted).
/// 진행·해결된 고장은 작업지시·조치 이력이 고장번호로 이어져 있고, 안돈 고장은 진행 중 안돈이 보전을 다시 부르면
/// 새 번호로 다시 생기므로 지우지 않는다. 화면(삭제 버튼)과 MntRepository.DeleteFailure 가 같은 규칙을 쓴다. 테스트: FailureDeleteRulesTests.
/// </summary>
public static class FailureDeleteRules
{
    public enum Block { None, NotOpen, FromAndon, WorkStarted }

    public static Block Check(string? status, string? source, int? workOrderId, string? woStatus)
    {
        if ((status ?? "OPEN").Trim().ToUpperInvariant() is not ("OPEN" or "REGISTERED")) return Block.NotOpen;
        if (string.Equals(source?.Trim(), "ANDON", StringComparison.OrdinalIgnoreCase)) return Block.FromAndon;
        if (workOrderId is not null && woStatus is not null && !MwoStatusRules.IsNotStarted(woStatus)) return Block.WorkStarted;
        return Block.None;
    }

    public static bool CanDelete(string? status, string? source, int? workOrderId, string? woStatus)
        => Check(status, source, workOrderId, woStatus) == Block.None;
}
