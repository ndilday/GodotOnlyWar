# Region Detail Visual Baseline

**Status:** Implemented and canonical as of Alpha 0.7.

The live Region Detail screen uses the selected operations-board layout: assignable forces on the
left, a compact seven-region target picker above the mission board, commit controls below it, and a
selected-target dossier on the right. The implementation in `Scenes/RegionScreen/region_screen.tscn`
and its controller/view is authoritative.

The earlier alternative mission-intel, operations-board, theater-dossier, and map-center mockups were
removed after this layout shipped. The compact operations-board composition is the selected direction.
