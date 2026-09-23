using AirportSmith.Models;

namespace AirportSmith.ViewModels;

// Editable wrapper around one Runway for the Edit tab's DataGrid. Properties
// read/write straight through to the wrapped runway, same no-separate-apply
// pattern as TaxiPathEditViewModel. PrimarySystemType/SecondarySystemType
// flatten Runway's ApproachLightSystem? record into a plain nullable enum for
// binding — setting it to null clears the record, setting it to a value
// (re)creates the record.
public class RunwayEditViewModel : ViewModelBase
{
    // Suggested starting position for a VASI/PAPI enabled from "(none)" via a
    // Type picker below, rather than leaving BiasX/BiasZ/Spacing null (which
    // AirportXmlExporter.AddVasi would otherwise default to 0/0/0 with a
    // warning at export time). 300m inward from that end's threshold along
    // the runway centerline is a reasonable real-world VASI/PAPI distance;
    // 0 lateral offset and 15m spacing are just placeholders the user is
    // expected to refine via the diagram's click-to-place
    // (MainViewModel's ArmVasiPlacementCommand family) or by typing over
    // them directly.
    private const double DefaultVasiBiasZMeters = 300;
    private const double DefaultVasiSpacingMeters = 15;

    private readonly Runway _runway;

    public RunwayEditViewModel(Runway runway)
    {
        _runway = runway;
    }

    public string PrimaryDesignation => _runway.PrimaryDesignation;
    public string SecondaryDesignation => _runway.SecondaryDesignation;

    public RunwayLightIntensity EdgeLightIntensity
    {
        get => _runway.EdgeLightIntensity;
        set
        {
            if (_runway.EdgeLightIntensity == value) return;
            _runway.EdgeLightIntensity = value;
            OnPropertyChanged();
        }
    }

    public VasiType? PrimaryLeftVasiType
    {
        get => _runway.PrimaryLeftVasiType;
        set
        {
            if (_runway.PrimaryLeftVasiType == value) return;
            var isNewInstall = _runway.PrimaryLeftVasiType is null && value is not null;
            _runway.PrimaryLeftVasiType = value;
            OnPropertyChanged();
            if (isNewInstall) ApplyDefaultPrimaryLeftVasiPosition();
        }
    }

    public double? PrimaryLeftVasiAngleDeg
    {
        get => _runway.PrimaryLeftVasiAngleDeg;
        set { if (_runway.PrimaryLeftVasiAngleDeg == value) return; _runway.PrimaryLeftVasiAngleDeg = value; OnPropertyChanged(); }
    }

    public double? PrimaryLeftVasiBiasXMeters
    {
        get => _runway.PrimaryLeftVasiBiasXMeters;
        set { if (_runway.PrimaryLeftVasiBiasXMeters == value) return; _runway.PrimaryLeftVasiBiasXMeters = value; OnPropertyChanged(); }
    }

    public double? PrimaryLeftVasiBiasZMeters
    {
        get => _runway.PrimaryLeftVasiBiasZMeters;
        set { if (_runway.PrimaryLeftVasiBiasZMeters == value) return; _runway.PrimaryLeftVasiBiasZMeters = value; OnPropertyChanged(); }
    }

    public double? PrimaryLeftVasiSpacingMeters
    {
        get => _runway.PrimaryLeftVasiSpacingMeters;
        set { if (_runway.PrimaryLeftVasiSpacingMeters == value) return; _runway.PrimaryLeftVasiSpacingMeters = value; OnPropertyChanged(); }
    }

    // Only applied when position data is completely unset (not e.g.
    // re-picking a different VasiType on an already-positioned slot), so it
    // never clobbers real extracted sim data — see the class-level doc
    // comment on the default constants above.
    private void ApplyDefaultPrimaryLeftVasiPosition()
    {
        if (_runway.PrimaryLeftVasiBiasXMeters != null || _runway.PrimaryLeftVasiBiasZMeters != null || _runway.PrimaryLeftVasiSpacingMeters != null) return;
        _runway.PrimaryLeftVasiBiasXMeters = 0;
        _runway.PrimaryLeftVasiBiasZMeters = DefaultVasiBiasZMeters;
        _runway.PrimaryLeftVasiSpacingMeters = DefaultVasiSpacingMeters;
        OnPropertyChanged(nameof(PrimaryLeftVasiBiasXMeters));
        OnPropertyChanged(nameof(PrimaryLeftVasiBiasZMeters));
        OnPropertyChanged(nameof(PrimaryLeftVasiSpacingMeters));
    }

    public VasiType? PrimaryRightVasiType
    {
        get => _runway.PrimaryRightVasiType;
        set
        {
            if (_runway.PrimaryRightVasiType == value) return;
            var isNewInstall = _runway.PrimaryRightVasiType is null && value is not null;
            _runway.PrimaryRightVasiType = value;
            OnPropertyChanged();
            if (isNewInstall) ApplyDefaultPrimaryRightVasiPosition();
        }
    }

    public double? PrimaryRightVasiAngleDeg
    {
        get => _runway.PrimaryRightVasiAngleDeg;
        set { if (_runway.PrimaryRightVasiAngleDeg == value) return; _runway.PrimaryRightVasiAngleDeg = value; OnPropertyChanged(); }
    }

    public double? PrimaryRightVasiBiasXMeters
    {
        get => _runway.PrimaryRightVasiBiasXMeters;
        set { if (_runway.PrimaryRightVasiBiasXMeters == value) return; _runway.PrimaryRightVasiBiasXMeters = value; OnPropertyChanged(); }
    }

    public double? PrimaryRightVasiBiasZMeters
    {
        get => _runway.PrimaryRightVasiBiasZMeters;
        set { if (_runway.PrimaryRightVasiBiasZMeters == value) return; _runway.PrimaryRightVasiBiasZMeters = value; OnPropertyChanged(); }
    }

    public double? PrimaryRightVasiSpacingMeters
    {
        get => _runway.PrimaryRightVasiSpacingMeters;
        set { if (_runway.PrimaryRightVasiSpacingMeters == value) return; _runway.PrimaryRightVasiSpacingMeters = value; OnPropertyChanged(); }
    }

    private void ApplyDefaultPrimaryRightVasiPosition()
    {
        if (_runway.PrimaryRightVasiBiasXMeters != null || _runway.PrimaryRightVasiBiasZMeters != null || _runway.PrimaryRightVasiSpacingMeters != null) return;
        _runway.PrimaryRightVasiBiasXMeters = 0;
        _runway.PrimaryRightVasiBiasZMeters = DefaultVasiBiasZMeters;
        _runway.PrimaryRightVasiSpacingMeters = DefaultVasiSpacingMeters;
        OnPropertyChanged(nameof(PrimaryRightVasiBiasXMeters));
        OnPropertyChanged(nameof(PrimaryRightVasiBiasZMeters));
        OnPropertyChanged(nameof(PrimaryRightVasiSpacingMeters));
    }

    public VasiType? SecondaryLeftVasiType
    {
        get => _runway.SecondaryLeftVasiType;
        set
        {
            if (_runway.SecondaryLeftVasiType == value) return;
            var isNewInstall = _runway.SecondaryLeftVasiType is null && value is not null;
            _runway.SecondaryLeftVasiType = value;
            OnPropertyChanged();
            if (isNewInstall) ApplyDefaultSecondaryLeftVasiPosition();
        }
    }

    public double? SecondaryLeftVasiAngleDeg
    {
        get => _runway.SecondaryLeftVasiAngleDeg;
        set { if (_runway.SecondaryLeftVasiAngleDeg == value) return; _runway.SecondaryLeftVasiAngleDeg = value; OnPropertyChanged(); }
    }

    public double? SecondaryLeftVasiBiasXMeters
    {
        get => _runway.SecondaryLeftVasiBiasXMeters;
        set { if (_runway.SecondaryLeftVasiBiasXMeters == value) return; _runway.SecondaryLeftVasiBiasXMeters = value; OnPropertyChanged(); }
    }

    public double? SecondaryLeftVasiBiasZMeters
    {
        get => _runway.SecondaryLeftVasiBiasZMeters;
        set { if (_runway.SecondaryLeftVasiBiasZMeters == value) return; _runway.SecondaryLeftVasiBiasZMeters = value; OnPropertyChanged(); }
    }

    public double? SecondaryLeftVasiSpacingMeters
    {
        get => _runway.SecondaryLeftVasiSpacingMeters;
        set { if (_runway.SecondaryLeftVasiSpacingMeters == value) return; _runway.SecondaryLeftVasiSpacingMeters = value; OnPropertyChanged(); }
    }

    private void ApplyDefaultSecondaryLeftVasiPosition()
    {
        if (_runway.SecondaryLeftVasiBiasXMeters != null || _runway.SecondaryLeftVasiBiasZMeters != null || _runway.SecondaryLeftVasiSpacingMeters != null) return;
        _runway.SecondaryLeftVasiBiasXMeters = 0;
        _runway.SecondaryLeftVasiBiasZMeters = DefaultVasiBiasZMeters;
        _runway.SecondaryLeftVasiSpacingMeters = DefaultVasiSpacingMeters;
        OnPropertyChanged(nameof(SecondaryLeftVasiBiasXMeters));
        OnPropertyChanged(nameof(SecondaryLeftVasiBiasZMeters));
        OnPropertyChanged(nameof(SecondaryLeftVasiSpacingMeters));
    }

    public VasiType? SecondaryRightVasiType
    {
        get => _runway.SecondaryRightVasiType;
        set
        {
            if (_runway.SecondaryRightVasiType == value) return;
            var isNewInstall = _runway.SecondaryRightVasiType is null && value is not null;
            _runway.SecondaryRightVasiType = value;
            OnPropertyChanged();
            if (isNewInstall) ApplyDefaultSecondaryRightVasiPosition();
        }
    }

    public double? SecondaryRightVasiAngleDeg
    {
        get => _runway.SecondaryRightVasiAngleDeg;
        set { if (_runway.SecondaryRightVasiAngleDeg == value) return; _runway.SecondaryRightVasiAngleDeg = value; OnPropertyChanged(); }
    }

    public double? SecondaryRightVasiBiasXMeters
    {
        get => _runway.SecondaryRightVasiBiasXMeters;
        set { if (_runway.SecondaryRightVasiBiasXMeters == value) return; _runway.SecondaryRightVasiBiasXMeters = value; OnPropertyChanged(); }
    }

    public double? SecondaryRightVasiBiasZMeters
    {
        get => _runway.SecondaryRightVasiBiasZMeters;
        set { if (_runway.SecondaryRightVasiBiasZMeters == value) return; _runway.SecondaryRightVasiBiasZMeters = value; OnPropertyChanged(); }
    }

    public double? SecondaryRightVasiSpacingMeters
    {
        get => _runway.SecondaryRightVasiSpacingMeters;
        set { if (_runway.SecondaryRightVasiSpacingMeters == value) return; _runway.SecondaryRightVasiSpacingMeters = value; OnPropertyChanged(); }
    }

    private void ApplyDefaultSecondaryRightVasiPosition()
    {
        if (_runway.SecondaryRightVasiBiasXMeters != null || _runway.SecondaryRightVasiBiasZMeters != null || _runway.SecondaryRightVasiSpacingMeters != null) return;
        _runway.SecondaryRightVasiBiasXMeters = 0;
        _runway.SecondaryRightVasiBiasZMeters = DefaultVasiBiasZMeters;
        _runway.SecondaryRightVasiSpacingMeters = DefaultVasiSpacingMeters;
        OnPropertyChanged(nameof(SecondaryRightVasiBiasXMeters));
        OnPropertyChanged(nameof(SecondaryRightVasiBiasZMeters));
        OnPropertyChanged(nameof(SecondaryRightVasiSpacingMeters));
    }

    public ApproachLightSystemType? PrimarySystemType
    {
        get => _runway.PrimaryApproachLights?.SystemType;
        set
        {
            if (_runway.PrimaryApproachLights?.SystemType == value) return;
            _runway.PrimaryApproachLights = value is null ? null : new ApproachLightSystem(value.Value);
            OnPropertyChanged();
        }
    }

    public ApproachLightSystemType? SecondarySystemType
    {
        get => _runway.SecondaryApproachLights?.SystemType;
        set
        {
            if (_runway.SecondaryApproachLights?.SystemType == value) return;
            _runway.SecondaryApproachLights = value is null ? null : new ApproachLightSystem(value.Value);
            OnPropertyChanged();
        }
    }

    // Independent of PrimarySystemType/SecondarySystemType above — see
    // Runway.PrimaryApproachLightsStrobeCount's own doc comment for why
    // these live as flat Runway properties rather than inside
    // ApproachLightSystem (a real runway can have REIL/touchdown lights
    // with no approach light system chosen at all, so tying them to
    // PrimarySystemType/SecondarySystemType being non-null would make that
    // case unrepresentable/uneditable).
    public int PrimaryApproachLightsStrobeCount
    {
        get => _runway.PrimaryApproachLightsStrobeCount;
        set { if (_runway.PrimaryApproachLightsStrobeCount == value) return; _runway.PrimaryApproachLightsStrobeCount = value; OnPropertyChanged(); }
    }

    public bool PrimaryApproachLightsHasEndLights
    {
        get => _runway.PrimaryApproachLightsHasEndLights;
        set { if (_runway.PrimaryApproachLightsHasEndLights == value) return; _runway.PrimaryApproachLightsHasEndLights = value; OnPropertyChanged(); }
    }

    public bool PrimaryApproachLightsHasReilLights
    {
        get => _runway.PrimaryApproachLightsHasReilLights;
        set { if (_runway.PrimaryApproachLightsHasReilLights == value) return; _runway.PrimaryApproachLightsHasReilLights = value; OnPropertyChanged(); }
    }

    public bool PrimaryApproachLightsHasTouchdownLights
    {
        get => _runway.PrimaryApproachLightsHasTouchdownLights;
        set { if (_runway.PrimaryApproachLightsHasTouchdownLights == value) return; _runway.PrimaryApproachLightsHasTouchdownLights = value; OnPropertyChanged(); }
    }

    public int SecondaryApproachLightsStrobeCount
    {
        get => _runway.SecondaryApproachLightsStrobeCount;
        set { if (_runway.SecondaryApproachLightsStrobeCount == value) return; _runway.SecondaryApproachLightsStrobeCount = value; OnPropertyChanged(); }
    }

    public bool SecondaryApproachLightsHasEndLights
    {
        get => _runway.SecondaryApproachLightsHasEndLights;
        set { if (_runway.SecondaryApproachLightsHasEndLights == value) return; _runway.SecondaryApproachLightsHasEndLights = value; OnPropertyChanged(); }
    }

    public bool SecondaryApproachLightsHasReilLights
    {
        get => _runway.SecondaryApproachLightsHasReilLights;
        set { if (_runway.SecondaryApproachLightsHasReilLights == value) return; _runway.SecondaryApproachLightsHasReilLights = value; OnPropertyChanged(); }
    }

    public bool SecondaryApproachLightsHasTouchdownLights
    {
        get => _runway.SecondaryApproachLightsHasTouchdownLights;
        set { if (_runway.SecondaryApproachLightsHasTouchdownLights == value) return; _runway.SecondaryApproachLightsHasTouchdownLights = value; OnPropertyChanged(); }
    }

    // A workspace/editor convenience for decluttering the diagram — NOT
    // written to Runway and never persisted by Save Project, since it isn't
    // an actual property of the airport being edited. MainViewModel listens
    // for this to toggle the matching RunwayShape.IsVisible. Same rationale
    // as TaxiPathEditViewModel.IsHiddenFromDiagram.
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
