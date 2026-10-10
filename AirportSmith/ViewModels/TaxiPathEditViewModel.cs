using AirportSmith.Models;
using AirportSmith.Services;

namespace AirportSmith.ViewModels;

// Editable wrapper around one TaxiPathSegment for the Edit tab's DataGrid.
// Properties read/write straight through to the wrapped segment — there's no
// separate "apply" step, edits land immediately on the in-memory
// AirportDetails so Save Project picks them up as-is.
//
// TaxiNameId picks from MainViewModel.TaxiNames (the shared, user-managed
// name list) rather than free text — since every segment sharing a
// TaxiNameId resolves the same live AirportDetails.TaxiNames entry, renaming
// or reassigning it here (or in the Taxi Names panel) is visible everywhere
// that name is used, unlike the free-text-per-segment approach this replaced.
//
// Name and Type are linked per TaxiPathNameRules: naming a Taxi/Path/Unknown
// path makes it Taxi, clearing its name makes it Path, and a Runway/Parking
// path's name can't be edited. isNamed resolves a TaxiNameId to "has a real
// name" (MainViewModel passes one that treats the blank "(no name)" entry
// as unnamed); without one, any non-null id counts as named.
public class TaxiPathEditViewModel : ViewModelBase
{
    private readonly TaxiPathSegment _segment;
    private readonly Func<Guid?, bool> _isNamed;

    public TaxiPathEditViewModel(TaxiPathSegment segment, Func<Guid?, bool>? isNamed = null)
    {
        _segment = segment;
        _isNamed = isNamed ?? (id => id is not null);
    }

    // Read-only — the sim's own TAXI_POINT indices this path connects
    // (matches the Edit tab's Taxiway Points grid rows/diagram dots by
    // Index, and the exported <TaxiwayPath start="..."/end="...">), not
    // independently editable here. Shown so a path's row can be
    // cross-referenced against the specific points it connects without
    // switching tabs or guessing from the diagram alone.
    public int StartIndex => _segment.StartIndex;
    public int EndIndex => _segment.EndIndex;

    public Guid? TaxiNameId
    {
        get => _segment.TaxiNameId;
        set
        {
            // Ignored rather than thrown — the grid shows "---" instead of a
            // picker for these, but batch edit and accepted name suggestions
            // can still reach this with a mixed selection.
            if (_segment.TaxiNameId == value || !IsNameEditable) return;
            SetTaxiNameIdCore(value);
            if (TaxiPathNameRules.FollowsName(Type))
                Type = TaxiPathNameRules.TypeForName(_isNamed(value));
        }
    }

    private void SetTaxiNameIdCore(Guid? value)
    {
        _segment.TaxiNameId = value;
        OnPropertyChanged(nameof(TaxiNameId));
    }

    // False for Runway/Parking paths — see TaxiPathNameRules.
    public bool IsNameEditable => !TaxiPathNameRules.IsNameLocked(Type);

    // Called when a TaxiName is deleted from the Taxi Names panel, so no row
    // is left silently referencing a name that no longer exists. Bypasses the
    // name lock and the name -> type rule: a Parking path must not keep a
    // dangling reference either, and deleting a name entry isn't the user
    // retyping this path.
    public void ClearTaxiNameIfReferencing(Guid deletedId)
    {
        if (_segment.TaxiNameId == deletedId)
            SetTaxiNameIdCore(null);
    }

    public TaxiPathType Type
    {
        get => _segment.Type;
        set
        {
            if (_segment.Type == value) return;
            _segment.Type = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsNameEditable));
            // A Runway path can't carry a name (same as on read). A Parking
            // path keeps its name, just hidden and locked.
            if (value == TaxiPathType.Runway && _segment.TaxiNameId is not null)
                SetTaxiNameIdCore(null);
        }
    }

    public int RunwayNumber
    {
        get => _segment.RunwayNumber;
        set
        {
            if (_segment.RunwayNumber == value) return;
            _segment.RunwayNumber = value;
            OnPropertyChanged();
        }
    }

    public TaxiPathRunwayDesignator RunwayDesignator
    {
        get => _segment.RunwayDesignator;
        set
        {
            if (_segment.RunwayDesignator == value) return;
            _segment.RunwayDesignator = value;
            OnPropertyChanged();
        }
    }

    public TaxiEdgeType LeftEdge
    {
        get => _segment.LeftEdge;
        set
        {
            if (_segment.LeftEdge == value) return;
            _segment.LeftEdge = value;
            OnPropertyChanged();
        }
    }

    public TaxiEdgeType RightEdge
    {
        get => _segment.RightEdge;
        set
        {
            if (_segment.RightEdge == value) return;
            _segment.RightEdge = value;
            OnPropertyChanged();
        }
    }

    public bool LeftEdgeLighted
    {
        get => _segment.LeftEdgeLighted;
        set
        {
            if (_segment.LeftEdgeLighted == value) return;
            _segment.LeftEdgeLighted = value;
            OnPropertyChanged();
        }
    }

    public bool RightEdgeLighted
    {
        get => _segment.RightEdgeLighted;
        set
        {
            if (_segment.RightEdgeLighted == value) return;
            _segment.RightEdgeLighted = value;
            OnPropertyChanged();
        }
    }

    public bool CenterLine
    {
        get => _segment.CenterLine;
        set
        {
            if (_segment.CenterLine == value) return;
            _segment.CenterLine = value;
            OnPropertyChanged();
        }
    }

    public bool CenterLineLighted
    {
        get => _segment.CenterLineLighted;
        set
        {
            if (_segment.CenterLineLighted == value) return;
            _segment.CenterLineLighted = value;
            OnPropertyChanged();
        }
    }

    // A workspace/editor convenience for decluttering the diagram — NOT
    // written to TaxiPathSegment and never persisted by Save Project, since
    // it isn't an actual property of the airport being edited. MainViewModel
    // listens for this to toggle the matching TaxiwaySegmentShape.IsVisible.
    private bool _isHiddenFromDiagram;
    public bool IsHiddenFromDiagram
    {
        get => _isHiddenFromDiagram;
        set
        {
            if (_isHiddenFromDiagram == value) return;
            _isHiddenFromDiagram = value;
            OnPropertyChanged();
        }
    }
}
