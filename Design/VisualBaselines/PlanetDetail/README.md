# Planet Detail Visual Baseline

**Status:** Historical baseline; superseded by Planetary Operations.

The live Planet Detail screen is a command-workspace hybrid: roster and filters on the left, a
layered regional map in the center, contextual dossier cards on the right, and selection-aware
Open Region, Land, and Embark commands. World-level data now lives in
`Scenes/MainGameScreen/SystemInspector`, with regional command in
`Scenes/PlanetaryOperationsScreen/planetary_operations_screen.tscn`.

The earlier dossier-first, region-command, and orbital-logistics mockups were inputs to this hybrid,
not separate screen modes. They were removed after the live workspace superseded them.
