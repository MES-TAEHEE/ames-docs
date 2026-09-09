namespace AMES.Contracts.Dto;

public static class PdaScenarioUsers
{
    public const string Simple = "SCTEST1";
    public const string Detailed = "SCTEST2";

    public static bool IsSimple(string? employeeNo) => Matches(employeeNo, Simple, "TEST1");
    public static bool IsDetailed(string? employeeNo) => Matches(employeeNo, Detailed, "TEST");
    public static bool IsAny(string? employeeNo) => IsSimple(employeeNo) || IsDetailed(employeeNo);

    private static bool Matches(string? employeeNo, string current, string legacy)
        => string.Equals(employeeNo, current, StringComparison.OrdinalIgnoreCase)
            || string.Equals(employeeNo, legacy, StringComparison.OrdinalIgnoreCase);
}
