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
        set { if (_runway.PrimaryLeftVasiType == value) return; _runway.PrimaryLeftVasiType = value; OnPropertyChanged(); }
    }

    public double? PrimaryLeftVasiAngleDeg
    {
        get => _runway.PrimaryLeftVasiAngleDeg;
        set { if (_runway.PrimaryLeftVasiAngleDeg == value) return; _runway.PrimaryLeftVasiAngleDeg = value; OnPropertyChanged(); }
    }

    public VasiType? PrimaryRightVasiType
    {
        get => _runway.PrimaryRightVasiType;
        set { if (_runway.PrimaryRightVasiType == value) return; _runway.PrimaryRightVasiType = value; OnPropertyChanged(); }
    }

    public double? PrimaryRightVasiAngleDeg
    {
        get => _runway.PrimaryRightVasiAngleDeg;
        set { if (_runway.PrimaryRightVasiAngleDeg == value) return; _runway.PrimaryRightVasiAngleDeg = value; OnPropertyChanged(); }
    }

    public VasiType? SecondaryLeftVasiType
    {
        get => _runway.SecondaryLeftVasiType;
        set { if (_runway.SecondaryLeftVasiType == value) return; _runway.SecondaryLeftVasiType = value; OnPropertyChanged(); }
    }

    public double? SecondaryLeftVasiAngleDeg
    {
        get => _runway.SecondaryLeftVasiAngleDeg;
        set { if (_runway.SecondaryLeftVasiAngleDeg == value) return; _runway.SecondaryLeftVasiAngleDeg = value; OnPropertyChanged(); }
    }

    public VasiType? SecondaryRightVasiType
    {
        get => _runway.SecondaryRightVasiType;
        set { if (_runway.SecondaryRightVasiType == value) return; _runway.SecondaryRightVasiType = value; OnPropertyChanged(); }
    }

    public double? SecondaryRightVasiAngleDeg
    {
        get => _runway.SecondaryRightVasiAngleDeg;
        set { if (_runway.SecondaryRightVasiAngleDeg == value) return; _runway.SecondaryRightVasiAngleDeg = value; OnPropertyChanged(); }
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
