# OnlyWar — Technical Design Document

**Version:** Alpha 0.7 (In Development)  
**Last Updated:** March 2026  
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

All scene controllers and helpers reach into this singleton for their data. This is a deliberate and accepted coupling point for the current solo-developer scope.

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

Save files are not version-migrated. `SaveFormat.CurrentVersion` is written to `GlobalData.SaveVersion`; discovery marks a different version as incompatible, and the data access layer rejects it before reading sector tables. Missing saves are opened in neither create nor write mode, preventing a failed load from leaving behind an empty SQLite file.

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
                      Organization, Entrenchment, Detection, AntiAir)
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

Soldier              (Id, SoldierTemplateId, SquadId, Name, Strength, Dexterity,
                      Constitution, Intelligence, Perception, Ego, Charisma,
                      PsychicPower, AttackSpeed, Size, MoveSpeed)
SoldierSkill         (SoldierId, BaseSkillId, PointsInvested)
HitLocation          (SoldierId, HitLocationTemplateId, IsCybernetic,
                      Armor, WoundTotal, WeeksOfHealing)

PlayerSoldier        (SoldierId, ImplantMillenium, ImplantYear, ImplantWeek)
PlayerSoldierHistory (PlayerSoldierId, Entry)
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
  ├─ Organization : int
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
  ├─ SoldierHistory : List<string>
  ├─ SoldierEvaluationHistory : List<SoldierEvaluation>
  ├─ SoldierAwards : List<SoldierAward>
  ├─ RangedWeaponCasualtyCountMap : Dictionary<int, ushort>
  ├─ MeleeWeaponCasualtyCountMap : Dictionary<int, ushort>
  └─ FactionCasualtyCountMap : Dictionary<int, ushort>
```

`PlayerSoldier` implements `ISoldier` by delegating all attribute and skill reads to its inner `Soldier`.

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

`HitLocationTemplate` defines per-location properties: `NaturalArmor`, `WoundMultiplier`, `CrippleWound` threshold, `SeverWound` threshold, `IsMotive`, `IsRangedWeaponHolder`, `IsMeleeWeaponHolder`, `IsVital`, and `HitProbabilityMap` (a 3-element int array for short/medium/long range bands).

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
  └─ DateRequestFulfilled : Date     (null if unfulfilled)

PresenceRequest : IRequest
  (currently the only concrete implementation)
```

---

## 6. System Implementations

### 6.1 Turn Controller

`TurnController` is the single entry point for end-of-turn processing, called by `MainGameScreenController.OnEndTurnButtonPressed`. Its `ProcessTurn(Sector)` method executes in this order:

1. Collect all active player squad orders.
2. Generate AI orders via `FactionStrategyController.GenerateFactionOrders` for every non-player, non-default faction.
3. For each order (player and AI), construct a `MissionContext` and run the mission step chain via `MissionStepOrchestrator.GetStartingStep`.
4. Collect `MissionContext` results and `SpecialMission` discoveries.
5. Run `SectorEntityLogic` for population growth, going-public logic, intelligence decay, and governor request generation.
6. Advance `GameDataSingleton.Instance.Date` by one week.
7. Return collected contexts and missions for display in the End of Turn Dialog.

Training is now wired into this flow. Non-deployed non-Scout marines receive weekly work-experience training through `ApplySoldierWorkExperience`; Scout squads are routed through `TrainScouts` with each squad's selected `TrainingFocus`. Scout squads assigned to missions are excluded from weekly Scout training.

### 6.2 Faction Strategy

`FactionStrategyController.GenerateFactionOrders(Faction, Sector)` runs per non-player, non-default faction per turn. For each planet where the faction has a public presence:

1. **Force assessment:** Compute `RequiredGarrison` per region, `OrganizedTroops = Population × Organization / 100`, `SpareTroops = max(0, OrganizedTroops − RequiredGarrison)`.
2. **Offensive planning:** If combined `SpareTroops` in regions adjacent to an enemy exceeds that enemy's strength × 1.5, generate an `Advance` order. `ForceGenerator` is called with `TargetBattleValue` set to 50–75% of `SpareTroops × 10` (randomized). Committed troops are deducted from contributing region garrisons.
3. **Construction:** Convert remaining `SpareTroops / 100` to build points. Spend greedily on the cheapest upgrade among Organization, Entrenchment, Detection, Anti-Air (costs scale as `2^currentLevel`).
4. **Patrol:** Any remaining `SpareTroops × 10` become a `ScoutPatrol` order.

**Player construction (squad-driven fortification).** The player can order a squad in its own region to build a defense (Entrenchment / Detection / Anti-Air), creating a `ConstructionMission` targeting the player's `RegionFaction`. Unlike the NPC squad-less construction (resolved at a flat `MissionSize` in `ProcessConstructionOrders`), a construction order that carries a squad is routed in `ProcessCombatMissions` to `ResolveSquadConstruction`: every able soldier contributes its `Engineering (Fortification)` skill value, the sum is divided by `EngineeringBuildDivisor` (100) and floored (minimum 1), and the result is applied via the shared `ApplyConstruction`. The order persists, so the squad accumulates defenses over successive turns. `Engineering (Fortification)` is an Intelligence-based Tech skill trained by all combat marines at low weight.

### 6.3 Sector Entity Logic

`SectorEntityLogic` runs after all orders are resolved. It handles:

**Population & carrying-capacity scales.** Population is a raw headcount (the `// in thousands` comments were stale). Per-type population and carrying capacity are each described by a `LogNormalValueTemplate { Floor, Scale }`: a roll is `Floor + 10^z · Scale` (z standard-normal), so `Floor` is a hard minimum, `Scale` is the median of the variable part, and the distribution is right-skewed (mean ≈ `Floor + 3.77·Scale`). These are distinct from the normal `NormalizedValueTemplate { BaseValue, StandardDeviation }` still used for Importance. The rules-DB columns are `PopulationFloor`/`PopulationScale` and `CarryingCapacityFloor`/`CarryingCapacityScale` (renamed from the misleading `*Base`/`*StandardDeviation`). Values are canon-grounded per world type (Hive ~80B typical down to Death ~310K), with carrying capacity = population scale × a per-type headroom (Hive 1.3 … Feral 5.0). Because hive/forge populations reach billions, `Garrison` and `PlanetaryDefenseForces` are `long`.

**Population Growth (per region, per faction):**
- Carrying capacity is a per-region value (`Region.CarryingCapacity`). It is an absolute, per-type quantity rolled at sector generation from `PlanetTemplate.CarryingCapacityRange`, distributed across the planet's regions by the same power law used for population, and persisted in the save's `Region` table. Starting population is seeded as a fraction of each region's capacity, so no region begins above capacity.
- `Logistic` and baseline (`None`) growth are scaled by a logistic crowding factor: `newPop = factionPop × growthRate × (1 − regionPop / carryingCapacity)`, where `regionPop` is the region's combined population across all factions. The factor is near-maximal when the region is sparse, zero at capacity, and gently negative above capacity (so an overfull region drifts back toward capacity). A carrying capacity of 0 is treated as uncapped (legacy behavior).
- `growthRate` is the maximum (uncrowded) rate: `LogisticGrowthRate = 0.0006`, `BaselineGrowthRate = 0.0004`. These are tuned so a world at a typical fill (~50–75% of capacity) still roughly doubles per century, matching the canon "population doubles every ~100 Terran years" — not just ultra-underpopulated worlds.
- `Conversion` growth: one default-faction member is converted per week. At population > 100, additional 0.2%/week organic growth. The garrison-to-population ratio determines whether a garrison member is also converted. (Conversion is not subject to the carrying-capacity factor.)

**Going Public:**
- If a hidden faction's population exceeds the configured threshold, `IsPublic` is set to `true`, making it visible and triggering conflict resolution in subsequent turns.

**Intelligence Decay:**
- Regions with `IntelligenceLevel > 0` have it multiplied by 0.75 each turn.
- While intelligence remains, hidden faction cells may be revealed as `Extermination` missions; public faction intelligence may generate `Ambush`, `Sabotage`, or `Assassination` special missions.
- Each unconsumed special mission has a 25% chance of expiring each turn.

**Fog of War (UI gating).** Enemy visibility on the planet-tactical and region screens is gated by `Region.IntelligenceLevel` (raised by Recon orders, decayed each turn). Hidden (non-public) factions are concealed on every screen — their population is folded into the civilian count and they are discovered only through the intelligence/special-mission system. For a public enemy, `RegionFactionExtensions.GetPopulationDescription` grades the population by intelligence ("Unknown" at 0 → power-of-10 fuzzing → exact at ≥6), and defenses (Entrenchment/Detection/Anti-Air) are shown only when `IntelligenceLevel > 1` and only as fuzzy descriptions via `GetDefenseLevelDescription`, never as raw integers.

**Governor Requests:**
- For each planetary leader with positive opinion of the player: check for a real threat via Investigation vs. hidden faction population ratio; check for a false alarm via Paranoia.
- If a threat (real or imagined) is detected: roll `Neediness × OpinionOfPlayerForce`. On success, `RequestFactory.GenerateNewRequest` creates a `PresenceRequest` and adds it to `PlayerForce.Requests`.

### 6.4 Mission Step State Machine

All steps implement `IMissionStep`:

```csharp
public interface IMissionStep
{
    string Description { get; }
    void ExecuteMissionStep(MissionContext context, float marginOfSuccess, IMissionStep returnStep);
}
```

Step chains by mission type:

| Mission Type | Step Chain |
|---|---|
| Any (cross-region) | `InfiltrateMissionStep` → main initial step |
| Recon | `ReconStealthMissionStep` → `PerformReconMissionStep` (loops 6 days) → `ExfiltrateMissionStep` |
| Advance | `PrepareAssaultMissionStep` → battle |
| Ambush / Extermination | `PositionAmbushMissionStep` → `AmbushBattleStep` |
| Assassination | `AssassinateStealthMissionStep` → `AssassinateBattleStep` |
| Sabotage | `SabotageStealthMissionStep` → `PerformSabotageMissionStep` (loops 6 days) → `ExfiltrateMissionStep` |

Detection during any stealth phase routes to `DetectedMissionStep`, which dispatches to `AmbushedMissionStep` or `MeetingEngagementMissionStep` depending on context.

**Shared stealth/infiltration/exfiltration difficulty formula:**
```
difficulty = enemyFaction.Detection
           + log10(missionSquad.AbleSoldiers.Count)
           + log10(enemyFaction.Garrison)
```
Skill is compared against difficulty, normalized to a z-score: `(skill − difficulty) / 5.0`.

### 6.5 Mission Checks

Three check types implement `IMissionCheck.RunMissionCheck(List<BattleSquad>)`:

| Type | Skill Source |
|---|---|
| `IndividualMissionTest` | Single highest-skilled soldier across all squads |
| `LeaderMissionTest` | Squad leader with highest skill; falls back to `IndividualMissionTest` if no leader present |
| `SquadMissionTest` | Average skill across all able soldiers |

All checks: `zAdvantage = (skillValue − difficulty) / 5.0`, then `GaussianCalculator.DetermineMarginOfSuccessZvalue(zAdvantage)` returns a signed float (positive = success, magnitude = degree).

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
- A soldier with a crippled ranged-weapon-holding arm fires two-handed weapons at a penalty (tracked via `FunctioningHands`).

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

**Squad placers:** `AmbushPlacer` and `AnnihilationPlacer` handle initial squad placement for their respective engagement types. Starting range is modified by `marginOfSuccess` from the preceding stealth check.

**Battle continuation (`BattleSquad.ShouldContinueMission`):**

| Aggression | Continues if able soldiers ≥ |
|---|---|
| Avoid | 90% of template max strength |
| Cautious | 75% |
| Normal | 50% |
| Attritional | 25% |
| Aggressive | Always (until 0 able soldiers) |

### 6.7 Force Generation

`ForceGenerator.GenerateForce(ForceGenerationRequest)` dispatches by `ForceCompositionProfile`:

- **Generic (Garrison, AssaultForce, AmbushForce):** Adds one affordable HQ squad when the budget can still field at least three full non-HQ squads afterward. It then randomly selects among affordable non-HQ squad templates, cycling through unused affordable types before repeating so large forces become mixed formations instead of copies of the most expensive squad. When no full squad fits the remaining budget, it generates the highest-value partial squad that can fit. `TargetBattleValue` is a `long` (region garrisons can reach billions on hive/forge worlds). When generating a region's *defending* force, `PrepareAssaultMissionStep` caps the mobilized garrison at `MaxMobilizedGarrison` (10,000 troopers) so battles stay tabletop-scale; very large garrisons act as a deterrent to direct assault, to be engaged at scale by future bombardment/war-machine mechanics.
- **SpecialHQTarget:** Selects an HQ template by tier index from sorted HQ templates. Adds a bodyguard squad if `TargetBattleValue ≤ 0` and a `BodyguardSquadTemplate` is defined.
- **ScoutPatrol:** Randomly selects from Scout-flagged templates, generating `Tier` squads.

`SquadFactory.GenerateSquad(SquadTemplate)` populates a squad from template elements via `SoldierFactory.Instance.GenerateNewSoldiers()`, then resolves random weapon selections from `WeaponOptions`.

### 6.8 Chapter Generation

`NewChapterBuilder.CreateChapter(...)` runs once on new game creation:

1. Generate 1,000 base soldiers via `SoldierFactory`.
2. Wrap each in `PlayerSoldier` with a generated name from `TempNameGenerator`.
3. Simulate 104 weeks of training via `ISoldierTrainingService.EvaluateSoldier`.
4. Apply role-specific skill boosts via `ApplySoldierTypeTraining`.
5. Sort soldiers into role buckets (Tactical, Assault, Devastator, Scout, and their sergeant variants) by evaluation score thresholds.
6. `BalanceLists` trims sergeant lists to match squad counts (max 18 Dev sgts, 18 Ass sgts, 52 Tact sgts), demoting excess to line roles.
7. Assign marines to squads in the pre-built chapter structure.
8. Assign scouts last, creating additional Scout Squads for overflow.
9. Initialize the fleet with the first available fleet template.
10. Record a founding history entry.

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
- Turn training excludes embarked squads while their fleet is `InWarp`; when `AdvanceTravelOneWeek` reports warp exit, `TurnController` applies `WeeklyTrainingPoints * WarpSubjectiveWeeks` to embarked idle squads.
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

**Location:** `SectorEntityLogic`, all `IMissionStep` implementations, `NewChapterBuilder`

Skills and templates are frequently looked up by name string (e.g., `s.Name == "Stealth"`, `st.Name == "Tactical Marine"`). A rename in the database silently breaks the lookup at runtime with no compile-time warning.

**Fix:** Introduce constants or a validated by-name lookup dictionary populated at rules-DB load time. Ideally, the load step asserts that all expected named entries are present and fails fast if any are missing, rather than producing a null reference at runtime.

**Update:** The initial training-profile migration moved work-experience training distributions and scout focus distributions into rules data. This reduces hardcoded skill-list coupling in `SoldierTrainingCalculator`, but does not close the broader issue. The remaining notable example is rating formulas that reference named skills.

**Update (validated skill registry):** The first validated registry now exists. `NamedSkillRegistry` (`Models/Soldiers/NamedSkillRegistry.cs`) resolves the base skills that game logic references by name — currently Stealth, Tactics, and Fist — once at rules-DB load (`GameRulesData.Skills`), throwing a clear `InvalidOperationException` if any is missing or ambiguous rather than producing a runtime null deep inside a mission/battle step. All thirteen mission-step `BaseSkillMap.Values.First(s => s.Name == ...)` lookups and the `BattleTurnResolver` "Fist" base-skill lookup now resolve through this registry. Covered by `NamedSkillRegistryTests` (resolves real skills; fails fast on a missing skill).

**Update (validated chapter-generation template registry):** `ChapterGenerationTemplates` (`Models/ChapterGenerationTemplates.cs`) extends the same pattern to the player-faction soldier and squad templates that `NewChapterBuilder` referenced by name. It resolves the full set (Chapter Master, Captain, Champion, Ancient, the Librarius/Armory/Apothecarion/Reclusium specialists, Veteran, the Tactical/Assault/Devastator/Scout marines and their sergeants, and the Tactical/Assault/Devastator/Scout squad templates) once at rules-DB load (`GameRulesData.ChapterTemplates`), failing fast on a missing or ambiguous template. All ~26 `faction.SoldierTemplates`/`SquadTemplates` `First(... Name == ...)` lookups and the three `squad.SquadTemplate.Name == "..."` string comparisons in `NewChapterBuilder` now resolve through this registry (the comparisons are now reference-equality against the resolved template). Covered by `ChapterGenerationTemplatesTests` and exercised end-to-end by the new-game path in `SaveLoadRoundTripTests`.

**Update (validated sector-generation faction registry):** `SectorGenerationFactions` (`Models/SectorGenerationFactions.cs`) extends the pattern to the non-player factions `SectorBuilder` places by name. It resolves the infiltration-capable cult faction (Genestealer Cult) and the invasion faction (Tyranids) once at rules-DB load (`GameRulesData.SectorFactions`), failing fast on a missing or ambiguous faction. The three `data.Factions.First(f => f.Name == ...)` lookups in `SectorBuilder.GeneratePlanet` now resolve through this registry via role-named accessors (`Infiltrator`, `Invader`). Covered by `SectorGenerationFactionsTests`.

**Update (validated battle-defaults registry).** The unarmed-melee default in `BattleTurnResolver` resolved a melee-weapon template by the name "Fist", but the rules DB contains **two** templates named "Fist" (ids 12 and 15) — so the name alone is not a stable key, and the old `First(...)` silently picked one by dictionary order. Investigation showed the duplicate is **intentional**: the two rows are stat-identical and differ only in related skill — id 12 uses the Imperial "Fist" skill (Space Marines), id 15 uses the catch-all "Generic Melee" skill (shared by non-Imperial creature weapons such as Choppa and Scything Talons). Neither is referenced by any `WeaponSet`; their only consumer is the battle default. `BattleDefaults` (`Models/BattleDefaults.cs`) now resolves both at rules-DB load (`GameRulesData.BattleDefaults`), disambiguated by related base skill (`ImperialUnarmedWeapon`, `GenericUnarmedWeapon`), failing fast on missing/ambiguous. `NamedSkillRegistry` gained `GenericMelee` to support this. `BattleTurnResolver.Plan` now hands player squads the Imperial Fist and opposing squads the Generic-Melee Fist (previously both sides shared the single `First()`-selected Imperial Fist — a behavior fix so non-Imperial combatants' Generic Melee training applies to their unarmed attacks). Covered by `BattleDefaultsTests`.

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

### 8.6 GameDataSingleton as Global Mutable State — Low (Now), Medium (Later)

**Location:** `GameDataSingleton`

Mutated from multiple controllers without coordination. Acceptable in a single-threaded context, but makes unit testing difficult because any test touching a logic system that reads from the singleton must set up the full singleton first.

**Fix:** No immediate action required. The preferred direction is to refactor pure-logic systems (battle resolution, mission checks, faction AI) to accept their inputs as method parameters rather than reading from the singleton, enabling isolated unit testing without the full singleton overhead. Progress so far: `FactionStrategyController` already takes `(faction, sector)` and is tested with no singleton at all (§9.2.1 #4), and `RatingCalculator` takes its `IRNG` by injection. `TurnController` still reads the singleton (`Date`, `Sector.PlayerForce`, `GameRulesData.Factions`), so its end-of-turn tests (§9.2.1 #5, #8) seed a hand-built sector into the singleton via `LoadGameDataFromBlob`; assembly-wide disabled parallelization keeps that safe.

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

### 8.12 End-of-Turn Resolution Bugs — RESOLVED

**Location:** `Helpers/TurnController.cs` (`UpdatePlanets`, `UpdateIntelligence`)

Surfaced while writing the `SectorEntityLogic` / multi-turn coverage (§9.2.1 #5, #8):

- **Collection-modified-during-iteration.** Three end-of-turn loops removed elements from the very collection they were iterating: depopulated `RegionFaction`s (over `RegionFactionMap.Values`), depopulated `PlanetFaction`s (over `PlanetFactionMap.Values`), and expired special missions (over `Region.SpecialMissions`). Any actual removal threw `InvalidOperationException`, so mission expiration and faction cleanup could crash a turn. Each loop now iterates a snapshot (`.ToList()`).
- **Governor logic was dead code.** `PlanetFaction.Population` is a get-only property hardcoded to `0` and never maintained, so the end-of-turn leader update was gated behind `planetFaction.Population <= 0` — always true. Every `PlanetFaction` was therefore stripped from the map each turn and `EndOfTurnLeaderUpdate` (governor aging, request fulfilment, and **request generation**) never ran. `UpdatePlanets` now derives the faction's planet-wide population by summing its `RegionFaction.Population` across the planet's regions, restoring the governor-request feature. (Removing the vestigial `PlanetFaction.Population` property is left as a follow-up.)

Covered by `SectorEntityLogicTests` and `MultiTurnSmokeTests`.


---

## 9. Testing Strategy

The `OnlyWar.Tests` xUnit project covers the pure domain and helper logic incrementally, without a full refactor. The sections below record the approach and the coverage to date.

### 9.1 Setup

The `OnlyWar.Tests` xUnit project references the game assembly and runs against the shipped rules database. Keep expanding it around pure domain and helper logic first. Systems with Godot node dependencies cannot be unit tested without a Godot runtime; focus the test project on pure domain and helper logic. Test parallelization is disabled assembly-wide (`[assembly: CollectionBehavior(DisableTestParallelization = true)]`) so suites that load the shared `GameDataSingleton` do not interfere.

Make `RNG` injectable: introduce an `IRNG` interface and a `SeededRNG` implementation so tests can run with a fixed seed for deterministic results. *(Implemented — `Helpers/IRNG.cs` with `StaticRNG` (production adapter over the global `RNG`), `SeededRNG` (own seeded instance), and a `FixedRNG` test double. `RatingCalculator` is the first consumer to take `IRNG` by injection.)*

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
5. **`SectorEntityLogic`** — *(Implemented — `SectorEntityLogicTests`.)* The end-of-turn logic lives in `TurnController`; driven through `ProcessTurn` with a seeded global `RNG` (`RNG.Reset`) over a compact hand-built sector (`SectorSimulationFixture`). Covers logistic growth, conversion growth (one default member converted per week), intelligence decay (×0.75/turn), stale special-mission expiration, and governor request generation against a public threat. Surfaced and fixed three latent bugs (see §8.12).
6. **`BattleGridManager` and `WoundResolver`** — *(Implemented — `BattleGridManagerTests`, `WoundResolverTests`.)* Grid tests cover placement/occupancy/reservation conflicts, movement (free-old/occupy-new and collision), removal, nearest-enemy/distance queries, open-adjacency selection, and clone fidelity. Wound tests cover the damage-ratio severity ladder, natural-armor subtraction, wound-multiplier scaling, already-severed short-circuit, and the vital-location-death / motive-location-fall event paths.
7. **Rating formula evaluator** — *(Implemented — `RatingCalculatorTests`, `RatingDefinitionDataTests`.)* Rating formulas and award thresholds are data-driven (§4.1.1); tests assert the evaluator's aggregation/normalization structure with a fixed `IRNG`, that the migrated definitions match the documented formulas, and that award tiers fire correctly (highest-tier-only, best-skill-in-category name interpolation, history flags).
8. **Seeded multi-turn smoke test** — *(Implemented — `MultiTurnSmokeTests`.)* Builds a compact single-planet sector (`SectorSimulationFixture`) with a conversion cult, a public rival controller, a governor, and a high-intelligence region, then runs twelve `ProcessTurn` cycles under a fixed seed and asserts high-level invariants survive: planet stays populated with no negative region populations, the default faction persists, the cult steadily recruits, intelligence decays toward zero, and the governor's aid request persists.
9. **New game smoke test** — Generate a new campaign from rules data and assert chapter, fleet, sector, subsector, planet, faction, and squad invariants without requiring the Godot UI.

### 9.3 Regression Risk Areas

These areas are particularly likely to produce hard-to-detect bugs as features are added:

- Changing the `WoundLevel` bitmask layout or adding new severity tiers.
- Adding new fields to `BattleSoldier` without updating both the copy constructor and `Clone()`.
- Adding new tables to the save schema without updating both `SaveData` and `GetData` in `GameStateDataAccess`.
- Changing skill or template names in the rules database without updating hardcoded string lookups or validated registries (see 8.3).
- Changing the `Wounds.WeeksToHeal` nibble-offset encoding without updating all dependent healing logic.
- Adding new data-driven rules tables without adding rules-load validation and regression tests.
