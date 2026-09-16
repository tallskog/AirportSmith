namespace AirportSmith.Services;

// Shared flat-earth/equirectangular approximation anchored on an airport's
// reference point — the same local-meters plane TAXI_POINT/TAXI_PARKING's
// BIAS_X/BIAS_Z already live in, and what AirportDiagramProjector projects
// runway/taxiway lat/lon into for rendering. Accurate enough at airport
// scale (not for long-range navigation math). Extracted out of
// AirportDiagramProjector (which only ever needed the forward direction) so
// AirportXmlExporter can reuse the exact same math for the inverse
// direction — computing <RunwayStart> lat/lon from a runway's stored
// center/heading/length — without the two ever drifting apart.
public static class GeoProjection
{
    public const double MetersPerDegLat = 111_320;

    public static (double X, double Z) ProjectLatLon(double refLat, double refLon, double lat, double lon)
    {
        var metersPerDegLon = MetersPerDegLat * Math.Cos(DegToRad(refLat));
        return ((lon - refLon) * metersPerDegLon, (lat - refLat) * MetersPerDegLat);
    }

    public static (double Lat, double Lon) UnprojectLocalPoint(double refLat, double refLon, double x, double z)
    {
        var metersPerDegLon = MetersPerDegLat * Math.Cos(DegToRad(refLat));
        return (refLat + z / MetersPerDegLat, refLon + x / metersPerDegLon);
    }

    private static double DegToRad(double deg) => deg * Math.PI / 180;
}
