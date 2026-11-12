namespace AMES.Data.Services;

/// <summary>
/// 설비 유형(공통코드 EQUIP_TYPE)과 그 설비가 붙을 수 있는 라인의 공정(작업장 ProcessCode)을 맞추는 규칙.
/// 사출 설비는 사출(INJ) 라인에만, 감싸기(WRAP) 설비는 감싸기(IMG) 라인에만, 도장(PNT) 설비는 도장 라인에만 붙는다.
/// 유형이 비었거나 모르는 값이면 규칙이 없으므로 모든 라인을 허용한다.
/// </summary>
public static class EquipmentLineRules
{
    public const string TypeInjection = "INJ";
    public const string TypeWrap = "WRAP";
    public const string TypePaint = "PNT";

    /// <summary>설비 유형에 대응하는 라인 공정 코드. 규칙이 없으면 null.</summary>
    public static string? ProcessFor(string? equipType) => equipType?.Trim().ToUpperInvariant() switch
    {
        TypeInjection => "INJ",
        TypeWrap => "IMG",
        TypePaint => "PNT",
        _ => null,
    };

    /// <summary>이 설비 유형을 이 라인 공정에 붙일 수 있는지. 규칙이 없으면 true.</summary>
    public static bool Allows(string? equipType, string? lineProcessCode)
    {
        var process = ProcessFor(equipType);
        return process is null || string.Equals(process, lineProcessCode?.Trim(), StringComparison.OrdinalIgnoreCase);
    }
}
