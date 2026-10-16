namespace AMES.Data.Services;

/// <summary>
/// PP-002 수주 자동 확정(정본). SYS_Config <see cref="ConfigKey"/>(bool, 'true'/'false') 가 켜져 있으면
/// 새로 들어오는 수주(엑셀 업로드·API 가져오기·PO Sync 워커 — 전부 <c>PpRepository.UpsertCustomerOrders</c>)가
/// Open 을 거치지 않고 저장 시점에 Confirmed 가 된다. 행이 없거나 비활성이면 꺼짐 — 마이그레이션 전 DB 도 종전 동작.
/// </summary>
public static class SoAutoConfirm
{
    public const string ConfigKey = "PP_SO_AUTO_CONFIRM";

    public static bool IsOn(string? value, bool isActive = true)
    {
        if (!isActive || value is null) return false;
        var v = value.Trim();
        return v.Equals("true", StringComparison.OrdinalIgnoreCase) || v == "1";
    }

    public static string ToStored(bool on) => on ? "true" : "false";
}
