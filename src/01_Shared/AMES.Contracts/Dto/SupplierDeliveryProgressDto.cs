namespace AMES.Contracts.Dto;

public sealed record SupplierDeliveryProgressDto(string Number, string VendorId, string VendorName,
    DateTime DeliveryDate, DateTime? ShipDate, List<SupplierDeliveryLineDto> Lines)
{
    public bool Complete => Lines.Count > 0 && Lines.All(l => l.Remaining == 0);
    public string Status => Complete ? "Received" : Lines.Any(l => l.Received > 0) ? "PartiallyReceived"
        : Lines.All(l => l.Shipped) ? "OnTheWay" : "Registered";
    public int UnitItems => Lines.Sum(l => l.Items.Count);
    public int ReceivedItems => Lines.Sum(l => l.Items.Count(i => i.Received));
}

public sealed record SupplierDeliveryLineDto(int Id, string DeliveryNumber, string OrderNumber,
    string PartNo, string PartName, string Unit, decimal Quantity, decimal Received, bool Shipped,
    List<SupplierDeliveryItemDto> Items)
{
    public decimal Remaining => Math.Max(0, Quantity - Received);
}

public sealed record SupplierDeliveryItemDto(string Barcode, string? ShippingUnitNo, decimal Quantity, bool Received);
