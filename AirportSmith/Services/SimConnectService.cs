using AirportSmith.Models;
using Microsoft.FlightSimulator.SimConnect;
using System.IO;
using System.Linq;
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
    // PRIMARY_*/SECONDARY_* designators are INT32. EDGE_LIGHTS is documented
    // as INT8 (sbyte) — the only INT8-sized field requested anywhere in this
    // codebase so far, and this project has already been bitten once by a
    // doc-declared size not matching the actual wire size (see the FLOAT32/
    // FLOAT64 bug above), so this is UNCONFIRMED against a live sim: if
    // SURFACE/PRIMARY_NUMBER/etc. downstream of it come back corrupted,
    // widen this to int and re-verify.
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct FacilityRunwayData
    {
        public double Latitude, Longitude, Altitude;
        public float Heading, Length, Width;
        public int Surface;
        public int PrimaryNumber, PrimaryDesignator, SecondaryNumber, SecondaryDesignator;
        public sbyte EdgeLights;
    }

    // VASI/PAPI light data for one runway end-side. RUNWAY nests up to four of
    // these by name (PRIMARY_LEFT_VASI, PRIMARY_RIGHT_VASI, SECONDARY_LEFT_VASI,
    // SECONDARY_RIGHT_VASI) rather than requesting them as its own scalar
    // fields, so each arrives as its own SIMCONNECT_FACILITY_DATA_TYPE.VASI row
    // — see the VASI case in OnFacilityData for how a row is matched back to
    // which of the four named slots it came from. Confirmed against a live sim
    // (a runway with no PAPI at all): all four slots are always sent in request
    // order, with TYPE 0 meaning "none installed" here. BIAS_X/BIAS_Z/SPACING
    // (light-bar geometry) were added for the XML export feature, which needs
    // real VASI placement rather than just presence/angle — field order here
    // MUST match the AddToFacilityDefinition request order below exactly
    // (TYPE, BIAS_X, BIAS_Z, SPACING, ANGLE), same fragility as every other
    // struct in this file.
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct FacilityVasiData
    {
        public int Type;
        public float BiasX;
        public float BiasZ;
        public float Spacing;
        public float Angle;
    }

    // PAVEMENT: the six sub-structures RUNWAY nests for PRIMARY_THRESHOLD/
    // PRIMARY_BLASTPAD/PRIMARY_OVERRUN and their SECONDARY_ counterparts, same
    // nesting-by-name pattern as VASI. LENGTH/WIDTH are FLOAT32 per the SDK
    // docs; ENABLE (INT32) is treated as a bool like VASI's TYPE==0 — see the
    // PAVEMENT case in OnFacilityData for how a row is matched back to which
    // of the six named slots it came from (PendingLookup.PavementSlotIndex,
    // reset on each new RUNWAY row exactly like VasiSlotIndex).
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct FacilityPavementData
    {
        public float Length, Width;
        public int Enable;
    }

    // APPROACH_LIGHTS: the two sub-structures RUNWAY nests for
    // PRIMARY_APPROACH_LIGHTS/SECONDARY_APPROACH_LIGHTS, same nesting-by-name
    // pattern as VASI/PAVEMENT. Only SYSTEM is requested (not STROBE_COUNT/
    // HAS_END_LIGHTS/HAS_REIL_LIGHTS/HAS_TOUCHDOWN_LIGHTS/ON_GROUND/OFFSET/
    // SPACING/SLOPE, which this project doesn't use yet). Deliberately NOT
    // requesting ENABLE: per the SDK's Facility Data reference, this
    // struct's ENABLE means "whether the approach lights are [currently]
    // enabled" — an operational/runtime flag — unlike PAVEMENT's ENABLE
    // ("whether the pavement area is actually... present"), which is a
    // structural existence flag. An earlier revision of this file treated
    // the two as the same convention and gated presence on ENABLE!=0, which
    // silently dropped every real SYSTEM value on a live sim (reported by
    // the user as "no approach light data on any runway checked") — SYSTEM
    // has an explicit 0=NONE value of its own (see OnFacilityData below),
    // so it's used as the presence signal instead, the same way VASI's own
    // TYPE==0 already is (VASI has no ENABLE field at all).
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct FacilityApproachLightsData
    {
        public int System;
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
    // RUNWAY_NUMBER/RUNWAY_DESIGNATOR/LEFT_EDGE/LEFT_EDGE_LIGHTED/RIGHT_EDGE/
    // RIGHT_EDGE_LIGHTED/CENTER_LINE/CENTER_LINE_LIGHTED are all INT32 (bools
    // among them use the SDK's usual 0/false convention) — field order here
    // must match the request order added in RegisterFacilityDefinition
    // exactly, which in turn follows the SDK docs' own TAXI_PATH field order.
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct FacilityTaxiPathData
    {
        public int Type;
        public float Width;
        public int RunwayNumber, RunwayDesignator;
        public int LeftEdge, LeftEdgeLighted;
        public int RightEdge, RightEdgeLighted;
        public int CenterLine, CenterLineLighted;
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

    // One TAXI_POINT row's resolved local-meters offset plus its own raw
    // TYPE/ORIENTATION, keyed by ItemIndex in PendingLookup.TaxiPoints — see
    // ResolveTaxiPathPoints. Type/Orientation used to be read off
    // FacilityTaxiPointData and immediately discarded rather than stored
    // here — see TaxiPathSegment.StartPointType's comment for why that was a
    // real bug, not a deliberate simplification.
    private readonly record struct TaxiPointNode(float X, float Z, int Type, int Orientation);

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
        // Same idea as VasiSlotIndex but for PAVEMENT rows — 0..5 map to
        // primary threshold/blastpad/overrun then secondary threshold/blastpad/
        // overrun, matching the request order in RegisterFacilityDefinition.
        // Reset whenever a new RUNWAY row arrives.
        public int PavementSlotIndex;
        // Same idea again but for APPROACH_LIGHTS rows — 0/1 map to
        // primary/secondary, matching the request order in
        // RegisterFacilityDefinition. Reset whenever a new RUNWAY row arrives.
        public int ApproachLightsSlotIndex;
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
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "EDGE_LIGHTS");

        // Order here fixes the PavementSlotIndex mapping in OnFacilityData —
        // same fragility as the VASI ordering comment below.
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "OPEN PRIMARY_THRESHOLD");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "LENGTH");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "WIDTH");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "ENABLE");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "CLOSE PRIMARY_THRESHOLD");

        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "OPEN PRIMARY_BLASTPAD");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "LENGTH");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "WIDTH");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "ENABLE");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "CLOSE PRIMARY_BLASTPAD");

        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "OPEN PRIMARY_OVERRUN");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "LENGTH");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "WIDTH");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "ENABLE");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "CLOSE PRIMARY_OVERRUN");

        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "OPEN SECONDARY_THRESHOLD");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "LENGTH");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "WIDTH");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "ENABLE");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "CLOSE SECONDARY_THRESHOLD");

        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "OPEN SECONDARY_BLASTPAD");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "LENGTH");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "WIDTH");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "ENABLE");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "CLOSE SECONDARY_BLASTPAD");

        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "OPEN SECONDARY_OVERRUN");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "LENGTH");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "WIDTH");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "ENABLE");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "CLOSE SECONDARY_OVERRUN");

        // Order here fixes the VasiSlotIndex mapping in OnFacilityData — if the
        // sim doesn't deliver VASI rows in this exact request order, that
        // mapping needs to change (or a more reliable correlation found).
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "OPEN PRIMARY_LEFT_VASI");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "TYPE");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "BIAS_X");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "BIAS_Z");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "SPACING");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "ANGLE");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "CLOSE PRIMARY_LEFT_VASI");

        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "OPEN PRIMARY_RIGHT_VASI");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "TYPE");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "BIAS_X");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "BIAS_Z");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "SPACING");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "ANGLE");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "CLOSE PRIMARY_RIGHT_VASI");

        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "OPEN SECONDARY_LEFT_VASI");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "TYPE");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "BIAS_X");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "BIAS_Z");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "SPACING");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "ANGLE");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "CLOSE SECONDARY_LEFT_VASI");

        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "OPEN SECONDARY_RIGHT_VASI");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "TYPE");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "BIAS_X");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "BIAS_Z");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "SPACING");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "ANGLE");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "CLOSE SECONDARY_RIGHT_VASI");

        // Order here fixes the ApproachLightsSlotIndex mapping in
        // OnFacilityData — same fragility as the VASI ordering comment above.
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "OPEN PRIMARY_APPROACH_LIGHTS");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "SYSTEM");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "CLOSE PRIMARY_APPROACH_LIGHTS");

        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "OPEN SECONDARY_APPROACH_LIGHTS");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "SYSTEM");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "CLOSE SECONDARY_APPROACH_LIGHTS");

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
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "RUNWAY_NUMBER");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "RUNWAY_DESIGNATOR");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "LEFT_EDGE");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "LEFT_EDGE_LIGHTED");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "RIGHT_EDGE");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "RIGHT_EDGE_LIGHTED");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "CENTER_LINE");
        sc.AddToFacilityDefinition(FacilityDefs.AirportCore, "CENTER_LINE_LIGHTED");
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
        sc.RegisterFacilityDataDefineStruct<FacilityPavementData>(SIMCONNECT_FACILITY_DATA_TYPE.PAVEMENT);
        sc.RegisterFacilityDataDefineStruct<FacilityApproachLightsData>(SIMCONNECT_FACILITY_DATA_TYPE.APPROACH_LIGHTS);
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
                    EdgeLightIntensity = (RunwayLightIntensity)r.EdgeLights,
                });
                pending.VasiSlotIndex = 0;
                pending.PavementSlotIndex = 0;
                pending.ApproachLightsSlotIndex = 0;
                break;

            case SIMCONNECT_FACILITY_DATA_TYPE.PAVEMENT:
                if (pending.Details.Runways.Count == 0) break;
                var pv = (FacilityPavementData)data.Data[0];
                var runwayForPavement = pending.Details.Runways[^1];
                // ENABLE==0 means "not present" here, same convention as
                // VASI's TYPE==0 — LENGTH/WIDTH aren't meaningful in that case.
                if (pv.Enable != 0)
                {
                    var feature = new RunwayPavementFeature(pv.Length, pv.Width);
                    switch (pending.PavementSlotIndex)
                    {
                        case 0: runwayForPavement.PrimaryThreshold = feature; break;
                        case 1: runwayForPavement.PrimaryBlastPad = feature; break;
                        case 2: runwayForPavement.PrimaryOverrun = feature; break;
                        case 3: runwayForPavement.SecondaryThreshold = feature; break;
                        case 4: runwayForPavement.SecondaryBlastPad = feature; break;
                        case 5: runwayForPavement.SecondaryOverrun = feature; break;
                    }
                }
                pending.PavementSlotIndex++;
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
                    var vasiType = (VasiType)v.Type;
                    switch (pending.VasiSlotIndex)
                    {
                        case 0:
                            runway.PrimaryLeftVasiType = vasiType; runway.PrimaryLeftVasiAngleDeg = v.Angle;
                            runway.PrimaryLeftVasiBiasXMeters = v.BiasX; runway.PrimaryLeftVasiBiasZMeters = v.BiasZ; runway.PrimaryLeftVasiSpacingMeters = v.Spacing;
                            break;
                        case 1:
                            runway.PrimaryRightVasiType = vasiType; runway.PrimaryRightVasiAngleDeg = v.Angle;
                            runway.PrimaryRightVasiBiasXMeters = v.BiasX; runway.PrimaryRightVasiBiasZMeters = v.BiasZ; runway.PrimaryRightVasiSpacingMeters = v.Spacing;
                            break;
                        case 2:
                            runway.SecondaryLeftVasiType = vasiType; runway.SecondaryLeftVasiAngleDeg = v.Angle;
                            runway.SecondaryLeftVasiBiasXMeters = v.BiasX; runway.SecondaryLeftVasiBiasZMeters = v.BiasZ; runway.SecondaryLeftVasiSpacingMeters = v.Spacing;
                            break;
                        case 3:
                            runway.SecondaryRightVasiType = vasiType; runway.SecondaryRightVasiAngleDeg = v.Angle;
                            runway.SecondaryRightVasiBiasXMeters = v.BiasX; runway.SecondaryRightVasiBiasZMeters = v.BiasZ; runway.SecondaryRightVasiSpacingMeters = v.Spacing;
                            break;
                    }
                }
                pending.VasiSlotIndex++;
                break;

            case SIMCONNECT_FACILITY_DATA_TYPE.APPROACH_LIGHTS:
                if (pending.Details.Runways.Count == 0) break;
                var al = (FacilityApproachLightsData)data.Data[0];
                var runwayForApproachLights = pending.Details.Runways[^1];
                // SYSTEM==0 (NONE) means "not present" here, same convention
                // as VASI's TYPE==0 — see FacilityApproachLightsData's
                // comment for why ENABLE (used for PAVEMENT's own presence
                // check) isn't the right signal for this struct.
                if (al.System != 0)
                {
                    var lights = new ApproachLightSystem((ApproachLightSystemType)al.System);
                    switch (pending.ApproachLightsSlotIndex)
                    {
                        case 0: runwayForApproachLights.PrimaryApproachLights = lights; break;
                        case 1: runwayForApproachLights.SecondaryApproachLights = lights; break;
                    }
                }
                pending.ApproachLightsSlotIndex++;
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
                    ItemIndex = (int)data.ItemIndex,
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
                    Type = (TaxiPathType)t.Type,
                    StartIndex = t.Start,
                    EndIndex = t.End,
                    WidthMeters = t.Width,
                    RunwayNumber = t.RunwayNumber,
                    RunwayDesignator = (TaxiPathRunwayDesignator)t.RunwayDesignator,
                    LeftEdge = (TaxiEdgeType)t.LeftEdge,
                    RightEdge = (TaxiEdgeType)t.RightEdge,
                    LeftEdgeLighted = t.LeftEdgeLighted != 0,
                    RightEdgeLighted = t.RightEdgeLighted != 0,
                    CenterLine = t.CenterLine != 0,
                    CenterLineLighted = t.CenterLineLighted != 0,
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
                pending.TaxiPoints[(int)data.ItemIndex] = new TaxiPointNode(tp.BiasX, tp.BiasZ, tp.Type, tp.Orientation);
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
    // every row for this request is in. Builds Details.TaxiNames (one TaxiName
    // per raw TAXI_NAME row, in arrival order, each with a fresh Id) and points
    // each TaxiPathSegment.TaxiNameId at the matching entry — see TaxiName.cs/
    // TaxiPathSegment.TaxiNameId for why a stable Id is used instead of keeping
    // the raw array index around.
    private static void ResolveTaxiPathNames(PendingLookup pending)
    {
        var taxiNames = pending.TaxiNames.Select(value => new TaxiName { Value = value }).ToList();
        pending.Details.TaxiNames = taxiNames;

        for (int i = 0; i < pending.Details.TaxiPaths.Count; i++)
        {
            var index = pending.TaxiPathNameIndices[i];
            if (index < taxiNames.Count)
                pending.Details.TaxiPaths[i].TaxiNameId = taxiNames[(int)index].Id;
        }
    }

    // TaxiPathSegment.StartIndex/EndIndex are positions into this request's
    // TAXI_POINT rows (by ItemIndex), resolved in the same deferred pass as
    // taxi path names for the identical reason — order isn't guaranteed.
    //
    // EXCEPT for a Type == Parking segment's EndIndex: per the SDK docs,
    // TAXI_PATH.START/END is "the index number of taxiway point OR PARKING
    // SPACE the path starts from/ends on" — not always a TAXI_POINT index.
    // Confirmed against real extracted airport data that every PARKING-type
    // path's End is a TAXI_PARKING ItemIndex, not a TAXI_POINT one: resolving
    // it against TaxiPoints (as this method used to do unconditionally)
    // happened to "succeed" every time — TAXI_POINT and TAXI_PARKING indices
    // both start at 0, so the wrong dictionary almost always has *some* entry
    // at that index — silently returning an unrelated point elsewhere on the
    // airport instead of failing loudly. That produced "taxi path" segments
    // hundreds to thousands of meters long for what should be a short
    // stand-to-taxiway stub, which is what an MSFS 2024 SDK Scenery Editor
    // import flagged as broken taxiway network connectivity.
    private static void ResolveTaxiPathPoints(PendingLookup pending)
    {
        // TryAdd rather than ToDictionary — defensive against a malformed
        // response with a duplicate ItemIndex, which would otherwise throw
        // and fail the whole lookup over one bad row.
        var parkingByItemIndex = new Dictionary<int, TaxiParkingSpot>();
        foreach (var spot in pending.Details.ParkingSpots)
            parkingByItemIndex.TryAdd(spot.ItemIndex, spot);

        foreach (var segment in pending.Details.TaxiPaths)
        {
            if (pending.TaxiPoints.TryGetValue(segment.StartIndex, out var s))
            {
                segment.StartXMeters = s.X;
                segment.StartZMeters = s.Z;
                segment.StartPointType = ResolveTaxiPointType(s.Type);
                segment.StartPointOrientation = ResolveTaxiPointOrientation(s.Orientation);
            }

            if (segment.Type == TaxiPathType.Parking)
            {
                if (parkingByItemIndex.TryGetValue(segment.EndIndex, out var parkingSpot))
                {
                    segment.EndXMeters = parkingSpot.BiasXMeters;
                    segment.EndZMeters = parkingSpot.BiasZMeters;
                }
                // No EndPointType/EndPointOrientation here — a parking spot
                // isn't a TAXI_POINT row, so that concept doesn't apply.
            }
            else if (pending.TaxiPoints.TryGetValue(segment.EndIndex, out var e))
            {
                segment.EndXMeters = e.X;
                segment.EndZMeters = e.Z;
                segment.EndPointType = ResolveTaxiPointType(e.Type);
                segment.EndPointOrientation = ResolveTaxiPointOrientation(e.Orientation);
            }
        }
    }

    // TYPE 0 (NONE) and anything outside the documented 1/2/4/5/6 set have no
    // meaningful named value — left null rather than guessing, same
    // convention as VasiType/ApproachLightSystemType's "0 means not present".
    private static TaxiPointType? ResolveTaxiPointType(int rawType) =>
        rawType is 1 or 2 or 4 or 5 or 6 ? (TaxiPointType)rawType : null;

    private static TaxiPointOrientation? ResolveTaxiPointOrientation(int rawOrientation) =>
        rawOrientation is 0 or 1 ? (TaxiPointOrientation)rawOrientation : null;

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
