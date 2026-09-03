# Casualty Realism — Leg Wounds, Incapacitation, and Field Apothecaries

Status: **IMPLEMENTED — reference record.** Moved from `Active/` to `Reference/` on 2026-08-07. Every
phase this plan scoped — −1, 0, 1, 1b, 2a, 2b and 3 — is built. Phase 2a's design is the sibling
`SpecialistAttachment.md`. The leg `SeverWound` move to `Mortal` (§2.1) landed 2026-08-06 as
`Database/RulesMigration_LegSeverThreshold.sql`, and §3.2's tuning questions are all answered in
`Helpers/Medical/FieldCareConstants.cs`.

Retained rather than distilled away because §2.4's wound-band healing cadence, §2.1's motive-speed
curve, and §2.6's field-care triage rules are decision tables that stay useful. The shipped
architecture is summarized in `OnlyWar_TDD.md` §5.3 and §6.6.1; this document is the *why*.

**Residual items live elsewhere, not here:**
- **Ranged-vs-melee pricing** is now part of the unified `Design/Reference/BattleLogic.md`; the
  two cone/grenade planner tests are green regression coverage. This file retains the wound-band
  and medical decision tables rather than the engagement-scoring implementation history.
- **Phase 2c** (characters as units of one in battle) shipped as part of Phase 2, with
  engagement-boundary materialization rather than mid-fight join/leave. **Phase 4** (stance / prone
  combat) remains deliberately unscheduled. §5 below is the confidence assessment behind cutting
  stance and remains the governing argument.

## Operational duty readiness (Phase 2)

Physical casualty state and Chapter deployment policy are deliberately separate. A brother can be
combat-effective in the current battle while being physically undeployable for a later engagement
(for example, after losing a second functioning arm/hand group), or physically deployable while
being **WITHHELD** by the Chapter's operational doctrine. `DutyReadinessService` evaluates these
questions in order: untreated severance, procedure reservation, combat incapacitation, insufficient
functioning arms, then the Chapter's inclusive worst-wound threshold. The explicit **Incapacitated**
policy has no additional wound threshold, but does not waive the physical or procedure exclusions.

The worst active band anywhere on the body is used; wounds on separate locations are not summed.
Squads additionally require the configured minimum duty-ready strength and, by default, a
duty-ready leader. Individually deployed characters do not inherit those squad gates. A threshold
crossing that does not cause physical incapacitation does not remove a brother from an engagement
already under way: the next engagement boundary rebuilds the participant set and withholds him
until healing or a doctrine change makes him duty-ready again. Doctrine-only transitions do not
create near-death, recovery, or other permanent service-history events.

Covers three PRD items that share one substrate (the wound model and what "out of the fight"
means) and should be planned together:

- §5.4 **Leg Wound & Prone-Combat Realism (maybe)** — a solid leg hit staggers rather than fells.
- §5.4 **Player-Soldier Incapacitation (maybe)** — a persistent casualty state between "walking
  wounded" and "dead".
- **Apothecary field care** — *not currently in the PRD in this form.* §4.8 covers the
  Apothecarium screen, weekly natural healing, and surgical procedures; it says nothing about an
  Apothecary attached to a deployed force accelerating recovery. This plan proposes it; the PRD
  needs a new §4.8 sub-section once the attachment question (§3.1) is settled.

They interlock: raising the leg cripple bar produces more soldiers who are *down but alive*;
incapacitation gives that population a persistent state; field apothecaries are what the player can
*do* about that population.

---

## 1. What already exists

Established by reading the code, not assumed:

| Concern | Current state |
| --- | --- |
| Wound accumulation | `Wounds` — packed nibble-per-band `uint`, bands Negligible→Unsurvivable, 5 per band promotes. `Models/Soldiers/Body.cs`. |
| Cripple / sever | Per-`HitLocationTemplate` thresholds. Legs: cripple = `Critical`, sever = `Massive`. Feet: cripple = `Major`, sever = `Critical`. Torso: cripple = `Massive`. |
| "Out of the fight" | `Soldier.CanFight` — false if **any** motive or vital location is crippled, or all hand groups are gone. One boolean, no gradations. |
| Slowed movement | `BattleSoldier.IsSlow` → flat `×0.75` move speed. The only sub-lethal motive effect that exists. |
| Downed, not dead | `BattleTurnResolver._incapacitatedSoldiers` — battle-scoped record of everyone who went down without dying; `FinishOffAbandonedWounded` kills the *losing* side's wounded when a side held the field, and **explicitly exempts player soldiers**. |
| Player death | `PlayerChapterBattleAftermathPolicy.RemoveSoldiersKilledInBattle` — dies only on a **severed vital** location. A crippled-but-not-severed vital already means "went down, lived". |
| Stance | `Stance` enum and a per-stance `HitProbabilityMap` (3 columns per location) exist. **Nothing ever assigns a stance other than `Standing`** outside a clone test. Stance is spec, not behavior. |
| Weekly healing | `MedicalTurnProcessor.ApplyWeeklyHealing` — unconditional, one week per wounded location, skips severed and replacement-eligible locations. No staffing input at all. |
| Surgery | `MedicalProcedureService` — Apothecary + Techmarine co-location, surgery-capable site, Requisition. Apothecary identified by **template name** (`"Apothecary"`, `"Master of the Apothecarion"`). |
| Medical rating | `RatingKeys.Medical` exists as a computed soldier rating. |
| Seniority ordering | `Helpers/SoldierSeniority.cs` already orders by `Template.Rank` desc; `Template.Subrank` exists and is used by `SoldierFilterService`. The triage tie-break is already expressible. |
| Orders | `Order.AssignedSquads` — orders bind **squads**, never individuals. There is no model for attaching a soldier to a deployment. |
| Loadouts | `LoadoutDoctrineService` (planet→chapter→template) and `CharacterLoadoutService` (per-character kit) resolve fresh at every allocation. A character carries his own kit, which matters for §3.1. |

**Two facts drive the cost estimate.** The DB threshold change is a one-line migration; everything
expensive hangs off it. And stance is entirely unimplemented — the PRD made prone-fire the point of
the leg-wound rework, so that item was really "implement stance" wearing a leg-wound hat. §2.2
removes that dependency.

---

## 2. Design

### 2.1 Motive impairment becomes graded — and never binary for feet

The current model asks one yes/no question ("is a motive location crippled?") and answers it with
total immobility. Replace it with a **speed multiplier computed per motive location by wound band**,
where immobility is simply what happens when the product reaches zero. This subsumes both the leg
threshold raise and the foot fix without adding a second concept.

| Location | Below cripple | At cripple | Severed |
| --- | --- | --- | --- |
| Leg (cripple raised `Critical` → `Massive`) | graded slow | 0 — cannot walk | 0 |
| Foot (cripple stays `Major`) | graded slow | **floored severe slow, never 0** | 0 for that foot |

**Feet never fell a soldier.** Confirmed as the right call: a large-calibre wound to the foot
reliably breaks bone and disrupts tendon — running is out, weight-bearing is agony — but a man on a
shattered foot is still a man who can shoot, and "shot in the foot, out of the fight entirely" is
the least believable outcome in the current model. Since we are *not* implementing prone fire
(§2.2), a location that immobilizes is a location that removes the soldier from the battle
outright, which makes getting this right more load-bearing than it would otherwise be.

The two legs compound multiplicatively; a soldier with two badly-hurt legs is slower than one with
a single bad leg but is not felled unless a leg reaches `Massive` or is severed.

Mechanical changes:
1. Rules-DB migration: `Left Leg` / `Right Leg` `CrippleWound` `Critical` → `Massive`
   (`RulesMigration_LegCrippleThreshold.sql`), **and `SeverWound` `Massive` → `Mortal`**
   (`RulesMigration_LegSeverThreshold.sql`, applied 2026-08-06 — see below). Both mirrored in
   `HumanBodyTemplate` and `TyranidWarriorBodyTemplate` in `Body.cs`, or the hard-coded fallbacks
   disagree with the DB.

   **Consequences of the sever move, checked rather than assumed.** Felling behaviour is
   *unchanged*: `MotiveImpairment` zeroes speed at `≥ Massive` / crippled / severed, and the
   **cripple** threshold did not move. Planner scoring is *unchanged*:
   `BattleSquadPlanner.AccumulateTakeOutTerms` reads `min(CrippleWound, SeverWound)`, which was
   `Massive` before and stays `Massive` after — so seeded battle baselines do not move, and the
   full `.Battles` suite confirmed it (542 pass, the same 2 known-red pricing failures). What
   *does* change is the aftermath: a felled leg is now a healing wound rather than an amputation,
   and the bionics load Phase 3 accidentally created is gone. Pinned by
   `MedicalTurnProcessorTests.ALegAtMassive_IsCrippledButNotSevered_AndStaysFrozen` and
   `.ALegAtMortal_IsSevered`.

   **Why sever moved too.** Raising cripple to `Massive` while sever stayed at `Massive` collapsed
   the two thresholds onto the same band, so *every* leg wound that felled a marine also severed the
   leg and made it replacement-eligible — a large, unintended bionics load, and it deleted
   "crippled but not severed" from the set of states a leg can hold. That state is exactly what
   §2.3's **Incapacitated** outcome is built on, so losing it for the body's principal motive
   location was a real loss. Pushing sever up one band to `Mortal` restores the intermediate state:
   a leg at `Massive` fells the soldier and leaves the leg attached; only `Mortal` takes it off.
   Discovered during Phase 3 verification, not during planning — the §2.1 table below reads as
   though cripple and sever were distinct for legs, which is what the original thresholds made true
   and the first migration accidentally undid.
2. Replace `BattleSoldier.IsSlow` / the flat `×0.75` in `GetMoveSpeed()` with the banded multiplier
   and its per-location-class floor.
3. Split `Soldier.CanFight` into `CanFight` (hands + consciousness) and `CanMove` (motive
   locations). Highest blast radius in the plan — `CanFight` is consumed by planning, targeting,
   morale, deployment gating, and the Apothecarium.

Under this model a marine with one Critical leg keeps fighting at reduced speed instead of dropping
— which is the whole point of the PRD item, and is achieved without a single new decision for the
planner to make.

### 2.2 Stance and prone fire — cut

**Decision: do not implement stance in this body of work.** A soldier whose motive capability
reaches zero is removed from the fight, exactly as today. Rationale, and a confidence read, in §5.

The consequence to accept openly: a felled marine is out of the battle even though he is holding a
bolter. That is a known fidelity loss, taken deliberately in exchange for not touching the squad
planner. It becomes recoverable later — see §4, Phase 4.

### 2.3 Incapacitation as persistent state

Three outcomes for a player soldier, resolved at battle end:

| Outcome | Trigger | Consequence |
| --- | --- | --- |
| **Impaired** | motive location degraded but non-zero | Keeps fighting, slower. Normal recovery. |
| **Incapacitated** | motive capability zero, or a vital location **crippled** but not severed | Removed from combat **alive**. Persists into the battle result as a casualty, not a kill. |
| **Killed** | vital location **severed** | Existing fallen-brother / gene-seed / death-record path. |

**Power-armor biostasis (decided).** A player soldier who goes down cannot die of his wounds
awaiting treatment — his armor puts him into stasis. This resolves the largest open question in the
previous draft and simplifies the whole plan:
- No deterioration clock, no bleed-out pass, no per-day medical resolution.
- Apothecary care is **exclusively about speed of recovery**, never about survival.
- Field care can be resolved weekly in `MedicalTurnProcessor` alongside natural healing.
- It also justifies the player/NPC asymmetry in fiction rather than by fiat: enemies without such
  armor stay under the existing `FinishOffAbandonedWounded` rules.

The "vital crippled but not severed" case already exists in code and already survives — it is
simply unnamed and unpersisted. Much of this item is **naming, persisting, and gating** an outcome
the engine already produces.

Disposition at battle end keys on `BattleHistory.Outcome.SideHoldingField`, which
`FinishOffAbandonedWounded` already consults:
- **Player held the field** → recovered, enters the wound-recovery pipeline.
- **Player did not hold the field** → presumed dead, gene-seed lost, via the existing death path.
  This is the current agreed rule and gives losing a fight teeth beyond the casualty count.
  Contesting that outcome is deferred to the **battlefield recovery missions** item now recorded in
  PRD §5.7 — a later mission type that returns for the brother, his gene-seed, or at minimum his
  armor and wargear, gated on wargear inventory and transport existing.

### 2.4 How natural healing actually works today

Worth stating precisely, because everything in §2.5 plugs into it and it is easy to misremember as
"after N weeks the wound is gone".

`Wounds.ApplyWeekOfHealing` is a **cascading one-band-per-interval step-down**, not a timer to zero:

| Band | Weeks at that band before it steps down |
| --- | --- |
| Unsurvivable → Mortal | 7 |
| Mortal → Massive | 6 |
| Massive → Critical | 5 |
| Critical → Major | 4 |
| Major → Moderate | 3 |
| Moderate → Minor | 2 |
| Negligible + Minor | cleared outright on any healing pass |

Two details that matter for design:

- **Demotion preserves count.** Three Major wounds become three Moderate wounds.
- **`AddWound` sets `WeeksOfHealing = 0` — for every band**, on top of adding the new wound. See §3.3.

**Defect found and fixed (2026-08-05).** The cadence above is what the model was *meant* to do; it
is not what it did. `WeeksOfHealing += 0x11111100` advanced **every** band's clock every week,
whether or not a wound sat in that band, so a wound stepping down found the lower band's dwell time
already served and fell straight through — cascading all the way to Minor in a single pass. Measured
against `RecoveryTimeLeft()`:

| Wound | Advertised | Actual (before fix) |
| --- | --- | --- |
| Moderate | 3 | 3 |
| Major | 6 | 4 |
| Critical | 10 | 5 |
| Massive | 15 | 6 |
| Mortal | 21 | 7 |
| Unsurvivable | 28 | 8 |

Every wound above Moderate collapsed to "top band's dwell + 1". Reversing the order of the
step-down checks removes the same-pass cascade but does **not** fix this, because the lower band's
clock is already saturated when the wound arrives — the wound then steps down one band per week
regardless of its dwell time (Unsurvivable ≈ 13 weeks instead of 28). Resetting the *receiving*
band's clock on demotion gets much closer but still fails for Mortal and Unsurvivable: over a long
convalescence the unused low nibbles overflow past 0xf and carry into the bands above them.

The fix applied: **every band that holds wounds advances its own clock, independently and
concurrently; empty bands do not** (`Wounds.AdvanceOccupiedBandClocks`).

The governing principle, and the thing the original code got right in intent and wrong in
execution: **wounds are discrete injuries, not one severity counter.** A broken nose, a swollen eye
and a split lip all mend at once — the lip does not wait for the nose. So a location's Major wounds
must convalesce alongside its Critical ones. The original blanket increment honored that but also
aged *empty* bands, which is what let a stepping-down wound find the band below already served. The
correction is narrow: age a band only while something is in it.

This falls out well. Because each band's dwell time is exactly one week shorter than the band above,
and all occupied bands run together, **a band always empties one week before the band above steps
down into it** — so the step-down can never overfill a band, even from a location carrying five
wounds in every band at once. All six wound levels heal in their advertised time, and multi-wound
locations resolve on a sensible interleaved timeline (worked example below).

**A discarded intermediate, recorded so it is not re-attempted.** An earlier version advanced only
the *worst* band's clock. It produced correct single-wound cadences but froze every lesser wound
until the worst one cleared, which contradicts the discrete-injury model — and because lower bands
then stayed occupied, step-downs collided: three Critical wounds falling onto three existing Major
wounds left **six Major wounds**, one over `WOUND_MAX`. That state is not merely untidy. Every
severity comparison in the game — cripple, sever, `CanFight`, the Apothecarium's labels — reads
`WoundTotal` as a plain magnitude, and six Major wounds (0x6000) compare as **less severe** than the
one Critical wound (0x10000) they are equivalent to, so an over-full location silently reads as
*uncrippled*. Concurrent per-band clocks remove the collision at its source.

`Wounds.Normalize()` was added during that attempt and is **kept as an invariant guard**: it folds
any band above `WOUND_MAX` into the band above at the `WOUND_MAX + 1` ratio, carrying the remainder,
and runs on both `AddWound` and `ApplyWeekOfHealing`. `AddWound` genuinely needs it (a sixth Major
wound is one Critical). The healing path can no longer trigger it, and a test asserts that.

All of the above is pinned by `OnlyWar.Tests/Domain/WoundHealingCadenceTests.cs` (11 tests: the six
advertised recovery times, single-band step-down, no band over maximum from a full house, the
`AddWound` fold, and the worked example).

**Worked example — 3 Critical + 3 Major in one location, fresh off the field:**

| Week | State | |
| --- | --- | --- |
| 1–2 | 3C / 3Mj | both bands' clocks running together |
| 3 | 3C / 3Mo | the Major wounds step down on their own three-week clock |
| 4 | 3Mj / 3Mo | the Critical wounds follow a week later, onto a now-empty band |
| 5 | 3Mj / 3Mi | |
| 6 | 3Mj | |
| 7–8 | 3Mo | |
| 9 | 3Mi | |
| 10 | clear | |

Ten weeks — which is exactly what `RecoveryTimeLeft()` reported at week 0 for a Critical wound. The
interleaving means the estimate comes out right here, and it is the number §2.6's triage sorts on.

**Balance consequence:** severe wounds now take ~3.5× longer to heal than they did in practice.
Recovery times, Apothecarium readiness, and the value of a field Apothecary all move with this;
the current cadence is retained and no further change is scheduled.

**The step-down structure is a gift for the medical system**: an Apothecary's treatment is simply a
*forced demotion*, expressible in the model's own vocabulary and immediately visible, with no
sub-week accumulator anywhere.

**This step-down structure is a gift for the medical system**: an Apothecary's treatment is simply a
*forced demotion*, expressible in the model's own vocabulary and immediately visible, with no
sub-week accumulator anywhere.

### 2.5 Astartes daily healing — negligible wounds close overnight

**New (decided).** At the end of each campaign day, negligible wounds clear on their own for
Astartes, reflecting their accelerated healing factor. `WoundTotal &= 0xfffffff0` in a daily pass;
Minor and above stay on the weekly cascade.

The boundary is meaningful rather than cosmetic. Negligible wounds promote to Minor at five
accumulated, so clearing them daily means **a day's worth of glancing hits no longer compounds into
a real wound, while a single battle's worth still does** — promotion within an engagement is
untouched, since a battle resolves inside one day. That is the correct place to draw the line.

### 2.6 Apothecary field care — daily treatment, effective immediately

An Apothecary attached to an order converts his **Medical** rating into a **daily wound capacity**,
spent each campaign day on the wounded under that order.

**Treatment is a forced wound-band demotion, applied the day it happens.** Not a banked credit
settled at end of turn — that was the wrong shape. A brother hit in a day-2 assault, treated that
evening, must go into the **day-3** battle at the reduced severity. Missions resolve day by day
(`MissionDayScheduler`) and battles read live wound state, so treatment that only lands at turn
processing would be invisible exactly where it matters most.

- **Reach:** every wounded soldier in squads assigned to the **same order** — which is what makes
  order-level attachment (§3.1) the right shape rather than merely convenient.
- **Capacity:** daily budget = f(Medical rating), spent on demotions. Demoting a high band costs
  more capacity than a low one — but far less than proportionally, since the bands are powers of 16
  (§3.2).
- **Triage (decided):** worst wound first, and deliberately *not* spread thin. The goal is returning
  brothers to the line, and Astartes healing already handles light wounds without help — so
  concentrating capacity on cases that would otherwise be out for months is both the fictional and
  the mechanically useful answer. Order: most severe first, then `Template.Rank` desc, then
  `Template.Subrank` desc, then random (seeded RNG, for replay determinism). `SoldierSeniority`
  supplies the rank/subrank half. Re-run **each day**, so day 4's casualties can displace day 1's.
- **Severity measure:** `Wounds.RecoveryTimeLeft()` — the player-visible number, so the triage order
  the player sees matches the one the game runs.
- **Where it runs:** a daily medical pass on the existing `MissionDayScheduler` day loop, after the
  day's Acting phase.

**Garrison care (added).** Field care is not only a mission mechanic. An Apothecary **not** assigned
to a mission treats co-located soldiers who are likewise not on a mission — the Apothecarium at rest,
which is where most convalescence actually happens. Same capacity and same triage order; reach is
co-location (same ship, or same region) rather than a shared order, matching the rule
`MedicalProcedureService` already uses for surgery staff. It resolves in `MedicalTurnProcessor`
during turn processing, since with nobody fighting there is no reason to iterate days.

This also creates the intended tension in a legible place: an Apothecary sent forward with an
assault is an Apothecary not clearing the backlog at home, and both effects are visible on the same
screen.
- **Baseline unchanged:** natural healing stays unconditional for everyone. Apothecary presence is a
  bonus, never a prerequisite — otherwise every garrisoned squad is silently punished and the feature
  reads as a tax.
- **What it cannot do:** unfreeze a replacement-eligible location. Surgery remains surgery.

**Historical first-pass scope limit (0.7; superseded by Phase 2).** The attached Apothecary had
**no battlefield presence** in the original field-care pass — he was with the force but abstracted
out of the engagement, and only his between-days healing effect was modeled. That boundary kept the
first field-care implementation from silently inventing battle-layer entities.

Phase 2 now materializes an individually duty-ready attached character as a one-person battle
element at each engagement boundary. The character retains the home squad, equipment, order, and
history identity; a withheld or physically unavailable character remains attached but contributes
no battle element. Mid-fight join/leave behavior and specialist battlefield auras remain future
work, but the current attached-character casualty and equipment boundary is no longer abstracted.

---

## 3. Open questions

### 3.1 ★ The specialist attachment model — decided: order-level attachment (Phase 2a BUILT)

> **Status:** designed in full by `Design/Reference/SpecialistAttachment.md` and implemented as
> Phase 2a. Every sub-question below is resolved; see the end of this section.


Orders bind squads, never individuals. Today HQ squads and top-level specialist squads deploy as
whole entities like line squads, which is not how the fiction works: an Apothecary, a Champion, a
Chaplain, or a Techmarine attaches to an operation and returns afterward. **This is not an
Apothecary question — it is the general model for every HQ/specialist role**, and it warrants its
own design doc; this plan consumes its answer rather than producing it.

**Decision: order-level attachment.** `Order` gains an attached-soldiers collection alongside
`AssignedSquads`, and the order-issue UI gains a picker over available specialists. The specialist
is attached to the *operation*, not to a particular squad.

Why this over squad secondment (the alternative considered): the specialist's contribution is
usually to the operation as a whole rather than to one squad's fighting strength — an Apothecary
treats everyone under the order, a Techmarine's expertise applies to the mission, a Chaplain's
presence is felt force-wide. Binding him to one squad would mean choosing an arbitrary squad and
then writing rules to leak his effect back out to the others. Order-level attachment also makes
field care's reach fall out for free: **the order is the reach** (§2.4).

**Battlefield presence — Phase 2 boundary model.** Order-level attachment still does not bind the
character to a particular standing squad. At an engagement boundary, however, an individually
duty-ready attached character is constructed as a unit of one alongside the ordered squads. The
campaign `Squad` remains the identity anchor for cohesion, equipment, aftermath, and history, while
the battle wrapper freezes the participant set for the current engagement. A later boundary can
withhold the character after a wound or doctrine change without rewriting the order or home roster.

This deliberately ships the smallest useful unit-of-one model: engagement-boundary entry and
casualty handling. Mid-fight join/leave actions and specialist battlefield effects (Champion
presence, Chaplain morale, Techmarine repairs) remain follow-on work rather than being inferred from
the attachment relationship.

**Dependent: squads whose members can be detached.** Attaching individuals to orders implies that
administrative and HQ squads must be able to give up members independently, which line squads should
not. That needs a marker on the squad template (a `PermitsIndividualDetachment`-style flag, or a
squad-role classification if one is wanted for other reasons), plus UI on the chapter/squad surfaces
for pulling a man out and putting him back. This is new UX in its own right and belongs in the
attachment design doc, not here.

**Sub-questions: all RESOLVED in `Design/Reference/SpecialistAttachment.md` §4, and Phase 2a is
BUILT.** Answers, for reference (see that doc for rationale):
- *Which squad types carry the flag?* `SquadTypes.PermitsIndividualDetachment = 0x80` on the four HQ
  templates and the four chapter offices (ids 5, 6, 7, 8, 9, 10, 11, 19), authored by
  `Database/RulesMigration_SpecialistDetachment.sql`. Not per-role.
- *More than one order?* No — guarded at order issue.
- *Home-squad strength/readiness?* Headcount yes, available strength no. `Squad.Members` is never
  modified by attachment (removing him would make him load back as a fallen brother).
- *Persistence / release?* Attachment lasts exactly as long as the order does; the three existing
  order-teardown paths release it, and death detaches him.
- *Home squad disbanded/destroyed?* The attachment survives; only his own death ends it.
- *Does his squad become non-deployable?* **It is never deployable at all.** The flag is two-sided:
  a formation that may lend individuals is a personnel pool, not a manoeuvre element. This
  deliberately replaced an earlier pair of mutual-exclusion guards that produced order-dependent
  behavior. Enforced in `OrderAssignment`, *not* by marking the templates `Administrative` —
  `IsOperational` must stay true or surgery staffing and recruitment/implantation break.
- *Unit-of-one join/leave (Phase 2c)?* **Engagement-boundary materialization shipped in Phase 2.**
  `BattleSquadFactory` creates a one-person battle element for each individually duty-ready
  attachment and freezes that participant for the current engagement. Mid-fight join/leave remains
  a future specialist-specific heuristic, potentially seeded by the order's aggression.

**For the record — what "first-class detachments" would have meant.** Before specialist pools and
the Phase 2 engagement boundary, an HQ or specialist squad was a `Squad` with members that deployed
as a unit. In the pool model it stops being deployable
at all and becomes a *roster you draw from*: every deployment composes an ad-hoc **detachment** — an
arbitrary list of individuals and squads — and that detachment, not the standing squad, is what
receives orders and fights. It is the most canonical model (it is how a company actually task-organizes),
and it subsumes attachment entirely, since attaching a specialist is just including him in the
detachment. It was ranked largest because squad identity is load-bearing well below the order layer:
`BattleSquad` cohesion, morale, formation, squad-template *elements*, and loadout doctrine all anchor
on a squad that has a template and a stable membership. Making arbitrary individual composition
first-class turns the battle layer's squad into an ad-hoc grouping and leaves those systems without
their anchor. Order-level attachment gets most of the fictional payoff while leaving that anchor
intact.

### 3.2 Care budget shape — **RESOLVED (Phase 2b, 2026-08-06)**

Every tunable lives in `Helpers/Medical/FieldCareConstants.cs`, in code and not the rules DB
(the `MoraleConstants` / `CasualtyConstants` precedent). Answers:

| Question | Answer | Where |
| --- | --- | --- |
| Rating → capacity | **Mildly superlinear**: `3.0 × (rating/100)^1.5`, clamped at 8.0/day. A Master at 130 is worth ~1.3× an ordinary Apothecary, not several. | `BaseDailyCapacity`, `CapacityExponent`, `MaxDailyCapacityPerApothecary` |
| Cost of a demotion | **Flat in band INDEX, not band value**: `1.0 + 0.5 × (index−1)` — Moderate 1.0 … Unsurvivable 3.5. One Apothecary-day moves the worst band in the game down one step. Plus a sub-linear `+0.5×(n−1)` surcharge for extra wounds in the band, since a demotion moves the whole band at once. | `DemotionBaseCost`, `DemotionCostPerBand`, `DemotionCountSurcharge` |
| Per-soldier daily cap | **None.** Greedy worst-first to exhaustion; one man may absorb the whole day. Daily *and* per-treatment re-triage is what keeps it fair. | `FieldCareService.RunOneDay` |
| Unspent capacity | **Use-it-or-lose-it.** No carry-over state to persist. | — |
| Anything else supplying capacity | **No** — Apothecaries only this pass. A ship's apothecarion or fortress-monastery bonus is a later question. | — |
| Medical XP for treating | **Yes, implemented.** There is no roll, so the margin substitute is *work done*: `0.01` skill points per point of capacity actually spent, to every base skill composing the Medical rating (Diagnosis + First Aid, read from the data-driven `RatingDefinition` rather than by name), split between co-working Apothecaries by capacity share. A fully-busy week banks ~0.21 points — deliberately level with `ChapterUpkeepProcessor`'s 0.2 weekly training points. | `MedicalExperiencePerCapacitySpent` |
| Opportunity cost / default lean | **Field wins, by construction rather than by rule.** An Apothecary under an order fails the "not on a mission" test that defines the garrison pool, so the pools are disjoint and no man spends a day twice. The cost is shown, not hidden: the Apothecarium's field-care readout goes to "no Apothecary on hand" for the men he left behind. | `ApplyGarrisonFieldCare` |

**One consequence worth recording, since §2.6 does not make it obvious.**
`HitLocation.IsReplacementEligible` is true from the **cripple** threshold upward, so the worst wound
field care can ever reach is the band immediately *below* it — `Critical` for a torso or a leg. A
brother who has actually gone down is a surgical case and an Apothecary in the field cannot shortcut
that. What he can do is exactly what was asked for: return the walking wounded to the line, including
men carrying several Critical wounds who would otherwise be out for two months.

### 3.3 Smaller calls

- **Slow curve values.** What multiplier does each band give, and what is the foot floor? Proposed
  starting point: Major 0.85, Critical 0.6, foot floor 0.4, legs 0 at Massive.
- **Deployability.** §4.12 says no marine with a crippled motive location may deploy. Under graded
  impairment, an impaired marine can fight — do we let the player deploy him with a warning, and
  does the roster/loadout logic respect that?
- **Multi-battle missions.** An incapacitated soldier is out for the rest of the mission (biostasis:
  out but safe). The *lightly* wounded are the point of daily treatment — a brother patched up on
  day 2 fights on day 3 at reduced severity. **Verify that battle setup reads live wound state per
  battle rather than snapshotting it at mission start**; if it snapshots, that is the one place
  daily care reaches into battle setup and must change.
- **★ `AddWound` resets the healing clock for every band.** *Treatment itself is never at risk* — a
  demotion is written into `WoundTotal` and nothing takes it back. What resets is `WeeksOfHealing`,
  and it resets on **any** new wound to that location however trivial. A brother sitting on a Massive
  wound one week from stepping down to Critical, who then takes a Negligible graze, is back to five
  weeks. This is pre-existing behavior, not something treatment creates — but daily fighting is
  exactly the condition that exposes it: over a week-long assault a wounded brother is scratched
  repeatedly and may accumulate **no natural healing at all**, which quietly makes the Apothecary the
  only healing that functions during a mission. That may be a feature (field care matters most in
  sustained fighting) or too harsh (a scratch erases a month). It needs a deliberate call, not a
  default. Options: reset only the wounded band; reset nothing; reset proportionally to the new
  wound's severity. **Resolved — keep the global reset**, and the reasoning has moved to PRD §6.14
  so it outlives this document: promotion destroys wound identity by design, so healing progress is
  necessarily a location-level property, and location-level progress being set back by fresh trauma
  is the coherent rule rather than a tolerated wart. A graduated setback scaled to the new wound's
  severity is recorded there as the contingency if disproportionality bites in play.
- **Healing-cadence rebalance.** The §2.4 fix makes severe wounds take ~3.5× longer than they did in
  practice. Are the advertised dwell times (7/6/5/4/3/2/1 weeks) the numbers we actually want now
  that they are real, or were they only ever tolerable because they were never reached?
- **Garrison vs field priority. Resolved (Phase 2b) — field, by construction.** The question turned
  out not to need a rule: an Apothecary attached to an order, or whose squad is under orders, fails
  the "not on a mission" predicate that *defines* the garrison pool, so the two pools are disjoint
  and the same man can never spend the same day twice. One capacity pool shared under a single
  triage was considered and rejected — it would have let a forward Apothecary keep clearing the
  Apothecarium backlog from the field, which is precisely the tension §2.6 wants to create.
- **Apothecary casualties.** An attached, individually duty-ready Apothecary now enters as a
  one-person battle element and can become a casualty. The current field-care model still treats
  medical capacity as a between-days effect; stopping or reducing that capacity after a tactical
  casualty remains a future medical-specialist behavior rather than an implicit side effect of
  attachment.
- **Daily healing scope. Resolved (Phase 1b) — Astartes-only**, expressed as
  `SpeciesAbilities.AcceleratedHealing` on the species rather than as a player-faction check, so
  the gate is a property of the biology and a future transhuman enemy gets it for the same reason.
- **Morale. Resolved (Phase 1) — same stress.** Morale reads `AbleSoldiers`, and a downed brother
  is not able, so the existing behavior already treats him as a casualty. Left unchanged
  deliberately: in the moment the squad cannot tell a dead brother from an unconscious one, and
  discounting the stress would mean the squad reacting to information it does not have. Revisit if
  §2.6's field care ever makes recovery visible on the battlefield.
 - **★ Ranged-vs-melee pricing — resolved in `BattleLogic.md`.** Tactical scoring now uses the
  resolver-mirroring take-out model for ranged, template, and melee contact value. The cone and
  grenade tie fixtures now select the cone as intended; they are regression coverage, not an open
  casualty-realism defect. This document retains the wound and medical decisions that feed that
  shared model, not the engagement-scoring implementation history.
- **Consciousness threshold.** Is incapacitation purely vital-crippled/immobilized, or also a
  whole-body wound-load threshold? The latter needs a new aggregate.
- **Save compatibility. Resolved (Phase 1) — no new persisted state at all.** The condition is
  derived from wounds, which the `HitLocation` table already carries, and saves are rebuilt from
  scratch on every write, so there is nothing to migrate and nothing to default. An incapacitated
  brother keeps his squad, which matters: `GameStateDataAccess` reads a squad-less player soldier
  as a fallen brother.
- **Tunable placement.** Morale precedent is a code constants file, not the rules DB. Proposed: DB
  for the leg threshold only (it is genuinely location data); a `CasualtyConstants` /
  `FieldCareConstants` file for the slow curve, care budget, and triage caps.

Expect seeded battle baselines to diverge regardless — the leg threshold change alone rewrites every
battle outcome.

---

## 4. Phasing

**Phase 0 — `CanFight` / `CanMove` split.** Pure refactor, no behavior change intended, guarded by
the existing battle suite. Everything else depends on it.

**Phase 1 — Incapacitation as persistent state. ✅ Done (2026-08-06).** `CasualtyState` /
`CasualtyStateEvaluator` (`Models/Soldiers/CasualtyState.cs`) name the three outcomes and derive
them from the body alone plus one external fact — whether the body was recovered.
`PlayerChapterBattleAftermathPolicy.ResolvePlayerCasualtyDispositions` is the single settlement
point, keyed on `BattleOutcome.SideHoldingField` (null — a mutual disengagement or the turn cap —
counts as recovered, matching `FinishOffAbandonedWounded`). `BattleHistory.IncapacitatedSoldierIds`
carries the outcome out of the battle, disjoint from `KilledSoldierIds`; the debrief report,
`MissionContext.FriendlyDeaths`/`FriendlyIncapacitated`, and
`MissionReportSummaryBuilder.BuildFriendlyCasualtyLine` render the two apart.

**No new persisted state**, and none is needed: the condition is derived from the wounds the
`HitLocation` table already stores, and a recovered brother keeps his squad, so he is never
mistaken for a fallen one. That settles §3.3's "absent state = healthy, or a migration?" — absent
state is healthy because there is no state to store. Pinned by `PlayerIncapacitationTests` and a
round-trip case in `SaveLoadRoundTripTests`.

Two decisions taken here rather than deferred. **Morale is unchanged**: a brother going down
generates exactly the stress a death does, because morale reads `AbleSoldiers` and in the moment
nobody in the squad knows which it was — what they see is a man dropping. **`WoundResolver`'s fall
hook keeps `IsCombatEffective`** as its predicate; that is precisely "out of the fight", which is
what the hook means now that the state has a name. Phase 3 breaks the equivalence between a
crippled motive location and `!IsCombatEffective`, and the motive branch must gain the explicit
test at that point (noted in the code).

**Phase −1 — Healing cadence fix. ✅ Done (2026-08-05).** `Wounds.AdvanceOccupiedBandClocks`
(concurrent per-band clocks, empty bands excluded), demotion masks clearing the receiving band, and
`Wounds.Normalize()` as an invariant guard on both mutation paths — pinned by
`WoundHealingCadenceTests`. Rebalancing the now-real dwell times and play-verifying the harsher
campaign remain open (§3.3).

**Phase 1b — Astartes daily healing. ✅ Done (2026-08-06).** `Wounds.ClearNegligibleWounds()` plus
`MedicalTurnProcessor.ApplyDailyHealing`, gated on a new `SpeciesAbilities.AcceleratedHealing`
(rules-DB migration `RulesMigration_AstartesAcceleratedHealing.sql` sets it on Space Marine) rather
than on the player faction, so a future transhuman enemy inherits it.

The seam is a new `onDayEnd` hook on `MissionDayScheduler.Run`, supplied by `MissionTurnProcessor`.
Scheduler-level and not per-driver, deliberately: one order fans out into several single-squad
drivers under `MissionForceMode.IndependentSquads`, so a driver-hung pass would run repeatedly per
day over overlapping men. `ChapterUpkeepProcessor.ProcessMedical` runs the same pass for garrison
weeks; that call is subsumed by the weekly cascade today (which clears Negligible and Minor for
everyone) and is kept explicit so the daily rule does not silently depend on it.

**Correction to §2.5's arithmetic:** promotion happens at *six*, not five. `Wounds.Normalize` folds
a band once it exceeds `WOUND_MAX` (5), so the sixth Negligible graze becomes one Minor wound. The
design intent is unaffected — a battle's worth still compounds, a week of separate days does not.

**Phase 2a — Order-level specialist attachment.** `Order.AttachedSoldiers`, the squad-template
detachment flag, availability validation, save/load, the order-issue picker, and the chapter-side
UI for pulling a specialist out of his squad and returning him. Phase 2 adds the battle-construction
boundary: an individually duty-ready attachment becomes a one-person battle element while the
organizational attachment remains the source of home identity and equipment.

**Phase 2b — Apothecary field care. ✅ Done (2026-08-06).** `Helpers/Medical/FieldCareService.cs`
plus `FieldCareConstants.cs` (every tunable, §3.2). Treatment is a forced band demotion —
`Wounds.FindTreatableBand()` / `Wounds.ApplyTreatmentDemotion()`, expressed in the healing model's
own vocabulary so the effect is immediately visible to `RecoveryTimeLeft()`, the Apothecarium, and
the next day's battle.

**The daily seam and its dedup.** The pass hangs off `MissionDayScheduler.Run`'s scheduler-level
`onDayEnd`, immediately after Phase 1b's natural daily healing, and runs **once per distinct
`Order`**. `MissionTurnProcessor` collects `distinctPlayerOrders` from its `ScheduledMission` list
*before* the day loop and iterates that dictionary — never the scheduled elements — because
`BuildMissionElements` fans one order into several single-squad elements under
`MissionForceMode.IndependentSquads` and a per-element pass would make an Apothecary silently worth
3× (SpecialistAttachment.md §8 trap 1).

**Triage.** Worst first by `Wounds.RecoveryTimeLeft()` over *treatable* locations only (an untreatable
crippled limb would otherwise park a man permanently at the head of a queue he can never leave), then
`Template.Rank` desc, then `Template.Subrank` desc, then a seeded random. The random draws from a
**private** `Random` keyed on `(order id, day)` — deliberately not the shared session RNG, since
consuming that stream from a medical pass would shift every subsequent battle roll and move seeded
battle baselines. Re-triaged after *every* treatment, not just every day.

**Garrison care** resolves in `ChapterUpkeepProcessor.ProcessMedical`, before the weekly cascade,
running the identical daily routine seven times over co-located non-mission brothers. Co-location is
computed through `PlayerSoldier.EffectiveRegion` (trap 2); `MedicalProcedureService`'s surgery gating
was routed through the same accessor in this phase, so an attached Apothecary can no longer staff a
surgery at a site he has left.

**Player-visible surfaces** include the attached specialist's duty status and battle participation
boundary: `BattleSquad` retains the campaign identity and `MissionContext` retains the engagement
participant set, while `MissionContext.FieldCare` → `MissionOutcomeClassification` →
`MissionReportSummaryBuilder.BuildFieldCareLine` is appended to the end-of-turn debrief. The
Apothecarium's `MedicalSoldierSummary.FieldCareStatus` names who is covering a brother and at what
daily capacity — or says nobody is — and its status separates duty-ready, physically unavailable,
and doctrine-withheld characters.

Carries the `AddWound` progress-reset decision (§3.3) unchanged. Pinned by
`OnlyWar.Tests/Domain/FieldCareServiceTests.cs` — capacity curve, cost-curve flatness, worst-first
with all three tie-breaks, daily re-triage displacing an earlier casualty, treatment visible to the
next day, garrison settlement and its co-location boundary, field-beats-garrison disjointness,
replacement-eligible locations untouched, and the once-per-order property behind trap 1 — plus three
report-line cases in `MissionReportSummaryBuilderTests`.

**Phase 2c — Characters as units of one in battle. ✅ Boundary slice shipped with Phase 2.** An
individually duty-ready attached character is materialized as a one-person battle element at the
engagement boundary; the campaign squad remains the identity/equipment/history anchor and the
current engagement freezes its participant set. This makes an attached Apothecary eligible for
ordinary battle casualty handling. Mid-fight join/leave and battlefield effects for Champion,
Chaplain, and Techmarine remain deliberately deferred; those are the planner-facing expansion that
§5 argues should be taken on separately.

**Phase 3 — Graded motive impairment.** DB migration + `Body.cs` fallbacks + banded speed
multiplier with the foot floor. Expect battle-balance churn and BV recalibration pressure.

**Phase 4 — Stance and prone combat. Not scheduled.** Revisit only after terrain and cover land
(PRD §5.7 Battle Visuals Phase 3), since stance's real payoff is prone *behind* something, and
after the engagement-scoring work in `Design/Reference/BattleLogic.md` has stabilized. See §5.

Every phase this plan scopes is now built. The implementation and player-facing behavior are
specified in the TDD and PRD; this reference retains the wound-band and field-care decision tables.

---

## 5. Why stance is cut — confidence assessment

The question asked was how confident I am that stance decision-making could be added to the squad
planner without compounding the instability the battle logic has shown. Split honestly, because the
two halves are very different:

**Involuntary prone only** (a felled soldier goes prone, fires at a penalty after a delay, makes no
choices): *moderate confidence*. It adds no decision axis. But it is not free either — the planner
assumes squad members move as a body, so a member anchored in place needs excluding from cohesion,
formation, and movement planning, and included in target selection and morale. That is a
*subtraction* from the planner rather than a new axis, which is the safer kind of change, but it is
still a change to the code that has needed the most iteration.

**Voluntary stance** (squads choose to kneel or go prone tactically): *low confidence*, and the
reason is structural rather than a matter of care taken. Stance's entire value is a trade of
exposure against mobility and accuracy — that is, a *defensive* term. The squad planner scores
primarily outgoing effect, and the current reconciliation is documented in
`Design/Reference/BattleLogic.md`. Adding a defensive-value axis before terrain and line of sight
exist would still make the decision hard to interpret. Separately, prone in open ground is a thin
decision; the interesting one is prone behind cover, and there is no terrain or line-of-sight system
yet — so building voluntary stance before terrain means building it twice.

The recommendation follows the low-confidence half: cut stance entirely for now, take the fidelity
loss in §2.2, and note that Phase 3 delivers most of the PRD's stated motivation anyway. The item's
goal was that a solid leg hit should stagger rather than reliably fell — graded motive impairment
achieves that without the planner ever learning a new decision.
