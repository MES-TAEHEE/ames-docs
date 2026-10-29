using AMES.Contracts.Dto;

namespace AMES.Web.Services;

public static class ShipmentPlanRows
{
    public sealed record Delivery(int DeliveryId, string DeliveryNo, string CustomerId, DateTime ShipDate, List<ShippingOrderDto> Orders)
    {
        public string Status => Orders.Any(o => o.Status == "Loading") ? "Loading" : "Open";
        public IEnumerable<ShippingOrderLineDto> Lines => Orders.SelectMany(o => o.Lines);
    }

    public static bool IsUnshipped(ShippingOrderDto order) => order.Status is "Open" or "Loading";

    public static List<Delivery> FromOrders(IEnumerable<ShippingOrderDto> orders) => orders
        .Where(o => o.DeliveryId.HasValue && !string.IsNullOrWhiteSpace(o.DeliveryNo) && IsUnshipped(o))
        .GroupBy(o => o.DeliveryId!.Value)
        .Select(group => new Delivery(group.Key, group.First().DeliveryNo!, group.First().CustomerId,
            group.First().ShipDate, group.ToList()))
        .OrderBy(d => d.ShipDate).ThenBy(d => d.DeliveryNo).ToList();
}
