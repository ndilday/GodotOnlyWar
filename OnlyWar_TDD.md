# OnlyWar — Technical Design Document

**Version:** Alpha 0.8

**Last Updated:** September 1, 2026

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
   - 5.2.1 [Faction Relationships & Target Intelligence](#521-faction-relationships--target-intelligence)
   - 5.3 [Soldiers](#53-soldiers)
   - 5.4 [Squads & Units](#54-squads--units)
   - 5.5 [Fleet](#55-fleet)
   - 5.6 [Missions & Orders](#56-missions--orders)
   - 5.7 [Characters & Requests](#57-characters--requests)
   - 5.8 [Campaign Scenario](#58-campaign-scenario)
   - 5.9 [Recruitment](#59-recruitment)
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
   - 6.10 [Campaign Operations Services](#610-campaign-operations-services)
   - 6.11 [Sector Map Label Layer](#611-sector-map-label-layer)
   - 6.12 [Ork Infestation](#612-ork-infestation)
7. [UI Layer](#7-ui-layer)
   - 7.1 [View / Controller Pattern](#71-view--controller-pattern)
   - 7.2 [Screen Inventory](#72-screen-inventory)
   - 7.3 [Navigation Model](#73-navigation-model)
   - 7.4 [Last-Turn Report Snapshot](#74-last-turn-report-snapshot)
   - 7.5 [Operations Workspaces](#75-operations-workspaces)
   - 7.6 [Force Legibility & Shared Squad Rows](#76-force-legibility--shared-squad-rows)
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
  /Battles                BattleHistory, BattleTurn, BattleMissionTemplate
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
  /PlanetaryOperationsScreen
  /RecruiterScreen
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
- `GameRulesData` — the loaded rules blob (all templates, profiles, base skills, body templates, and economy rules)
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
| `EquipmentLoadout` | `RangedWeapon` / `MeleeWeapon` plus shared `AmmunitionReservePool` |

Templates are immutable after load. All mutable state lives in instances.

### 3.4 Mission Step State Machine

Mission execution is modeled as a chain of `IMissionStep` objects. Each step's `ExecuteMissionStep(MissionContext, float margin, IMissionStep returnStep)` either calls the next appropriate step directly (passing `this` as `returnStep` for looping steps such as daily stealth checks) or returns when the mission is complete or the force is wiped out.

`MissionStepOrchestrator` is the entry point, selecting the initial step from mission type. If the squad is not already in the target region, an `InfiltrateMissionStep` is prepended regardless of mission type.

### 3.5 Battle Snapshot Cloning

`BattleSquad` is the single `ICloneable` battle type used to store battlefield snapshots for
`BattleHistory`. Its copy path creates copied `BattleSoldier` runtime state; `BattleSoldier` itself
does not implement `ICloneable`, and its underlying `ISoldier` is shared by design because replay
reads snapshot battle fields and the action log rather than an independent campaign body.

---

## 4. Data Layer

### 4.1 Game Rules Database

Read-only SQLite file loaded once at application start. Accessed via `GameRulesDataAccess` (singleton). Contains:

At runtime, `GameStorage` locates the immutable install root and supplies the ordinary filesystem path `Database/OnlyWar.s3db`; the database is deliberately shipped loose beside the exported executable because `Microsoft.Data.Sqlite` cannot open a database inside Godot's virtual PCK filesystem. Editor and test runs locate the same install root by walking up from the process/assembly directories, so no code depends on the current working directory.

- `Faction`, `Species`, `SoldierTemplate`, `SquadTemplate`, `SquadTemplateElement`
- `UnitTemplate`, `UnitTemplateHierarchy`, `UnitTemplateSquadTemplate`
- `BaseSkill`, `SkillTemplate`, and the optional `SkillRoleAssignment` semantic bindings
- `HitLocationTemplate` (grouped into body types)
- `RangedWeaponTemplate`, `MeleeWeaponTemplate`, and the compatibility `WeaponSet` rows
- `EquipmentTemplate`, `EquipmentRangedProfile`, `EquipmentMeleeProfile`, `EquipmentArmorProfile`,
  `AmmunitionType`, `EquipmentAmmunitionPackage`, `EquipmentKitTemplate`, `EquipmentKitItem`,
  and `PersonalEquipmentRole` for the itemized equipment catalog
- `TrainingProfile`, `TrainingProfileEntry` for data-driven skill and attribute training distributions
- `PlanetTemplate` and `PlanetTemplateEligibility`
- `SectorGenerationProfile` for data-owned sector dimensions, planet density, and subsector scale
- `ShipTemplate`, `BoatTemplate`, `FleetTemplate`, `FleetShipTemplate`

Load order matters: skills → hit locations → weapon templates → training profiles → soldier/squad templates → unit templates → planet templates → planet-template eligibility → sector-generation profiles → fleet templates → factions.

#### Data Ownership and Modding Contract

The rules database is the primary mod surface. It contains universe content, templates, distributions, balance values, and configuration profiles that a campaign author may reasonably want to change. Code owns executable behavior: simulation algorithms, topology construction, closed vocabularies, and validation. A data-driven rule may provide inputs to an algorithm without moving the algorithm itself into the database.

| Data category | Owner | Examples |
| --- | --- | --- |
| Universe content | Rules DB | Factions, species, planets, soldiers, squads, units, weapons, equipment, and fleets |
| Configuration profiles | Rules DB when modifiable | Sector generation, travel, scenario, training, and ratings |
| Semantic assignments | Rules DB plus validated code consumers | Infiltrator faction, chapter tactical squad, teaching skill, and default equipment roles |
| Algorithms and simulation behavior | Code | Subsector clustering, warp-lane construction, request valuation, and turn processing |

The supply economy is a deliberate exception to the general configuration rule. Its balance
values live in the typed, code-owned `SupplyEconomyRules.CreateDefault()` profile. The rules
database contains the universe and campaign-content vocabulary; it does not define request
valuation, governor offers, pledge pacing, or supply multipliers. Requisition and pledge state
remain campaign data in the save model. If a future economy overhaul becomes a first-class mod
surface, it should be introduced as a separate versioned profile rather than restoring the old
EAV tables to the universe database.

Display names are presentation data and are never stable code identifiers. Numeric row IDs are likewise not semantic identities and must not be hardcoded in production code. Cross-table foreign keys are normal rules composition and are not considered hardcoded row dependencies. When code requires a semantic assignment, the database must provide a stable key, role, or flag, and the rules loader must validate that assignment at startup.

Required content and profile tables must exist and contain enough data for their owning system to operate. Relationship or extension tables may be empty only when they are explicitly classified as optional or structurally empty-valid. Optional tables must have defined behavior when absent or empty. The loader is the boundary where these contracts are checked and should fail fast with a clear error rather than allowing a later `First(...)` or dictionary lookup failure during play.

**Implemented (RDB-009):** `RulesDatabaseSchemaValidator` checks the required table contract before
hydration, `RulesDatabaseReferenceValidator` rejects orphan relational rows, and
`RulesDatabaseValidator` checks hydrated collection availability, positive planet probability totals,
generation-faction content, training profiles, equipment availability, and player fleet
prerequisites. Loader-side lookups now include their source relation in errors. Compatibility and
extension tables remain absent-valid only where their loaders provide explicit fallback behavior.

**Implemented (RDB-015):** `SectorGenerationProfile` supplies the data-owned sector dimensions,
planet spawn probability, and maximum subsector diameter used by new-sector generation and derived
topology reconstruction. The loader requires exactly one default profile and validates its ranges.
Sector-map and battle-replay pixel metrics remain code-owned presentation settings.

The rules database is loaded as an immutable rules profile for a campaign. Campaign compatibility should identify the rules/mod version used to generate the save so incompatible rules changes cannot silently alter an ongoing campaign. Current saves persist the random-algorithm version but not a rules-profile identity or content hash; adding that metadata remains a compatibility follow-up.

The rules loader is the single boundary for resolving stable keys, semantic flags, and validated registries into runtime objects. Consumers should use those resolved identities rather than rediscovering rules rows by display name.

### 4.1.2 Equipment Rules & Loadout Resolution

The itemized equipment catalog is the authoritative vocabulary for personal equipment. Each
`EquipmentTemplate` has a globally unique id, immutable optional profiles (ranged, melee, armor,
ammunition package, or gear), carry cost, maximum quantity, tags, and data-authored requirements.
`EquipmentKitTemplate` is a reusable armor-plus-item composition; `EquipmentLoadout` is the complete
resolved composition and exposes a stable `EquipmentSignature` sorted by equipment id, quantity, and
initial-ready order. A two-handed profile reports two hand groups, but only consumes those groups when
readied; carried gear is not implicitly readied.

`EquipmentLoadoutValidator` is shared by assignment, deployment, and UI-facing validation. It checks
faction/species/template/role/strength/skill/tag requirements, duplicate limits, carry capacity, and
ready-order validity. Capacity is species base capacity plus personal-role, worn-armor, and gear
bonuses; worn armor itself has zero carry cost. `EquipmentLoadoutService` resolves in this order:
personal override, chapter role default, authored role kit, element fallback, and squad fallback.
Personal overrides remain stored while a soldier occupies a pooled role, but are inactive until the
soldier is eligible for a personal-equipment role again. Pooled squad allocation continues to use the
legacy `WeaponSet` compatibility path until that standard-issue UI is migrated. In the itemized path,
`SquadTemplateElementEquipmentRole` is the authoritative element-to-role binding; the display name
`Command Weapon` is only a legacy-fixture fallback and is not consulted by production role
resolution. `EquipmentLoadoutEditorView` is the shared complete-loadout editor used by both chapter
role doctrine and live-soldier Customize/Inherit flows.

The shipped rules database contains the itemized tables and the loader builds a globally identified
catalog from them and the compatibility weapon rows. Runtime equipment ids never collide across
ranged, melee, armor, ammunition-type, package, and kit namespaces. The catalog currently bridges
legacy pooled sets into itemized kits so personal roles use the same validator, signature, and
effective-value path as bespoke loadouts.

### 4.1.1 Data-Driven Rule Profiles

Data-driven rule profiles follow this boundary:

- Code owns the algorithm for applying a profile.
- Rules data owns which skills or attributes a role trains, and at what relative weights.
- `SoldierTemplate` records the work-experience training profile for that soldier type.
- Scout focus modes use training profiles rather than hardcoded skill lists.

Additional profile/definition candidates:

- Mission skill requirements, e.g. stealth checks and tactical planning checks.
- Default battle resources, e.g. unarmed melee weapon/skill.
- Chapter-generation role bindings, e.g. Chapter Master, Scout Company, Armory, Apothecarion.
- Sector-generation faction roles, e.g. primary hidden infiltrator and invasion faction.

Rating formulas use a constrained evaluator rather than arbitrary script execution. The model stores `RatingDefinition`, `RatingComponent`, and `RatingNormalizationFactor` rows, with a small fixed set of component types such as attribute value, skill total, best skill bonus in category, and best skill total in category. This keeps formulas tunable without embedding a general-purpose expression language.

A profile belongs in the rules database when it is intended to be a mod surface. Code must not interpret arbitrary row presence as a feature contract: if a feature requires an assignment, model it explicitly with a stable key, role, or semantic flag and validate it at load time; if a table is optional, document its absent/empty behavior.

**Implemented (rating definitions, awards, and consumer bindings).** Soldier ratings and their award thresholds are fully data-driven. The rules DB holds `RatingDefinition` (`RatingKey`, display name, `Product`/`Sum` aggregation), `RatingComponent` (component type + polymorphic target — attribute, base-skill id, or skill category), `RatingNormalizationFactor` (uniform `(Low, High)` divisor factors), and `RatingAwardTier` (award tiers and history-flag thresholds, with a `{bestSkillInCategory}` name placeholder). **`Database/OnlyWar.s3db` is the source of truth for the seven shipped formulas and their tiers** — read them there rather than from any document; the migration that seeded them ran through the since-deleted `RulesDbTool`. `RatingCalculator` (`Helpers/RatingCalculator.cs`) evaluates `Aggregate(components) / Π sample(factor)` using an injected `IRNG`, and applies the highest matching award/flag tier per rating. `SoldierTrainingCalculator` delegates `UpdateRatings`/awards to it.

The set of ratings is open-ended: `SoldierEvaluation` stores a `RatingKey → value` map, persisted via the `SoldierEvaluationRating` child table (§4.3) keyed by the rating *string*, so adding, removing, or renaming an ordinary rating never touches the save schema. Gameplay code does not require the seven shipped keys by name. `RatingConsumerBindings` maps stable code-owned capabilities (melee combat, ranged combat, command, medical, technical, spiritual, and ancient service) to data-owned rating keys. `GameRulesData` loads that mapping from the optional `RatingConsumerAssignment` table, falls back to the shipped mapping for older databases, and validates only the explicitly required capability roles. Legacy convenience accessors over the seven shipped keys remain for source compatibility; production consumers use the bindings.

Award identity is likewise open-ended. `RatingAwardTier.AwardType` is retained as the serialized field name but is interpreted as an award-family key. The optional `AwardFamily` table supplies display name, sort/group metadata, and a logical `IconAssetKey`. `IconAssetRegistry` resolves that key from a core or mod manifest to either a standalone texture or an atlas region. Mod keys are namespaced (`package_id:local_key`), and unavailable art falls back to the generic award icon. `SoldierAward.Type` stores the stable family key while `SoldierAward.Name` stores the display-name snapshot. Older databases without the two optional rules tables use the shipped default consumer bindings and award catalogue.

Three properties of this design are easy to break and worth stating. The component vocabulary is a **closed set** (`AttributeValue`, `SkillTotal`, `BestSkillBonusInCategory`, `BestSkillTotalInCategory`) rather than an expression language — the same constraint the supply-economy rules data observes, and the reason formulas can be tuned without shipping an evaluator for arbitrary script. `ranged` is the only `Sum` aggregation and the only formula reading a best-skill-bonus-in-category instead of a named skill total, so it is the one that breaks first under a refactor that assumes uniformity; it also inherits `GetBestSkillInCategory`'s throw when a soldier has no skill in the category, which is safe only because every marine has a ranged skill. Award dedup keeps the highest award per award-family key across evaluations, and the material word (Bronze/Silver/Gold/Adamantium) is baked into each tier's `NameTemplate` rather than living in its own table — revisit only if materials need to be tuned independently.

### 4.2 Save State Database

Written in full on each save (file is deleted and recreated from scratch using the loose, read-only `Database/SaveStructure.sql`). Read on load via `GameStateDataAccess` (singleton). All writes are wrapped in a single transaction; exceptions trigger rollback. Player saves live under `user://saves` (`%APPDATA%\OnlyWar\saves` on Windows), never in the install directory. `SaveGameCatalog` discovers `*.s3db` files and inspects only their metadata for the start menu.

**Current Alpha 0.8 behavior:** `SaveFormat.CurrentVersion` is 19 and is written to `GlobalData.SaveVersion`. Format 12 adds stable line-formation ordinals and durable squad battle-history retention; format 13 adds persisted individual postings; format 14 adds administrative formation stations, explicit order ownership, character participants, and physical-only postings; format 15 adds stable-key Scout training options; format 16 adds indelible Ork region state, latent ghost sources, and persistent Waaagh! identities; format 17 adds successor Waaagh! transit Battle Value; format 18 adds the resolved Promised-World invader faction; format 19 adds the singleton Chapter operational doctrine. Only format 19 is currently accepted; older and newer versions are rejected before campaign-table loading. Missing saves are opened in neither create nor write mode, preventing a failed load from leaving behind an empty SQLite file. The visible chooser retains compatible, incompatible, and corrupt entries with an explicit reason instead of silently choosing the newest file.

Named manual slots, the initial recovery point, three rolling post-turn autosaves, and the protected pre-turn recovery point all use the same atomic persistence path. `CampaignRecoverabilityTracker` records whether the current in-memory revision has a successfully written recovery point, while `SaveGameManager` owns slot naming, metadata, retention, overwrite protection, and restoration of the prior valid save on failure. The protected pre-turn write completes before `ProcessTurn` mutates state; failure blocks turn resolution. Alpha saves use exact-version compatibility only: there is no legacy save migrator or legacy-history import path.

**Format version 7 (historical, 2026-08-08).** The `LastTurnReport` table stores one optional bounded JSON snapshot of the latest resolved turn report. The row is written in the same atomic transaction as the campaign and is hydrated onto `PlayerForce`; a missing table or row is treated as a null report so a campaign-start/pre-turn save can still load and show an intentional empty Last Turn Report state. The payload contains display strings, debrief lines, and compact casualty data only — never `MissionContext`, `BattleHistory`, or live campaign entities. Current format 19 loading rejects format 7 before table access.

**Format version 8 (historical, 2026-08-11).** The save schema includes the canonical `CampaignEvent` /
`CampaignEventEntity` / `CampaignEventPublication` tables, the persistent Chapter Chronicle
tables, and campaign identity/random-stream metadata. The narrative-event emission pass uses these
existing tables and the existing format-8 JSON payload column; it does not add a table or change a
column, so no further save-format increment is required. New event enum values are append-only and
legacy payload versions remain readable through the registry described above. The current writer
and loader use the canonical event and Chronicle tables only; legacy free-text history tables are
not imported.

**Command Brief & Chapter Chronicle (Alpha 0.8 item 5).** `CommandScreenController` presents one
primary workspace with live `COMMAND BRIEF` and persisted `CHAPTER CHRONICLE` lenses. Brief cards
are built from current state by `Helpers/Command/CommandBriefBuilder`; the preference-free
`CommandAttentionEvaluator` supplies the shared idle/leaderless/fleet/opportunity/recruitment facts
used by both the Brief and `EndTurnPreflight`. Brief view state is session-local and is never saved.
`CampaignNavigationTarget` is the semantic routing contract; `MainGameScene` owns return-stack
navigation and preserves the Command surface across deep links. `LastTurnReportSnapshot` remains the
bounded latest-report surface and is available from the Command header in addition to the automatic
post-turn dialog.

`ChapterChronicleProjector` composes standalone entries as events settle and grouped battle entries
when their `BattleResolved` anchor arrives. `ChapterFounded` is a defining Chapter-level event;
routine `BattleResolved` facts remain Turn Report material unless a qualifying correlated or explicit
strategic publication promotes them. Chronicle prose, narrator version, variant, contributor ids,
and selected callback ids are frozen in `ChapterChronicleEntry`; linked archival annotations append
cooler corrections without rewriting the original. Browsing uses typed filters, newest-first pages of 20, and live or
historical/unavailable entity links without re-narrating. Save transactions only validate and write
the already-projected ledgers. New campaigns emit the founding event after scenario construction;
loaded scenario saves missing it receive a deterministic compatibility event from persisted roster,
scenario, and world facts.

The format-14 change follows the same rule established by format 6: any change to `SaveStructure.sql`'s shape bumps `SaveFormat.CurrentVersion`. A save/load round-trip test cannot catch a missed bump because the writer recreates the schema from scratch; only an older file read by a newer build exposes it. There is intentionally no migration boundary: an older or newer format is reported as incompatible and must be replaced by a new current-format campaign. `ChapterEquipmentRoleLoadout` and `SoldierEquipmentLoadout` store complete armor/item compositions; their item tables preserve quantity and initial-ready order, and personal rows are filtered against the current `Soldier` roster during save. `Squad.FormationOrdinal` and `Squad.HasBattleHistory` preserve line identity and historical Scout retention. Format 14 stores administrative duty stations and `OrderCharacter` relationships separately from `IndividualPosting`, which now preserves only a detached soldier's physical purpose, location, and start date. Format 16 adds the Ork region links and persistent source/Waaagh! records; format 17 adds the transit Battle Value carried by a successor Waaagh!; format 18 adds the resolved scenario invader identity; format 19 adds the Chapter operational doctrine row.

`CurrentCampaignSaveWriter` passes `PlayerForce.LastTurnReportSnapshot` explicitly to `GameStateDataAccess.SaveData`. A null snapshot is written as a valid current-version save with no `LastTurnReport` row; it is not an error and represents a campaign whose first turn has not resolved yet. Full battle replay is deliberately not part of this payload. The chapter event chronicle is also separate: it cannot reconstruct all strategic, construction, governor, recruitment, and mission-report cards.

Connections use `Microsoft.Data.Sqlite` (the `SqliteConnectionStringBuilder` `DataSource`) with foreign key enforcement enabled (`ForeignKeys = true`). The schema is foreign-key-valid — every reference resolves to a table in the save file — and the save routines insert parent rows before the rows that reference them. `Faction` is intentionally *not* a foreign-key target: factions live only in the read-only rules database and are matched by id at load. See §8.5.1 for the provider-compatibility work that established this.

### 4.3 Save Schema

Key tables and their relationships:

```
GlobalData           (Millenium, Year, Week, SaveVersion)
LastTurnReport       (Id = 1, ResolvedDate, PayloadJson)
ChapterOperationalDoctrine
                     (Id = 1, InjuryThreshold nullable, RequireDutyReadySquadLeader,
                      MinimumDutyReadySquadStrength)

Planet               (Id, PlanetTemplateId, Name, x, y, Importance, TaxLevel)
PlanetFaction        (PlanetId, FactionId, IsPublic, Population, PlanetaryControl,
                      PlayerReputation, LeaderId→Character)
PlanetFactionRegionAwareness
                     (PlanetId, FactionId, RegionId, Awareness)
FactionRelationship  (LowerFactionId, HigherFactionId, Stance)
PlanetFactionTargetIntel
                     (PlanetId, ObserverFactionId, RegionId, TargetFactionId,
                      Evidence, EstimatedPopulation, EstimatedMilitaryStrength,
                      LastEvidenceWeek)
Region               (Id, PlanetId, RegionNumber, RegionName, RegionType,
                      IsUnderAssault, IntelligenceLevel, CarryingCapacity)
RegionFaction        (RegionId, FactionId, IsPublic, Population, Garrison,
                      Organization, OrganizedMilitaryStrength, Entrenchment, Detection, AntiAir,
                      StrategicInvasionForceId, DormantConsolidation)
GhostPopulationSource
                     (Id, FactionId, x, y, PlanetTemplateId, Population,
                      PopulationCapacity, Consolidation)
StrategicInvasionForce
                     (Id, FactionId, CommandSquadId→Squad, CurrentRegionId→Region,
                      OriginPlanetId→Planet, DestinationPlanetId→Planet,
                      TravelWeeksRemaining, TransitBattleValue, IsActive)
Mission              (Id, MissionType, RegionId, FactionId, MissionSize, DefenseTypeId,
                      IsRegionMission)                     -- 1 = region special mission, 0 = order-attached

Character            (Id, Investigation, Paranoia, Neediness, Patience,
                      Appreciation, Influence, LoyalFactionId, OpinionOfPlayer)
Request              (Id, CharacterId, PlanetId, RequestDate, FulfillmentDate)

Fleet                (Id, FactionId, x, y, DestinationPlanetId)
Ship                 (Id, ShipTemplateId, FleetId, Name, IsFlagship)

Unit                 (Id, UnitTemplateId, ParentUnitId, Name)
Squad                (Id, SquadTemplateId, UnitId, Name, LoadedShipId, LandedRegionId,
                      ScoutTrainingOptionKey, UsesLoadoutDoctrine, FormationOrdinal, HasBattleHistory,
                      DutyStationShipId, DutyStationRegionId)
SquadWeaponSet       (SquadId, WeaponSetId)
ChapterEquipmentRoleLoadout
                     (PersonalEquipmentRoleId, ArmorEquipmentId)
ChapterEquipmentRoleLoadoutItem
                     (PersonalEquipmentRoleId→ChapterEquipmentRoleLoadout,
                      EquipmentId, Quantity, InitialReadyOrder)
SoldierEquipmentLoadout
                     (SoldierId→Soldier, ArmorEquipmentId)
SoldierEquipmentLoadoutItem
                     (SoldierId→SoldierEquipmentLoadout, EquipmentId,
                      Quantity, InitialReadyOrder)
Assignment           (Id, MissionId, IsQuiet, IsActivelyEngaging,
                      Aggression, OwnerFactionId)          -- the "Order" domain object
OrderSquad           (OrderId→Assignment, SquadId)       -- order-to-squad junction
OrderCharacter       (OrderId→Assignment, SoldierId→Soldier) -- character participants
IndividualPosting    (SoldierId→Soldier, Purpose,
                      LoadedShipId→Ship, LandedRegionId→Region, StartedDate)
                                                          -- physical posting only; exactly one place

Soldier              (Id, SoldierTemplateId, SquadId, Name, Strength, Dexterity,
                      Constitution, Intelligence, Perception, Ego, Charisma,
                      PsychicPower, AttackSpeed, Size, MoveSpeed)
SoldierSkill         (SoldierId, BaseSkillId, PointsInvested)
HitLocation          (SoldierId, HitLocationTemplateId, IsCybernetic,
                      Armor, WoundTotal, WeeksOfHealing)

PlayerSoldier        (SoldierId, ImplantMillenium, ImplantYear, ImplantWeek)
SoldierEvaluation       (SoldierId, Millenium, Year, Week)   -- identity only
SoldierEvaluationRating (SoldierId, Millenium, Year, Week, RatingKey, Value)
                                                            -- open-ended: one row per rating
SoldierAward         (SoldierId, Millenium, Year, Week, Name, Type, Level)
PlayerSoldierFactionCasualtyCount        (PlayerSoldierId, FactionId, Count)
PlayerSoldierRangedWeaponCasualtyCount   (PlayerSoldierId, RangedWeaponTemplateId, Count)
PlayerSoldierMeleeWeaponCasualtyCount    (PlayerSoldierId, MeleeWeaponTemplateId, Count)

CampaignEvent            (Id, EventType, OccurredWeek, RecordedWeek, CorrelationKey,
                          DedupeKey, PayloadVersion, PayloadJson)
CampaignEventEntity      (CampaignEventId→CampaignEvent, EntityKind, EntityId,
                          EntityRole, DisplayName, SortOrder)
CampaignEventPublication (CampaignEventId→CampaignEvent, surface flags, Importance,
                          ReasonFlags, ChronicleTreatment, ClassifierVersion)
ChapterChronicleEntry    (Id, OccurredWeek, RecordedWeek, Importance, CorrelationKey,
                          DedupeKey, Title, Body, NarratorKey, NarratorVersion, NarrativeVariant)
ChapterChronicleEvent    (ChronicleEntryId→ChapterChronicleEntry,
                          CampaignEventId→CampaignEvent, SortOrder)
ChapterChronicleCallback (ChronicleEntryId→ChapterChronicleEntry,
                          CampaignEventId→CampaignEvent, SortOrder)
ChapterChronicleAnnotation (Id, ChronicleEntryId→ChapterChronicleEntry,
                            EvidenceEventId→CampaignEvent, RecordedWeek, Body,
                            NarratorKey, NarratorVersion, DedupeKey, IsCorrection)
WorldControlEpisode      (PlanetId→Planet, ImperialFactionId, LastControllingFactionId,
                          WasImperialControlled, ContestedSinceWeek, ChapterParticipated)
```

**Note:** Region adjacency is runtime-only. It is reconstructed from the ordered region array on load and is not persisted.

**Canonical campaign event spine.** The current format-19 save retains the format-8 event spine as the durable source of truth
for player-facing career and battle facts. `PayloadJson` is decoded through the explicit
`(CampaignEventType, PayloadVersion)` registry; entity rows retain stable ids and display-name
snapshots, and publication rows retain the classifier decision so loading never reclassifies an old
event. Current saves write the campaign event/Chronicle tables and rebuild each `SoldierEvent`
history projection from them. Legacy free-text history tables are ignored.

The append-only event vocabulary uses the existing values for `FirstBlood`, `KillMilestone`,
`LastSurvivor`, `MentorAssigned`, `NearDeathRecovery`, and `MissionOutcome`; `Oath` remains reserved.
`SquadHeldAgainstOdds` and `BodyPartReplacement` append values 107 and 108. Current typed versions
are v3 for battle participation, incapacitation, death, gene-seed, last-survivor, mentor, and
near-death payloads, and v1 for squad-held and body-part replacement payloads. Legacy v1/v2 payload
readers remain registered. Battle payloads carry one immutable `BattleEventContextSnapshot`, and
the ledger validates source-event references and maintains the derived open-near-death projection
while loading in event-id order.

Classifier v2 owns the complete publication matrix. Its initial kill thresholds are 10 and 50
(Service Record only), 100 (major), 500 (major), and 1,000 (defining); 25 and 250 are not rules.
Death seniority is derived lexicographically from snapshotted rank/subrank against
`NarrativeEventRules.NotableCasualtyMinimumRank/Subrank`. The veteran reason is set only by the
snapshotted Terminator Honours fact and is dormant while honours have no emitter. Actual squad
leaders who fail the shared `IsDeployable` rule emit an operational disruption fact without
automatic Chronicle publication.

Format 11 adds `WorldControlEpisode`, preserving contested episodes and bounded Chapter
participation until a world is restored or lost. Completed events and per-planet/per-faction cult
revelations use stable dedupe keys. Chronicle composition uses the `chapter-internal` narrator;
Service Record, Turn Report, Command Brief, and Battle Review have separate composition paths.
Notable death prose waits for its correlated gene-seed outcome. Chronicle body text, narrator
version, deterministic variant, and callback ids are persisted and never regenerated on load.
Later corrections append `ChapterChronicleAnnotation` rows in the archival-annotation voice and
leave the original body untouched.

---

## 5. Domain Model

### 5.1 Galaxy & Planets

```
Sector
  ├─ Planets : Dictionary<int, Planet>
  ├─ Subsectors : List<Subsector>
  ├─ GhostPopulationSources : IReadOnlyList<GhostPopulationSource>
  ├─ StrategicInvasionForces : IReadOnlyList<StrategicInvasionForce>
  ├─ RelationshipLedger : FactionRelationshipLedger
  └─ PlayerForce : PlayerForce

Planet
  ├─ Regions : Region[]
  ├─ PlanetFactionMap : Dictionary<int, PlanetFaction>
  ├─ RelationshipLedger : FactionRelationshipLedger (shared with Sector)
  └─ Template : PlanetTemplate

Region
  ├─ RegionFactionMap : Dictionary<int, RegionFaction>
  ├─ AdjacentRegions : List<Region>        (runtime only, not persisted)
  ├─ SpecialMissions : List<Mission>
  └─ IntelligenceLevel : float             (legacy serialized region scalar; not target visibility)

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
  ├─ StrategicInvasionForceId : long?      (active invasion-force affiliation, if any)
  ├─ DormantConsolidation : double         (local indelible dormant-population consolidation)
  └─ IsPublic : bool

PlanetFaction
  ├─ Faction : Faction
  ├─ Leader : Character                    (null if the faction has no leader assigned)
  ├─ IsPublic : bool
  ├─ RegionAwareness : Dictionary<Region, float>
  ├─ TargetIntel : Dictionary<(Region, TargetFaction), FactionIntelBelief>
  ├─ PlayerReputation : float
  └─ PlanetaryControl : int

Subsector
  ├─ Planets : List<Planet>
  └─ CellList : List<Vector2I>             (grid cells this subsector covers)
```

### 5.2 Factions

`Faction` is a read-only template object loaded from the rules database. It is not persisted in the save file — it is reconstructed from the rules DB on load and matched to saved `PlanetFaction` / `RegionFaction` rows by ID.

Key role data: `IsPlayerFaction` and `IsDefaultFaction` (the imperial PDF baseline). Mechanical
behavior flags live in the `[Flags]` `FactionBehavior` value: `CanInfiltrate`,
`PopulationIsMilitary`, `InvadesOnVictory`, `DefendsHostWhileHidden`, `OffersExternalEnemyTruce`,
`UniversallyHostile`, and `Indelible`. `GrowthType` (None, Logistic, Conversion, Consumption, or
Unrest) and scalar values such as `FireDiscipline` remain separate from the flags.

### 5.2.1 Faction Relationships & Target Intelligence

`FactionRelationshipLedger` is the sector-owned, symmetric relationship state. `FactionPair`
canonicalizes its two ids, an absent entry means `Hostile`, and only explicit `Neutral` or `Allied`
entries are persisted. The Chapter and default-Imperial faction are seeded `Allied` during sector
construction; same-faction identity is allied without a row. `UniversallyHostile` overrides stored
relationships and rejects Neutral/Allied mutations. `Planet.RelationshipLedger` is the same ledger
instance, so region control, shared defenses, target selection, and intelligence sharing resolve the
same state. `FactionRelationshipService.GetBaseStance` is context-free; `GetEffectiveStance` applies
only the scoped planetary Insurrectionist external-enemy ceasefire. Role checks such as
`IsImperial` are not substitutes for relationship queries.

Regional awareness remains a sparse, target-agnostic float on `PlanetFaction`. It feeds watch,
stealth, recon quality, listening-post sensors, and strategic-combat surprise. Target intelligence
is a separate sparse `(observer, target, region)` belief store. `FactionIntelBelief` persists
continuous evidence, derived `IntelLevel` (`None`, `Rumor`, `Suspected`, `Confirmed`, `Located`),
stored population/military estimates, and `LastEvidenceWeek`; it never stores whether the target is
actually present. Evidence is capped at 12, decays by 0.75 weekly, and removes a record below 0.25.
Estimates are blended when observations arrive and are never read from live `RegionFaction` objects
by planners or presentation code.

`IntelObservation` is the mutation boundary for beliefs. Public activity, listening posts, patrol
contact, directed recon, battle contact, governor investigation/paranoia, scenario setup, and future
disinformation all submit transient observations. Direct allies receive a one-pass copy of new
awareness and observations; Neutral factions receive neither. Belief-only `PlanetFaction` entries
are retained as intelligence footprints and are attached to the sector event spine when materialized.
Decay is silent; meaningful threshold crossings, first contact, disproof, and relationship changes
are recorded through the campaign-event recorder.

NPC strategy enumerates Confirmed/Located beliefs through `IntelligenceTargetService`, sizes threats
from stored estimates, and resolves current presence only at execution. `StrategicTarget` therefore
supports both a real `RegionFaction` and a phantom search target. A no-contact search consumes its
assigned force allocation and submits negative evidence without creating casualties, control change,
or a phantom operational presence. The player UI likewise renders only player/default beliefs and
uses stored estimates; own Chapter forces remain exact.

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
- **`IsCombatEffective`** — `CanFight && CanMove`. This is the current-battle seam for planning,
  targeting, morale, and casualty handling; player-force deployment uses the separate
  `DutyReadinessService` result, which adds physical deployment requirements and Chapter doctrine.
  Nothing in production means "can shoot but need not walk".

`MotiveImpairment` computes a **speed multiplier per motive location by wound band**, and immobility is simply the product reaching zero — there is no separate binary. Constants live in `CasualtyConstants`, not the rules DB. Below Major 1.0, Major 0.85, Critical 0.6, and zero at Massive/crippled/severed; locations compound **multiplicatively**, so two Critical legs give 0.36 and still fight. **Extremities floor at 0.40 and can never fell a soldier** — a location counts as an extremity when some other motive location on that body has a strictly higher cripple threshold, which makes legs principal and feet extremities on both authored bodies. `BattleSoldier.GetMoveSpeed()` multiplies `Soldier.MoveSpeed` by it, replacing the former binary `IsSlow` / flat ×0.75.

Legs cripple at `Massive` and sever at `Mortal` — deliberately a band apart, so "crippled but not severed" stays reachable for the body's principal motive location, which is the state incapacitation is built on. Feet cripple at `Major`, sever at `Critical`. Thresholds live in the rules DB and are mirrored in the `Body.cs` hard-coded fallbacks; the two must not diverge.

`CasualtyState { Unharmed, Impaired, Incapacitated, Killed }` with `CasualtyStateEvaluator` classifies the outcome from the body plus one external fact (whether the body was recovered). **Power-armor biostasis:** a downed player soldier cannot die of his wounds awaiting treatment, so there is no deterioration clock, no bleed-out pass, and medical care is only ever about *speed* of recovery. Nothing new is persisted — the condition derives from wounds already in the `HitLocation` table, and a recovered brother keeps his squad, so he never trips the null-squad-means-dead path at load.

`PlayerChapterBattleAftermathPolicy` settles the external recovery fact from `BattleOutcome.SideHoldingField`: a side holding the field recovers incapacitated brothers, a lost field moves them through the dead/fallen path with geneseed loss, and a mutual disengagement or turn-cap result counts as recovered. `BattleHistory.IncapacitatedSoldierIds` is kept disjoint from `KilledSoldierIds`; mission reports and debriefs render the two casualty classes separately.

**Individual postings.** `PlayerSoldier.AssignedSquad` is the permanent organizational home. An optional
`IndividualPosting` overrides the soldier's physical location without removing him from
`Squad.Members`, preserving nominal strength, lineage, save ownership, and fallen-brother detection.
`CampaignLocation` represents exactly one ship or one landed region. Format 14 stores only the physical
`Purpose` (`Independent` or `Medical`), location, and start date; operational commitment is the separate
`PlayerSoldier.CurrentOrder` / `Order.AssignedCharacters` relationship. `IndividualPostingService` owns
physical movement, medical detachment, reunion normalization, death cleanup, and individual ship
manifests. Posted soldiers never occupy two locations, and ending an order or procedure does not
teleport them home.

`CampaignLocationService.ForSoldier` resolves a posted character first, then an unposted member of a
MembersOnly formation from its `DutyStation`, and finally a normal squad from its operational location.
`SoldierPresenceService` keeps organizational and physical counts separate: nominal members include all
home-squad members, present members exclude posted soldiers, deployable members additionally require
canonical duty readiness and existing deployment gates, and order participants combine present squad members
with assigned characters. Movement, battle rosters, field care, construction, readiness, and transport
use the appropriate projection rather than approximating all of them with `Squad.Members.Count`.

#### Skill Model

```
BaseSkill
  ├─ SkillKey : string (stable rules-data identity)
  ├─ Category : SkillCategory
  ├─ BaseAttribute : Attribute
  └─ Difficulty : float

SkillRoleAssignment
  ├─ RoleKey : string (code-owned semantic role)
  └─ SkillKey : string → BaseSkill.SkillKey

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
  ├─ DutyStation : CampaignLocation (MembersOnly formations only)
  ├─ ParentUnit : Unit
  ├─ FormationOrdinal : int?        (stable line identity within the parent unit)
  └─ HasBattleHistory : bool       (historical Scout retention predicate)

SquadTemplate
  ├─ Elements : List<SquadTemplateElement>
  ├─ WeaponOptions : List<SquadWeaponOption>
  ├─ SquadType : SquadTypes         (flags: HQ, Scout, Elite, etc.)
  ├─ IsAdministrative : bool
  ├─ MobilityPolicy : WholeFormation | MembersOnly | Fixed
  ├─ BattleValue : int              (derived: sum of members' BV at ExpectedNumber)
  └─ BodyguardSquadTemplate : SquadTemplate   (for Assassination missions)

SquadTemplateElement
  ├─ SoldierTemplate : SoldierTemplate
  ├─ MinimumNumber : int
  ├─ MaximumNumber : int
  ├─ RollsStrength : bool           (opt-in; see below)
  └─ ExpectedNumber : float         (RollsStrength ? midpoint : MaximumNumber)
```

`FormationMobilityPolicy` is authored rules data, not mutable state on a live squad. Normal line
formations use `WholeFormation`; Chapter HQs, Company HQs, Librarius, Armory, Apothecarion, and
Reclusium use `MembersOnly`. A MembersOnly formation is a seated personnel container: it has a duty
station, is excluded from operational movement/orders and regional combat/control rosters, and exposes
`PermitsIndividualDeployment` for its members. `Fixed` is reserved for genuinely immobile future
formations. `CanMoveAsFormation`, `CanAcceptSquadOrder`, `IsPresentOperationalForce`, and
`MayProvideLocalSupport` are separate capabilities so staffing, movement, and combat do not share an
ambiguous `IsOperational` proxy.

**Element strength: establishment vs. rolled.** `SquadFactory` builds an element at `MaximumNumber`,
and `MinimumNumber` is an understrength floor consulted only by `GenerateSquadWithinBudget` when a
squad is scaled down to fit a leftover budget. Most ranged elements in the rules data mean exactly
that — a Tactical Squad's 4–9 marines and a chapter office's 0–50 specialists are establishments
squads are filled *to*.

`RollsStrength` (a rules-DB column, default 0) opts a single element out of that: it musters at a
strength drawn uniformly from `[Min, Max]` every time, which is how an irregular formation turns out
however many turn out. Consequences, all confined to rolling elements:

- The template is priced at `ExpectedNumber`, since that is what generation fields on average.
- `ForceGenerator` charges its budget for the squad that actually mustered, not the template's
  advertised price, so an understrength mob is not billed for bodies it never fielded.
- The roll consumes randomness, so a force containing such a squad walks the shared RNG stream
  differently. Non-rolling elements draw nothing, exactly as before.

As of 0.7.3 the only rolling element in the database is the Insurrectionist Mob's 4–29 insurgents
(`Database/RulesMigration_InsurrectionistUnits.sql`).

**Insurrectionist formations.** The rules migration defines an `Insurrectionist` species rather than
reusing the PDF species: attributes are centered at 10 with σ 2, MoveSpeed is centered at 5 with a
10% spread, and Generic Ranged MOS is 2.0 rather than the PDF's 3.0. The faction owns three
formations — a Scout-flagged Mob with a Ringleader and the rolling 4–29 insurgents, a two-man
Weapon Team with one pooled heavy stubber, and a one-man Firebrand HQ with a Mob bodyguard. Their
weapon sets use autoguns and omit grenades. The Firebrand gives revolt assassination missions a
valid HQ target and supplies the faction's command aura.

The strategic Battle Value remains template-level: an Insurrectionist trooper prices at the PDF
trooper anchor of 5, while the heavy-stubber carrier has a higher itemized tactical value but shares
the same intrinsic template value as his Weapon Team partner. Strategic generation and mission
accounting never persist or substitute the tactical value. Pooled compatibility crews retain the
legacy approximation until their standard-issue allocation is migrated. Omitting grenades does not change the BV of a trooper who has a
working primary because the calculator values mutually exclusive primary-versus-sidearm fire; the
grenade contributes only to a fists-only profile. Light Armour is retained because armour values
below roughly 10 are invisible against the current reference threats. The faction is therefore
distinct in organization and force behavior, not cheaper per man.

`PlayerForce` contains:
- `Army : Unit` — the top-level chapter unit (order of battle root)
- `Fleet` — aggregates the `TaskForce` list
- `Requests : List<IRequest>`
- `BattleHistory : Dictionary<Date, List<EventHistory>>`
- `Army.SquadMap : Dictionary<int, Squad>` — flat lookup populated by `Army.PopulateSquadMap()`

Non-Scout line formations have a durable designation allocated by `FormationOrdinalAllocator` and
formatted by `SquadDesignationFormatter`; the designation does not change when a Sergeant leaves, dies,
or is replaced. Empty non-Scout formations retain their identity and lineage. Empty Scouts without battle
history are discarded after deployment references are cleared, while Scouts with recorded battle history
remain as historical, unlocated formations. `SquadLifecycleService` is the shared owner of this cleanup
across transfer, death, recruitment/procedure, and other final-member removal paths. The battle aftermath
marks a Scout's history before casualty removal so a wiped historical formation is retained.

Chapter Muster uses `MusterPlanService` for an editable, stable draft of transfers, promotions, role
changes, and new/reconstituted formations. It validates the complete plan before any mutation and
revalidates at commit. `FleetCapacityPlanService` supplies direct-placement and bounded whole-squad
rebalance results; it never splits squads or silently relocates unrelated formations.

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
  ├─ AdministrativeStations : IEnumerable<Squad>
  ├─ IsFlagship : bool
  └─ AvailableCapacity : int
```

`ShipCapacityService` calculates present passenger load and validates whole-squad boarding. The separate
administrative station manifest reserves berths for unposted members of seated formations without putting
those formations in `LoadedSquads`. Individual postings consume one berth and are not double-counted when
the posting and home squad reference the same ship. Land and embark operations update ship manifests,
region presence, and order cleanup atomically; failed capacity or live-state validation changes none of
those relationships. `FlagshipService` selects and validates the unique player flagship using authored
template precedence/hull size, then stable capacity and ship-id tie-breakers. `AdministrativeStationService`
seats and relocates every administrative formation without moving members who are independently posted.

### 5.6 Missions & Orders

```
MissionType enum:
  Advance, Ambush, Assassination, Extermination,
  Recon, Sabotage, Patrol, Defense, Construction, Recruitment

Mission
  ├─ MissionType : MissionType
  ├─ RegionFaction : RegionFaction   (the target)
  └─ MissionSize : int               (tier / intensity)

SabotageMission : Mission
  └─ DefenseType : DefenseType       (Organization, Detection, Entrenchment, AntiAir)

ConstructionMission : Mission
  └─ ConstructionType : DefenseType  (Organization, Detection, Entrenchment, AntiAir)

Order
  ├─ AssignedSquads : List<Squad>
  ├─ AssignedCharacters : List<PlayerSoldier>
  ├─ OwnerFaction : Faction
  ├─ Mission : Mission
  └─ LevelOfAggression : Aggression  (Avoid, Cautious, Normal, Attritional, Aggressive)

OrderForce
  ├─ Squads / Characters
  ├─ IsEmpty / ParticipantCount
  ├─ AllPlayerSoldiers / AllSoldiers
  └─ OwnerFaction

MissionContext  (runtime only, not persisted)
  ├─ Order : Order
  ├─ MissionSquads : List<BattleSquad>
  ├─ OpposingSquads : List<BattleSquad>
  ├─ Log : List<string>
  ├─ DaysElapsed : int
  ├─ Impact : float
  └─ EnemiesKilled : int
```

An order is a participant set, not a squad-only container: it is valid when it has at least one assigned
squad or character. Any mission type may therefore be squad-only, character-only, or mixed. `OrderForce`
is the shared projection used by scheduling, field effects, reports, and aftermath; ownership comes from
the explicit `OwnerFaction`, never from the first squad. `OrderForceService` is the mutation boundary for
the two participant collections and their pointer pairs (`Squad.CurrentOrders` and
`PlayerSoldier.CurrentOrder`). An order ends only when its complete participant set is empty or normal
mission lifetime rules resolve it.

Characters belonging to MembersOnly administrative formations remain in their home squad's nominal
roster, but are independently targetable participants when assigned. `SpecialistAvailability` enumerates
the global character roster by effective location rather than scanning only a home squad's regional list.
`CharacterAvailabilityService` supplies decision-specific evaluations and reason codes for movement,
order assignment, organizational transfer, local support, and continuous tasks. Compatibility aliases for
format-13 callers remain obsolete; current production state is `AssignedCharacters`, `CurrentOrder`,
and physical `IndividualPosting` state.

`Recruitment` is a persistent construction-like task order for the 10th Company recruitment staff. It is
not a combat mission and is excluded from combat/no-contact routing while still using the same order
participant and persistence machinery. Its staff are synchronized daily from eligible, co-located
characters assigned to the task.

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

### 5.9 Recruitment

`PlayerForce.RecruitmentProgram` is one persisted aggregate keyed to the Chapter Home World. It is created by the Promised World win and remains locked until the founding setup is complete. The aggregate owns the standing doctrine (`RecruitmentPolicy`, attribute thresholds, genetic threshold, source-world type), staff assignments, unscreened cohorts, qualified candidates, aspirants, implantation procedures, and program events.

Staff assignments are synchronized from the administrative Scout/Recruitment squad rather than maintained by a separate roster. Eligible Scout Sergeants provide non-genetic screening and training capacity; Apothecaries provide genetic screening, implantation capacity, and medical rating; Chaplains or Judiciars provide spiritual screening and training capacity. The Captain is the administrative master of recruitment but is not a throughput post. Screening capacity is the minimum of the three independent screening roles, and aspirant training capacity is the minimum of Sergeant and Chaplain capacity, so one missing role pauses the program rather than being substituted by surplus staff elsewhere.

`RecruitmentTurnProcessor` advances the aggregate once per campaign week during Chapter upkeep: it grows or draws the unscreened pool, applies compliance and policy, generates qualified candidates, admits candidates into Phase 0 when capacity exists, advances training and implantation through Phases 1–13, and promotes survivors through the player-directed neophyte and Scout path. Each implantation phase consumes one compatibility check; Phase 1 consumes one mature progenoid, and Black Carapace completion moves the survivor into the player-selected reserved Devastator seat. The forecast service uses the same centralized `RecruitmentRules` as processing so the Recruiter preview and simulation share rates, costs, age windows, and world modifiers.

Persistence is isolated at the save boundary by `RecruitmentSaveMapper` and `RecruitmentDataAccess`. The program and its child rows persist cohorts, candidates, aspirants, skill points, aspirant history, pending procedures, and the program log; domain objects do not issue SQLite commands directly.

Successful aspirant-to-neophyte placement also records a typed `MentorAssigned` event after the
new brother has a stable id, recorder attachment, and Scout-squad membership. Selection is
deterministic: a living squad leader/Scout Sergeant is preferred, then an active recruitment-staff
Scout Sergeant by descending Leadership rating and ascending soldier id. If neither exists,
promotion still succeeds without an invented relationship. The event stores mentor and squad
display snapshots, so later mentor death or transfer cannot rewrite the historical relationship;
founding generation and ordinary transfers do not emit it.

---

## 6. System Implementations

### 6.1 Turn Controller

`TurnController` is the single entry point for end-of-turn processing, called by `MainGameScene.OnEndTurnButtonPressed`. It is an orchestration facade: phase behavior lives in focused processors under `Helpers/Turns`. Two context objects separate lifetime and responsibility:

- `GameSession` is the stable dependency set for simulations belonging to one loaded game: rules, sector, mutable campaign date, and `IRNG`. The production constructors build it once from `GameDataSingleton` plus `StaticRNG`; an internal constructor accepts an explicit session for isolated tests and future alternate simulations.
- `SimulationContext` is per-run state: the session, `TurnResolutionResult`, `TurnIntelligenceLedger`, separate player/all-order lists, and an optional planet scope for generation-time forward simulation.

`ProcessTurn(Sector)` returns the run's `TurnResolutionResult`; the retained sector parameter must be the same object owned by the session, preventing rules/date/RNG from one game being combined with another sector. The controller's `MissionContexts`, `SpecialMissions`, `StrategicCombatResults`, and `ScenarioNotification` properties remain as compatibility views for existing tests.

The processors are divided by simulation responsibility:

- `TurnOrderPlanner` appends hostile-faction and defensive Imperial PDF orders without owning mission resolution.
- `MissionTurnProcessor` resolves diversion shaping, strategic/tactical missions, and construction; `MissionAftermathProcessor` applies strategic consequences and cleans consumed missions/orders. `InvaderPresenceService` provides the common foothold operation used by tactical aftermath, strategic combat, and planetary expansion.
- `ChapterUpkeepProcessor` owns weekly medical and training work; `FleetTurnProcessor` advances travel and delegates warp-subjective training back to the shared upkeep processor.
- `PlanetTurnProcessor` owns planet/region simulation, revolts, governors, and intelligence-derived special missions. `TurnIntelligenceLedger` accumulates awareness gains, directed observations, recon evidence, and one-pass Allied sharing until the intelligence phase applies them. Target-belief decay, public-activity sampling, and belief-backed special opportunities happen in this phase.
- `ScenarioTurnProcessor` resolves campaign objectives after the simulated world state settles. `ScenarioMetricsCollector` owns the optional debug-only opening-scenario trace.

`ProcessTurn` preserves this phase order:

1. Advance the campaign date, clear the result/intel ledger, and begin scenario metrics.
2. Append NPC orders from the observers' current Confirmed/Located beliefs and defensive estimates.
3. Resolve strategic combat, tactical missions, construction, and squad-less biomass feeding; record encounter contacts and remove consumed special missions.
4. Apply mission aftermath, Chapter medical/training upkeep, fleet movement, and planet simulation.
5. Decay awareness/beliefs, apply listening-post and recon gains, generate public/patrol/contact observations, fan them out once to Allies, then reconcile belief-backed opportunities and governor requests.
6. Resolve the campaign scenario, finish diagnostics, clean resolved player orders, and return the result.

`SimulatePlanetForward` reuses the same planning, mission, aftermath, planet, intelligence, and diagnostic processors for generation-time world evolution, but intentionally omits date advancement, Chapter upkeep, fleet movement, other planets, and scenario resolution. Because it has no following planning pass, it sweeps the transient AI forces (patrol screens, recon parties) still standing after its last week, so the world hands off to the player with nothing landed on it.

Non-deployed non-Scout marines receive weekly work-experience training through `ApplySoldierWorkExperience`; Scout squads are routed through `TrainScouts` with each squad's selected database-defined `ScoutTrainingOption`. Scout squads assigned to missions are excluded from weekly Scout training. The option catalog includes Balanced as an ordinary option whose linked `TrainingProfile` defines its exact distribution; the selected stable option key is persisted with the squad.

### 6.2 Faction Strategy

`FactionStrategyController.GenerateFactionOrders(Faction, Sector)` runs per non-player, non-default faction per turn. For each planet where the faction has a public presence:

1. **Force assessment:** Compute `RequiredGarrison` per region from the observer's confirmed target beliefs and stored estimated military strengths, scaled by regional awareness; then derive `SpareTroops` from the concrete organized pool. The planner never rounds a real unknown `RegionFaction` into an estimate.
2. **Offensive planning:** Enumerate the faction's `Confirmed`/`Located` `FactionIntelBelief` entries through `IntelligenceTargetService`. If adjacent committed force exceeds the stored estimate × 1.5, generate an `Advance`, raid, or recon order. A current `RegionFaction` is attached to the `StrategicTarget` only so execution can resolve contact; it is not used to discover an unknown target or size the force.
3. **Construction:** Convert remaining `SpareTroops / 100` to build points. Reorganization transfers `ReorganizationBattleValuePerEffort` BV from the disorganized pool to the organized pool per effort point; other construction improves Entrenchment, Detection, or Anti-Air (costs scale as `2^currentLevel`).
4. **Patrol:** Any remaining `SpareTroops × 10` become a `ScoutPatrol` order.
5. **Swarm operations (`GrowthType.Consumption` only):** Spread, then feed, from what is left.

Transient AI forces — patrol screens and recon parties — are generated from nothing each pass and cleared at the top of the next one (`ClearStaleTransientSquads`). Recon was previously omitted from that sweep, and a party that survived its week was landed in its home region by `ExfiltrateMissionStep` and never removed, so every completed NPC recon left a permanent ghost squad inflating the region's search difficulty.

**Swarm operations (Consumption factions).** Spreading and biomass feeding are planned taskings drawing on the same per-region `SpareTroops` budget as everything above them, and they run last so both receive the true residual.

- **Spread** applies directly, like garrison and front reinforcement, rather than issuing an order: it relocates strength to the adjacent region of highest biomass (prey population plus carrying capacity) when that region is strictly richer than home, sized as `SpareTroops × depletion × ConsumptionExpansionShare`. It is deliberately not folded into the ordinary offensive path — the richest neighbour is frequently empty ground with a high carrying capacity and no enemy `RegionFaction` to target at all, so sharing the offensive code path would cost the consumer exactly the moves that matter most.
- **Feed** commits whatever remains as a squad-less `FeedMission` carrying the committed battle value. It is dispatched from the mission phase beside squad-less construction (`ProcessFeedOrders`), resolves instantly, and creates no `MissionContext`. No force is generated: materializing squads for a million-strong swarm would be absurd and there is nothing for them to do tactically. Because a `PopulationIsMilitary` faction's BV pool and headcount are the same number, the committed value drops straight into the biomass allocator's troop term.

Both were previously side effects of `PlanetTurnProcessor.UpdatePlanet` that re-derived a Consumption faction's whole deployed strength from `Population × Organization`, so the same troops fed, spread, defended, patrolled and attacked in the same week; phase ordering ("spread before consume") de-duplicated those two against each other and left both blind to everything else. The planner's budget replaces that ordering. The planner only sees `IsPublic` region-factions, so `UpdatePlanet` retains a hidden-consumer fallback (`ConsumptionTurnProcessor.ResolveHiddenExpansion` / `ResolveHiddenFeeding`) that keeps the old whole-strength behaviour for a force nothing planned for. `MissionType.Feed` is appended to the enum for the same save-ordinal reason as `ShowOfForce`.

**Player construction (squad-driven fortification).** The player can order a squad in its own region to build a defense (Entrenchment / Detection / Anti-Air), creating a `ConstructionMission` targeting the player's `RegionFaction`. Unlike the NPC squad-less construction (resolved at a flat `MissionSize` in `ProcessConstructionOrders`), a construction order that carries a squad is routed in `ProcessCombatMissions` to `ResolveSquadConstruction`: every able soldier contributes its `Engineering (Fortification)` skill value, the sum is divided by `EngineeringBuildDivisor` (100) and floored (minimum 1), and the result is applied via the shared `ApplyConstruction`. The order persists, so the squad accumulates defenses over successive turns. `Engineering (Fortification)` is an Intelligence-based Tech skill trained by all combat marines at low weight.

**Multi-faction regions.** A region can hold several enemy factions at once (the opening scenario puts a public Tyranid incursion on top of a still-hidden cult), and three subsystems previously collapsed "the enemies" to the first one found. `Region.RegionFactionMap` and `Mission.RegionFaction` already supported the case; the work was selection, aggregation, and presentation, with no data-model or save change.

- **Order targeting.** Orders carry an explicit target `RegionFaction` instead of a `FirstOrDefault` enemy. Only the two *synthesized* enemy-directed missions need a selector — **Advance** and **Diversion**. Own-region missions (construction, DefenseInDepth, Patrol, Training, LastStand) target the player's own `RegionFaction`; **Recon is region-scoped** and takes any valid anchor, because it discovers which factions are present and so must not require pre-selecting one; and the special missions (Ambush, Assassination, Sabotage, Extermination) already carry a concrete target from generation. With one eligible enemy the selector auto-fills read-only, keeping the common case one click; with two or more a pick is required. The default/PDF faction is never targetable — it remains an ally.
- **Detection** is a property of the region, not of the mission's target: an intruder is seen by whoever watches the ground it crosses. Every term of the stealth model sums over `Region.GetDetectingEnemyFactions()`, and `Region.SelectSpotter` draws the actual spotter/interceptor from that same set weighted by the same per-faction `WatchScore` that made the crossing hard, so difficulty and interceptor cannot disagree about who was looking (§6.5). One aggregated check per day, deliberately not N independent rolls.
- **Intelligence opportunities** are budgeted proportionally to deployed strength across the region's enemy factions. The old counter subtracted a region-wide `SpecialMissions.Count` while factions were processed in dictionary order, so the first-iterated faction spent the whole region budget and the rest were starved.
- **Belief-backed opportunities** are budgeted from the player/default observer's stored estimates. Confirmed beliefs can create targeted special opportunities even when no current `RegionFaction` exists; a phantom search consumes the assigned force allocation and records negative evidence on no contact.
- **Strength display** uses the intel ladder and stored estimates (`FactionIntelBelief`) rather than awareness-based rounding of live truth. Rumor/Suspected entries show no exact numbers; Confirmed/Located entries use the estimate stored by the observer. The same belief query feeds the target-faction dropdown, the planet/region detail panes, and NPC target enumeration.

`RegionFaction.GetDeployedStrength()` (`MilitaryStrength × Organization / 100`) is the shared "troops actually fielded here" figure behind garrison sizing, the opportunity budget, and the stealth model's ambient term; `MilitaryStrength` resolves the horde-vs-civilian split, so it is correct for a `PopulationIsMilitary` faction with no garrison at all.

**Strategic NPC combat.** NPC-only assaults cross from tactical to `StrategicCombatResolver` when either side exceeds `MaxTacticalActors` (120), generated forces would exceed `MaxGeneratedSquads` (24), or committed strength exceeds `MassCombatBattleValueFloor` (1,500 BV). Named/player squads always remain tactical. Strategic resolution works directly in conserved BV pools: only organized BV deploys and takes ordinary battle casualties; effective strength combines committed BV, aggression, faction quality, entrenchment, and awareness-derived surprise. A Gaussian combat ratio determines bounded casualties and whether the attacker clears the 1.10 capture threshold. Every participating faction receives reciprocal `BattleContact` observations at Located level with estimates based on the engaged force, not planetary totals. Invaders establish a foothold on victory, raiders return survivors, and no transient tactical squads are generated. Equations and rejected alternatives are retained in `Design/Reference/BattleLogic.md`.

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

**Intelligence pipeline.** Each planet's intelligence pass first decays sparse `RegionAwareness` and target beliefs, then applies listening-post awareness and accumulated recon evidence. It next generates target observations from public activity, successful patrol contact, directed recon, and battle contact, applies them in stable faction/region/target order, and fans each new report once to currently Allied observers. Positive observations blend supplied estimates; negative observations affect only the named target. The ledger exposes counters for materialized awareness rows, belief rows, observations applied, and Allied copies.

Recon and patrol have distinct products: recon always changes target-agnostic regional awareness and directed recon also submits target evidence; patrol is an active search whose successful sweep submits `PatrolContact`; a listening post raises awareness and improves the quality of later observations rather than creating a belief by itself. Public activity gives observers with planetary presence or existing awareness at least Confirmed evidence. Strategic and tactical encounters record contact from the forces that actually participated. Governor Investigation and Paranoia use the same observation boundary, and scenario setup may seed explicit beliefs.

**Fog of War (UI gating).** Enemy visibility on the planet-tactical and region screens comes from the Chapter/default observer's `FactionIntelBelief` records, not from `RegionFaction.IsPublic` or `Region.IntelligenceLevel`. None is omitted; Rumor and Suspected are attributed reports without exact numbers; Confirmed and Located display the observer's stored population/force estimates. Own Chapter forces remain exact. A belief can be stale, false-positive, or absent while a real presence exists; explicit no-contact searches submit negative evidence and never create an operational phantom.

Each unconsumed special mission has a 25% chance of expiring each turn. Belief-backed opportunities are reconciled after the intelligence pass, so NPC planning, governor requests, special missions, and player presentation consume the same post-observation state.

**Governor Requests:**
- For each planetary leader with positive opinion of the player: consume Confirmed hostile beliefs; if none exists, Investigation can submit evidence about a real public hostile presence and Paranoia can submit a Rumor about a plausible hostile faction absent from the planet.
- If a threat (real or imagined) is detected: roll `RequestGenerationRate × Neediness × OpinionOfPlayerForce`. On success, `RequestFactory.GenerateNewRequest` creates a `PresenceRequest` and adds it to `PlayerForce.Requests`.
- `RequestGenerationRate` (`SupplyEconomyRules`) throttles the whole petition economy. Both gates are linear in the governor's traits, so it scales only how often worlds petition, not which ones do. Sector-wide arrivals per week ≈ `governorCount × 0.125 × RequestGenerationRate`; at the shipped 0.006 that is ~0.6/week for the ~800-governor production sector, holding ~13 petitions open at a time.
- The deadline comes from `SupplyEconomyRules.SeverityDeadlineWeeks`, keyed by the `RequestSeverity` that `ClassifyRequest` derives from the local threat ratio: Concerned 39 weeks, Serious 26, Desperate 13, Existential 13. It is deliberately a property of the petitioning world, not of where the Chapter's forces are — the Chapter may be spread across several task forces, so there is no single position to measure against, and keying off the nearest asset would tighten every deadline as the player expanded. Reachability instead falls out of geography: a round trip costs 4 weeks of system transit before any warp travel (`TaskForce.SystemTransitWeeksPerEnd`), so a short fuse is implicitly a proximity requirement and only urgent petitions near a standing force can be answered.
- Severity is classified from stored target estimates before the commitment package is built, so `ForceCommitmentPackage.CompletionDeadlineWeeks` carries the real fuse length and `RequestValueCalculator`'s throughput premium prices urgent petitions higher without any separate urgency term.
- Request valuation uses the code-owned typed `SupplyEconomyRules` profile. The player sees squads, qualifications, service weeks, deadlines, progress, and the fixed offer; Battle Value and Battle-Value-Time remain internal accounting units. `GovernorTurnProcessor` advances request state, creates pledges on fulfillment, and applies opinion/cooldown consequences. `PledgeDeliveryProcessor` runs at sector scope because deliveries affect the Chapter economy and may originate from many worlds.

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
WatchScore(rf) = SurveillanceWeight × rf.GetOwnRegionAwareness()
               + Magnitude(rf.GetPatrolStrength())
               + min(AmbientSearchCap, AmbientWeight × Magnitude(staticStrength))

staticStrength = max(0, rf.GetDeployedStrength() − rf.GetPatrolStrength())
Magnitude(x)   = x ≤ 0 ? 0 : log10(1 + x)

difficulty = Σ WatchScore(enemy) + Magnitude(intruderHeadcount) − intruderRegionAwareness
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

**Orchestration boundaries and lifetimes:**

`BattleTurnResolver` owns the single live `BattleState`, turn sequencing, action/wound execution,
casualty cleanup, history, completion, and `BattleHistory.Outcome`. Its battle-scoped collaborators
share that state and grid; none accepts a resolver callback or keeps a second authoritative roster.

| Owner | Responsibility |
|---|---|
| `BattleMoraleService` | Starting strength/leader bookkeeping, turn-start morale snapshots, checks and squad effects, mob coercion decisions, and ever-routed IDs |
| `BattleRoundMetrics` | Recent value/damage histories and force-metric construction |
| `BattleWithdrawalService` | Continuation, withdrawal/pursuit transitions, role constraints, rear guards, contact/escape handling, and current-turn pursuit pairings |
| `BattleActionPlanningCoordinator` | Warm live views, create pass data and frames, initialize horizons, schedule indexed decisions, then declare and build actions serially |
| `SquadEngagementPolicy` / `SoldierActionPlanner` | Select legal squad options and root-action descriptors without action emission or battle RNG access |
| `SquadActionBuilder` / `MeleeActionBuilder` | Serial declaration and materialization, including deferred charge intent; receive the shared battle RNG explicitly |
| `RangedTargetSelector` / `RangedShotEvaluator` / `BlastThrowEvaluator` | Target ranking and sticky aim, shot estimates, and blast selection respectively |
| `SoldierMovementProjector` / `SoldierMovementPlanner` | Movement calculation versus serial reservations, action construction, and speed commitment |

`BattleSquadPlanner` is a compatibility/composition facade, including ambush aim seeding and
existing test entry points. Its policy, builders, and targeting/movement collaborators live for
one planning pass. Both side planners share one fresh `BattlePlanningContext`; workers have indexed
result slots, not private caches. `SquadPlanningServices` exposes rules, live state, tracing, and
that memo but no RNG or action sink. `RangedTargetingServices` narrows this further. These are
capability boundaries, not deeply immutable model snapshots. Execution and aftermath retain the
same battle RNG stream through `BattleExecutionContext`.

The resolver preserves this phase order: reset/advance/recovery; morale snapshot; planning;
pending mob suppression; shooting; movement; melee; wounds; casualty cleanup; metrics;
escape/contact; morale; continuation; history; completion. Terminal casualties precede contact
resolution. Both sides' force metrics are captured before the first morale check; each returned
side-routed transition is handled immediately, with a terminal guard before the second side.
Withdrawal returns a typed terminal request; only the resolver constructs and assigns the outcome.

Planning prepares role constraints serially, warms live views, builds paired frames and the shared
horizon, then evaluates every choice before any declaration. Worker jobs receive the engagement
policy. Pairings replace the lifecycle service's prior-turn pairings before all decisions are
declared serially; only after every declaration are actions built serially. Stable side, squad,
soldier, target, and action ordering preserves ties and RNG consumption. Declaration changes
speeds; construction reserves destinations and can change speeds again. Memo use must respect
these barriers. Candidate movement cannot commit through its projection API; construction
revalidates against current reservations. Deferred charges re-project after ordinary reservations
are cleared. Materialization preserves the selected root descriptor and its readiness/ammo rules.
Trace formatting stays lazy when disabled. Fixed-seed resolver characterization and the battle
tests cover outcomes, action state, and real degree-1 versus degree-4 planning equivalence.

**Loadout allocation (`BattleSquad.AllocateEquipment`):**
- Iterates members, allocating weapons from the squad `Loadout` (weapon sets).
- One-hand weapons allow dual-wielding; two-hand weapons consume both slots.
- A one-hand ranged weapon leaves the off-hand available for a one-hand melee weapon.
- Equipped weapons are bound to physical hand groups. Disabling an arm or hand drops the weapon gripped by that group; two-handed weapons require two functioning groups and drop if either group is disabled.

`BattleSquad.ReallocateEquipment` runs at the start of every battle in a mission. Pooled squads retain
their compatibility `WeaponSet` allocation, while personal-equipment elements resolve the itemized
planet/chapter/personal layers through `EquipmentLoadoutService` and the rules catalog. The resolved
loadout is complete: armor, carried quantities, gear, weapons, and initial-ready preferences are
validated before runtime conversion. The mission bridge retains the same physical weapon objects in
the squad's mission pool, so a fallen carrier's recoverable weapon is reassigned to the best valid
survivor without refilling its magazine; reassignment preserves the weapon's current magazine and shared reserve state.

The physical `RangedWeapon` and `MeleeWeapon` objects retained by `BattleSquad` are the sole mutable
mission equipment state. All itemized weapons from one resolved loadout reference one
`AmmunitionReservePool`, so compatible weapons draw from the same package count and a loadout with no
package has no reserve. Magazine weapons reload from that pool, incremental weapons load partial
amounts, consumable grenades decrement their carried quantity, unlimited weapons spend nothing, and
self-regenerating profiles advance recovery at turn boundaries without a reload action. Initial-ready
orders are copied onto the physical weapons and applied by `BattleSoldier`; lower numbers have higher
priority when hand requirements conflict. Ordinary fire commits the full legal burst; recoil controls
hits and never refunds ammunition. Reload progress belongs to the weapon, not the soldier, and no
reload path creates rounds.

The tactical `BattleSoldier.EffectiveBattleValue` is derived from resolved armor, weapons, and gear
and cached by `(SoldierTemplate.Id, EquipmentSignature)` in `EffectiveBattleValueCalculator`. It is
used by tactical side strength, target/removal scoring, and remaining-force calculations. The
intrinsic `SoldierTemplate.BattleValue` remains the strategic value used by force generation,
mission sizing, and persistent casualty accounting. The compatibility allocation retains intrinsic
values for legacy fixtures until their pooled UI is migrated; itemized personal carriers use the
effective value immediately.

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

`RangedWeaponTemplate.TemplateType` selects normal fire (`0`), a cone (`1`), launched blast (`2`), or thrown blast (`3`). `AreaRadius` carries cone half-width or blast radius. Template attacks pre-resolve geometry, victims, scatter, and wounds once; replay reuses the stored result rather than consuming new randomness.

- `ConeTemplate` projects the weapon's full-range cone along the shooter-to-target direction. A combatant is caught when any occupied footprint cell lies inside it. `AreaAttackAction` auto-hits every caught friend or foe except the shooter, applies normal armor/hit-location/wound resolution per victim, and consumes one ammo per burst. The planner scores the entire firing line and never aims a cone weapon.
- `BlastTemplate` resolves an aim cell, then converts a failed normal-curve skill check into margin-proportional scatter in a pre-resolved random direction. A combatant is caught when any footprint cell lies inside `AreaRadius`; the thrower is not excluded. `BlastAttackAction` scales damage quadratically from full at the impact center to zero at the rim before armor.
- Thrown blast range is `Strength × MaximumRange`; launched blasts use `MaximumRange` directly.
  `WeaponSet.GrenadeWeapon` remains a pooled-compatibility slot, while itemized kits represent
  grenades as finite `ConsumableItem` equipment instances.
- The planner scores a throw as expected enemy BV removed minus expected friendly/self BV lost, **integrated over both the delivery scatter distribution and the per-victim damage roll** (`BlastThrowEvaluator.EvaluateThrow`): every miss node lands the template somewhere and pays its friendly cost, so a throw that only catches the squad when it scatters is not free. This replaced an earlier perfect-impact-times-delivery-confidence estimate; `deliveryConfidence` now survives only as a trace field, and neither half carries an arrival-time discount, since the grenade detonates this turn (matching the conventional ranged path — engagement-scoring Phase 3). A grenade must also beat the soldier's best conventional action. Two tie-breaks keep grenades from displacing ordinary fire: **ties go to the gun**, and a **melee-engaged soldier never throws**. Empty grenades restock through the normal reload branches on idle turns rather than a separate resupply path. Movement options price the actual Bulk/aim transition directly, so a separate movement-retention threshold is unnecessary.
- `BattleValueCalculator` values cones and blasts through density-scaled expected victims, ammo/reload duty cycle, template reach, blast falloff, and the same reference-threat panel used for conventional weapons. A grenade is valued as a sidearm (`max(primary, grenade)`), matching the planner's mutually exclusive throw-or-shoot choice.
- Remaining template/ranged work is tracked in `Design/Active/RangedCombatFollowUps.md`.

**Melee resolution.** Attacks per melee action are `AttackSpeed/10 × weapon.AttackSpeedMultiplier`, with the fractional remainder resolved probabilistically in `MeleeMath`. `AttackSpeedMultiplier` replaced the old `ExtraAttacks` column; all shipped values are currently `1.0`, leaving per-weapon speed differentiation as an unused data lever. Dual wielding two one-handed melee weapons grants one off-hand strike using the off-hand weapon's own profile; its defensive value comes entirely from weapon `ParryModifier`s summed across equipped weapons (the unarmed fist is `−1`), with no flat dual-wield bonus — an early flat `+1` was removed because it stacked a free defensive bonus on top of the evasion that already models Tyranid natural weapons. `BattleSquadPlanner.BuildStrikePlan` distributes strikes across adjacent enemies, committing to one target until cumulative take-out confidence reaches 75% before moving on.

The contested melee roll is calibrated to tabletop's intuition band rather than to raw skill differences: `MeleeDefenderAdvantage = 0` (equal skill trades at ~50%, tabletop's "hit on 4s") and a per-side roll σ of `6`, making each skill point worth ~5.6% near parity and compressing large gaps toward tabletop's clamped 33–67% ladder — a Genestealer runs ~72% out / ~28% back against a marine. `StrengthMultiplier` values are doubled against dialed-down heavy-tier `WoundMultiplier`s; the deliberate balance stance is that base marines are two-wound soldiers.

**Battle Value derivation.** `BattleValueCalculator` is an engine-faithful valuation, not a stat-line heuristic: it replays the real to-hit/damage math (recoil decay, aim-vs-fire arbitrage, single-target overkill caps, ammo duty cycle, melee closing and engagement limits) against a four-profile reference threat panel — swarm chaff, light infantry, elite infantry, monster — to derive expected kills per turn and survival turns, then computes `BV = 5 · √(offense × durability) · command`. `SoldierTemplate.BattleValue` rows are generated from it (PDF Trooper 5 as the anchor; current strategic anchors include Tactical Marine 9, Genestealer 13, and Melee Carnifex 30) and the `StrategicCombatRules` BV anchors track those values. An offline `Compute-BattleValue.ps1` harness reproduces the calculator to 6-decimal parity for bulk regeneration. **Player-soldier BV intentionally remains the template guideline rather than a live skill-tracking value** — enemy forces size their responses by estimating the player force, not by reading concrete data on every marine.

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

**Morale, withdrawal, and pursuit.** `BattleSideState` carries force intent (`Engaged`, fighting/rear-guard withdrawal, pursuit, rout, or disengaged), the withdrawal heading, covering/rear-guard assignments, and starting force metrics. Organized withdrawal alternates Cover and Bound roles; pursuers choose Break Off, Follow, Press, or Standoff behavior from contact and expected value. Follow compares stationary fire, jog-and-fire, and a no-fire run in the same engagement value choice; Press is the committed running posture. `WithdrawalForecast` compares projected BV preservation, including masked departure and command-collapse risk, before assigning an autonomous rear guard. Pursuit decisions retain their actual target-squad pairing. After movement, an unpursued withdrawing squad beyond every enemy's useful attack range disengages when pairwise relative closing speed puts the earliest possible interception beyond the engagement planner's two-turn retargeting horizon; this is an open-ground contact abstraction, not a battlefield edge. Running soldiers lose melee guard; Burrow can break contact immediately.

**Standoff invariant.** A pursuing force may enter `Standoff` only when it has no meaningful speed advantage over the withdrawing target (using the shared pursuit-speed tolerance), cannot reach melee this turn, and has a worthwhile shot available at the current range. `Standoff` means standing fire: every ordinary squad in that posture may `Hold` and aim/fire, but may not `JogToward` or `RunToward`. If movement toward the enemy is desired, the squad must receive a different pursuit posture or role; a standoff squad must never turn an unwinnable equal-speed chase into a running pursuit.

After each combat round, `BattleMoraleEvaluator` computes local shock from current/cumulative casualties, leader loss, nearby routing allies, and local outnumbering, then multiplies it by force-wide disadvantage. Per-soldier resolve is a convex Ego function. Synapse coverage skips the check; command auras reduce shock without granting immunity. Squads aggregate to `Steady`, `Shaken`, or sticky `Routing`; routing preempts the normal plan and enters the same pursuit, outcome, aftermath, and replay pipeline as voluntary withdrawal. Morale and withdrawal tunables live in code (`MoraleConstants` and the withdrawal planners) and are calibration surfaces rather than rules-data facts.

Battle completion produces a typed `BattleOutcome` with end reason, field holder, and disengaged/eliminated/routing/rear-guard squad ids. Typed `BattleEvent`s record withdrawal, cover, rear guard, pursuit, rout, and disengagement transitions for replay and narrative consumers.

**Player aftermath event emission.** `BattleAftermathDependencies` allocates one immutable
`BattleEventContextSnapshot` per tactical battle. `PlayerBattleAftermathSink` passes that context
through the aftermath boundary so battle participation, First Blood and kill milestones,
incapacitation, death, gene-seed outcome, Last Brother Standing, Squad Held Against Odds, and
`BattleResolved` share one correlation key. Casualty facts are emitted after final body-state
settlement: only a crippled, non-severed vital location marks an incapacitation as a near-death
source; a severed vital or an unrecovered brother follows the typed death disposition. Last Brother
Standing requires five starting Chapter participants and exactly one combat-effective finisher.
Squad Held Against Odds is evaluated per starting player squad at a 50% casualty threshold under a
defensive commitment, only when the Chapter held the field. All achievement thresholds live in the
validated `NarrativeEventRules` object, and recorder dedupe keys make replayed aftermath idempotent.

The canonical events project to minimal factual Soldier History lines. Chapter-level legacy battle
history remains a compatibility/reporting view; it is not parsed to classify typed death,
incapacitation, gene-seed, or achievement facts.

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

- **Natural healing.** Applies `Wounds.ApplyWeekOfHealing()` to every wounded player-soldier hit location regardless of deployment, *except* severed non-vital locations that require a replacement procedure. Crippled locations do not require replacement for now. `HitLocation.IsReplacementEligible` is the single source of truth for that exclusion and is shared with the Apothecarium view and the Squad Screen, so the three surfaces cannot disagree. Cadence and the daily Astartes pass are specified in §5.3.
- **Procedure resolution.** `ResolveProcedures` decrements weeks-remaining and, on completion, clears the location's wounds and removes the procedure. Cybernetic completion sets `HitLocation.IsCybernetic`; vat-grown leaves it clear. Because wounds are not cleared until completion, a marine under a procedure stays out-of-action automatically rather than needing a separate flag.

Medical completion returns a bounded list of `CompletedMedicalProcedure` facts. Each successful
primary target emits one `BodyPartReplacement` event, recording the method, prior cybernetic state,
duration, cost, and (when applicable) the source incapacitation episode. The canonical ledger keeps
an in-memory `SoldierId → OpenNearDeathEpisode` projection: a typed qualifying incapacitation
opens it, and a recovery referencing that source closes it. `ChapterUpkeepProcessor` snapshots
deployability only for those open episodes, runs the existing daily healing, field-care, weekly
healing, and procedure order, then emits exactly one `NearDeathRecovery` when a non-deployable
brother becomes deployable. Natural/field care, cybernetic, and vat-grown recovery are distinguished
without scanning full career histories; a missing or fallen soldier closes no fictional recovery.

`MedicalProcedure` (soldier id, hit-location template id, `MedicalProcedureType { Cybernetic, VatGrown }`, weeks remaining, Requisition cost paid up front) lives on `Army` beside the Requisition pool and roster, and persists to a `MedicalProcedure` table keyed to `Soldier`. `MedicalProcedureService.TryAssign` validates eligibility, surgery site, co-located staff, and affordability, then deducts cost and creates the procedure; `EvaluateRequisites` returns the per-requisite breakdown the UI renders green/red. Durations and costs live in `MedicalProcedureRules`, never in UI literals. The gates are a co-located Apothecary **and** Techmarine (same ship or same region, checked only at procedure start) plus a valid surgery site — aboard a ship, or an Imperial/player-controlled Hive/Forge/Civilised region. No fortress-monastery is modeled, so a player-held region serves as the de-facto base.

**Apothecary field care.** `FieldCareService` converts an Apothecary's **Medical** rating into a daily wound capacity spent on the wounded he can reach. Treatment is a **forced wound-band demotion applied the day it happens**, not a credit settled at turn end — a brother hit in a day-2 assault and treated that evening enters the day-3 battle at reduced severity, which is the whole point, since battles read live wound state. All tunables live in `FieldCareConstants`, never the rules DB.

- **Reach** is the order: every wounded soldier in its assigned squads plus its attached soldiers. This is what makes order-level attachment the right shape (§5.6).
- **Capacity** is mildly superlinear in Medical rating and clamped, so a Master of the Apothecarion outworks an ordinary brother without replacing several of them.
- **Cost is flat in band *index*, not band value.** Wound bands are powers of 16, so a proportional cost would make severe wounds untreatable; a sub-linear surcharge covers extra wounds within a band, since a demotion moves the whole band at once.
- **Triage** is worst-first and deliberately not spread thin: severity by `Wounds.RecoveryTimeLeft()` — the *player-visible* number, so the order shown is the order run — then Rank desc, Subrank desc, then a random draw from the session RNG. Re-triaged after **every** treatment, so a demotion can hand the queue to someone else mid-day.
- **Greedy, no per-soldier cap, use-it-or-lose-it.** Re-triage self-levels: once the worst case drops below the next man, the queue reorders on its own.
- **Ceiling.** `IsReplacementEligible` is true for severed non-vital locations only. Crippled locations remain eligible for natural and field healing; a brother who has actually lost a non-vital part is a surgical case.

Two seams, deliberately deduped. The mission pass runs on `MissionDayScheduler`'s scheduler-level `onDayEnd`, iterating **distinct `Order`s** — never mission elements, because `BuildMissionElements` fans one order into several single-squad drivers for `IndependentSquads` and a per-driver pass would make an Apothecary silently worth 3×. Garrison care runs the identical routine in `ChapterUpkeepProcessor.ProcessMedical` before the weekly cascade. **Field beats garrison by construction, not by rule:** an Apothecary under an order fails the "not on a mission" test defining the garrison pool, so the pools are disjoint and no man spends a day twice. Co-location resolves through `PlayerSoldier.EffectiveRegion`, since an attached Apothecary's home squad may sit on the ship while he is forward — `MedicalProcedureService.HasCoLocatedStaff` routes through it for the same reason.

Gene-seed recovery resolves once per confirmed-dead brother in `BattleTurnResolver.RemoveSoldiersKilledInBattle` (`ResolveGeneseedRecovery`), folding any recovered gland's purity into the chapter aggregate and writing a structured `SoldierEventType.GeneseedRecovery` event onto the preserved fallen-brother dossier; the battle log reads that recorded outcome rather than recomputing it. `PlayerForce` carries a count-weighted aggregate `GeneseedPurity` float alongside `GeneseedStockpile` — seeded pristine at founding, each recovered gland contributing a purity rolled around a baseline with small downward drift (`GeneseedRules`). Both persist on the extended `GlobalData` row. Stockpile drawdown happens in the recruitment pipeline (one unit consumed on Phase 0 → Phase 1; PRD §4.9).

### 6.6.2 Chapter Operational Doctrine & Duty Readiness

`Army.ChapterOperationalDoctrine` is mutable campaign state, distinct from rules-data loadout
doctrine. `ChapterOperationalDoctrine.InjuryThreshold` is nullable: null is the explicit
**Incapacitated** policy, while the other accepted values are Critical, Major, Moderate, Minor,
and Negligible. Threshold comparisons use the highest occupied wound band on any body location
and are inclusive; wounds on separate locations are never summed. New campaigns use Major,
`RequireDutyReadySquadLeader = true`, and `MinimumDutyReadySquadStrength = 5`.

`DutyReadinessService` is the single individual decision path. It returns a typed
`DutyReadinessEvaluation` with one of `CombatIncapacitation`, `UntreatedSeverance`,
`InsufficientFunctioningArms`, `ProcedureReservation`, or `ChapterInjuryThreshold` (plus
`Ready`). Physical and procedure exclusions run before the Chapter threshold, so changing the
policy cannot make a severed, one-handed, incapacitated, or reserved brother eligible. NPC
soldiers use physical combat effectiveness; player individuals use the same service without any
squad minimum or leader rule.

`SquadStrengthSnapshotBuilder` computes `Full`, `Rostered`, `Present`, `Effective` (the
combat-effective projection), and `DutyReady`, and classifies each unavailable player member once
as physical incapacity, individual posting, procedure reservation, or doctrine withholding.
`SquadReadinessService` adds the structural blockers `BelowMinimumDutyReadyStrength` and
`RequiredLeaderUnavailable`; the leader counts toward the minimum. Existing orders are not
cancelled when policy changes, but order mutation and the mission boundary both use the same
decision.

The campaign-to-battle bridge gives each player `BattleSquad` an explicit frozen participant-id
set. It is populated from duty-ready members only after squad gates pass, while individually
attached characters become one-person battle elements after their own individual check. The set
is refreshed before each mission stage or engagement. Wounds that cross the Chapter threshold
without causing physical combat incapacity therefore do not remove a current combatant, but do
exclude that identity from the next engagement. The wrapper still references the campaign squad,
so equipment pools, ammunition, order membership, casualty history, and aftermath identity are
not recreated or silently rewritten. A blocked or empty next-stage result is represented by the
typed `MissionAvailabilityStatus`, a per-squad `MissionSquadReadinessIssue` retaining the campaign
squad identity, and an explicit mission log entry.

Loadout allocation counts duty-ready carriers, while stored squad, role, and personal loadouts
remain intact when doctrine withholds a carrier. Operational fractions use `DutyReady / Full`;
combat-effective counts remain available as a secondary diagnostic. Doctrine-only transitions
do not enter the near-death/recovery event path.

### 6.7 Force Generation

`ForceGenerator.GenerateForce(ForceGenerationRequest, IRNG, IEntityIdAllocator)` dispatches by `ForceCompositionProfile`. The allocator is optional at persistent-campaign call sites; tactical missions supply a mission-local `TacticalEntityIdAllocator`, which issues negative IDs and therefore does not advance or collide with the positive campaign counters:

The irregular-strength path is opt-in through `SquadTemplateElement.RollsStrength`; `Min < Max` alone never enables it. This protects establishment formations such as Tactical Squads and chapter offices, whose minimum is an understrength floor rather than a random muster. For a rolling element, the template's Battle Value uses `ExpectedNumber` (the midpoint), while the generated force charges the budget for the actual rolled count. The roll consumes the shared tactical RNG; non-rolling elements remain exact no-ops. If a faction has no squad-template map, force generation normalizes the null map to empty and returns no force instead of dereferencing it.

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

All role lists share the `unassignedSoldierMap` as the single consumption authority, so one soldier may qualify for several roles but can only be assigned once. `ChapterGenerationDoctrine` resolves the required rules objects once by stable semantic assignment and fails fast when required data is missing or ambiguous. The detailed founding eligibility and ordering table is retained in `Design/Reference/FoundingRoleAssignment.md`.

### 6.9 Sector Generation

`SubsectorBuilder.BuildSubsectors(planets, gridDimensions)` clusters planets using a greedy merge.
`SectorGenerationProfile` supplies the sector dimensions, planet spawn probability, and maximum
subsector diameter; the shipped profile is 200×200 light years with a 2% spawn probability and a
20-light-year maximum diameter. Each grid unit represents 1×1 light year. A subsector typically
contains 2–8 star systems.

Subsectors, warp lanes, and governance designations are derived runtime structures, not rules-database entities; they are reconstructed from the saved sector and rules profile. The topology algorithm remains code-owned. Sector dimensions, density, and subsector scale are data-owned through `SectorGenerationProfile`; pixel metrics for the sector map and battle replay remain code-owned presentation settings. Independent adjacency and travel tuning remain future configuration candidates.

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

### 6.10 Campaign Operations Services

The campaign operations workflows use pure services and read models beneath Godot views. They keep
multi-entity changes staged until confirmation and revalidate against live state before commit.

**Chapter Muster.** `SquadLifecycleService` owns final-member cleanup and the distinction between
historical and disposable empty Scouts. `SquadDesignationFormatter` and `FormationOrdinalAllocator`
provide stable non-Scout line identities. `MusterPlanService` stages transfers, promotions, role changes,
and new or reconstituted formations, while `ChapterMusterViewModelBuilder` supplies candidate,
formation, roster-delta, and constraint rows. `FleetCapacityPlanService` returns typed direct-placement,
rebalance-required, or impossible results; it never splits a squad. Commit is all-or-nothing after
revalidation, and canceled provisional formations do not consume ordinals.

**Recovery Operations.** `IndividualPostingService` is the single mutation boundary for detached
personnel. `MedicalFacilityService` enumerates known treatment sites with typed Ready, Resolvable, and
Ineligible outcomes; `RecoveryOperationsViewModelBuilder` and `RecoveryPlanService` expose and commit
care destination, patient movement, staff/capacity, treatment, and reunion decisions. A medical
detachment removes the soldier from physical squad presence while retaining nominal membership. After
care completes, the physical posting remains a `Medical` posting until the player explicitly reunites
the soldier, and reunion requires co-location; the obsolete `AwaitingReunion` label is only a legacy
projection and is not persisted.

**Planetary Operations.** `RegionalOrderEligibilityService` scopes candidates to the target and adjacent
surface regions, includes squads already assigned to the selected order, and excludes orbiting or
otherwise ineligible formations. `OrderMutationService` owns typed create, assign, add, remove, and cancel
operations. `PlanetForceMovementService` performs atomic landing and whole-squad embarkation with
capacity checks and order cleanup for `MovementParty` selections containing squads and/or characters.
`AdministrativeStationService` owns duty-station seating and relocation, while `FlagshipService` owns
the unique flagship and deterministic succession. `RecruitmentStaffService` maintains the 10th Company
continuous task from physically present assigned characters. `PlanetRegionMapViewModelBuilder` and the shared
`PlanetRegionMapView` keep the sixteen-card regional topology mounted while Order, Land, Embark, and
Detach verbs re-scope the force tree and live editor. `SystemInspector` owns the world dossier.
Hostile estimates preserve confidence, age, and evidence-decay information instead of flattening the
belief ledger to one magnitude word.

### 6.11 Sector Map Label Layer

SectorMap renders the label layer in world coordinates alongside the subsector fills and
boundaries. SectorLabelBandStyle resources in Scenes/SectorMap/SectorMap.tscn own the font,
world size, zoom range, colour, outline, shadow, and letter spacing for the three bands:

- Band A (0.33-1.1): subsector names at display scale.
- Band B (1.1-3.5): priority-ordered planet names while Band A remains dimmed.
- Band C (3.5-10): close planet names.

Helpers/UI/SectorMapLabelLayout is the engine-independent placement solver. It receives measured
world-space extents and returns deterministic, non-overlapping positions. Subsector candidates
also carry their Voronoi region polygons, so the solver rejects placements whose sampled bounds
would cross the region. Planet priority is ordered by active work, request severity, governance
seat, importance, and stable id tiebreakers.

Subsector names are derived during governance assignment from the subsector's capital/governance
seat planet as "{capital name} Subsector"; Subsector {id} is only the fallback when no seat
exists. SectorMap keeps the full name on one line when it fits comfortably, and otherwise
measures and places a centered two-line form:

    Capital Name
    Subsector

The layout is rebuilt when the map is built or loaded and planet bands are refreshed after a turn.
Camera zoom only queues a redraw and cross-fades the bands. Camera2D maps the L hotkey to the
map's label visibility toggle.

OnlyWar.Tests.UI.SectorMapLabelLayoutTests covers band selection, deterministic priority,
collision rejection, map bounds, scaling, anchor fallback, and region containment. Governance
hierarchy tests cover the derived subsector name.

### 6.12 Ork Infestation

`FactionCapabilityCampaignProcessor` coordinates the Ork lifecycle independently of the founding
scenario's invader. `SectorBuilder` seeds latent sources and configured inhabited-world feral
presences after ordinary world generation; `TurnController` runs the capability-owned weekly phase
before NPC planning. Orks are one rules-data configuration of the reusable ghost, dormant-population,
invasion-generation, and mob-morale capabilities; no consumer identifies them from a display name,
hardcoded faction id, or a composite behavior test.

**Rules profile and feral state.** `FactionBehaviorRulesProfile` is a validated rules-data row
loaded by `GameRulesData`. It supplies ghost cadence, consolidation and mobilization values, landing
and successor thresholds, travel, growth, culling, initial-belief, and morale coefficients. The
capability-owned rules classes remain the calculation API; code owns equations, operation ordering,
clamps, invariants, and safe fallbacks. Feral means `RegionFaction.StrategicInvasionForceId == null`;
`IsPublic` is the open-ground state and remains false for a known feral presence. Observer-specific
`FactionIntelBelief` is independent of that state. Feral Orks do not migrate or accumulate across
regions without an active local Waaagh! attraction.

**Latent sources.** Each eligible empty sector tile receives a seeded profile chance, with a minimum
source fallback when eligible tiles exist but all rolls miss. A source stores position, a generated
non-Hive/non-Forge/non-Civilised world template, ecosystem population/capacity, and consolidation.
It is not a visible `Planet`, fleet, beacon, or travel object. Population grows through the logistic
path; consolidation advances by the profile-backed normal draw and forms a Waaagh! at its threshold.
Mobilization dispatches the profile-backed fraction, leaves the source in place, and resets source
consolidation to remaining population/capacity.

**Formation, operations, and exact identity.** A completed source lands at an existing planet.
Defended regions receive desired allocations of `2 × defending BV`; undefended regions receive the
profile's `1000 BV` token; remainder goes to the largest valid region. The same `2 × defending BV`
viability rule and largest-plausible-target ordering drive ordinary Ork strategy. Each
`StrategicInvasionForce` owns one persistent commander squad in a real saved Unit/Squad, outside
`RegionFaction.LandedSquads`. It contributes to strategic strength, is assigned to at most one
battle, joins an offensive only after defence is considered, and is included when its region is
assaulted. Orders and strategic results carry the exact Waaagh! id; capture, tactical affiliation,
leader death, successor creation, and reports refuse ambiguous first-active fallbacks. An active
Waaagh! attracts one third of each adjacent unaffiliated Ork population; transit Waaaghs attract
nothing.

**Leader loss and successors.** Strategic Warboss death uses `0.5 × loss fraction`; tactical death
uses command-squad presence and the mission killed-soldier ledger, with no second strategic roll.
Assassination is physical-location and concentration/success-quality gated. Leader death ends only
that identity and preserves indelible populations. Organized regional fragments at or above the
profile threshold create successors; smaller fragments remain leaderless and can reconsolidate.
Successors stay on the current planet when viable ground exists, otherwise use the nearest existing
planet and ordinary route time multiplied by `1.10`. Same-destination successors merge into a new
identity, lose `10%` per losing claimant, and keep the strongest Warboss. Stranded successors remain
on-map and re-evaluate.

**Morale and leader coercion.** `MobMoraleSupportEvaluator` computes only a bounded mob-side
support term from nearby mobs, casualties, routs, separation, and living command presence. Generic
HQ support/loss is authoritative and is not counted twice. Live morale and the RNG-free withdrawal
forecast call the same evaluator; realized support is recorded in `BattleEvent`. When an Ork squad
would Route and has an available leader, the next round is committed to normal melee attacks
against nearby squadmates. The Routing result is ignored completely, the leader takes no ordinary
action, casualties remain real, and commitment, attacks, and consequences are replay-visible.

**Strategic culling and presentation.** Confirmed feral intelligence creates a belief-gated
Extermination opportunity. PDF culling is strategic and consumes no tactical battle: public threats,
committed defence, and PDF survival take precedence, while outside help is considered only below the
profile's effective-PDF floor. True positives reduce population/consolidation but never delete the
indelible presence; false positives resolve as no-contact searches that consume the operation. Map
cards, dossiers, tooltips, mission availability, and command attention use the same observer belief
gate and never reveal hidden ground truth.

**Promised World.** `NewGameSettings` carries a setup-time `ScenarioFactionSelection` resolved from
the rules database's `ScenarioFactionOption` rows, with a deterministic Random choice, and persists
the resolved result in `CampaignScenario.InvaderFactionId`. The Ork opening reuses the existing
Promised-World objective, victory/lapse, and Home World reward loop, preserves a naturally rolled
Genestealer Cult, records the canonical destroyed-fleet/no-reinforcement briefing, and incorporates
local feral Orks into one opening Waaagh!. No live enemy fleet is required; feral survivors remain
indelible after victory.

**Persistence.** Format 16 added regional Ork links, latent sources, and persistent Waaaghs; format
17 added successor transit BV; format 18 adds the resolved scenario invader; format 19 adds Chapter
operational doctrine. The loader restores
source positions, command identities, affiliations, physical locations, transit state, and scenario
selection, validates references, and keeps persistent command squads out of ordinary landed-squad
collections. `SaveFormat.CurrentVersion` and `MinimumSupportedVersion` are both 19. Beacon/scope
state is intentionally absent because visible ghost worlds, fleets, and persisted threat scopes are
outside the completed feature.

### 6.13 Faction Capability Decoupling

Campaign behavior is capability-owned rather than identity-owned. `FactionBehavior` exposes the
independent `HasGhostPlanets`, `HasDormantPopulations`, `GeneratesInvasions`, and `MobMentality`
flags; `FactionCapabilities` is the shared query boundary. Consumers must not reconstruct a
faction identity from hostility, indelibility, population model, faction id, or display name.
`InvadesOnVictory` is likewise consumed directly for victory aftermath decisions.

Visibility, activity, and command affiliation are separate state axes. A regional presence uses
`IsPublic`/`IsOpenlyActive` for disclosure, `DormantConsolidation` for dormant ecosystem state, and
`StrategicInvasionForceId` for persistent command affiliation. `GhostPopulationSource` and
`StrategicInvasionForce` are generic domain models; their compatibility projections and the old
Ork-named save members exist only at the load/API boundary.

`FactionBehaviorRulesProfile` and `FactionBehaviorRulesDataAccess` own reusable numeric tuning.
`FactionCapabilityCampaignProcessor` coordinates the capability stages, with named seams for ghost
seeding, dormant-population processing, invasion generation, and strategic invasion lifecycle.
Commanders are selected from rules-data HQ roles (`SquadTypes.HQ` plus `IsSquadLeader`), not from
the display name `Warboss`. Tactical morale support is provided by `MobMentality`, and dormant
culling is gated by `HasDormantPopulations` and observer belief.

Persistence uses the generic `GhostPopulationSource`, `StrategicInvasionForce`, and canonical
regional columns. Older Ork table/column/member names remain read-compatible so existing saves can
load and round-trip into the generic state. The capability-subset regression tests, rules-data
validation tests, save/load tests, and planetary-operations presentation tests cover the contract.

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
| `chapter_muster_screen` | `ChapterMusterScreenController` | scene-backed controls | Staged promotion, transfer, formation, and capacity planning |
| `soldier_screen` | `SoldierController` | `SoldierView` | Individual marine detail |
| `squad_screen` | `SquadScreenController` | `SquadScreenView` | Squad detail |
| `planetary_operations_screen` | `PlanetaryOperationsScreenController` | `PlanetaryOperationsScreenView` | Regional order, movement, specialist, and detachment workspace |
| `apothecary_screen` | `ApothecariumScreenController` | `ApothecariumScreenView` | Wound, geneseed, and Recovery Operations management |
| `recruiter_screen` | *(controller)* | *(view)* | Training pipeline |
| `BattleReviewScreen` | `BattleReviewController` | `BattleReviewView` | Post-battle replay |
| `CommandScreen` | `CommandScreenController` | `CommandScreenView` | Live Command Brief, frozen Chapter Chronicle, and Last Turn Report access |
| `EndOfTurnDialog` | `EndOfTurnDialogController` | *(view)* | Current turn summary and latest report snapshot |
| `order_dialog` | `OrderDialogController` | — | Inline order assignment sub-dialog |

### 7.3 Navigation Model

`MainGameScreenController` maintains a `Stack<Control>` (`_previousScreenStack`). Opening a sub-screen pushes the current screen onto the stack and hides it. Closing via `CloseButton` pops and restores the previous screen. The galaxy view is the root; all other screens are overlays managed through this stack.

### 7.4 Last-Turn Report Snapshot

The end-of-turn report is both the immediate post-resolution dialog and the campaign's latest-report
action inside Command. It is not a historical browser; the chapter event chronicle remains the
separate long-term history surface. The feature exists because `EndOfTurnDialogController` is normally created
only after a turn resolves, so an unloaded campaign must be able to reconstruct the dialog from a
bounded snapshot rather than from the transient `TurnResolutionResult` or a live mission graph.

#### Snapshot contract

The pure models under `Models/Reports` are independent of Godot and campaign entities:

```text
LastTurnReportSnapshot
  ResolvedDate                 -- Date.GetTotalWeeks(), or 0 when no date was supplied
  Entries[]

LastTurnReportEntrySnapshot
  Title
  Subtitle
  Summary
  OutcomeStatus
  IsEnemyActivity
  Debrief (optional)

LastTurnDebriefSnapshot
  Title
  Subtitle
  OutcomeStatus
  OutcomeSummary
  Lines[]

LastTurnDebriefLineSnapshot
  Text
  Day (optional)
  SquadName (optional)
  BattleSummary (optional)

BattleSummarySnapshot
  PlayerDeaths
  OpposingDeaths
  PlayerIncapacitated
  Casualties[]

BattleCasualtySnapshot
  SoldierId, Name, Rank, Squad, Company, Disposition, RecoveryWeeks
```

Casualty rows contain display data only. They must not retain a `PlayerSoldier`, `BattleHistory`,
`MissionContext`, `RegionFaction`, or any other live campaign reference. `BattleHistory` is retained
by the current-session presentation path only; it is never serialized by this feature.

#### Build and runtime flow

`TurnController.ProcessTurn` resolves the week and returns one `TurnResolutionResult`. After the
result has returned, `LastTurnReportSnapshotBuilder.Build` runs the existing report-entry builders
once and returns both the persisted `LastTurnReportSnapshot` and the live
`EndOfTurnReportEntry` presentation list. This keeps immediate post-turn wording and reloaded latest-report
wording on one construction path.

On a successful resolution, `MainGameScene` gives the build to `EndOfTurnDialogController` and then
stores the snapshot on `Sector.PlayerForce.LastTurnReportSnapshot`. The assignment occurs only after
report construction succeeds. The post-turn autosave and later manual saves therefore serialize the
new report, while the protected pre-turn save—written before `ProcessTurn` mutates state—preserves the
previous report. If resolution, report construction, or save fails, the prior in-memory/database
snapshot remains available.

When a save is loaded, `GameStateDataAccess` reads the optional `LastTurnReport` row,
`SavedGameLoader` attaches it to the reconstructed `PlayerForce`, and `MainGameScene` lazily creates
the end-of-turn dialog when Last Turn Report is pressed from Command. A loaded snapshot is converted back into presentation
entries without replay history. Its debrief can show narrative lines and compact casualty details,
but it does not expose `VIEW BATTLE`. A null snapshot produces the explicit empty state
“No previous turn report is available for this save.” rather than silently doing nothing.

#### Persistence and compatibility

The save schema contains one bounded row:

```text
LastTurnReport
  Id              INTEGER PRIMARY KEY CHECK (Id = 1)
  ResolvedDate    INTEGER NOT NULL
  PayloadJson     TEXT NOT NULL
```

`LastTurnReportDataAccess` owns `System.Text.Json` serialization. It treats a missing table or missing
row as null, which is needed for a supported-version campaign-start or protected turn-1 save with no
resolved report. This null tolerance is not backward compatibility: the exact-version guard rejects
earlier saves after the format-7 schema bump. The row is written in the same transaction as all other
campaign data; no sidecar file is used, so slot copying, autosave retention, rollback, and diagnostics
remain on one persistence path.

#### Acceptance and verification

The shipped behavior is covered by the report-builder, data-access, and save/load tests. The required
invariants are:

- Last Turn Report after loading a post-turn save shows the same cards and wording as the immediate report.
- A campaign-start save shows the disabled Last Turn Report action with an intentional empty state.
- Saving before a new turn preserves the previous resolved report.
- A successful new turn replaces the previous report only after report construction succeeds.
- Reloaded debriefs retain narrative and compact casualty information but no replay button.
- Save failures and turn-resolution failures do not erase the last known snapshot.
- A current-version save with an absent report row loads with no snapshot; earlier save versions remain
  incompatible under the existing save policy.

### 7.5 Operations Workspaces

The Alpha 0.8 operations surfaces consolidate multi-step campaign actions while preserving the existing
view/controller boundary. The raster studies in `Design/Exploration/ForceCommandWorkflows/` are
composition references; runtime controls, typography, badges, and states come from the Godot theme,
shared components, and the icon atlas.

**Chapter Muster.** `ChapterMusterScreenController` presents an explicitly scoped candidate list, the
formation board, staged roster deltas, and a logistics/plan panel. Candidate rows use existing squad and
rank badges plus honor/status presentation. Empty line formations show identity and lineage but no
location. A proposed leader's destination rank and resulting title distinguish leader assignment from
ordinary membership. The screen supports direct capacity, bounded fleet rebalance, and unsatisfiable
states without applying a partial plan.

**Recovery Operations.** `RecoveryOperationsView` is hosted by the Apothecarium screen. It presents the
recovery queue, a complete injury ledger and code-drawn body map, factual squad status, eligible care
destinations, patient movement choice, dependent staff/capacity/treatment actions, and explicit reunion.
Queue sorting is by severity, recovery time, squad, or location. The plan surface exposes blockers and
revalidates after logistics detours; medical completion does not implicitly reunite the soldier.

**Planetary Operations.** `PlanetaryOperationsScreen` keeps one selected planet, selected region, and
shared sixteen-card map mounted while Order, Land, Embark, and Detach verbs re-scope its force tree and
editor. The sector-map `SystemInspector` owns the world dossier; the operations header carries live
regional, landed/orbit, and request aggregates and reopens the dossier as an overlay. Orders are edited
live: the first squad or character creates one, the last participant removal ends it, aggression and
character assignment mutate immediately, and one-step undo covers the last edit. Land and Embark retain
explicit confirmation and atomic movement validation for mixed `MovementParty` selections. Detach creates
a medical posting to a ship in orbit without moving the home squad, while Recovery Operations owns
treatment and onward care. Hostile estimates show intel-dependent precision, four-rung confidence,
evidence age, and deterministic decay. The force tree is collapsed and summarized by company, ship, or
administrative character group, supports parent selection and filtering, and retains excluded formations
with their typed reason.

These workspaces are covered primarily by pure domain/view-model tests and shallow scene-wiring smoke;
supported-resolution visual layout remains release QA. The orphaned Planet Detail and Region Detail
surfaces were removed after their remaining specialist and tree behavior was ported.

### 7.6 Force Legibility & Shared Squad Rows

Live squad strength has one source of truth: `SquadStrengthSnapshotBuilder` produces a
`SquadStrengthSnapshot` with `Full`, `Rostered`, `Present`, combat-effective `Effective`,
`DutyReady`, `Unavailable`, and `Vacancies`. `Full` is the template establishment, never below a
legacy overstrength roster; `Rostered` includes every member; `Present` excludes individual
postings; and `Unavailable` is the non-overlapping remainder classified as physical
injury/incapacitation, individual posting, procedure reservation, or doctrine withholding. Region
cards aggregate `DutyReady` and `Full`; combat-effective strength remains a secondary diagnostic.

`SquadReadinessService` derives leadership, commitment, structural readiness, context restrictions,
and typed blockers. A required leader who is absent or not duty-ready blocks a new deployment when
the Chapter policy requires one, and a formation below the configured minimum duty-ready strength
also blocks. The leader counts toward the minimum. These gates are applied both by order mutation
and by mission/battle construction; existing orders remain assigned when a later policy change
blocks them. Embark/recall, ship transfer, and Chapter Muster retain their separate movement
semantics, but any action that begins a new deployment uses the shared result. Command attention
facts use the same readiness and strength facts.

`SquadRowViewModelBuilder` and `SquadRowView` own the common two-line row: squad icon and name,
duty-ready/full strength, secondary combat-effective strength, unavailable and leadership tokens, location, commitment, context state,
selection, focus, truncation, and tooltip detail. Screen controllers supply context and actions;
they do not redefine the force vocabulary. `ProjectedSquadRowViewModel` adds Muster deltas and
future strength, while `BattleSquadRowViewModel` adds historical starting/current strength and
keeps replay rows non-actionable.

The common row is integrated into the Planetary Operations force hierarchy, Chapter browser,
Recruiter training list, Apothecarium hierarchy, Chapter Muster live formations, and Battle Review
formation leaves. Fleet transfer nodes carry the same row payload and facts into the native
drag-and-drop tree, whose native tree interaction remains the transfer surface. Recovery Operations
and Command retain their purpose-built layouts but consume the canonical snapshot for squad facts.
`HierarchyTreeItem` and fleet `TreeNode` therefore transport the shared row model without forcing
group headers, individual-soldier rows, or drag/drop containers into the squad-row component.

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

### 8.3 Hardcoded String-Based Lookups — Medium — Partially Mitigated

**Location:** `PlanetTurnProcessor`, all `IMissionStep` implementations, `NewChapterBuilder`

Skills and templates are frequently looked up by name string (e.g., `s.Name == "Stealth"`, `st.Name == "Tactical Marine"`). A rename in the database silently breaks the lookup at runtime with no compile-time warning.

**Fix direction:** Replace display-name lookups at the rules boundary with stable keys or semantic flags. Validated registries are useful consumer-facing boundaries, but they do not make display names stable identifiers; the registry itself must resolve an explicit rules-data contract and fail fast when it is missing or ambiguous.

**Update:** The initial training-profile migration moved work-experience training distributions and scout focus distributions into rules data. This reduces hardcoded skill-list coupling in `SoldierTrainingCalculator`, but does not close the broader issue. The remaining notable example is rating formulas that reference named skills.

**Update (validated skill registry):** `NamedSkillRegistry` (`Models/Soldiers/NamedSkillRegistry.cs`) resolves the base skills whose game-rule meaning is genuinely named — currently Stealth, Tactics, and Engineering (Fortification) — once at rules-DB load (`GameRulesData.Skills`), throwing a clear `InvalidOperationException` if any is missing or ambiguous. Mission execution projects Stealth and Tactics into `MissionRules`; individual steps no longer perform lookups or read global rules. Fist and Generic Melee are deliberately absent: unarmed combat derives its skill from the species-selected weapon template described below. Covered by `NamedSkillRegistryTests` and the mission execution tests.

**Update (RDB-005 resolved):** `BaseSkill.SkillKey` is now the stable identity for every rules skill;
`Name` is presentation text. The optional `SkillRoleAssignment` table lets rules data map the required
code-owned roles — Stealth, Tactics, Engineering (Fortification), Power Armor, and Teaching — to
any stable skill keys. `NamedSkillRegistry` validates the role bindings once at load and exposes the
resolved skills. Work-experience and scout training use that registry (or stable-key fallback in
isolated domain tests), so renaming a skill no longer changes behavior and replacing the skill behind
a role requires only a rules-data assignment change. Covered by the registry, training, and rules
database validation tests.

**Update (RDB-010 resolved):** Scout training choices are loaded from the rules database's
`ScoutTrainingOption` catalog. Each stable `OptionKey` selects a linked `TrainingProfile`, while
`DisplayName` and `SortOrder` are presentation data enumerated by the training screen. Balanced is
an ordinary profile-backed option whose seeded profile preserves the former equal-share behavior;
there is no code-owned Balanced branch. Squads persist the selected option key. The schema change
is an intentional alpha save break, and unknown selected keys fail explicitly during load/training
instead of becoming silent no-ops. Covered by `RulesDatabaseValidationTests`, training tests, and
the save/load round-trip tests.

**Update (RDB-013 resolved):** `PlanetTemplateEligibility` is a data-owned many-to-many catalog
that assigns planet-template IDs to stable generation contexts. The shipped contexts are
`scenario.promised_world` and `ambient.ghost_population_source`; `ScenarioBuilder` filters
promised-world candidates through the first context, and `GhostPlanetSeeder` filters ghost sources
through the second.
Runtime code no longer treats planet-template display names as eligibility rules. The rules loader
validates table presence, referenced template IDs, required context coverage, and positive
probability totals within each context. The migration seed retains the previous Hive/Forge and
Hive/Forge/Civilised exclusions as data, rather than executable name checks. Covered by
`RulesDatabaseValidationTests`.

**Update (RDB-007 resolved — chapter-generation doctrine):** `ChapterGenerationDoctrine` (`Models/ChapterGenerationDoctrine.cs`) compiles the data-owned `ChapterGenerationProfile` assignment tables into a validated runtime contract. Soldier, squad, and unit roles resolve to the concrete template IDs selected by the profile; formation rows bind member and leader roles plus their code-owned founding candidate roles; unit-order rows preserve explicit company ordering and repeated instances. `NewChapterBuilder` consumes this doctrine for all chapter template, formation, company, and ordering decisions, while retaining founding thresholds and distribution algorithms in code. The loader validates faction ownership, complete role coverage, formation slot compatibility, administrative capabilities, and the selected root graph before a campaign can start. Covered by `RulesDatabaseValidationTests`, `ChapterGenerationDoctrineTests`, and `NewChapterBuilderTests`.

**Update (validated sector-generation faction registry):** `SectorGenerationFactions` (`Models/SectorGenerationFactions.cs`) resolves the non-player factions `SectorBuilder` places from the rules database's stable role assignments. The infiltrator, invader, and insurrectionist roles are resolved once at rules-DB load (`GameRulesData.SectorFactions`), failing fast on a missing, duplicate, unknown, or behavior-incompatible assignment. Player and default faction flags are validated as exact singletons. Covered by `SectorGenerationFactionsTests` and `RulesDatabaseValidationTests`.

**Update (ambient faction registry):** `FactionCapabilities` resolves reusable behavior from
independent flags, and `GhostPlanetSeeder` uses `HasGhostPlanets` plus the generic eligibility
context for latent-world generation. The shipped Ork rules still provide the concrete soldier,
squad, and commander HQ templates; `GameRulesData` creates a code-owned runtime command template
when a capability-enabled faction has no authored command-unit template, so persistent command
squads use the ordinary Unit/Squad and save/load machinery.

**Update (species-owned unarmed defaults).** Unarmed combat is now a rules-data relationship, not a battle-side default or a player/NPC distinction. Every `Species` row has a validated `DefaultUnarmedWeaponTemplateId`; the resolved `MeleeWeaponTemplate` supplies the attack profile and its own `RelatedSkill`. Space Marines currently select template 12 (Fist), while the other shipped species select the stat-identical template 15 (Generic Melee) to preserve their existing training and balance. Nothing restricts either template to Astartes or to a faction: an ordinary-human species can select the Fist template in data. The obsolete `BattleDefaults` registry and the named Fist/Generic-Melee skill dependencies were removed. Battle planning, attack resolution, defense, and aftermath XP all use the combatant's species default. Covered by `SpeciesDefaultUnarmedWeaponTests` and battle-aftermath tests.

**Update (geneseed progenoid flag).** The geneseed-status logic in `BattleTurnResolver` checked `hl.Template.Name == "Face"` / `== "Torso"` against a soldier's own body to decide whether a killed marine's geneseed was destroyed. This is now a semantic `HitLocationTemplate.HoldsProgenoid` flag: a new rules-DB column (added by the `migrate-progenoid` command in `RulesDbTool`, set for the Face and Torso locations) is read by `HitLocationTemplateDataAccess` and mirrored on the hardcoded test-fixture body templates. `GetGeneseedStatusDescription` now tests `hl.Template.HoldsProgenoid && hl.IsSevered`. Covered by a rules-DB validation test asserting exactly the Face/Torso locations carry the flag.

**Update (chapter instance-graph lookups).** `NewChapterBuilder` resolves generated squads and units through the compiled doctrine's template identity and explicit unit order. It no longer searches the instance graph by inherited display names or infers companies from a name containing `Company`; the veteran/scout company distinction and specialist formations come from semantic profile assignments. Validated by the renamed-template rules test and exercised end-to-end by `NewChapterBuilderTests` and `SaveLoadRoundTripTests`.

**Update (rating/training skill references — fully data-driven).** The rating formulas in `SoldierTrainingCalculator.UpdateRatings` (and the award thresholds in `EvaluateSoldier`) previously indexed a by-name skill dictionary (`_skillsByName["Sword"]`, etc.) and hardcoded medal/flag tiers. Both are now data-driven (see §4.1.1 "Implemented"): the formulas and awards live in rules tables, evaluated by `RatingCalculator`; `GameRulesData` validates the definitions at load. Before RDB-005, `SoldierTrainingCalculator.RequiredSkillNames` covered the two skills training still referenced by display name (`Power Armor`, `Teaching`); those references now use validated skill roles and stable keys. Rating-formula skills continue to be validated transitively through the rating-component references. Covered by `RatingCalculatorTests`, `RatingDefinitionDataTests`, and `SoldierTrainingCalculatorValidationTests`.

**Current status:** The skill, sector-faction, chapter-generation, scout-training, and planet-
template eligibility portions of this issue are resolved. Several runtime graph lookups now use
validated registries, template identity, or data-owned semantic assignments. Remaining work is
tracked in [RulesDatabasePolicyCleanup.md](Design/Active/RulesDatabasePolicyCleanup.md).

**Long-term direction:** Introduce stable rules keys and semantic flags where appropriate, plus validated registries populated at rules-DB load time. For tunable behavior, prefer data-driven definitions over constants. Candidate migrations include mission skill requirement definitions, default battle resource definitions, and scenario planet-type definitions. The load step should assert that all required entries are present and fail fast with clear diagnostics.

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

**Location:** `Helpers/Turns/PlanetTurnProcessor.cs` (`UpdatePlanet`), `PlanetIntelligenceProcessor.cs`

Surfaced while writing the `SectorEntityLogic` / multi-turn coverage (§9.2.1 #5, #8):

- **Collection-modified-during-iteration.** Three end-of-turn loops removed elements from the very collection they were iterating: depopulated `RegionFaction`s (over `RegionFactionMap.Values`), depopulated `PlanetFaction`s (over `PlanetFactionMap.Values`), and expired special missions (over `Region.SpecialMissions`). Any actual removal threw `InvalidOperationException`, so mission expiration and faction cleanup could crash a turn. Each loop now iterates a snapshot (`.ToList()`).
- **Governor logic was dead code.** `PlanetFaction.Population` is a get-only property hardcoded to `0` and never maintained, so the end-of-turn leader update was gated behind `planetFaction.Population <= 0` — always true. Every `PlanetFaction` was therefore stripped from the map each turn and `EndOfTurnLeaderUpdate` (governor aging, request fulfilment, and **request generation**) never ran. `UpdatePlanets` now derives the faction's planet-wide population by summing its `RegionFaction.Population` across the planet's regions, restoring the governor-request feature. (Removing the vestigial `PlanetFaction.Population` property is left as a follow-up.)

Covered by `SectorEntityLogicTests` and `MultiTurnSmokeTests`.

### 8.13 Production RNG Policy

**Location:** `GameSession`, `StaticRNG`, simulation processors

Production simulation consumes the `IRNG` supplied by `GameSession`; the production composition boundary supplies the shared `StaticRNG` adapter. Random draw ordering belongs to the evolving simulation execution, not to a persisted replay contract. Reloading the same save and submitting the same orders is not guaranteed to reproduce identical outcomes, and draws in one subsystem may affect later randomized outcomes. Tests may still inject fixed or seeded implementations when deterministic test setup is useful. Persisted `CampaignIdentity` remains part of event and narrative presentation; it does not define production simulation streams.

### 8.14 PlanetTurnProcessor Breadth — RESOLVED

**Location:** `Helpers/Turns/PlanetTurnProcessor.cs`

The `TurnController` extraction succeeded, but its largest leaf still owned several independently evolving domains: organic/conversion growth and garrison drafting; Consumption growth and expansion; Conversion maneuvers; Imperial remnants and emigration; revolt/civil-stability behavior; governor aging/requests/Requisition; and intelligence/opportunity generation. The class preserved important phase ordering, but its breadth made unrelated changes collide and encouraged more cross-domain helper methods.

**Resolution:** `PlanetTurnProcessor` is now the order-defining coordinator. Demographics, Consumption behavior, Conversion behavior, regional control, and intelligence/opportunity generation live in `PlanetDemographicsProcessor`, `ConsumptionTurnProcessor`, `ConversionTurnProcessor`, `RegionControlTurnProcessor`, and `PlanetIntelligenceProcessor`. `OrganicPopulationGrowthLedger` is shared directly with recruitment, while intelligence producers share `PlanetIntelligenceProcessor` and its `TurnIntelligenceLedger`. Existing enumeration and random-draw order remain explicit in the coordinator and its phase methods.

### 8.15 Transitional Turn APIs and Dead Prototypes — Low

**Location:** `TurnController.Compatibility.cs`, focused tests/callers

The behavior-preserving controller split initially retained historical helper entry points in a compatibility partial, alongside an unused early orchestration prototype. The high-confidence dead prototypes and unused compatibility shims have now been removed; direct tests call the focused processors and services. `TurnController` retains only the three result accessors still used by its own orchestration and the scenario-resolution entry point.

The remaining result accessors can be folded into private `_lastResult` reads in a later API-tightening pass if the public surface is no longer needed. The scenario entry point now returns its notification directly, so callers do not need a notification compatibility property.

### 8.16 Battle Planning Calibration Seams — Medium

**Location:** `Helpers/Battles/EngagementPotential.cs`, `BattleSquadPlanner.cs`,
`RangedEffectivenessCurve.cs`, `BattleTurnResolver.cs`

The battle decision contract is stable and covered by the Battles suite, but several seams remain
calibration or measurement work rather than player-facing rules. The root and projected
`EngagementPotential` values are constructed through different planner states, so a real
plan/execute/re-plan test has not yet proven component-by-component telescoping across turns;
`MoraleValue` and `PursuitClosingValue` are the most important components to audit. The melee half
of bounded continuation still uses a capability proxy while the ranged half uses the removal-rate
table, `WITHDRAW_EVAL.friendly_viable_damage` records whether a damaging action occurred rather than
whether it was effective, and `TotalRangedRemovalRate` intentionally re-sums a continuously varying
range table.

The named seams `WoundProgressCreditWeight`, `ContactSeekerRangedRelevanceFraction`,
`RangedEffectivenessCurve.SaturationFraction`, and
`RangedEffectivenessCurve.NegligibleRemovalFraction` are intentionally code-level calibration
surfaces. Their values should move only from real-battle evidence or invariant-driven tests, not
from a single seeded fixture. See `Design/Reference/BattleLogic.md` for the retained derivations and
`Design/Active/RangedCombatFollowUps.md` for genuinely unshipped ranged features.

Strategic combat has the same kind of balance debt: the 1,500-BV handoff floor, weekly base
intensity, Imperial Guard quality/counterattack behavior, and future air/void-support modifiers
are deliberately not settled as player-facing rules. They belong to strategic-combat calibration,
not to the tactical battle contract.

### 8.17 Alpha 0.8 event and Command verification debt — Low

The shipped event/Command slice is covered by the domain, data, UI, and stable Godot headless
smoke tests listed below. The optional quantitative long-horizon diagnostic from the promoted
designs is not part of the ordinary suite, and no automated wall-clock threshold is claimed.
Supported-window visual layout review remains release QA rather than a unit-test contract.

Milestone threshold validation is enforced by `KillMilestoneRules` (strictly positive, unique,
increasing), and publication decisions are persisted with each event. The initial calibration list
is currently code-owned (`KillMilestoneRules.Initial`) rather than loaded from the rules database;
move it to rules data when narrative balancing becomes data-driven.

Exact-version rejection and current-format save/load behavior are covered by the data tests.
Long-horizon measurements remain follow-up evidence rather than ordinary CI gates.


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

1. **Save/load round-trip tests** — *(Implemented — `SaveLoadRoundTripTests`.)* Generates a real new-game sector via `SectorBuilder.GenerateSector`, saves it through `GameStateDataAccess.SaveData` to a temporary SQLite file, reads it back through `GetData`, and asserts high-level state survives (date, planet/character/request/ship/squad/soldier counts, total population, and the bounded latest-turn report). This also serves as the new-game smoke test (target #9 below) and is the regression guard for schema drift: any schema change not propagated to both `SaveData` and `GetData` fails here. Surfacing and fixing the provider-compatibility cluster in §8.5.1 was driven entirely by getting this test to pass.
2. **Mission save duplication regression** — *(Implemented — `MissionSaveTests`.)* Drives `PlanetDataAccess.SavePlanet` against a freshly created save schema and asserts the `Mission` table holds exactly one row for a region with one special mission, plus field round-trip and null-`DefenseType` cases. Covers §8.1.
3. **Rules DB schema and policy validation** — *(Implemented — `RulesDatabaseValidationTests`.)* The suite constructs `GameRulesData` against the shipped database and exercises fail-fast validation for required-table existence, nonempty content, positive planet probability totals, relational references, optional extension behavior, per-faction fleet prerequisites, rating/training tables, and validated registries. The remaining rules-policy work is tracked in [RulesDatabasePolicyCleanup.md](Design/Active/RulesDatabasePolicyCleanup.md).
4. **`FactionStrategyController`** — *(Implemented — `FactionStrategyControllerTests`.)* The controller already takes `(faction, sector)` and reads no `GameDataSingleton` state, so no refactor was needed; tests build `Planet`/`Region`/`RegionFaction` graphs and cover the empty-result cases (faction absent, hidden regions, no spare troops) and the development-construction path (spare troops spent on `ConstructionMission` orders).
5. **`SectorEntityLogic`** — *(Implemented — `SectorEntityLogicTests`, `SessionSimulationContextPrimitiveTests`, `GameSessionTurnControllerTests`.)* The end-of-turn domain logic lives in the `Helpers/Turns` processors and is driven through the `TurnController.ProcessTurn` orchestration facade over a compact hand-built sector (`SectorSimulationFixture`). Existing seeded tests protect turn behavior and random draw order; session/context tests cover dependency identity, per-run order isolation, null contracts, and a controller run whose date and RNG differ from `GameDataSingleton`. Domain coverage includes logistic growth, conversion growth (one default member converted per week), intelligence decay (×0.75/turn), stale special-mission expiration, and governor request generation against a public threat. Surfaced and fixed three latent bugs (see §8.12).
6. **`BattleGridManager` and `WoundResolver`** — *(Implemented — `BattleGridAndPlacementTests`, `WoundResolverTests`.)* Grid tests cover placement/occupancy/reservation conflicts, movement (free-old/occupy-new and collision), removal, nearest-enemy/distance queries, open-adjacency selection, and clone fidelity. Wound tests cover the damage-ratio severity ladder, natural-armor subtraction, wound-multiplier scaling, already-severed short-circuit, and the vital-location-death / motive-location-fall event paths.
7. **Rating formula evaluator** — *(Implemented — `RatingCalculatorTests`, `RatingDefinitionDataTests`.)* Rating formulas and award thresholds are data-driven (§4.1.1); tests assert the evaluator's aggregation/normalization structure with a fixed `IRNG`, that the migrated definitions match the documented formulas, and that award tiers fire correctly (highest-tier-only, best-skill-in-category name interpolation, history flags).
8. **Seeded multi-turn smoke test** — *(Implemented — `MultiTurnSmokeTests`.)* Builds a compact single-planet sector (`SectorSimulationFixture`) with a conversion cult, a public rival controller, a governor, and a high-intelligence region, then runs twelve `ProcessTurn` cycles under a fixed seed and asserts high-level invariants survive: planet stays populated with no negative region populations, the default faction persists, the cult steadily recruits, intelligence decays toward zero, and the governor's aid request persists.
9. **New game smoke test** — *(Implemented — `NewGameSaveTests` and the generation suite.)* Generate a new campaign from rules data and assert chapter, fleet, sector, subsector, planet, faction, and squad/save invariants without requiring the Godot UI.
10. **Godot scene-wiring smoke tests** — *(Implemented for Alpha 0.8 — `Scenes/Debug/release_scene_wiring_smoke.tscn`.)* Headlessly instantiate the main command scene and release-control overlays, verify required nodes resolve, and exercise top-level actions far enough to prove their event has a subscriber and opens the intended surface. These tests are intentionally shallow: their purpose is to catch visible-but-inert controls and broken scene paths, not duplicate controller/domain tests through Godot.
11. **Last-turn report snapshot** — *(Implemented — `LastTurnReportSnapshotBuilderTests`, `LastTurnReportDataAccessTests`, and `SaveLoadRoundTripTests`.)* Builder tests cover mission, strategic, construction, fortification, governor, recruitment, empty-report, casualty, and replay-redaction cases. Data-access tests cover missing table, missing row, and JSON round trip. Save/load coverage verifies persistence and loader attachment to `PlayerForce`; the main-scene wiring keeps automatic post-turn display and Command header access.

### 9.2.2 Alpha 0.8 event and Command coverage

Coverage for the promoted Alpha 0.8 slice includes:

- `CampaignEventSpineTests`: recorder dedupe/projection, crossed milestones, grouped Chronicle entries, founding projection/idempotence, and routine battle Chronicle policy.
- `NarrativeEventEmissionTests`: near-death projection/recovery, typed medical/mentor/gene-seed facts, payload/entity data-access round trips, and invalid source/correlation validation.
- `EndTurnPreflightTests`: shared attention-fact identity, preference-only suppression, and Command Brief retention.
- `SaveLoadRoundTripTests` plus the data-access tests: current format-19 relationship, awareness, target-belief, mission, latest-report, narrative episode, Chronicle callback/annotation, itemized equipment, squad-lineage, station, character-order, physical-posting, scenario-invader, Ork source/Waaagh!, and Chapter operational-doctrine persistence; compatibility tests verify that older formats are rejected before campaign-table loading. `EquipmentDoctrinePersistenceTests` covers complete role/personal loadouts, quantities, armor, and ready order.
- `EquipmentFoundationTests`, `RulesDatabaseValidationTests`, and the focused battle coverage: global equipment identity, requirements/capacity, kit validation, shared mission reserves, ammunition behavior, reload/recovery, initial-ready priority, effective tactical Battle Value, and carrier reassignment.
- `ChapterOperationalDoctrineTests` covers defaults, inclusive threshold boundaries, the explicit Incapacitated policy, worst-wound aggregation, physical-over-doctrine precedence, leader/minimum squad gates, individual deployment, and frozen battle participants.
- `SquadLineageTests`, `FleetCapacityPlanServiceTests`, `IndividualPostingServiceTests`, `DeploymentStorageTests`, and `FactionCapabilityStateTests`: line identity/history retention, whole-squad capacity planning, individual posting invariants, format-19 persistence, and persistent dormant-population/invasion-force state. `PlanetaryOperationsServiceTests` and the Recovery/Planetary icon tests cover mission-scoped eligibility, atomic mixed-participant land/embark behavior, and required UI registrations.
- `Scenes/Debug/release_scene_wiring_smoke.tscn` plus the stable headless main-scene smoke: shallow scene wiring.

### 9.3 Regression Risk Areas

These areas are particularly likely to produce hard-to-detect bugs as features are added:

- Changing the `WoundLevel` bitmask layout or adding new severity tiers.
- Adding new fields to `BattleSoldier` without updating both the copy constructor and `Clone()`.
- Adding new tables to the save schema without updating both `SaveData` and `GetData` in `GameStateDataAccess`.
- Changing skill or template names in the rules database without updating hardcoded string lookups or validated registries (see 8.3).
- Changing the `Wounds.WeeksToHeal` nibble-offset encoding without updating all dependent healing logic.
- Adding new data-driven rules tables without adding rules-load validation and regression tests.
- Changing the save schema without bumping the exact-version guard and covering current-format round trip plus early rejection of the preceding version.
