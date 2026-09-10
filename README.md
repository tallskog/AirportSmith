# AirportSmith

A Windows desktop editor for producing customized versions of default MSFS 2024 airports — edit a stock airport's runways, taxiways, and parking, then package the result as a Community-folder add-on that overrides the sim's built-in version at that ICAO.

**Status: v0.1 — read-only extraction.** AirportSmith can connect to a running MSFS 2024 session via SimConnect and display an airport's runways, frequencies, taxi parking, taxiway path network, and jetways for a given ICAO. Editing and packaging are not yet implemented — see [`requirements.md`](requirements.md) for the full roadmap, and [`CLAUDE.md`](CLAUDE.md) for the domain research summary and project conventions.

## Usage

1. Start MSFS 2024 and load into any session (the main menu is sufficient — an aircraft/flight does not need to be active).
2. Launch AirportSmith, type an ICAO code, and click **Load Airport**.

In Debug builds only, two extra buttons appear: **Export Debug Data** (once an airport is loaded) saves the loaded data as JSON to a temp folder (`%Temp%\AirportSmith-dev`) for manual inspection/debugging — files are kept across app runs, not deleted on exit. **Load Debug Data File** opens a file picker on that same folder and loads a previously exported JSON file back in, without needing MSFS running. Neither button is present in Release builds.

A **Diagram** tab shows a top-down rendering of the loaded airport's runways, taxiways (pavement bands with a centerline — solid blue and labelled with the taxiway name if named, dashed gray if not, a quick visual check for missing taxiway names), and parking spots. It starts fitted to the window; scroll the mouse wheel to zoom in/out around the cursor and hold the left mouse button to drag the view around. Taxiway/parking positions depend on `TAXI_POINT`/`TAXI_PARKING` bias-coordinate fields that are not yet confirmed against a live sim, so that part of the diagram may be empty or look wrong until verified — see `requirements.md`'s Known gaps for the Diagram epic.

## Build

```
dotnet build AirportSmith.slnx
```

## Test

```
dotnet test AirportSmith.slnx
```
