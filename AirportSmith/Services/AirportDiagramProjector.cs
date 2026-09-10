using AirportSmith.Models;
using AirportSmith.Models.Diagram;

namespace AirportSmith.Services;

// Pure computation (no I/O), so it's tested directly with plain xUnit facts
// rather than via the interface+fake pattern reserved for I/O boundaries
// like ISimConnectService.
//
// Coordinate approach: project runway lat/lon into the same local-meters
// plane TAXI_POINT/TAXI_PARKING's BIAS_X/BIAS_Z already use (a flat-earth/
// equirectangular approximation anchored on the airport reference point —
// accurate enough at airport scale), then normalize everything into
// canvas/screen-space meters as the final step. The Z-flip for north-up
// (screenY = maxZ - z) and the runway-heading-to-threshold convention below
// are both documented assumptions pending live-sim confirmation of
// BIAS_X/BIAS_Z's actual axis convention — see TaxiPathSegment/TaxiParkingSpot.
public static class AirportDiagramProjector
{
    private const double MetersPerDegLat = 111_320;
    private const double CanvasMarginMeters = 50;
    private const double MinCanvasSpanMeters = 100;

    // Raw local-meters coordinate in the flat-earth tangent plane, before
    // normalization to canvas space. Not the public Point2D (screen-space)
    // type — kept private so callers can't confuse the two coordinate spaces.
    private readonly record struct LocalPoint(double X, double Z);

    public static AirportDiagram Project(AirportDetails airport)
    {
        var metersPerDegLon = MetersPerDegLat * Math.Cos(DegToRad(airport.Latitude));

        LocalPoint ProjectLatLon(double lat, double lon) => new(
            (lon - airport.Longitude) * metersPerDegLon,
            (lat - airport.Latitude) * MetersPerDegLat);

        var runways = new List<(Runway Runway, LocalPoint Threshold1, LocalPoint Threshold2, LocalPoint[] Corners)>();
        foreach (var runway in airport.Runways)
        {
            var center = ProjectLatLon(runway.Latitude, runway.Longitude);
            var headingRad = DegToRad(runway.HeadingDeg);
            var forward = (X: Math.Sin(headingRad), Z: Math.Cos(headingRad));
            var halfLength = runway.LengthMeters / 2;
            var halfWidth = runway.WidthMeters / 2;

            // Documented assumption: the primary threshold sits in the
            // +heading direction from center, secondary in -heading —
            // affects only which end a label points at on the diagram, not
            // the rectangle's shape.
            var threshold1 = new LocalPoint(center.X + forward.X * halfLength, center.Z + forward.Z * halfLength);
            var threshold2 = new LocalPoint(center.X - forward.X * halfLength, center.Z - forward.Z * halfLength);

            var right = (X: Math.Cos(headingRad), Z: -Math.Sin(headingRad));
            var widthOffset = (X: right.X * halfWidth, Z: right.Z * halfWidth);

            var corners = new[]
            {
                new LocalPoint(threshold1.X + widthOffset.X, threshold1.Z + widthOffset.Z),
                new LocalPoint(threshold2.X + widthOffset.X, threshold2.Z + widthOffset.Z),
                new LocalPoint(threshold2.X - widthOffset.X, threshold2.Z - widthOffset.Z),
                new LocalPoint(threshold1.X - widthOffset.X, threshold1.Z - widthOffset.Z),
            };

            runways.Add((runway, threshold1, threshold2, corners));
        }

        var taxiways = new List<(TaxiPathSegment Segment, LocalPoint Start, LocalPoint End, LocalPoint[] WidthCorners, LocalPoint MidPoint)>();
        foreach (var segment in airport.TaxiPaths)
        {
            var type = (TaxiPathType)segment.Type;
            if (type is not (TaxiPathType.Taxi or TaxiPathType.Path))
                continue;
            if (segment.StartXMeters is not { } sx || segment.StartZMeters is not { } sz ||
                segment.EndXMeters is not { } ex || segment.EndZMeters is not { } ez)
                continue;

            var start = new LocalPoint(sx, sz);
            var end = new LocalPoint(ex, ez);
            var midPoint = new LocalPoint((sx + ex) / 2, (sz + ez) / 2);

            // Pavement footprint band from TaxiPathSegment.WidthMeters — the
            // centerline (Start/End) stays as-is for the existing
            // named/unnamed line styling. Perpendicular is the segment
            // direction rotated 90°; a zero-length segment (degenerate
            // Start == End) falls back to an arbitrary perpendicular since
            // there's no direction to rotate — it'll render as a point either way.
            var dx = ex - sx;
            var dz = ez - sz;
            var length = Math.Sqrt(dx * dx + dz * dz);
            var (perpX, perpZ) = length > 0 ? (-dz / length, dx / length) : (1.0, 0.0);
            var halfWidth = segment.WidthMeters / 2;
            var widthCorners = new[]
            {
                new LocalPoint(start.X + perpX * halfWidth, start.Z + perpZ * halfWidth),
                new LocalPoint(end.X + perpX * halfWidth, end.Z + perpZ * halfWidth),
                new LocalPoint(end.X - perpX * halfWidth, end.Z - perpZ * halfWidth),
                new LocalPoint(start.X - perpX * halfWidth, start.Z - perpZ * halfWidth),
            };

            taxiways.Add((segment, start, end, widthCorners, midPoint));
        }

        var parkingSpots = new List<(TaxiParkingSpot Spot, LocalPoint Center, LocalPoint HeadingTip)>();
        foreach (var spot in airport.ParkingSpots)
        {
            var center = new LocalPoint(spot.BiasXMeters, spot.BiasZMeters);
            var headingRad = DegToRad(spot.HeadingDeg);
            var tipDistance = spot.RadiusMeters * 1.5;
            var tip = new LocalPoint(
                center.X + Math.Sin(headingRad) * tipDistance,
                center.Z + Math.Cos(headingRad) * tipDistance);

            parkingSpots.Add((spot, center, tip));
        }

        var (minX, maxX, minZ, maxZ) = ComputeBounds(runways, taxiways, parkingSpots);

        Point2D ToScreen(LocalPoint p) => new(
            p.X - minX + CanvasMarginMeters,
            (maxZ - p.Z) + CanvasMarginMeters);

        return new AirportDiagram
        {
            CanvasWidth = (maxX - minX) + 2 * CanvasMarginMeters,
            CanvasHeight = (maxZ - minZ) + 2 * CanvasMarginMeters,
            Runways = runways.Select(r => new RunwayShape(
                r.Runway.PrimaryDesignation,
                r.Runway.SecondaryDesignation,
                ToScreen(r.Threshold1),
                ToScreen(r.Threshold2),
                r.Corners.Select(ToScreen).ToList())).ToList(),
            TaxiwaySegments = taxiways.Select(t => new TaxiwaySegmentShape(
                ToScreen(t.Start),
                ToScreen(t.End),
                !string.IsNullOrWhiteSpace(t.Segment.Name),
                t.Segment.Name,
                t.WidthCorners.Select(ToScreen).ToList(),
                ToScreen(t.MidPoint))).ToList(),
            ParkingSpots = parkingSpots.Select(p => new ParkingSpotShape(
                ToScreen(p.Center),
                p.Spot.RadiusMeters,
                ToScreen(p.HeadingTip))).ToList(),
        };
    }

    private static (double MinX, double MaxX, double MinZ, double MaxZ) ComputeBounds(
        List<(Runway Runway, LocalPoint Threshold1, LocalPoint Threshold2, LocalPoint[] Corners)> runways,
        List<(TaxiPathSegment Segment, LocalPoint Start, LocalPoint End, LocalPoint[] WidthCorners, LocalPoint MidPoint)> taxiways,
        List<(TaxiParkingSpot Spot, LocalPoint Center, LocalPoint HeadingTip)> parkingSpots)
    {
        var points = new List<LocalPoint>();
        foreach (var r in runways)
            points.AddRange(r.Corners);
        foreach (var t in taxiways)
            points.AddRange(t.WidthCorners);
        foreach (var p in parkingSpots)
        {
            var radius = p.Spot.RadiusMeters;
            points.Add(new LocalPoint(p.Center.X - radius, p.Center.Z - radius));
            points.Add(new LocalPoint(p.Center.X + radius, p.Center.Z + radius));
        }

        double minX = 0, maxX = 0, minZ = 0, maxZ = 0;
        if (points.Count > 0)
        {
            minX = points.Min(p => p.X);
            maxX = points.Max(p => p.X);
            minZ = points.Min(p => p.Z);
            maxZ = points.Max(p => p.Z);
        }

        if (maxX - minX < MinCanvasSpanMeters)
        {
            var cx = (minX + maxX) / 2;
            minX = cx - MinCanvasSpanMeters / 2;
            maxX = cx + MinCanvasSpanMeters / 2;
        }
        if (maxZ - minZ < MinCanvasSpanMeters)
        {
            var cz = (minZ + maxZ) / 2;
            minZ = cz - MinCanvasSpanMeters / 2;
            maxZ = cz + MinCanvasSpanMeters / 2;
        }

        return (minX, maxX, minZ, maxZ);
    }

    private static double DegToRad(double deg) => deg * Math.PI / 180;
}
