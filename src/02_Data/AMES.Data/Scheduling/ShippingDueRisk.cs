using AMES.Contracts.Dto;

namespace AMES.Data.Scheduling;

public static class ShippingDueRisk
{
    public static DateTime NextWorkday(DateTime today, WorkdayCalendar calendar)
    {
        var date = today.Date.AddDays(1);
        while (!calendar.IsWorkday(date)) date = date.AddDays(1);
        return date;
    }

    public static ShippingDueRiskLevel Evaluate(DateTime today, DateTime nextWorkday, DateTime? due,
        decimal? ordered, decimal? shipped, string? status, bool identified = true)
    {
        if (status?.Trim().ToUpperInvariant() is "CANCELLED" or "CANCELED" or "CLOSED" or "SHIPPED")
            return ShippingDueRiskLevel.None;
        if (ordered >= 0 && shipped >= 0 && shipped >= ordered) return ShippingDueRiskLevel.None;
        if (!identified || due is null || ordered is null || shipped is null || ordered < 0 || shipped < 0)
            return ShippingDueRiskLevel.Unknown;
        if (due.Value.Date < today.Date) return ShippingDueRiskLevel.Overdue;
        return due.Value.Date <= nextWorkday.Date ? ShippingDueRiskLevel.DueSoon : ShippingDueRiskLevel.Normal;
    }
}
