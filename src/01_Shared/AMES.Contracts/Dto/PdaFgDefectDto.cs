namespace AMES.Contracts.Dto;

public sealed record PdaFgDefectLot(string LotNo, string PartNo, string? PartName,
    decimal Qty, string? Unit, string? LocationNo, string? PalletNo,
    int LotId, string ProcessCode, string ConfirmStatus, int? DefectId = null,
    string? Reason = null);

public sealed record PdaFgDefectRequest(Guid RequestId, string LotNo, string PartNo,
    decimal ExpectedQty, string? ExpectedLocation, string? ExpectedPallet,
    string Reason, byte[] Photo, string PhotoType);

public sealed record PdaFgDefectResult(int DefectId, string LotNo, string Status);
