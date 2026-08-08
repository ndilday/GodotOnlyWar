# Large-Scale NPC Combat

**Status:** Implemented (July 2026). Retained as the one coherent statement of the strategic combat
model — the equations, the aggression and quality tables, and the open balance questions.
Architecture and integration are in `OnlyWar_TDD.md` §6.2; player-facing behaviour in
`OnlyWar_PRD.md`.

**Corrected against the code 2026-08-08.** Three formulas in the July draft had drifted and are fixed
below: the defender no longer has a readiness term, the flat defender detection multiplier is gone,
and entrenchment reads the side-wide shared position. The superseded forms are recorded at the end so
the change is not silently re-introduced.

Resolves large NPC-on-NPC regional combat without generating thousands of temporary squads. Tactical
battles remain the path for named player squads and small encounters; regional forces still lose
strength, seize ground, and feed downstream systems (biomass consumption, revolts, scenario checks,
governor requests) out of the existing `RegionFaction` pools rather than a parallel army model.

## Resolution choice

Classified before any transient squads are generated. **Always tactical** when any assigned squad is
the player's, the target region holds landed player squads, either side includes persistent named
roster soldiers, or the mission is not an army-scale `Advance`. **Strategic** only when the order is
NPC-only, the mission is an `Advance`, and committed attacker + defender strength clears the mass
floor or the estimated actor count would exceed the tactical cap.

| constant | value | note |
|---|---:|---|
| `MassCombatBattleValueFloor` | 1,500 BV | Deliberately low relative to regional armies — a region with thousands of combatants is already a strategic battle, and the tactical engine should only ever see squad-scale contact. |
| `MaxTacticalActors` | 120 | Defensive guard against future data tuning, not a balance lever. |
| `MaxGeneratedSquads` | 24 | Same. |

## Strength currency

Battle value throughout. `RegionFaction.MilitaryStrength` already resolves the horde-vs-civilian
split — Tyranids, cults and Orks lose from `Population`; Imperial/PDF-style factions lose from
`Garrison`, and the fallen also leave `Population` because garrison is a sub-pool of it. Reading it
rather than `Garrison` is what stops population-is-military defenders being undercounted, which the
old tactical-planning assumption got wrong. Landed non-player squads add their members' template
battle values.

Only the **organized** pool deploys and takes ordinary battle casualties
(`OrganizedMilitaryStrength`; see TDD §6.2). Losses are distributed across defending factions in
proportion to organized strength, largest first, with the rounding residue absorbed by the largest
holder so strength is conserved.

## Effective strength

```text
attackerEffective = committedBattleValue
                  * FactionQuality(attacker)
                  * AggressionStrengthMultiplier(aggression)
                  * AmbushSurpriseMultiplier(attackerIntel, defenderIntel)

defenderEffective = engagedDefenderBattleValue
                  * FactionQuality(defender)
                  * EntrenchmentMultiplier(sharedEntrenchment)

EntrenchmentMultiplier(e)  = min(3.0, 1 + 0.10·e)
AmbushSurpriseMultiplier() = 1 + min(0.50, max(0, attackerIntel − defenderIntel) · 0.10)
```

`sharedEntrenchment` is `RegionDefenses.GetShared(...)`: a defender fights from the whole position its
**side** holds in the region, not merely the stretch of trench its own faction dug.

**Surprise is an attacker-side term, deliberately.** A defender's awareness does not make it
intrinsically stronger — it only denies the attacker surprise. That is what prices a cult rising from
within a blind PDF region, and it decays as the defender builds awareness through listening posts,
patrols and recon. A defender that survives an assault also gains
`IntelGainedFromBeingAttacked = 2.0` awareness of every region the attack staged from, purely from
being hit: the blow reveals where the enemy is massing, so a blind neighbour can be garrisoned next
turn with no deliberate recon.

| Aggression | Strength × | Casualty × |
|---|---:|---:|
| Avoid | 0.60 | 0.50 |
| Cautious | 0.80 | 0.75 |
| Normal | 1.00 | 1.00 |
| Attritional | 1.15 | 1.25 |
| Aggressive | 1.30 | 1.50 |

`FactionQuality` is keyed off growth type rather than faction identity, so a new faction inherits a
sane default: `Consumption` (Tyranids) 1.15, `Conversion` (Genestealer Cults) 0.85, default/PDF and
everything else 1.0. Orks, when added, are intended at ~1.10. Starting points, not final balance;
these should eventually live in rules data or `FactionBehavior`.

## Randomness

```text
sideRoll = sideEffective · exp(z · CombatSigma),  CombatSigma = 0.12
```

A small log-normal spread, drawn from the campaign RNG stream — never `GD.Rand*`. The stronger side
usually wins without the result being a pure function of the ratio.

## Casualty math

```text
intensity        = BaseIntensity · AggressionCasualtyMultiplier      BaseIntensity = 0.08
attackerPressure = attackerEffective / max(defenderEffective, 1)
defenderPressure = defenderEffective / max(attackerEffective, 1)

attackerLossRate = clamp(intensity · defenderPressure^0.65,                        0.01, 0.60)
defenderLossRate = clamp(intensity · attackerPressure^0.65 · DefenderProtection(e), 0.01, 0.75)

DefenderProtection(e) = max(1 / (1 + 0.08·e), 0.35)

attackerLosses = round(committedBattleValue        · attackerLossRate)
defenderLosses = round(engagedDefenderBattleValue · defenderLossRate)
```

Both clamped to available strength. A nonzero committed force whose loss rounds to zero takes a
minimum of 1 BV, but only when the opposing effective strength is also nonzero. Entrenchment appears
twice on purpose and in opposite directions — it raises defender effective strength *and* lowers the
defender's loss rate — because a prepared position both fights better and absorbs better.

This is a campaign-week attrition model tuned for readable outcomes: strong forces hurt weak ones
badly, defenders benefit from preparation, and very large battles take several turns instead of
deleting a region instantly.

## Outcome

Attacker takes the region when `attackerRoll > defenderRoll × CaptureThreshold`, with
`CaptureThreshold = 1.10`. Then, in order: no survivors → `AttackerDestroyed`; win and
`InvadesOnVictory` → survivors establish a foothold, `InvaderFoothold`; win without it → survivors
return to staging, `Raided` (a raid, not a conquest); otherwise `DefenderHeld`. Survivors return to
multiple staging regions proportionally to their original contributions, residue to the largest
contributor.

**Control changes are narrower than casualties.** Strategic combat opens a region and kills organized
defenders; it does not delete civilian populations. When an invader wins and the defender's military
strength reaches zero, the invader becomes public there, and a civilian-base defender with population
but no garrison becomes **non-public** rather than removed — overrun civilians and remnants, not
organized control. Tyranid predation and consumption stay separate turn steps: strategic combat opens
the region, biomass processing handles what follows.

## Open tuning questions

Live balance work, unanswered.

- Is `MassCombatBattleValueFloor = 1500` too low for factions whose templates make very elite,
  low-actor forces?
- Should base weekly intensity be lower for world-scale wars, so invasions take more turns?
- Should Imperial Guard, once modeled separately from PDF, get a higher quality value *and* a
  counterattack behaviour, rather than the current defensive-only PDF posture?
- Should air superiority, void support, and fleet blockade become modifiers here once those systems
  exist?

## Superseded forms

Recorded so they are not reintroduced from the July draft.

| was | now | why |
|---|---|---|
| `defenderReadiness = 0.35 + 0.65 · Organization/100` as a multiplier on defender strength | *removed* | Organization now enters structurally through the organized/disorganized BV partition, which is a conserved pool rather than a scaling factor. Keeping both would discount readiness twice. |
| `detectionMultiplier = min(1.5, 1 + 0.02 · Detection)` on the defender | `AmbushSurpriseMultiplier` on the attacker | Awareness should deny surprise, not confer strength. The old term made a well-surveilled defender intrinsically tougher even against an enemy it had never seen. |
| `defender.Entrenchment` | `RegionDefenses.GetShared(defender, Entrenchment)` | A faction fights from its side's whole prepared position, not only its own contribution to it. |
