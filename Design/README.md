# Design Library

Godot ignores this directory via `.gdignore`; its contents are documentation and design artifacts, not runtime assets.

## Structure

- `Active/` — designs with meaningful implementation work still outstanding.
- `Reference/` — durable technical content the TDD structurally cannot absorb.
- `VisualBaselines/` — selected visual directions used by shipped interfaces.
- `Exploration/` — unresolved UI alternatives. Once a direction is selected, retain the chosen baseline and remove rejected variants.

New feature plans go directly in `Active/`.

## Promotion rule

**Retention is decided by kind of content, not by lifecycle stage.** "It shipped" is not a reason to
keep a file — it is the moment to decide whether anything in it is worth keeping at all. Revised
2026-08-08, because the previous rule ("implemented → `Reference/`") was a lifecycle rule with no
eviction criterion, so the folder grew monotonically and reached 2.7× the size of the TDD.

When a plan is implemented, split it:

- **Distil into the TDD** and **delete the file**: everything about *how the work went*. Phase-by-phase
  status, work-item breakdowns, sequencing and commit plans, test-strategy checklists, test counts,
  blast-radius lists, and re-statements of what the code now plainly says.
- **Retain in `Reference/`** only what the TDD cannot absorb without becoming unreadable: quantitative
  tables, derivations and calibration sweeps, and rejected alternatives *with the reason they were
  rejected*. A doc that survives should read as an appendix, not as a narrative.

Two tests, applied at promotion and whenever the folder is audited:

1. **Citation test.** A `Reference/` doc earns its place only while something cites it — code comments,
   the TDD, or the PRD. Nothing cites it, nothing needs it; fold the residue and delete.
2. **Drift test.** A retained doc must not restate anything with a live source of truth. Snapshotting
   rules-DB tables or a formula that lives in code produces a second copy that silently goes stale —
   and it did, in all three docs audited on 2026-08-08. Point at the source; do not mirror it.

**A `Reference/` doc is never the place to record open work.** Residual items belong to whatever tracker
owns them — an `Active/` plan, the PRD backlog, or the TDD's debt section — and must be moved there
*before* the file is promoted, not left behind in it.

Generated Godot `.import` metadata does not belong here. Render scripts should only be retained when they are portable and runnable in the supported project environment.

## Current active designs

The Alpha 0.8 event spine, Command workspace, faction-relationship/target-intelligence plans,
equipment/ammunition foundation, Chapter Muster and squad-lineage work, Recovery Operations and
individual postings, and Planetary Operations workspace have been promoted into `OnlyWar_TDD.md`;
their implementation facts and verification boundaries are recorded there. The three corresponding
Active plans were deleted on 2026-08-21 under the promotion rule. Active design work currently retained
here is:

- `RangedCombatFollowUps.md` — narrow ranged-combat backlog described below.
- `PlanetaryOperationsRework.md` — implemented 2026-08-25; retained temporarily as its phase-by-phase
  acceptance record. The shipped architecture is summarized in `OnlyWar_TDD.md` §7.5.

Audited 2026-08-16 against the code. The equipment/ammunition foundation is implemented and its
active plan was removed under the promotion rule; the narrow pooled `WeaponSet` compatibility
cleanup is tracked in PRD §5.7 rather than in an active design. `Reference/BattleLogic.md` owns the
engagement-scoring derivations, tactical combat decisions, and strategic NPC-combat equations that
were previously split across several phase plans. The remaining live battle work is intentionally
narrow:

- `RangedCombatFollowUps.md` — **backlog** for friendly line-of-fire tracing, krak grenades,
  launcher expansion, and template/terrain interaction gated on Battle Visuals Phase 3. Closed
  scatter-pricing and delivery-confidence items remain recorded there only as short decisions.
- TDD §8.16 — calibration/measurement debt that is not a new player-facing rules plan: real
  transition telescoping and a few named scoring seams.

The former engagement-scoring trackers and large-scale NPC-combat record were distilled into
`Reference/BattleLogic.md` and removed. Their phase history, test counts, and open-work lists do not
belong in a reference appendix.

`ConsumptionFeedingAsMission.md` also moved to `Reference/` on 2026-08-07, implemented: biomass feeding
and swarm spreading are now planner-allocated taskings competing on the same per-region force budget
as defence and offence, rather than planet-update side effects that each spent the whole swarm. It is
retained for the two decisions the code cannot explain — why feeding is one mission type and not two,
and why expansion shares the budget but deliberately not the offensive code path.

Two plans moved to `Reference/` on 2026-08-07 once implemented: `CasualtyRealism.md` (graded motive
impairment, persistent incapacitation, Astartes daily healing, Apothecary field care) and
`SpecialistAttachment.md` (the historical format-13 detachment decisions). Both are retained
rather than deleted because their decision tables remain useful — the wound-band healing cadence and
motive-speed curve in the first, and the resolved detachment sub-questions plus the save load-ordering
trap in the second. The administrative-formation and character-as-battle-element architecture that
superseded the organizational half of the second is now distilled into `OnlyWar_TDD.md`; its open
join/leave governance question remains in PRD §5.7. Their other residual items did **not** move with
them: stance/prone combat is in PRD §5.7, and the active ranged backlog remains in
`RangedCombatFollowUps.md`.

Implemented mission scheduling, engagement range and posture, squad engagement planning, take-out-probability combat scoring,
evasion/burrowing, morale, withdrawal/pursuit, scatter-aware targeting, civil stability, strategic NPC
combat, casualty/medical simulation, specialist attachment, multi-faction regions, data-driven ratings, and opening-scenario architecture are documented in `OnlyWar_TDD.md`. Detailed formulas or decision tables remain under `Reference/` only
where they are still useful independently.

**First pass under the new promotion rule, 2026-08-08.** The three docs with no code citations were
audited; all three had drifted from the code, which is what the drift test predicts of a snapshot
nobody reads.

- `MultiFactionRegions.md` — **folded into TDD §6.2 and deleted.** It was largely a work-item and
  sequencing plan, and its two technical cores were superseded: the spotter was rewritten to weight by
  `WatchScore` (the old intel-then-strength rule could let the faction least responsible for catching
  an intruder be the one that caught it), and the detection formula was replaced by the three-term
  watch model that TDD §6.5 already documents in full. Retained in the fold: the mission
  target-faction taxonomy, the magnitude-word ladder, and the rejected alternatives.
- `DataDrivenRatings.md` — **folded into TDD §4.1.1 and deleted.** Redundant with a TDD paragraph that
  already covered the shipped system, and its schema snapshot had drifted (`Key` shipped as
  `RatingKey`) while its migration section described the long-deleted `RulesDbTool`. The rules DB is
  the source of truth for the seven formulas and their tiers. Retained in the fold: the closed
  component vocabulary, the `ranged`-is-the-odd-one-out warning, and the award-dedup and
  material-naming decisions.
- `BattleLogic.md` — **new unified reference.** It retains the strategic-combat equations, the
  take-out/wound-progress and range derivations, the finite-pool potential model, and the rejected
  alternatives that the TDD would become unwieldy to carry in full.

Nine code comments cited the two deleted docs by a stale flat path (`Design/DataDrivenRatings.md`) that
had been wrong since the `Reference/` folder was created; all were repointed at the TDD sections that
now own the content.

`Exploration/ScoutMuster` is the only unresolved visual study. Selecting a direction is a product
decision: move the chosen image to `VisualBaselines/` and delete its rejected alternatives. Planet
Detail and Region Detail have shipped canonical workspaces under `VisualBaselines/`; their earlier
alternatives were removed. Do not keep generated or nonportable render sources beside raster studies.
