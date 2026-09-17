using AirportSmith.Models;

namespace AirportSmith.ViewModels;

// One distinct sim TAXI_POINT index for the Edit tab's Taxiway Points grid —
// unlike TaxiPathEditViewModel/RunwayEditViewModel, there's no single
// TaxiwayPoint model to wrap: AirportDetails has no separate taxi-point list,
// only TaxiPathSegment rows that each carry their own resolved Start/End
// coordinates and point Type/Orientation, duplicated across every segment
// that shares the same underlying sim point. Type/Orientation write through
// to every segment referencing this index (as either Start or End) at once,
// so editing a point here always keeps every segment sharing it consistent —
// unlike the raw per-segment fields, which could otherwise disagree.
public class TaxiwayPointEditViewModel : ViewModelBase
{
    private readonly IReadOnlyList<TaxiPathSegment> _startSegments;
    private readonly IReadOnlyList<TaxiPathSegment> _endSegments;

    private TaxiwayPointEditViewModel(int index, double xMeters, double zMeters,
        IReadOnlyList<TaxiPathSegment> startSegments, IReadOnlyList<TaxiPathSegment> endSegments)
    {
        Index = index;
        XMeters = xMeters;
        ZMeters = zMeters;
        _startSegments = startSegments;
        _endSegments = endSegments;
    }

    public int Index { get; }
    public double XMeters { get; }
    public double ZMeters { get; }

    public TaxiPointType? Type
    {
        get => _startSegments.Select(s => s.StartPointType)
            .Concat(_endSegments.Select(s => s.EndPointType))
            .FirstOrDefault(t => t != null);
        set
        {
            if (Type == value) return;
            foreach (var s in _startSegments) s.StartPointType = value;
            foreach (var s in _endSegments) s.EndPointType = value;
            OnPropertyChanged();
        }
    }

    // Only meaningful when Type is one of the hold-short variants, per the
    // SDK docs — same convention TaxiPathSegment.StartPointOrientation/
    // EndPointOrientation already follow.
    public TaxiPointOrientation? Orientation
    {
        get => _startSegments.Select(s => s.StartPointOrientation)
            .Concat(_endSegments.Select(s => s.EndPointOrientation))
            .FirstOrDefault(o => o != null);
        set
        {
            if (Orientation == value) return;
            foreach (var s in _startSegments) s.StartPointOrientation = value;
            foreach (var s in _endSegments) s.EndPointOrientation = value;
            OnPropertyChanged();
        }
    }

    // Same distinct-index synthesis AirportXmlExporter.BuildTaxiwayPoints uses
    // to build <TaxiwayPoint> elements, and AirportDiagramProjector.Project
    // uses for the diagram's red dots — every taxi path's Start, plus its End
    // unless the path is Type==Parking (whose End references a
    // TaxiwayParking item instead of a taxi point). Kept as its own copy
    // rather than a shared helper: this one needs to retain the owning
    // TaxiPathSegment references themselves (for Type/Orientation to write
    // back onto), where the other two only need coordinates/XML output.
    public static IReadOnlyList<TaxiwayPointEditViewModel> BuildAll(AirportDetails airport)
    {
        var coords = new Dictionary<int, (double X, double Z)>();
        var startSegments = new Dictionary<int, List<TaxiPathSegment>>();
        var endSegments = new Dictionary<int, List<TaxiPathSegment>>();

        foreach (var segment in airport.TaxiPaths)
        {
            if (segment.StartXMeters is { } sx && segment.StartZMeters is { } sz)
            {
                coords.TryAdd(segment.StartIndex, (sx, sz));
                if (!startSegments.TryGetValue(segment.StartIndex, out var list))
                    startSegments[segment.StartIndex] = list = [];
                list.Add(segment);
            }

            if (segment.Type != TaxiPathType.Parking && segment.EndXMeters is { } ex && segment.EndZMeters is { } ez)
            {
                coords.TryAdd(segment.EndIndex, (ex, ez));
                if (!endSegments.TryGetValue(segment.EndIndex, out var list))
                    endSegments[segment.EndIndex] = list = [];
                list.Add(segment);
            }
        }

        return coords.OrderBy(kvp => kvp.Key)
            .Select(kvp => new TaxiwayPointEditViewModel(
                kvp.Key, kvp.Value.X, kvp.Value.Z,
                startSegments.TryGetValue(kvp.Key, out var starts) ? starts : [],
                endSegments.TryGetValue(kvp.Key, out var ends) ? ends : []))
            .ToList();
    }
}
