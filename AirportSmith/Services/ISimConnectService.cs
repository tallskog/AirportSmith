using AirportSmith.Models;

namespace AirportSmith.Services;

public enum AirportLookupStatus { Success, NotFound, NotConnected, Timeout, Error }

public record AirportLookupResult(AirportLookupStatus Status, AirportDetails? Airport, string? ErrorMessage);

public interface ISimConnectService
{
    bool IsConnected { get; }
    event EventHandler? ConnectionChanged;

    // Connects if not already connected. Safe to call speculatively (e.g. at window
    // startup) — silently stays disconnected if MSFS isn't reachable.
    void Connect(nint windowHandle);
    void Disconnect();

    // Connects if needed, requests every facility sub-type for icao, aggregates all
    // streamed rows, and completes once the sim signals it's done sending them (or
    // on timeout / not-connected / exception). Only one lookup may be in flight at
    // a time — callers must gate this behind their own CanExecute/busy check.
    Task<AirportLookupResult> GetAirportDetailsAsync(string icao, CancellationToken cancellationToken = default);
}
