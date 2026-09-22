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
    // WGS84 ellipsoid constants (semi-major axis, flattening, first
    // eccentricity squared) — used to compute the meridian and prime-vertical
    // radii of curvature at the reference latitude, replacing a previous
    // single equatorial constant (111_320 m/deg) that was ~0.47% too large
    // for north-south distance away from the equator. Evaluated once, at
    // refLat only, by both ProjectLatLon and UnprojectLocalPoint below, so
    // the two stay exact inverses of each other (the same guarantee the old
    // flat constant gave, just latitude-dependent instead of fixed). This
    // is a local tangent-plane approximation good to well under a meter over
    // the few-km span the diagram/map tiles need — not a geodesic, and not
    // meant for points tens of km+ apart.
    private const double WgsSemiMajorAxisMeters = 6_378_137.0;
    private const double WgsFlattening = 1.0 / 298.257223563;
    private const double WgsEccentricitySquared = WgsFlattening * (2 - WgsFlattening);

    public static (double X, double Z) ProjectLatLon(double refLat, double refLon, double lat, double lon)
    {
        var (metersPerDegLat, metersPerDegLon) = MetersPerDegree(refLat);
        return ((lon - refLon) * metersPerDegLon, (lat - refLat) * metersPerDegLat);
    }

    public static (double Lat, double Lon) UnprojectLocalPoint(double refLat, double refLon, double x, double z)
    {
        var (metersPerDegLat, metersPerDegLon) = MetersPerDegree(refLat);
        return (refLat + z / metersPerDegLat, refLon + x / metersPerDegLon);
    }

    // Meters per degree of latitude (meridian radius M) and longitude (prime
    // vertical radius N, scaled by cos(refLat)) at refLat, per the standard
    // WGS84 closed-form formulas.
    private static (double MetersPerDegLat, double MetersPerDegLon) MetersPerDegree(double refLat)
    {
        var latRad = DegToRad(refLat);
        var sinLatSquared = Math.Sin(latRad) * Math.Sin(latRad);
        var meridianRadius = WgsSemiMajorAxisMeters * (1 - WgsEccentricitySquared)
            / Math.Pow(1 - WgsEccentricitySquared * sinLatSquared, 1.5);
        var primeVerticalRadius = WgsSemiMajorAxisMeters / Math.Sqrt(1 - WgsEccentricitySquared * sinLatSquared);

        return (meridianRadius * Math.PI / 180, primeVerticalRadius * Math.Cos(latRad) * Math.PI / 180);
    }

    private static double DegToRad(double deg) => deg * Math.PI / 180;
}
