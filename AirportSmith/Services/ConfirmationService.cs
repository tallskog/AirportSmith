using System.Windows;

namespace AirportSmith.Services;

public class ConfirmationService : IConfirmationService
{
    public bool Confirm(string message)
        => MessageBox.Show(message, "AirportSmith", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
}
