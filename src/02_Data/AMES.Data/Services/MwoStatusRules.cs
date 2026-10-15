using AMES.Contracts.Dto;

namespace AMES.Data.Services;

/// <summary>
/// 보전 작업지시 상태 전이 규칙의 정본(10-15 사용자 결정, 공통코드 MNT_STATUS).
/// 발행 ISSUED(담당자 없음) → 배정 ASSIGNED → 착수 IN_PROGRESS → 완료 COMPLETED. 끝나기 전이면 어느 단계에서든 종료 CLOSED.
/// 배정은 착수 뒤에도 담당자만 바꾼다(상태는 IN_PROGRESS 그대로). MntRepository 의 SQL 과 MNT-007 버튼이 같은 규칙을 쓴다. 테스트: MwoStatusRulesTests.
/// </summary>
public static class MwoStatusRules
{
    static string? Norm(string? s) => s?.Trim().ToUpperInvariant();

    /// <summary>담당자가 있으면 배정, 없으면 발행 — 새로 만드는 작업지시(고장·PM·수동 발행·후속)의 첫 상태.</summary>
    public static string Initial(string? techId) =>
        string.IsNullOrWhiteSpace(techId) ? MwoStatusCodes.Issued : MwoStatusCodes.Assigned;

    /// <summary>착수 전(발행·배정) — 고장·PM 수정이 작업지시 내용을 같이 맞추고, PM·고장 삭제가 작업지시를 같이 지우는 범위.</summary>
    public static bool IsNotStarted(string? s) => Norm(s) is MwoStatusCodes.Issued or MwoStatusCodes.Assigned;

    public static bool IsEnded(string? s) => Norm(s) is MwoStatusCodes.Completed or MwoStatusCodes.Closed;

    public static bool CanAssign(string? s)   => !IsEnded(s);
    public static bool CanStart(string? s)    => IsNotStarted(s);
    public static bool CanComplete(string? s) => !IsEnded(s);
    public static bool CanClose(string? s)    => !IsEnded(s);

    /// <summary>배정 뒤 상태 — 착수 전이면 ASSIGNED, 진행 중이면 그대로.</summary>
    public static string AfterAssign(string? s) =>
        Norm(s) == MwoStatusCodes.InProgress ? MwoStatusCodes.InProgress : MwoStatusCodes.Assigned;
}
