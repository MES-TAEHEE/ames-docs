namespace AMES.Contracts.Dto;

public sealed record PickingRequestOptionDto(string Code, string Display, string Unit);
public sealed record PickingRequestOptionsDto(List<PickingRequestOptionDto> Destinations,
    List<PickingRequestOptionDto> Parts, DateTime Today, bool CanCreate);
public sealed record CreatePickingRequestDto(string Destination, string PartNo, decimal Qty, DateTime RequiredAt = default, bool ConfirmAdditional = false);
public sealed record PickingRequestCreatedDto(string PickSlipNo);
public sealed record ExistingPickingRequestDto(string PickSlipNo, string Destination, string PartNo,
    decimal RequestedQty, decimal PickedQty, decimal RemainingQty, string Unit, string Status);
public sealed record PickingRequestConflictDto(string Message, List<ExistingPickingRequestDto> ExistingOrders);
