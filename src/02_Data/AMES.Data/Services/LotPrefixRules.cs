namespace AMES.Data.Services;

/// <summary>
/// 라인 LOT PREFIX 규칙(10-23 사용자 결정) — LOT 을 발행하는 공정(사출·감싸기·도장)의 라인만 접두어가 필수이고,
/// 그 밖(재작업 등)의 라인은 접두어를 두지 않는다. 접두어는 LOT 번호([년][월][일][접두어][순번])·라인 재고 위치·WH 구역 코드의 키다.
/// MD-001 저장(MasterDataRepository)·WO 릴리스 후보(WorkOrderRepository.ReadPreview)·PP-003·PP-APS WO 생성이 이 규칙을 쓴다.
/// </summary>
public static class LotPrefixRules
{
    /// <summary>LOT 을 발행하는 공정 — 라인 작업장(MD_WorkCenter.ProcessCode) 기준.</summary>
    public static readonly IReadOnlyList<string> LotProcesses = ["INJ", "IMG", "PNT"];

    /// <summary>SQL IN 목록 — <see cref="LotProcesses"/> 와 같아야 한다(LotPrefixRulesTests).</summary>
    public const string LotProcessSqlList = "'INJ','IMG','PNT'";

    public static bool RequiresPrefix(string? processCode)
        => !string.IsNullOrWhiteSpace(processCode)
           && LotProcesses.Contains(processCode.Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary>영문·숫자 2자.</summary>
    public static bool IsValid(string? prefix)
        => prefix is { Length: 2 } && prefix.All(char.IsAsciiLetterOrDigit);
}
