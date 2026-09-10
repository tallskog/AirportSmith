namespace AirportSmith.Services;

// Thin abstraction over a WPF file-picker dialog so MainViewModel can stay
// unit-testable (no System.Windows dependency) via a fake.
public interface IFileDialogService
{
    // Returns the chosen path, or null if the user cancelled.
    string? ShowOpenJsonFileDialog(string initialDirectory);
}
