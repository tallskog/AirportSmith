using System.IO;
using Microsoft.Win32;

namespace AirportSmith.Services;

public class FileDialogService : IFileDialogService
{
    public string? ShowOpenJsonFileDialog(string initialDirectory)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "AirportSmith debug data (*.json)|*.json|All files (*.*)|*.*",
            InitialDirectory = Directory.Exists(initialDirectory) ? initialDirectory : string.Empty,
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? ShowSaveXmlFileDialog(string suggestedFileName, string initialDirectory)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "Airport XML (*.xml)|*.xml|All files (*.*)|*.*",
            FileName = suggestedFileName,
            InitialDirectory = Directory.Exists(initialDirectory) ? initialDirectory : string.Empty,
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
