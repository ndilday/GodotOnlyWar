# Battle Screen Raster Mockups

**Status:** Selected and implemented as the Alpha 0.7 Battle Review baseline. Retained for deferred replay-motion and battlefield-visual work.

Full-raster UI overhaul concepts for the battle screen, based on the current Sector Map and Chapter screen visual direction: dark panels, antique-gold trim, compact roster/report cards, smoky glass, and dense strategy-game presentation.

The first pass explored a tactical-command interpretation. That proved useful visually, but it implied direct player control during battle. The second pass is the better fit: the battle screen should display what already happened, closer to a Dominions-style battle replay/report, and must scale to sizable battles with many units.

## Notes

- These are raster ideation mockups, not SVG assets or implementation files.
- In-image text is concept-level and should be treated as placeholder copy.
- Avoid bottom command menus, large action buttons, and player-order affordances on the battle screen.
- Avoid large blue/red territory-control overlays; use small banners, pips, outlines, and labels to distinguish forces.
- The strongest production candidate is now V3 01: Battle Chronicle's playback/event structure combined with Formation Tableau's scalable force hierarchy and selected formation summary.

## V3 Direction - Preferred Hybrid

### V3 01 - Chronicle Formation Hybrid

File: `battle_screen_mockup_v3_01_chronicle_formation_hybrid.png`

Current preferred layout. It combines:

- Left force hierarchy tree from Formation Tableau.
- Top-center turn playback controls from Battle Chronicle.
- Central battlefield replay viewport with formation markers and event callouts.
- Right column split into selected formation summary at the top and event chronicle below.
- Bottom battle timeline and casualties-by-round table, kept informational rather than command-oriented.

This is the best fit so far for an automated battle display with sizable unit counts.
