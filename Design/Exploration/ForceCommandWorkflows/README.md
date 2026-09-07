# Force Command Workflow Exploration

**Status:** Exploratory raster mockups, not approved visual baselines or implementation specifications.

These concepts explore a workflow-oriented alternative to dividing player tasks strictly between
the Chapter, Fleet, Planet, Region, and Apothecarium screens. Each mockup keeps the information and
common corrective actions for one player intention in a single workspace.

## Chapter Muster — Initial Promotions

`chapter_muster_initial_promotions_v3.png`

- Promotion candidates, many vacancies, and transport constraints remain visible together.
- Existing squad-type badges identify each candidate's current placement, while a wide honors strip
  carries the promotion-relevant distinction data.
- The candidate pool retains an explicit organizational scope and the Chapter screen's compound
  filter vocabulary, extended with Sergeant Recommended; staged candidates remain visible but locked.
- The vacancy board prioritizes throughput across the many leaderless and understrength squads found
  at campaign start instead of devoting the workspace to one destination roster.
- Squads use persistent formal designations and retain their lineage when empty. Separate empty-lineage
  and available-new-formation groups distinguish reconstituting a historic squad from founding one.
- Roster cells combine present staffing with every staged departure and arrival, using signed red and
  green deltas such as `10 -2 +1 / 10` instead of a separate staged-count column.
- Personnel changes are staged as a small plan rather than forcing a sequence of destructive filter
  changes.
- A single, named location conflict discloses ship capacity before confirmation and offers explicit
  before/after relocation choices without abandoning the muster.

## Planetary Operations — Landfall

The implemented architecture is recorded in [`OnlyWar_TDD.md` §7.5](../../../OnlyWar_TDD.md#75-operations-workspaces);
these images remain composition references rather than runtime UI contracts.

Current mode concepts:

- `planetary_operations_world_dossier_v3.png`
- `planetary_operations_regional_operations_v3.png`

- Planetary Operations replaces the old Planet Detail and Region Detail navigation pair with two
  modes that preserve the selected world and region. World Dossier retains demographics, governance,
  faction populations and force estimates, PDF and Astartes presence, and disclosed fortification
  coverage. Regional Operations owns movement, landing, intelligence, and order management.
- Both modes use the same sixteen-card regional topology: equal rectangular terrain cards in the
  planet's centered `1–2–3–4–3–2–1` diamond. Regular gutters and consistent row offsets make
  adjacency legible without relying on generated hex geometry. The implementation keeps one map
  mounted and swaps only the side and contextual bottom panels.
- Region borders encode control. A segmented border represents contested control, while the small
  badges within a card represent disclosed faction presences and may therefore appear together.
  Control itself is always Imperial, contested, or enemy; uncertainty remains an intelligence state
  inside a card rather than an unknown territorial-control category.
- Normal order assignment is mission-scoped: only unassigned squads, plus squads already assigned
  to the selected order, in the target and adjacent regions appear. Orbiting forces are excluded
  until they complete the separate Land Forces flow.
- Enemy strength uses the existing descriptive vocabulary. Each disclosed faction or alliance owns
  its own Entrenchment, Listening Post, and Anti-Air values rather than inheriting an ambiguous
  region-wide fortification rating.
- Special-mission cards pair an icon with a text descriptor and identify the target faction. Ambushes
  also disclose approximate target strength, while rewards appear only on Governor's Requests.
- Selection is source-specific: Land Forces presents a flat orbital list; normal assignment presents
  only eligible nearby surface squads; Embark Forces presents squads in the selected source region
  plus capacity-aware destination ships. These sources never form one active selection.
- Regional Operations exposes existing assignments and ordinary available orders alongside special
  missions. Active orders have selection-aware Add Squad(s) and Cancel Order actions; Recon, Defend,
  Patrol, Attack, Move, Diversion, Entrenchment, Listening Post, and Anti-Air remain directly
  assignable.
- Orbiting squads form a flat list; ship identity is secondary metadata rather than the primary
  organization. `Land Squad(s)` is the direct movement action. The shared order composer explicitly
  creates ordinary orders or assigns existing special missions, while active orders explicitly add
  the current selection. Landing completes before those squads enter mission eligibility.
- Selecting surface squads changes the movement composer to `Embark Squad(s)` and presents eligible
  destination ships with before/after capacity. Normal and special order choices feed one shared
  Order Assignment composer placed below both catalogs.
- World Dossier unifies Imperial territorial control with Astartes presence, uses the existing broad
  enemy-strength descriptors without pseudo-precision, and replaces nonzero-region fortification
  counts with faction-selectable map overlays and exact selected-region defense values.
- The selected-region dossier contains the actionable information directly, so the ambiguous
  `Open Detail` escape hatch and the unnecessary capability checklist are absent.

## Recovery Operations — Injury Logistics

Current iteration: `recovery_operations_injury_logistics_v3.png`

- Rank-badge casualty rows reuse the Chapter Muster's compact soldier language and replace honors with
  wound severity, recovery time, and missing local-care capabilities. The queue can sort by severity,
  recovery time, squad, or location.
- The selected-soldier summary lists every injury instead of repeating squad and location data. Its
  deliberately simple body schematic assigns every visible segment to a named hit location—shoulder,
  arm, and hand share one color—then colors healthy locations green and wounds on a yellow-to-red
  severity scale. The adjacent squad status is factual rather than a readiness score.
- A branching Care Pathway selects among zero, one, or many eligible treatment destinations, then
  chooses whether to detach the casualty or relocate the whole squad. Later action cards recompute
  from those decisions.
- Temporary medical detachment keeps the soldier on the squad roll but removes him from its order and
  deployable strength until treatment, recovery, and reunion, allowing the rest of the squad to act.
- The staged Recovery Plan exposes patient movement, capacity, staff, treatment, time, and cost in one
  workspace without repeating the same dependencies in a second summary chain.

Earlier raster iterations remain in Exploration as superseded concepts until their respective
directions are selected or revised.

## Shared direction

All three concepts use the current Apothecarium baseline as their stylistic reference: matte-black
panels, thin antique-gold rules, warm ivory headings, and restrained semantic state colors. They also
share three interaction ideas that require further design before implementation:

1. rich force rows with effective/full strength, casualties, leadership, location, and commitment;
2. early disclosure and inline resolution of cross-system constraints; and
3. staged multi-action plans with review, undo, and explicit confirmation.

The mockups were generated with the built-in image-generation tool from three separate high-fidelity
UI prompt briefs: a Chapter reorganization workspace, a unified planetary operations workspace, and
an actionable casualty recovery workspace. Each prompt specified a 16:9 desktop strategy interface,
the established project palette and panel language, the relevant workflow data, and the requirement
that the common workflow be completable without an implied screen change.
