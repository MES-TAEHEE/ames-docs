namespace AMES.Data.Services;

public static class LocationCapacity
{
    public static bool IsFull(decimal qty, decimal? capacity)
        => capacity is > 0 && qty >= capacity.Value;

    public static bool ShouldWarnOnReduction(decimal qty, decimal? previous, decimal? proposed)
        => proposed is > 0 && qty > proposed.Value
            && (previous is null or <= 0 || proposed.Value < previous.Value);
}
