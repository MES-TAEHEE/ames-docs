using AMES.Data.Repositories;

namespace AMES.Web.Services;

public sealed record DeliveryPrintBundle(ScmRepository.DeliveryNote Note,
    IReadOnlyList<ScmRepository.DeliveryCase> ShippingUnits,IReadOnlyList<ScmRepository.DeliveryBox> UnitItems)
{
    public IReadOnlyList<ShippingLabel> UnitLabels => ShippingUnits.Select(EnglishPrint.ShippingUnit).ToArray();
    public IReadOnlyList<ShippingLabel> ItemLabels => UnitItems.Select(EnglishPrint.UnitItem).ToArray();
    public IReadOnlyList<ShippingLabel> AllLabels => new[] { EnglishPrint.DeliveryNote(Note) }.Concat(UnitLabels).Concat(ItemLabels).ToArray();

    public static DeliveryPrintBundle Load(ScmRepository repository,string number,string user)
    {
        var note=repository.GetDeliveryNote(number,user) ?? throw new InvalidOperationException("Delivery note is unavailable.");
        var deliveries=repository.ListNoteDeliveries(user).Where(d=>string.Equals(d.NoteNumber,number,StringComparison.OrdinalIgnoreCase) || string.Equals(d.Number,number,StringComparison.OrdinalIgnoreCase))
            .Select(d=>d.Number).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if(deliveries.Length==0)throw new InvalidOperationException("Delivery note is unavailable.");
        var units=repository.ListDeliveryCases(null,user,note:number).OrderBy(c=>c.CreatedAt).ThenBy(c=>c.Number).ToArray();
        var items=deliveries.SelectMany(d=>repository.ListDeliveryBoxes(d,user)).DistinctBy(b=>b.Id).OrderBy(b=>b.Delivery).ThenBy(b=>b.Sequence).ToArray();
        var itemNumbers=items.Select(i=>i.Number).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if(units.SelectMany(c=>c.Boxes).Any(b=>!itemNumbers.Contains(b.Number)))
            throw new InvalidOperationException("Shipping unit labels changed. Refresh the delivery note.");
        _=repository.GetDeliveryNote(number,user) ?? throw new InvalidOperationException("Delivery note is unavailable.");
        return new(note,units,items);
    }
}
