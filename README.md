# AirportSmith

A Windows desktop editor for producing customized versions of default MSFS 2024 airports — edit a stock airport's runways, taxiways, and parking, then package the result as a Community-folder add-on that overrides the sim's built-in version at that ICAO.

**Status: planning phase.** No airport-reading, editing, or packaging functionality exists yet. See [`requirements.md`](requirements.md) for the researched constraints this project is built on and the proposed v0.1 scope, and [`CLAUDE.md`](CLAUDE.md) for the domain research summary and project conventions.

## Build

```
dotnet build AirportSmith.slnx
```

## Test

```
dotnet test AirportSmith.slnx
```
