namespace AMES.Data.Services;

/// <summary>
/// 프로그램이 그룹 이름으로 직접 읽는 공통코드 그룹 — MD-030 에서 그룹을 지우면 콤보가 비거나 판정(교대·권한·상태 등)이
/// 조용히 깨지므로 삭제를 막는다(10-12 사용자 결정). 항목 하나를 고치거나 지우는 것은 막지 않는다.
/// 목록은 소스(.cs·.razor)에 문자열로 나오는 그룹 + DB 프로시저가 읽는 그룹이고, Worker 설정 그룹은 이름을 "SW_" + Code 로
/// 만들어 쓰므로 접두어로 막는다. 새 화면이 그룹을 읽기 시작하면 여기에 더한다 — CodeGroupsUsedByCodeTests 가 빠뜨림을 잡는다.
/// </summary>
public static class CodeGroupsUsedByCode
{
    /// <summary>Worker 설정 그룹 접두어(CLAUDE.md "외부 API 연동 Worker").</summary>
    public const string WorkerPrefix = "SW_";

    /// <summary>소스에는 없고 DB 프로시저만 읽는 그룹(프로시저 이름).</summary>
    public static readonly IReadOnlyDictionary<string, string> ReadByDbProcedure = new Dictionary<string, string>
    {
        ["WH_INV_STATUS"] = "WH_SET_LOT_STATUS",
    };

    public static readonly IReadOnlySet<string> Groups = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "ACTIVE_FLAG", "ANDON_CAUSE", "ANDON_DEPT", "APS_COVER_TIER", "APS_SETTING", "AUDIT_STATUS", "BARCODE_TYPE",
        "BOM_STATUS", "CAUSE_CATEGORY", "CURRENCY_CODE", "CUSTOMER_STATUS", "CUSTOMER_TYPE", "DAY_TYPE",
        "DEFECT_CATEGORY", "DEFECT_SEVERITY", "DELIVERY_STATUS", "DEPARTMENT", "DISPOSITION", "EQUIP_STATUS",
        "EQUIP_TYPE", "FAILURE_SOURCE", "FAILURE_TYPE", "FG_RETURN_REASON", "FG_STORAGE_METHOD", "IF_DIRECTION",
        "IF_STATUS", "INSPECT_TYPE", "INV_ADJUST_REASON", "ITEM_CATEGORY", "ITEM_TYPE", "LABEL_TYPE", "LANGUAGE",
        "LINE_STATUS", "MATERIAL_STATUS", "MATERIAL_TYPE", "MNT_SLOT", "MNT_STATUS", "MNT_ZONE", "MODULE", "MOLD_STATUS",
        "MWO_RESULT", "MWO_TYPE", "NATION_CODE", "NOTIFICATION_CHANNEL", "NOTIFICATION_STATUS", "OVEN_STATUS",
        "PACKING_TYPE", "PERMISSION_LEVEL", "PLANT", "PM_CLASS", "PM_CYCLE_BASIS", "PM_STATUS", "PM_TYPE", "PROCESS",
        "RECIPE_STATUS", "RECIPE_TYPE", "RFID_READER_STATUS", "RFID_STATUS", "ROUTING", "SCHEDULE_STATUS",
        "SEGMENT_STATE", "SHIFT_PATTERN", "SHIPMENT_DEST_STATUS", "SHIPMENT_DEST_TYPE", "SPAREPARTS_CATEGORY",
        "SPAREPARTS_EQUIP", "STATION_STATUS", "SUBPROCESS", "TIME_ZONE", "UNIT_CATEGORY", "USER_STATUS",
        "VENDOR_TYPE", "WH_AISLE", "WH_AREA", "WH_BAY", "WH_CODE", "WH_OUTGOING_TYPE", "WH_SLOT", "WH_ZONE",
        "WORK_SHIFT",
        "WH_INV_STATUS",
    };

    public static bool IsUsedByCode(string? groupCode)
    {
        var g = groupCode?.Trim();
        if (string.IsNullOrEmpty(g)) return false;
        return g.StartsWith(WorkerPrefix, StringComparison.OrdinalIgnoreCase) || Groups.Contains(g);
    }
}
