# Icon Atlas

`icon_atlas.svg` is the editable source for the first UI icon atlas. It uses an
8 by 8 grid of 64px cells. `icon_atlas_manifest.json` provides stable names and
texture rectangles for each cell.

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

Recommended next atlas expansion:

- skill icons: melee, ranged, leadership, medical, tech, piety, ancient
- equipment icons: melee weapon, ranged weapon, armor, ammunition, transport
- planet detail icons: garrison, governor, region, defense,
  rebellion, compliance
- mission icons: recon, ambush, sabotage, assassinate, assault, meeting
  engagement, exfiltrate
- ship class icons: escort, strike cruiser, battle barge, transport, lander

Implementation note: once the SVG import looks good in Godot, create either
named `AtlasTexture` resources for common icons or a small helper that reads the
manifest and returns an `AtlasTexture` by icon key.
