using AirportSmith.Models;
using Microsoft.FlightSimulator.SimConnect;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace AirportSmith.Services;

// Requires Libs\Microsoft.FlightSimulator.SimConnect.dll (managed wrapper, x64)
// and Libs\SimConnect.dll (native, x64) — both copied from DestinationPlanner's
// SDK-derived copies. The native DLL must be next to the executable (see the
// CopySimConnectNative build targets in AirportSmith.csproj).
//
// Unlike DestinationPlanner's SimConnectService (a continuous flight-tracking
// stream), this service handles a single on-demand request/response per ICAO —
// one lookup in flight at a time, resolved via a TaskCompletionSource rather than
// an ongoing event stream.
//
// Facility Data field names/types below are taken from the official SDK
// reference (docs.flightsimulator.com, SimConnect_AddToFacilityDefinition —
// via its 2020-era static mirror, since the 2024 doc site is a JS app that
// doesn't expose field tables to a plain fetch), not guessed. The docs
// explicitly confirm multiple child sub-object types (RUNWAY, FREQUENCY,
// TAXI_PARKING, TAXI_PATH) can be nested under one AIRPORT facility
// definition ("Runway is a child of Airport, so we can request them in
// Airport FacilityDataDefinition") — an earlier revision of this file
// suspected that combination itself was the source of corrupted data and
// split every sub-type into its own definition; the real cause (confirmed
// against the docs) was several fields being declared as FLOAT64 (double)
// in the marshaling structs below when the SDK actually writes them as
// FLOAT32 (float) — HEADING, LENGTH, WIDTH, RADIUS, and FREQUENCY's Hz value
// among them. One combined definition is used again since it's simpler and
// the docs confirm it's supported.
public class SimConnectService : ISimConnectService
{
    private const int WM_USER_SIMCONNECT = 0x0402;
    private static readonly TimeSpan LookupTimeout = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan JetwayTimeout = TimeSpan.FromSeconds(2);

    private SimConnect? _sc;
    private HwndSource? _hwndSource;
    private nint _lastWindowHandle;

    public bool IsConnected { get; private set; }
    public event EventHandler? ConnectionChanged;

    private enum FacilityDefs { AirportCore }
    private enum FacilityReqs { AirportCore }

    // AIRPORT: LATITUDE/LONGITUDE/ALTITUDE are FLOAT64; MAGVAR is FLOAT32; NAME
    // is a fixed CHAR[32] (not STRING64 — that's NAME64, a separate field we
    // don't request).
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct FacilityAirportData
    {
        public double Latitude, Longitude, Altitude;
        public float MagVar;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string Name;
    }

    // RUNWAY: LATITUDE/LONGITUDE/ALTITUDE are FLOAT64; HEADING/LENGTH/WIDTH are
    // FLOAT32 (this was the bug in the previous revision — they were declared
    // as double, which corrupted every field after them); SURFACE and the
    // PRIMARY_*/SECONDARY_* designators are INT32.
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct FacilityRunwayData
    {
        public double Latitude, Longitude, Altitude;
        public float Heading, Length, Width;
        public int Surface;
        public int PrimaryNumber, PrimaryDesignator, SecondaryNumber, SecondaryDesignator;
    }

    // VASI/PAPI light data for one runway end-side. RUNWAY nests up to four of
    // these by name (PRIMARY_LEFT_VASI, PRIMARY_RIGHT_VASI, SECONDARY_LEFT_VASI,
    // SECONDARY_RIGHT_VASI) rather than requesting them as its own scalar
    // fields, so each arrives as its own SIMCONNECT_FACILITY_DATA_TYPE.VASI row
    // — see the VASI case in OnFacilityData for how a row is matched back to
    // which of the four named slots it came from. Confirmed against a live sim
    // (a runway with no PAPI at all): all four slots are always sent in request
    // order, with TYPE 0 meaning "none installed" here. Only TYPE and ANGLE are
    // requested — BIAS_X/BIAS_Z/SPACING (light-bar geometry) aren't needed to
    // show "is there a PAPI/VASI here and what's its glideslope angle".
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct FacilityVasiData
    {
        public int Type;
        public float Angle;
    }

    // FREQUENCY: FREQUENCY is INT32 raw Hz (not FLOAT64 — this was the other
    // instance of the double/float-family bug). NAME is CHAR[64].
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct FacilityFrequencyData
    {
        public int Type;
        public int FrequencyHz;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string Name;
    }

    // TAXI_PARKING has no LATITUDE/LONGITUDE field at all (an earlier revision
    // requested it anyway, which just read garbage/adjacent memory — removed).
    // NAME and SUFFIX are enumerated INT32 codes (0-37), not strings, despite
    // the name — there is no free-text parking name in this API. HEADING and
    // RADIUS are FLOAT32. NUMBER is UINT32 (read into an int here; same 4-byte
    // layout, sign doesn't matter for realistic parking numbers). BiasX/BiasZ
    // (BIAS_X/BIAS_Z, appended after Radius — not inserted earlier, to avoid
    // disturbing the already-working fields' layout) are the spot's position
    // as a local-meters offset; assumed FLOAT32 like the rest of this struct's
    // continuous fields — UNCONFIRMED against a live sim.
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct FacilityTaxiParkingData
    {
        public int Type;
        public int NameCode;
        public int SuffixCode;
        public int Number;
        public float Heading;
        public float Radius;
        public float BiasX, BiasZ;
    }

    // TAXI_PATH has no NAME string field at all — only NAME_INDEX (UINT32), an
    // index into the separate TAXI_NAME facility type's rows for this airport
    // (resolved in OnFacilityDataEnd once all TAXI_NAME rows are in — order
    // between sub-types isn't guaranteed, so resolution can't happen inline).
    // An earlier revision requested a "NAME" field that doesn't exist on
    // TAXI_PATH itself, corrupting every subsequent field. WIDTH is FLOAT32.
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct FacilityTaxiPathData
    {
        public int Type;
        public float Width;
        public int Start, End;
        public uint NameIndex;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct FacilityTaxiNameData
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string Name;
    }

    // TAXI_POINT: field order per the SDK docs is TYPE, ORIENTATION, BIAS_X,
    // BIAS_Z. BiasX/BiasZ assumed FLOAT32 per this project's established
    // pattern (HEADING/LENGTH/WIDTH/RADIUS/FREQUENCY all turned out FLOAT32,
    // not FLOAT64) — UNCONFIRMED against a live sim, verify before trusting.
    // TaxiPathSegment.StartIndex/EndIndex reference these rows by ItemIndex
    // (see TaxiPointNode/OnFacilityData/ResolveTaxiPathPoints below), not by
    // arrival order.
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct FacilityTaxiPointData
    {
        public int Type;
        public int Orientation;
        public float BiasX, BiasZ;
    }

    // One TAXI_POINT row's resolved local-meters offset, keyed by ItemIndex
    // in PendingLookup.TaxiPoints — see ResolveTaxiPathPoints.
    private readonly record struct TaxiPointNode(float X, float Z);

    // Aggregation buffer for one in-flight GetAirportDetailsAsync call. Only one
    // lookup may be in flight at a time (enforced in GetAirportDetailsAsync).
    private sealed class PendingLookup
    {
        public required string Icao;
        public AirportDetails Details = new();
        public TaskCompletionSource<AirportLookupResult> Tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool GotAnyData;
        // TAXI_PARKING rows' ItemIndex (not their user-facing Number) — this is
        // what RequestJetwayData's "parking index" argument actually refers to.
        public List<int> ParkingIndices { get; } = [];
        // Parallel to Details.TaxiPaths (same add order) — each entry is that
        // path's raw NameIndex, resolved against TaxiNames once all data is in.
        public List<uint> TaxiPathNameIndices { get; } = [];
        public List<string> TaxiNames { get; } = [];
        // TAXI_POINT rows keyed by their own ItemIndex (not arrival order,
        // unlike TaxiNames above) — TaxiPathSegment.StartIndex/EndIndex
        // reference this same index space per the SDK docs. A dictionary
        // keyed by the row's actual ItemIndex is robust even if TAXI_POINT
        // rows don't arrive in strict ascending order; ResolveTaxiPathPoints
        // uses TryGetValue and leaves a segment's coordinates null rather
        // than mis-indexing if a point is ever missing.
        public Dictionary<int, TaxiPointNode> TaxiPoints { get; } = [];
        // Counts VASI rows since the last RUNWAY row arrived — 0/1/2/3 map to
        // primary-left/primary-right/secondary-left/secondary-right, matching
        // the request order in RegisterFacilityDefinition. Reset whenever a new
        // RUNWAY row arrives.
        public int VasiSlotIndex;
    }

    private PendingLookup? _pending;
    private PendingLookup? _jetwayPending;
    private TaskCompletionSource<bool>? _jetwayTcs;

    public void Connect(nint windowHandle)
    {
        _lastWindowHandle = windowHandle;
        if (IsConnected) return;
        try
        {
            CleanupSimConnect();

            _sc = new SimConnect("AirportSmith", windowHandle, WM_USER_SIMCONNECT, null, 0);

            _sc.OnRecvOpen           += OnOpen;
            _sc.OnRecvQuit           += OnQuit;
            _sc.OnRecvException      += OnException;
            _sc.OnRecvFacilityData    += OnFacilityData;
            _sc.OnRecvFacilityDataEnd += OnFacilityDataEnd;
            _sc.OnRecvJetwayData      += OnJetwayData;

            RegisterFacilityDefinition(_sc);

            if (_hwndSource is null)
            {
                _hwndSource = HwndSource.FromHwnd(windowHandle);
                _hwndSource?.AddHook(WndProc);
            }
        }
        catch (Exception ex) when (ex is COMException            // MSFS not running
                                    or FileNotFoundException        // native SimConnect.dll missing
                                    or BadImageFormatException      // architecture mismatch
                                    or TypeLoadException)           // assembly load failure
        {
            CleanupSimConnect();
        }
    }

    public void Disconnect()
    {
        _hwndSource?.RemoveHook(WndProc);
        _hwndSource = null;
        CleanupSimConnect();
        SetConnected(false);
    }

    public async Task<AirportLookupResult> GetAirportDetailsAsync(string icao, CancellationToken cancellationToken = default)
    {
        if (_pending is not null)
            throw new InvalidOperationException("A lookup is already in progress.");

        if (!IsConnected)
            Connect(_lastWindowHandle);

        if (!IsConnected || _sc is null)
            return new AirportLookupResult(AirportLookupStatus.NotConnected, null,
                "Not connected to the simulator. Make sure MSFS 2024 is running and try again.");

        var pending = new PendingLookup { Icao = icao };
        pending.Details.Icao = icao;
        _pending = pending;

        using var timeoutCts = new CancellationTokenSource(LookupTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        using var registration = linkedCts.Token.Register(() => pending.Tcs.TrySetResult(
            new AirportLookupResult(AirportLookupStatus.Timeout, null, "Timed out waiting for a response from the simulator.")));

        AirportLookupResult result;
        try
        {
            _sc.RequestFacilityData(FacilityDefs.AirportCore, FacilityReqs.AirportCore, icao, "");
            result = await pending.Tcs.Task;
        }
        catch (COMException ex)
        {
            result = new AirportLookupResult(AirportLookupStatus.Error, null, ex.Message);
        }
        finally
        {
            _pending = null;
        }

        if (result.Status == AirportLookupStatus.Success)
            await EnrichJetwaysAsync(pending);

        return result;
    }

    private void RegisterFacilityDefinition(SimConnect sc)
    {
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "OPEN AIRPORT");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "LATITUDE");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "LONGITUDE");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "ALTITUDE");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "MAGVAR");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "NAME");

        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "OPEN RUNWAY");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "LATITUDE");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "LONGITUDE");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "ALTITUDE");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "HEADING");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "LENGTH");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "WIDTH");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "SURFACE");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "PRIMARY_NUMBER");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "PRIMARY_DESIGNATOR");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "SECONDARY_NUMBER");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "SECONDARY_DESIGNATOR");

        // Order here fixes the VasiSlotIndex mapping in OnFacilityData — if the
        // sim doesn't deliver VASI rows in this exact request order, that
        // mapping needs to change (or a more reliable correlation found).
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "OPEN PRIMARY_LEFT_VASI");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "TYPE");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "ANGLE");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "CLOSE PRIMARY_LEFT_VASI");

        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "OPEN PRIMARY_RIGHT_VASI");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "TYPE");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "ANGLE");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "CLOSE PRIMARY_RIGHT_VASI");

        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "OPEN SECONDARY_LEFT_VASI");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "TYPE");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "ANGLE");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "CLOSE SECONDARY_LEFT_VASI");

        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "OPEN SECONDARY_RIGHT_VASI");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "TYPE");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "ANGLE");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "CLOSE SECONDARY_RIGHT_VASI");

        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "CLOSE RUNWAY");

        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "OPEN FREQUENCY");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "TYPE");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "FREQUENCY");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "NAME");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "CLOSE FREQUENCY");

        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "OPEN TAXI_PARKING");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "TYPE");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "NAME");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "SUFFIX");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "NUMBER");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "HEADING");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "RADIUS");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "BIAS_X");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "BIAS_Z");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "CLOSE TAXI_PARKING");

        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "OPEN TAXI_PATH");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "TYPE");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "WIDTH");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "START");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "END");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "NAME_INDEX");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "CLOSE TAXI_PATH");

        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "OPEN TAXI_NAME");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "NAME");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "CLOSE TAXI_NAME");

        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "OPEN TAXI_POINT");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "TYPE");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "ORIENTATION");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "BIAS_X");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "BIAS_Z");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "CLOSE TAXI_POINT");

        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "CLOSE AIRPORT");

        sc.RegisterFacilityDataDefineStruct<FacilityAirportData>(SIMCONNECT_FACILITY_DATA_TYPE.AIRPORT);
        sc.RegisterFacilityDataDefineStruct<FacilityRunwayData>(SIMCONNECT_FACILITY_DATA_TYPE.RUNWAY);
        sc.RegisterFacilityDataDefineStruct<FacilityVasiData>(SIMCONNECT_FACILITY_DATA_TYPE.VASI);
        sc.RegisterFacilityDataDefineStruct<FacilityFrequencyData>(SIMCONNECT_FACILITY_DATA_TYPE.FREQUENCY);
        sc.RegisterFacilityDataDefineStruct<FacilityTaxiParkingData>(SIMCONNECT_FACILITY_DATA_TYPE.TAXI_PARKING);
        sc.RegisterFacilityDataDefineStruct<FacilityTaxiPathData>(SIMCONNECT_FACILITY_DATA_TYPE.TAXI_PATH);
        sc.RegisterFacilityDataDefineStruct<FacilityTaxiNameData>(SIMCONNECT_FACILITY_DATA_TYPE.TAXI_NAME);
        sc.RegisterFacilityDataDefineStruct<FacilityTaxiPointData>(SIMCONNECT_FACILITY_DATA_TYPE.TAXI_POINT);
    }

    // Jetways come from a separate, older SimConnect API (RequestJetwayData /
    // OnRecvJetwayData) keyed by TAXI_PARKING item index, not a facility
    // definition struct. Best-effort: any failure here leaves Jetways empty
    // without failing the overall lookup, which already has the rest of the data.
    private async Task EnrichJetwaysAsync(PendingLookup pending)
    {
        if (pending.ParkingIndices.Count == 0 || _sc is null) return;

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _jetwayPending = pending;
        _jetwayTcs = tcs;

        using var timeoutCts = new CancellationTokenSource(JetwayTimeout);
        using var registration = timeoutCts.Token.Register(() => tcs.TrySetResult(false));

        try
        {
            _sc.RequestJetwayData(pending.Icao, pending.ParkingIndices);
            await tcs.Task;
        }
        catch
        {
            // Best-effort — see method comment.
        }
        finally
        {
            _jetwayPending = null;
            _jetwayTcs = null;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_USER_SIMCONNECT)
        {
            _sc?.ReceiveMessage();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void OnOpen(SimConnect sender, SIMCONNECT_RECV_OPEN data) => SetConnected(true);

    private void OnQuit(SimConnect sender, SIMCONNECT_RECV data)
    {
        _pending?.Tcs.TrySetResult(new AirportLookupResult(AirportLookupStatus.Error, null, "The simulator disconnected."));
        _pending = null;
        _jetwayTcs?.TrySetResult(false);
        _jetwayTcs = null;
        CleanupSimConnect();
        SetConnected(false);
    }

    private void OnException(SimConnect sender, SIMCONNECT_RECV_EXCEPTION data)
    {
        // Not reliably correlatable to a specific pending request (the managed
        // wrapper doesn't expose the send-ID needed to match dwSendID back to a
        // request here) — the pending lookup's own timeout is the safety net if
        // an exception silently drops a request instead of completing it.
        var exception = Enum.IsDefined(typeof(SIMCONNECT_EXCEPTION), (int)data.dwException)
            ? ((SIMCONNECT_EXCEPTION)data.dwException).ToString()
            : $"unknown ({data.dwException})";
        Console.WriteLine($"[SimConnect] Exception received: {exception} (index {data.dwIndex})");
    }

    private void OnFacilityData(SimConnect sender, SIMCONNECT_RECV_FACILITY_DATA data)
    {
        var pending = _pending;
        if (pending is null || data.UserRequestId != (uint)FacilityReqs.AirportCore) return;

        pending.GotAnyData = true;

        switch ((SIMCONNECT_FACILITY_DATA_TYPE)data.Type)
        {
            case SIMCONNECT_FACILITY_DATA_TYPE.AIRPORT:
                var a = (FacilityAirportData)data.Data[0];
                pending.Details.Name = a.Name?.Trim() ?? string.Empty;
                pending.Details.Latitude = a.Latitude;
                pending.Details.Longitude = a.Longitude;
                pending.Details.ElevationMeters = a.Altitude;
                pending.Details.MagneticVariationDeg = a.MagVar;
                break;

            case SIMCONNECT_FACILITY_DATA_TYPE.RUNWAY:
                var r = (FacilityRunwayData)data.Data[0];
                pending.Details.Runways.Add(new Runway
                {
                    PrimaryDesignation = DesignatorLabel(r.PrimaryNumber, r.PrimaryDesignator),
                    SecondaryDesignation = DesignatorLabel(r.SecondaryNumber, r.SecondaryDesignator),
                    Latitude = r.Latitude,
                    Longitude = r.Longitude,
                    HeadingDeg = r.Heading,
                    LengthMeters = r.Length,
                    WidthMeters = r.Width,
                    SurfaceType = r.Surface,
                    ElevationMeters = r.Altitude,
                });
                pending.VasiSlotIndex = 0;
                break;

            case SIMCONNECT_FACILITY_DATA_TYPE.VASI:
                if (pending.Details.Runways.Count == 0) break;
                var v = (FacilityVasiData)data.Data[0];
                var runway = pending.Details.Runways[^1];
                // Confirmed against a live sim: all four slots are always sent
                // (in request order — the VasiSlotIndex correlation is correct),
                // and TYPE 0 means "no VASI/PAPI installed" here rather than a
                // real enum value — ANGLE is a meaningless fixed default (3) in
                // that case, not a real glideslope angle, so both stay null.
                if (v.Type != 0)
                {
                    switch (pending.VasiSlotIndex)
                    {
                        case 0: runway.PrimaryLeftVasiType = v.Type; runway.PrimaryLeftVasiAngleDeg = v.Angle; break;
                        case 1: runway.PrimaryRightVasiType = v.Type; runway.PrimaryRightVasiAngleDeg = v.Angle; break;
                        case 2: runway.SecondaryLeftVasiType = v.Type; runway.SecondaryLeftVasiAngleDeg = v.Angle; break;
                        case 3: runway.SecondaryRightVasiType = v.Type; runway.SecondaryRightVasiAngleDeg = v.Angle; break;
                    }
                }
                pending.VasiSlotIndex++;
                break;

            case SIMCONNECT_FACILITY_DATA_TYPE.FREQUENCY:
                var f = (FacilityFrequencyData)data.Data[0];
                pending.Details.Frequencies.Add(new Frequency
                {
                    Type = f.Type,
                    FrequencyHz = f.FrequencyHz,
                    Name = f.Name?.Trim() ?? string.Empty,
                });
                break;

            case SIMCONNECT_FACILITY_DATA_TYPE.TAXI_PARKING:
                var p = (FacilityTaxiParkingData)data.Data[0];
                pending.Details.ParkingSpots.Add(new TaxiParkingSpot
                {
                    Number = p.Number,
                    Type = p.Type,
                    NameCode = p.NameCode,
                    SuffixCode = p.SuffixCode,
                    HeadingDeg = p.Heading,
                    RadiusMeters = p.Radius,
                    BiasXMeters = p.BiasX,
                    BiasZMeters = p.BiasZ,
                });
                // ItemIndex is what RequestJetwayData's "parking index" argument
                // refers to — not the user-facing gate Number field.
                pending.ParkingIndices.Add((int)data.ItemIndex);
                break;

            case SIMCONNECT_FACILITY_DATA_TYPE.TAXI_PATH:
                var t = (FacilityTaxiPathData)data.Data[0];
                pending.Details.TaxiPaths.Add(new TaxiPathSegment
                {
                    Type = t.Type,
                    StartIndex = t.Start,
                    EndIndex = t.End,
                    WidthMeters = t.Width,
                });
                // Kept parallel to Details.TaxiPaths (same add order); resolved
                // against TaxiNames in OnFacilityDataEnd once all rows are in.
                pending.TaxiPathNameIndices.Add(t.NameIndex);
                break;

            case SIMCONNECT_FACILITY_DATA_TYPE.TAXI_NAME:
                var n = (FacilityTaxiNameData)data.Data[0];
                pending.TaxiNames.Add(n.Name?.Trim() ?? string.Empty);
                break;

            case SIMCONNECT_FACILITY_DATA_TYPE.TAXI_POINT:
                var tp = (FacilityTaxiPointData)data.Data[0];
                pending.TaxiPoints[(int)data.ItemIndex] = new TaxiPointNode(tp.BiasX, tp.BiasZ);
                break;
        }
    }

    private void OnFacilityDataEnd(SimConnect sender, SIMCONNECT_RECV_FACILITY_DATA_END data)
    {
        var pending = _pending;
        if (pending is null || data.RequestId != (uint)FacilityReqs.AirportCore) return;

        ResolveTaxiPathNames(pending);
        ResolveTaxiPathPoints(pending);

        var result = pending.GotAnyData
            ? new AirportLookupResult(AirportLookupStatus.Success, pending.Details, null)
            : new AirportLookupResult(AirportLookupStatus.NotFound, null, $"No airport found for ICAO \"{pending.Icao}\".");

        pending.Tcs.TrySetResult(result);
    }

    // TAXI_PATH rows and TAXI_NAME rows can arrive in any order relative to each
    // other, so NAME_INDEX can't be resolved inline in OnFacilityData — only once
    // every row for this request is in.
    private static void ResolveTaxiPathNames(PendingLookup pending)
    {
        for (int i = 0; i < pending.Details.TaxiPaths.Count; i++)
        {
            var index = pending.TaxiPathNameIndices[i];
            if (index < pending.TaxiNames.Count)
                pending.Details.TaxiPaths[i].Name = pending.TaxiNames[(int)index];
        }
    }

    // TaxiPathSegment.StartIndex/EndIndex are positions into this request's
    // TAXI_POINT rows (by ItemIndex), resolved in the same deferred pass as
    // taxi path names for the identical reason — order isn't guaranteed.
    private static void ResolveTaxiPathPoints(PendingLookup pending)
    {
        foreach (var segment in pending.Details.TaxiPaths)
        {
            if (pending.TaxiPoints.TryGetValue(segment.StartIndex, out var s))
            {
                segment.StartXMeters = s.X;
                segment.StartZMeters = s.Z;
            }
            if (pending.TaxiPoints.TryGetValue(segment.EndIndex, out var e))
            {
                segment.EndXMeters = e.X;
                segment.EndZMeters = e.Z;
            }
        }
    }

    // rgData may arrive across more than one event for airports with many
    // jetways (dwEntryNumber/dwOutOf paginate like other SimConnect list
    // responses) — accumulate across calls and only resolve on the last chunk.
    private void OnJetwayData(SimConnect sender, SIMCONNECT_RECV_JETWAY_DATA data)
    {
        var pending = _jetwayPending;
        if (pending is null) return;

        for (int i = 0; i < data.dwArraySize; i++)
        {
            var j = (SIMCONNECT_JETWAY_DATA)data.rgData[i];
            pending.Details.Jetways.Add(new Jetway
            {
                ParkingIndex = j.ParkingIndex,
                JetwayObjectId = (int)j.JetwayObjectId,
                Status = j.Status,
                Latitude = j.Lla.Latitude,
                Longitude = j.Lla.Longitude,
                HeadingDeg = j.Pbh.Heading,
            });
        }

        if (data.dwOutOf == 0 || data.dwEntryNumber + 1 >= data.dwOutOf)
            _jetwayTcs?.TrySetResult(true);
    }

    private static string DesignatorLabel(int number, int designator) => designator switch
    {
        1 => $"{number:00}L",
        2 => $"{number:00}R",
        3 => $"{number:00}C",
        _ => $"{number:00}",
    };

    private void CleanupSimConnect()
    {
        try { _sc?.Dispose(); } catch { }
        _sc = null;
    }

    private void SetConnected(bool value)
    {
        if (IsConnected == value) return;
        IsConnected = value;
        ConnectionChanged?.Invoke(this, EventArgs.Empty);
    }
}
