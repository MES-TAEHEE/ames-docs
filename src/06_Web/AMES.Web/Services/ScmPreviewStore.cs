namespace AMES.Web.Services;

/// <summary>Screen prototype data, isolated to the current Blazor circuit. No business DB writes.</summary>
public sealed class ScmPreviewStore
{
    public const string DemoVendor = "DEMO-V001";
    public sealed class OrderLine
    {
        public int PoID { get; set; }
        public decimal PendingDelivery { get; set; }
        public decimal AvailableDelivery => Math.Max(0, Quantity - Received - PendingDelivery);
        public int Id { get; set; } = 1;
        public string Item { get; set; } = "";
        public string Name { get; set; } = "";
        public string Unit { get; set; } = "";
        public decimal Quantity { get; set; } = 1;
        public decimal Price { get; set; }
        public decimal Received { get; set; }
        public decimal Amount => Quantity * Price;
        public OrderLine Copy() => (OrderLine)MemberwiseClone();
    }
    public sealed class Order
    {
        public string Number { get; set; } = "";
        public string Vendor { get; set; } = DemoVendor;
        public string VendorName { get; set; } = "";
        public List<OrderLine> Lines { get; set; } = [new()];
        public string Item => string.Join(", ", Lines.Select(l => l.Item));
        public string Name => string.Join(", ", Lines.Select(l => l.Name));
        public int LineCount => Lines.Count;
        public decimal Amount => Lines.Sum(l => l.Amount);
        public DateTime Ordered { get; set; } = DbClock.Today;
        public DateTime Due { get; set; } = DbClock.Today.AddDays(7);
        public string Destination { get; set; } = "EOS · WH-01";
        public string Status { get; set; } = "Draft";
        public bool Persistent { get; set; }
        public string Currency { get; set; } = "USD";
        public DateTime? SupplierConfirmedAt { get; set; }
        public string? SupplierConfirmedBy { get; set; }
        public Dictionary<int,string> Versions { get; set; } = new();
        public Order Copy()
        {
            var copy = (Order)MemberwiseClone();
            copy.Lines = Lines.Select(l => l.Copy()).ToList();
            copy.Versions = new(Versions);
            return copy;
        }
    }
    public sealed class Delivery
    {
        public string Version { get; set; } = "";
        public string VendorLotNo { get; set; } = "";
        public DateTime ProductionDate { get; set; }
        public DateTime? ShipDate { get; set; }
        public DateTime? ShippedAt { get; set; }
        public string? ShippedBy { get; set; }
        public int PoID { get; set; }
        public string Item { get; set; } = "";
        public string Unit { get; set; } = "";
        public decimal Received { get; set; }
        public string Number { get; set; } = "";
        public string OrderNumber { get; set; } = "";
        public int LineId { get; set; } = 1;
        public DateTime Date { get; set; } = DbClock.Today;
        public decimal Quantity { get; set; }
        public decimal Accepted { get; set; }
        public decimal Rejected { get; set; }
        public string Status { get; set; } = "Registered";
        public Delivery Copy() => (Delivery)MemberwiseClone();
    }
    public List<Order> Orders { get; } = new();
    public List<Delivery> Deliveries { get; } = new();
    public decimal Delivered(Order o) => o.Persistent ? o.Lines.Sum(l => l.Received) : Deliveries.Where(d => d.OrderNumber == o.Number && d.Status != "Cancelled").Sum(d => d.Quantity);
    public decimal Delivered(Order o, OrderLine line) => o.Persistent ? line.Received : Deliveries.Where(d => d.OrderNumber == o.Number && d.LineId == line.Id && d.Status != "Cancelled").Sum(d => d.Quantity);
    public decimal Remaining(Order o, OrderLine line) => Math.Max(0, line.Quantity - Delivered(o, line));
    public decimal Remaining(Order o) => o.Lines.Sum(l => Remaining(o, l));
    public OrderLine? FindLine(Delivery delivery) => Orders.Find(o => o.Number == delivery.OrderNumber)?.Lines.Find(l => l.Id == delivery.LineId);
    public string NextDelivery() => $"DEMO-DN-{Deliveries.Count + 1:000}";
}
