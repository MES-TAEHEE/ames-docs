namespace AMES.Contracts.Dto;

public sealed record PickingPartOrder(string PickSlipNo,string LocationNo,decimal RemainingQty);
public sealed record PickingPartDto(string PartNo,string? PartName,string Unit,decimal RemainingQty,
    string? WarehouseLocations,List<PickingPartOrder> Orders);
