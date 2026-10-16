namespace AMES.Contracts.Dto;

public sealed record PdaSparePartWorkOrder(string WorkOrderNo, string? EquipId, string? EquipName,
    string? Description, string Status)
{
    public string DisplayName => $"{WorkOrderNo} · {EquipId ?? EquipName ?? "No equipment"} · {Status}";
}
