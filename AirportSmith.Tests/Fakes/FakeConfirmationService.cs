using AirportSmith.Services;

namespace AirportSmith.Tests.Fakes;

public class FakeConfirmationService : IConfirmationService
{
    public bool ConfirmResult { get; set; } = true;
    public string? LastMessage { get; private set; }
    public int CallCount { get; private set; }

    public bool Confirm(string message)
    {
        LastMessage = message;
        CallCount++;
        return ConfirmResult;
    }
}
