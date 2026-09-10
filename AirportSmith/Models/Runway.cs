namespace AirportSmith.Models;

public class Runway
{
    public string PrimaryDesignation { get; set; } = string.Empty;
    public string SecondaryDesignation { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double ElevationMeters { get; set; }
    public double HeadingDeg { get; set; }
    public double LengthMeters { get; set; }
    public double WidthMeters { get; set; }

    // Enum-backed per SimConnect (0-32 pavement types, 254=UNKNOWN, 255=UNDEFINED);
    // kept raw until worth mapping to friendly labels.
    public int SurfaceType { get; set; }

    // VASI/PAPI light data per runway end/side, from the RUNWAY facility type's
    // four nested VASI slots. Null means no VASI/PAPI installed for that
    // end/side — confirmed against a live sim that SimConnect reports this as
    // Type 0 (with a meaningless default Angle) rather than omitting the row,
    // and SimConnectService maps that to null here. Type is otherwise
    // enum-backed (1-13) per SimConnect; kept raw for the same reason as
    // SurfaceType above.
    public int? PrimaryLeftVasiType { get; set; }
    public double? PrimaryLeftVasiAngleDeg { get; set; }
    public int? PrimaryRightVasiType { get; set; }
    public double? PrimaryRightVasiAngleDeg { get; set; }
    public int? SecondaryLeftVasiType { get; set; }
    public double? SecondaryLeftVasiAngleDeg { get; set; }
    public int? SecondaryRightVasiType { get; set; }
    public double? SecondaryRightVasiAngleDeg { get; set; }
}
