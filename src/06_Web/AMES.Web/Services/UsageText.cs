using AMES.Data.Repositories;
using Microsoft.Extensions.Localization;

namespace AMES.Web.Services;

/// <summary>
/// 삭제를 막은 사용처 목록(<see cref="MasterDataRepository.UsageRef"/>)을 화면 문구로 — "BOM 2건(V-…, V-…) · 작업지시 6건(WO-…, …)".
/// 종류 이름은 resx 키 MD.Usage.Kind.{Kind}, 한 종류의 서식은 MD.Usage.Fmt.
/// </summary>
public static class UsageText
{
    // 화면에 보이는 순서 — 마스터 → 계획·생산 → 보전 → 재고·거래처, 패턴은 스케줄 → 발행 → APS
    static readonly string[] Order =
    [
        "LINE", "STATION", "EQUIP", "SUPERVISOR", "PATTERN", "WCDEFAULT", "OVEN", "RFID", "INJCOND", "CARD",
        "BOM", "BOP", "MOLD", "RESIN", "MOLDITEM", "MOLDLINE", "SHIPDEST", "LABEL", "QCSTD", "ITEMVENDOR", "SPARE", "FABRIC", "PORTALUSER", "WORKER",
        "WO", "SO", "DEMAND", "FORECAST", "SHIPORDER", "LOT", "SCHEDULE", "PUBLISHED", "APSLINE", "APSDEFAULT",
        "PM", "MWO", "FAILURE",
        "STOCK", "VENDOR", "PR", "PO", "DELIVERY", "DNOTE", "DCASE",
    ];

    public static string Format(IStringLocalizer L, IEnumerable<MasterDataRepository.UsageRef> refs)
        => string.Join(" · ", refs
            .OrderBy(r => Array.IndexOf(Order, r.Kind) is var i && i >= 0 ? i : int.MaxValue)
            .Select(r => string.Format(L["MD.Usage.Fmt"].Value,
                L[$"MD.Usage.Kind.{r.Kind}"].Value, r.Count, r.Samples + (r.Count > 3 ? ", …" : ""))));
}
