# OnlyWar — Technical Design Document

**Version:** Alpha 0.7

**Last Updated:** July 2026

**Author:** Nathan Dilday

---

## Table of Contents

1. [Technology Stack](#1-technology-stack)
2. [Project Structure](#2-project-structure)
3. [Architectural Patterns](#3-architectural-patterns)
4. [Data Layer](#4-data-layer)
   - 4.1 [Game Rules Database](#41-game-rules-database)
   - 4.2 [Save State Database](#42-save-state-database)
   - 4.3 [Save Schema](#43-save-schema)
5. [Domain Model](#5-domain-model)
   - 5.1 [Galaxy & Planets](#51-galaxy--planets)
   - 5.2 [Factions](#52-factions)
   - 5.3 [Soldiers](#53-soldiers)
   - 5.4 [Squads & Units](#54-squads--units)
   - 5.5 [Fleet](#55-fleet)
   - 5.6 [Missions & Orders](#56-missions--orders)
   - 5.7 [Characters & Requests](#57-characters--requests)
   - 5.8 [Campaign Scenario](#58-campaign-scenario)
6. [System Implementations](#6-system-implementations)
   - 6.1 [Turn Controller](#61-turn-controller)
   - 6.2 [Faction Strategy](#62-faction-strategy)
   - 6.3 [Sector Entity Logic](#63-sector-entity-logic)
   - 6.4 [Mission Step State Machine](#64-mission-step-state-machine)
   - 6.5 [Mission Checks](#65-mission-checks)
   - 6.6 [Battle System](#66-battle-system)
   - 6.7 [Force Generation](#67-force-generation)
   - 6.8 [Chapter Generation](#68-chapter-generation)
   - 6.9 [Sector Generation](#69-sector-generation)
7. [UI Layer](#7-ui-layer)
   - 7.1 [View / Controller Pattern](#71-view--controller-pattern)
   - 7.2 [Screen Inventory](#72-screen-inventory)
   - 7.3 [Navigation Model](#73-navigation-model)
8. [Identified Technical Risks & Debt](#8-identified-technical-risks--debt)
9. [Testing Strategy](#9-testing-strategy)

---

## 1. Technology Stack

| Concern | Choice |
|---|---|
| Engine | Godot 4 |
| Language | C# (.NET via Godot's .NET build) |
| Game rules data | SQLite (read-only at runtime) |
| Save state data | SQLite (written on save, read on load) |
| RNG | Custom `RNG` static class wrapping `System.Random` |
| Statistical math | Custom `GaussianCalculator` static class |

---

## 2. Project Structure

```
/Assets                   Textures, icons, audio
/Builders                 Procedural generation and factory classes
/Database                 SaveStructure.sql schema file
/Helpers
  /Battles                Battle grid, soldier, squad, resolver, placers, actions
  /Database
    /GameRules            Read-only rules DB access (templates, factions, weapons, etc.)
    /GameState            Save/load DB access (planets, soldiers, squads, orders, etc.)
  /Extensions             Extension methods (Color, Squad, etc.)
  /Missions               Mission step implementations organized by mission type
  /Simulation             Session-scoped simulation dependencies
  /Turns                  End-of-turn phase processors, shared turn context/result, intel ledger
/Models
  /Battles                BattleConfiguration, BattleHistory, BattleTurn, BattleMissionTemplate
  /Equippables            Weapon and armor models and templates
  /Fleets                 Fleet, Ship, TaskForce, and their templates
  /Missions               Mission, MissionContext, MissionType enum
  /Orders                 Order, Aggression, Disposition enums
  /Planets                Planet, Region, RegionFaction, PlanetFaction, Subsector
  /Soldiers               Soldier, PlayerSoldier, ISoldier, Skill, Body, HitLocation, etc.
  /Squads                 Squad, SquadTemplate, SquadTemplateElement, SquadTypes
  /Units                  Unit, UnitTemplate
/Scenes
  /ApothecaryScreen
  /BattleReviewScreen
  /ChapterScreen
  /EndOfTurnDialog
  /GalaxyView
  /MainGameScreen
  /MainMenuScreen
  /PlanetDetailScreen
  /RecruiterScreen
  /RegionScreen
  /SoldierScreen
  /SquadScreen
```

---

## 3. Architectural Patterns

### 3.1 View / Controller Separation

Every Godot scene that has meaningful logic is split into two C# classes:

- **View** (`partial class`, inherits a Godot node type): owns all `GetNode<T>` calls, declares C# `event`s for every user interaction, and exposes methods the controller calls to update display state. The View contains no game logic.
- **Controller** (plain C# class, or `partial class` node at the scene root): subscribes to View events in `_Ready()`, reads and mutates game state, and calls View methods to reflect the result.

`DialogView` and `DialogController` are base classes providing common close-button handling and the `CloseButtonPressed` event.

### 3.2 GameDataSingleton

`GameDataSingleton` is a globally accessible singleton (not a Godot autoload — it is a plain C# singleton accessed via `GameDataSingleton.Instance`) that holds:

- `Sector` — the live sector state (all planets, factions, player force)
- `GameRulesData` — the loaded rules blob (all templates, base skills, body templates)
- `Date` — current game date

Most scene controllers still reach into this singleton for their data. End-of-turn simulation now treats it as a composition boundary instead: the default `TurnController` constructor captures the loaded rules, sector, date, and production `IRNG` adapter in a `GameSession`, then injects that session into the turn processors. Tests and future simulations can construct a session directly without making the singleton their source of truth.

### 3.3 Template / Instance Pattern

All content is split between immutable templates (loaded from the rules database once at startup) and mutable runtime instances:

| Template | Runtime Instance |
|---|---|
| `SoldierTemplate` | `Soldier` / `PlayerSoldier` |
| `SquadTemplate` | `Squad` |
| `UnitTemplate` | `Unit` |
| `ShipTemplate` / `FleetTemplate` | `Ship` / `TaskForce` |
| `PlanetTemplate` | `Planet` |
| `HitLocationTemplate` | `HitLocation` |
| `RangedWeaponTemplate` / `MeleeWeaponTemplate` | `RangedWeapon` / `MeleeWeapon` |

Templates are immutable after load. All mutable state lives in instances.

### 3.4 Mission Step State Machine

Mission execution is modeled as a chain of `IMissionStep` objects. Each step's `ExecuteMissionStep(MissionContext, float margin, IMissionStep returnStep)` either calls the next appropriate step directly (passing `this` as `returnStep` for looping steps such as daily stealth checks) or returns when the mission is complete or the force is wiped out.

`MissionStepOrchestrator` is the entry point, selecting the initial step from mission type. If the squad is not already in the target region, an `InfiltrateMissionStep` is prepended regardless of mission type.

### 3.5 ICloneable on Battle Types

`BattleSquad` and `BattleSoldier` implement `ICloneable` to support storing snapshots of battlefield state for the `BattleHistory` replay system. Each turn's state is stored as a clone of the current grid state, enabling the Battle Review Screen to step backward and forward through the engagement.

---

## 4. Data Layer

### 4.1 Game Rules Database

Read-only SQLite file loaded once at application start. Accessed via `GameRulesDataAccess` (singleton). Contains:

At runtime, `GameStorage` locates the immutable install root and supplies the ordinary filesystem path `Database/OnlyWar.s3db`; the database is deliberately shipped loose beside the exported executable because `Microsoft.Data.Sqlite` cannot open a database inside Godot's virtual PCK filesystem. Editor and test runs locate the same install root by walking up from the process/assembly directories, so no code depends on the current working directory.

- `Faction`, `Species`, `SoldierTemplate`, `SquadTemplate`, `SquadTemplateElement`
- `UnitTemplate`, `UnitTemplateHierarchy`, `UnitTemplateSquadTemplate`
- `BaseSkill`, `SkillTemplate`
- `HitLocationTemplate` (grouped into body types)
- `RangedWeaponTemplate`, `MeleeWeaponTemplate`, `WeaponSet`, `WeaponSetEntry`
- `TrainingProfile`, `TrainingProfileEntry` for data-driven skill and attribute training distributions
- `PlanetTemplate`
- `ShipTemplate`, `BoatTemplate`, `FleetTemplate`, `FleetShipTemplate`

Load order matters: skills → hit locations → weapon templates → training profiles → soldier/squad templates → unit templates → planet templates → fleet templates → factions.

Rules-data display names are not intended to be stable code contracts. Any code path that needs a specific skill, faction, template, weapon, hit location, chapter role, or rating definition should eventually resolve that dependency through a stable key, a semantic flag, or a validated registry loaded at startup. Startup validation should fail fast with a clear error when required rules data is missing, rather than allowing a later `First(...)` or dictionary lookup failure during play.

### 4.1.1 Data-Driven Rule Profiles

Training distributions are the first candidate for moving tunable rules out of C# and into the rules database. The long-term pattern is:

- Code owns the algorithm for applying a profile.
- Rules data owns which skills or attributes a role trains, and at what relative weights.
- `SoldierTemplate` records the work-experience training profile for that soldier type.
- Scout focus modes use training profiles rather than hardcoded skill lists.

Future profile/definition candidates:

- Mission skill requirements, e.g. stealth checks and tactical planning checks.
- Default battle resources, e.g. unarmed melee weapon/skill.
- Chapter-generation role bindings, e.g. Chapter Master, Scout Company, Armory, Apothecarion.
- Sector-generation faction roles, e.g. primary hidden infiltrator and invasion faction.
- Soldier rating formulas and award thresholds.

Rating formulas require a constrained evaluator rather than arbitrary script execution. The proposed model is to store `RatingDefinition`, `RatingComponent`, and `RatingNormalizationFactor` rows, with a small fixed set of component types such as attribute value, skill total, best skill bonus in category, and best skill total in category. This keeps formulas tunable without embedding a general-purpose expression language.

**Implemented (rating definitions & awards).** Soldier ratings and their award thresholds are now fully data-driven (see `Design/Reference/DataDrivenRatings.md`). The rules DB holds `RatingDefinition` (key, display name, `Product`/`Sum` aggregation), `RatingComponent` (component type + polymorphic target — attribute, base-skill id, or skill category), `RatingNormalizationFactor` (uniform `(Low, High)` divisor factors), and `RatingAwardTier` (medal tiers and history-flag thresholds, with a `{bestSkillInCategory}` name placeholder), all seeded by `RulesDbTool migrate-ratings`. `RatingCalculator` (`Helpers/RatingCalculator.cs`) evaluates `Aggregate(components) / Π sample(factor)` using an injected `IRNG`, and applies the highest matching award/flag tier per rating (fixing the old double-Banner bug and `"Admantium"` typo). `SoldierTrainingCalculator` delegates `UpdateRatings`/awards to it. `GameRulesData` validates the definitions at load (all required keys present, components reference real skills, award tiers reference real ratings). The set of ratings is open-ended: `SoldierEvaluation` stores a `RatingKey → value` map (with convenience accessors over the seven well-known keys), persisted via the `SoldierEvaluationRating` child table (§4.3). `IRNG`/`SeededRNG`/`StaticRNG` were introduced (§9.1). Remaining future candidates above (mission skill requirements, etc.) are unaffected.

### 4.2 Save State Database

Written in full on each save (file is deleted and recreated from scratch using the loose, read-only `Database/SaveStructure.sql`). Read on load via `GameStateDataAccess` (singleton). All writes are wrapped in a single transaction; exceptions trigger rollback. Player saves live under `user://saves` (`%APPDATA%\OnlyWar\saves` on Windows), never in the install directory. `SaveGameCatalog` discovers `*.s3db` files and inspects only their metadata for the start menu.

**Current 0.7.1 behavior:** `SaveFormat.CurrentVersion` is written to `GlobalData.SaveVersion`; discovery marks a different version as incompatible, and the data access layer rejects it before reading sector tables. Missing saves are opened in neither create nor write mode, preventing a failed load from leaving behind an empty SQLite file. The visible chooser retains compatible, incompatible, and corrupt entries with an explicit reason instead of silently choosing the newest file.

Named manual slots, the initial recovery point, three rolling post-turn autosaves, and the protected pre-turn recovery point all use the same atomic persistence path. `CampaignRecoverabilityTracker` records whether the current in-memory revision has a successfully written recovery point, while `SaveGameManager` owns slot naming, metadata, retention, overwrite protection, and restoration of the prior valid save on failure. The protected pre-turn write completes before `ProcessTurn` mutates state; failure blocks turn resolution. Ordered save-format migration remains intentionally deferred; no migrators exist.

**Format version 6 (2026-08-07).** The first bump. `OrderSoldier` (specialist attachment, §5.6) was added to the save schema, and a version-5 save read by the new build faulted deep in the loader with `SQLite Error 1: 'no such table: OrderSoldier'`. The guard for exactly this already existed — `GlobalDataAccess.EnsureCompatibleSaveVersion` runs at `GameStateDataAccess.GetData` *before* any sector table is read, and `SaveGameCatalog` marks mismatched files incompatible in the chooser — but it can only fire if `SaveFormat.CurrentVersion` moves with the schema. **The rule this establishes: any change to `SaveStructure.sql`'s shape bumps `SaveFormat.CurrentVersion`.** A save/load round-trip test cannot catch the omission, because the writer recreates the schema from scratch on every save and so always agrees with itself; only an *older file* read by a *newer build* exposes it. Old saves are rejected rather than migrated — acceptable during alpha, and the point at which that stops being acceptable is the point migrators get written.

Connections use `Microsoft.Data.Sqlite` (the `SqliteConnectionStringBuilder` `DataSource`) with foreign key enforcement enabled (`ForeignKeys = true`). The schema is foreign-key-valid — every reference resolves to a table in the save file — and the save routines insert parent rows before the rows that reference them. `Faction` is intentionally *not* a foreign-key target: factions live only in the read-only rules database and are matched by id at load. See §8.5.1 for the provider-compatibility work that established this.

### 4.3 Save Schema

Key tables and their relationships:

```
GlobalData           (Millenium, Year, Week, SaveVersion)

Planet               (Id, PlanetTemplateId, Name, x, y, Importance, TaxLevel)
PlanetFaction        (PlanetId, FactionId, IsPublic, Population, PlanetaryControl,
                      PlayerReputation, LeaderId→Character)
Region               (Id, PlanetId, RegionNumber, RegionName, RegionType,
                      IsUnderAssault, IntelligenceLevel, CarryingCapacity)
RegionFaction        (RegionId, FactionId, IsPublic, Population, Garrison,
                      Organization, OrganizedMilitaryStrength, Entrenchment, Detection, AntiAir)
Mission              (Id, MissionType, RegionId, FactionId, MissionSize, DefenseTypeId,
                      IsRegionMission)                     -- 1 = region special mission, 0 = order-attached

Character            (Id, Investigation, Paranoia, Neediness, Patience,
                      Appreciation, Influence, LoyalFactionId, OpinionOfPlayer)
Request              (Id, CharacterId, PlanetId, RequestDate, FulfillmentDate)

Fleet                (Id, FactionId, x, y, DestinationPlanetId)
Ship                 (Id, ShipTemplateId, FleetId, Name)

Unit                 (Id, UnitTemplateId, ParentUnitId, Name)
Squad                (Id, SquadTemplateId, UnitId, ShipId, RegionId, Name)
SquadWeaponSet       (SquadId, WeaponSetId)
Assignment           (Id, MissionId, Disposition, IsQuiet,
                      IsActivelyEngaging, Aggression)     -- the "Order" domain object
OrderSquad           (OrderId→Assignment, SquadId)       -- order-to-squad junction
OrderSoldier         (OrderId→Assignment, SoldierId)     -- individual specialists attached to an
                                                         -- operation without their home squad
                                                         -- (Design/Reference/SpecialistAttachment.md).
                                                         -- Soldier.SquadId still points at the home
                                                         -- squad; this row is the only record that
                                                         -- he is currently detached forward.

Soldier              (Id, SoldierTemplateId, SquadId, Name, Strength, Dexterity,
                      Constitution, Intelligence, Perception, Ego, Charisma,
                      PsychicPower, AttackSpeed, Size, MoveSpeed)
SoldierSkill         (SoldierId, BaseSkillId, PointsInvested)
HitLocation          (SoldierId, HitLocationTemplateId, IsCybernetic,
                      Armor, WoundTotal, WeeksOfHealing)

PlayerSoldier        (SoldierId, ImplantMillenium, ImplantYear, ImplantWeek)
PlayerSoldierEvent   (PlayerSoldierId, Millenium, Year, Week, EventType,
                      FactionId, WeaponTemplateId, Magnitude, LocationName,
                      Detail, RelatedSoldierIds)
SoldierEvaluation       (SoldierId, Millenium, Year, Week)   -- identity only
SoldierEvaluationRating (SoldierId, Millenium, Year, Week, RatingKey, Value)
                                                            -- open-ended: one row per rating
SoldierAward         (SoldierId, Millenium, Year, Week, Name, Type, Level)
PlayerSoldierFactionCasualtyCount        (PlayerSoldierId, FactionId, Count)
PlayerSoldierRangedWeaponCasualtyCount   (PlayerSoldierId, RangedWeaponTemplateId, Count)
PlayerSoldierMeleeWeaponCasualtyCount    (PlayerSoldierId, MeleeWeaponTemplateId, Count)

PlayerFactionEvent      (Id, Millenium, Year, Week, Title)
PlayerFactionSubEvent   (PlayerFactionEventId, Entry)
```

**Note:** Region adjacency is runtime-only. It is reconstructed from the ordered region array on load and is not persisted.

---

## 5. Domain Model

### 5.1 Galaxy & Planets

```
Sector
  ├─ Planets : Dictionary<int, Planet>
  ├─ Subsectors : List<Subsector>
  └─ PlayerForce : PlayerForce

Planet
  ├─ Regions : Region[]
  ├─ PlanetFactionMap : Dictionary<int, PlanetFaction>
  └─ Template : PlanetTemplate

Region
  ├─ RegionFactionMap : Dictionary<int, RegionFaction>
  ├─ AdjacentRegions : List<Region>        (runtime only, not persisted)
  ├─ SpecialMissions : List<Mission>
  └─ IntelligenceLevel : float

RegionFaction
  ├─ PlanetFaction : PlanetFaction         (back-reference for faction identity)
  ├─ Population : long
  ├─ Garrison : int
  ├─ OrganizedMilitaryStrength : long
  ├─ DisorganizedMilitaryStrength : long (derived)
  ├─ Organization : int (derived compatibility/display percentage)
  ├─ Detection : int
  ├─ Entrenchment : int
  ├─ AntiAir : int
  ├─ LandedSquads : List<Squad>            (squads of this RegionFaction's faction currently in this region)
  └─ IsPublic : bool

PlanetFaction
  ├─ Faction : Faction
  ├─ Leader : Character                    (null if the faction has no leader assigned)
  ├─ IsPublic : bool
  ├─ Population : long
  ├─ PlayerReputation : float
  └─ PlanetaryControl : int

Subsector
  ├─ Planets : List<Planet>
  └─ CellList : List<Vector2I>             (grid cells this subsector covers)
```

### 5.2 Factions

`Faction` is a read-only template object loaded from the rules database. It is not persisted in the save file — it is reconstructed from the rules DB on load and matched to saved `PlanetFaction` / `RegionFaction` rows by ID.

Key flags: `IsPlayerFaction`, `IsDefaultFaction` (the imperial PDF baseline), `CanInfiltrate`, `GrowthType` (None, Logistic, Conversion).

### 5.3 Soldiers

```
ISoldier (interface)
  implemented by Soldier; delegated to by PlayerSoldier

Soldier
  ├─ Template : SoldierTemplate
  ├─ Body : Body
  │    └─ HitLocations : HitLocation[]
  ├─ Skills : IReadOnlyCollection<Skill>
  ├─ AssignedSquad : Squad
  └─ Attributes: Strength, Dexterity, Constitution, Intelligence,
                 Perception, Ego, Charisma, PsychicPower,
                 AttackSpeed, Size, MoveSpeed  (float each)

PlayerSoldier  (wraps Soldier, adds player-tracking data)
  ├─ _soldier : Soldier                    (private delegate target)
  ├─ ProgenoidImplantDate : Date
  ├─ SoldierEvents : IReadOnlyList<SoldierEvent>
  ├─ SoldierEvaluationHistory : List<SoldierEvaluation>
  ├─ SoldierAwards : List<SoldierAward>
  ├─ RangedWeaponCasualtyCountMap : Dictionary<int, ushort>
  ├─ MeleeWeaponCasualtyCountMap : Dictionary<int, ushort>
  └─ FactionCasualtyCountMap : Dictionary<int, ushort>
```

`PlayerSoldier` implements `ISoldier` by delegating all attribute and skill reads to its inner `Soldier`.
Its career history is stored as structured `SoldierEvent` records. Event metadata supports
queries by type, date, faction, weapon, magnitude, location, and related soldiers; `Detail`
holds the authored service-record summary, and `Render()` formats that summary for display.

#### Wound Model

`HitLocation` holds a `Wounds` struct (two `uint` fields: `WoundTotal` and `WeeksOfHealing`), an `IsCybernetic` flag, a per-location `Armor` float, and a reference to its immutable `HitLocationTemplate`.

`WoundTotal` accumulates using `WoundLevel` values as bitmask steps:

```
WoundLevel enum (bitmask):
  Negligible   = 0x0000001
  Minor        = 0x0000010
  Moderate     = 0x0000100
  Major        = 0x0001000
  Critical     = 0x0010000
  Massive      = 0x0100000
  Mortal       = 0x1000000
  Unsurvivable = 0x10000000
```

`WeeksOfHealing` encodes healing progress across tiers using nibble offsets. The `Wounds.WeeksToHeal` property reads the appropriate nibble for the highest active tier to determine remaining weeks.

**Healing cadence.** `Wounds.AdvanceOccupiedBandClocks` advances the clock of **every band that holds wounds, independently and concurrently; empty bands do not age.** Dwell times step down one band per interval — Unsurvivable 7 weeks, Mortal 6, Massive 5, Critical 4, Major 3, Moderate 2 — with Negligible and Minor cleared outright on any pass. The governing principle is that wounds are *discrete injuries, not one severity counter*: a location's Major wounds convalesce alongside its Critical ones, exactly as a split lip mends without waiting for a broken nose. Because each band's dwell is one week shorter than the band above, a band always empties one week before the band above steps into it, so a step-down can never overfill a band. `Wounds.Normalize()` is retained as an invariant guard on both mutation paths (it folds a band above `WOUND_MAX` upward), and `AddWound` genuinely needs it — a sixth Major wound *is* one Critical. Demotion preserves count: three Major wounds become three Moderate. `AddWound` resets `WeeksOfHealing` for **every** band, deliberately (PRD §6.14). Pinned by `WoundHealingCadenceTests`.

**Astartes daily healing.** `Wounds.ClearNegligibleWounds()` (`WoundTotal &= 0xfffffff0`) runs once per campaign day for species carrying `SpeciesAbilities.AcceleratedHealing`, so a day's glancing hits no longer compound into a real wound while a single battle's worth still can — a battle resolves inside one day. Severed locations are skipped.

`HitLocationTemplate` defines per-location properties: `NaturalArmor`, `WoundMultiplier`, `CrippleWound` threshold, `SeverWound` threshold, `IsMotive`, nullable `HandGroupId`, `IsVital`, and `HitProbabilityMap` (a 3-element int array for short/medium/long range bands). An arm and its hand share a hand group; disabling either disables that physical hand.

#### Capability, Motive Impairment & Casualty State

"Out of the fight" is three predicates on `ISoldier`, not one:

- **`CanFight`** — hands and vitals. False if any *vital* location is crippled or severed, or no hand group functions.
- **`CanMove`** — motive only. False when the motive speed multiplier reaches zero.
- **`IsCombatEffective`** — `CanFight && CanMove`. This is the seam every consumer uses (planning, targeting, morale, deployment gating, the Apothecarium); nothing in production means "can shoot but need not walk".

`MotiveImpairment` computes a **speed multiplier per motive location by wound band**, and immobility is simply the product reaching zero — there is no separate binary. Constants live in `CasualtyConstants`, not the rules DB. Below Major 1.0, Major 0.85, Critical 0.6, and zero at Massive/crippled/severed; locations compound **multiplicatively**, so two Critical legs give 0.36 and still fight. **Extremities floor at 0.40 and can never fell a soldier** — a location counts as an extremity when some other motive location on that body has a strictly higher cripple threshold, which makes legs principal and feet extremities on both authored bodies. `BattleSoldier.GetMoveSpeed()` multiplies `Soldier.MoveSpeed` by it, replacing the former binary `IsSlow` / flat ×0.75.

Legs cripple at `Massive` and sever at `Mortal` — deliberately a band apart, so "crippled but not severed" stays reachable for the body's principal motive location, which is the state incapacitation is built on. Feet cripple at `Major`, sever at `Critical`. Thresholds live in the rules DB and are mirrored in the `Body.cs` hard-coded fallbacks; the two must not diverge.

`CasualtyState { Unharmed, Impaired, Incapacitated, Killed }` with `CasualtyStateEvaluator` classifies the outcome from the body plus one external fact (whether the body was recovered). **Power-armor biostasis:** a downed player soldier cannot die of his wounds awaiting treatment, so there is no deterioration clock, no bleed-out pass, and medical care is only ever about *speed* of recovery. Nothing new is persisted — the condition derives from wounds already in the `HitLocation` table, and a recovered brother keeps his squad, so he never trips the null-squad-means-dead path at load.

#### Skill Model

```
BaseSkill
  ├─ Category : SkillCategory
  ├─ BaseAttribute : Attribute
  └─ Difficulty : float

Skill
  ├─ BaseSkill : BaseSkill
  ├─ PointsInvested : float
  └─ SkillBonus = (PointsInvested == 0 ? -4 : log2(PointsInvested)) - Difficulty

Soldier.GetTotalSkillValue(BaseSkill) = attribute value + SkillBonus
```

Attributes and skills both contribute to the same roll, meaning untrained soldiers still benefit from high raw attributes.

### 5.4 Squads & Units

```
Unit
  ├─ Template : UnitTemplate
  ├─ ChildUnits : List<Unit>
  ├─ Squads : List<Squad>           (non-HQ squads)
  └─ HQSquad : Squad

Squad
  ├─ Template : SquadTemplate
  ├─ Members : List<ISoldier>
  ├─ Loadout : List<WeaponSet>
  ├─ CurrentOrders : Order
  ├─ CurrentRegion : Region         (null if aboard a ship)
  ├─ BoardedLocation : Ship         (null if on a planet)
  └─ ParentUnit : Unit

SquadTemplate
  ├─ Elements : List<SquadTemplateElement>
  ├─ WeaponOptions : List<SquadWeaponOption>
  ├─ SquadType : SquadTypes         (flags: HQ, Scout, Elite, etc.)
  ├─ BattleValue : int
  └─ BodyguardSquadTemplate : SquadTemplate   (for Assassination missions)

SquadTemplateElement
  ├─ SoldierTemplate : SoldierTemplate
  ├─ MinimumNumber : int
  └─ MaximumNumber : int
```

`PlayerForce` contains:
- `Army : Unit` — the top-level chapter unit (order of battle root)
- `Fleet` — aggregates the `TaskForce` list
- `Requests : List<IRequest>`
- `BattleHistory : Dictionary<Date, List<EventHistory>>`
- `Army.SquadMap : Dictionary<int, Squad>` — flat lookup populated by `Army.PopulateSquadMap()`

### 5.5 Fleet

```
Fleet (player-force level)
  └─ TaskForces : List<TaskForce>

TaskForce
  ├─ Faction : Faction
  ├─ Ships : List<Ship>
  ├─ Position : Coordinate?           (null if in transit)
  ├─ CurrentPlanet : Planet          (null if in transit)
  └─ Destination : Planet            (null if stationary)

Ship
  ├─ Template : ShipTemplate
  ├─ Fleet : TaskForce
  ├─ LoadedSquads : IEnumerable<Squad>
  └─ AvailableCapacity : int
```

### 5.6 Missions & Orders

```
MissionType enum:
  Advance, Ambush, Assassination, Extermination,
  Recon, Sabotage, Patrol, Defense, Construction

Mission
  ├─ MissionType : MissionType
  ├─ RegionFaction : RegionFaction   (the target)
  └─ MissionSize : int               (tier / intensity)

SabotageMission : Mission
  └─ DefenseType : DefenseType       (Organization, Detection, Entrenchment, AntiAir)

ConstructionMission : Mission
  └─ ConstructionType : DefenseType  (Organization, Detection, Entrenchment, AntiAir)

Order
  ├─ Squads : List<Squad>
  ├─ AttachedSoldiers : List<PlayerSoldier>   (individuals attached without their squad)
  ├─ Mission : Mission
  ├─ Disposition : Disposition       (Mobile, DugIn)
  ├─ IsPlayerControlled : bool
  ├─ IsForceAdvance : bool
  └─ LevelOfAggression : Aggression  (Avoid, Cautious, Normal, Attritional, Aggressive)

MissionContext  (runtime only, not persisted)
  ├─ Order : Order
  ├─ MissionSquads : List<BattleSquad>
  ├─ OpposingSquads : List<BattleSquad>
  ├─ Log : List<string>
  ├─ DaysElapsed : int
  ├─ Impact : float
  └─ EnemiesKilled : int
```

#### Specialist Attachment

Orders bind squads; an individual specialist reaches an operation by **attachment** instead. `Order.AttachedSoldiers` ⟷ `PlayerSoldier.AttachedOrder` is a pointer pair owned entirely by `OrderAttachment` (`Attach`/`Detach`/`ReleaseAll`/`CanAttach`), so nothing can half-attach. `SpecialistAvailability` holds the selection rules, extracted from the Godot controller so they are unit-testable.

An attached soldier **stays in `Squad.Members`** — the save keys a soldier's squad, and a null squad at load means *dead*, so removing him would resurrect him as a fallen brother. Home-squad headcount therefore still counts him; only "ready right now" displays subtract him.

`SquadTypes.PermitsIndividualDetachment` (0x80, rules DB) marks the eight formations that exist to supply specialists — the four HQ templates and the four chapter offices. The flag is **two-sided**: a formation that may give up individuals **never deploys as a unit**, enforced in `OrderAssignment.AssignSquadsToMission`. This is deliberately *not* implemented via the `Administrative` bit, because `Squad.IsOperational` must stay true — surgery staffing (`MedicalProcedureService`) and recruitment/implantation (`RecruitmentPromotionService`) both gate on it, and these are exactly the formations that supply that staff.

Invariant: **an order always has ≥1 assigned squad.** A specialists-only order is rejected; several sites partition orders on `AssignedSquads.Any()`. Attachment lifetime is order lifetime — it is released when the player unassigns, when `MissionAftermathProcessor` cleans up a resolved order, when the last squad leaves, when the home squad turns administrative, or on the man's death. An attached specialist has **no battlefield presence**: he is in no `BattleSquad`, cannot become a casualty, and is added to the mission report explicitly rather than via battle participation.

Persistence is the `OrderSoldier` table. Hydration must run **after** player soldiers load, not where orders are constructed, because `PlayerSoldier`'s constructor evicts the base `Soldier` from its squad and inserts the wrapper — so the round-trip test asserts *reference* equality, not matching ids.

### 5.7 Characters & Requests

```
Character
  ├─ Personality traits: Investigation, Paranoia, Neediness,
  │                       Patience, Appreciation, Influence  (float, 0–1 range)
  ├─ Loyalty : Faction
  ├─ OpinionOfPlayerForce : float
  └─ ActiveRequest : IRequest

IRequest (interface)
  ├─ Id : int
  ├─ Requester : Character
  ├─ TargetPlanet : Planet
  ├─ DateRequestMade : Date
  ├─ Deadline / DateRequestResolved : Date
  ├─ FulfillmentKind : ForceCommitment | ThreatSuppressed
  ├─ Status : Open | InProgress | Fulfilled | Failed
  ├─ Commitment : ForceCommitmentPackage
  ├─ ProgressBattleValueTime : long
  └─ Snapshotted offer, severity, and hazard

PresenceRequest : IRequest
  (the first governor-request vertical slice)

Pledge
  ├─ SourcePlanetId / GrantingAuthorityId : int
  ├─ Payload : Requisition amount
  ├─ ScheduleKind : OneOff | Standing
  ├─ NextDeliveryDate / CadenceWeeks
  └─ Status : Active | Suspended | Completed | Defaulted
```

Requests snapshot their readable force package, deadline, hidden Battle-Value-Time valuation, and offered Requisition when generated. Confirmed threats use outcome fulfillment; false alarms use capped weekly force commitment. Fulfillment creates an institutional `Pledge` rather than crediting Requisition immediately. `PledgeDeliveryProcessor` handles delayed one-off grants, standing cadence, source-world suspension/default, and succession independently of the individual governor. Requests and pledges persist their evaluated values and lifecycle state exactly.

### 5.8 Campaign Scenario

`Sector.Scenario` is an optional persisted `CampaignScenario`; `null` retains sandbox behavior. The current `PromisedWorld` scenario stores its promised planet, objective state (`Pending`, `Won`, or `Lapsed`), one-shot briefing acknowledgement, composed briefing text, and original authority id. Mechanical authority is resolved from the current governance hierarchy rather than pinned to the original character.

Governance designations are derived, not persisted. `SectorBuilder.AssignGovernance` deterministically selects the highest-importance Imperial world as sector capital and one Imperial governance seat per subsector; the governor characters themselves already round-trip. `ScenarioBuilder` stamps the opening objective and starting opposition after normal sector generation, while `ScenarioTurnProcessor` evaluates success or lapse only after the week's missions and planetary simulation settle.

---

## 6. System Implementations

### 6.1 Turn Controller

`TurnController` is the single entry point for end-of-turn processing, called by `MainGameScene.OnEndTurnButtonPressed`. It is an orchestration facade: phase behavior lives in focused processors under `Helpers/Turns`. Two context objects separate lifetime and responsibility:

- `GameSession` is the stable dependency set for simulations belonging to one loaded game: rules, sector, mutable campaign date, and `IRNG`. The production constructors build it once from `GameDataSingleton` plus `StaticRNG`; an internal constructor accepts an explicit session for isolated tests and future alternate simulations.
- `SimulationContext` is per-run state: the session, `TurnResolutionResult`, `TurnIntelLedger`, separate player/all-order lists, and an optional planet scope for generation-time forward simulation.

`ProcessTurn(Sector)` returns the run's `TurnResolutionResult`; the retained sector parameter must be the same object owned by the session, preventing rules/date/RNG from one game being combined with another sector. The controller's `MissionContexts`, `SpecialMissions`, `StrategicCombatResults`, and `ScenarioNotification` properties remain as compatibility views for existing tests.

The processors are divided by simulation responsibility:

- `TurnOrderPlanner` appends hostile-faction and defensive Imperial PDF orders without owning mission resolution.
- `MissionTurnProcessor` resolves diversion shaping, strategic/tactical missions, and construction; `MissionAftermathProcessor` applies strategic consequences and cleans consumed missions/orders. `InvaderPresenceService` provides the common foothold operation used by tactical aftermath, strategic combat, and planetary expansion.
- `ChapterUpkeepProcessor` owns weekly medical and training work; `FleetTurnProcessor` advances travel and delegates warp-subjective training back to the shared upkeep processor.
- `PlanetTurnProcessor` owns planet/region simulation, revolts, governors, and intelligence-derived special missions. `TurnIntelLedger` accumulates recon, listening-post, and patrol gains until the intelligence phase applies them.
- `ScenarioTurnProcessor` resolves campaign objectives after the simulated world state settles. `ScenarioMetricsCollector` owns the optional debug-only opening-scenario trace.

`ProcessTurn` preserves this phase order:

1. Advance the campaign date, clear the result/intel ledger, and begin scenario metrics.
2. Resolve player diversions so their projected threat exists during planning.
3. Append hostile-faction orders and defensive-only Imperial PDF orders, then clear the one-turn diversion effects.
4. Resolve strategic combat, tactical missions, construction, and squad-less biomass feeding; remove consumed special missions.
5. Apply mission aftermath, Chapter medical/training upkeep, fleet movement, planet simulation, special-mission pruning, and intelligence updates.
6. Resolve the campaign scenario, finish diagnostics, clean resolved player orders, and return the result.

`SimulatePlanetForward` reuses the same planning, mission, aftermath, planet, intelligence, and diagnostic processors for generation-time world evolution, but intentionally omits date advancement, Chapter upkeep, fleet movement, other planets, and scenario resolution. Because it has no following planning pass, it sweeps the transient AI forces (patrol screens, recon parties) still standing after its last week, so the world hands off to the player with nothing landed on it.

Non-deployed non-Scout marines receive weekly work-experience training through `ApplySoldierWorkExperience`; Scout squads are routed through `TrainScouts` with each squad's selected `TrainingFocus`. Scout squads assigned to missions are excluded from weekly Scout training.

### 6.2 Faction Strategy

`FactionStrategyController.GenerateFactionOrders(Faction, Sector)` runs per non-player, non-default faction per turn. For each planet where the faction has a public presence:

1. **Force assessment:** Compute `RequiredGarrison` per region from the concrete `OrganizedMilitaryStrength` pool, then `SpareTroops = max(0, OrganizedMilitaryStrength − RequiredGarrison)`.
2. **Offensive planning:** If combined `SpareTroops` in regions adjacent to an enemy exceeds that enemy's strength × 1.5, generate an `Advance` order. `ForceGenerator` is called with `TargetBattleValue` set to 50–75% of `SpareTroops × 10` (randomized). Committed troops are deducted from contributing region garrisons.
3. **Construction:** Convert remaining `SpareTroops / 100` to build points. Reorganization transfers `ReorganizationBattleValuePerEffort` BV from the disorganized pool to the organized pool per effort point; other construction improves Entrenchment, Detection, or Anti-Air (costs scale as `2^currentLevel`).
4. **Patrol:** Any remaining `SpareTroops × 10` become a `ScoutPatrol` order.
5. **Swarm operations (`GrowthType.Consumption` only):** Spread, then feed, from what is left.

Transient AI forces — patrol screens and recon parties — are generated from nothing each pass and cleared at the top of the next one (`ClearStaleTransientSquads`). Recon was previously omitted from that sweep, and a party that survived its week was landed in its home region by `ExfiltrateMissionStep` and never removed, so every completed NPC recon left a permanent ghost squad inflating the region's search difficulty.

**Swarm operations (Consumption factions).** Spreading and biomass feeding are planned taskings drawing on the same per-region `SpareTroops` budget as everything above them, and they run last so both receive the true residual.

- **Spread** applies directly, like garrison and front reinforcement, rather than issuing an order: it relocates strength to the adjacent region of highest biomass (prey population plus carrying capacity) when that region is strictly richer than home, sized as `SpareTroops × depletion × TyranidExpansionShare`. It is deliberately not folded into the ordinary offensive path — the richest neighbour is frequently empty ground with a high carrying capacity and no enemy `RegionFaction` to target at all, so sharing the offensive code path would cost the swarm exactly the moves that matter most.
- **Feed** commits whatever remains as a squad-less `FeedMission` carrying the committed battle value. It is dispatched from the mission phase beside squad-less construction (`ProcessFeedOrders`), resolves instantly, and creates no `MissionContext`. No force is generated: materializing squads for a million-strong swarm would be absurd and there is nothing for them to do tactically. Because a `PopulationIsMilitary` faction's BV pool and headcount are the same number, the committed value drops straight into the biomass allocator's troop term.

Both were previously side effects of `PlanetTurnProcessor.UpdatePlanet` that re-derived the swarm's whole deployed strength from `Population × Organization`, so the same troops fed, spread, defended, patrolled and attacked in the same week; phase ordering ("spread before consume") de-duplicated those two against each other and left both blind to everything else. The planner's budget replaces that ordering. The planner only sees `IsPublic` region-factions, so `UpdatePlanet` retains a hidden-swarm fallback (`ResolveHiddenSwarmExpansion` / `ResolveHiddenSwarmConsumption`) that keeps the old whole-strength behaviour for a swarm nothing planned for. `MissionType.Feed` is appended to the enum for the same save-ordinal reason as `ShowOfForce`.

**Player construction (squad-driven fortification).** The player can order a squad in its own region to build a defense (Entrenchment / Detection / Anti-Air), creating a `ConstructionMission` targeting the player's `RegionFaction`. Unlike the NPC squad-less construction (resolved at a flat `MissionSize` in `ProcessConstructionOrders`), a construction order that carries a squad is routed in `ProcessCombatMissions` to `ResolveSquadConstruction`: every able soldier contributes its `Engineering (Fortification)` skill value, the sum is divided by `EngineeringBuildDivisor` (100) and floored (minimum 1), and the result is applied via the shared `ApplyConstruction`. The order persists, so the squad accumulates defenses over successive turns. `Engineering (Fortification)` is an Intelligence-based Tech skill trained by all combat marines at low weight.

**Multi-faction regions.** Orders target an explicit `RegionFaction`; the assignment UI requires a choice when several hostile factions are present and locks it when only one exists. Detection is aggregated across every detecting enemy in the region, with the actual spotter/interceptor selected by intel and deployed-strength weighting. Intelligence opportunities are budgeted proportionally by faction strength rather than dictionary order. The detailed selection and presentation decisions remain in `Design/Reference/MultiFactionRegions.md`.

**Strategic NPC combat.** NPC-only assaults cross from tactical to `StrategicCombatResolver` when either side exceeds `MaxTacticalActors` (120), generated forces would exceed `MaxGeneratedSquads` (24), or committed strength exceeds `MassCombatBattleValueFloor` (1,500 BV). Named/player squads always remain tactical. Strategic resolution works directly in conserved BV pools: only organized BV deploys and takes ordinary battle casualties; effective strength combines committed BV, aggression, faction quality, entrenchment, and intel-derived surprise. A Gaussian combat ratio determines bounded casualties and whether the attacker clears the 1.10 capture threshold. Invaders establish a foothold on victory, raiders return survivors, and no transient tactical squads are generated. Formulas and tuning questions remain in `Design/Reference/LargeScaleNpcCombat.md`.

**Organized and disorganized military strength.** `RegionFaction.MilitaryStrength` is partitioned into persisted `OrganizedMilitaryStrength` and derived `DisorganizedMilitaryStrength`. Newly raised troops, transferred formations, and returning survivors enter organized. Ordinary engagements remove organized BV and total BV together; disruptive effects may transfer BV into the disorganized pool without killing it. Reorganization is a fixed-BV transfer back, not a percentage increase. Ambush opportunities size against total military strength and distribute casualties proportionally across both pools. After an Advance has eliminated the organized defence, each remaining operating day destroys up to `attacker BV × UndefendedAssaultDestructionMultiplier` disorganized BV (initial multiplier 1.0).

### 6.3 Sector Entity Logic

`PlanetTurnProcessor` runs after mission aftermath and Chapter/fleet upkeep. It handles:

**Population & carrying-capacity scales.** Population is a raw headcount (the `// in thousands` comments were stale). Per-type population and carrying capacity are each described by a `LogNormalValueTemplate { Floor, Scale }`: a roll is `Floor + 10^z · Scale` (z standard-normal), so `Floor` is a hard minimum, `Scale` is the median of the variable part, and the distribution is right-skewed (mean ≈ `Floor + 3.77·Scale`). These are distinct from the normal `NormalizedValueTemplate { BaseValue, StandardDeviation }` still used for Importance. The rules-DB columns are `PopulationFloor`/`PopulationScale` and `CarryingCapacityFloor`/`CarryingCapacityScale` (renamed from the misleading `*Base`/`*StandardDeviation`). Values are canon-grounded per world type (Hive ~80B typical down to Death ~310K), with carrying capacity = population scale × a per-type headroom (Hive 1.3 … Feral 5.0). Because hive/forge populations reach billions, `Garrison` and `PlanetaryDefenseForces` are `long`.

**Population Growth (per region, per faction):**
- Carrying capacity is a per-region value (`Region.CarryingCapacity`). It is an absolute, per-type quantity rolled at sector generation from `PlanetTemplate.CarryingCapacityRange`, distributed across the planet's regions by the same power law used for population, and persisted in the save's `Region` table. Starting population is seeded as a fraction of each region's capacity, so no region begins above capacity.
- `Logistic` and baseline (`None`) growth are scaled by a logistic crowding factor: `newPop = factionPop × growthRate × (1 − regionPop / carryingCapacity)`, where `regionPop` is the region's combined population across all factions. The factor is near-maximal when the region is sparse, zero at capacity, and gently negative above capacity (so an overfull region drifts back toward capacity). A carrying capacity of 0 is treated as uncapped (legacy behavior).
- `growthRate` is the maximum (uncrowded) rate: `LogisticGrowthRate = 0.0006`, `BaselineGrowthRate = 0.0004`. These are tuned so a world at a typical fill (~50–75% of capacity) still roughly doubles per century, matching the canon "population doubles every ~100 Terran years" — not just ultra-underpopulated worlds.
- `Conversion` growth: one default-faction member is converted per week. At population > 100, additional 0.2%/week organic growth. The garrison-to-population ratio determines whether a garrison member is also converted. (Conversion is not subject to the carrying-capacity factor.)
- `Unrest` is reversible civilian allegiance rather than organic growth. Internal per-region Contentment drifts 3% of the gap toward a tax/governor/security/crowding target; the Insurrectionist population closes toward a maximum 30% target share, recruits PDF at 0.7 weight, arms a separate civilian cadre pool, and concentrates one adjacency step at 5% per week. A revolt becomes public at 2:1 rebel-to-loyal strength and hides again below 0.5:1. Hidden embedded PDF remains in the nominal player-facing PDF roster but is excluded from loyal strength. Capital control and contextual human/xenos truces use this same public-state model.

**Going Public:**
- If a hidden faction's population exceeds the configured threshold, `IsPublic` is set to `true`, making it visible and triggering conflict resolution in subsequent turns.

**Intelligence Decay:**
- Regions with `IntelligenceLevel > 0` have it multiplied by 0.75 each turn.
- Recon produces signed evidence: strong failures can reduce the observer's regional belief, but the stored value is clamped at zero. All positive and negative recon evidence produced by an intel-sharing group in the same region during a week is pooled separately, transformed through `D(x) = 6 * (1 - exp(-x / 6))`, and combined as `D(positive) - D(negative)`. Passive listening-post, patrol, and battle-contact gains remain linear. Pooling before the transform makes the result independent of order grouping and prevents allied factions from bypassing diminishing returns.
- While intelligence remains, hidden faction cells may be revealed as `Extermination` missions; public faction intelligence may generate `Ambush`, `Sabotage`, or `Assassination` special missions.
- Each unconsumed special mission has a 25% chance of expiring each turn.

**Fog of War (UI gating).** Enemy visibility on the planet-tactical and region screens is gated by `Region.IntelligenceLevel` (raised by Recon orders, decayed each turn). Hidden (non-public) factions are concealed on every screen — their population is folded into the civilian count and they are discovered only through the intelligence/special-mission system. For a public enemy, `RegionFactionExtensions.GetPopulationDescription` grades the population by intelligence ("Unknown" at 0 → power-of-10 fuzzing → exact at ≥6), and defenses (Entrenchment/Detection/Anti-Air) are shown only when `IntelligenceLevel > 1` and only as fuzzy descriptions via `GetDefenseLevelDescription`, never as raw integers.

**Governor Requests:**
- For each planetary leader with positive opinion of the player: check for a real threat via Investigation vs. hidden faction population ratio; check for a false alarm via Paranoia.
- If a threat (real or imagined) is detected: roll `RequestGenerationRate × Neediness × OpinionOfPlayerForce`. On success, `RequestFactory.GenerateNewRequest` creates a `PresenceRequest` and adds it to `PlayerForce.Requests`.
- `RequestGenerationRate` (`SupplyRule`) throttles the whole petition economy. Both gates are linear in the governor's traits, so it scales only how often worlds petition, not which ones do. Sector-wide arrivals per week ≈ `governorCount × 0.125 × RequestGenerationRate`; at the shipped 0.006 that is ~0.6/week for the ~800-governor production sector, holding ~13 petitions open at a time.
- The deadline comes from `SupplySeverityDeadline`, keyed by the `RequestSeverity` that `ClassifyRequest` derives from the local threat ratio: Concerned 39 weeks, Serious 26, Desperate 13, Existential 13. It is deliberately a property of the petitioning world, not of where the Chapter's forces are — the Chapter may be spread across several task forces, so there is no single position to measure against, and keying off the nearest asset would tighten every deadline as the player expanded. Reachability instead falls out of geography: a round trip costs 4 weeks of system transit before any warp travel (`TaskForce.SystemTransitWeeksPerEnd`), so a short fuse is implicitly a proximity requirement and only urgent petitions near a standing force can be answered.
- Severity is classified before the commitment package is built, so `ForceCommitmentPackage.CompletionDeadlineWeeks` carries the real fuse length and `RequestValueCalculator`'s throughput premium prices urgent petitions higher without any separate urgency term.
- Request valuation is data-driven through `SupplyEconomyRules`. The player sees squads, qualifications, service weeks, deadlines, progress, and the fixed offer; Battle Value and Battle-Value-Time remain internal accounting units. `GovernorTurnProcessor` advances request state, creates pledges on fulfillment, and applies opinion/cooldown consequences. `PledgeDeliveryProcessor` runs at sector scope because deliveries affect the Chapter economy and may originate from many worlds.

### 6.4 Mission Step State Machine

All steps implement `IMissionStep`:

```csharp
public interface IMissionStep
{
    string Description { get; }
    MissionStepPhase Phase { get; }
    bool ConsumesDay { get; }
    MissionStepResult ExecuteMissionStep(
        MissionExecutionContext execution,
        float marginOfSuccess,
        IMissionStep resumeStep);
}
```

Steps return their successor through `MissionStepResult`; `MissionStepDriver` executes this trampoline instead of allowing steps to recursively run an entire mission. `MissionDayScheduler` interleaves all active missions for up to six days. On each day it runs every `Shaping` step before any `Acting` step, and only `ConsumesDay` steps advance mission time. This makes interactions declarative at the step level rather than hardcoded by mission type.

`RegionFaction.CommittedAttention` is transient same-day state. Diversions draw a portion of the defender's remaining attention during the shaping phase; stealth, patrol, and interception steps consume the resulting exposure during acting, and the scheduler resets it at the next day boundary. `MissionReturnPolicy` determines whether a mission returns, holds captured ground, or remains static. Mission opening ranges interpolate between both sides' preferred ranges, so a successful ranged ambush opens farther away while a successful melee ambush opens close.

**Diversion effect channels.** `DemonstrateForceMissionStep` runs a daily Tactics check whose difficulty rises with the target's defender-held regional intel and *deployed strength* — deliberately reading total force present rather than the search-effort `WatchScore` the stealth checks use, because a feint has to be seen by a garrison that is actually there. Accumulated Impact feeds two independent transient channels:

- `PerceivedThreatBonus` on the target `RegionFaction`, set to a superlinear `apparentThreat = manpower × (1 + impact/scale)²`, inflating the garrison the controller feels it must hold.
- `ProvocationLevel` on the feinting force at Normal aggression or higher, which lowers the AI's force-ratio threshold for attacking (toward parity) and biases target selection — baiting a counterattack. Because the feint force stands in the open, it is pulled into the resulting fight as a defender.

Both are set during the shaping phase, consumed by faction planning in the same turn, and cleared by `ClearDiversionEffects` before the turn ends. **Neither is ever persisted** — they must not appear in the save schema. This same-turn lifecycle is why an AI-generated feint *against the player* cannot reuse this mechanism: the player commits orders before `ProcessTurn` runs, so it would have to become a one-turn-lagged intelligence deception instead (see PRD §5.7).

Step chains by mission type:

| Mission Type | Step Chain |
|---|---|
| Any (cross-region) | `InfiltrateMissionStep` → main initial step |
| Recon | `ReconStealthMissionStep` → `PerformReconMissionStep` (loops 6 days) → `ExfiltrateMissionStep` |
| Advance | reciprocal assault check → `PrepareAssaultMissionStep` → battle |
| Ambush / Extermination | `PositionAmbushMissionStep` → `AmbushBattleStep` |
| Assassination | `AssassinateStealthMissionStep` → `AssassinateBattleStep` |
| Sabotage | `SabotageStealthMissionStep` → `PerformSabotageMissionStep` (loops 6 days) → `ExfiltrateMissionStep` |

Mission force topology defaults to `UnifiedForce`. Recon explicitly uses `IndependentSquads`: every assigned squad receives its own `MissionContext`, stealth checks, interception state, battles, field experience, and soldier outcome record. The shared `Order` is only an organizational/reporting container. The end-of-turn view groups those element contexts back into one order-level recon entry while retaining squad/day attribution and each battle replay. Raids, assassinations, sabotage, advances, and other mass-force missions continue to resolve all assigned squads in one unified context.

Mission continuation thresholds measure casualties relative to the combat-capable members present when each `BattleSquad` mission element is created, not the squad template's maximum roster. An under-strength squad therefore begins at 100% mission strength; subsequent losses are compared with that starting force according to the order's aggression setting.

At the start of each day's acting phase, `MissionTurnProcessor` pairs exact reciprocal Advance orders once both forces have reached `PrepareAssaultMissionStep` in the same target region. `ReciprocalAssaultResolver` substitutes one shared field battle for their two independent assaults, gives both sides the Attacker battle role, and never reads regional entrenchment. A withdrawal is only that day's result: a force with combatants remaining and cumulative losses still inside its aggression threshold reforms and contests again on the next day. A nonviable driver's chain ends; the survivor retains `PrepareAssaultMissionStep`, so the scheduler cannot run its attack on static defenders until the next day. An inbound counter-assault therefore spends its approach day while a local assault may hit defenders, then interrupts that assault once it arrives. Casualties in the shared battle belong to the already-committed formations and are not deducted again from either static regional military pool.

Detection during any stealth phase routes to `DetectedMissionStep`, which dispatches to `AmbushedMissionStep` or `MeetingEngagementMissionStep` depending on context.

**Shared stealth/infiltration/exfiltration difficulty formula** (`MissionStealthDifficulty`):

Stealth difficulty scales with how hard enemies are *looking*, not with how many of them *live* in the region. The model replaced a `log10(deployed strength)` term built on `Garrison`, which had become an Imperium-only concept among public factions: for a `PopulationIsMilitary` horde `GetDeployedStrength()` resolves to Population, so the old term meant "how many creatures live here" and, at `MissionCheck`'s 0.2σ per difficulty point, spanned 1.6σ while every other lever combined spanned ~0.6σ. Mass was not *a* factor in the check — mass *was* the check.

Detection is a property of the **region**, not of the mission's chosen target, so both terms sum over `Region.GetDetectingEnemyFactions()` — the same set `Region.SelectSpotter` draws the interceptor from, so difficulty and interceptor always agree on "the enemies present".

```
WatchScore(rf) = SurveillanceWeight × rf.GetOwnRegionIntel()
               + Magnitude(rf.GetPatrolStrength())
               + min(AmbientSearchCap, AmbientWeight × Magnitude(staticStrength))

staticStrength = max(0, rf.GetDeployedStrength() − rf.GetPatrolStrength())
Magnitude(x)   = x ≤ 0 ? 0 : log10(1 + x)

difficulty = Σ WatchScore(enemy) + Magnitude(intruderHeadcount) − intruderRegionIntel
```

Skill is compared against difficulty, normalized to a z-score: `(skill − difficulty) / 5.0`.

| Constant | Value | Role |
|---|---|---|
| `SurveillanceWeight` | 0.5 | Weight on a faction's own regional intel (listening posts, informants, its own past recon). |
| `AmbientWeight` | 0.5 | Weight on fielded troops that are *not* out searching — half the patrol term, because standing in a region is a fraction as useful as sweeping it. |
| `AmbientSearchCap` | 1.5 | Ceiling on the presence term. Load-bearing: without it the mass term alone spans 0..4 (0.8σ) and re-creates the failure the model exists to fix. |

`GetPatrolStrength()` counts squads on `Patrol` or `Recon` orders only — the two orders whose whole content is "cover ground and report what you find". Every other mission type counts as static: a squad fortifying, assaulting, or holding an objective is an obstacle in the region, not a sweep of it.

**Units.** `GetPatrolStrength()` and `GetDeployedStrength()` are both **battle value**, not headcount, because they are subtracted from each other. `Garrison`/`Population` are BV pools (`RegionFaction.AddMilitaryStrength`: *"forces are raised, lost, and returned in the same currency"*), and `FactionStrategyController` seeds `SpareTroops` from `GetDeployedStrength()` then decrements it by `SquadBattleValue`. Summing `Members.Count` for the patrol term would subtract headcount from battle value and compute the patrol term one to two orders of magnitude below its calibrated scale. BV also reads correctly on its own terms: a patrol's worth as a search is not only how many pairs of eyes it has, but how well equipped and trained they are to use them.

**Why `1 + x` inside every log.** It makes every term ≥ 0 by construction. Zero maps to exactly 0 rather than `−∞`, so an empty region yields difficulty 0, and there is no path by which a difficulty of `−∞` becomes a margin of `+∞` and hands an intruder an automatic success. That failure mode was patched in one mission step after another (via `Max(1, …)` guards) before being fixed in the shape of the formula instead. `MissionStealthDifficulty.TroopMagnitude` retains the older unshifted `log10(max(1, x))` form, which is used **only** for order-of-magnitude mission-size banding in `PlanetTurnProcessor` (where 1,000,000 must band to exactly 6), never for difficulty.

**Deliberately not on this model.** `PerformAssassinationMissionStep` and `DemonstrateForceMissionStep` scale with `GetDeployedStrength()` directly. They ask "how well guarded is this target" / "is there a garrison here to be feinted at", not "who is looking for me", so total force present is the right quantity for both.

Calibration (intel 2, no intruder terms): empty region `0.00`; 5,000 BV idle `2.50`; the same 5,000 with 500 BV on patrol `5.20`; a dormant 10⁷ horde `2.50`.

### 6.5 Mission Checks

Three check types implement `IMissionCheck.RunMissionCheck(List<BattleSquad>, IRNG)`:

| Type | Skill Source |
|---|---|
| `IndividualMissionTest` | Single highest-skilled soldier across all squads |
| `LeaderMissionTest` | Squad leader with highest skill; falls back to `IndividualMissionTest` if no leader present |
| `SquadMissionTest` | Average skill across all able soldiers |

All checks: `zAdvantage = (skillValue − difficulty) / 5.0`, then `GaussianCalculator.DetermineMarginOfSuccessZvalue(zAdvantage, random)` consumes the injected random stream and returns a signed float (positive = success, magnitude = degree).

### 6.6 Battle System

**Key classes:**

| Class | Role |
|---|---|
| `BattleGridManager` | Owns the 2D grid; tracks cell occupancy; resolves movement |
| `BattleSoldier` | Runtime battle state per soldier: position, equipped weapons, aim, speed, stance, turn counters |
| `BattleSquad` | Wraps a `Squad` with `List<BattleSoldier>`, cover modifier, melee state |
| `BattleTurnResolver` | Drives one full battle turn; fires `OnBattleComplete` when done |
| `BattleHistory` | Stores `List<BattleTurn>`, each with a state snapshot and `List<IAction>` |

**Loadout allocation (`BattleSquad.AllocateEquipment`):**
- Iterates members, allocating weapons from the squad `Loadout` (weapon sets).
- One-hand weapons allow dual-wielding; two-hand weapons consume both slots.
- A one-hand ranged weapon leaves the off-hand available for a one-hand melee weapon.
- Equipped weapons are bound to physical hand groups. Disabling an arm or hand drops the weapon gripped by that group; two-handed weapons require two functioning groups and drop if either group is disabled.

**Hit location resolution:**
- `HitProbabilityMap` is a 3-element array for short, medium, and long range bands.
- A random value is drawn against the weighted sum of all location probabilities for the applicable range band.

**Accuracy formula (`ChosenRangedWeapon.GetAccuracyAtRange`):**
```
accuracy = weapon.Accuracy
         + soldier.GetTotalSkillValue(weapon.RelatedSkill)
         + (2.4663 × log(2 / range))
```
The log term produces a sharp drop-off at range.

**Damage formula (`ChosenRangedWeapon.GetStrengthAtRange`):**
```
strength = weapon.DamageMultiplier × (1 − range / weapon.MaximumRange)
```
Strength after armor reduction is compared against wound thresholds to determine severity applied to the struck `HitLocation`.

**Ranged planning and friendly fire:**

- `BattleSquadPlanner` scores candidate attacks in Battle Value rather than selecting a random member of the nearest squad. The common shape is `imminence × expected enemy BV removed − expected friendly BV lost`; imminence discounts enemies that cannot engage soon without double-counting their threat, since target BV already represents combat value.
- Candidate acquisition is shared by conventional, cone, and blast paths. Enemies are ranked once and capped by `RangedCandidateEvaluationCount` (6), preventing the three weapon paths from independently choosing unrelated fields. Blast delivery is evaluated over deterministic normal quadrature and angle samples; both enemy benefit and friendly/self cost use the same scatter distribution.
- **Take-out probability is the per-hit quantity.** Ranged, cone, blast, melee, and friendly-fire scoring all price a landed hit through `CalculateTakeOutProbabilityOnHit`, which mirrors the resolver rather than approximating it: the stance-weighted hit-location lottery, weapon and natural armor, the wound-level ladder applied against each location's `CrippleWound`/`SeverWound` threshold **including wounds already accumulated there**, the motive/vital restriction, and the last-functioning-hand rule, integrated in closed form over the real `N(3.5, 1.75)` damage roll. This replaced a clamped linear wound ratio, which was indifferent between concentrating and spreading damage and therefore overpaid for grenades sprinkling thin damage across loose clusters; incapacitation is a threshold event, so preferring concentration and finishing off wounded enemies is now emergent rather than tuned. Because the estimator reads live wound state, chip damage still scores — it moves a location toward its threshold. `EstimateProjectedMeleeBattleValue`'s survival-product composition was already correct and was left alone; only its input changed. Burst sizing (`CalculateShotsToFire`) targets take-out confidence in the same currency.
- Shooting into a melee scrum applies `RangedFriendlyFireRules.FiringIntoMeleePenalty` in both planning and resolution. A miss inside the narrow near-miss band may strike another scrum participant, selected by footprint-size weight. `ShootAction` records the actual victim and whether the result was friendly fire so aftermath and replay do not credit or narrate the nominal target incorrectly.
- An engaged soldier compares his planned melee sequence against a point-blank ranged action in the same BV currency. The ranged option pays the firing-into-melee and weapon-`Bulk` penalties plus the expected self-BV cost of giving up parry against adjacent attackers.
- General line-of-fire tracing through friendly formations is not implemented; the scrum distribution is intentionally reusable when terrain and fire lanes are introduced.

**Template weapons:**

`RangedWeaponTemplate.TemplateType` selects normal fire (`0`), a cone (`1`), launched blast (`2`), or thrown blast (`3`). `AreaRadius` carries cone half-width or blast radius, and `FuelPerBurst` carries cone fuel consumption. Template attacks pre-resolve geometry, victims, scatter, and wounds once; replay reuses the stored result rather than consuming new randomness.

- `ConeTemplate` projects the weapon's full-range cone along the shooter-to-target direction. A combatant is caught when any occupied footprint cell lies inside it. `AreaAttackAction` auto-hits every caught friend or foe except the shooter, applies normal armor/hit-location/wound resolution per victim, and consumes `FuelPerBurst`. The planner scores the entire firing line and never aims a cone weapon.
- `BlastTemplate` resolves an aim cell, then converts a failed normal-curve skill check into margin-proportional scatter in a pre-resolved random direction. A combatant is caught when any footprint cell lies inside `AreaRadius`; the thrower is not excluded. `BlastAttackAction` scales damage quadratically from full at the impact center to zero at the rim before armor.
- Thrown blast range is `Strength × MaximumRange`; launched blasts use `MaximumRange` directly. `WeaponSet.GrenadeWeapon` is a third ranged slot, and grenades use the ordinary loaded-ammo/reload action economy rather than a separate inventory count.
- The planner scores a throw as expected enemy BV removed minus expected friendly/self BV lost, **integrated over both the delivery scatter distribution and the per-victim damage roll** (`BlastThrowEvaluator.EvaluateThrow`): every miss node lands the template somewhere and pays its friendly cost, so a throw that only catches the squad when it scatters is not free. This replaced an earlier perfect-impact-times-delivery-confidence estimate; `deliveryConfidence` now survives only as a trace field, and neither half carries an arrival-time discount, since the grenade detonates this turn (matching the conventional ranged path — engagement-scoring Phase 3). A grenade must also beat the soldier's best conventional action. Two tie-breaks keep grenades from displacing ordinary fire: **ties go to the gun**, and a **melee-engaged soldier never throws**. Empty grenades restock through the normal reload branches on idle turns rather than a separate resupply path. Movement options price the actual Bulk/aim transition directly, so a separate movement-retention threshold is unnecessary.
- `BattleValueCalculator` values cones and blasts through density-scaled expected victims, fuel/ammo duty cycle, template reach, blast falloff, and the same reference-threat panel used for conventional weapons. A grenade is valued as a sidearm (`max(primary, grenade)`), matching the planner's mutually exclusive throw-or-shoot choice.
- Remaining template/ranged work is tracked in `Design/Active/RangedCombatFollowUps.md`.

**Melee resolution.** Attacks per melee action are `AttackSpeed/10 × weapon.AttackSpeedMultiplier`, with the fractional remainder resolved probabilistically in `MeleeMath`. `AttackSpeedMultiplier` replaced the old `ExtraAttacks` column; all shipped values are currently `1.0`, leaving per-weapon speed differentiation as an unused data lever. Dual wielding two one-handed melee weapons grants one off-hand strike using the off-hand weapon's own profile; its defensive value comes entirely from weapon `ParryModifier`s summed across equipped weapons (the unarmed fist is `−1`), with no flat dual-wield bonus — an early flat `+1` was removed because it stacked a free defensive bonus on top of the evasion that already models Tyranid natural weapons. `BattleSquadPlanner.BuildStrikePlan` distributes strikes across adjacent enemies, committing to one target until cumulative take-out confidence reaches 75% before moving on.

The contested melee roll is calibrated to tabletop's intuition band rather than to raw skill differences: `MeleeDefenderAdvantage = 0` (equal skill trades at ~50%, tabletop's "hit on 4s") and a per-side roll σ of `6`, making each skill point worth ~5.6% near parity and compressing large gaps toward tabletop's clamped 33–67% ladder — a Genestealer runs ~72% out / ~28% back against a marine. `StrengthMultiplier` values are doubled against dialed-down heavy-tier `WoundMultiplier`s; the deliberate balance stance is that base marines are two-wound soldiers.

**Battle Value derivation.** `BattleValueCalculator` is an engine-faithful valuation, not a stat-line heuristic: it replays the real to-hit/damage math (recoil decay, aim-vs-fire arbitrage, single-target overkill caps, ammo duty cycle, melee closing and engagement limits) against a four-profile reference threat panel — swarm chaff, light infantry, elite infantry, monster — to derive expected kills per turn and survival turns, then computes `BV = 5 · √(offense × durability) · command`. `SoldierTemplate.BattleValue` rows are generated from it (PDF Trooper 5 as the anchor; Tactical Marine 10, Genestealer 14, Hive Tyrant 95) and the `StrategicCombatRules` BV anchors track those values. An offline `Compute-BattleValue.ps1` harness reproduces the calculator to 6-decimal parity for bulk regeneration. **Player-soldier BV intentionally remains the template guideline rather than a live skill-tracking value** — enemy forces size their responses by estimating the player force, not by reading concrete data on every marine.

**Squad placers:** `AmbushPlacer` and `AnnihilationPlacer` handle initial squad placement for their respective engagement types. Starting range is modified by `marginOfSuccess` from the preceding stealth check.

`Species.MeleeEvasion` participates in the contested melee roll and `Species.RangedEvasion` is a flat penalty in shooting and planner distance estimates. `SpeciesAbilities.Burrow` moves eligible squads adjacent to the nearest enemy after ordinary placement, preserving valid footprints and letting ambushers erupt into melee; the same capability permits immediate tactical disengagement during withdrawal.

**Squad engagement planning.** Movement posture is selected once per squad; weapon, target, grenade, aim/reload, point-blank, and melee choices remain per soldier. `BattleEngagementFrameBuilder` derives current-capability profiles and builds both force frames from the same frozen state: pairwise geometry and allocation weights, a primary counterpart and deterministic baseline posture, withdrawal/pursuit masks, and capacity-limited screening assignments. Profiles use the able roster, functioning grips, currently usable weapons, ammo/readiness, movement and footprint/contact capacity. `SoldierTemplate.MeleeFraction` is the canonical doctrinal input generated beside BV, but a lost heavy weapon or exhausted/disabled loadout changes the runtime role immediately.

`BattleSquadPlanner` scores the legal semantic options (`Hold`, walk back/forward, jog toward, close-to-contact, pursuit run, and assigned interpose) directly in raw Battle Value: immediate outgoing minus friendly fire plus readiness, minus allocated option-dependent incoming, plus contact melee and the discounted bounded continuation, plus screening value, minus contact/whole-squad-lock cost. Current outgoing calls the same deterministic ranged/template/blast evaluators Layer 3 uses and caps combined shooters at each target's remaining BV. Current incoming is allocated across likely targets and includes the candidate's feasible declared speed. Semantic hysteresis is an **indifference rule, not additive BV**: candidates within a small fraction of the best score (`EngagementIndifferenceFraction`, scaled by squad BV) prefer the previous turn's option kind, so an incumbent posture cannot buy its way past a materially better plan. Destinations never participate in that identity, since an interpose point and a quarry position move every turn.

**The scored unit is an executable root-turn action policy, not a movement enum.** Evaluating a posture and then independently reconstructing soldier actions is not planning parity: it let scoring award a jog the value of a theoretical shot while action construction independently preferred `Aim`, which is illegal at a jog, so the soldier did nothing. Each candidate therefore carries an ordered, immutable list of `PlannedSoldierAction` descriptors (`Shoot`, `Aim`, `Reload`, `Ready`, `AreaAttack`, `BlastAttack`, `None`) recording soldier, target, weapon, range, shot count, Bulk/Aim multipliers, and expected BV terms. Selection sees only actions legal under that candidate's tier, and the winning descriptors are materialized after the declaration barrier without a second tactical vote — execution may reject a structurally invalid target but may not substitute a different preference. Proposals are ordered by soldier id so the target-cap reduction is float-stable. Movement and melee remain candidate-level branches.

Incoming fire uses no invented defensive-speed multiplier. For each plausible `(attacker squad, target squad, declared target speed, attacker Bulk)` tuple the planner runs a bounded set of real shooters, loaded weapons, and nearest viable targets through the same hit/range estimator as live shooting, feeding the candidate speed to `CalculateRangeModifier`; geometry stays pre-movement because shooting precedes movement. Results are memoized in the per-turn planning context. Pair-allocation weights still approximate which squad receives a volley, and template/blast response plus exact enemy target-policy best response remain bounded approximations.

Continuation is a bounded two-node policy maximum rather than a repeated range delta: at each future node the focal squad re-chooses among aggregate Hold, jog-and-fire, and Run branches, so `Hold→Run`, `Run→Hold`, and `Jog→Run` are actually comparable. The root turn stays exact and executable; future nodes use cached capability aggregates and never reach per-soldier target selection. The horizon boundary rewards attainable action value — usable offensive BV discounted by turns to the relevant firing/contact band — so a squad with no usable offense earns nothing for merely closing. Pursuit projects the quarry running away rather than applying the ordinary closing baseline.

**Pursuit fire-window policy.** The Xibarrus pursuit review established three linked rules. First, ranged pursuit's Run term is continuous across the preferred band: it is zero at `PreferredBandLower`, full at `PreferredBandUpper`, and interpolated between them, then multiplied by the fraction of net closing speed left after subtracting the quarry's withdrawal speed. This preserves a reason to run outside the band without creating a one-yard score cliff at its edge. Second, a Pursuit `Hold` candidate receives a separate `FireWindowValue`. It projects the quarry opening at its declared run speed for `PursuitFireWindowTurns = FullAimBonusTurns + 2` (five turns: four fresh `Aim` actions followed by the full-aim shot), evaluates each shooter's actual weapon/target choice at that projected range with `Accuracy + FullAimBonusTurns + 1`, and discounts the resulting expected removal with the normal continuation discount. The value is zero when no positive, worthwhile full-aim shot survives that projection, so a squad must run until the target will still be a viable shot at the end of the cycle. `ENGAGE_EVAL` exposes this term as `fire_window`.

Third, pursuit Hold has fire-cycle hysteresis. If the squad chose `Hold` last turn and any member still has a viable `Aim` on the current primary quarry, the legal option set is `[Hold]`; movement would clear the invested aim and restart the cycle. The commitment releases when `ShootAction` fires and clears the aim, or when the existing aim-viability checks reject the target because it died, left weapon range, became a bad shot, or an emergency closer invalidated the commitment. This is state-based rather than a second independent timer, so the squad cannot alternate Run/Hold while an actual aimed shot is maturing. These rules are pursuit-only and do not weaken the separate `Standoff` invariant: standoff exists only without a meaningful speed advantage and therefore only holds and fires.

A cloned grid acts as the local reservation overlay, so blocked destinations and large footprints receive only their real displacement/contact capacity without mutating live occupancy. Charge is the payoff step of the closing option: only contact-reaching members create current melee value, closing fire and forfeited defense are charged along the approach, and one member making contact prices the existing whole-squad melee lock. Future samples use capability groups and pair weights only; they never call per-soldier target selection. The candidate controls the first transition; later steps close to the preferred band, hold inside it, or back away below it, clamped at contact/band boundaries.

Planning runs as five staged passes with one bounded parallel level: build the paired force frame and role constraints serially from the frozen turn state; evaluate `(side, squad)` jobs concurrently, including each squad's candidate action policies; store results by stable job index; declare chosen postures serially in side/squad order; then materialize planned root actions and movement reservations serially in side/squad/soldier order. Workers consume no RNG and mutate no soldier, grid, reservation, action bag, or live diagnostic stream, so concurrent memo misses may duplicate work benignly while degree-1 and parallel runs still produce identical options, descriptors, and actions. Current shooting therefore observes both sides' declared feasible speeds regardless of planning order. Routing and force-assigned bounds bypass scoring. Force withdrawal heading, cover/rear-guard assignment, and pursue/break-off commitment remain force decisions; their roles mask the squad option set, and pursuit runs through the same masked candidate search rather than a separate posture/action API. A normal squad may step back for spacing but cannot independently run away — leaving the fight belongs to routing or force withdrawal. `ENGAGE_EVAL` emits one row per candidate with its BV term breakdown, chosen option, and margin over the runner-up; `SCREEN_EVAL` records screen capacity and counterfactual loss. Cross-turn semantic state (`LastEngagementOptionKind`, `LastScreenThreatSquadId`, `LastProtectedSquadId`) is cloned in battle snapshots, but absolute destinations are never retained.

This design retired the per-soldier movement vote and its `EngagementIntent`/`EngagementReason` enums, `AssessSoldierEngagement`, `ResolveWeakRangedEngagement`, the `WalkBulkShootingRetention` and `EngagementChargeMargin` constants, and the hard `Shaken` clamp on advance votes, which is now a score penalty on forward options. `BattleForcePlanner`'s standalone withdrawal action builders (`PrepareFightingWithdrawal`, `PrepareRearGuardWithdrawal`, and the cover/rear-guard/standing/advance/retreat soldier preparers they called) are deleted: withdrawal actions come out of the same masked option search as everything else, and `BattleForcePlanner` is now a static holder for the force-level geometry the resolver calls while building role constraints (`SelectWithdrawalHeading`, `BuildCoverCandidates`, `SelectCover`, `GetHeadingVector`). Removing the standing/moving preparers also retired the intra-squad worker-plan parallelism they carried; the one bounded parallel level is the `(side, squad)` job pass described above.

**Engagement range primitives.** There is now exactly one engagement-range model, `RangedEffectivenessCurve`: expected battle value removed per turn as a smooth function of range, in the same `hit × (takeOut + λ · woundProgress) × BV` currency as immediate fire. It replaced four parallel approximations (`EstimateHitDistance`, `EstimateKillDistance`, `CalculateOptimalDistance`'s `min(hit, kill)` construction, and `CalculateOpeningDistance`'s hit-limited/wound-limited disambiguation). Two questions are asked of it:

- `BattleModifiersUtil.CalculateOptimalDistance` — the outer edge of a soldier's useful band, the largest range where it is still at least half as effective as at its best. `BattleSquad.GetPreferredEngagementRange`/`GetPreferredOpeningRange` average it and feed force-level reach gates, meeting-engagement, and ambush placement.
- `BattleSquadCapabilityProfile.EffectiveEngagementRange` — mid-fight standoff, the argmax of `removal(r) − incoming(r)` against the opposing force, where incoming is the enemy's own curve plus an arrival-discounted melee term. For a non-degrading weapon against a penetrable target, removal only improves as you close, so standoff is set by return fire rather than by the gun.

Three properties are load-bearing:

- A curve has **no cliffs**, which is why one function now suffices. The old primitives returned a hard 0 when a to-hit total failed to clear 10.5, and returned the weapon's `MaximumRange` outright for any non-degrading weapon; the first is why a separate opening-range function had to exist to disambiguate that 0 by cause, and the second made "optimal distance" collapse to reach for most marine small arms.
- **A target that cannot be penetrated buys no standoff.** The curve's peak must clear a floor of one thousandth of the target's battle value per turn, otherwise the range is 0 (close). The Gaussian damage model has no hard impenetrability, so this floor is where the tail stops counting as a reason to choose a range.
- Immediate fire reads the **best engageable target**, while the force frame retains pairwise threat geometry and a separately weighted primary counterpart for movement. One tough body can no longer veto the whole squad's stance or erase a materially relevant threat on another flank.

Charge value is quoted in the same present-value currency as ranged fire and its closing exposure is integrated along the projected path. Lane spread biases target selection only and never alters the returned raw removal value, so it is unaffected by the squad scorer's temporal discount.

**Morale, withdrawal, and pursuit.** `BattleSideState` carries force intent (`Engaged`, fighting/rear-guard withdrawal, pursuit, rout, or disengaged), the withdrawal heading, covering/rear-guard assignments, and starting force metrics. Organized withdrawal alternates Cover and Bound roles; pursuers choose Break Off, Follow, Press, or Standoff behavior from contact and expected value. `WithdrawalForecast` compares projected BV preservation, including masked departure and command-collapse risk, before assigning an autonomous rear guard. Pursuit decisions retain their actual target-squad pairing. After movement, an unpursued withdrawing squad beyond every enemy's useful attack range disengages when pairwise relative closing speed puts the earliest possible interception beyond the engagement planner's two-turn retargeting horizon; this is an open-ground contact abstraction, not a battlefield edge. Running soldiers lose melee guard; Burrow can break contact immediately.

**Standoff invariant.** A pursuing force may enter `Standoff` only when it has no meaningful speed advantage over the withdrawing target (using the shared pursuit-speed tolerance), cannot reach melee this turn, and has a worthwhile shot available at the current range. `Standoff` means standing fire: every ordinary squad in that posture may `Hold` and aim/fire, but may not `JogToward` or `RunToward`. If movement toward the enemy is desired, the squad must receive a different pursuit posture or role; a standoff squad must never turn an unwinnable equal-speed chase into a running pursuit.

After each combat round, `BattleMoraleEvaluator` computes local shock from current/cumulative casualties, leader loss, nearby routing allies, and local outnumbering, then multiplies it by force-wide disadvantage. Per-soldier resolve is a convex Ego function. Synapse coverage skips the check; command auras reduce shock without granting immunity. Squads aggregate to `Steady`, `Shaken`, or sticky `Routing`; routing preempts the normal plan and enters the same pursuit, outcome, aftermath, and replay pipeline as voluntary withdrawal. Morale and withdrawal tunables live in code (`MoraleConstants` and the withdrawal planners) and are calibration surfaces rather than rules-data facts.

Battle completion produces a typed `BattleOutcome` with end reason, field holder, and disengaged/eliminated/routing/rear-guard squad ids. Typed `BattleEvent`s record withdrawal, cover, rear guard, pursuit, rout, and disengagement transitions for replay and narrative consumers.

**Battle continuation (`BattleSquad.ShouldContinueMission`):**

| Aggression | Continues if able soldiers ≥ |
|---|---|
| Avoid | 90% of template max strength |
| Cautious | 75% |
| Normal | 50% |
| Attritional | 25% |
| Aggressive | Always (until 0 able soldiers) |

### 6.6.1 Medical & Gene-Seed

`MedicalTurnProcessor` runs as the `ProcessMedical` step of `TurnController.ProcessTurn` and has two halves.

- **Natural healing.** Applies `Wounds.ApplyWeekOfHealing()` to every wounded player-soldier hit location regardless of deployment, *except* locations that require a replacement procedure — severed, or a crippled functional/vital location. `HitLocation.IsReplacementEligible` is the single source of truth for that exclusion and is shared with the Apothecarium view and the Squad Screen, so the three surfaces cannot disagree. Cadence and the daily Astartes pass are specified in §5.3.
- **Procedure resolution.** `ResolveProcedures` decrements weeks-remaining and, on completion, clears the location's wounds and removes the procedure. Cybernetic completion sets `HitLocation.IsCybernetic`; vat-grown leaves it clear. Because wounds are not cleared until completion, a marine under a procedure stays out-of-action automatically rather than needing a separate flag.

`MedicalProcedure` (soldier id, hit-location template id, `MedicalProcedureType { Cybernetic, VatGrown }`, weeks remaining, Requisition cost paid up front) lives on `Army` beside the Requisition pool and roster, and persists to a `MedicalProcedure` table keyed to `Soldier`. `MedicalProcedureService.TryAssign` validates eligibility, surgery site, co-located staff, and affordability, then deducts cost and creates the procedure; `EvaluateRequisites` returns the per-requisite breakdown the UI renders green/red. Durations and costs live in `MedicalProcedureRules`, never in UI literals. The gates are a co-located Apothecary **and** Techmarine (same ship or same region, checked only at procedure start) plus a valid surgery site — aboard a ship, or an Imperial/player-controlled Hive/Forge/Civilised region. No fortress-monastery is modeled, so a player-held region serves as the de-facto base.

**Apothecary field care.** `FieldCareService` converts an Apothecary's **Medical** rating into a daily wound capacity spent on the wounded he can reach. Treatment is a **forced wound-band demotion applied the day it happens**, not a credit settled at turn end — a brother hit in a day-2 assault and treated that evening enters the day-3 battle at reduced severity, which is the whole point, since battles read live wound state. All tunables live in `FieldCareConstants`, never the rules DB.

- **Reach** is the order: every wounded soldier in its assigned squads plus its attached soldiers. This is what makes order-level attachment the right shape (§5.6).
- **Capacity** is mildly superlinear in Medical rating and clamped, so a Master of the Apothecarion outworks an ordinary brother without replacing several of them.
- **Cost is flat in band *index*, not band value.** Wound bands are powers of 16, so a proportional cost would make severe wounds untreatable; a sub-linear surcharge covers extra wounds within a band, since a demotion moves the whole band at once.
- **Triage** is worst-first and deliberately not spread thin: severity by `Wounds.RecoveryTimeLeft()` — the *player-visible* number, so the order shown is the order run — then Rank desc, Subrank desc, then a random draw from the session RNG. Re-triaged after **every** treatment, so a demotion can hand the queue to someone else mid-day.
- **Greedy, no per-soldier cap, use-it-or-lose-it.** Re-triage self-levels: once the worst case drops below the next man, the queue reorders on its own.
- **Ceiling.** `IsReplacementEligible` is true from the *cripple* threshold up, so the worst wound field care can reach is the band below it. A brother who actually went down is a surgical case — surgery remains surgery.

Two seams, deliberately deduped. The mission pass runs on `MissionDayScheduler`'s scheduler-level `onDayEnd`, iterating **distinct `Order`s** — never mission elements, because `BuildMissionElements` fans one order into several single-squad drivers for `IndependentSquads` and a per-driver pass would make an Apothecary silently worth 3×. Garrison care runs the identical routine in `ChapterUpkeepProcessor.ProcessMedical` before the weekly cascade. **Field beats garrison by construction, not by rule:** an Apothecary under an order fails the "not on a mission" test defining the garrison pool, so the pools are disjoint and no man spends a day twice. Co-location resolves through `PlayerSoldier.EffectiveRegion`, since an attached Apothecary's home squad may sit on the ship while he is forward — `MedicalProcedureService.HasCoLocatedStaff` routes through it for the same reason.

Gene-seed recovery resolves once per confirmed-dead brother in `BattleTurnResolver.RemoveSoldiersKilledInBattle` (`ResolveGeneseedRecovery`), folding any recovered gland's purity into the chapter aggregate and writing a structured `SoldierEventType.GeneseedRecovery` event onto the preserved fallen-brother dossier; the battle log reads that recorded outcome rather than recomputing it. `PlayerForce` carries a count-weighted aggregate `GeneseedPurity` float alongside `GeneseedStockpile` — seeded pristine at founding, each recovered gland contributing a purity rolled around a baseline with small downward drift (`GeneseedRules`). Both persist on the extended `GlobalData` row. Stockpile drawdown happens in the recruitment pipeline (one unit consumed on Phase 0 → Phase 1; PRD §4.9).

### 6.7 Force Generation

`ForceGenerator.GenerateForce(ForceGenerationRequest, IRNG, IEntityIdAllocator)` dispatches by `ForceCompositionProfile`. The allocator is optional at persistent-campaign call sites; tactical missions supply a mission-local `TacticalEntityIdAllocator`, which issues negative IDs and therefore does not advance or collide with the positive campaign counters:

- **Generic (Garrison, AssaultForce, AmbushForce):** Adds one affordable HQ squad when the budget can still field at least three full non-HQ squads afterward. It then randomly selects among affordable non-HQ squad templates, cycling through unused affordable types before repeating so large forces become mixed formations instead of copies of the most expensive squad. When no full squad fits the remaining budget, it generates the highest-value partial squad that can fit. `TargetBattleValue` is a `long` (region garrisons can reach billions on hive/forge worlds). When generating a region's *defending* force, `PrepareAssaultMissionStep` caps the mobilized garrison at `MaxMobilizedGarrison` (10,000 troopers) so battles stay tabletop-scale; very large garrisons act as a deterrent to direct assault, to be engaged at scale by future bombardment/war-machine mechanics.
- **SpecialHQTarget:** Selects an HQ template by tier index from sorted HQ templates. Adds a bodyguard squad if `TargetBattleValue ≤ 0` and a `BodyguardSquadTemplate` is defined.
- **ScoutPatrol:** Randomly selects from Scout-flagged templates, generating `Tier` squads.

`SquadFactory.GenerateSquad(...)` populates a squad from template elements via `SoldierFactory.Instance.GenerateNewSoldiers(...)`, then resolves random weapon selections from `WeaponOptions`. Both randomness and temporary entity IDs are explicit dependencies on the tactical path; legacy persistent callers retain the campaign counters.

### 6.8 Chapter Generation

`NewChapterBuilder.CreateChapter(...)` runs once on new game creation:

1. Generate 1,000 base soldiers via `SoldierFactory`.
2. Wrap each in `PlayerSoldier` with a generated name from `NameGenerator`.
3. Simulate 104 weeks of training via `ISoldierTrainingService.EvaluateSoldier`.
4. Apply role-specific skill boosts via `ApplySoldierTypeTraining`.
5. Remove psykers into the Librarius path, where they are ranked by Ego and assigned relative seniority from the rules-data rank ratio. Build best-first candidate lists for the remaining soldiers with `RoleSuitabilityService`; specialists must cross their rating thresholds, and line/sergeant roles use their documented melee/ranged/leadership bands.
6. Populate chapter-level organizations in rank order: Chapter HQ, Librarius, Armory, Apothecarion, and Reclusium.
7. Derive company demand from seedable line squads. A company needs at least one eligible sergeant and four eligible members for a line slot before its HQ is staffed; otherwise its persistent HQ squad remains empty for later promotion/transfer.
8. Populate seedable companies in order, staffing the Captain and specialists before line squads so leadership is not consumed as line personnel. The Veteran Company requires a qualified veteran captain independently of its line-squad eligibility.
9. Run the explicit spill pass: surplus tactical candidates fill vacant assault seats, then surplus tactical/assault candidates with sufficient ranged aptitude fill vacant devastator seats. Existing assignments are never displaced.
10. Sweep remaining specialists into their chapter organizations and remaining soldiers into the Tenth Company, creating overflow Scout Squads as necessary.
11. Initialize the fleet with the first available fleet template.
12. Record a founding history entry.

All role lists share the `unassignedSoldierMap` as the single consumption authority, so one soldier may qualify for several roles but can only be assigned once. `ChapterGenerationTemplates` resolves the required rules objects once by stable template identity and fails fast when required data is missing or ambiguous. The detailed founding eligibility and ordering table is retained in `Design/Reference/FoundingRoleAssignment.md`.

### 6.9 Sector Generation

`SubsectorBuilder.BuildSubsectors(planets, gridDimensions)` clusters planets using a greedy merge. The sector grid is 200×200 light years with each grid unit representing 1×1 light year. A subsector has a maximum diameter of 20 light years (10 light year radius), typically containing 2–8 star systems.

Warp lane generation (0.7 addition): after subsector clustering, the highest-population planet in each subsector is designated its capital. A warp lane is established from each capital to every other planet in its subsector, and between each capital and the capitals of adjacent subsectors. The resulting lane graph is used by fleet movement routing (Dijkstra shortest path, weighted by Euclidean hop distance) to compute known multi-hop lane routes. Travel duration is determined by subsector relationship and Gaussian subjective/objective time multipliers rather than by Euclidean distance alone.

`FleetRouteCalculator` computes route topology and timing:

- `FleetRouteScope.SameSubsector`: 1 expected subjective warp week.
- `FleetRouteScope.AdjacentSubsector`: 3 expected subjective warp weeks.
- `FleetRouteScope.DistantSubsector`: 7 expected subjective warp weeks.
- Every journey adds 4 fixed subjective/objective weeks for in-system travel to and from warp translation points.
- Subjective warp multiplier: z = 0 maps to 1x, +0.5/-0.5 maps to 1/2x/2x, +1/-1 maps to 1/3x/3x.
- Objective warp multiplier: z = 0 maps to 1x, +5 maps to 1/10x, -5 maps to 10x.
- `FleetRoute.BaseTurns` is the objective total weeks rounded up for campaign turn processing.
- `TaskForce` stores resolved travel state rather than the full route graph: origin, destination, `FleetTravelPhase`, total and current-phase objective weeks remaining, rolled subjective warp weeks, rolled objective warp weeks, and a one-time subjective-training-applied flag.
- Route-based movement advances through `OutboundSystemTransit`, `InWarp`, `InboundSystemTransit`, and `InOrbit`. Legacy fixed-week movement remains a simple countdown path for tests and older callers.
- Turn training excludes embarked squads while their fleet is `InWarp`; when `AdvanceTravelOneWeek` reports warp exit, `FleetTurnProcessor` delegates `WeeklyTrainingPoints * WarpSubjectiveWeeks` training for embarked idle squads to `ChapterUpkeepProcessor`.
- Navigator quality modifying either Gaussian roll is a TODO for a later pass.

1. Assign each planet its own subsector.
2. Compute pairwise longest-distance between all subsector pairs.
3. While any pair is within `MaxSubsectorCellDiameter`: merge the closest pair and recompute affected distances.
4. Compute a bounding circle for each resulting subsector.
5. Assign grid cells to subsectors by closest-circle membership.

---

## 7. UI Layer

### 7.1 View / Controller Pattern

See Section 3.1. All events flow View → Controller → Model → Controller → View. Controllers never call Godot node APIs directly.

### 7.2 Screen Inventory

| Scene | Controller | View | Purpose |
|---|---|---|---|
| `main_menu_screen` | `MainMenuController` | `MainMenuView` | New game / load game |
| `main_game_screen` | `MainGameScreenController` | — | Top-level orchestrator; screen stack |
| `GalaxyView` | `GalaxyController` | `GalaxyView` | Sector map; planet selection; End Turn |
| `chapter_screen` | `ChapterController` | `ChapterView` | Order of battle; squad assignment |
| `soldier_screen` | `SoldierController` | `SoldierView` | Individual marine detail |
| `squad_screen` | `SquadScreenController` | `SquadScreenView` | Squad detail |
| `planet_detail_screen` | `PlanetDetailScreenController` | `PlanetDetailScreenView` | Planet info; fleet/troop management |
| `region_screen` | `RegionScreenController` | `RegionScreenView` | Region detail; order assignment |
| `apothecary_screen` | *(controller)* | *(view)* | Wound and geneseed management |
| `recruiter_screen` | *(controller)* | *(view)* | Training pipeline |
| `BattleReviewScreen` | `BattleReviewController` | `BattleReviewView` | Post-battle replay |
| `EndOfTurnDialog` | `EndOfTurnDialogController` | *(view)* | Turn summary |
| `order_dialog` | `OrderDialogController` | — | Inline order assignment sub-dialog |

### 7.3 Navigation Model

`MainGameScreenController` maintains a `Stack<Control>` (`_previousScreenStack`). Opening a sub-screen pushes the current screen onto the stack and hides it. Closing via `CloseButton` pops and restores the previous screen. The galaxy view is the root; all other screens are overlays managed through this stack.

---

## 8. Identified Technical Risks & Debt

### 8.1 Duplicate Mission Save — Bug (High) — RESOLVED

**Location:** `PlanetDataAccess.SavePlanetRegions` and `PlanetDataAccess.SaveMissions`

`SavePlanetRegions` contained an inline loop that inserted rows into the `Mission` table; `SaveMissions` was then called immediately after from `SavePlanet` and inserted the same rows again. (`Mission.Id` already carries a `PRIMARY KEY UNIQUE` constraint, so the second insert was a latent hard failure that only escaped notice because `SpecialMissions` is typically empty.)

**Resolution:** Removed the mission insert loop from `SavePlanetRegions`; `SaveMissions` is now the single persistence path. While reconciling this, two latent encoding bugs shared by both copies were also fixed: enum values were interpolated by name into INTEGER columns (now cast to `(int)`), and a null `DefenseType` interpolated to an empty string (now emits `null`). Covered by `MissionSaveTests` (see Section 9).

### 8.1.1 Order-Mission Persistence — Bug (High) — RESOLVED

**Location:** `GameStateDataAccess.SaveData` / `PlanetDataAccess.PopulateRegionMissions` / `UnitDataAccess`.

An `Order`'s `Mission` was only persisted if it happened to be in a region's `SpecialMissions` (the only source `SaveMissions` wrote and the only source the loader read into its mission map). Player-issued order missions (Recon, Advance, and the new Construction/Fortify) are created on the order and never enter `SpecialMissions`, so saving with any such order active wrote an `Assignment` row whose `MissionId` referenced an unsaved mission — `missionMap[missionId]` then threw `KeyNotFoundException` on load. Separately, the loader constructed `Order` objects but never assigned them to `Squad.CurrentOrders`, so even resolvable orders were silently dropped.

**Resolution:** The `Mission` table gained an `IsRegionMission` flag. `SaveData` now also persists each order's mission (with `IsRegionMission = 0`) when it isn't already saved as a region special mission, deduplicated by id. `PopulateRegionMissions` returns the full mission map (all rows) for order resolution and only re-adds rows with `IsRegionMission = 1` to `Region.SpecialMissions`. `UnitDataAccess` resolves orders against that full map and reattaches each loaded order to its squads' `CurrentOrders`. Covered by `SaveLoadRoundTripTests.SaveThenLoad_PlayerOrderWithNonSpecialMission_SurvivesRoundTrip`.

### 8.2 Specialist Assignment Bug — RESOLVED

**Location:** `NewChapterBuilder.AssignSpecialistsToUnit`

The per-company inner loop iterated `chapter.Squads` rather than `company.Squads` when distributing specialists within companies, so specialists were never correctly placed below the chapter HQ level. Low impact while no specialist roles were player-facing, but a latent correctness bug for Apothecaries, Techmarines, and Chaplains.

**Resolution:** Changed the inner iteration from `chapter.Squads` to `company.Squads`.

### 8.3 Hardcoded String-Based Lookups — Medium

**Location:** `PlanetTurnProcessor`, all `IMissionStep` implementations, `NewChapterBuilder`

Skills and templates are frequently looked up by name string (e.g., `s.Name == "Stealth"`, `st.Name == "Tactical Marine"`). A rename in the database silently breaks the lookup at runtime with no compile-time warning.

**Fix:** Introduce constants or a validated by-name lookup dictionary populated at rules-DB load time. Ideally, the load step asserts that all expected named entries are present and fails fast if any are missing, rather than producing a null reference at runtime.

**Update:** The initial training-profile migration moved work-experience training distributions and scout focus distributions into rules data. This reduces hardcoded skill-list coupling in `SoldierTrainingCalculator`, but does not close the broader issue. The remaining notable example is rating formulas that reference named skills.

**Update (validated skill registry):** `NamedSkillRegistry` (`Models/Soldiers/NamedSkillRegistry.cs`) resolves the base skills whose game-rule meaning is genuinely named — currently Stealth, Tactics, and Engineering (Fortification) — once at rules-DB load (`GameRulesData.Skills`), throwing a clear `InvalidOperationException` if any is missing or ambiguous. Mission execution projects Stealth and Tactics into `MissionRules`; individual steps no longer perform lookups or read global rules. Fist and Generic Melee are deliberately absent: unarmed combat derives its skill from the species-selected weapon template described below. Covered by `NamedSkillRegistryTests` and the mission execution tests.

**Update (validated chapter-generation template registry):** `ChapterGenerationTemplates` (`Models/ChapterGenerationTemplates.cs`) extends the same pattern to the player-faction soldier and squad templates that `NewChapterBuilder` referenced by name. It resolves the full set (Chapter Master, Captain, Champion, Ancient, the Librarius/Armory/Apothecarion/Reclusium specialists, Veteran, the Tactical/Assault/Devastator/Scout marines and their sergeants, and the Tactical/Assault/Devastator/Scout squad templates) once at rules-DB load (`GameRulesData.ChapterTemplates`), failing fast on a missing or ambiguous template. All ~26 `faction.SoldierTemplates`/`SquadTemplates` `First(... Name == ...)` lookups and the three `squad.SquadTemplate.Name == "..."` string comparisons in `NewChapterBuilder` now resolve through this registry (the comparisons are now reference-equality against the resolved template). Covered by `ChapterGenerationTemplatesTests` and exercised end-to-end by the new-game path in `SaveLoadRoundTripTests`.

**Update (validated sector-generation faction registry):** `SectorGenerationFactions` (`Models/SectorGenerationFactions.cs`) extends the pattern to the non-player factions `SectorBuilder` places by name. It resolves the infiltration-capable cult faction (Genestealer Cult) and the invasion faction (Tyranids) once at rules-DB load (`GameRulesData.SectorFactions`), failing fast on a missing or ambiguous faction. The three `data.Factions.First(f => f.Name == ...)` lookups in `SectorBuilder.GeneratePlanet` now resolve through this registry via role-named accessors (`Infiltrator`, `Invader`). Covered by `SectorGenerationFactionsTests`.

**Update (species-owned unarmed defaults).** Unarmed combat is now a rules-data relationship, not a battle-side default or a player/NPC distinction. Every `Species` row has a validated `DefaultUnarmedWeaponTemplateId`; the resolved `MeleeWeaponTemplate` supplies the attack profile and its own `RelatedSkill`. Space Marines currently select template 12 (Fist), while the other shipped species select the stat-identical template 15 (Generic Melee) to preserve their existing training and balance. Nothing restricts either template to Astartes or to a faction: an ordinary-human species can select the Fist template in data. The obsolete `BattleDefaults` registry and the named Fist/Generic-Melee skill dependencies were removed. Battle planning, attack resolution, defense, and aftermath XP all use the combatant's species default. Covered by `SpeciesDefaultUnarmedWeaponTests` and battle-aftermath tests.

**Update (geneseed progenoid flag).** The geneseed-status logic in `BattleTurnResolver` checked `hl.Template.Name == "Face"` / `== "Torso"` against a soldier's own body to decide whether a killed marine's geneseed was destroyed. This is now a semantic `HitLocationTemplate.HoldsProgenoid` flag: a new rules-DB column (added by the `migrate-progenoid` command in `RulesDbTool`, set for the Face and Torso locations) is read by `HitLocationTemplateDataAccess` and mirrored on the hardcoded test-fixture body templates. `GetGeneseedStatusDescription` now tests `hl.Template.HoldsProgenoid && hl.IsSevered`. Covered by a rules-DB validation test asserting exactly the Face/Torso locations carry the flag.

**Update (chapter instance-graph lookups).** `NewChapterBuilder` located *generated* units and squads by display name (`chapter.Squads.First(s => s.Name == "Librarius"/"Armory"/...)`, `chapter.ChildUnits.First(u => u.Name == "First Company"/"Tenth Company")`). Because a generated child squad inherits its `SquadTemplate.Name` and the veteran "First Company" and scout "Tenth Company" map to *distinct* unit templates (Veteran Company / Scout Company), these are now resolved by **template identity** rather than display name. `ChapterGenerationTemplates` gained the Librarius/Armory/Apothecarion/Reclusium squad templates and the VeteranCompany/ScoutCompany unit templates; the lookups became `s.SquadTemplate == templates.Librarius` and `u.UnitTemplate == templates.VeteranCompany`. Validated by `ChapterGenerationTemplatesTests` and exercised end-to-end by `SaveLoadRoundTripTests`.

**Update (rating/training skill references — fully data-driven).** The rating formulas in `SoldierTrainingCalculator.UpdateRatings` (and the award thresholds in `EvaluateSoldier`) previously indexed a by-name skill dictionary (`_skillsByName["Sword"]`, etc.) and hardcoded medal/flag tiers. Both are now data-driven (see §4.1.1 "Implemented" and `Design/Reference/DataDrivenRatings.md`): the formulas and awards live in rules tables, evaluated by `RatingCalculator`; `GameRulesData` validates the definitions at load. `SoldierTrainingCalculator.RequiredSkillNames` now covers only the two skills training still references by name directly (`Power Armor`, `Teaching`); rating-formula skills are validated transitively through the rating-component references. Covered by `RatingCalculatorTests`, `RatingDefinitionDataTests`, and `SoldierTrainingCalculatorValidationTests`.

With this, every named-lookup cluster in §8.3 has been moved onto stable keys / semantic flags / validated registries / data-driven definitions.

**Long-term direction:** Introduce stable rules keys and semantic flags where appropriate, plus validated registries populated at rules-DB load time. For tunable behavior, prefer data-driven definitions over constants. Candidate migrations include mission skill requirement definitions, sector generation faction roles, chapter organization role bindings, default battle resource definitions, and rating formula definitions. The load step should assert that all required entries are present and fail fast with clear diagnostics.

### 8.4 Dual Clone Paths on Battle Types — RESOLVED

**Location:** `BattleSquad`, `BattleSoldier`

`BattleSoldier` previously had both a copy constructor and a separately-maintained `Clone()` method, which had already silently diverged: `Clone()` deep-cloned the underlying `ISoldier`, while the copy constructor shared it. Production only ever used the copy-constructor path (via `BattleSquad.Clone()` → `BattleState`); `BattleSoldier.Clone()` was exercised only by a test, whose assertion locked in the unused deep-clone behavior — exactly the dual-maintenance hazard predicted here.

**Resolution:** Removed `BattleSoldier.Clone()` and its `ICloneable` implementation; the copy constructor is now the single copy path (it sets the cloned-squad back-reference that a parameterless `Clone()` cannot). The underlying `ISoldier` is shared by design — replay reads per-snapshot battle fields and the action log, not an independent body — and this is now documented on the copy constructor. `BattleSquad` retains `ICloneable` (required by `BattleState`); its `Clone()` already delegates to its single copy constructor. The regression test (`BattleSoldierCloneTests`) now exercises the copy constructor and asserts both the copied battle fields and the shared-soldier contract.

### 8.5 String-Interpolated SQL — RESOLVED

**Location:** `DataAccess` classes (GameState)

Save SQL was previously built via string interpolation, which broke on single quotes (escaped inconsistently with manual `Replace("'", "''")` calls) and risked float-locale issues on non-English systems.

**Resolution:** All GameState `DataAccess` save/update statements now use parameterized queries via `SqliteCommand.Parameters` (no interpolation or manual escaping remains). This also removes the SQL injection surface area.

### 8.5.1 Save/Load Provider Compatibility — RESOLVED

**Location:** `GameStateDataAccess`, `PlanetDataAccess`, `UnitDataAccess`, `PlayerSoldierDataAccess`, `SaveStructure.sql`

The save/load path was written against the older `System.Data.SQLite`/Mono provider and broke under `Microsoft.Data.Sqlite` 9.0 (the package the project actually references). The end-to-end round-trip test (Section 9) surfaced and fixed a cluster of latent breakages:

- **Connection string.** `URI=file:{path}` is not a valid `Microsoft.Data.Sqlite` keyword; replaced with a `SqliteConnectionStringBuilder` (`DataSource`). The schema-file path was also decoupled from Godot (`ProjectSettings.GlobalizePath`) so save/load is unit-testable; it is now an optional `SaveData` parameter that defaults to the Godot path.
- **Float reads.** `Microsoft.Data.Sqlite` boxes `REAL` columns as `double`; `(float)reader[i]` threw `InvalidCastException`. All such reads now use `Convert.ToSingle`.
- **Population reads.** `RegionFaction.Population` is `BIGINT`/`long` but was read with `GetInt32`, overflowing on large planets. Now `GetInt64`.
- **Region load ordering.** `GetPlanets` populated region factions/missions against a region map that was not loaded until later in `GetData`, throwing `KeyNotFoundException`. Those calls were moved to `GetData` after `GetRegions`.
- **Structural insert bugs.** `SaveOrder` inserted 7 values into the 6-column `Assignment` table (an extra `RegionId`) and encoded enums/bools as strings; `SoldierEvaluation` supplied 11 values for 12 columns (a vestigial `EvaluatingSoldierId` the model and loader never used — column removed from the schema); `SoldierAward` interpolated `Name`/`Type` unquoted; a misnamed `PlayerSoldierRamgedWeaponCasualtyCount` insert targeted a non-existent table.
- **Foreign keys.** `Microsoft.Data.Sqlite` enforces foreign keys by default. Two schema references could never resolve in the save database — `Mission.FactionId → Faction` (factions live only in the read-only rules DB) and `SoldierSkill → Soldiers` (a typo for `Soldier`). With both corrected, FK enforcement is now enabled on the connection (`ForeignKeys = true`); the save routines insert parent rows before dependents, validated by the round-trip test.

A separate `Date.CompareTo` bug (used reference equality, so it returned non-zero for equal-but-distinct `Date` instances and broke `IComparable`-based equality, sorting, and dictionary use) was fixed and `GetHashCode` added.

### 8.6 GameDataSingleton as Global Mutable State — Partially Mitigated

**Location:** `GameDataSingleton`

Mutated from multiple controllers without coordination. Acceptable in a single-threaded context, but makes unit testing difficult because any test touching a logic system that reads from the singleton must set up the full singleton first.

**Mitigation:** Pure-logic systems accept their inputs rather than reading global state. `TurnController` creates or accepts a `GameSession` containing rules, sector, date, and `IRNG`, and injects it into every processor under `Helpers/Turns`; `SimulationContext` owns each run's result, intel ledger, orders, and optional planet scope. Tactical execution continues that seam through two bounded contexts rather than exposing `GameSession` as a service locator: `MissionExecutionContext` carries mission state, projected mission rules, the injected RNG, a mission-local temporary-ID allocator, and a separate `BattleExecutionContext`; the battle context carries rules, the same RNG instance, and explicit aftermath dependencies. Mission checks, spotting, force generation, placement, planning, actions, hit-location rolls, and gene-seed rolls all consume the injected stream. `IPlayerBattleAftermathSink` makes roster removal, fallen-brother registration, recovered gene-seed, and chapter battle-history writes explicit campaign effects.

The simulation risk is now concentrated at the outer compatibility boundaries: most scene controllers still use the singleton, production still supplies the process-global `StaticRNG` adapter, persistent entity creation retains the campaign-wide positive ID counters, and older end-to-end tests intentionally seed `GameDataSingleton`. Tactical missions and battles themselves no longer read `GameDataSingleton` or static `RNG`.

### 8.7 IdGenerator Is Not Thread-Safe — Low

**Location:** `Builders/IdGenerator.cs`

Flagged in a TODO comment. Static fields `_nextOrderId` and `_nextMissionId` are incremented non-atomically. No issue in the current single-threaded model.

**Fix:** If async turn processing is ever introduced, switch to `Interlocked.Increment`. Until then, no action.

### 8.8 Dead Code: BattleMissionTemplate and OrbitalRaidMission — RESOLVED

**Location:** (removed) `Models/Battles/BattleMissionTemplate.cs`

These classes (`BattleMissionTemplate`, `OrbitalRaidMission`, the `IBattleMissionStepChallenge` stubs) were an earlier design pass for a data-driven mission template system, with hardcoded `true` challenge results and an `OrbitalRaidMission.RunMission` that never placed or resolved a battle. Nothing in the codebase referenced them.

**Resolution:** Deleted `Models/Battles/BattleMissionTemplate.cs`. Its only dependency, `Builders/TempArmyBuilder.cs`, was deleted with it (see 8.9).

### 8.9 TempNameGenerator Naming — RESOLVED

**Location:** `Models/Soldiers/NameGenerator.cs` (was `TempNameGenerator.cs`)

The "Temp" prefix implied placeholder status, but the generator is used in production code paths for all soldier and character naming.

**Resolution:** Renamed `TempNameGenerator` to `NameGenerator` (file and class), updated its callers in `CharacterBuilder` and `NewChapterBuilder`. `TempArmyBuilder` — only ever called from the now-deleted `OrbitalRaidMission` — was deleted.

### 8.10 Orphan Region Faction in Sector Generation — RESOLVED

**Location:** `SectorBuilder.FoundTakebackPlanet`

Sector generation left a region with a `RegionFaction` whose faction had no corresponding `PlanetFaction` on that planet — observed as a stray Space Marines (player) region presence on a hostile, Genestealer-Cult-controlled world, in a single region. `FoundTakebackPlanet` constructed the player `RegionFaction` with an inline `new PlanetFaction(playerForce.Faction)` that was never registered in `planet.PlanetFactionMap`, so the orphan existed in memory but could not be saved or reconstructed on load.

**Resolution:** `FoundTakebackPlanet` now registers a player `PlanetFaction` on the planet (reusing an existing one if present) before attaching the player `RegionFaction` to a region.

**Note:** `PlanetDataAccess.PopulateRegionFactions` retains a defensive fallback — if a saved `RegionFaction` references a faction with no `PlanetFaction` on the planet, it reconstructs a minimal one rather than failing the load. With the generation fix in place this is now belt-and-suspenders rather than a required mitigation.

### 8.11 PlanetBuilder Static Generation State — RESOLVED

**Location:** `Builders/PlanetBuilder.cs`

`PlanetBuilder` drew planet names without replacement from a finite list using a static `_usedPlanetNameIndexes` set, and held static id counters, none of which were reset between sector generations. Generating more than one sector in a single process (e.g. across a test run) eventually exhausted the name pool and the random-retry name-selection loop spun forever; it also made repeated generation non-deterministic for a fixed seed.

**Resolution:** Added `PlanetBuilder.Reset()` (clears the name set and resets the id counters) and call it at the start of `SectorBuilder.GenerateSector`, alongside the existing `RNG.Reset(seed)`.

**Follow-up — name draw replaced:** `Reset()` fixed accumulation *across* sectors but left the rejection-sampling draw in place, so a *single* sector needing more planets than there are names still spun forever. That became a live constraint when the ~1080-entry canon-derived name list was replaced with 1000 authored names, against a production sector of ~800 planets (200×200 grid at `PlanetChance` 0.02). The retry loop is now gone: `Reset()` builds and Fisher-Yates shuffles `_shuffledNameIndexes`, and `GenerateNewPlanet` pops from its tail in O(1) via `TakeNextNameIndex()`. Should a sector ever exhaust the pool, it reshuffles and names begin repeating rather than stalling. The shuffle runs after `RNG.Reset(seed)`, so generation remains deterministic for a given seed — but draw order changed, so a seed produces a different sector than it did before this change. Saved campaigns are unaffected: planet names are persisted through `PlanetDataAccess` and generation only runs on new game.

### 8.12 End-of-Turn Resolution Bugs — RESOLVED

**Location:** `Helpers/Turns/PlanetTurnProcessor.cs` (`UpdatePlanet`, `UpdateIntelligence`)

Surfaced while writing the `SectorEntityLogic` / multi-turn coverage (§9.2.1 #5, #8):

- **Collection-modified-during-iteration.** Three end-of-turn loops removed elements from the very collection they were iterating: depopulated `RegionFaction`s (over `RegionFactionMap.Values`), depopulated `PlanetFaction`s (over `PlanetFactionMap.Values`), and expired special missions (over `Region.SpecialMissions`). Any actual removal threw `InvalidOperationException`, so mission expiration and faction cleanup could crash a turn. Each loop now iterates a snapshot (`.ToList()`).
- **Governor logic was dead code.** `PlanetFaction.Population` is a get-only property hardcoded to `0` and never maintained, so the end-of-turn leader update was gated behind `planetFaction.Population <= 0` — always true. Every `PlanetFaction` was therefore stripped from the map each turn and `EndOfTurnLeaderUpdate` (governor aging, request fulfilment, and **request generation**) never ran. `UpdatePlanets` now derives the faction's planet-wide population by summing its `RegionFaction.Population` across the planet's regions, restoring the governor-request feature. (Removing the vestigial `PlanetFaction.Population` property is left as a follow-up.)

Covered by `SectorEntityLogicTests` and `MultiTurnSmokeTests`.

### 8.13 Production RNG Reproducibility — Medium

**Location:** `GameSession`, `StaticRNG`, save metadata, simulation processors

`GameSession` now makes the random dependency explicit and removes direct static RNG reads from turn processors, but production still injects `StaticRNG.Instance`, backed by one process-global sequence. The campaign seed and random-stream position are not persisted. Consequently, a save plus its orders cannot reproduce the next turn exactly, and adding one unrelated random draw can perturb every later seeded outcome.

**Planned direction:** introduce deterministic named streams derived from persisted campaign identity/seed, campaign turn, subsystem key, and—where appropriate—entity id. Examples include `turn/planet-growth/{planetId}`, `turn/faction-planning/{factionId}`, and `battle/{battleId}`. Stream-key/version metadata must be explicit so algorithm changes do not masquerade as the same reproducible simulation. Do not switch all consumers in one pass: preserve current draw order within each migrated subsystem, characterize results, then move one boundary at a time. `StaticRNG` remains the compatibility adapter until the migration is complete.

### 8.14 PlanetTurnProcessor Breadth — Medium

**Location:** `Helpers/Turns/PlanetTurnProcessor.cs`

The `TurnController` extraction succeeded, but its largest leaf still owns several independently evolving domains: organic/conversion growth and garrison drafting; Tyranid consumption and expansion; Cult maneuvers; Imperial remnants and emigration; revolt/civil-stability behavior; governor aging/requests/Requisition; and intelligence/opportunity generation. The class preserves important phase ordering, but its breadth makes unrelated changes collide and encourages more cross-domain helper methods.

**Planned direction:** retain a small `PlanetTurnProcessor` as the order-defining coordinator and extract domain processors beneath it: `PopulationTurnProcessor`, `ConsumptionTurnProcessor`, `CivilStabilityTurnProcessor`, `GovernorTurnProcessor`, and `IntelligenceTurnProcessor`. Share `GameSession`, `SimulationContext`, and `TurnIntelLedger`; do not duplicate state between processors. Extract one domain at a time behind the existing turn and generation tests, preserving enumeration and random-draw order.

### 8.15 Transitional Turn APIs and Dead Prototypes — Low

**Location:** `TurnController.Compatibility.cs`, `Helpers/OrderProcessor.cs`, focused tests/callers

The behavior-preserving controller split intentionally retained historical helper entry points in a compatibility partial. That made the refactor safe, but the shims should not become a permanent second API alongside the focused processors. `OrderProcessor` also appears to be an unused earlier orchestration prototype.

**Planned direction:** migrate tests and production callers to `TurnResolutionResult` and the focused owner for each behavior, then delete each compatibility member when its final caller is gone. Confirm unused prototypes with solution-wide reference search and remove them rather than documenting them as supported paths. This cleanup follows the processor/session migration; it must not be combined with behavior changes.


---

## 9. Testing Strategy

The `OnlyWar.Tests` xUnit project covers the pure domain and helper logic incrementally, without a full refactor. The sections below record the approach and the coverage to date.

### 9.1 Setup

The `OnlyWar.Tests` xUnit project references the game assembly and runs against the shipped rules database. Keep expanding it around pure domain and helper logic first. Systems with Godot node dependencies cannot be unit tested without a Godot runtime; focus the test project on pure domain and helper logic. Test parallelization is disabled assembly-wide (`[assembly: CollectionBehavior(DisableTestParallelization = true)]`) so suites that load the shared `GameDataSingleton` do not interfere.

Make `RNG` injectable: introduce an `IRNG` interface and a `SeededRNG` implementation so tests can run with a fixed seed for deterministic results. *(Implemented — `Helpers/IRNG.cs` with `StaticRNG` (production adapter over the global `RNG`), `SeededRNG` (own seeded instance), and a `FixedRNG` test double. `RatingCalculator`, end-of-turn processors, mission checks/steps, tactical force generation, battle placement/planning/actions, and aftermath consume injected `IRNG`; production supplies `StaticRNG` through `GameSession`.)*

### 9.2 Priority Test Targets

All of the targets below are now implemented; they are retained as a record of the initial test build-out, listed in the original recommended implementation order (lowest to highest setup cost). Ongoing work is tracked in §9.2.1.

1. **`Wounds` struct arithmetic** — Severity threshold transitions, `WeeksToHeal` computation, healing progress. Pure value logic, zero dependencies.
2. **`Skill.SkillBonus` and `Soldier.GetTotalSkillValue`** — Single-function math, no dependencies.
3. **`GaussianCalculator`** — Validate the margin-of-success distribution against known z-score expectations.
4. **`IMissionCheck` implementations** — Requires a minimal `BattleSquad` mock with a soldier list. No game state needed.
5. **`ForceGenerator`** — Requires a `Faction` object with squad templates. No Godot dependencies.
6. **`SubsectorBuilder`** — Pure spatial algorithm. Provide a list of positioned planets and assert subsector membership.
7. **`FactionStrategyController`** — Requires constructed `Planet`/`Region`/`RegionFaction` model objects. *(Implemented — `FactionStrategyControllerTests`; the controller takes `(faction, sector)` and reads no `GameDataSingleton` state, so no refactor was needed — see §9.2.1 #4.)*
8. **`BattleSoldier` clone round-trip** — Construct a fully populated `BattleSoldier`, clone it, and assert field-by-field equality. Catches 8.4 above. *(Implemented — `BattleSoldierCloneTests`.)*

### 9.2.1 Next Test Targets

Initial coverage now exists for wounds, skill math, Gaussian math, mission checks, force generation, subsector generation, battle-soldier cloning, rules database validation, training profile application, turn training flow, and save/load (round-trip and the mission-save regression). The next recommended targets are:

1. **Save/load round-trip tests** — *(Implemented — `SaveLoadRoundTripTests`.)* Generates a real new-game sector via `SectorBuilder.GenerateSector`, saves it through `GameStateDataAccess.SaveData` to a temporary SQLite file, reads it back through `GetData`, and asserts high-level state survives (date, planet/character/request/ship/squad/soldier counts, total population). This also serves as the new-game smoke test (target #9 below) and is the regression guard for schema drift: any schema change not propagated to both `SaveData` and `GetData` fails here. Surfacing and fixing the provider-compatibility cluster in §8.5.1 was driven entirely by getting this test to pass.
2. **Mission save duplication regression** — *(Implemented — `MissionSaveTests`.)* Drives `PlanetDataAccess.SavePlanet` against a freshly created save schema and asserts the `Mission` table holds exactly one row for a region with one special mission, plus field round-trip and null-`DefenseType` cases. Covers §8.1.
3. **Rules DB schema validation** — *(Implemented — `RulesDatabaseValidationTests`.)* In addition to the existing `TrainingProfile` coverage, the suite now constructs `GameRulesData` against the shipped database (exercising the fail-fast load-time validation for the data-driven rating/training tables and the validated registries) and directly asserts rating-table referential integrity (every award tier references a defined rating; every `SkillTotal` rating component resolves to a real base skill). Mission-definition tables remain future work (§4.1.1) and will get the same treatment when introduced.
4. **`FactionStrategyController`** — *(Implemented — `FactionStrategyControllerTests`.)* The controller already takes `(faction, sector)` and reads no `GameDataSingleton` state, so no refactor was needed; tests build `Planet`/`Region`/`RegionFaction` graphs and cover the empty-result cases (faction absent, hidden regions, no spare troops) and the development-construction path (spare troops spent on `ConstructionMission` orders).
5. **`SectorEntityLogic`** — *(Implemented — `SectorEntityLogicTests`, `SessionSimulationContextPrimitiveTests`, `GameSessionTurnControllerTests`.)* The end-of-turn domain logic lives in the `Helpers/Turns` processors and is driven through the `TurnController.ProcessTurn` orchestration facade over a compact hand-built sector (`SectorSimulationFixture`). Existing seeded tests protect turn behavior and random draw order; session/context tests cover dependency identity, per-run order isolation, null contracts, and a controller run whose date and RNG differ from `GameDataSingleton`. Domain coverage includes logistic growth, conversion growth (one default member converted per week), intelligence decay (×0.75/turn), stale special-mission expiration, and governor request generation against a public threat. Surfaced and fixed three latent bugs (see §8.12).
6. **`BattleGridManager` and `WoundResolver`** — *(Implemented — `BattleGridManagerTests`, `WoundResolverTests`.)* Grid tests cover placement/occupancy/reservation conflicts, movement (free-old/occupy-new and collision), removal, nearest-enemy/distance queries, open-adjacency selection, and clone fidelity. Wound tests cover the damage-ratio severity ladder, natural-armor subtraction, wound-multiplier scaling, already-severed short-circuit, and the vital-location-death / motive-location-fall event paths.
7. **Rating formula evaluator** — *(Implemented — `RatingCalculatorTests`, `RatingDefinitionDataTests`.)* Rating formulas and award thresholds are data-driven (§4.1.1); tests assert the evaluator's aggregation/normalization structure with a fixed `IRNG`, that the migrated definitions match the documented formulas, and that award tiers fire correctly (highest-tier-only, best-skill-in-category name interpolation, history flags).
8. **Seeded multi-turn smoke test** — *(Implemented — `MultiTurnSmokeTests`.)* Builds a compact single-planet sector (`SectorSimulationFixture`) with a conversion cult, a public rival controller, a governor, and a high-intelligence region, then runs twelve `ProcessTurn` cycles under a fixed seed and asserts high-level invariants survive: planet stays populated with no negative region populations, the default faction persists, the cult steadily recruits, intelligence decays toward zero, and the governor's aid request persists.
9. **New game smoke test** — Generate a new campaign from rules data and assert chapter, fleet, sector, subsector, planet, faction, and squad invariants without requiring the Godot UI.
10. **Godot scene-wiring smoke tests** — *(Implemented for 0.7.1 — `Scenes/Debug/release_scene_wiring_smoke.tscn`.)* Headlessly instantiate the main command scene and release-control overlays, verify required nodes resolve, and exercise top-level actions far enough to prove their event has a subscriber and opens the intended surface. These tests are intentionally shallow: their purpose is to catch visible-but-inert controls and broken scene paths, not duplicate controller/domain tests through Godot.

### 9.3 Regression Risk Areas

These areas are particularly likely to produce hard-to-detect bugs as features are added:

- Changing the `WoundLevel` bitmask layout or adding new severity tiers.
- Adding new fields to `BattleSoldier` without updating both the copy constructor and `Clone()`.
- Adding new tables to the save schema without updating both `SaveData` and `GetData` in `GameStateDataAccess`.
- Changing skill or template names in the rules database without updating hardcoded string lookups or validated registries (see 8.3).
- Changing the `Wounds.WeeksToHeal` nibble-offset encoding without updating all dependent healing logic.
- Adding new data-driven rules tables without adding rules-load validation and regression tests.
- Bumping the declared save-format version without a one-version migration, backup/failure test, and current-version round trip.
- Renaming or re-keying a deterministic RNG stream without an explicit stream-version decision and reproducibility fixture.
