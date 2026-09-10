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
// (screenY = maxZ - z) is a documented assumption pending live-sim
// confirmation of BIAS_X/BIAS_Z's actual axis convention — see
// TaxiPathSegment/TaxiParkingSpot. The runway-heading-to-threshold
// convention (which end is "primary") was corrected after live testing
// against OIBK showed it backwards — see the comment at threshold1/2 below.
//
// Displaced-threshold/blast-pad/overrun marking geometry (colors, threshold
// bar, demarcation bar, arrow, chevrons) follows FAA AIM 2-3-3 — see the
// link in requirements.md's reference list.
public static class AirportDiagramProjector
{
    private const double MetersPerDegLat = 111_320;
    private const double CanvasMarginMeters = 50;
    private const double MinCanvasSpanMeters = 100;

    // Raw local-meters coordinate in the flat-earth tangent plane, before
    // normalization to canvas space. Not the public Point2D (screen-space)
    // type — kept private so callers can't confuse the two coordinate spaces.
    private readonly record struct LocalPoint(double X, double Z);

    // AIM 2-3-3: the displaced-threshold zone (still usable runway pavement,
    // just not for landing) is marked entirely in WHITE — a threshold bar at
    // the displaced threshold itself, and an arrow along the centerline
    // pointing at it. One arrow drawn, not the repeated arrowheads AIM's
    // figure shows across the full width — a diagram-level simplification.
    private sealed record ThresholdMarkingWorking(
        LocalPoint[] ZoneCorners,
        LocalPoint[] ThresholdBar,
        LocalPoint ArrowShaftStart,
        LocalPoint ArrowShaftEnd,
        LocalPoint[] ArrowHead);

    // AIM 2-3-3: a blast pad or overrun/stopway (pavement NOT usable for
    // landing/takeoff/taxi) is marked entirely in YELLOW — a demarcation bar
    // at the boundary with the runway, and chevrons within it. Chevron count
    // is capped/spaced by this code, not a literal reproduction of AIM's
    // figure spacing.
    private sealed record ExtensionWorking(
        LocalPoint[] Corners,
        LocalPoint[] DemarcationBar,
        LocalPoint[][] Chevrons);

    // Working data for one runway during projection, before the final
    // ToScreen pass.
    private sealed record RunwayWorkingData(
        Runway Runway,
        LocalPoint Threshold1,
        LocalPoint Threshold2,
        LocalPoint[] Corners,
        LocalPoint PrimaryLabelPosition,
        LocalPoint SecondaryLabelPosition,
        ThresholdMarkingWorking? PrimaryThresholdMarking,
        ExtensionWorking? PrimaryBlastPad,
        ExtensionWorking? PrimaryOverrun,
        ThresholdMarkingWorking? SecondaryThresholdMarking,
        ExtensionWorking? SecondaryBlastPad,
        ExtensionWorking? SecondaryOverrun);

    public static AirportDiagram Project(AirportDetails airport)
    {
        var metersPerDegLon = MetersPerDegLat * Math.Cos(DegToRad(airport.Latitude));

        LocalPoint ProjectLatLon(double lat, double lon) => new(
            (lon - airport.Longitude) * metersPerDegLon,
            (lat - airport.Latitude) * MetersPerDegLat);

        // Both endpoints must already be in the local-meters plane — builds
        // the 4 corners of a rectangle spanning between them, offset by
        // +/-halfWidthOffset on each end. Shared by every pavement rectangle
        // below (the runway itself, threshold bars, extensions, demarcation
        // bars).
        static LocalPoint[] BuildRectangle(LocalPoint a, LocalPoint b, (double X, double Z) halfWidthOffset) =>
        [
            new LocalPoint(a.X + halfWidthOffset.X, a.Z + halfWidthOffset.Z),
            new LocalPoint(b.X + halfWidthOffset.X, b.Z + halfWidthOffset.Z),
            new LocalPoint(b.X - halfWidthOffset.X, b.Z - halfWidthOffset.Z),
            new LocalPoint(a.X - halfWidthOffset.X, a.Z - halfWidthOffset.Z),
        ];

        var runways = new List<RunwayWorkingData>();
        foreach (var runway in airport.Runways)
        {
            var center = ProjectLatLon(runway.Latitude, runway.Longitude);
            var headingRad = DegToRad(runway.HeadingDeg);
            var forward = (X: Math.Sin(headingRad), Z: Math.Cos(headingRad));
            var halfLength = runway.LengthMeters / 2;
            var halfWidth = runway.WidthMeters / 2;

            // HeadingDeg is the heading of travel when using the primary
            // end — i.e. the direction you roll after touching down at the
            // primary threshold (or depart toward). That means the primary
            // threshold itself sits at the -heading end of the runway (you
            // start there and travel +heading), not the +heading end. Only
            // affects which end a label points at on the diagram, not the
            // rectangle's shape.
            var threshold1 = new LocalPoint(center.X - forward.X * halfLength, center.Z - forward.Z * halfLength);
            var threshold2 = new LocalPoint(center.X + forward.X * halfLength, center.Z + forward.Z * halfLength);

            var right = (X: Math.Cos(headingRad), Z: -Math.Sin(headingRad));
            var widthOffset = (X: right.X * halfWidth, Z: right.Z * halfWidth);

            var corners = BuildRectangle(threshold1, threshold2, widthOffset);

            // Nudge each designation label inward from its threshold along
            // the centerline so it sits on the pavement, not the runway's
            // very edge. Capped at 40m so a short runway doesn't push both
            // labels past each other into the middle.
            var labelInset = Math.Min(halfLength * 0.3, 40);
            var primaryLabelPosition = new LocalPoint(threshold1.X + forward.X * labelInset, threshold1.Z + forward.Z * labelInset);
            var secondaryLabelPosition = new LocalPoint(threshold2.X - forward.X * labelInset, threshold2.Z - forward.Z * labelInset);

            // Displaced threshold: WITHIN the runway's own pavement, inset
            // from that end's threshold inward by LengthMeters. Uses the
            // runway's own width (widthOffset), not the feature's — AIM's
            // markings span the full runway.
            ThresholdMarkingWorking? ThresholdMarking(RunwayPavementFeature? feature, LocalPoint threshold, (double X, double Z) inwardDir)
            {
                if (feature is not { LengthMeters: > 0 } f) return null;

                var innerPoint = new LocalPoint(threshold.X + inwardDir.X * f.LengthMeters, threshold.Z + inwardDir.Z * f.LengthMeters);
                var zoneCorners = BuildRectangle(threshold, innerPoint, widthOffset);

                // 10ft (~3m) threshold bar, right at the displaced threshold.
                var barDepth = Math.Min(3, f.LengthMeters * 0.15);
                var barNearEdge = new LocalPoint(innerPoint.X - inwardDir.X * barDepth, innerPoint.Z - inwardDir.Z * barDepth);
                var thresholdBar = BuildRectangle(barNearEdge, innerPoint, widthOffset);

                // Centerline arrow, shaft starting a little in from the very
                // edge, head pointing at (just before) the bar.
                var shaftStart = new LocalPoint(threshold.X + inwardDir.X * f.LengthMeters * 0.15, threshold.Z + inwardDir.Z * f.LengthMeters * 0.15);
                var shaftEnd = barNearEdge;
                var headDepth = Math.Min(8, f.LengthMeters * 0.2);
                var headHalfWidth = halfWidth * 0.5;
                var headBase = new LocalPoint(shaftEnd.X - inwardDir.X * headDepth, shaftEnd.Z - inwardDir.Z * headDepth);
                var arrowHead = new[]
                {
                    shaftEnd,
                    new LocalPoint(headBase.X + right.X * headHalfWidth, headBase.Z + right.Z * headHalfWidth),
                    new LocalPoint(headBase.X - right.X * headHalfWidth, headBase.Z - right.Z * headHalfWidth),
                };

                return new ThresholdMarkingWorking(zoneCorners, thresholdBar, shaftStart, shaftEnd, arrowHead);
            }

            // Blast pad/overrun: pavement extending OUTWARD, beyond that
            // end's threshold — using the feature's own WidthMeters (falling
            // back to the runway's width if unset/zero), per the standard
            // apt.dat-style convention that both are attached to, and extend
            // away from, the specific end they're named after.
            ExtensionWorking? Extension(RunwayPavementFeature? feature, LocalPoint threshold, (double X, double Z) outwardDir)
            {
                if (feature is not { LengthMeters: > 0 } f) return null;

                var featureHalfWidth = (f.WidthMeters > 0 ? f.WidthMeters : runway.WidthMeters) / 2;
                var featureWidthOffset = (X: right.X * featureHalfWidth, Z: right.Z * featureHalfWidth);
                var outer = new LocalPoint(threshold.X + outwardDir.X * f.LengthMeters, threshold.Z + outwardDir.Z * f.LengthMeters);
                var extCorners = BuildRectangle(threshold, outer, featureWidthOffset);

                // 3ft (~1m) demarcation bar at the runway/extension boundary.
                var barDepth = Math.Min(1, f.LengthMeters * 0.1);
                var barFarEdge = new LocalPoint(threshold.X + outwardDir.X * barDepth, threshold.Z + outwardDir.Z * barDepth);
                var demarcationBar = BuildRectangle(threshold, barFarEdge, featureWidthOffset);

                // AIM chevrons meet at a 90° tip and tile with no gap — the
                // tip of one chevron sits exactly where the previous
                // chevron's arm-ends are. A 90° vertex angle means each arm
                // makes a 45° angle with the centerline, which requires
                // armHalfWidth == chevronDepth (opposite/adjacent legs equal
                // in a 45-45-90 triangle); the tile period is then just
                // chevronDepth, so consecutive chevrons directly adjoin.
                // Capped at the feature's own LengthMeters so a short, wide
                // extension (e.g. a stubby overrun on a wide runway) gets a
                // proportioned chevron that fits within its own footprint,
                // rather than one sized off the width alone and overflowing
                // past the actual pavement.
                // The upper clamp is only a safety ceiling against corrupt/
                // garbage data (e.g. a bogus multi-kilometer LengthMeters) —
                // it must stay far above any real pavement's chevron count,
                // since chevronDepth is fixed (derived from width, not
                // length): an earlier low cap here just left the rest of a
                // long extension blank instead of scaling anything, which is
                // exactly the bug the user reported on OIBK's blast pads.
                var armHalfWidth = Math.Min(featureHalfWidth * 0.85, f.LengthMeters);
                var chevronDepth = armHalfWidth;
                var chevronCount = Math.Clamp((int)(f.LengthMeters / chevronDepth), 1, 500);
                var chevrons = new LocalPoint[chevronCount][];
                for (var i = 0; i < chevronCount; i++)
                {
                    var vertexDist = i * chevronDepth;
                    var armDist = (i + 1) * chevronDepth;
                    var vertex = new LocalPoint(threshold.X + outwardDir.X * vertexDist, threshold.Z + outwardDir.Z * vertexDist);
                    var armBase = new LocalPoint(threshold.X + outwardDir.X * armDist, threshold.Z + outwardDir.Z * armDist);
                    chevrons[i] =
                    [
                        new LocalPoint(armBase.X + right.X * armHalfWidth, armBase.Z + right.Z * armHalfWidth),
                        vertex,
                        new LocalPoint(armBase.X - right.X * armHalfWidth, armBase.Z - right.Z * armHalfWidth),
                    ];
                }

                return new ExtensionWorking(extCorners, demarcationBar, chevrons);
            }

            runways.Add(new RunwayWorkingData(
                runway, threshold1, threshold2, corners, primaryLabelPosition, secondaryLabelPosition,
                PrimaryThresholdMarking: ThresholdMarking(runway.PrimaryThreshold, threshold1, forward),
                PrimaryBlastPad: Extension(runway.PrimaryBlastPad, threshold1, (-forward.X, -forward.Z)),
                PrimaryOverrun: Extension(runway.PrimaryOverrun, threshold1, (-forward.X, -forward.Z)),
                SecondaryThresholdMarking: ThresholdMarking(runway.SecondaryThreshold, threshold2, (-forward.X, -forward.Z)),
                SecondaryBlastPad: Extension(runway.SecondaryBlastPad, threshold2, forward),
                SecondaryOverrun: Extension(runway.SecondaryOverrun, threshold2, forward)));
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

        RunwayThresholdMarkingShape? ToScreenMarking(ThresholdMarkingWorking? m) => m is null ? null : new RunwayThresholdMarkingShape(
            m.ZoneCorners.Select(ToScreen).ToList(),
            m.ThresholdBar.Select(ToScreen).ToList(),
            ToScreen(m.ArrowShaftStart),
            ToScreen(m.ArrowShaftEnd),
            m.ArrowHead.Select(ToScreen).ToList());

        RunwayPavementExtensionShape? ToScreenExtension(ExtensionWorking? e) => e is null ? null : new RunwayPavementExtensionShape(
            e.Corners.Select(ToScreen).ToList(),
            e.DemarcationBar.Select(ToScreen).ToList(),
            e.Chevrons.Select(c => (IReadOnlyList<Point2D>)c.Select(ToScreen).ToList()).ToList());

        return new AirportDiagram
        {
            CanvasWidth = (maxX - minX) + 2 * CanvasMarginMeters,
            CanvasHeight = (maxZ - minZ) + 2 * CanvasMarginMeters,
            Runways = runways.Select(r => new RunwayShape(
                r.Runway.PrimaryDesignation,
                r.Runway.SecondaryDesignation,
                ToScreen(r.Threshold1),
                ToScreen(r.Threshold2),
                r.Corners.Select(ToScreen).ToList(),
                ToScreen(r.PrimaryLabelPosition),
                ToScreen(r.SecondaryLabelPosition),
                new RunwayEndFeatures(
                    ToScreenMarking(r.PrimaryThresholdMarking),
                    ToScreenExtension(r.PrimaryBlastPad),
                    ToScreenExtension(r.PrimaryOverrun)),
                new RunwayEndFeatures(
                    ToScreenMarking(r.SecondaryThresholdMarking),
                    ToScreenExtension(r.SecondaryBlastPad),
                    ToScreenExtension(r.SecondaryOverrun)))).ToList(),
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
        List<RunwayWorkingData> runways,
        List<(TaxiPathSegment Segment, LocalPoint Start, LocalPoint End, LocalPoint[] WidthCorners, LocalPoint MidPoint)> taxiways,
        List<(TaxiParkingSpot Spot, LocalPoint Center, LocalPoint HeadingTip)> parkingSpots)
    {
        var points = new List<LocalPoint>();

        static void AddMarking(List<LocalPoint> points, ThresholdMarkingWorking? m)
        {
            if (m is null) return;
            points.AddRange(m.ZoneCorners);
            points.AddRange(m.ThresholdBar);
            points.Add(m.ArrowShaftStart);
            points.Add(m.ArrowShaftEnd);
            points.AddRange(m.ArrowHead);
        }

        static void AddExtension(List<LocalPoint> points, ExtensionWorking? e)
        {
            if (e is null) return;
            points.AddRange(e.Corners);
            points.AddRange(e.DemarcationBar);
            foreach (var chevron in e.Chevrons)
                points.AddRange(chevron);
        }

        foreach (var r in runways)
        {
            points.AddRange(r.Corners);
            AddMarking(points, r.PrimaryThresholdMarking);
            AddExtension(points, r.PrimaryBlastPad);
            AddExtension(points, r.PrimaryOverrun);
            AddMarking(points, r.SecondaryThresholdMarking);
            AddExtension(points, r.SecondaryBlastPad);
            AddExtension(points, r.SecondaryOverrun);
        }
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
