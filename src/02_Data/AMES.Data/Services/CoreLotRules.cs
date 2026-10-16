using AMES.Contracts.Enums;

namespace AMES.Data.Services;

/// <summary>
/// IMG 가 받는 사출 Core LOT 의 상태 규칙. 사출에서 양품 확정된 LOT 만 완제품이 된다.
/// 단위 테스트: CoreLotRulesTests.
/// </summary>
public static class CoreLotRules
{
    /// <summary>PR_InjLot.ConfirmStatus → 통과면 Created. 모르는 상태는 미확정으로 거부한다.</summary>
    public static ImgCoreOutcome Check(string injConfirmStatus) => injConfirmStatus switch
    {
        LotDefectRules.Confirmed                                    => ImgCoreOutcome.Created,
        LotDefectRules.NgBlocked or LotDefectRules.Defect           => ImgCoreOutcome.CoreDefect,
        LotDefectRules.Scrapped or LotDefectRules.LegacyNgConfirmed => ImgCoreOutcome.CoreScrapped,
        _                                                           => ImgCoreOutcome.CoreUnconfirmed,
    };
}
