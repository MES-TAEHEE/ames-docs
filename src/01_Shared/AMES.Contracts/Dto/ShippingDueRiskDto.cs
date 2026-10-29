namespace AMES.Contracts.Dto;

public enum ShippingDueRiskLevel { None, Normal, DueSoon, Overdue, Unknown }
public sealed record ShippingDueRiskDto(int SoId,string? SoNumber,string? CustomerId,string? PartNo,string? PartName,
    DateTime? DueDate,decimal? OrderQty,decimal? ShippedQty,decimal? RemainingQty,ShippingDueRiskLevel Risk);
public sealed record ShippingDueRiskSnapshot(DateTime AsOf,DateTime NextWorkday,List<ShippingDueRiskDto> Rows);
