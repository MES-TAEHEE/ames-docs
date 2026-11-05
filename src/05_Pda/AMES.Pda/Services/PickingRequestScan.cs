using AMES.Contracts.Dto;

namespace AMES.Pda.Services;

public static class PickingRequestScan
{
    public static (string Kind, PickingRequestOptionDto? Option) Resolve(string? barcode, PickingRequestOptionsDto options, string? destination = null)
    {
        var value=(barcode??"").Replace("\u0002","").Replace("\u0003","").Trim();
        var location=options.Destinations.FirstOrDefault(p=>string.Equals(p.Code,value,StringComparison.OrdinalIgnoreCase));
        var part=options.Parts.FirstOrDefault(p=>string.Equals(p.Code,value,StringComparison.OrdinalIgnoreCase));
        if(location is not null && part is not null)return ("AMBIGUOUS",null);
        if(location is not null)return ("LOCATION",location);
        if(part is not null)return options.Destinations.Any(p=>string.Equals(p.Code,destination,StringComparison.OrdinalIgnoreCase))
            ? ("PART",part) : ("LOCATION_REQUIRED",null);
        return ("UNKNOWN",null);
    }
}
