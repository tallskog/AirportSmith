using AirportSmith.Models;

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
public class TaxiPathEditViewModel : ViewModelBase
{
    private readonly TaxiPathSegment _segment;

    public TaxiPathEditViewModel(TaxiPathSegment segment)
    {
        _segment = segment;
    }

    public Guid? TaxiNameId
    {
        get => _segment.TaxiNameId;
        set
        {
            if (_segment.TaxiNameId == value) return;
            _segment.TaxiNameId = value;
            OnPropertyChanged();
        }
    }

    // Called when a TaxiName is deleted from the Taxi Names panel, so no row
    // is left silently referencing a name that no longer exists.
    public void ClearTaxiNameIfReferencing(Guid deletedId)
    {
        if (_segment.TaxiNameId == deletedId)
            TaxiNameId = null;
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
