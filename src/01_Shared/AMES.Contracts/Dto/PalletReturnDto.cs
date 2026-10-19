namespace AMES.Contracts.Dto;

public sealed record PalletReturnDto(string PalletNo, string? CustomerId, string? CustomerName,
    DateTime ShippedAt, DateTime? ReturnedAt);

public sealed record PalletReturnRequest(string Barcode, DateTime ExpectedShippedAt);
