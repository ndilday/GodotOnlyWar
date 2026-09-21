# Battle Logic Reference

**Status: implemented reference record (2026-08-11).** This is the durable design record for the
shipped battle systems. It consolidates the tactical engagement, ranged/melee, casualty, morale,
withdrawal, pursuit, and army-scale NPC-combat decisions that were previously spread across the
engagement-scoring plans and the former large-scale NPC-combat record.

The architectural source of truth remains `OnlyWar_TDD.md` §6.6 (tactical battles), §6.2
(strategic NPC combat), and §6.6.1 (medical and gene-seed). Player-facing behavior remains in
`OnlyWar_PRD.md` §4.14. This file retains the equations, invariants, and rejected alternatives
that are useful when changing those systems without replaying the entire design history.

The following remain deliberately separate because they describe adjacent systems rather than the
battle engine itself:

- `CasualtyRealism.md` contains the detailed wound-band, healing, and Apothecary decision tables.
- `SpecialistAttachment.md` contains the historical order-level personnel-attachment decisions and
  save/load rationale; current posting architecture is in `OnlyWar_TDD.md` §5.3, §5.6, and §6.10.
- `../Active/RangedCombatFollowUps.md` is the live backlog for terrain/line-of-fire, krak grenades,
  launcher expansion, and other intentionally unshipped ranged work.

## 1. System boundary and turn lifecycle

Named/player battles and small encounters use the tactical engine. NPC-only army-scale `Advance`
orders use the strategic resolver when the mass-combat floor or tactical actor/squad caps are
exceeded. Player participation, landed player squads, persistent named roster soldiers, or a
non-`Advance` mission keeps the encounter tactical.

The tactical runtime is divided into explicit state and policy layers:

| Component | Responsibility |
|---|---|
| `BattleGridManager` | Sparse 2D occupancy, footprint placement, movement, reservations, and spatial queries |
| `BattleSoldier` / `BattleSquad` | Per-soldier and per-squad live battle state |
| `BattleEngagementFrameBuilder` | Frozen capability profiles, pairings, roles, baselines, and screen assignments |
| `BattleSquadPlanner` | Legal posture selection and executable root-turn action policies |
| `BattleTurnResolver` | Planning barriers, action segments, wound resolution, morale, continuation, and completion |
| `BattleHistory` | Cloned state snapshots, executed actions, events, casualties, and typed outcome |

Each tactical turn is resolved as follows:

1. Clear reservations, advance the battle turn, and snapshot the turn-start morale state.
2. Materialize lazy roster/equipment aggregates, build both sides' paired engagement frame, and
   freeze the shared planning context.
3. Evaluate each `(side, squad)` job against the same frozen state. Jobs may run concurrently;
   they consume no RNG and do not mutate live battle state.
4. Reveal all squad postures in deterministic side/squad order. Only after that declaration
   barrier are the selected root soldier actions materialized in deterministic order.
5. Execute shooting, then movement, then melee. Shooting therefore uses turn-start positions;
   melee occurs after movement. Wounds are queued and resolved after all three segments.
6. Remove deaths and settle incapacitation, record metrics, resolve withdrawal/contact breaks,
   evaluate morale, and then evaluate voluntary continuation/rear-guard state.
7. Append the cloned post-turn state, actions, events, and casualties to `BattleHistory`. The
   battle ends on an empty side, a contact/disengagement outcome, or a defensive safety cap for an
   inert/runaway battle.

`BattleTurnResolver` uses a 1,000-turn hard cap and declares an inert battle after 100 consecutive
turns with neither a casualty nor meaningful separation change. These are safety outcomes, not
balance rules.

## 2. Tactical state and movement

Each soldier occupies a footprint determined by species dimensions. A squad posture selects the
movement restrictions for the turn; weapon, target, aim/reload, template, and point-blank choices
remain per soldier. The current tiers and their canonical speeds are:

| Tier | Speed used for ranged defense | Ranged behavior | Other behavior |
|---|---:|---|---|
| `Stationary` | `0` | Aim fully; fire without `Bulk` penalty | Melee is legal; clears banked movement |
| `Walk` | `0.2 × MoveSpeed` | Aim retained at half effect; fire with half `Bulk` penalty | Unrestricted firing direction |
| `Jog` | `0.5 × MoveSpeed` | No aim bonus; full `Bulk` penalty; target must be within the forward 90° arc | Melee is legal |
| `Run` | `MoveSpeed` | No ranged attack, including templates and grenades | Movement or a new charge only; turn is limited to one facing step |
| `InMelee` | Per soldier | Adjacent soldiers use legal melee/point-blank actions; separated soldiers close | The squad may contain both stationary fighters and closing members |

Movement allowance is tier speed plus the soldier's unbounded `LeftoverMovement` bank. Actual
Euclidean displacement is subtracted; unused distance survives a shortened or blocked move.
`Stationary` clears the bank. A declared tier still supplies its planned `CurrentSpeed` for ranged
defense even if a footprint cannot realize the full displacement.

Walk halves the entire aim-derived accuracy bonus rather than discarding stored aim. Jog and Run
clear aim. Reloading and readying remain legal while moving. A Run charge is available only when a
soldier was not already adjacent at turn start; it uses the existing moved-attack accuracy penalty
and forfeits weapon parry for that turn. Running itself supplies the ranged-defense benefit, so
charging has no extra defensive penalty.

The `Stance` enum and hit-location maps exist as data/runtime vocabulary, but voluntary
Standing/Crouching/Prone behavior is not shipped. Every production soldier remains Standing; true
stance and prone combat are deferred until terrain, cover, and line of sight make the exposure trade
meaningful. Involuntary incapacity is handled by the wound/casualty system instead.

## 3. Resolution rules

### 3.1 Equipment and hands

`BattleSquad.AllocateEquipment` assigns weapons from the squad loadout. One-handed weapons can
share the two physical hand groups; two-handed weapons require both groups. A weapon becomes
unusable when any group that grips it is disabled, and a two-handed weapon is dropped when either
group fails. Unarmed combat uses the species-selected default unarmed weapon and that weapon's
related skill; it is not a player/NPC code fallback.

### 3.2 Conventional ranged fire

The live ranged model is used by both resolution and planning. At range `r`, the range modifier is

```text
2.4663 × ln(2 / (r + movement-speed contribution))
```

and the weapon's effective accuracy combines weapon accuracy, the related skill, size/evasion,
movement `Bulk`, aim, and firing-into-melee modifiers. Damage strength falls linearly with range
for weapons whose `DoesDamageDegradeWithRange` flag is set. Armor, hit location, wound multiplier,
and the Gaussian damage roll are then resolved by the normal wound pipeline.

The planning quantity is expected take-out value, not raw accuracy or wound ratio:

```text
ranged removal = hit probability × take-out probability × target Battle Value
```

`CalculateTakeOutProbabilityOnHit` mirrors the resolver: stance-weighted hit-location choice,
armor, natural armor, the wound ladder against each location's existing wounds and
`CrippleWound`/`SeverWound` thresholds, motive/vital restrictions, and the last-functioning-hand
rule. The real `N(3.5, 1.75)` damage roll is integrated rather than replaced with a clamped linear
wound fraction. This makes concentration and finishing damaged targets emerge from the threshold
model.

Ranged removal is not discounted because a target is far away or withdrawing. Distance already
changes the shot through range and take-out probability; arrival discount is reserved for effects
that genuinely occur only after contact, such as a charge.

Shooting into a melee scrum applies `RangedFriendlyFireRules.FiringIntoMeleePenalty`. A near miss in
the narrow configured band can resolve a full-strength stray hit against a footprint-size-weighted
participant, including a friendly soldier and, at point blank, the shooter. The actual victim and
friendly-fire flag are recorded for aftermath and replay. General line-of-fire tracing through
formations is not yet part of the engine.

### 3.3 Template weapons and grenades

`RangedWeaponTemplate.TemplateType` selects ordinary fire, cone, launched blast, or thrown blast.
Template geometry, victims, scatter, and wounds are resolved once; replay reuses the stored result
and does not consume new randomness.

- A cone extends to the weapon's full range along the selected direction. It auto-hits every
  occupied footprint in the cone except the shooter, friend or foe, with normal armor and wound
  resolution. Size, evasion, and aim do not alter the hit because there is no hit roll.
- A blast targets an impact cell. A failed normal-curve delivery check scatters by failure margin
  in a pre-resolved random direction. Every footprint inside the radius is hit, including the
  thrower. Damage falls quadratically from the impact center to zero at the rim.
- Thrown range is `Strength × MaximumRange`; launched range uses the weapon's `MaximumRange`.
  Grenades occupy a third ranged slot and use ordinary ready/reload action economy.
- Blast scoring integrates enemy benefit and friendly/self cost across the same scatter nodes and
  victim damage roll. It does not multiply a perfect-impact estimate by delivery confidence.
  A throw must beat the soldier's best conventional action; ties go to the gun, and a melee-engaged
  soldier never throws.
- Grenades are a sidearm in Battle Value: per threat profile, the calculator uses
  `max(primary ranged rate, grenade rate)`, never both in the same turn.
- Flamers do not create an on-fire condition, damage over time, action-economy panic, or
  fire-specific morale shock. Their distinctiveness is the immediate indiscriminate cone; morale
  is deliberately priced from outcomes rather than weapon flavor.

### 3.4 Melee

The contested melee roll uses attacker skill, defender skill, species melee evasion, weapon
accuracy, and defender parry. The current calibration uses equal-skill parity (`MeleeDefenderAdvantage
= 0`) and per-side roll standard deviation `6`, producing a compressed tabletop-like intuition band.

Attacks per melee action are:

```text
AttackSpeed / 10 × weapon.AttackSpeedMultiplier
```

The fractional attack is resolved probabilistically. A second one-handed melee weapon grants one
off-hand strike with its own profile; defense receives only the sum of equipped weapons'
`ParryModifier`s. There is no flat dual-wield defense bonus. A charge loses parry for the turn.
`BuildStrikePlan` keeps striking one adjacent target until cumulative take-out confidence reaches
75%, then distributes remaining attacks to other valid contacts.

An engaged soldier compares the projected melee sequence with a point-blank ranged action in the
same Battle Value currency. The ranged alternative pays firing-into-melee, weapon `Bulk`, and the
expected self-value of forfeiting parry against adjacent attackers.

### 3.5 Wounds and battle casualties

Wounds are live body state, not a battle-only damage counter. The resolver queues each result and
settles it after the action segments. Incapacitation persists as a named casualty state; death
removes the soldier from the active battle. `CanFight && CanMove` is the production definition of
combat effectiveness used by planning, targeting, morale, deployment, and medical systems.

Graded motive impairment replaces the former binary leg-fall rule. Detailed wound-band thresholds,
healing cadence, Apothecary care, and gene-seed aftermath remain in `CasualtyRealism.md` and TDD
§6.6.1. Battle history preserves the information needed to distinguish dead, incapacitated, and
returned wounded outcomes.

## 4. Battle Value and capability profiles

Battle Value is the shared currency for force sizing, ranged/melee comparison, movement choices,
screening, continuation, and strategic strength. `BattleValueCalculator` replays engine-faithful
to-hit, damage, reload, melee, closing, and survival math against a weighted reference panel:
swarm chaff, light infantry, elite infantry, and a monster.

```text
BV = 5 × sqrt(expected offense × expected durability) × command multiplier
```

The persisted `SoldierTemplate.BattleValue` is a template guideline, not a live skill-tracking
rating. The PDF trooper anchor is 5; current strategic anchors include Tactical Marine 9,
Genestealer 13, and Melee Carnifex 30. A soldier's `MeleeFraction` is generated beside BV and feeds
doctrine/capability profiles, while current loadout, ammunition, functioning hands, wounds, and
movement capacity determine the runtime profile.

`BattleSquadCapabilityProfile` summarizes the able roster, usable weapons and ammo, functioning
grips, range/removal curves, movement, footprint/contact capacity, and melee/ranged mode. A lost
heavy weapon or disabled hand changes the profile immediately; authored `MeleeFraction` does not
override current capability.

## 5. Squad engagement planning

### 5.1 Frame, doctrine, and legal options

`BattleEngagementFrameBuilder` constructs both sides' frames from one frozen state. It derives
pairwise geometry and allocation weights, a primary counterpart, a deterministic baseline posture,
force-role masks, withdrawal/pursuit data, and capacity-limited screen assignments.

The normal candidate set is semantic rather than a raw movement enum: `Hold`, `StepBack`,
`StepForward`, `JogToward`, `RunToward`, `CloseToContact`, and, where assigned, `MoveToInterpose`.
`Bound`, `Cover`, `RearGuard`, `Routing`, `Pursuit`, `Standoff`, and `BreakOff` roles mask this set
before scoring. A contact-seeking squad with no ranged answer worth preserving cannot hold or give
ground beyond contact; a ranged squad outside its own useful band cannot pretend that a close charge
is its normal opening; and a pursuit standoff cannot run without a meaningful speed advantage.

The baseline answers "am I roughly in the fight" from reach, not from the sharply derived
effective engagement range — feeding the derivation into the fallback would make the fallback carry
the judgement the score is meant to make. A contact-seeker closes unless it is already in contact;
a squad inside its band holds; one that has closed past the near edge steps back; and one beyond
its own reach takes the fast approach if it can run. That last case was a jog until 2026-09-20, and
it only ever shows when the scored options sit inside the indifference band — which is exactly the
far-away case, where every option is worth about the same. Against a quarry of equal speed a jog
does not merely close more slowly; it does not close at all.

The baseline and previous option are tie-breakers inside the small indifference band, not additive
Battle Value. A previous posture cannot win when another legal option is materially better. Absolute
destinations are not semantic identity and are never used for cross-turn hysteresis.

### 5.2 The score

The scored object is an executable root-turn policy. Every candidate carries the legal ordered
`PlannedSoldierAction` descriptors that will actually be materialized if it wins. Planning may not
score an aim/fire policy and then independently substitute an illegal action after movement is
declared.

The current transition shape is:

```text
score(s -> s') = ImmediateExchange(s -> s')
                 + Phi(s') - Phi(s)
                 - contact commitment

ImmediateExchange = enemy removal - friendly fire - incoming fire + melee now
```

`Phi` is `EngagementPotential`, evaluated without an option kind. It contains state-derived
`NetRateValue`, readiness, role, fire-window, morale, command, and contribution-access terms.
`EngagementPotentialDiscount` is `1`; the separate `0.65` rollout discount is not used to discount
the battle-duration potential.

Positive outgoing opportunity is saturated against each enemy Battle Value pool and all incoming
opportunity is aggregated and saturated once against the friendly pool:

```text
finitePool(x, B) = B × (1 - exp(-x / B))
```

This prevents a per-turn removal rate from billing the same target pool indefinitely while
preserving a continuous positional gradient. Contribution access separately applies a continuous
tempo cost:

```text
access           = -tempoRate × turnsToUsefulRange
tempoRate        = scale × helplessness × viability
scale            = targetBattleValue / AccessValueTurns
helplessness     = negligibleRate / (negligibleRate + currentRate)
negligibleRate   = targetBattleValue × NegligibleRemovalFraction
viability        = destinationRate / (scale + destinationRate)
turnsToUsefulRange = (range - desiredRange) / (ownMoveSpeed - quarrySpeed)
```

The tempo rate fades as current contribution becomes useful and is zero when the destination cannot
produce removal. There is no activation branch at the old half-pool/four-pool boundary. `T` is
derived once per planning turn from Battle Value at risk and current removal rate, capped by the
implementation's maximum exchange horizon, and is shared by every candidate on both sides.

Two properties of that decomposition are load-bearing and were both wrong until 2026-09-20.

**Helplessness is measured against the plinking floor, not against `scale`.** `scale` is a tempo
magnitude — the rate that would clear the enemy squad in `AccessValueTurns` — and no real squad
achieves it, so using it as the reference for "is my current fire useful" pins the factor near 1
for a squad that is shooting perfectly well. The reference is the negligible-removal rate, the
same floor `RangedEffectivenessCurve` uses to decide a curve is worth nothing; that is a removal
rate, so it is dimensionally the same kind of quantity as the current rate, and a squad an order
of magnitude above the floor is priced as barely helpless. It is deliberately **not** a fraction
of what the squad could achieve by moving: a squad at a small share of its best rate is not
helpless, only suboptimally placed, and that loss is already carried by the net-rate integral.
Keying access to the destination rate would double-count it.

**The delay is priced against the net closing rate.** A chase closes at the difference of the two
speeds. Dividing the gap by the pursuer's own move prices a full stride against a stationary
target and then re-prices the shortened gap the same way next turn, so the same tempo bonus is
paid every turn for an arrival that keeps receding. A quarry the squad cannot out-run yields no
access value at all: the arrival never happens, so there is no delay to buy out, and the squad is
scored on the fire it can deliver where it stands. Only a pursuit frame carries a quarry speed.

Both errors were found together in the Grist Nine Epsilon battle of 2026-09-20, where 315 marines
pursued 20 routing orks for the last 700 turns of a 1000-turn battle without firing. The access
delta held near 10 Battle Value per turn while the separation fell from 323 to 104 — a stock that
should have been depleting did not move — and it outbid a standing shot worth ~3 on 1112 of 1112
squad-turns where one was available. Deleting the term alone would have reversed 997 of them.

**Every `Phi` component is a function of state, never of the option that produced it.** The
pursuit contact-progress term broke this and was the larger half of the same failure. It used to
pay `attainable` — the squad's whole usable ranged or melee Battle Value — scaled by the fraction
of its top speed the candidate actually used. Because the root state is always evaluated at zero
speed, the root scored zero and every moving candidate scored the entire bounty, so
`gamma*Phi(s') - Phi(s)` never telescoped and became a per-turn payment that could not deplete. In
the second Grist Nine Epsilon run it read **exactly 141.150** for a running squad in two windows
seventy turns apart while the separation closed, against a standing shot worth 7.4. Neither term
alone decided those turns; removing both reversed all 115 of them.

It is now a position value that saturates as the squad closes:

```text
contactProgress = attainable × span / (span + max(0, projectedDistance - desiredRange))
```

The whole approach is worth `attainable` once, rather than `attainable` every turn, and the
saturating form has no flat region, so there is a gradient toward the quarry at any distance. The
quarry's speed is deliberately absent: a positional potential answers "how good is standing here",
while how long the ground takes to cover is a question about time, priced by the access term and
by the pursuit closing value. Splitting them keeps each term telescoping on its own.

Two consequences are deliberate. The marginal value of a yard RISES as a squad approaches its
band, rather than tapering; the taper the older form showed was the per-turn bounty, not a
gradient. And because the total is bounded, the per-turn delta at long range is small — smaller
than the squad-scoring indifference band. Position value is a tie-breaker and a gradient where the
exchange integral is flat; it is not meant to outvote real fire, and the exchange terms decide
whenever a squad has a shot worth taking.

A squad beyond its own reach therefore falls through to the baseline posture, whose derivation is
in §5.1.

**One known exception remains.** The pursuit closing value still reads the candidate's speed, to
penalise a half-hearted chase by a pursuer slower than its quarry. It is bounded by the squad's
Battle Value, fires only in that case, and its sign pushes toward standing still rather than
toward moving, so it does not compound the way the two terms above did. It is nevertheless not a
state function, and whether "am I keeping up" belongs in `Phi` at all — rather than in the
immediate exchange or the legal-option mask — is open. The speed-invariance regression test is
scoped around it deliberately and says so.

The value model intentionally keeps doctrine in the legal-option mask and value in `Phi`. A large
negative future value cannot make an able melee-only contact-seeker retreat by outvoting its doctrine.

Options whose scores differ by less than an indifference band are treated as equivalent, and the
baseline posture, then the previous turn's choice, break the tie. **That band is a fraction of the
best score, not of the squad's Battle Value.** Battle Value is a stock and these scores are
per-turn rates, so sizing the band from the stock made it unescapable for any squad whose
shooting was worth less than that fraction of its own worth per turn: every option read as
indifferent and the tie-break decided in place of the score. A 191-value marine squad carried a
3.82 band while its whole shooting was worth 1.3 a turn. An absolute floor still applies, for the
case where every option really is worth about nothing — which is when the baseline and the
previous posture should carry the decision.

This was the last of the three defects behind the Grist Nine Epsilon turn-cap battles, and the
only one of them that was not itself a scoring error. It stayed invisible while the pursuit
shaping terms paid out more than a hundred Battle Value per turn, because that put the moving
options far outside any band; correcting those terms is what exposed it.

### 5.3 Range and removal models

There is one engagement-range model: `RangedEffectivenessCurve`, a smooth expected Battle Value
removal rate over range. It is used for force-level useful reach, opening-range derivation, and
mid-fight standoff:

```text
standoff = argmax_r [ outgoing(r) - incoming(r) - meleeThreat(r) ]
```

The melee threat is discounted by time to contact because melee payoff is genuinely delayed. A
non-degrading weapon against a penetrable target normally prefers closer range until return fire
sets the standoff. A target with no meaningful removable rate buys no standoff; the negligible-rate
floor prevents a Gaussian damage tail from making an effectively impenetrable target look worth
plinking.

The curve is sampled globally and refined, rather than assuming the objective is unimodal. The
effective band and opening band answer different questions: mid-fight standoff includes incoming
fire and melee threat; opening range asks where the force is still effective before that exchange
exists. Pursuit uses useful reach rather than the normal standoff band because a quarry's return
fire is not the pursuit objective.

The graded removal model uses the shared `WoundProgressCreditWeight` (`lambda`, currently 0.5) to
give partial credit for moving a location toward a wound threshold while keeping take-out as the
dominant event. `RangedEffectivenessCurve`'s saturation and negligible-removal floors are named
calibration seams in code, not rules-data facts.

### 5.4 Planning and execution invariants

- Planning and resolution use the same hit, damage, template, melee, and friendly-fire semantics.
- A planner cannot select a target whose expected removal is non-positive.
- Planning is deterministic: workers use no RNG; proposal order, declaration, action materialization,
  and grid reservations are stable by side/squad/soldier order.
- The root candidate is exact and executable. Bounded continuation uses capability aggregates and
  cached pair tables; it does not call per-soldier targeting recursively.
- The declaration barrier reveals every squad's feasible speed before any current-turn shooting is
  materialized, so planning order cannot give one side a stale defensive-speed view.
- `ENGAGE_EVAL` records candidate term breakdowns and the winning margin; `SCREEN_EVAL` records
  screen capacity and counterfactual loss. Cross-turn semantic fields are cloned in snapshots.

## 6. Morale, withdrawal, and pursuit

After each round, `BattleMoraleEvaluator` combines current and cumulative casualty shock, leader
loss, nearby routing allies, local outnumbering, and force-wide disadvantage. Per-soldier resolve is
an Ego-based convex curve. Synapse coverage skips the morale check; command auras reduce shock but
do not grant immunity. Squad states are `Steady`, `Shaken`, or sticky `Routing`.

Force continuation treats the aggression casualty threshold as eligibility, not an automatic order.
It also considers remaining effective Battle Value, loss trend, ability to damage the enemy, and the
mission posture. A routing squad uses the withdrawal/pursuit pipeline without covering fire or
orderly role rotation.

Organized withdrawal uses a fixed heading and leapfrog roles. The farthest suitable squad covers
while other squads bound/run; role assignment is re-evaluated as the formation moves. A rear guard
is selected only when the counterfactual predicts at least one additional survivor, with surviving
Battle Value as the tie-break. Weapon quality alone cannot pull a safely escaping squad back.

Pursuers choose `BreakOff`, `Follow`, `Press`, or `Standoff` from contact, relative speed, and
expected value. `Follow` compares holding fire, jogging with reduced-accuracy fire, and running
without fire; the run is selected when the projected gain from closing outweighs the moving shot.
`Press` runs without ranged fire as a committed pursuit posture.
`Standoff` is legal only without a meaningful speed advantage, without a melee reach this turn, and
with a worthwhile shot at current range. A standoff squad holds and fires; it never turns an
equal-speed chase into a running pursuit.

Pursuit retains actual target-squad pairings. A pursuit hold can reserve a full-aim fire cycle when
the quarry remains a viable future shot; moving clears the invested aim. The fire-window value is
zero when no worthwhile projected shot survives. A withdrawing squad beyond all useful enemy ranges
disengages when pairwise relative closing speed places interception beyond the two-turn retargeting
horizon. There is no battlefield-edge escape rule. Burrow-capable squads may break contact
immediately; flight will use the same capability seam when introduced.

**Active-pursuit invariant.** Contact remains active only while an actual pursuer-quarry pairing
has at least one of: meaningful positive closing speed; a worthwhile attack executable now; or a
committed fire cycle making observable progress toward a worthwhile attack. The aiming exception is
deliberately narrower than weapon capability: a theoretical shot may bridge turns in which a squad
holds, retains its viable target, and accumulates readiness, but it may not preserve contact while
the chosen policy repeatedly declines that fire plan. `Standoff` already implies the committed-fire
branch because it permits only holding and firing; `Follow` may instead keep contact by genuinely
closing. Force-level capability must not substitute for pair-local progress on either branch.

Battle completion produces typed `BattleOutcome` and `BattleEvent` records for withdrawal, cover,
rear guard, pursuit, rout, disengagement, field holder, and casualty/aftermath consumers.

## 7. Strategic NPC combat

Strategic combat preserves the same Battle Value currency without creating transient tactical squads.
Only organized military strength deploys and takes ordinary casualties; disorganized strength is
preserved as a separate regional pool for reorganization and disruption mechanics.

The effective-strength model is:

```text
attackerEffective = committedBV
                  × factionQuality(attacker)
                  × aggressionStrengthMultiplier
                  × ambushSurpriseMultiplier

defenderEffective = engagedDefenderBV
                  × factionQuality(defender)
                  × entrenchmentMultiplier(sharedPosition)

entrenchmentMultiplier(e) = min(3.0, 1 + 0.10 × e)
ambushSurpriseMultiplier = 1 + min(0.50, max(0, attackerIntel - defenderIntel) × 0.10)
```

Surprise belongs to the attacker: defender awareness denies surprise rather than multiplying the
defender's intrinsic strength. Entrenchment reads the side-wide shared defensive position. The
aggression strength/casualty multipliers are `Avoid 0.60/0.50`, `Cautious 0.80/0.75`, `Normal
1.00/1.00`, `Attritional 1.15/1.25`, and `Aggressive 1.30/1.50`.

Each side receives a small log-normal combat-roll perturbation:

```text
sideRoll = sideEffective × exp(z × 0.12)
```

With base intensity `0.08`, casualty rates are:

```text
attackerLossRate = clamp(intensity × defenderPressure^0.65, 0.01, 0.60)
defenderLossRate = clamp(intensity × attackerPressure^0.65
                         × max(1 / (1 + 0.08 × entrenchment), 0.35),
                         0.01, 0.75)
```

Losses are rounded against committed/engaged Battle Value, bounded by available strength, with a
minimum one-BV loss when both sides have nonzero effective strength. The attacker captures when
`attackerRoll > defenderRoll × 1.10`. The outcome distinguishes attacker destruction, invader
foothold, raid, and defender-held results; casualties do not delete civilian population.

## 8. Decisions that are part of the contract

These decisions replace earlier forms and should not be reintroduced during maintenance:

| Earlier form | Current rule |
|---|---|
| Geometric `0.65^turns` discount over the whole approach | `0.65` is only a bounded rollout discount; contact arrival uses the state/value model |
| Hard `min(pool, rate × horizon)` cap | Continuous finite-pool saturation plus continuous access cost |
| Saturation activated at a threshold | Saturation is continuous for all positive opportunity |
| Ranged removal discounted by enemy arrival time | Ranged removal is immediate; only delayed contact payoff is arrival-discounted |
| Access helplessness referenced to `targetBattleValue / AccessValueTurns` | Referenced to the negligible-removal rate, so a squad whose fire is landing is not priced as helpless |
| Access delay measured against the pursuer's own move | Measured against the net closing rate; an un-outrunnable quarry yields no access value |
| A `Phi` term scaled by the candidate's speed (pursuit contact progress) | `Phi` is a function of projected state only, so the potential difference telescopes |
| Pursuit contact progress paying `attainable` per turn of movement | A saturating position value worth `attainable` across the whole approach, once |
| Jogging as the baseline posture beyond a squad's reach | The fast approach, when the squad can run |
| Indifference band as a fraction of the squad's Battle Value | A fraction of the best score, with an absolute floor — stock and rate are not the same dimension |
| Per-soldier movement vote and weak-ranged fallback tree | Squad semantic options with doctrine masks and executable root policies |
| A flat dual-wield defense bonus | Weapon `ParryModifier`s only |
| Wound ratio as damage value | Resolver-mirroring take-out probability with live wound state |
| Perfect-impact grenade EV × delivery confidence | Scatter-node integration of enemy benefit and friendly/self cost |
| Flamer burning/panic condition | No persistent fire state; cone auto-hit and outcome-based morale are sufficient |
| `MaxFormationStandoff` as a balance clamp | Derived opening range and explicit range-model calibration seams |
| Defender readiness/detection as a strategic strength multiplier | Organized/disorganized partition plus attacker-side surprise |

## 9. Source map

For implementation changes, start here:

- Tactical lifecycle/state: `Helpers/Battles/BattleTurnResolver.cs`,
  `Helpers/Battles/BattleState.cs`, `Helpers/Battles/BattleGridManager.cs`,
  `Models/Battles/BattleHistory.cs`.
- Ranged/melee/wounds: `Helpers/Battles/RangedTargetSelector.cs`,
  `Helpers/Battles/RangedEffectivenessCurve.cs`, `Helpers/Battles/RemovalMath.cs`,
  `Helpers/Battles/BlastThrowEvaluator.cs`, `Helpers/Battles/MeleeStrikeEstimator.cs`,
  `Helpers/Battles/Resolutions/WoundResolver.cs`.
- Engagement policy: `Helpers/Battles/BattleEngagementFrameBuilder.cs`,
  `Helpers/Battles/BattleSquadPlanner.cs`, `Helpers/Battles/EngagementPotential.cs`,
  `Helpers/Battles/PairRemovalRateTable.cs`.
- Morale and force movement: `Helpers/Battles/BattleMoraleEvaluator.cs`,
  `Helpers/Battles/BattleForcePlanner.cs`, `Helpers/Battles/BattlePursuitPlanner.cs`,
  `Helpers/Battles/WithdrawalForecast.cs`, `Helpers/Battles/BattleEscapeRules.cs`.
- Strategic resolution: `Helpers/StrategicCombat/StrategicCombatResolver.cs` and the
  `StrategicCombatRules` constants.
- Regression coverage: `OnlyWar.Tests/Battles` and the strategic-combat tests under the corresponding
  namespace.

The active ranged backlog is intentionally not repeated here. Changes to it must preserve planning /
resolution parity, deterministic replay, friendly-fire attribution, and the shared Battle Value
currency.
