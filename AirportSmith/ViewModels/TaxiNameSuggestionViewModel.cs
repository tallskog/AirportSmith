namespace AirportSmith.ViewModels;

// One row of the Edit tab's "Suggest Names from OSM" work list: the taxi
// path at TaxiPathEdits[PathIndex] and the name OSM suggests for it.
// IsAccepted starts ticked only for an unnamed path, so applying never
// overwrites an existing name unless the user opts in for that row. Once
// applied, the row stays in the list marked as done (IsApplied) until the
// user removes it.
public class TaxiNameSuggestionViewModel : ViewModelBase
{
    public TaxiNameSuggestionViewModel(int pathIndex, int startIndex, int endIndex, string currentName, string suggestedName)
    {
        PathIndex = pathIndex;
        StartIndex = startIndex;
        EndIndex = endIndex;
        _currentName = currentName;
        SuggestedName = suggestedName;
        _isAccepted = string.IsNullOrEmpty(currentName);
    }

    public int PathIndex { get; }
    public int StartIndex { get; }
    public int EndIndex { get; }
    public string SuggestedName { get; }

    private string _currentName;
    public string CurrentName
    {
        get => _currentName;
        private set => SetField(ref _currentName, value);
    }

    private bool _isAccepted;
    public bool IsAccepted
    {
        get => _isAccepted;
        set => SetField(ref _isAccepted, value && !IsApplied);
    }

    private bool _isApplied;
    public bool IsApplied
    {
        get => _isApplied;
        private set
        {
            if (!SetField(ref _isApplied, value)) return;
            OnPropertyChanged(nameof(CanAccept));
            OnPropertyChanged(nameof(Status));
        }
    }

    public bool CanAccept => !IsApplied;
    public string Status => IsApplied ? "Applied" : string.Empty;

    // Called whenever the path's name may have changed — by Apply or by any
    // other edit (MainViewModel.RefreshSuggestionCurrentNames). The row is
    // applied exactly when the path's name equals the suggestion
    // (case-insensitive); changing it away again makes the row acceptable
    // again (left unticked).
    public void UpdateCurrentName(string currentName)
    {
        CurrentName = currentName;
        var applied = string.Equals(currentName, SuggestedName, StringComparison.OrdinalIgnoreCase);
        if (applied) IsAccepted = false;
        IsApplied = applied;
    }
}
