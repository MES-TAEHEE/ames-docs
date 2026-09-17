namespace AMES.Contracts.Dto;

public static class PdaScenarioUsers
{
    public const string Simple = "SCTEST1";
    public const string Detailed = "SCTEST2";

    public static bool IsSimple(string? employeeNo) => Matches(employeeNo, Simple);
    public static bool IsDetailed(string? employeeNo) => Matches(employeeNo, Detailed);
    public static bool IsAny(string? employeeNo) => IsSimple(employeeNo) || IsDetailed(employeeNo);

    private static bool Matches(string? employeeNo, string current)
        => string.Equals(employeeNo, current, StringComparison.OrdinalIgnoreCase);
}
