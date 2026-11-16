using AMES.Data.Repositories;

namespace AMES.Web.Services;

public static class LocationBarcodeSelection
{
    public static IReadOnlyList<string> Codes(MasterDataRepository.LocationRow location,
        IEnumerable<MasterDataRepository.LocationRow> allLocations) => location.LocationLevel == MasterDataRepository.LocationLevelPrimary
        ? allLocations.Where(r => r.ParentLocationID == location.LocationID && r.LocationLevel == MasterDataRepository.LocationLevelDetail && r.ActiveFlag)
            .OrderBy(r => r.LocationID).Select(r => r.LocationID).ToArray()
        : new[] { location.LocationID };
}
