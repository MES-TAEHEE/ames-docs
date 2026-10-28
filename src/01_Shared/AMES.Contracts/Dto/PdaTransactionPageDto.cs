namespace AMES.Contracts.Dto;

public sealed record PdaTransactionRowDto(long Id,DateTime OccurredAt,string Barcode,string? PartNo,
    string? PartName,decimal Qty,string? Unit,string Direction,string? LocationNo,string? WorkerId,
    string? ReasonCode,string? Note,decimal? BeforeQty,decimal? DeltaQty,decimal? AfterQty,
    string? BeforeLocation,string? AfterLocation,int? DeliveryId,int PartCount);

public sealed record PdaTransactionPageDto(List<PdaTransactionRowDto> Items,bool HasMore,
    DateTime? BeforeAt,long? BeforeId);

public sealed record PdaTransactionPartDto(string PartNo,string? PartName,decimal Qty,string? Unit,int LotCount);
