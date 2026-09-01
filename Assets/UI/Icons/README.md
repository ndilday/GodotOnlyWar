# Icon Atlas

`icon_atlas.png` is the UI icon atlas. It uses an 8 by 13 grid of 128px cells.
`icon_atlas_manifest.json` provides stable names and texture rectangles for each
cell.

The first pass is organized around the current UI needs:

- footer navigation: sector, chapter, apothecarium, reclusium, librarium,
  armamentarium, training unit, fleet, diplomacy, archive, end turn
- command actions: save, settings, menu, close, plot course, divide, merge,
  land squads, load squads
- map tools and map states: zoom, focus, layers, filter, route, warp lane,
  star, planet, controlled, allied, neutral, hostile
- squad and unit roles: HQ, scout, elite, tactical, assault, devastator,
  bodyguard, infantry, vehicle, ship
- status/rank: alert, request, threat, resource, construction, orbit, transit,
  warp, wounded, medical, training, locked, rank markers, award
- planet and region detail: imperial population, PDF/Astra Militarum forces,
  landed player forces, Tyranids, Genestealer Cult, Chaos
- Chapter Muster: tintable gun, sword, voice, and banner honors; historical
  lineage; create formation; fleet rebalance
- Recovery Operations: sort, recovery time, limb replacement, medical
  detachment, individual posting, and reunion

The approved full-resolution Chapter Muster masters live in
`Masters/ChapterMuster`. Run `build_chapter_muster_atlas.ps1` after changing one
of those masters to rebuild row 10 without moving existing atlas cells. The
script also accepts seven source paths, in manifest order, for the one-time
import of generated review images whose checkerboard background must be
converted to real transparency.

Approved Recovery Operations raster masters live in
`Masters/RecoveryOperations`. The same build script rebuilds row 11 from those
masters while preserving all established atlas coordinates.

Recommended next atlas expansion:

- skill icons: melee, ranged, leadership, medical, tech, piety, ancient
- equipment icons: melee weapon, ranged weapon, armor, ammunition, transport
- planet detail icons: garrison, governor, region, defense,
  rebellion, compliance
- mission icons: recon, ambush, sabotage, assassinate, assault, meeting
  engagement, exfiltrate
- ship class icons: escort, strike cruiser, battle barge, transport, lander

Runtime resolution is provided by `Helpers/UI/IconAssetRegistry.cs` and
`IconAtlas`. The registry accepts both standalone textures and atlas regions,
so callers only depend on a logical key. Core keys may remain unqualified;
content supplied by a mod must use its package namespace, for example
`iron_halo:award_duelist`.

Award families are rules data rather than UI code. A rules database can set an
award family's `IconAssetKey`, and the mod package can ship the corresponding
manifest and image/atlas beside its database:

```json
{
  "atlas": "icons/awards.png",
  "icons": {
    "award_duelist": { "x": 0, "y": 0, "w": 64, "h": 64 }
  }
}
```

The mod loader should register that manifest with
`IconAtlas.RegisterModIconManifest(manifestPath, "iron_halo")` before any
award-bearing view is built, and unregister it with
`IconAtlas.ClearModIconManifest("iron_halo")` when the package is unloaded.
Missing or unavailable content falls back to the generic `award` icon.

## Planetary Operations

Rows 12 and 13 contain the dark-brass Planetary Operations set:

- control: `control_contested`
- ordinary missions: `mission_recon`, `mission_defend`, `mission_patrol`,
  `mission_attack`, and `mission_diversion`
- fortifications: `fortification_entrenchment`,
  `fortification_listening_post`, and `fortification_anti_air`
- special missions: `mission_ambush`, `mission_sabotage`, and
  `mission_show_of_force`
- order state: `order_active`, `order_assigned`, and `order_unassigned`

The approved source sheet, transparent per-icon masters, and integrated review
strip live in `Masters/PlanetaryOperations`. Run
`build_planetary_operations_atlas.ps1` to regenerate those masters and the two
atlas rows while preserving the earlier 11 rows.
