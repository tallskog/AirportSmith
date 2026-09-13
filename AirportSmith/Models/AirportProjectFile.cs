namespace AirportSmith.Models;

// The on-disk shape AirportProjectStore reads/writes under
// AppDataHelper.AppDataPath\Projects\{ICAO}.json. Unlike DebugDataStore's
// throwaway export (a bare AirportDetails dump to a temp folder), this is the
// first real persisted project format, so it carries a SchemaVersion for
// future migrations. All properties are plain, non-required value
// types/nullable references so System.Text.Json defaults any field missing
// from an older file (bool -> false, enum -> 0, nullable -> null) instead of
// throwing — see AirportProjectStoreTests for the back-compat contract this
// must keep satisfying as the schema grows.
//
// v1 -> v2: TaxiPathSegment.Name (a free-text string) was replaced by
// TaxiNameId (a reference into the new AirportDetails.TaxiNames list) — see
// TaxiName.cs. This is a reshape, not just an added field, so
// AirportProjectStore.Load migrates a v1 file's legacy names into the new
// shape rather than letting System.Text.Json silently discard the
// now-unknown "Name" property.
public class AirportProjectFile
{
    public int SchemaVersion { get; set; } = 2;
    public DateTime SavedAtUtc { get; set; }
    public AirportDetails Airport { get; set; } = new();
}
