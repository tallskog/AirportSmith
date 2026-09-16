using AirportSmith.Models;

namespace AirportSmith.Services;

// One skipped/defaulted/truncated piece of data noticed while building the
// export (e.g. a taxi path type with no XML equivalent, a taxi name
// truncated to fit stString8, an unmapped surface/parking code) — surfaced
// to the user rather than silently dropped, per this project's "be honest
// about gaps" convention (see requirements.md's Known limitations for this
// epic).
public sealed record AirportXmlExportOutcome(string FilePath, IReadOnlyList<string> Warnings);

// Writes a bglcomp.xsd-conformant <FSData><Airport>...</Airport></FSData>
// document for the loaded airport, ready to compile with the MSFS 2024 SDK's
// bglcomp or import into the Dev Mode Scenery Editor. See AirportXmlExporter
// for the actual mapping; kept as an interface so MainViewModel stays
// unit-testable via a fake, same pattern as IAirportProjectStore/IDebugDataStore.
public interface IAirportXmlExporter
{
    AirportXmlExportOutcome Export(AirportDetails airport, string filePath);
}
