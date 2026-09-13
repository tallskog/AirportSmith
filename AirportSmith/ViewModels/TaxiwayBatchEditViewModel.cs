namespace AirportSmith.ViewModels;

// Staging values for the Edit tab's "batch edit selected taxi paths" popover
// — unlike TaxiPathEditViewModel, these don't write through to any segment
// directly. MainViewModel resets this to "untouched" every time the diagram
// selection changes (ResetToUntouched), and ApplyTaxiwayBatchEditCommand only
// copies a field onto the selected paths if the user actually touched it in
// the popover.
//
// An earlier revision seeded these from the first selected path's current
// values and had Apply overwrite all three fields on every selected path
// unconditionally — which silently changed fields the user never touched
// whenever the selected paths didn't already agree on them (e.g. selecting
// two differently-named paths just to toggle one's lighting would silently
// rename the other to match the first one's name). Tracking which fields
// were actually touched avoids that.
public class TaxiwayBatchEditViewModel : ViewModelBase
{
    private Guid? _taxiNameId;
    private bool? _leftEdgeLighted;
    private bool? _rightEdgeLighted;

    public bool IsTaxiNameIdTouched { get; private set; }

    public Guid? TaxiNameId
    {
        get => _taxiNameId;
        set
        {
            IsTaxiNameIdTouched = true;
            SetField(ref _taxiNameId, value);
        }
    }

    // Null means "leave unchanged" — bound to an IsThreeState CheckBox, whose
    // indeterminate state doubles as the "untouched" visual.
    public bool? LeftEdgeLighted
    {
        get => _leftEdgeLighted;
        set => SetField(ref _leftEdgeLighted, value);
    }

    public bool? RightEdgeLighted
    {
        get => _rightEdgeLighted;
        set => SetField(ref _rightEdgeLighted, value);
    }

    public void ResetToUntouched()
    {
        // Deliberately doesn't go through the TaxiNameId setter — that marks
        // IsTaxiNameIdTouched, which resetting to "untouched" must not do.
        SetField(ref _taxiNameId, null, nameof(TaxiNameId));
        IsTaxiNameIdTouched = false;
        OnPropertyChanged(nameof(IsTaxiNameIdTouched));
        LeftEdgeLighted = null;
        RightEdgeLighted = null;
    }
}
