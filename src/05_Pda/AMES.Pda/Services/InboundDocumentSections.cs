namespace AMES.Pda.Services;

public static class InboundDocumentSections
{
    public sealed record Section(string? ShippingUnitNo,IReadOnlyList<PdaApi.InboundDocumentLineRow> Lines,
        IReadOnlyList<PdaApi.InboundDocumentBoxRow> Boxes);

    public static IReadOnlyList<Section> Build(bool isDeliveryNote,IReadOnlyList<PdaApi.InboundDocumentLineRow> lines,
        IReadOnlyList<PdaApi.InboundDocumentBoxRow> boxes)
    {
        if(!isDeliveryNote)return [new(null,lines,boxes)];
        return boxes.GroupBy(box=>box.ShippingUnitNo??"",StringComparer.OrdinalIgnoreCase).Select(unit=>
        {
            var parts=unit.GroupBy(box=>box.PartNo,StringComparer.OrdinalIgnoreCase).Select(part=>
            {
                var received=part.Where(box=>string.Equals(box.Yn,"Y",StringComparison.OrdinalIgnoreCase)).ToList();
                return new PdaApi.InboundDocumentLineRow(part.Key,
                    lines.FirstOrDefault(line=>string.Equals(line.PartNo,part.Key,StringComparison.OrdinalIgnoreCase))?.PartName,
                    part.Count(),received.Count,part.Sum(box=>box.Qty),received.Sum(box=>box.Qty),
                    part.Sum(box=>box.Qty)-received.Sum(box=>box.Qty),part.First().Unit,
                    received.Count==part.Count()?"Y":"N");
            }).ToList();
            return new Section(unit.Key,parts,unit.ToList());
        }).ToList();
    }
}
