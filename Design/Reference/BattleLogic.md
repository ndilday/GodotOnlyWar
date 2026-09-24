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

Three terms shape which shot a soldier takes, and when (`RangedTargetSelector`, since 2026-09-22):

- **Lane spread.** A squad's shooters spread across the target squad's frontage. The lane frame is
  per `(shooter squad, target squad)`, with both frontages normalized to 0..1, and the penalty for
  leaving one's lane is a fraction of that shot's own score (`BaseLaneSpreadFraction` × faction
  `FireDiscipline`). It biases selection only; the returned removal value is unchanged. An earlier
  frame measured lanes against the whole enemy force's centroid, so at long range every shooter's
  lane pointed at the same few bodies.
- **Aim or shoot.** The choice compares value per turn: shooting now, against the prepared shot's
  value divided by the turns it takes, with the target's range projected forward along its direction
  of travel (`EvaluateFireTiming`, `ProjectedRangeChangePerTurn`). Aiming at a closing target can
  pay; aiming at a quarry running away usually does not, because the range grows while the aim
  matures. A retained aim is kept while the target can reach the shooter, while the kill chance is at
  least `LikelyTakeOutProbability`, or while the rate rule still says to shoot.
- **Ammunition.** Both sides of that comparison charge the shot for its rounds: net value = score −
  scarcity × (best score per round on offer) × rounds. Scarcity is 0 at `PlentifulMagazines` (6)
  magazines' worth of loaded plus reserve rounds and rises to 1 on an empty weapon, so a soldier
  running dry saves rounds for better shots.

A planned aim is priced in `Phi` at that same per-turn rate (`FireTiming.Readiness`), as is a stored
aim at the root (`FireTiming.StoredReadiness`). It replaced 5% of the shooter's battle value times the removal fraction, which was
about a tenth of the shot and made a squad whose soldiers aimed look idle to the option scorer.

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
derived once per planning turn for each squad and is shared by every candidate in that squad's
decision. It is the shorter of two clocks, each Battle Value above a withdrawal threshold divided
by a removal rate, clamped to 1–183 turns:

```text
T          = min(enemyClock, ownClock)
enemyClock = enemy BV above an Attritional (25%) withdrawal threshold / side's removal rate on it
ownClock   = squad BV above its own orders' threshold / Σ_e rate(e→squad) × share(e→squad)
share      = rate(e→squad) / Σ_s rate(e→s)
```

The enemy's orders are not the planner's to know, hence the assumed Attritional enemy; the squad's
own threshold is the one `BattleSquad.ShouldContinueMission` applies, counted in whole soldiers.
Until 2026-09-23 `T` was one side-wide value, enemy BV over the side's removal rate, and ignored
how fast the squad itself was being worn down. The architecture summary is in `OnlyWar_TDD.md`
§6.6 (*Engagement horizon*).

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
quarry's speed is deliberately absent from this positional formula: a potential answers "how good
is this projected state". Candidate states nevertheless project the same one-turn quarry movement,
so speed changes the resulting separation rather than adding an option-dependent bonus. Arrival
time outside useful range is priced separately by access. Each term therefore telescopes.

Two consequences are deliberate. The marginal value of a yard RISES as a squad approaches its
band, rather than tapering; the taper the older form showed was the per-turn bounty, not a
gradient. And because the total is bounded, the per-turn delta at long range is small — smaller
than the squad-scoring indifference band. Position value is a tie-breaker and a gradient where the
exchange integral is flat; it is not meant to outvote real fire, and the exchange terms decide
whenever a squad has a shot worth taking.

A squad beyond its own reach therefore falls through to the baseline posture, whose derivation is
in §5.1.

The former `EvaluatePursuitClosingValue` exception no longer exists. It read candidate speed rather
than projected state and therefore could not telescope. Projecting both endpoints now supplies its
legitimate purpose directly: an equal-speed advance preserves separation, a slower move loses
ground, and a faster move closes, all at the same end-of-turn instant.

The value model intentionally keeps doctrine in the legal-option mask and value in `Phi`. A large
negative future value cannot make an able melee-only contact-seeker retreat by outvoting its doctrine.

Options whose scores differ by less than an indifference band are treated as equivalent, and the
baseline posture, then the previous turn's choice, break the tie. **That band is a fraction of the
best score, not of the squad's Battle Value.** Battle Value is a stock and these scores are
per-turn rates, so sizing the band from the stock made it unescapable for any squad whose
shooting was worth less than that fraction of its own worth per turn: every option read as
indifferent and the tie-break decided in place of the score. A 191-value marine squad carried a
3.82 band while its whole shooting was worth 1.3 a turn. A floor still applies, for the
case where every option really is worth about nothing — which is when the baseline and the
previous posture should carry the decision. Since 2026-09-23 the floor scales with the squad's
horizon, `0.1 × T / 183`: `Phi` differences are rates integrated over `T`, so a fixed floor set
when horizons sat near the cap swallowed real decisions once `T` began ending at the first
withdrawal point.

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
exists.

Opening range is asked once per squad against the whole opposing force
(`BattleEngagementFrameBuilder.CalculatePreferredOpeningRange`, averaged per side by
`MissionOpeningRange`). Since 2026-09-22 each squad plans only its **share** of its own force's
battle value (`ForceShare`): its kill target is that share of `ApproachRemovalShare` times the
enemy's battle value, and the enemy's fire against it is scaled by the same share. Before, every
squad planned the whole kill under the whole enemy's fire.

`ApproachRemovalShare` is the loss at which the enemy is expected to **break**. The squad cannot
read the enemy's orders, so it assumes Attritional, the most stubborn aggression that still
withdraws: 1 − `AttritionalEligibilityThreshold` = 75% (Aggressive never withdraws voluntarily). The
approach is then accepted only if that break lands inside the squad's useful band less the
10-turn retreat headroom (the same quantity as the floor); if no opening range does, the
unconstrained best stands. Without that condition, extra range looked free once the kill share was
met, and Grist Nine Epsilon's tactical squads opened at ~1050, near weapon reach; the orks broke at
~500 and the marines, one cell a turn faster, never brought them back into range.

This section used to end by claiming that "pursuit uses useful reach rather than the normal standoff
band because a quarry's return fire is not the pursuit objective". **That is true of the force-level
posture decision and false of squad-level pursuit scoring**, which reads the standoff band and raw
weapon reach and never reads useful reach at all. §5.5 sets out the three range quantities, says
which of them each consumer actually uses today, and states the rule the scoring is intended to
follow.

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

### 5.5 Pursuit range targets — implemented scoring and remaining work

**STATUS. Useful-fire pursuit scoring and one-turn quarry-motion projection are implemented for the
fire-preserving `Pursuit` and `Follow` roles. `Press` remains the committed no-fire close and melee
pursuit remains contact-seeking.** The measurements come from
`OnlyWar.Tests/Battles/RangedPursuitBaselineTests.cs`, which drives one pursuit geometry per named
scenario through the real option search and writes every candidate's terms to
`%TEMP%\GodotOnlyWar\pursuit-baseline\ranged-pursuit-baseline.log`.

#### Three ranges, three questions

| Name | Question | Proposed source |
|---|---|---|
| **Useful firing range** | "Out to here my shooting still contributes something meaningful." | `BattleSquadCapabilityProfile.UsefulFireRange`, derived from the squad's outgoing `RangedEffectivenessCurve` against the representative opposing profile |
| **Optimal firing range** | "Here the modeled exchange is at its best." | `BattleSquadCapabilityProfile.EffectiveEngagementRange`, the argmax of `outgoing - incoming - meleeThreat` |
| **Contact range** | "Here I can strike." | `BattleContactRules.MeleeContactAllowance`, with `1` as the desired range of a contact-seeker |

**The implemented rule.** Fire-preserving ranged pursuit seeks *useful firing opportunities*, not optimal ones and not
contact: the objective of a ranged pursuit is to get inside useful firing range and then shoot.
Beyond that point, closing further is a trade and must be scored as one — **further closing must
justify the shots it costs**, because a moving squad fires at a Bulk penalty with its aim bonus
zeroed, and a running squad does not fire at all. Contact range remains the objective of melee
pursuit, where there is nothing to trade away.

The three are ordered `contact <= optimal <= useful` for an ordinary gun, but nothing enforces that
and the ordering does not hold in general: against an enemy that cannot shoot back, the optimal
range degenerates to the far edge of reach while the useful range stays where the gun stops working
(scenario 2 below: useful 8.8, optimal 1000.0).

**Initial usefulness criterion.** Let `floor = targetBV × NegligibleRemovalFraction` and `peak` be
the maximum of the squad's outgoing removal curve. Fire is useful where
`removal(r) >= floor + 0.2 × (peak - floor)`; `UsefulFireRange` is the outermost such range. A curve
whose peak does not clear the floor returns 0. This deliberately uses neither a percentage of peak
alone nor the floor alone: the floor excludes Gaussian damage tails that amount to plinking, while
20% of the removal *above* it requires material headroom before holding to fire is called useful.
The initial 0.2 retains the established baseline calibration of `SaturationFraction`. The pursuit
baseline's effective rifle, ineffective long-range rifle, and degrading short-range weapon provide
the initial three shapes: useful at reach, ineffective everywhere, and useful only after closing.

#### What each consumer reads today

| Consumer | Range it targets | Source |
|---|---|---|
| Squad pursuit posture (`Follow`/`Press`/`Standoff`/`BreakOff`, one per pursuing squad since 2026-09-22; the side keeps the most committed as its summary) | That squad's pair-local useful ranged opportunity, with melee retained as a separate branch; the search stops once the posture is settled | `BattleWithdrawalService.ProjectedFollowShotTurns` -> `BattleAttackOpportunityProjection` and the live `RangedTargetSelector`, fed to `BattlePursuitPlanner` as `ProjectedFollowPositiveShotTurns` |
| `Pursuit`/`Follow` exchange/access potential (`desiredRange`) | **Useful firing range** | `EngagementPotential.EvaluateExchangePotential`, `max(1, profile.UsefulFireRange)` |
| `Pursuit`/`Follow` positional progress (`role_term`) | **Useful firing range** | `EngagementPotential.EvaluatePursuitContactProgress` |
| `Press` and melee pursuit progress | Existing reach/contact objective | `EngagementPotential.EvaluatePursuitContactProgress` |
| Option mask (`contactSeekerMustClose`) | **Weapon reach** | `SquadEngagementPolicy.GetLegalOptionKinds`, `profile.PreferredBandUpper` |
| Fire-window projection | Weapon `MaximumRange` | `RangedTargetSelector.EvaluatePursuitFireWindowShot` |

The force gate still uses the older per-soldier peak-relative calculation. This phase changes only
squad scoring for fire-preserving pursuit roles; it does not change posture selection, `Press`, the
option mask, or fire-window projection.

#### Implemented formulas

For `Pursuit` and `Follow`, let `u = max(1, UsefulFireRange)`, `d` be current separation,
`g = max(0, d-u)`, and `c = ownMove-quarryRunSpeed`. Arrival delay is `g/c` when `c > 0` and
unreachable otherwise. The delay used by access is capped at the squad's existing derived expected
exchange horizon (maximum 183 turns). An unreachable arrival contributes no access value; a tiny
positive closing speed whose arrival lies beyond the horizon therefore has the same bounded access
state instead of an arbitrarily large reward.

At the useful boundary, let `r_u` be outgoing removal rate and `r_d` the rate at the current state.
The access deficit is `x = clamp((r_u-r_d)/r_u, 0, 1)` and its smooth weight is
`h = x^2(3-2x)`. Access potential remains a negative state value:
`-(targetBV/5) * h * (r_u/(targetBV/5+r_u)) * boundedDelay`. It is exactly zero once useful fire
is available. Root and projected states call the same function with their own geometry.

Positional progress is `attainable * span/(span + max(0,d-u))`, with `span` equal to the larger of
ten move strides and the distance from useful range to raw weapon reach. It saturates at `u`, so
movement within useful fire receives no second access or positional bounty. Inside `u`, exchange
potential evaluates the actual state range rather than snapping to an optimum; the expected
exchange curve alone prices whether moving closer improves the shot enough to justify lost fire.

#### Measured baseline, 2026-09-21

One soldier per side, pursuer Battle Value 20, quarry 10, pursuer speed 8. `useful` and `optimal` in
yards; scores in Battle Value.

| Scenario | Sep | Quarry spd | useful | optimal | band lower | Chosen | Driver |
|---|---|---|---|---|---|---|---|
| 1 effective rifle | 150 | 3.0 | 1000.0 | 711.2 | 700 | `Hold` 7.243 | immediate exchange |
| 1b effective rifle, beyond band lower | 900 | 3.0 | 1000.0 | 711.2 | 700 | `Hold` 4.506 | already inside useful fire; no access/position credit |
| 1c as 1b, narrow speed edge | 900 | 7.5 | 1000.0 | 949.3 | 700 | `Hold` 4.506 | already inside useful fire; `Jog` retains closing penalty |
| 1d moderate rifle, aiming hold | 60 | 3.0 | 38.1 | 12.7 | 700 | `Hold` 0.498 | readiness 0.087 + fire window 0.474 outweigh approach |
| 2 ineffective fire at long range | 950 | 3.0 | **8.8** | **1000.0** | 700 | `Hold` -0.015 | Run scores 0.025 but remains inside the 0.05 hysteresis floor |
| 3 short-ranged sidearm | 300 | 3.0 | 30.0 | 30.0 | 21 | `Run` 1.645 | access 1.582 + position 0.063 |
| 4 melee pursuer | 200 | 3.0 | 0.0 | 0.0 | 0 | `Run` 1.661 | masked to closing only |
| 5a rifle, much faster | 400 | 3.0 | 1000.0 | 711.2 | 700 | `Hold` 6.193 | immediate exchange |
| 5b rifle, nearly equal | 400 | 7.5 | 1000.0 | 949.3 | 700 | `Hold` 6.193 | useful shot dominates; no speed-ratio penalty |
| 5c rifle, equal | 400 | 8.0 | 1000.0 | 968.8 | 700 | `Hold` 6.193 | useful shot dominates; advance preserves separation |
| 5d sidearm, nearly equal | 300 | 7.5 | 30.0 | 30.0 | 21 | `Run` 0.006 | access is 0; finite position gradient chooses advance |
| 5e sidearm, equal | 300 | 8.0 | 30.0 | 30.0 | 21 | `Run` 0.000 | baseline breaks the zero-score tie toward advance |
| 6 immediate 60-yard volley | 55 | 8.0 | 60.0 | 60.0 | 42 | `Jog` 7.339 | current volley is legal; jog preserves nearly all fire and the future window |
| 7 preparation window closes | 55 | 8.0 | 39.1 | 60.0 | 42 | `Run` 0.000 | readiness/window are zero once withdrawal carries the shot out of range |
| 8 completed aim | 55 | 8.0 | 39.1 | 60.0 | 42 | `Hold` 1.473 | the matured shot resolves before movement |

Holding is legal in every row except 4, where the option mask removes it because the pursuer has no
gun, so every decision above except that one is attributable to scoring rather than to doctrine.

#### What the numbers say

The useful boundary now governs both ranged position and access. Rows 1, 1b and 5a-c are already
inside useful fire and therefore receive exactly zero from both terms; any benefit from moving
closer comes from the changed exchange rate. Row 1d shows that a worthwhile prepared volley can
beat both jog and run. Row 2 retains a positive state-derived approach gradient even though it is
too small to overcome ordinary choice hysteresis in one turn. Row 3 still commits a short-range
weapon to closing, and row 4 preserves melee doctrine.
Row 2 is an intentional limitation of the generic absolute hysteresis floor: the score prefers
advance by 0.040 BV, but that difference is treated as noise. It is not evidence that holding is
universally preferred. Rows 6-8 pin turn timing and distinguish a current shot from preparation
whose future firing window will disappear.

The former speed-ratio blow-up is gone. More importantly, every candidate is now valued at the same
post-movement instant: hold projects the quarry one run allowance farther away, while an advance
projects both feasible pursuer movement and that same quarry movement. The old
`EvaluatePursuitClosingValue` speed-ratio penalty was removed only after this geometry supplied its
purpose directly. Equal-speed pursuit therefore keeps separation rather than receiving a special
penalty or fictitious closing credit.

**Turn timing.** Shooting and melee resolve against turn-start geometry; movement for both sides is
later. Immediate shot scoring consequently uses the live range and cannot be invalidated by future
quarry motion. The state potential uses the end-of-turn geometry. Aim, ready, and reload value then
projects only the additional withdrawal intervals before the prepared shot can occur. The current
interval is already present in the projected state and is not added a second time. Preparation
readiness is zero when that shared fire-window projection says the eventual shot will be gone.

#### Remaining pursuit work

1. `SquadEngagementPolicy.EvaluateEngagementOption` — where a forgone-fire cost would be charged to
   a moving candidate, and where `role_term` would be split into two trace columns.
2. The squad-level preparation window uses weapon maximum range and a centroid separation delta for
   the one-turn squad projection; it does not project each target soldier's exact future cell
   through obstacles. (Pursuit posture itself is now chosen per squad from that squad's own pair
   projections; see §5.6 and `Design/Active/PursuitFireAndMovement.md`.)

#### Complete-pursuit validation, 2026-09-21

The opt-in `ActivePursuitScaleDiagnosticsTests` exercises the full resolver, contact lifecycle,
outcome, and aftermath-facing history. The original Grist Nine failure ran 315 marines against 20
routing orks for the final 700 turns of a 1,000-turn battle, with worthwhile standing fire
available in 1,112 of 1,112 sampled squad-turns and no shot taken. The current reusable scale
fixture terminates the 315-vs-20 pursuit by annihilation in 10 turns; the larger 360-vs-24 fixture
does so in 15. Neither hits the turn cap, alternates Hold/advance, nor advances while its logged
Hold candidate has positive outgoing fire. These fixtures begin at the pursuit transition and the
current default marine loadout resolves the quarry in melee (`first_shot_turn=none`,
`shots_fired=0`), so they establish termination/contact behavior, not that holding or ranged fire
must win every pursuit. The baseline matrix supplies the ranged timing and weapon-shape evidence.
Diagnostic summaries record first shot, shots fired, maximum consecutive worthwhile-fire chase,
posture switches, duration, outcome, and casualties.

### 5.6 Interception estimates — pair-local pressing projection

**STATUS. Pair-local pressing and useful-attack projections are implemented.** The word
"interception" still covers several different questions, and the consumers keep them separate:

1. **Hypothetical melee contact if a pursuer presses.** This is a counterfactual time for one
   concrete pursuer/quarry squad pair to enter the existing melee contact allowance if both
   continue on the relevant movement policy. It is not a record that the pair pressed, and it uses
   pair-local geometry and pair-local movement capabilities.
2. **Hypothetical time to a useful attack opportunity.** This is a counterfactual time for one
   assigned pair to enter an attack envelope that is actually useful. The envelope may be ranged
   fire or melee and may depend on weapon reach, useful-fire range, ammunition, preparation, and
   quarry motion. It is not a melee-contact time merely because the pursuer is called an
   interceptor.
3. **Observed progress under the actions that executed.** This is what the resolver can report
   after the turn: concrete move attempts and outcomes, pre/post positions, centroids, and the
   change in the measured separation. It is evidence about the actions chosen, not a validation of
   either counterfactual estimate.

A battle that ends because a useful shot removes the quarry before melee contact is required has a
   successful useful-attack outcome. It is not an interception-estimate failure just because a
   hypothetical melee-contact turn was never reached.

#### Current calculation and consumer map

| Current calculation | Current inputs | Consumer and current meaning | Audit status |
|---|---|---|---|
| `BattleWithdrawalService.EvaluatePursuitResponse` `pressTurns` / `PURSUIT_EVAL.press_attack_phase_turns` | `BattleInterceptionProjection` evaluates each pursuing squad's own pairs against every active quarry squad, using that pair's nearest placed combat-effective soldier pair, fast-approach movement capability, and fixed withdrawal heading | `BattlePursuitPlanner` chooses that squad's posture (one `PURSUIT_EVAL` per squad, with `squad=` and `doctrine=`), and the side keeps the most committed as its summary; `PURSUIT_EVAL` names the contact supplier as `press_contact_pursuer` / `press_contact_quarry`, while `PRESS_CONTACT_PROJECTION` carries its geometry, heading, speeds, destination allowance, movement clock, attack-phase clock, and unreachable reason | **Pair aggregate.** `press_contact_turns` is the continuous hypothetical press-to-contact clock; `press_attack_phase_turns` is the first attack phase that can use nonzero movement contact. An all-unreachable result is explicit and the projection record identifies a stable diagnostic candidate without calling it a supplier. |
| `BattleInterceptionProjection.CanReachContactThisTurn` as passed by the pursuit response | The same concrete pair projections used for pressing; the gate is true when any projected pair can enter the contact allowance during this turn's movement | Immediate hypothetical “contact can happen this turn” gate; `PursuitPairActivity` separately evaluates exact assigned-pair evidence after planning | **Pair aggregate.** The force-level call no longer combines a minimum separation with unrelated speed extrema. |
| `BattleEscapeRules.ProjectInterceptTurns` / `ESCAPE_EVAL.attack_opportunity_turns` | One `BattleAttackOpportunity` per concrete pursuer/withdrawing pair: live weapon, ammunition, target defense, readiness, useful-range boundary, and separate melee contact projection | `ResolveUnpursuedWithdrawalEscapes` takes the earliest valid attack opportunity and decides whether the withdrawing squad can escape beyond the retargeting horizon. A squad that is already some pursuer's quarry is not projected (`actively_pursued`); once a threat inside the horizon is found, later pursuers are checked only for a shot now (`RangedSearch.ImmediateOnly`), and after a zero-turn threat not at all | **Pair-local attack opportunity.** Ranged fire and melee contact share the elapsed-turn convention but retain different destination conditions; an all-unreachable result is explicit. The fallback `intercept_turns` speed-ratio scalar is retained only for legacy direct callers and is labeled `legacy_scalar_estimate_diagnostic_only=true`. |
| `BattleWithdrawalService.TryAssignRearGuard` and `WithdrawalForecast.ProjectOpenGround` / `Intercepted` | Each candidate squad's nearest-soldier separation to the enemy force; that squad's base run speed; one force-wide fastest pursuer; one force-wide one-turn attack reach; a finite horizon and rear-guard delay | Rear-guard selection and open-ground interception classification; `Projection.ExpectedSquadsIntercepted` feeds the candidate comparison | **Mixed geometry/speed.** A force-wide fastest pursuer is reused for independently positioned candidate squads, and nearest-soldier separation is not a squad-centroid route. This is an expected force projection, not a pair contact time. |
| `BattleWithdrawalService.ProjectedFollowShotTurns` / `FOLLOW_SHOT_EVAL` | One pursuing squad's pairs; selected shooter/target geometry, each weapon's live evaluator result, ammunition/readiness preparation, and that pair's follow/withdrawal speeds | `BattlePursuitPlanner.Input.ProjectedFollowPositiveShotTurns`, used by `BattlePursuitPlanner` for that squad's posture. Skipped where it cannot change the posture (`NeedsFollowProjection`; `follow_useful_attack_turns=not_evaluated`), otherwise stopped once the facts the planner reads are known (`follow_search=first_before_contact`, `shot_now` or `any_shot`), so the reported turn is not necessarily the earliest | **Pair aggregate.** Only valid finite pair opportunities are compared. `useful_attack_turns` is the hypothetical useful ranged-attack clock; `shot_turns` remains a compatibility alias explicitly scoped as counterfactual. The trace records supplier geometry, movement assumptions, useful-range destination, and explicit unreachable reason; it is never executed-fire evidence. |
| `SquadEngagementPolicy.GetLegalOptionKinds` / `GetOptionTier` | Nearest-soldier `MinimumDistance` to the selected primary; profile/squad base `MoveSpeed` or `GetSquadMove()`; `MeleeContactAllowance`; contact-seeking and preferred-band flags | Masks pursuit options and chooses whether a legal close action is already an `InMelee` tier | **One-turn contact gate, squad scope.** It is a legality/materialization test, not a multi-turn estimate; it still mixes nearest-soldier geometry with a squad capability minimum and must not be read as a pair-local arrival clock. |
| `EngagementPotential.TurnsToUsefulRange` and `EvaluatePursuitContactProgress` | Candidate/profile move speed, quarry run speed, projected squad state and centroid/range terms, useful-fire or contact band | Squad option scoring for `Pursuit`, `Follow`, and `Press` | **Useful-range access value.** It is a score term over projected state, not a terminal battle-time prediction. |
| `RangedTargetSelector.EvaluatePursuitFireWindowShot` and `EngagementPotential.EvaluateFireWindow` | Exact shooter, target, weapon, starting range, target motion, preparation horizon, aim/ammunition state | Fire-window scoring and prepared-fire contact evidence | **Useful-fire opportunity.** It models whether a ranged attack remains available over the horizon; it does not predict melee contact. Its executed-action evidence remains owned by `BattleRoundMetrics`. |
| `BattleActionPlanningCoordinator` pursuit pair extraction and `ReplaceCurrentTurnPursuitPairings` | The selected primary counterpart for each current-turn pursuit/follow/press/standoff decision | Freezes the pair map consumed by contact, escape, and progress diagnostics | **Assignment, not an estimate.** The selected counterpart gives later calculations a scope, but it does not make force-wide speed or separation values pair-local. |
| `PursuitPairActivity` and `BattleContactRules.Evaluate` | Exact assigned pair, pair minimum separation, rolling observed separation history, recent movement/fire evidence, and corrected same-turn reach | Contact lifecycle, break-contact, and pursuit-contact decisions | **Pair-local current behavior.** Declared speeds remain diagnostics; contact evidence comes from timely observed geometry, executed attacks, viable preparation, an eliminated quarry, or bounded startup. Executed attacks and aims are credited to the pursuer's pair whichever withdrawing squad they were aimed at. |
| `PURSUIT_PROGRESS` and `PURSUIT_MOVE_RESULT` | Exact assigned pair; pre/post nearest-soldier separation; per-member actual displacement; planned move distance and success/failure | Trace consumers and investigation tooling | **Observed progress.** `separation_gain` and the rolling history are results of executed movement. The record also states the history window/capacity, assignment start, sample validity/reset reason, startup status, measured/rolling progress, and whether observed geometry qualified as contact-maintenance evidence. They must not be substituted for a counterfactual estimate. |
| `PURSUIT_PROGRESS.declared_speed_delta` | Pair-local `CurrentSpeed` difference at the post-movement boundary | Diagnostic comparison only | **Never contact evidence.** It is retained to explain why the old scalar estimate would disagree with observed geometry. |
| `BattleTurnResolver.CurrentMinimumSeparation` / `UpdateInertTurnCount` | Force-wide minimum nearest-soldier separation, able counts, and a tolerance | Inert-turn and stagnation diagnostics | **Safety diagnostic only.** It is neither melee-contact time nor useful-attack time. |

#### Pair-local pressing projection

`BattleInterceptionProjection` is a pure, bounded helper. For each active `(pursuer squad,
quarry squad)` pair, it chooses the nearest placed pair of combat-effective soldiers in stable
soldier-id order. Contact means that this nearest pair enters
`BattleContactRules.MeleeContactAllowance` (currently one cell), matching the existing nearest
soldier contact geometry rather than claiming that a squad centroid has touched.

The movement terms belong to that same squad pair. The pursuer uses its legal fast-approach
capability — `GetSquadMove()` when the squad can Run, otherwise the same Jog multiplier used by
`FastApproachOption`. The quarry uses its own corresponding withdrawal capability. These are base
capabilities, not `CurrentSpeed`, last turn's displacement, leftover movement, or the option chosen
by the current planner pass. Pressing is therefore counterfactual and independent of the selected
action.

The quarry moves along the side's existing fixed `WithdrawalHeading`; when a routing side has not
yet stored one, the same deterministic `BattleForcePlanner.SelectWithdrawalHeading` rule supplies
it without mutating the side. The pursuer may lead the moving quarry. In continuous open-ground
geometry the helper solves

`|relativePosition + quarryVelocity × t| <= pursuerSpeed × t + contactAllowance`.

This is the analytical equivalent of pressing toward the future quarry position, and avoids an
unbounded rollout. `ContactTurns` is the movement clock. A pair already inside the allowance is
zero; a nonzero movement contact is attackable only at the next turn's attack phase because attacks
resolve before movement. The force policy therefore receives `AttackableContactTurns`, which is
zero for existing contact and otherwise the first following attack phase (`ceil(ContactTurns)`).
The separate `CanReachContactThisTurn` gate uses `ContactTurns <= 1`, so it recognizes a movement
collision without pretending that collision produces a same-turn melee strike.

The aggregate enumerates all active squad pairs, selects the smallest finite `ContactTurns`, and
records the supplying squad ids in `PURSUIT_EVAL`. Ties are stable by pursuer id then quarry id.
Equal-speed or slower stern chases have no finite root and return positive infinity (`never` in the
trace); a crossing or lead geometry may still be finite when the moving target can enter the
contact radius. Already-in-contact, zero-motion, invalid, and no-intersection cases are explicit
helper results rather than hidden denominator behavior.

This remains an open-ground approximation. It does not reserve grid cells, trace terrain, model
formation spread/member replacement, or change the quarry heading after the projection starts.
The nearest-soldier geometry and squad bottleneck speed are conservative formation proxies. The
executed `PURSUIT_PROGRESS` and `PURSUIT_MOVE_RESULT` records remain observed action evidence and
are not back-solved into this counterfactual estimate.

#### Observed pursuit contact evidence

The active-contact rule no longer treats a positive declared-speed difference as proof that a
pursuit is productive. `BattleWithdrawalService.ReplaceCurrentTurnPursuitPairings` snapshots each
assigned pair before actions: the exact able-soldier id sets, placed positions, and the pair's
nearest-soldier `MinimumDistance`. After movement, `LogPursuitProgress` measures the same nearest
soldier separation and commits one sample. This happens whether or not `BattleLog` is enabled.

The final policy is a four-sample rolling window (`PursuitProgressPolicy.HistoryLength = 4`). Its
progress value is the oldest sample's pre-action separation minus the newest sample's post-movement
separation. It must exceed `0.5` cell (`ProgressTolerance`) to supply observed-closing evidence.
The window therefore absorbs integer-grid rounding, fractional leftover movement, and one brief
blocked turn, while four consecutive blocked samples age a productive sample out. A move's
planned origin-to-destination distance is recorded separately from endpoint displacement; failed
`MoveAction`s contribute planned distance and a failure count but contribute no actual movement or
separation gain.

**Progress must be timely (2026-09-22).** Observed progress keeps contact only if, at the pair's
observed closing rate (rolling gain ÷ samples), it reaches its effect separation within
`BattlePursuitPlanner.MaximumChaseTurns` (`PursuitPairActivity.HasTimelyClosingProgress`). The
effect separation is the pursuer's useful fire range against that quarry while it is outside it,
and melee contact once it is inside that range (a pursuer inside useful range that is not firing is
closing only for contact) or when it has no useful fire. Slower progress is reported as
`slow_progress` in `pair_reasons` and counted in `CONTACT_EVAL.slow_progress_pairs`, and it does not
hold contact. Grist Nine Epsilon reached the 1000-turn cap with 16 pairs closing half a cell a turn
from 445 cells and no shot fired.

**Executed fire is credited to the pursuer (2026-09-22).** Soldiers choose their own targets, so a
pursuer's executed attacks, advancing aims, and viable aims count for its pair whichever withdrawing
squad they were aimed at. With pair-only credit, all 33 Epsilon pursuers were assigned to the ork
Cover squad while ~100 aimed and ~10 fired at other orks each turn, and contact broke as
`stalled_pursuit` with a third of the ork force still under fire. A quarry eliminated during the
pairing's own turn is also evidence (`quarry_eliminated_pairs`).

A newly assigned pair receives two completed observation turns of startup grace. This prevents a
pair from being dropped before its first movement can be observed, but the grace is not an
alternative progress signal. Assignment changes discard the old pair history. The pursuer-level
assignment state allows at most two unproductive retarget switches to receive a fresh startup
window; a productive observed window resets that switch counter. Exact able-soldier id sets are
part of the history key: replacement of a body invalidates the history even when the able count is
unchanged. A membership reset starts a fresh bounded window rather than carrying a stale nearest
soldier's distance across the change.

The same-turn contact exception remains counterfactual and separate. `PursuitPairActivity` uses
the corrected `BattleInterceptionProjection` for its assigned pair, including the pair's nearest
placed geometry, fixed withdrawal heading, legal pressing speeds, and the one-cell melee
allowance. It does not turn that projection into multi-turn pursuit evidence. Actual attacks and
successful viable fire preparation continue to arrive independently from `BattleRoundMetrics`; a
future projected shot alone still cannot maintain contact, whichever squad it would be aimed at.

The regression matrix fixes the intended effect: oscillating declared speeds cannot keep a pair
alive without net geometric gain; moving faster away from the quarry is stalled; one blocked turn
is tolerated but sustained blocking breaks contact; equal-speed chasing breaks after startup;
reassignment cannot inherit old progress or rotate through indefinite grace; and replacement
members invalidate the old window. The existing attack, aim, ready, and reload regressions retain
contact through their executed pair-local evidence.

The following clocks also look like interception when reading score traces, but belong to their
own value models and are not replacements for either estimate:

- `MeleeStrikeEstimator.EstimateChargeNet` uses one soldier's distance to a sampled enemy,
  subtracts the one-cell contact allowance, and divides by that attacker's base move. It supplies
  a charge payoff discount and `reachesContactThisTurn`; it does not model a withdrawing quarry or
  an assigned squad pair.
- `EngagementPotential.EvaluateProjectedMeleeOpportunity` estimates the incoming contact threat
  from the opposing squad's centroid-range and `opposing.MoveSpeed`. It is an expected incoming
  value over the exchange horizon, not a promise that the threat will reach a particular soldier.
- `EngagementPotential.TurnsToUsefulRange`, which feeds the potential's access value, uses
  projected squad-centroid range, the useful band (`EffectiveEngagementRange`, or
  `UsefulFireRange` for fire-preserving pursuit), the candidate profile move speed, and, for a
  pursuit role, the quarry's run speed. It replaced `EngagementExchangeModel.EvaluateArrivalTimeValue`
  and the continuation terminal of the bounded policy rollout, both since removed, which read the
  same inputs. These are potential differences and present-value terms. The `arrival_value` trace
  column now holds the root-state offset of the potential transition (the negated net-rate
  component of Φ(s)); it must not be read
  as an observed travel time.
- `EngagementPotential.EvaluateScreenRole`, `BattleEngagementFrameBuilder.AssignScreens`, and
  `SCREEN_EVAL.intercept_point` use threat-to-protected-centroid distance, threat profile speed,
  and a projected interpose point to score a screen. This is a screening/force-protection clock,
  not the pursuing squad's melee or useful-fire estimate.
- `BattleEngagementFrameBuilder.CalculateEffectiveEngagementRange` and its opening-range sweep
  use representative force profiles and the maximum opposing squad `GetSquadMove()` as a
  closing-rate input. They calibrate where an engagement should open; they do not claim that the
  fastest force member will reach the nearest quarry.

#### Pair-local useful-attack projection

`BattleAttackOpportunityProjection` evaluates the concrete `(pursuer squad, quarry squad)` pairs
used by follow posture and unpursued escape. It does not combine a force-wide minimum separation
with a different squad's weapon reach, ammunition, or movement speed. Within a pair it enumerates
placed able shooters, placed able targets, and each carried conventional ranged weapon in stable
id/template order. The first finite ranged result records the shooter, target, weapon template,
preparation flags, movement requirement, sampled useful range, and elapsed attack-phase clock.

The ranged evaluator remains the source of hit probability, target defense, expected removal, score,
and available-ammunition behavior. The projection recognizes a shot available now only when the
weapon is already readied, can fire from its current magazine/quantity, and the live evaluation is
useful at the current range. Future results may require `Ready`, `Reload`, `Aim`, or movement; only
one preparation action is advanced per attack phase, and movement is evaluated at the next attack
phase because shooting resolves before movement. Empty magazines with valid reserves take a reload
route. A typed magazine with no reserve is `Unreachable`, not a reloadable opportunity. Melee-only
pairs retain their independent contact projection, even when the ranged branch is unreachable.

The useful-range destination is a named `sampled_live_ranged_evaluator` approximation: it samples
and refines the existing live evaluator's calibrated usefulness boundary for a pair, then gives the
geometry helper that ranged destination rather than the melee contact allowance. This phase does
not redefine `BattleSquadCapabilityProfile.UsefulFireRange` or change its calibration globally, and
the boundary approximation is not executable-shot evidence. The elapsed-turn convention is common
with pressing: zero means attackable at the current turn-start attack phase; a movement or
preparation result is the first following integer attack phase. `MovementTurns` remains the
continuous/destination diagnostic, while `ElapsedTurns` is the comparison clock.

`BattleAttackOpportunityProjection.EarliestPair` aggregates only finite pair results, with stable
squad/soldier/template tie-breaks. When no pair can attack, it returns an explicit `Unreachable`
opportunity with positive-infinite elapsed time. `BattleEscapeRules` preserves that result and
records the supplying pair and mode in `ESCAPE_EVAL`; it does not convert an unreachable ranged
branch into an executable shot. `FOLLOW_SHOT_EVAL` records the ranged supplier and the overall
earliest attack mode, while `BattleRoundMetrics` and action records remain the only evidence that a
pursuer actually aimed, reloaded, or fired.

#### The geometry and speed terms that are currently being mixed

- `MinimumSeparation(pursuingSide, withdrawingSide)` is the minimum top-left distance over every
  active pursuer/quarry squad pair and every soldier pair. `MinimumSquadSeparation` is the same
  nearest-soldier operation for one squad pair. `MinimumSquadToForceSeparation` keeps one squad
  fixed and minimizes against the whole opposing force.
- `BattleEngagementFrameBuilder.MinimumDistance` and the grid's minimum-distance helpers use the
  same nearest-soldier idea for pair selection and contact gates. `SquadEngagementPolicy` then
  compares that distance with a profile or squad movement capability plus the contact allowance;
  this is a one-turn option/legal-action decision, not a continuous-time interception estimate.
- `BattleEngagementFrameBuilder.Centroid` averages able-soldier positions. Centroid distance is
  useful for squad-state scoring and trace context, but it is not the nearest soldier's contact
  distance and it can move differently when a formation spreads, loses a member, or changes its
  nearest member.
- `SquadEngagementPolicy.GetIntendedDestination`, `ProjectPrimaryCentroid`, and
  `ProjectFeasibleSquadEndpoint` steer and score from squad centroids. They project the quarry's
  centroid by its role/run speed, then project each pursuer soldier through the grid projector;
  the resulting centroid is a scored destination, not proof that the nearest soldier pair follows
  that route or that the destination is reached.
- `BattleRoundMetrics.FastestPursuitSquadSpeed` and
  `SlowestMainBodySquadSpeed` aggregate squad speeds. A squad's `GetSquadMove()` is the minimum
  **base** `GetMoveSpeed()` among its able soldiers. “Fastest squad” and “slowest squad” therefore
  do not identify the soldiers or squads that supplied the minimum separation.
- `BattleWithdrawalService.DeclaredTurnSpeed` instead takes the minimum `BattleSoldier.CurrentSpeed`
  among able soldiers. `CurrentSpeed` reflects the declared/realized turn state and may be zero
  after a hold, partial move, attack, or failed movement. It is not interchangeable with the base
  squad move or with actual displacement.
- Movement itself is grid-rounded and carries fractional `LeftoverMovement`. `MoveAction` records
  the concrete result and banks budget minus Euclidean displacement on success. A hypothetical
  estimate that reads a minimum `CurrentSpeed` as if it were observed separation gain skips this
  rounding, blocking, and leftover state.

The reusable `InterceptionEstimateFixture` makes these differences explicit. Its nearest-pursuer
and nearest-quarry example has a finite force-wide combination even though the nearest assigned
pair supplies no observed closing progress. Its straight-chase example preserves integer grid
rounding and leftover movement, while its lateral-motion example has a faster declared pursuer
speed but zero observed separation gain.

#### Investigation evidence

The latest Epsilon-named trace was not present in the repository or the available
`%TEMP%\GodotOnlyWar\pursuit-trace` files during this audit; the current files there are the
opt-in pursuit summaries and the ranged baseline trace. The supplied Epsilon excerpt remains the
relevant investigation evidence: squad 114 closed by roughly `0.7–1.3` distance units per turn
over turns 290–294 while its diagnostic scalar estimate ranged from about `607` to `8,440` turns.
That is exactly the expected symptom of observed progress and a tiny/unstable scalar diagnostic
being read as if it were a counterfactual arrival time. It is not, by itself, evidence that the
battle's melee estimate failed.

#### Current behavior versus remaining work

This phase changes the pair-local hypothetical pressing clock, the separate useful-attack clock, and
the matching current-turn/escape consumers:

- `BattlePursuitPlanner` now receives the earliest pair-local pressing projection. It keeps the
  existing aggression choices when a finite pair is available, but an explicit unreachable result
  can trigger the existing arithmetic standoff/break-off override.
- Since 2026-09-22 it is evaluated **per pursuing squad** over that squad's own pairs
  (`Design/Active/PursuitFireAndMovement.md`). Under a Normal policy the squad's equipment doctrine
  decides (contact-seeking presses, fire-support follows, mixed weighs the projections); a squad with
  no shot at all chases; and a pressing squad whose contact is more than
  `BattlePursuitPlanner.MaximumChaseTurns` away is treated as unable to catch up (stand and fire if it
  has a shot now, follow if it has a shot later, otherwise break off). A squad that breaks off while
  others keep pursuing stands down and leaves the field; its battle value still counts for its side,
  and the outcome does not report it as disengaged (`BattleSideState.StoodDownSquadIds`).
- The force-level `CanReachContactThisTurn` input comes from those same pair projections; it no
  longer combines independent force-wide separation and speed extrema.
- `PRESS_CONTACT_PROJECTION` is the canonical hypothetical press-to-contact record. It names the
  selected or diagnostic candidate pair, nearest-pair positions, fixed quarry heading, both pair
  movement assumptions, the `melee_contact_allowance` destination, continuous movement/next-attack
  clocks, and an explicit `press_unreachable_reason`. `PURSUIT_EVAL` keeps only the decision summary:
  `press_contact_turns`, `press_attack_phase_turns`, and the contact supplier ids.
- `FOLLOW_SHOT_EVAL` now records `scope=pair_aggregate`, pair count, ranged supplier, attack mode/
  kind, shooter/target/weapon ids, preparation, movement requirement, sampled-boundary model, useful
  range, pair positions/speeds/heading, destination condition, useful-attack clock, and explicit
  unreachable reason. `ESCAPE_EVAL` records the same pair-local geometry contract for its selected
  useful ranged/melee opportunity. These are counterfactual opportunities, not executed actions.
- `PURSUIT_PROGRESS` and `PURSUIT_MOVE_RESULT` remain observed action traces; they now include
  planned-versus-actual displacement, failed moves, exact membership validity, history window and
  assignment start, sample validity/reset reason, startup status, measured/rolling progress, and
  whether observed geometry qualified as contact-maintenance evidence. The gameplay history exists
  independently of the logging sink.
- Retired trace names `press_intercept_turns`, `press_intercept_pursuer`, `press_intercept_quarry`,
  and `follow_shot_turns` are replaced by the explicit contact/useful-attack names above. The
  `PursuitPairActivity.ClosingSpeed` value and `PURSUIT_PROGRESS.declared_speed_delta` remain only as
  diagnostic comparisons; `declared_speed_not_evidence=true` is part of that contract.
- Exact pair contact lifecycle behavior, shooting-before-movement resolution, escape projection,
  rear-guard forecast, and squad option scoring remain separate consumers. The resolver still allows
  shooting, casualty resolution, and battle termination before a later movement/contact opportunity.
- The rear-guard `ForecastApproximateOneTurnAttackReach` remains a documented legacy force forecast;
  it is not an executable ranged shot, a pair attack opportunity, or an escape threat.

#### Acceptance criteria for the projection contract

The focused fixtures in `OnlyWar.Tests/Battles/InterceptionEstimateFixturesTests.cs` and
`OnlyWar.Tests/Battles/AttackOpportunityProjectionTests.cs` are the minimum reusable contract:

- nearest pursuer is not assumed to be the fastest pursuer, and nearest quarry is not assumed to be
  the slowest quarry;
- centroid distance and nearest-soldier separation remain separately named quantities, so a scored
  squad-centroid destination cannot be presented as contact geometry;
- equal-speed and slower stern pursuers produce no finite positive pairwise interception time, while
  a valid crossing/lead geometry may still be finite;
- already-in-contact and contact-during-movement cases include the melee allowance and distinguish
  movement contact from the next attack phase;
- a straight chase demonstrates grid rounding and fractional leftover movement rather than ideal
  continuous motion;
- faster declared movement can coexist with zero observed separation gain when both sides move
  laterally or the nearest pair changes;
- a shooting resolver run can end in `Annihilation` with a `ShootAction` and no melee/closing action
  even though a separate melee-contact estimate is positive;
- every future interception consumer identifies exactly one of the three meanings above and names
  its scope (force aggregate, squad pair, soldier pair, centroid, or observed action result);
- projected trace records identify the supplying pair (or an explicitly non-supplying unreachable
  candidate), geometry, movement assumptions, destination condition, comparison clock, and explicit
  unreachable reason;
- observed-progress trace records identify the history window, measured and rolling progress,
  sample validity/reset reason, startup status, and whether observed geometry qualified as contact
  evidence; and
- diagnostic fields retain their original meaning or are explicitly renamed/documented when a
  consumer changes scope; and
- targeted battle tests continue to pass with `Category!=Slow`, with slow scenario diagnostics run
  only when explicitly requested.

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

Each pursuing squad chooses `BreakOff`, `Follow`, `Press`, or `Standoff` for itself, from its own
pairs' contact and useful-attack projections, its aggression and its equipment doctrine
(contact-seeking squads press, fire-support squads follow, mixed squads weigh the projections). A
squad with no shot at all chases; a squad that cannot reach contact within
`BattlePursuitPlanner.MaximumChaseTurns` stands and fires with a shot now, follows with a shot later,
and otherwise breaks off. The side's posture is the most committed squad posture, so the pursuit
ends only when every squad breaks off. A squad that breaks off while others continue **stands
down** and leaves the field (`BattleSideState.StoodDownSquadIds`): its battle value at that moment
still counts in its side's force metrics, so the force evaluator does not read the exit as
casualties. `Follow` compares holding fire, jogging with reduced-accuracy fire, and running without
fire; the run is selected when the projected gain from closing outweighs the moving shot. `Press`
runs without ranged fire as a committed pursuit posture.
`Standoff` is legal only when the squad cannot catch up -- no pair can ever reach contact, or the
nearest contact is more than `BattlePursuitPlanner.MaximumChaseTurns` away -- without a melee reach
this turn, and with a worthwhile shot at current range. A standoff squad holds and fires; it never
turns an unwinnable chase into a running pursuit.

Pursuit retains actual target-squad pairings. A pursuit hold can reserve a full-aim fire cycle when
the quarry remains a viable future shot; moving clears the invested aim. The fire-window value is
zero when no worthwhile projected shot survives. A withdrawing squad beyond all useful enemy ranges
disengages when its assigned pair has no timely observed geometric progress, executed attack, viable
preparation, eliminated quarry, bounded startup grace, or corrected same-turn contact exception. There is no
battlefield-edge escape rule. Burrow-capable squads may break contact immediately; flight will use
the same capability seam when introduced.

**Active-pursuit invariant.** Contact remains active only while an actual pursuer-quarry pairing
has at least one of: smoothed observed nearest-soldier separation gain that reaches the pair's
effect separation within the chase horizon; a bounded startup window for a newly assigned pair; a
worthwhile attack executed recently by the pursuer against any withdrawing squad; a committed fire
cycle on any withdrawing squad making observable progress toward a worthwhile attack; or a quarry
eliminated during the pairing's turn. A declared-speed advantage is diagnostic only. The
four-sample, half-cell history tolerates grid rounding and one brief obstruction, while sustained
blocking, moving away, and equal-speed chasing age out. Assignment and exact able-soldier membership
changes invalidate the old window, and a pursuer can receive only bounded grace across unproductive
retargets. The aiming exception is deliberately narrower than weapon capability: a theoretical shot
may bridge turns in which a squad holds, retains its viable target, and accumulates readiness, but it
may not preserve contact while the chosen policy repeatedly declines that fire plan. Same-turn
contact remains the corrected pair-local projection exception; force-level capability must not
substitute for pair-local observed progress or executed fire evidence.

Battle completion produces typed `BattleOutcome` and `BattleEvent` records for withdrawal, cover,
rear guard, pursuit, rout, disengagement, field holder, and casualty/aftermath consumers.
`BattleOutcome.DisengagedSquadIds` excludes stood-down pursuers: they left the field but did not
withdraw, and the mission layer reads a disengaged mission squad as the mission side withdrawing.

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

### Pursuit diagnostic records

With `BattleLog` enabled, pursuit emits the following additional records. These are observations,
not new scoring inputs or changes to the legal-option mask. The three clocks are intentionally
separate: `PRESS_CONTACT_PROJECTION` is hypothetical press-to-contact, `FOLLOW_SHOT_EVAL` and the
pair opportunity in `ESCAPE_EVAL` are hypothetical useful-attack timing, and `PURSUIT_PROGRESS` /
`PURSUIT_MOVE_RESULT` are observed executed-action evidence.

- `PRESS_CONTACT_PROJECTION` explains the pair-aggregate press estimate: the supplying pair when
  finite (or a stable diagnostic candidate when all pairs are unreachable), nearest-pair positions,
  fixed quarry heading, pair-local fast-approach and withdrawal speeds, `melee_contact_allowance`,
  continuous `press_movement_turns`, next-attack-phase `press_attack_phase_turns`, and
  `press_unreachable_reason`. `PURSUIT_EVAL` contains the compact posture-decision summary but is
  not a replacement for this geometry record.
- `FOLLOW_SHOT_EVAL` explains the pair-aggregate useful-attack estimate: `pair_count`, the concrete
  ranged supplier, shooter/target/weapon ids, attack kind/mode, preparation flags, movement
  requirement, pair positions/speeds/heading, the sampled live-evaluator useful-range destination,
  elapsed useful-attack clock, and the overall earliest attack mode. The named
  `useful_range_model=sampled_live_ranged_evaluator` is a deterministic boundary approximation;
  it is not evidence that a shot executed. A missing ranged opportunity is represented by
  `useful_attack_turns=none`, `shot_turns=none`, and an explicit `ranged_unreachable_reason`.
- `ESCAPE_EVAL` records the concrete pair opportunity when escape evaluation receives one. Its
  `attack_mode=Ranged`, `Melee`, or `Unreachable` describes a counterfactual opportunity and the
  `selected_*` fields repeat its geometry, movement assumptions, destination condition, attack
  clock, and unreachable reason. Actual aiming, reloading, and firing remain action/
  `BattleRoundMetrics` evidence. The legacy speed-ratio fallback is labeled
  `legacy_scalar_estimate_diagnostic_only=true`.
- `PURSUIT_FIRE_EVAL` captures each pursuer's counterfactual stationary root action before movement
  declarations, even when Hold is excluded. It also reports the conventional target selector's
  preferred shot, score, range, target squad, and rejection category. This conventional comparison
  excludes template/blast weapons; the stationary root action includes the normal action planner's
  alternatives and preparation decisions. `weapons` entries encode
  `templateId:loadedRounds:reloadProgress:equipped`. A positive conventional score alone does not
  establish that the action planner would shoot now; inspect `stationary_action` and `hold_legal`.
- `PURSUIT_PROGRESS` compares the assigned pair's pre-action and post-movement nearest-soldier
  separation, reports centroids for context, actual versus planned displacement, failed moves,
  membership validity, the `history_window` and assignment start, measured and rolling progress,
  sample validity/reset reason, startup status, and whether observed geometry qualified as contact
  evidence. Positive `separation_gain` means the pair actually got closer. Exact member ids, rather
  than counts, invalidate a history across replacement. `declared_speed_delta` explains the old
  scalar comparison but is never contact evidence.
- `PURSUIT_MOVE_RESULT` records each assigned pursuer or quarry's attempted concrete move and
  success, including failed ordinary moves that are omitted from executed-action metrics. Correlate
  by turn, squad, role, and soldier with `ENGAGE_EVAL.intended` and the existing planned `MOVE`
  budget/blocking records. Closing moves use the concrete moves retained by their closing action.

Diagnostics are collected only while logging is enabled. Counterfactual fire records are stored on
the decision and emitted serially with engagement traces; they do not replace selected actions.

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
- Interception/contact and movement audit: `Helpers/Battles/BattleWithdrawalService.cs`,
  `Helpers/Battles/BattleContactRules.cs`, `Helpers/Battles/SoldierMovementProjector.cs`,
  `Helpers/Battles/SoldierMovementPlanner.cs`, `Helpers/Battles/Actions/MoveAction.cs`,
  `Helpers/Battles/Actions/SquadClosingMoveAction.cs`, and
  `OnlyWar.Tests/Battles/InterceptionEstimateFixturesTests.cs`.
- Strategic resolution: `Helpers/StrategicCombat/StrategicCombatResolver.cs` and the
  `StrategicCombatRules` constants.
- Regression coverage: `OnlyWar.Tests/Battles` and the strategic-combat tests under the corresponding
  namespace. `OnlyWar.Tests/Battles/RangedPursuitBaselineTests.cs` is the reusable pursuit baseline
  behind §5.5: it measures rather than judges, so add scenarios to its matrix rather than writing a
  fresh fixture when a pursuit change needs before/after evidence.

The active ranged backlog is intentionally not repeated here. Changes to it must preserve planning /
resolution parity, deterministic replay, friendly-fire attribution, and the shared Battle Value
currency.
