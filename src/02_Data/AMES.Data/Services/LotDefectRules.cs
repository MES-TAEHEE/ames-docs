using AMES.Contracts.Enums;

namespace AMES.Data.Services;

/// <summary>스캔 확정이 막히는 이유. 각 공정의 ConfirmOutcome 으로 매핑한다.</summary>
public enum LotConfirmBlock { None, AlreadyConfirmed, NgBlocked, InRework, Scrapped }

/// <summary>
/// PR_InjLot / PR_ImgLot.ConfirmStatus 상태 기계의 정본.
///   RAW ─스캔→ CONFIRMED ─불량→ DEFECT ─수리→ CONFIRMED / ─폐기→ SCRAPPED
///   INJ: RAW ─로봇NG→ NG_BLOCKED ─불량→ DEFECT
/// INJ·IMG 리포지토리와 ReworkRepository 가 같은 규칙을 쓴다. 단위 테스트: LotDefectRulesTests.
/// </summary>
public static class LotDefectRules
{
    public const string Raw       = "RAW";
    public const string Confirmed = "CONFIRMED";
    public const string NgBlocked = "NG_BLOCKED";
    public const string Defect    = "DEFECT";
    public const string Scrapped  = "SCRAPPED";
    /// <summary>폐지된 종결 상태. 마이그레이션이 SCRAPPED 로 옮기지만 그 전 행은 폐기로 취급한다.</summary>
    public const string LegacyNgConfirmed = "NG_CONFIRMED";

    public const string DispositionReworked = "REWORKED";
    public const string DispositionScrapped = "SCRAPPED";
    public const string DispositionLegacy   = "LEGACY";

    public static DefectRegisterOutcome CheckRegister(string status) => status switch
    {
        Raw or Confirmed or NgBlocked => DefectRegisterOutcome.Registered,
        Defect                        => DefectRegisterOutcome.AlreadyInRework,
        Scrapped or LegacyNgConfirmed => DefectRegisterOutcome.Scrapped,
        _ => throw new InvalidOperationException($"Unknown lot status '{status}'."),
    };

    public static LotConfirmBlock ConfirmBlock(string status) => status switch
    {
        Raw                           => LotConfirmBlock.None,
        Confirmed                     => LotConfirmBlock.AlreadyConfirmed,
        NgBlocked                     => LotConfirmBlock.NgBlocked,
        Defect                        => LotConfirmBlock.InRework,
        Scrapped or LegacyNgConfirmed => LotConfirmBlock.Scrapped,
        _ => throw new InvalidOperationException($"Unknown lot status '{status}'."),
    };

    /// <summary>확정 후 LOT 만 실적이 있어 역분개가 필요하다.</summary>
    public static bool ReversesResult(string priorStatus) => priorStatus == Confirmed;

    /// <summary>Disposition 이 비어 있어야 판정할 수 있다.</summary>
    public static bool CanDecide(string? disposition) => string.IsNullOrEmpty(disposition);
}
