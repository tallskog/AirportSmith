namespace AirportSmith.Models;

public class Runway
{
    public string PrimaryDesignation { get; set; } = string.Empty;
    public string SecondaryDesignation { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double ElevationMeters { get; set; }
    public double HeadingDeg { get; set; }
    public double LengthMeters { get; set; }
    public double WidthMeters { get; set; }

    // Enum-backed per SimConnect (0-32 pavement types, 254=UNKNOWN, 255=UNDEFINED);
    // kept raw until worth mapping to friendly labels.
    public int SurfaceType { get; set; }

    // RUNWAY.EDGE_LIGHTS per the SDK docs — always present (not a "not
    // installed" sentinel like the VASI/approach-light fields below), so
    // this is a plain non-nullable enum defaulting to None.
    public RunwayLightIntensity EdgeLightIntensity { get; set; }

    // VASI/PAPI light data per runway end/side, from the RUNWAY facility type's
    // four nested VASI slots. Null means no VASI/PAPI installed for that
    // end/side — confirmed against a live sim that SimConnect reports this as
    // Type 0 (with a meaningless default Angle) rather than omitting the row,
    // and SimConnectService maps that to null here.
    public VasiType? PrimaryLeftVasiType { get; set; }
    public double? PrimaryLeftVasiAngleDeg { get; set; }
    public VasiType? PrimaryRightVasiType { get; set; }
    public double? PrimaryRightVasiAngleDeg { get; set; }
    public VasiType? SecondaryLeftVasiType { get; set; }
    public double? SecondaryLeftVasiAngleDeg { get; set; }
    public VasiType? SecondaryRightVasiType { get; set; }
    public double? SecondaryRightVasiAngleDeg { get; set; }

    // VASI's BIAS_X/BIAS_Z/SPACING per the SDK docs — the slot's position as
    // a local-meters offset from the runway centerline/center point (same
    // convention as TaxiPathSegment's resolved coordinates and
    // TaxiParkingSpot.BiasXMeters/BiasZMeters) plus the spacing between light
    // rows. Added purely additively alongside the pre-existing Type/AngleDeg
    // pair per slot (not merged into a record) so older saved projects keep
    // deserializing unchanged — these three are simply null for a project
    // saved before this field was added, same "missing means not yet known"
    // convention as everywhere else nullable in this class. Needed for
    // AirportXmlExporter's <Vasi> element, which requires biasX/biasZ/spacing
    // as attributes.
    public double? PrimaryLeftVasiBiasXMeters { get; set; }
    public double? PrimaryLeftVasiBiasZMeters { get; set; }
    public double? PrimaryLeftVasiSpacingMeters { get; set; }
    public double? PrimaryRightVasiBiasXMeters { get; set; }
    public double? PrimaryRightVasiBiasZMeters { get; set; }
    public double? PrimaryRightVasiSpacingMeters { get; set; }
    public double? SecondaryLeftVasiBiasXMeters { get; set; }
    public double? SecondaryLeftVasiBiasZMeters { get; set; }
    public double? SecondaryLeftVasiSpacingMeters { get; set; }
    public double? SecondaryRightVasiBiasXMeters { get; set; }
    public double? SecondaryRightVasiBiasZMeters { get; set; }
    public double? SecondaryRightVasiSpacingMeters { get; set; }

    // RUNWAY's six nested PAVEMENT sub-structures (PRIMARY_THRESHOLD/
    // PRIMARY_BLASTPAD/PRIMARY_OVERRUN and their SECONDARY_ counterparts).
    // Null means "not present" (the PAVEMENT struct's ENABLE field was 0),
    // matching the VASI Type==0 "not installed" convention above — not a
    // zero-length feature.
    public RunwayPavementFeature? PrimaryThreshold { get; set; }
    public RunwayPavementFeature? PrimaryBlastPad { get; set; }
    public RunwayPavementFeature? PrimaryOverrun { get; set; }
    public RunwayPavementFeature? SecondaryThreshold { get; set; }
    public RunwayPavementFeature? SecondaryBlastPad { get; set; }
    public RunwayPavementFeature? SecondaryOverrun { get; set; }

    // RUNWAY's two nested APPROACH_LIGHTS sub-structures (PRIMARY_APPROACH_LIGHTS/
    // SECONDARY_APPROACH_LIGHTS). Null means "not present" (the structure's
    // SYSTEM field was 0/NONE), matching the VASI Type==0 convention above —
    // NOT the structure's own ENABLE field, which (per the SDK's Facility
    // Data reference) means "are the lights currently enabled", an
    // operational flag distinct from whether the system is installed at
    // all — see SimConnectService.FacilityApproachLightsData's comment.
    public ApproachLightSystem? PrimaryApproachLights { get; set; }
    public ApproachLightSystem? SecondaryApproachLights { get; set; }

    // APPROACH_LIGHTS' STROBE_COUNT/HAS_END_LIGHTS/HAS_REIL_LIGHTS/
    // HAS_TOUCHDOWN_LIGHTS per the SDK docs — kept as flat properties
    // directly on Runway (like the VASI Bias/Spacing fields above) rather
    // than folded into ApproachLightSystem, deliberately: unlike SystemType,
    // these are genuinely independent of whether a full approach light
    // system is installed at all (a real, fairly common runway has REIL or
    // touchdown lights with no approach light system whatsoever), so tying
    // their presence to PrimaryApproachLights/SecondaryApproachLights being
    // non-null would make that real-world case unrepresentable. Plain
    // bool/int (not nullable), default false/0 — same "always resolved,
    // never ambiguous" convention as EdgeLightIntensity/
    // TaxiPathSegment.LeftEdgeLighted above/elsewhere, since SimConnect
    // reports a real 0/1 or count for these regardless of SYSTEM, and 0/false
    // is exactly what "missing from an older saved project" should default
    // to as well. Added purely additively, so older saved projects keep
    // deserializing unchanged (see the VASI Bias/Spacing fields' own comment
    // for this same convention).
    public int PrimaryApproachLightsStrobeCount { get; set; }
    public bool PrimaryApproachLightsHasEndLights { get; set; }
    public bool PrimaryApproachLightsHasReilLights { get; set; }
    public bool PrimaryApproachLightsHasTouchdownLights { get; set; }
    public int SecondaryApproachLightsStrobeCount { get; set; }
    public bool SecondaryApproachLightsHasEndLights { get; set; }
    public bool SecondaryApproachLightsHasReilLights { get; set; }
    public bool SecondaryApproachLightsHasTouchdownLights { get; set; }
}
