namespace AMES.Devices;

/// <summary>사출 반제품 태그 1장 분량의 데이터. DataMatrix 내용 = LotCode.</summary>
public sealed record ZplLabel(
    string   LotCode,
    string   ItemNo,
    string?  ItemName,
    string?  ColorCode,
    string?  CavityPos,
    string?  LineId,
    DateTime ProducedAt);
