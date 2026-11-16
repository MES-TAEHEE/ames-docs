using System.Globalization;
using AMES.Contracts.Formatting;
using AMES.Data.Repositories;

namespace AMES.Web.Services;

public static class EnglishPrint
{
    public static string Date(DateTime? value) => value?.ToString("MM/dd/yyyy",CultureInfo.InvariantCulture) ?? "—";
    public static string DateTime(DateTime value) => value.ToString("MM/dd/yyyy HH:mm",CultureInfo.InvariantCulture);
    public static string Number(decimal value,int? digits=null) => UomQty.Format(value,digits,CultureInfo.InvariantCulture);
    public static string Quantity(decimal value,string? unit,int? digits=null) =>
        $"{Number(value,digits)} {unit?.Trim().ToUpperInvariant()}".TrimEnd();
    public static ShippingLabel DeliveryNote(ScmRepository.DeliveryNote n) => new(n.Number,"DELIVERY NOTE",n.Vendor,"",
        $"{n.Buyer} · {n.Destination}",string.Join(" / ",n.Lines.GroupBy(l=>l.Unit).Select(g=>Quantity(g.Sum(l=>l.Quantity),g.Key))),
        $"PO {n.OrderNumber} · {Date(n.ShipDate)}",Destination:n.Destination,DeliveryDate:Date(n.ShipDate));
    public static ShippingLabel UnitItem(ScmRepository.DeliveryBox b) => new(b.Number,"UNIT ITEM (UIT)",b.VendorName,b.Item,b.Name,
        Quantity(b.Quantity,b.Unit),$"LOT {b.VendorLotNo} · PO {b.Order} · {b.Sequence}/{b.BoxCount}",
        Fifo:Date(b.ProductionDate),Destination:b.Destination,Order:b.Order,Lot:b.VendorLotNo,Sequence:$"{b.Sequence}/{b.BoxCount}");
    public static ShippingLabel ShippingUnit(ScmRepository.DeliveryCase c) => new(c.Number,"SHIPPING UNIT (SHU)",c.VendorName,"",
        $"{c.Boxes.Count} Unit Items",string.Join(" / ",c.Boxes.GroupBy(b=>b.Unit).Select(g=>Quantity(g.Sum(b=>b.Quantity),g.Key))),
        $"{c.NoteNumber} · PO {c.Order}",ParentNumber:c.NoteNumber,UnitItemCount:c.Boxes.Count);
}
