# Planet Detail Visual Baseline

**Status:** Implemented and canonical as of Alpha 0.7.

The live Planet Detail screen is a command-workspace hybrid: roster and filters on the left, a
layered regional map in the center, contextual dossier cards on the right, and selection-aware
Open Region, Land, and Embark commands. The implementation in
`Scenes/PlanetDetailScreen/planet_tactical_screen.tscn` and its controller/view is authoritative.

The earlier dossier-first, region-command, and orbital-logistics mockups were inputs to this hybrid,
not separate screen modes. They were removed after the live workspace superseded them.
