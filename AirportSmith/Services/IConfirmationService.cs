namespace AirportSmith.Services;

// Thin abstraction over a WPF Yes/No confirmation dialog so MainViewModel can
// stay unit-testable (no System.Windows dependency) via a fake — same
// rationale/pattern as IFileDialogService.
public interface IConfirmationService
{
    // True if the user confirmed (Yes), false if they declined (No).
    bool Confirm(string message);
}
