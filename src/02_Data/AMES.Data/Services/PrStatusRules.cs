namespace AMES.Data.Services;

/// <summary>
/// PP-006 구매요청 상태 기계(정본). SAP B1 연동 전까지는 화면이 전이를 확정한다.
/// Draft/Failed →(Send) Sent →(Approve) Approved. Draft/Failed/Sent →(Fail) Failed. Approved 는 종결.
/// 구 어휘 Pending·Rejected 는 <see cref="Normalize"/> 가 Draft·Failed 로 읽는다(마이그레이션이 이관하지만 방어).
/// </summary>
public static class PrStatusRules
{
    public const string Draft = "Draft", Sent = "Sent", Approved = "Approved", Failed = "Failed";

    public enum PrAction { Send, Fail, Approve }

    public static string Normalize(string? status) => status switch
    {
        null or "" or "Pending" => Draft,
        "Rejected" => Failed,
        _ => status,
    };

    public static bool CanSend(string? status)    => Normalize(status) is Draft or Failed;
    public static bool CanFail(string? status)    => Normalize(status) is Draft or Failed or Sent;
    public static bool CanApprove(string? status) => Normalize(status) == Sent;

    /// <summary>허용된 전이면 다음 상태, 아니면 <see cref="InvalidOperationException"/>.</summary>
    public static string Next(string? status, PrAction action)
    {
        var from = Normalize(status);
        var ok = action switch
        {
            PrAction.Send    => CanSend(from),
            PrAction.Fail    => CanFail(from),
            PrAction.Approve => CanApprove(from),
            _ => false,
        };
        if (!ok) throw new InvalidOperationException($"PR status '{from}' does not allow {action}.");
        return action switch { PrAction.Send => Sent, PrAction.Approve => Approved, _ => Failed };
    }
}
