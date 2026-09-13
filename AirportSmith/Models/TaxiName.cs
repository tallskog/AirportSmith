namespace AirportSmith.Models;

// One entry from the sim's TAXI_NAME facility rows, now a first-class,
// user-editable list on AirportDetails rather than something SimConnectService
// resolves into a flat per-segment string and discards (see
// TaxiPathSegment.TaxiNameId). Id is a stable identity independent of list
// position — TaxiPathSegment references a name by Id, not by array index, so
// renaming or deleting an entry never silently misaligns another segment's
// reference the way shifting array indices would.
public class TaxiName
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Value { get; set; } = string.Empty;
}
