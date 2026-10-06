namespace AMES.Data.Aps.Contracts;

/// 품번 마스터 한 줄 (REBUILD Data/PartRepository.cs:7) — DemandBuilder·ChainExpander 가 읽는다.
public sealed record PartInfo(string PartNo, string? LineCd, string? Alc, string? Pgn, double SafetyStock, string? PartName, double Uph = 0);

/// 일자별 실적 (REBUILD Data/ActualsRepository.cs:8) — 스펙 §2.1 목록에 따라 함께 옮긴다. Domain 직접 참조 없음.
public sealed record ActualDay(double Produced, double Shipped, double Defect, double Rework = 0);

/// 기준일 아침 재고 (REBUILD Services/StockService.cs:8) — ChainExpander 가 읽는다.
public sealed record OpeningInfo(double Qty, bool Counted, string? BaselineDate, int RolledDays);
