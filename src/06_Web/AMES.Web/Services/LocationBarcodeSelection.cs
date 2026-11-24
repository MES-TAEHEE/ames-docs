using AMES.Data.Repositories;

namespace AMES.Web.Services;

public static class LocationBarcodeSelection
{
    public static IReadOnlyList<string> Codes(MasterDataRepository.LocationRow location,
        IEnumerable<MasterDataRepository.LocationRow> allLocations) => location.LocationLevel == MasterDataRepository.LocationLevelPrimary
        ? allLocations.Where(r => r.ParentLocationID == location.LocationID && r.LocationLevel == MasterDataRepository.LocationLevelDetail && r.ActiveFlag)
            .OrderBy(r => r.LocationID).Select(r => r.LocationID).ToArray()
        : new[] { location.LocationID };

    public static bool? State(IEnumerable<string> codes, IReadOnlySet<string> selected)
    {
        var targets=codes.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var count=targets.Count(selected.Contains);
        return count==0 ? false : count==targets.Length ? true : null;
    }

    public static void Toggle(IEnumerable<string> codes, HashSet<string> selected)
    {
        var targets=codes.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var select=State(targets,selected)!=true;
        foreach(var code in targets)
            if(select) selected.Add(code); else selected.Remove(code);
    }
}
