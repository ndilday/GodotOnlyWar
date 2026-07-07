# Opening Scenario — "Promised World"

Detailed design for the **Game Start Phase 2** item in the PRD (§5.3). Replaces the
current "name your chapter, pick a seed, get dropped into a sandbox" launch with a framed,
*in-media-res* opening that hands the new player one concrete objective — liberate an
invaded world — whose reward is the thing the rest of the game is built around: their
**Chapter World**.

## Decisions (locked)

| Question | Decision |
|---|---|
| Who makes the promise? | The **Sector Lord** — the governor of the sector capital, the ranking secular Imperial authority in the sector (a real, persistent character, not an invented free-floater, not "the Imperium"). Requires a small **governance-hierarchy** addition to track who that is — see §2.3 / §3.4. |
| Failure / abandonment handling? | **Promise lapses + reputation hit.** If the world is overrun (or ignored until overrun), the promise is withdrawn — lost world, damaged standing with the Sector Lord — and the game continues as a sandbox. |
| Starting posture? | **Fleet in orbit, player must land.** Squads start embarked; the first action the player takes is landing into the beachhead. Teaches the landing UI immediately. |

> **Authority refinement (post-initial-design).** The promise was originally scoped to an
> *invented* "Crusade commander" Character. It is now anchored to the **Sector Lord**: the
> governor of the sector capital is the natural person to gift a reconquered frontier world,
> and they already exist in the simulation. This swaps "generate a free-standing authority"
> (old §3.4) for "resolve the sitting Sector Lord," and adds the governance-hierarchy data in
> §2.3. The reputation relationship follows the **seat** (derived each time), not a stored
> character id, so it survives the lord's death/succession.

These three reshape several sections below; they are baked into the design rather than left open.

## Current state — what already exists

`SectorBuilder.FoundTakebackPlanet` ([Builders/SectorBuilder.cs:99](../Builders/SectorBuilder.cs)) is a
rough prototype of this scenario, already wired into `GenerateSector`:

- Picks the **lowest-population enemy planet** with `Population >= 16000`.
- Converts that planet's **weakest region** to the player faction.
- **Lands all squads** into that region (squads get `CurrentRegion` set).
- Parks the fleet in orbit.
- Fortifies every *other* region (sets `Organization/IsPublic/Entrenchment/Detection/AntiAir = 1`, `Garrison = Population`).

It has no briefing, no narrative, no balance tuning, no failure handling, and it starts the
player **already landed** and stamps the world as **mostly enemy**. The scenario work
**inverts and formalizes** this: a mostly-Imperial world with Tyranids **confined to a few
regions**, fleet in orbit, plus the briefing / narrative / objective / failure layers.

`FoundTakebackPlanet` is replaced wholesale (see §3). `ReplaceChapterPlanetFaction`
is retained for the *reward* path (granting the world on victory, §6); the old
`FoundChapterPlanet` and `PlaceStartingForces` helpers were deleted.

## 1. Architecture overview

The scenario is an **override layer**, not a fork of the generator. `GenerateSector` runs
its normal deterministic generation, then a single new entry point stamps the scenario on
top:

```
GenerateSector(seed, …)
  ├─ RNG.Reset(seed)
  ├─ normal planet/character/sector generation   (unchanged)
  ├─ NewChapterBuilder.CreateChapter(…)            (unchanged)
  └─ ScenarioBuilder.StampPromisedWorld(sector, data, currentDate, …)   ← replaces FoundTakebackPlanet
         ├─ select promised planet (deterministic; rimward — §3.1)
         ├─ seed + strengthen + REVEAL the Genestealer Cult (the psychic beacon — §3.1a)
         ├─ pre-landing sim: cult war weakens the PDF (SimulatePlanetForward — PRD §4.24)
         ├─ stamp the authored Tyranid beachhead onto the now-evolved board (§3.2)
         ├─ post-landing sim: the stranded swarm feeds/spreads a Gaussian stretch (PRD §4.24)
         ├─ place fleet in orbit (NOT landed)
         ├─ resolve the Sector Lord (governor of the sector capital) as the promising authority
         ├─ compose the briefing narrative + founding-history entry
         └─ attach CampaignScenario to the sector
```

> **Flow note.** The bullet order above (and §3's temporal sequencing) is the current,
> implemented flow — the opening plays out as a timed sequence during generation (PRD §4.24,
> "Opening Scenario Application"), not a single static stamp. Several §3 sub-sections below were
> authored against the earlier static-stamp design and describe the individual *stamp* steps
> (selection, Tyranid presence, fleet, authority); they remain accurate for those steps, but the
> **sequencing** that wraps them lives in PRD §4.24 and the inline "Implemented" notes.

All scenario randomness draws from the **already-seeded `RNG` stream** so that
`seed + scenario` reproduces the same opening (determinism requirement). No `GD.Rand*`.

A new persistent `CampaignScenario` object carries the live objective state through the
campaign and across save/load. When it is `null` (legacy saves, or a future "plain
sandbox" mode) the game behaves exactly as today.

## 2. Data-model changes

### 2.1 `CampaignScenario` (new) — `Models/CampaignScenario.cs`

```csharp
public enum ScenarioType { None = 0, PromisedWorld = 1 }

public enum ObjectiveState { Pending = 0, Won = 1, Lapsed = 2 }

public class CampaignScenario
{
    public ScenarioType Type { get; }
    public int PromisedPlanetId { get; }
    public ObjectiveState State { get; set; }
    public bool BriefingAcknowledged { get; set; }     // one-shot guard for the popup
    public string BriefingText { get; }                // composed once at generation, persisted
    public int OriginalAuthorityCharacterId { get; }   // the Sector Lord who first promised (flavor/continuity)
}
```

Attached to `Sector` as a nullable property (`Sector.Scenario`). Persisted (§7).

`BriefingText` is composed **once** at generation and stored, so the popup, the Chapter
screen, and any later recap all read the same authored text and it survives reload.

**Why no live `AuthorityCharacterId`.** The promise follows the **seat**, not the person.
The mechanically-relevant authority is always the *current* Sector Lord, resolved on demand
via `Sector.GetSectorLord()` (§2.3) — so the reputation lever (§6.2) moves on whoever holds
the seat now, and the lord's death/succession is handled for free. `OriginalAuthorityCharacterId`
is kept only for narrative continuity (e.g. "the lord who promised you this world is dead");
it may dangle harmlessly if that character is later removed.

### 2.2 `RegionFaction.GrowthMultiplier` (new field)

A per-`RegionFaction` `float GrowthMultiplier` (default `1.0`), applied multiplicatively to
**organic** population growth (the `Logistic`/baseline branches only).

> **Superseded for Tyranids (PRD §4.24).** The original design throttled the stamped Tyranid
> regions with this multiplier. Tyranids are now a **`Consumption`** faction with *no organic
> birthrate* — they grow only by eating biomass (Predate/Consume), which does not read
> `GrowthMultiplier` — so the scenario no longer sets it on Tyranid regions, and winnability
> comes from the **finite stranded-biomass budget**, not a growth throttle. `GrowthMultiplier`
> remains as a general primitive for organically-growing factions (the post-0.7 Ork "feral
> efficiency penalty" (§4.22) and revolt tuning). Keeping it on `RegionFaction` means the turn
> loop never has to be scenario-aware.

```csharp
// RegionFaction.cs
public float GrowthMultiplier { get; set; } = 1.0f;
```

### 2.3 Governance hierarchy — Sector Lord & subsector governors (new)

The promise needs a *named, persistent* authority. The right one is the **Sector Lord** (the
governor of the sector capital), but nothing tracks that today. Capital selection exists only
as a private, **topology-only** helper in `WarpLaneBuilder.SelectCapital`
([Builders/WarpLaneBuilder.cs:49](../Builders/WarpLaneBuilder.cs)) — it picks the highest-
`Population` planet *of any faction* purely to anchor warp lanes, stores it nowhere, and is
not governance-aware (the warp capital can be an enemy world with no governor).

Add a small, **derived** governance layer (no fragile new persistence — see below):

**Governance seat vs. warp capital.** Keep these distinct but usually-coincident:
- *Warp capital* (existing, unchanged): highest population, any faction, for lane topology.
- *Governance seat* (new): the highest-`Importance` **Imperial-controlled** planet in a
  subsector — the one that actually has a governor (`PlanetFaction.Leader`). This filter is
  what prevents an enemy-held warp capital from being treated as a seat of government.

**Model additions:**

```csharp
// Planet.cs — queryable rank of the governor seated here.
public enum GovernanceTier { Planetary = 0, SubsectorCapital = 1, SectorCapital = 2 }
public GovernanceTier GovernanceTier { get; set; } = GovernanceTier.Planetary;

// Subsector.cs — the subsector's seat of government (Imperial governance capital).
public Planet GovernanceSeat { get; set; }   // null if the subsector has no Imperial world

// Sector.cs — convenience resolvers over the derived designation.
public Planet GetSectorCapital();            // the SectorCapital-tier planet
public Character GetSectorLord();            // GetSectorCapital()?.Governor  (its PlanetFaction.Leader)
public Character GetSubsectorGovernor(Subsector s); // s.GovernanceSeat?.Governor
```

(`Planet` already exposes the controlling `PlanetFaction` and its `Leader`; a thin
`Planet.Governor` accessor returning that leader is a convenient add.)

**Where it's computed.** Fold a governance pass into the existing
`SectorBuilder.GenerateWarpNetwork` (which already runs on both new-game and load, right after
subsectors are built). For each subsector, pick the governance seat and tag its planet
`SubsectorCapital`; then pick the top seat sector-wide and promote it to `SectorCapital`.
`WarpLaneBuilder` keeps its own topology capital — or, optionally, is refactored to read
`Subsector.GovernanceSeat` when that seat exists and fall back to population otherwise.

> **Implemented (step 1a).** Landed as `SectorBuilder.AssignGovernance`, invoked at the end of
> `GenerateWarpNetwork`. Seat selection orders Imperial-controlled worlds by `Importance`, then
> `Population`, then `Id` as deterministic tie-breaks (the design specifies Importance only; the
> Population/Id tie-breaks were added so a seed reproduces an identical, single seat). The same
> ordering promotes the top subsector seat to `SectorCapital`, so exactly one capital is tagged.
> The pass first resets every planet to `Planetary` so reruns (load / future end-of-turn
> refresh) re-derive cleanly. `WarpLaneBuilder` was left unchanged (its population-based warp
> capital is intentionally distinct from the governance seat). `Planet.Governor` resolves the
> controlling `PlanetFaction.Leader` via the existing `GetControllingFaction` extension.

**Persistence stance — recompute, don't store.** `GovernanceTier` / `GovernanceSeat` are
deterministic functions of (population, importance, control), all of which are already
persisted on the planets. So they are **recomputed at build/load** alongside subsectors and
warp lanes, not saved — the same pattern the warp network already uses. Populations drift over
a campaign, so the designation is refreshed at the warp-network rebuild cadence (load/new
game) and **optionally** at end-of-turn; intra-session staleness is acceptable for 0.7 because
capital-tier worlds (hive-scale populations) effectively never change rank. The governor
**characters** themselves are already persisted as planet leaders, so "who is the Sector Lord"
round-trips correctly without any new save data.

**Title.** The briefing needs a title ("Lord of the Sector", "Sector Governor", a Crusade-era
"Lord Commander", …). Derive it from `GovernanceTier` at compose time rather than storing a
field; a hand-authored `Character.Title` can be added later if per-character titles are wanted.

This governance layer is **independently useful** beyond the scenario: it gives the Planet
Detail / Galaxy screens a "sector capital / subsector capital" marker, gives §4.16 governor
death-chance a cleaner "higher-importance world" signal, and gives §6.5 Inquisition/authority
requests a ranked set of issuers. **It is folded into the Opening Scenario work** (PRD §5.3,
implementation step 1a) rather than tracked as a separate line item; the scenario itself
strictly needs only `GetSectorLord()`, but the full designation is cheap and lands together.

## 3. Sector generation — `ScenarioBuilder.StampPromisedWorld`

New `Builders/ScenarioBuilder.cs`. Replaces `FoundTakebackPlanet`. Signature roughly:

```csharp
internal static CampaignScenario StampPromisedWorld(
    Sector sector, GameRulesData data, Date currentDate,
    PlayerForce playerForce, List<Planet> planetList,
    List<Character> characterList, List<TaskForce> forceList);
```

> **Implemented (step 2).** Landed as `Builders/ScenarioBuilder.cs`, with constants in
> `Helpers/ScenarioRules.cs`. Deviations from the sketch above, all minor:
> - **Call order / signature.** `GenerateSector` now builds the `Sector` and runs
>   `GenerateWarpNetwork` (which assigns governance) *before* stamping, because
>   `StampPromisedWorld` resolves `GetSectorLord()`. The final `forceList` parameter was
>   dropped: by the time the scenario runs the sector already exists, so the orbiting fleet is
>   registered via `Sector.AddNewFleet` rather than appended to a list the `Sector` constructor
>   consumes. `FoundTakebackPlanet`, `FoundChapterPlanet`, and `PlaceStartingForces`
>   were deleted; `ReplaceChapterPlanetFaction` is retained for the reward path (§6).
> - **Selection (§3.1).** Eligible = default-faction, `GovernanceTier == Planetary` (excludes
>   sub/sector capitals), population in `[5M, 500M]`; the world **nearest the sector edge**
>   (`EdgeDistance`, then population then id as tie-breaks) is chosen — rimward frontier. Widen-band
>   and lowest-population-enemy fallbacks are in place. Selection is order-based (no RNG draw), so it
>   is stable per seed.
> - **Stamp (§3.2).** `N = 2–3` regions chosen as a contiguous run from an RNG start index
>   (mod 16). The displaced Imperial remnant is set **non-public** (`IsPublic = false`, garrison
>   zeroed, population scaled by `ImperialRemnantFraction`) so the region resolves to single
>   Tyranid control — leaving it public would give the region two public factions and a null
>   `ControllingFaction`. *(Tyranid regions no longer get a `GrowthMultiplier` throttle: Tyranids
>   are a `Consumption` faction (PRD §4.24) with no organic growth, so it did nothing — see §2.2/§8.)*
> - **Briefing.** Now composed through `BriefingComposer` with a "The Promised World"
>   founding-history entry (see the step 3 note in §4); `currentDate` is threaded through for the
>   history timestamp. (Originally a plain-facts placeholder deferred to the following session.)

### 3.1 Select the promised world (deterministic)

The promised world is **Imperial-habitable but invaded**. We pick a **default-faction
(Imperial) world** and stamp Tyranids onto it, rather than picking an enemy world. Selection
rules (all on the seeded `RNG` stream):

- Eligible = default-faction controlled, `Population` in a tuned band (big enough to be a
  worthwhile reward, small enough to be a *starter* — not a hive world). Proposed band:
  `[~5M, ~500M]`, to be tuned. Exclude subsector capitals (too central / too large for a
  first objective).
- Among eligible worlds, pick deterministically: **nearest the sector edge** (Chebyshev
  `EdgeDistance`), with population then id as tie-breaks. The opening invasion sits on the
  frontier — a rimward incursion the over-stretched Imperium can't spare a regiment for — which
  also keeps the first objective off the populous sector core. Stable for a given seed.
  *(Earlier drafts chose the world at index `count/3` of a population-ordered list; the rimward
  rule replaced it.)*
- Fallback: if none qualify, widen the band; ultimate fallback re-uses the old
  "lowest-population enemy world" rule so generation can never fail.

### 3.1a The Genestealer Cult beacon + pre-landing sim (temporal sequencing)

> This section and the pre/post-landing simulation are the **temporal-sequencing** rewrite (PRD
> §4.24, "Opening Scenario Application"). The opening is not a single static stamp: it plays out as
> a short headless simulation *during generation* so the world the player inherits is emergent —
> sometimes a fresh beachhead, sometimes a month-eaten ruin — from the same sim that runs in play.
> All draws come from the seeded `RNG`, so `seed + scenario` still reproduces the same opening.

Canon: a Tyranid invasion is *drawn in* by a Genestealer Cult whose psychic beacon calls the hive
fleet down. So the promised world must harbour a cult, and the sequence is:

1. **Ensure a cult** (`EnsureGenestealerCult`) — if planet gen didn't already roll one (~2%), seed
   one with the same infiltration logic. Runs before the beachhead so the cult carves its numbers
   out of the *intact* Imperial regions.
2. **Strengthen to landing-site strength** (`StrengthenPromisedWorldCult`) — pull the cult up to
   `ScenarioRules.PromisedWorldCultStrengthFraction` (0.10) of each region's combined (cult +
   Imperial) population/garrison, carving the increase out of the Imperial owner. This is the deep
   infiltration that hollowed out the PDF and drew the swarm.
3. **Reveal** (`RevealGenestealerCult`) — flip the cult `PlanetFaction` and its `RegionFaction`s
   public (the beacon lit), mirroring `CheckForPlanetaryRevolt`. On reveal, a `Conversion` faction
   **stops converting/growing** (PRD §4.24 — proselytizing is clandestine; open warfare ends it).
4. **Seed insider intel** (`SeedPromisedWorldCultIntel`) — give the cult strong per-region belief
   (`PromisedWorldCultStartingIntel`) about every public non-cult force, so its opening decisions
   model an insider revolt, not a blind invader.
5. **Pre-landing sim** — `TurnController.SimulatePlanetForward(sector, promised, ScenarioRules.PreLandingTurns)`
   runs a planet-scoped slice of the weekly turn so the cult uprising fights the PDF and weakens the
   defenders the Tyranids will land into.
6. Then **§3.2 stamps the authored beachhead** onto the now-evolved board, followed by a
   **post-landing sim** of `max(0, round(PostLandingTurnsMean + z))` weeks (`z ~ N(0,1)`) — the
   stranded swarm feeds and spreads before the player arrives (§8).

> **Pre-landing cult war (now active).** An earlier build left this inert (the revealed cult sat
> at `Organization = -1` and end-of-turn revolt-suppression re-hid it before it fought). That is
> fixed: `RevealGenestealerCult` now mobilizes each revealed cell to `Organization = 100` (full
> mobilization on the 0-100 scale), and
> `StrengthenPromisedWorldCult` pulls the cult to `PromisedWorldCultStrengthFraction` (0.10) of the
> populace — and since a cult's `MilitaryStrength` is measured by **population** (`PopulationIsMilitary`),
> not its vestigial garrison (PRD §4.24), the strengthened cult dwarfs the PDF, so suppression
> cannot re-hide it. The cult now actually grinds the PDF during the pre-landing sim. Exact
> organization/strength values remain playtest-pending tuning, not a structural gap.

### 3.2 Stamp Tyranid presence onto a few regions

For the chosen planet:

- Ensure a **Tyranid `PlanetFaction`** exists on the planet (`data.SectorFactions.Invader`,
  resolved as "Tyranids" — see `SectorGenerationFactions`). Mark `IsPublic = true` (the
  Navy already identified the incursion; the world is *known* to be invaded).
- Pick **N regions** to host Tyranids — proposed `N = 2–3`, deterministic, chosen as a
  contiguous cluster if region adjacency is available, else random distinct regions. The
  **rest of the world stays default-Imperial.**
- In each stamped region:
  - Add a Tyranid `RegionFaction` with `IsPublic = true`, a tuned starting `Population` and
    `Garrison` (the **load-bearing balance number**, §8), `Organization = 100` (fully mobilized
    on the 0-100 scale — the whole swarm feeds and fights), and
    `Entrenchment/Detection/AntiAir` low (these are raiders, not dug-in defenders). *(No
    `GrowthMultiplier` throttle: Tyranids are a `Consumption` faction — PRD §4.24 — and grow
    only by eating biomass, so the throttle would do nothing; see §2.2.)*
  - Reduce/clear the Imperial `RegionFaction` in that region (the Tyranids have overrun the
    local population). The Imperial civilian remnant may be left at a token level or zeroed;
    proposal: zero the Imperial garrison, keep a small displaced civilian population so the
    region still reads as "contested/overrun" rather than empty.
- Leave the planet's **governor** in place (normal Imperial planet gen already produced one).
  Optionally seed an **initial governor request** on this world (PRD §4.16) so the
  request loop is visible during the very first objective — see §9 (optional).

The "spread / pressure" the PRD asks for needs **no new mechanic**: the `Consumption` biomass
model (PRD §4.24) makes the stranded swarm push into adjacent regions on its own once a region's
biomass is exhausted (forced expansion). What keeps that pressure winnable rather than runaway is
the **finite biomass budget** — the swarm cannot grow without a fresh food source — not a growth
throttle.

### 3.3 Place the chapter in orbit (not landed)

Per the locked decision:

- Set each player `TaskForce.Planet`/`Position` to the promised planet and add to `forceList`
  (as today).
- **Do not** set `squad.CurrentRegion` and **do not** add squads to any `LandedSquads` list.
  Squads remain embarked; the player's first action is to land them via the Planet Tactical
  screen. This is the inversion of the prototype's `LandedSquads.AddRange(...)`.

### 3.4 Resolve the promising authority (the Sector Lord)

No character is *created*. The authority is the sitting **Sector Lord**, resolved from the
governance layer (§2.3) which `GenerateWarpNetwork` has already established by this point:

- `Character authority = sector.GetSectorLord();` — the governor of the `SectorCapital`-tier
  planet. This is an already-generated, already-persisted planet leader.
- Record `authority.Id` on `CampaignScenario.OriginalAuthorityCharacterId` (narrative
  continuity only; mechanics resolve the *current* lord on demand — §2.1, §6.2).
- Edge case: if no Imperial sector capital exists (a sector with no qualifying Imperial
  world — rare, but possible with adverse generation), fall back to the highest-importance
  Imperial world overall; if even that is absent, fall back to generating a free-standing
  Crusade-commander `Character` (the original design) so the scenario can never fail to have
  an authority. This fallback is the *only* path that creates a new character.
- The authority's `OpinionOfPlayerForce` is the reputation lever moved on win/lapse, but it is
  re-resolved at resolution time via `GetSectorLord()` rather than cached (§6.2).

## 4. Narrative composer (minimal — not the full §4.19 system)

There is **no narrative text system yet**; today's "founding myth" is hardcoded strings in
`NewChapterBuilder` ([Builders/NewChapterBuilder.cs:47](../Builders/NewChapterBuilder.cs))
written to `BattleHistory`. So "generate through the narrative text system, not hardcoded
prose" is satisfied for 0.7 by a **small token-substitution composer**, explicitly a
placeholder for the eventual §4.19 narrator.

New `Helpers/Narrative/BriefingComposer.cs`:

```csharp
public static string ComposePromisedWorldBriefing(BriefingTokens tokens);
```

`BriefingTokens` carries: chapter name, planet name, subsector/sector name, authority
name + title, and the controlling-enemy name ("Tyranids"). The **authority name + title**
come from the resolved Sector Lord (§3.4): the name from the `Character`, the title derived
from the governing planet's `GovernanceTier` ("Lord of the Sector" for the sector capital).
The composer fills one of a
**small set of templated paragraphs** chosen deterministically from the seed (gives run-to-run
variety without authoring a system). Example skeleton:

> Brothers of the **{ChapterName}**, your Chapter is born into war. The world of
> **{PlanetName}**, in the **{SubsectorName}**, has been bled by a Tyranid splinter — its
> defenders broken, its skies cleared by the Navy, but no Guard regiment can be spared to
> retake the ground. **{AuthorityTitle} {AuthorityName}** has marked it for you: take
> {PlanetName} from the swarm, and it is yours — your Chapter World, in the Emperor's name.

Outputs:
1. The `CampaignScenario.BriefingText` (shown in the popup, §5).
2. A founding-history `EventHistory` entry appended via
   `playerForce.AddToBattleHistory(date, "The Promised World", [...])`, so the objective is
   recorded alongside the existing "Chapter Founding" entry and visible on the Chapter screen.

**Token sourcing notes.** The sector has no name today, and subsectors are identified only by
their capital. For tokens: name the subsector after its capital ("the **{Capital} Reaches**")
or add a light sector/subsector name generator. Minimal path: derive `SubsectorName` from the
capital planet's name; flag a proper sector-naming pass as a small follow-up.

> **Implemented (step 3).** Landed as `Helpers/Narrative/BriefingComposer.cs`
> (`ComposePromisedWorldBriefing(BriefingTokens)`), wired into `ScenarioBuilder.StampPromisedWorld`
> in place of the plain-facts placeholder. Deviations, all minor:
> - **Tokens.** A `BriefingTokens` readonly struct carries the resolved strings (chapter, planet,
>   subsector, authority name + title, enemy) plus an `int TemplateSelector`. The composer picks one
>   of **three** hand-authored BBCode templates via `selector mod 3` (non-negative), so it is a pure
>   function of its tokens — deterministic and unit-testable without RNG. The caller passes the
>   promised planet's `Id` as the selector (deterministic per seed), rather than drawing a fresh RNG
>   value, so `seed + scenario` reproduces the same briefing.
> - **Title.** Derived from the seat's `GovernanceTier` via `BriefingComposer.GetAuthorityTitle`
>   ("Lord of the Sector" / "Lord of the Subsector" / "Planetary Governor"). The rare free-standing
>   fallback commander (§3.4) is titled as `SectorCapital` since no seated rank exists to read.
> - **Subsector name.** Subsectors carry only an id-string name today, so the token is sourced from
>   the subsector's `GovernanceSeat` (capital) as "**{Capital} Subsector**", falling back to the
>   id-name then the planet name. A proper sector/subsector naming pass remains a follow-up.
> - **Founding history.** A matching `EventHistory` titled **"The Promised World"** is appended via
>   `playerForce.AddToBattleHistory(currentDate, …)`, so the objective sits beside "Chapter Founding"
>   on the Chapter screen.

## 5. Briefing pop-up

A new dialog shown **once**, on first entry into the main game after a *new* game (never on
load, never twice).

- Scene `Scenes/MainGameScreen/briefing_dialog.tscn` + controller, following the existing
  `DialogController`/`DialogView` and `EndOfTurnDialog` patterns
  ([Scenes/DialogController.cs](../Scenes/DialogController.cs),
  [Scenes/EndOfTurnDialogController.cs](../Scenes/EndOfTurnDialogController.cs)). A
  `RichTextLabel` renders `CampaignScenario.BriefingText`; a single **"For the Emperor"**
  acknowledge button closes it.
- Shown from `MainGameScene._Ready` ([Scenes/MainGameScreen/MainGameScene.cs:42](../Scenes/MainGameScreen/MainGameScene.cs)):

```csharp
var scenario = GameDataSingleton.Instance.Sector.Scenario;
if (scenario is { State: ObjectiveState.Pending, BriefingAcknowledged: false })
    ShowBriefingDialog(scenario);   // on dismiss: scenario.BriefingAcknowledged = true
```

`BriefingAcknowledged` is persisted, so the one-shot guard survives reload without a separate
"is new game" flag: a freshly stamped scenario has it `false`; dismissing sets it `true`; a
loaded game already has it `true`.

> **Implemented (step 4).** Landed as `Scenes/MainGameScreen/briefing_dialog.tscn` with
> `BriefingDialogController` / `BriefingDialogView` (following the `DialogController`/`DialogView` /
> `EndOfTurnDialog` pattern). A BBCode `RichTextLabel` in a `ScrollContainer` renders
> `CampaignScenario.BriefingText`; the single acknowledge button reuses the base `Dialog` close
> button, relabelled **"For the Emperor"** and repositioned bottom-centre, so its press flows through
> the existing `CloseButtonPressed` event. `MainGameScene._Ready` shows it once when
> `Scenario is { State: Pending, BriefingAcknowledged: false }`; `OnBriefingDialogClosed` sets
> `BriefingAcknowledged = true` (persisted on the next save) and hides the dialog. Verified by a
> headless scene-load smoke test (node paths resolve, `SetBriefing` updates the label) and the
> save/load round-trip test that asserts `BriefingAcknowledged`/`BriefingText` survive reload.

## 6. Turn-loop integration

### 6.1 The `GrowthMultiplier` primitive (not a Tyranid throttle)

`TurnController.EndOfTurnRegionFactionsUpdate` multiplies **organic** growth by
`regionFaction.GrowthMultiplier`:

```csharp
case GrowthType.Logistic:
    newPop = ApplyCarryingCapacity(
        regionFaction.Population * LogisticGrowthRate * regionFaction.GrowthMultiplier,
        regionFaction.Region);
    break;
```

(and the `default`/baseline branch likewise). Because the multiplier defaults to `1.0`, every
region is unaffected unless something sets it, which keeps the turn loop scenario-agnostic.

> **The scenario does *not* use this to pace the Tyranids.** Tyranids are a `Consumption` faction
> with no organic birthrate (PRD §4.24), so they never touch this branch and `GrowthMultiplier`
> does nothing to them. The lever survives only as a **general primitive** for organically-growing
> factions (future Ork/revolt tuning). Tyranid pacing comes from the finite stranded-biomass budget
> (§8), and the `Consumption` swarm reaches fresh biomass via its own forced-expansion step, not
> via NPC offensive orders as the earlier draft assumed.

### 6.2 Resolve the objective (new `ProcessScenario` step)

A new step at the end of `ProcessTurn` (or invoked from `MainGameScene.OnEndTurnButtonPressed`
after `ProcessTurn`). Operates only when `sector.Scenario is { State: Pending }`:

- **Win** — the promised planet has **no remaining enemy presence of any kind** — the world is
  fully back in Imperial/player hands. `HasEnemyPresence` checks every region for any faction that
  is *neither the default (Imperial) nor the player* holding `Population > 0` / `Garrison > 0`, so
  it covers the **Tyranid swarm and the revealed Genestealer Cult (§3.1a) alike**. Driving out the
  swarm while a cult still holds ground is *not* liberation and leaves the objective `Pending`.
  - `scenario.State = Won`.
  - **Grant the Chapter World**: use `ReplaceChapterPlanetFaction` to install a player
    `PlanetFaction` (`PlayerReputation = 1`, `IsPublic = true`) across the planet — this is
    the reward path once covered by the old `FoundChapterPlanet` prototype. (Because win now
    requires *all* enemies cleared, no cult can survive into the grant.)
  - Move the **current Sector Lord's** `OpinionOfPlayerForce` **up** (promise honored),
    resolved via `sector.GetSectorLord()` at this moment — so the credit lands with whoever
    holds the seat now, even if the original promiser has died.
  - Surface a victory notification (reuse a dialog; or fold into the end-of-turn dialog).

- **Lapse** — the promised planet is **fully overrun** (no Imperial *and* no player presence
  remains; Tyranids hold every region):
  - `scenario.State = Lapsed`.
  - Promise withdrawn — **no** Chapter World granted.
  - **Current Sector Lord's** `OpinionOfPlayerForce` moves **down** (reputation hit), resolved
    via `sector.GetSectorLord()`. If the seat is currently vacant (sector capital itself fell),
    the reputation effect is a no-op for this pass — the lapse still resolves.
  - Surface a "the world is lost" notification.
  - Game continues as a normal sandbox; the scenario object stays for record/history.

The finite biomass budget (§8) plus the fact that the sim never pauses means liberation is
intended to take **~6–12 turns**, not one — which is exactly what gives the rest of the sector
time to generate governor/Inquisition requests ("let the sector simulate forward during liberation").
No artificial early-game lull is needed because nothing is gated; the pacing comes from the
balance numbers.

There is no global chapter-reputation scalar in 0.7, so reputation lives on the Sector Lord
`Character` (the governing seat) for now. A sector-wide reputation value is post-0.7 (and
aligns with §6.5 Inquisition consequences).

> **Implemented (step 5).** The throttle (§6.1) is applied in
> `TurnController.EndOfTurnRegionFactionsUpdate`: the `Logistic` and baseline branches now multiply
> the organic delta by `regionFaction.GrowthMultiplier` (`Conversion` is left unthrottled — it is
> not organic growth). `ProcessScenario` (§6.2) runs as a new final phase of `ProcessTurn` and is
> also `public` so it is unit-testable in isolation; it no-ops unless `Scenario is { State: Pending }`.
> Deviations, all minor:
> - **Win grant.** Repurposed `SectorBuilder.ReplaceChapterPlanetFaction` (made `internal`) installs
>   the player as the planet-wide controlling faction (`PlayerReputation = 1`, all regions public),
>   inheriting the displaced Imperial garrison/population region by region. It was hardened to resolve
>   the Imperial faction from the planet's faction map rather than via `GetControllingFaction`,
>   because a freshly-liberated world can have a cleared region with no public faction (the displaced
>   civilian remnant is non-public), which the old call would have thrown on.
> - **Opinion lever.** Win/lapse move `GetSectorLord().OpinionOfPlayerForce` by
>   `ScenarioRules.SectorLordOpinionReward` / `…Penalty` (±0.5, playtest starting points), resolved at
>   resolution time; a vacant seat (lapse where the capital itself fell) is a no-op that still resolves
>   the lapse.
> - **Notification.** Surfaced via `TurnController.ScenarioNotification` (a string set on resolution,
>   cleared each `ProcessTurn`). `MainGameScene.OnEndTurnButtonPressed` reads it and shows the message
>   by reusing the `briefing_dialog` scene on a separate instance (BBCode message + acknowledge), so
>   the one-shot opening-briefing guard is untouched.
> - **Turn-loop hardening (discovered).** Running a *real generated* sector forward (which the scenario
>   now requires every turn) exposed a pre-existing crash: `PlanetExtensions.GetControllingFaction`
>   dereferenced a null per-region controller whenever a region held ≠1 public faction (transiently
>   true once factions fight over a region). It now skips contested/vacated regions and falls back to a
>   public planet faction if none are cleanly controlled. This was never hit before because no test ran
>   a full generated sector through `ProcessTurn`; it is required for the scenario's "simulate forward"
>   loop to survive.

## 7. Persistence

New persisted state:

- **`CampaignScenario`** — `Type`, `PromisedPlanetId`, `State`, `BriefingAcknowledged`,
  `BriefingText`, `OriginalAuthorityCharacterId`. Store on the extended `GlobalData` row
  alongside `Requisition`/`GeneseedStockpile`/`GeneseedPurity` (same pattern as the
  Apothecary/Supply work), threaded through `GameStateDataAccess.SaveData`/`GetData` and the
  load path in `StartMenu.LoadGameData`.
- **`RegionFaction.GrowthMultiplier`** — add a column to the RegionFaction persistence in
  `PlanetDataAccess`; default `1.0` on read for legacy rows.
- **Governance designation (`Planet.GovernanceTier`, `Subsector.GovernanceSeat`)** — **not
  persisted.** Recomputed in `GenerateWarpNetwork` on both new-game and load, exactly like the
  subsector/warp-lane layer it sits beside (§2.3). The Sector Lord round-trips because the
  governor **characters** are already saved as planet leaders; only the *designation* is
  rederived.
- **Authority `Character`** — the Sector Lord is an ordinary persisted planet leader, so no
  special handling. The only planet-less character is the rare fallback commander (§3.4); if
  that path is exercised, verify the Characters round-trip does not assume a planet/
  `PlanetFaction` back-reference (flagged as a test, §11).

Legacy saves (no scenario row) load with `Sector.Scenario = null` and behave as today.

> **Implemented (step 6).** The `CampaignScenario` fields are appended to the single `GlobalData`
> row (`ScenarioType`, `ScenarioPromisedPlanetId`, `ScenarioState`, `ScenarioBriefingAcknowledged`,
> `ScenarioBriefingText`, `ScenarioOriginalAuthorityCharacterId`), threaded through
> `GameStateDataAccess.SaveData`/`GetData` and reattached in `StartMenu.LoadGameData`. "No scenario"
> is represented by `ScenarioType = None (0)`, which `GetData` maps to `Scenario = null`. A
> genuinely legacy DB that predates the columns is handled by a **column-count guard**
> (`reader.FieldCount`) in `GlobalDataAccess.GetGlobalData`, so it also loads as `null` rather than
> throwing. `RegionFaction.GrowthMultiplier` is an appended column with the same `FieldCount` guard
> in `PlanetDataAccess.PopulateRegionFactions`, defaulting legacy rows to `1.0`. The turn-loop
> *application* of `GrowthMultiplier` (§6.1) and `ProcessScenario` (§6.2) are out of scope for this
> session and not yet wired in.

## 8. Balance — the load-bearing numbers

Per the PRD this is *the* decision that determines whether the first objective is
"tense-and-winnable" vs. trivial or impossible. The knobs:

1. **Tyranid starting strength** on the promised world: starting `Garrison`/`Population`
   summed across the N stamped regions.
2. **Pre-arrival delay** (`PreLandingTurns` + the Gaussian `PostLandingTurns`): how long the
   `Consumption` swarm feeds and spreads before the player arrives, which sets both the swarm's
   size *and* how blighted the world is at game start (PRD §4.24).
3. **Consumption model rates** (biomass appetite, forced-expansion share, capacity recovery) —
   these live with the §4.24 model, not the scenario, and govern how fast the swarm grows and
   spreads on the finite biomass budget.

> **Not a growth throttle.** Earlier drafts of this section proposed a `GrowthMultiplier ≈
> 0.3–0.5` as the winnability lever. That was superseded when Tyranids became a `Consumption`
> faction (PRD §4.24): they have no organic birthrate, so a growth multiplier does nothing to
> them. Winnability is now structural — the swarm is stranded (no reinforcement) on a **finite
> biomass budget**, so it cannot grow without limit; the race is to break it before it has drawn
> down enough of the world. A fully idle player still loses the world (the lapse state) as the
> swarm eats through it.

Proposed starting point (to be playtested, **not** final):

- Keep the chapter at its full generated 1,000 brothers for 0.7 (the chapter-gen pipeline is
  complex; reducing starting strength is a larger change). "Understrength" is expressed
  instead through *materiel scarcity* — the chapter already starts with a finite Requisition
  pool (1,000) and a single task force, so it cannot brute-force everything at once.
- Size the total Tyranid garrison so that committing ~2–3 companies clears the stamped
  regions over **~6–12 turns** given travel/landing/among-region movement.

These belong in named constants (e.g. a `ScenarioRules` static, mirroring
`MedicalProcedureRules`/`GeneseedRules`) so they are tunable in one place, not scattered
literals. A later move into the rules DB is consistent with the PRD's "move tunable rules out
of code" direction.

> **Implemented (step 7) — partial; key finding below.** All knobs live in `ScenarioRules`.
> Opinion swings are `±0.5`. *(Historical: this step also set a `TyranidGrowthMultiplier = 0.4`
> throttle. That was later removed when the Tyranid faction became `Consumption` — PRD §4.24 —
> since consumption ignores `GrowthMultiplier`; see §2.2/§6.1/§8-intro.)* The load-bearing strength knob was
> **redesigned**: the original absolute constants (`TyranidRegionGarrison = 2,000`,
> `…Population = 50,000`) were replaced by a single **relative** `TyranidStrengthFraction = 0.5` —
> Tyranid per-region garrison/population is now `0.5 ×` the promised world's *average Imperial region*,
> measured before the stamp (`ScenarioBuilder.ScaledTyranidStrength`).
>
> **Why the redesign.** A headless forward-simulation of a real stamped sector (added as a throwaway
> diagnostic, since there is no Godot runtime here to playtest in-engine) showed the absolute constants
> were ~3 orders of magnitude too small: a representative promised world carried **~2.5M of Imperial
> PDF (~159k/region)**, against which a 2,000-strong Tyranid garrison is a rounding error — trivial for
> the player to clear and far too weak to ever press into an adjacent region. Sizing the Tyranids to the
> world's own PDF keeps the fight in the same ballpark across the entire `[5M, 500M]` band. The `0.5`
> fraction remains a **playtest starting point, not final** (as do the §4.24 consumption rates and
> the pre/post-landing delays, which now govern how fast the swarm grows and spreads).
>
> **What is *not* yet validated, and why.** The target outcomes — a clean ~6–12 turn win and an
> idle-player lapse — could **not** be confirmed empirically:
> 1. *Win window* needs the player's actual combat throughput against the stamped garrison, which means
>    issuing assault orders through the battle engine (or playing in-engine). No Godot runtime is
>    available in this environment, so the win pace is an estimate.
> 2. *Idle lapse* needs the Tyranids to **spread**, which routes NPC offensives through
>    `FactionStrategyController` → the battle engine. Forward-simulating that surfaced a **pre-existing
>    NRE** in the battle code (`BattleSquad.ShouldContinueMission`, reached via
>    `InfiltrateMissionStep.ShouldContinue` during `ProcessCombatMissions`) that crashes any headless
>    run once real battles occur. The scenario's *resolution* logic (§6.2 win/lapse, opinion, grant) is
>    fully unit-tested by forcing the board state directly; only its *spread trigger* is gated on that
>    battle-engine bug, which is **out of scope for this turn-loop session** and flagged as follow-up.
> Net: the mechanics are correct and green; the numeric tuning of the win/lapse *windows* awaits a
> playtest pass (and the battle-engine fix that unblocks automated forward-sim).

## 9. Optional polish (in-scope-if-cheap)

- **Seed an initial governor request** on the promised world at stamp time, so the governor
  request loop is concretely visible during the first objective (teaches §4.16 before the
  player understands it — a stated intent of the scenario). Uses the existing
  `RequestFactory`.
- **Reveal/known check**: confirm the sector map's name-display rule shows the promised world
  (and ideally its subsector) at start. No `IsKnown` field exists in the planet model today,
  so visibility is effectively global at the map level — verify and, if a known-set is later
  added, ensure the promised world and the chapter's orbit are seeded as known.

## 10. Implementation sequence

1. **Models** — `CampaignScenario` + enums; `Sector.Scenario`; `RegionFaction.GrowthMultiplier`;
   (optional) `Character.Title`.
1a. **Governance hierarchy** (§2.3) — `Planet.GovernanceTier` + `Planet.Governor`;
   `Subsector.GovernanceSeat`; `Sector.GetSectorCapital/GetSectorLord/GetSubsectorGovernor`;
   the governance pass in `GenerateWarpNetwork`. Landable independently and useful on its own;
   the scenario depends only on `GetSectorLord()`.
2. **Stamping** — `ScenarioBuilder.StampPromisedWorld`; remove `FoundTakebackPlanet` from
   `GenerateSector`; place fleet in orbit (not landed); resolve the Sector Lord as authority
   (with fallback); set tuned Tyranid strengths via a new `ScenarioRules`. *(No growth throttle:
   Tyranids grow by consumption — §2.2/§6.1.)*
3. **Narrative** — `BriefingComposer` + token sourcing (subsector naming); founding-history
   entry; store `BriefingText` on the scenario.
4. **Briefing UI** — `briefing_dialog` scene/controller; one-shot hook in `MainGameScene._Ready`.
5. **Turn-loop** *(done)* — apply `GrowthMultiplier` in growth; `ProcessScenario` for win (grant world +
   rep up) and lapse (withdraw + rep down) + notifications. (Plus a required hardening of
   `GetControllingFaction` so the loop survives forward simulation — see the step 5 note in §6.)
6. **Persistence** — scenario fields on `GlobalData`; `GrowthMultiplier` column; verify
   planet-less authority `Character` round-trips.
7. **Balance pass** *(partial — mechanics done; numbers are starting points)* — tunables in
   `ScenarioRules`; strength redesigned to scale with the world's PDF. The ~6–12 turn win and
   idle-player lapse *windows* are not empirically validated (no Godot runtime; a pre-existing
   battle-engine NRE blocks headless forward-sim of combat). See the step 7 note in §8.

Steps 1–4 are independent enough to land before 5; 6 must accompany whatever state 1/2/3 add.
**Status: steps 1, 1a, 2, 3, 4, 5, 6 implemented; 7 partial (see §8).**

## 11. Tests

- **Governance resolution**: `GenerateWarpNetwork` tags exactly one `SectorCapital` and one
  `SubsectorCapital` per subsector-with-an-Imperial-world; `GetSectorLord()` returns that
  planet's governor; resolution is deterministic for a seed and re-derives identically after a
  save/load (designation not persisted, but stable).
- `ScenarioBuilder` determinism: same `seed` ⇒ same promised planet, same stamped regions,
  same resolved Sector Lord, same `BriefingText`.
- Stamp invariants: promised world is majority-Imperial; exactly N Tyranid regions; fleet in
  orbit with **no** landed squads; Tyranids grow by consumption, so `GrowthMultiplier` stays at
  the default `1.0` everywhere (the stamp sets no throttle — `StampedTyranids_GrowByConsumption_NotAThrottle`).
- `BriefingComposer`: all tokens substituted, no leftover placeholders, deterministic choice.
- Growth: the general `GrowthMultiplier` primitive — a throttled *Logistic* region grows strictly
  slower than an identical unthrottled one over a fixed number of turns.
- `ProcessScenario`: win path grants the player `PlanetFaction` and raises the **current
  Sector Lord's** opinion; lapse path withdraws and lowers it; a lapse with a vacant seat
  still resolves (no-op reputation); neither fires while `State != Pending`.
  *(Implemented in `OnlyWar.Tests/Turns/ScenarioTurnTests.cs`, alongside the general
  `GrowthMultiplier` throttle test — a throttled Logistic region grows strictly slower than an
  identical unthrottled one — and a default-multiplier-unchanged guard. Win/lapse are exercised
  against a real stamped sector with the board forced to the win/lapse state.)*
- Save/load round-trip: `CampaignScenario`, `GrowthMultiplier`, and the planet-less authority
  character all survive (extend `SaveLoadRoundTripTests`).
- Legacy save (no scenario row) loads with `Scenario == null` and no growth change.

## 12. Deferred / out of scope

- Full §4.19 narrative system — `BriefingComposer` is a deliberate placeholder.
- Global chapter-reputation scalar and §6.5 Inquisition consequences — reputation lives on the
  Sector Lord `Character` (the governing seat) for now.
- Per-character hand-authored `Character.Title`, and a fuller titled governance UI (capital
  markers on the Galaxy/Planet screens) — the governance layer (§2.3) enables these but they
  are not required for the scenario.
- "Replacement world offered" on loss and sector-wide narrative recaps — not in this pass.
- Reducing starting *chapter* strength (vs. expressing understrength through materiel) — left
  for a later balance pass if playtesting shows the full chapter trivializes the objective.
- Tyranid region-to-region *spread* as an explicit mechanic — handled implicitly by the
  existing `FactionStrategyController` NPC orders for 0.7. **Caveat (discovered in step 7):** the
  implicit spread currently routes NPC offensives through the battle engine, which is both too
  expensive for army-scale NPC/PDF/Tyranid fights and fragile once real generated armies enter
  forward simulation. The follow-up is now scoped as the large-scale NPC combat resolver in
  `Design/LargeScaleNpcCombat.md`: NPC-only regional assaults above tactical scale resolve in
  strategic battle-value space, while player/named-squad fights stay tactical.
```
