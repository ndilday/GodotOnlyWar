# Ranged Combat Follow-ups

The implemented ranged-combat architecture is documented in TDD §6.6 and the player-facing behavior in PRD §4.14. This file tracks only work that remains intentionally unfinished.

## Template weapons

- Add krak grenades with the vehicle/anti-armor work. They are single-target anti-armor weapons, not blast templates. Tracked as part of Vehicles in PRD §5.7.
- Add Space Marine grenade launchers and a dedicated launcher skill if they become player equipment.
- Apply cover, terrain, and line-of-sight interaction to cones and blasts when Battle Visuals Phase 3 supplies those systems.

## Conventional ranged fire

- Add general line-of-fire tracing through friendly formations. The current friendly-fire model applies to melee scrums; it does not model allies standing anywhere along a shot's path. Still open — there is no line-of-fire or line-of-sight tracing anywhere in `Helpers/`.
- Pair fire-lane tracing with formation behavior that avoids blocking friendly weapons. Note that lane *spread* already exists (`LaneSpreadPenalty` biases target selection so squads do not stack their fire into one corridor); that is a targeting bias, not occlusion, and does not satisfy this item.

## Validation

Future changes must preserve planning/resolution parity, deterministic battle replay, friendly-fire attribution, and the shared Battle Value currency used to compare ranged, melee, and movement choices.
