using AirportSmith.Models;

namespace AirportSmith.ViewModels;

// Editable wrapper around one TaxiName for the Edit tab's Taxi Names panel.
// Same no-separate-apply pattern as TaxiPathEditViewModel/RunwayEditViewModel
// — renaming here writes straight through, and since every TaxiPathSegment
// referencing this entry looks it up live by Id (see
// AirportDiagramProjector.ResolveTaxiName), the rename is immediately visible
// everywhere that name is used — no separate propagation step needed.
public class TaxiNameEditViewModel : ViewModelBase
{
    public TaxiName Model { get; }

    public TaxiNameEditViewModel(TaxiName model)
    {
        Model = model;
    }

    public Guid Id => Model.Id;

    public string Value
    {
        get => Model.Value;
        set
        {
            if (Model.Value == value) return;
            Model.Value = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayValue));
            OnPropertyChanged(nameof(IsUnnamed));
        }
    }

    // A blank Value is a real, meaningful sim entry, not an empty/junk row —
    // the sim's own TAXI_NAME array can include one, and many taxi paths
    // (e.g. ones leading to parking) legitimately point at it via
    // TaxiNameId, often several dozen at once. A blank row in the grid gave
    // no hint of that, so a user renamed what looked like an empty leftover
    // entry and was surprised when "numerous paths" picked up the new name —
    // the shared-rename behavior working exactly as designed, just with no
    // warning this particular entry was heavily shared. DisplayValue/
    // IsUnnamed exist purely to make that visible (see MainWindow.xaml's
    // Taxi Names panel and the Name pickers); Value itself is untouched, and
    // still directly editable — this entry can be given a real name like
    // any other if that's genuinely what's wanted.
    public string DisplayValue => IsUnnamed ? "(no name)" : Value;

    public bool IsUnnamed => string.IsNullOrEmpty(Value);
}
