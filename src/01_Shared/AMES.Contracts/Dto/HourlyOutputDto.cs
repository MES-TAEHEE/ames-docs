namespace AMES.Contracts.Dto;

/// <summary>One bar of the hourly output chart on INJ-02 / INJ-07.</summary>
public sealed class HourlyOutputDto
{
    public required int Hour     { get; init; }   // 0..23
    public required int GoodQty  { get; init; }
    public required int DefectQty{ get; init; }
}
