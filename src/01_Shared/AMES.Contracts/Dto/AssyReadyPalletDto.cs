namespace AMES.Contracts.Dto;

public sealed record AssyReadyPartDto(string PartNo,string? PartName,decimal Qty,string? Unit,int LotCount);
public sealed record AssyReadyLotDto(string LotNo,string PartNo,string? PartName,decimal Qty,string? Unit,DateTime? ReadyAt);
public sealed record AssyReadyPalletDto(string? PalletNo,DateTime? ReadyAt,int LotCount,List<AssyReadyPartDto> Parts,
    List<AssyReadyLotDto>? Lots = null);
