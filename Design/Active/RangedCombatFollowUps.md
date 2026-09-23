# Ranged Combat Follow-ups

The implemented ranged-combat architecture is documented in TDD §6.6 and the player-facing behavior in PRD §4.14. This file tracks only work that remains intentionally unfinished.

## Template weapons

- Add krak grenades with the vehicle/anti-armor work. They are single-target anti-armor weapons, not blast templates. Tracked as part of Vehicles in PRD §5.7.
- Add Space Marine grenade launchers and a dedicated launcher skill if they become player equipment.
- Apply cover, terrain, and line-of-sight interaction to cones and blasts when Battle Visuals Phase 3 supplies those systems.

## Conventional ranged fire

- Add general line-of-fire tracing through friendly formations. The current friendly-fire model applies to melee scrums; it does not model allies standing anywhere along a shot's path. Still open — there is no line-of-fire or line-of-sight tracing anywhere in `Helpers/`.
- Pair fire-lane tracing with formation behavior that avoids blocking friendly weapons. Note that lane *spread* already exists (`LaneSpreadPenaltyFraction` biases target selection so a squad spreads its fire across the squad it is shooting at); that is a targeting bias, not occlusion, and does not satisfy this item.

## Validation

Future changes must preserve planning/resolution parity, deterministic battle replay, friendly-fire attribution, and the shared Battle Value currency used to compare ranged, melee, and movement choices.

## Pursuit limitations

- Keep the calibrated squad-level `BattleSquadCapabilityProfile.UsefulFireRange` definition stable
  while auditing other consumers. Follow posture and unpursued escape now use concrete-pair
  `BattleAttackOpportunityProjection` results from the live ranged evaluator; the deterministic
  `sampled_live_ranged_evaluator` boundary is a named destination approximation, not an executable
  shot. The rear-guard force forecast remains explicitly approximate and separate.
- Replace the preparation window's centroid separation delta with exact projected target cells once
  obstacle/path projection exists. It currently applies the same one-turn quarry displacement to
  each candidate target.
- Revisit the generic 0.05 engagement indifference floor if complete scenarios show ineffective
  long-range fire repeatedly holding: the focused baseline's advance advantage is only 0.040 BV and
  is intentionally swallowed by that floor. Do not special-case pursuit without broader evidence.
- Add a complete resolver fixture that begins before morale withdrawal and uses a deliberately
  ranged-effective pursuing loadout. The current scale diagnostics begin at the pursuit transition,
  so casualties before withdrawal are not measurable there, and their default marine loadout ends
  the chase in melee.
