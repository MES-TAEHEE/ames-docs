namespace AMES.Contracts.Dto;

public sealed record PdaMntAccess(string? UserId, bool IsAdmin, bool CanRead, bool CanEdit, bool CanAct,
    bool CanReadWork, bool CanReadEquip, bool CanReadMaint)
{
    public bool IsAssignedTo(string? technicianId)
        => !string.IsNullOrWhiteSpace(UserId) && string.Equals(UserId, technicianId, StringComparison.Ordinal);
}
public sealed record PdaMntSchedule(int Id, string Number, string EquipId, string EquipName, string PmClass,
    string PmType, string? CycleBasis, int? CycleValue, DateTime? DueDate, string? TemplateId,
    string? TechId, string Status, int? WorkOrderId, bool InProgress, DateTime? ModifiedAt);
public sealed record PdaMntWork(int Id, string Number, string? EquipId, string? EquipName, string? TechId,
    string? Status, string? Description, string? PmClass, DateTime? DueDate, int TaskCount, int TaskDone,
    int TaskFail, bool NeedsNextDue, bool RequiresLoto, DateTime? StartedAt);
public sealed record PdaMntTask(int Id, int WorkOrderId, int? Sequence, string? Name, string? Criteria,
    string? SparePartNo, string? PartName, int? RequiredQty, int? DurationMin, string? Result,
    string? Note, string? CompletedBy, DateTime? CompletedAt, DateTime? ModifiedAt);
public sealed record PdaMntBoard(DateTime Today, int LeadDays, PdaMntAccess Access,
    List<PdaMntSchedule> Schedules, List<PdaMntWork> WorkOrders);
public sealed record PdaMntDetail(PdaMntWork Work, List<PdaMntTask> Tasks);
public sealed record PdaMntStart(bool LotoConfirmed);
public sealed record PdaMntTaskSave(string Result, string? Note, DateTime? ExpectedModifiedAt);
public sealed record PdaMntComplete(string Result, string Action, string? RootCause, int? LaborMinutes, DateTime? NextDue);
public sealed record PdaMntSaved(string Message, string? NextWorkOrder = null, string? FollowUpWorkOrder = null);
