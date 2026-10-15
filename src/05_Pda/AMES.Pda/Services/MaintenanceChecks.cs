using AMES.Contracts.Dto;

namespace AMES.Pda.Services;

public static class MaintenanceChecks
{
    public static bool MatchesAssignment(PdaMntAccess access, string? technicianId, bool onlyMine)
        => !onlyMine || access.IsAssignedTo(technicianId);
    public static string[] StatusOptions(bool schedule, bool includeCompleted)
        => schedule ? ["OK", "DUE", "OVERDUE"]
        : includeCompleted ? [MwoStatusCodes.Issued, MwoStatusCodes.Assigned, MwoStatusCodes.InProgress, MwoStatusCodes.Completed, MwoStatusCodes.Closed]
        : [MwoStatusCodes.Issued, MwoStatusCodes.Assigned, MwoStatusCodes.InProgress];
    public static bool MatchesValue(string? value, string filter)
        => filter == "" || (filter == "__NONE" ? string.IsNullOrWhiteSpace(value)
            : string.Equals(value, filter, StringComparison.OrdinalIgnoreCase));
    public static bool MatchesSchedule(PdaMntSchedule row, PdaMntAccess access,
        string type, string basis, string status, bool onlyMine)
        => MatchesValue(row.PmType, type) && MatchesValue(row.CycleBasis, basis)
            && MatchesValue(row.Status, status) && MatchesAssignment(access, row.TechId, onlyMine);

    public static bool IsSaved(PdaMntTask task) => task.Result is "PASS" or "FAIL" or "NA";
    public static bool AllSaved(IReadOnlyList<PdaMntTask> tasks) => tasks.Count > 0 && tasks.All(IsSaved);
    public static bool RequiresNote(string result, string? note) => result == "FAIL" && string.IsNullOrWhiteSpace(note);
    public static string StepState(IReadOnlyList<PdaMntTask> tasks, PdaMntTask task)
        => IsSaved(task) ? "done"
        : tasks.TakeWhile(t => t.Id != task.Id).Any(t => !IsSaved(t)) ? "upcoming" : "active";
}
