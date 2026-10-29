using AirportSmith.Models;
using AirportSmith.Services;

namespace AirportSmith.ViewModels;

// Editable wrapper around one TaxiwaySign for the Edit tab's Taxiway Signs
// grid. Properties read/write straight through to the wrapped sign, same
// no-separate-apply pattern as ParkingSpotEditViewModel. Any change made
// through here clears IsAutoPlaced: the user has touched the sign, so a later
// auto-placement run keeps it instead of replacing it (see
// TaxiwaySignPlanner.Merge).
public class TaxiwaySignEditViewModel : ViewModelBase
{
    public TaxiwaySignEditViewModel(TaxiwaySign sign)
    {
        Sign = sign;
    }

    public TaxiwaySign Sign { get; }

    public bool IsAutoPlaced => Sign.IsAutoPlaced;

    public string Label
    {
        get => Sign.Label;
        set
        {
            value ??= string.Empty;
            if (Sign.Label == value) return;
            Sign.Label = value;
            Touched();
            OnPropertyChanged(nameof(LabelError));
            OnPropertyChanged(nameof(HasLabelError));
            OnPropertyChanged(nameof(Segments));
        }
    }

    public string? LabelError => TaxiwaySignLabel.Validate(Sign.Label);
    public bool HasLabelError => LabelError != null;

    // The grid's colored preview (same segments the diagram draws).
    public IReadOnlyList<TaxiwaySignSegment> Segments => TaxiwaySignLabel.Parse(Sign.Label).Segments;

    public double HeadingDeg
    {
        get => Sign.HeadingDeg;
        set { if (Sign.HeadingDeg == value) return; Sign.HeadingDeg = value; Touched(); }
    }

    public TaxiwaySignSize Size
    {
        get => Sign.Size;
        set { if (Sign.Size == value) return; Sign.Size = value; Touched(); }
    }

    public TaxiwaySignJustification Justification
    {
        get => Sign.Justification;
        set { if (Sign.Justification == value) return; Sign.Justification = value; Touched(); }
    }

    public double XMeters
    {
        get => Sign.XMeters;
        set { if (Sign.XMeters == value) return; Sign.XMeters = value; Touched(); }
    }

    public double ZMeters
    {
        get => Sign.ZMeters;
        set { if (Sign.ZMeters == value) return; Sign.ZMeters = value; Touched(); }
    }

    // Moves the sign in one step (diagram click-to-place), raising one
    // change per axis like two grid edits would.
    public void MoveTo(double xMeters, double zMeters)
    {
        XMeters = xMeters;
        ZMeters = zMeters;
    }

    private void Touched([System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        OnPropertyChanged(name);
        if (!Sign.IsAutoPlaced) return;
        Sign.IsAutoPlaced = false;
        OnPropertyChanged(nameof(IsAutoPlaced));
    }
}
