# OnlyWar — Technical Design Document

**Version:** Alpha 0.8

**Last Updated:** September 9, 2026

**Author:** Nathan Dilday

---

## Table of Contents

1. [Technology Stack](#1-technology-stack)
2. [Project Structure](#2-project-structure)
3. [Architectural Patterns](#3-architectural-patterns)
4. [Data Layer](#4-data-layer)
   - 4.1 [Game Rules Database](#41-game-rules-database)
   - 4.1.1 [Equipment Rules & Loadout Resolution](#411-equipment-rules--loadout-resolution)
   - 4.1.2 [Data-Driven Rule Profiles](#412-data-driven-rule-profiles)
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
8. [Current Architectural Constraints](#8-current-architectural-constraints)
9. [Testing Strategy](#9-testing-strategy)

---

## 1. Technology Stack

| Concern | Choice |
|---|---|
| Engine | Godot 4 |
| Language | C# (.NET via Godot's .NET build) |
| Game rules data | SQLite (read-only at runtime) |
| Save state data | SQLite (written on save, read on load) |
| RNG | `IRNG` boundary in `OnlyWar.Abstractions`; Runtime `RNG`, `StaticRNG`, and `SeededRNG` implementations |
| Statistical math | `GaussianCalculator` in `OnlyWar.Domain.Math` |

The simulation foundation builds with the ordinary .NET 8 SDK. Godot.NET.Sdk/4.7.0
is confined to the root `OnlyWarGodot` host project. SQLite file/catalog/format adapters,
rules/catalog readers, game-state readers and save writers are owned by `OnlyWar.Persistence`.
Persistence returns Domain data and raw relationship/event/history records; `OnlyWar.Application`
owns detached session reconstruction and publication, while `OnlyWar.Campaign` applies policy,
narrative projection and reconciliation.

---

## 2. Project Structure

Production code is split between headless .NET projects under `Modules/` and the root
`OnlyWarGodot` host. The module projects target .NET 8; the host is the only project that uses
Godot.NET.Sdk. Project references define dependency direction and compilation boundaries, while
namespace declarations define conceptual ownership. Physical subdirectories are organizational
details and are not an ownership boundary.

A project-reference arrow points from consumer to dependency. The current direction is:

- `OnlyWar.Abstractions` is the foundation. `OnlyWar.Domain` depends only on it.
- `*Abstractions` projects own cross-module ports and boundary records. They depend only on Domain,
  shared primitives, and other abstraction projects; they never depend on implementation modules.
- Runtime, Generation, Medical, Operations, Battles, and Persistence implement headless policy and
  infrastructure over Domain and their relevant boundary projects. Runtime is the shared factory,
  identity, randomness, naming, logging, and geometry infrastructure used by several feature modules.
- Campaign owns live campaign policy and turn orchestration over the feature modules. Application is
  the session-lifetime and composition root: it creates/loads/saves sessions and wires feature services
  to public screen contracts. Persistence is deliberately outside Campaign and does not depend on it.
- `OnlyWarGodot` is the outer host. It consumes the application and feature assemblies, owns Godot
  composition and presentation, and is never a dependency of headless production code.

| Project | Direct project references | Responsibility |
|---|---|---|
| `OnlyWar.Abstractions` | None | Shared `IRNG` and entity-ID allocation primitives. |
| `OnlyWar.Domain` | Abstractions | Domain entities, value objects, rules data, events, and independent simulation math. |
| `OnlyWar.Application.Abstractions` | Abstractions, Domain | Session identity and the explicitly wider simulation-session contract. |
| `OnlyWar.Battles.Abstractions` | Domain | Tactical facts/results and battle capability ports. |
| `OnlyWar.Generation.Abstractions` | Abstractions, Domain | Generation ports and training-service contracts. |
| `OnlyWar.Medical.Abstractions` | Domain | Medical and readiness facts/results. |
| `OnlyWar.Operations.Abstractions` | Abstractions, Battles.Abstractions, Domain, Medical.Abstractions | Personnel, order, mission, and outcome ports plus neutral operational records. |
| `OnlyWar.Persistence.Abstractions` | None | Provider-neutral file and derived-state reconstruction contracts. |
| `OnlyWar.Runtime.Abstractions` | Abstractions, Domain | Runtime construction boundary values. |
| `OnlyWar.Battles` | Abstractions, Battles.Abstractions, Domain, Runtime | Tactical state, planning, actions, wounds, morale, aftermath, and replay. |
| `OnlyWar.Medical` | Abstractions, Domain, Medical.Abstractions | Headless readiness, health, procedure, facility, and care policy. |
| `OnlyWar.Persistence` | Abstractions, Domain, Persistence.Abstractions | SQLite rules/catalog readers, save readers/writers, file storage, metadata, and format validation. |
| `OnlyWar.Runtime` | Abstractions, Domain, Runtime.Abstractions | Factories, names, logging, persistent/tactical IDs, randomness, and world geometry. |
| `OnlyWar.Generation` | Abstractions, Domain, Generation.Abstractions, Runtime | Initial sector/chapter/character construction and scenario stamping. |
| `OnlyWar.Operations` | Abstractions, Battles.Abstractions, Domain, Medical.Abstractions, Operations.Abstractions, Runtime | Mission sequencing, personnel/order policy, strategic combat, and operational-turn policy. |
| `OnlyWar.Campaign` | Abstractions, Application.Abstractions, Battles, Battles.Abstractions, Domain, Generation, Generation.Abstractions, Medical, Medical.Abstractions, Operations, Operations.Abstractions, Runtime | Live campaign policy, turn orchestration, narrative projection/reconciliation, and campaign-owned mutation services. No SQLite. |
| `OnlyWar.Application` | Abstractions, Application.Abstractions, Battles, Battles.Abstractions, Campaign, Domain, Generation, Generation.Abstractions, Medical, Medical.Abstractions, Operations, Operations.Abstractions, Persistence, Persistence.Abstractions, Runtime, Runtime.Abstractions | Session lifetime, detached screen queries/commands, create/load/save coordination, and composition. |
| `OnlyWarGodot` | Abstractions, Application, Application.Abstractions, Battles, Battles.Abstractions, Campaign, Domain, Generation.Abstractions, Medical, Medical.Abstractions, Operations, Operations.Abstractions, Persistence, Persistence.Abstractions, Runtime, Runtime.Abstractions | Godot scenes, host presentation, startup/path/logging wiring, and host-only adapters. |

The host excludes `Modules/**/*.cs` and both test projects from its own compilation, including
nested generated `obj` sources. Runtime owns the embedded soldier-name resources and resolves them
through the Runtime assembly. The test-only headless project references the feature assemblies
directly and does not load the Godot host.

### 2.1 Namespace ownership

The namespace family and owning project are one-to-one. New types use the namespace family of their
owning project; physical subdirectories remain organizational details.

| Namespace family | Owning project | Scope |
|---|---|---|
| `OnlyWar.Abstractions.*` | `OnlyWar.Abstractions` | Shared cross-cutting primitives. |
| `OnlyWar.Domain.*` | `OnlyWar.Domain` | Domain aggregates, value objects, events, rules data, and domain math. |
| `OnlyWar.Application.Abstractions.*` | `OnlyWar.Application.Abstractions` | Public session boundary. |
| `OnlyWar.Application.*` | `OnlyWar.Application` | Session orchestration, screen application services, detached query/command contracts, and composition adapters. |
| `OnlyWar.Battles.Abstractions.*` | `OnlyWar.Battles.Abstractions` | Tactical boundary contracts. |
| `OnlyWar.Battles.*` | `OnlyWar.Battles` | Tactical state, planning, action resolution, aftermath, and replay. |
| `OnlyWar.Campaign.*` | `OnlyWar.Campaign` | Campaign policy, turns, narrative, strategy, and campaign mutation. |
| `OnlyWar.Generation.Abstractions.*` | `OnlyWar.Generation.Abstractions` | Generation boundary contracts. |
| `OnlyWar.Generation.*` | `OnlyWar.Generation` | New-world, chapter, character, and scenario generation. |
| `OnlyWar.Medical.Abstractions.*` | `OnlyWar.Medical.Abstractions` | Medical boundary contracts. |
| `OnlyWar.Medical.*` | `OnlyWar.Medical` | Headless medical and readiness policy. |
| `OnlyWar.Operations.Abstractions.*` | `OnlyWar.Operations.Abstractions` | Operations boundary contracts and neutral records. |
| `OnlyWar.Operations.*` | `OnlyWar.Operations` | Missions, orders, personnel, strategic combat, and operational turns. |
| `OnlyWar.Persistence.Abstractions.*` | `OnlyWar.Persistence.Abstractions` | Provider-neutral persistence contracts. |
| `OnlyWar.Persistence.*` | `OnlyWar.Persistence` | SQLite, save/load, rules, files, and storage adapters. |
| `OnlyWar.Runtime.Abstractions.*` | `OnlyWar.Runtime.Abstractions` | Runtime boundary contracts. |
| `OnlyWar.Runtime.*` | `OnlyWar.Runtime` | Factories, IDs, naming, randomness, logging, and geometry. |
| `OnlyWar.Host.*`, `OnlyWar.Scenes.*`, and global host scene types | `OnlyWarGodot` | Godot presentation, composition, and scene interaction. |

### 2.2 Host, application, and domain boundaries

Domain owns state and rules. Campaign owns policy that mutates the live campaign graph and coordinates
turn phases. Feature modules own their headless capabilities: Battles resolves tactical engagements,
Medical evaluates medical transitions, Operations sequences missions/orders and operational policy, and
Generation builds initial state. Application owns session lifetime, detached screen-facing contracts,
load reconstruction, save coordination, and composition of those capabilities.

`Host/Presentation` owns display-only models, formatting, layout, palettes, icons, and the explicit
battle-replay projection adapter. `Scenes` own Godot nodes, input/events, navigation, and view updates.
They call Application contracts and may use host-approved persistence/path and rules-data composition
surfaces; gameplay decisions and live campaign traversal remain below the host boundary. The scene
assembly is intentionally explicit, but the architecture suite checks public contract shape rather
than claiming that every host source is compiler-isolated from every Domain or Persistence type.

### 2.3 Compatibility surfaces

Compatibility is local to the owner. Cross-module compatibility surfaces are limited to external
formats or boundary identity:

- Persistence keeps the exact save-format guard and save-catalog classification, maps values that older
  saves did not persist, and reads legacy rules layouts into the current Domain catalog. The legacy
  equipment and faction-rule readers are storage adapters, not live gameplay policy. Canonical event
  payload registration preserves readable legacy event/history payloads during load; old free-text
  history tables are not imported.
- Representative owner-local adapters are `SaveFormat`/`SaveGameCatalog`,
  `LegacySaveValueMappers`, `LegacyEquipmentCatalogBuilder`, and
  `LegacyFactionBehaviorRulesDataAccess`; each translates persisted or rules data into current
  structures at the Persistence boundary.
- `OnlyWar.Operations` contains `OperationsBoundaryTypeForwarders`, an assembly-level type-forwarder
  for mission debrief and outcome values
  whose current owner is `OnlyWar.Operations.Abstractions`. This preserves binary consumers without
  moving ownership back into the implementation project.
- Narrow source-compatible overloads and aliases that remain inside an owning Domain, Runtime, or
  feature type are not cross-module façades. New production composition uses explicit sessions,
  allocators, RNGs, and feature contexts.

No broad compatibility façade or parallel ownership manifest is part of the architecture. New types
must use the namespace family of their owning project.

### 2.4 Contract and friend boundaries

The abstraction projects own public cross-module ports: shared identity/RNG, session, tactical,
generation, medical, operations, persistence, and runtime boundaries. They contain no implementation
references. Application query/command contracts are the deliberate composition-root exception because
the host consumes their complete detached graph. Operations readiness diagnostics and aggregate-backed
projection implementations remain internal.

Internal access is a test-only seam. `OnlyWar.Application`, `OnlyWar.Battles`, `OnlyWar.Campaign`,
`OnlyWar.Domain`, `OnlyWar.Generation`, `OnlyWar.Operations`, `OnlyWar.Persistence`, and the Godot
host grant access only to `OnlyWar.Tests`; Medical, Runtime, and all abstraction projects grant none.
`OnlyWar.HeadlessTests` uses public contracts. There is no Campaign-to-Application friend relationship.

The project graph and namespace declarations are the architecture reference. Cross-module contracts
stay with their owning abstraction project, while Application query contracts remain at the
composition root because the host consumes their complete detached graph.

---

## 3. Architectural Patterns

### 3.1 View / Controller Separation

Headless module code has no Godot references. `GridCell` carries signed integer grid dimensions
and cells; `PlanePoint` carries single-precision geometry. The subsector algorithms retain
their enumeration, scalar arithmetic and rounding; the host SectorMap converts these values into
Godot vectors at rendering entry. `GodotHostPaths` resolves `user://` paths and configures
the save directory from the existing `GodotLogBridge` autoload before scene startup.
Headless callers configure ordinary save paths explicitly. Preferences receive a file
path and use the existing `GameLog` sink for warnings. Report/row rendering stays in the
host, including the last-turn snapshot presentation builder that calls the end-turn dialog.

Every Godot scene that has meaningful logic is split into two C# classes:

- **View** (`partial class`, inherits a Godot node type): owns all `GetNode<T>` calls, declares C# `event`s for every user interaction, and exposes methods the controller calls to update display state. The View contains no game logic.
- **Controller** (plain C# class, or `partial class` node at the scene root): subscribes to View events in `_Ready()`, reads and mutates game state, and calls View methods to reflect the result.

`DialogView` and `DialogController` are base classes providing common close-button handling and the `CloseButtonPressed` event.

### 3.2 CampaignApplication and GameSession

Campaign state is carried explicitly by `GameSession`, which implements the intentionally wide
`ICampaignSimulationSession` contract containing the loaded rules, live sector, mutable campaign
date and `IRNG` for one simulation lifetime. The common `ICampaignSession` seam carries only
session identity; feature surfaces build narrower contexts from the installed session.

`CampaignApplication` creates or reconstructs detached sessions, installs a successful session as
the active campaign, and coordinates turn advancement and saving. Its public entry point publishes
screen contracts plus narrow persistence capabilities; the live `GameSession` and complete
`CampaignServices` graph are internal composition details. Host scene paths configure that
application before creating gameplay views; no global campaign object is published. `TurnController`
requires an explicit `ICampaignSimulationSession`, so tests, Application composition and alternate
simulations can select their rules, sector, date and random stream directly.

The installed session is projected into feature contexts rather than handed to screen services:
`OperationsReadContext` and `OperationsCommandContext` isolate the Operations read/write paths,
`MedicalReadContext` resolves the Apothecarium/Recovery read facts and `MedicalCommandContext`
re-resolves and commits treatment commands, `FleetCommandContext`
owns Classis fleet lookup, route and reorganization capabilities, and `TrainingContext` owns the
10th Company recruitment facts, authored training catalog, staffing synchronization and doctrine
commands. Each context keeps its aggregate/rules handles private and exposes only internal,
feature-scoped operations; public UI values remain detached records.

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

Mission execution is modeled as a chain of `IMissionStep` objects. Each step's
`ExecuteMissionStep(MissionExecutionContext, float margin, IMissionStep resumeStep)` returns a
`MissionStepResult` describing the next step, completion, margin, and resume point. A
`MissionStepDriver` applies that result as a trampoline, allowing `MissionDayScheduler` to interleave
active missions without recursive whole-mission calls.

`MissionStepOrchestrator` is the entry point, selecting the initial step from mission type. If the squad is not already in the target region, an `InfiltrateMissionStep` is prepended regardless of mission type.

### 3.5 Battle Snapshot Cloning

`BattleSquad` is the single `ICloneable` battle type used to store battlefield snapshots for
`BattleHistory`. Its copy path creates copied `BattleSoldier` runtime state; `BattleSoldier` itself
does not implement `ICloneable`, and its underlying `ISoldier` is shared by design because replay
reads snapshot battle fields and the action log rather than an independent campaign body.

---

## 4. Data Layer

### 4.1 Game Rules Database

Read-only SQLite content is hydrated explicitly by `GameRulesLoader.Load(databasePath)`
in `OnlyWar.Persistence`. `GameRulesData(GameRulesBlob)` constructs and validates an
in-memory catalog without a path, SQL, storage reference, or current-session selection.
The loader retains schema/reference/hydrated-data checks and the catalog retains semantic
registry validation. Template references and lookup ordering are preserved. Equipment SQL
hydration belongs to `EquipmentCatalogLoader`; legacy fixture conversion belongs to
`LegacyEquipmentCatalogBuilder`. `EquipmentRulesCatalog` itself accepts dictionaries.

All SQL and rules/catalog readers in `OnlyWar.Persistence.Database` are Persistence-owned. Campaign
consumes the validated Domain catalog and does not reference SQLite or the provider package.
Contains:

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
remain campaign data in the save model.

Display names are presentation data and are never stable code identifiers. Numeric row IDs are likewise not semantic identities and must not be hardcoded in production code. Cross-table foreign keys are normal rules composition and are not considered hardcoded row dependencies. When code requires a semantic assignment, the database must provide a stable key, role, or flag, and the rules loader must validate that assignment at startup.

Required content and profile tables must exist and contain enough data for their owning system to operate. Relationship or extension tables may be empty only when they are explicitly classified as optional or structurally empty-valid. Optional tables must have defined behavior when absent or empty. The loader is the boundary where these contracts are checked and should fail fast with a clear error rather than allowing a later `First(...)` or dictionary lookup failure during play.

`RulesDatabaseSchemaValidator` checks the required table contract before hydration;
`RulesDatabaseReferenceValidator` rejects orphan relational rows; and `RulesDatabaseValidator` checks
hydrated collection availability, positive planet-probability totals, generation-faction content,
training profiles, equipment availability, and player-fleet prerequisites. Optional extension tables
are absent-valid only where their loaders provide explicit fallback behavior. `SectorGenerationProfile`
supplies the data-owned sector dimensions, planet spawn probability, and maximum subsector diameter;
the loader requires one default profile and validates its ranges. Sector-map and battle-replay pixel
metrics remain code-owned presentation settings.

The installed rules database is read-only, but the hydrated object graph is not deeply immutable.
`Faction.Units` is campaign force state populated by generation/runtime owners, and a loaded catalog
is campaign-owned rather than shared as immutable state between independent campaigns. Current saves
persist the random-algorithm version but not a rules-profile identity or content hash, so changing
rules content for an existing save remains an operational compatibility concern.

The rules loader is the single boundary for resolving stable keys, semantic flags, and validated registries into runtime objects. Consumers should use those resolved identities rather than rediscovering rules rows by display name.

### 4.1.1 Equipment Rules & Loadout Resolution

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
soldier is eligible for a personal-equipment role again. Pooled squad allocation currently consumes
compatibility `WeaponSet` rows; itemized roles use `EquipmentLoadout`. In the itemized path,
`SquadTemplateElementEquipmentRole` is the authoritative element-to-role binding; the display name
`Command Weapon` is only a legacy-fixture fallback and is not consulted by production role
resolution. `EquipmentLoadoutEditorView` is the shared complete-loadout editor used by both chapter
role doctrine and live-soldier Customize/Inherit flows.

The shipped rules database contains the itemized tables and the loader builds a globally identified
catalog from them and the compatibility weapon rows. Runtime equipment ids never collide across
ranged, melee, armor, ammunition-type, package, and kit namespaces. The catalog currently bridges
legacy pooled sets into itemized kits so personal roles use the same validator, signature, and
effective-value path as bespoke loadouts.

### 4.1.2 Data-Driven Rule Profiles

Data-driven rule profiles follow this boundary:

- Code owns the algorithm for applying a profile.
- Rules data owns which skills or attributes a role trains, and at what relative weights.
- `SoldierTemplate` records the work-experience training profile for that soldier type.
- Scout focus modes use training profiles rather than hardcoded skill lists.

Current profile and semantic-assignment examples:

- Mission skill requirements, e.g. stealth checks and tactical planning checks.
- Default battle resources, e.g. unarmed melee weapon/skill.
- Chapter-generation role bindings, e.g. Chapter Master, Scout Company, Armory, Apothecarion.
- Sector-generation faction roles, e.g. primary hidden infiltrator and invasion faction.

Rating formulas use a constrained evaluator rather than arbitrary script execution. The model stores `RatingDefinition`, `RatingComponent`, and `RatingNormalizationFactor` rows, with a small fixed set of component types such as attribute value, skill total, best skill bonus in category, and best skill total in category. This keeps formulas tunable without embedding a general-purpose expression language.

A profile belongs in the rules database when it is intended to be a mod surface. Code must not interpret arbitrary row presence as a feature contract: if a feature requires an assignment, model it explicitly with a stable key, role, or semantic flag and validate it at load time; if a table is optional, document its absent/empty behavior.

Soldier ratings, award thresholds, and consumer bindings are data-driven. The rules database stores
`RatingDefinition`, `RatingComponent`, `RatingNormalizationFactor`, and `RatingAwardTier` rows;
`RatingCalculator` evaluates the constrained component vocabulary with an injected `IRNG`, and
`SoldierTrainingCalculator` delegates rating and award updates to it. `RatingConsumerBindings` maps
stable code-owned capabilities to data-owned rating keys. The loader validates these definitions and
uses shipped defaults only for optional tables absent from an older rules database.

The set of ratings is open-ended: `SoldierEvaluation` stores a `RatingKey → value` map, persisted via the `SoldierEvaluationRating` child table (§4.3) keyed by the rating *string*, so adding, removing, or renaming an ordinary rating never touches the save schema. Gameplay code does not require the seven shipped keys by name. `RatingConsumerBindings` maps stable code-owned capabilities (melee combat, ranged combat, command, medical, technical, spiritual, and ancient service) to data-owned rating keys. `GameRulesData` loads that mapping from the optional `RatingConsumerAssignment` table, falls back to the shipped mapping for older databases, and validates only the explicitly required capability roles. Legacy convenience accessors over the seven shipped keys remain for source compatibility; production consumers use the bindings.

Award identity is likewise open-ended. `RatingAwardTier.AwardType` is retained as the serialized field name but is interpreted as an award-family key. The optional `AwardFamily` table supplies display name, sort/group metadata, and a logical `IconAssetKey`. `IconAssetRegistry` resolves that key from a core or mod manifest to either a standalone texture or an atlas region. Mod keys are namespaced (`package_id:local_key`), and unavailable art falls back to the generic award icon. `SoldierAward.Type` stores the stable family key while `SoldierAward.Name` stores the display-name snapshot. Older databases without the two optional rules tables use the shipped default consumer bindings and award catalogue.

The rating component vocabulary is deliberately closed (`AttributeValue`, `SkillTotal`,
`BestSkillBonusInCategory`, and `BestSkillTotalInCategory`) rather than an expression language.
Award families use stable keys and retain their display-name snapshots, so rules data can tune formulas
and thresholds without moving executable behavior into the database.

### 4.2 Save State Database

`OnlyWar.Persistence` owns the save schema, SQL, readers/writers, atomic file storage, metadata,
catalog/retention, and exact-version compatibility. The writer recreates the loose
`Database/SaveStructure.sql` schema on each save and commits one transaction. `GameStateDataAccess`
returns Domain objects plus raw relationship, request, event, and chronicle records; it does not
reconstruct live campaign policy. `OnlyWar.Application.Storage.SavedGameLoader` rebuilds the Campaign-owned
policy graph and invokes Operations/Campaign relationship, projection, and reconciliation services
before publishing the detached session. Persistence therefore never references Campaign or Application.

Relationship records such as `PresenceRequestRecord`, `OrderCharacterRecord`, and
`IndividualPostingRecord` preserves database relationships as raw ids and values. Application
rebuilds session state, while Operations and Campaign bind order, posting, relationship, narrative,
and chronicle projections after the domain graph exists. SQL access does not own those policies.

`SaveFormat.CurrentVersion` and `MinimumSupportedVersion` are both 19 and are written to
`GlobalData.SaveVersion`. Missing saves are opened in neither create nor write mode, and the chooser
retains compatible, incompatible, and corrupt entries with an explicit reason.

Manual slots, autosaves, and recovery points use the same atomic persistence path. The protected
pre-turn write completes before `ProcessTurn` mutates state; failure blocks turn resolution. There
is no general legacy save migrator or legacy-history import path.

Canonical campaign events and Chapter Chronicle entries are projected before save and loaded as typed
records. The event registry supports legacy payload versions without reclassifying historical events;
old free-text history tables are ignored. `LastTurnReportSnapshot` is an optional bounded display
payload and does not contain live campaign or replay objects. Full battle replay remains outside the
save snapshot.

Connections use `Microsoft.Data.Sqlite` with foreign-key enforcement enabled. `Faction` is not a save
foreign-key target because factions live in the read-only rules database and are matched by id during
load.

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

**Canonical campaign event spine.** Campaign events and Chapter Chronicle entries are the durable
source of truth for player-facing career and battle facts. `PayloadJson` is decoded through the explicit
`(CampaignEventType, PayloadVersion)` registry; entity rows retain stable ids and display-name
snapshots, and publication rows retain the classifier decision so loading does not reclassify an event.
Load rebuilds the `SoldierEvent` history projection from these tables. Legacy free-text history tables
are ignored.

The append-only event vocabulary has typed payloads and versioned readers. Legacy payload readers
remain registered, while battle payloads carry an immutable `BattleEventContextSnapshot`. The ledger
validates source-event references and maintains the derived open-near-death projection in event order.

Campaign event classification owns publication decisions for Service Record, Turn Report, Command
Brief, Battle Review, and Chapter Chronicle surfaces. Stored publication rows preserve those decisions;
chronicle prose, narrator/version metadata, variants, and callbacks are not regenerated on load.

`WorldControlEpisode` preserves contested control episodes. Stable dedupe keys prevent repeated
events, and corrections append `ChapterChronicleAnnotation` rows without rewriting the original entry.

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
contact, directed recon, governor investigation/paranoia, and scenario setup all submit transient
observations. Direct allies receive a one-pass copy of new
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

**Astartes daily healing.** `Wounds.ClearNegligibleWounds()` (`WoundTotal &= 0xfffffff0`) runs once per campaign day for species carrying `SpeciesAbilities.AcceleratedHealing`, so glancing hits from one day do not compound into a real wound while a single battle's worth still can — a battle resolves inside one day. Severed locations are skipped.

`HitLocationTemplate` defines per-location properties: `NaturalArmor`, `WoundMultiplier`, `CrippleWound` threshold, `SeverWound` threshold, `IsMotive`, nullable `HandGroupId`, `IsVital`, and `HitProbabilityMap` (a 3-element int array for short/medium/long range bands). An arm and its hand share a hand group; disabling either disables that physical hand.

#### Capability, Motive Impairment & Casualty State

"Out of the fight" is three predicates on `ISoldier`, not one:

- **`CanFight`** — hands and vitals. False if any *vital* location is crippled or severed, or no hand group functions.
- **`CanMove`** — motive only. False when the motive speed multiplier reaches zero.
- **`IsCombatEffective`** — `CanFight && CanMove`. This is the current-battle seam for planning,
  targeting, morale, and casualty handling; player-force deployment uses the separate
  `DutyReadinessService` result, which adds physical deployment requirements and Chapter doctrine.
  Nothing in production means "can shoot but need not walk".

`MotiveImpairment` computes a **speed multiplier per motive location by wound band**, and immobility is
simply the product reaching zero. Constants live in `CasualtyConstants`, not the rules DB. Below
Major 1.0, Major 0.85, Critical 0.6, and zero at Massive/crippled/severed; locations compound
**multiplicatively**. Extremities floor at 0.40 and can never fell a soldier. `BattleSoldier.GetMoveSpeed()`
multiplies `Soldier.MoveSpeed` by the resulting impairment.

Legs cripple at `Massive` and sever at `Mortal` — deliberately a band apart, so "crippled but not severed" stays reachable for the body's principal motive location, which is the state incapacitation is built on. Feet cripple at `Major`, sever at `Critical`. Thresholds live in the rules DB and are mirrored in the `Body.cs` hard-coded fallbacks; the two must not diverge.

`CasualtyState { Unharmed, Impaired, Incapacitated, Killed }` with `CasualtyStateEvaluator` classifies the outcome from the body plus one external fact (whether the body was recovered). **Power-armor biostasis:** a downed player soldier cannot die of his wounds awaiting treatment, so there is no deterioration clock, no bleed-out pass, and medical care is only ever about *speed* of recovery. Nothing new is persisted — the condition derives from wounds already in the `HitLocation` table, and a recovered brother keeps his squad, so he never trips the null-squad-means-dead path at load.

`PlayerChapterBattleAftermathPolicy` settles the external recovery fact from `BattleOutcome.SideHoldingField`: a side holding the field recovers incapacitated brothers, a lost field moves them through the dead/fallen path with geneseed loss, and a mutual disengagement or turn-cap result counts as recovered. `BattleHistory.IncapacitatedSoldierIds` is kept disjoint from `KilledSoldierIds`; mission reports and debriefs render the two casualty classes separately.

**Individual postings.** `PlayerSoldier.AssignedSquad` is the permanent organizational home. An optional
`IndividualPosting` overrides the soldier's physical location without removing him from
`Squad.Members`, preserving nominal strength, lineage, save ownership, and fallen-brother detection.
`CampaignLocation` represents exactly one ship or one landed region. The save stores only the physical
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
`PermitsIndividualDeployment` for its members. `Fixed` is reserved for genuinely immobile
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

The current rules data uses rolling strength for the Insurrectionist Mob's 4–29 insurgents.

**Insurrectionist formations.** Rules data defines an `Insurrectionist` species rather than reusing
the PDF species: attributes are centered at 10 with σ 2, MoveSpeed is centered at 5 with a
10% spread, and Generic Ranged MOS is 2.0 rather than the PDF's 3.0. The faction owns three
formations — a Scout-flagged Mob with a Ringleader and the rolling 4–29 insurgents, a two-man
Weapon Team with one pooled heavy stubber, and a one-man Firebrand HQ with a Mob bodyguard. Their
weapon sets use autoguns and omit grenades. The Firebrand gives revolt assassination missions a
valid HQ target and supplies the faction's command aura.

The strategic Battle Value remains template-level: an Insurrectionist trooper prices at the PDF
trooper anchor of 5, while the heavy-stubber carrier has a higher itemized tactical value but shares
the same intrinsic template value as his Weapon Team partner. Strategic generation and mission
accounting never persist or substitute the tactical value. Pooled compatibility crews use the
legacy `WeaponSet` approximation, while itemized roles use the resolved loadout. Omitting grenades does not change the BV of a trooper who has a
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
order assignment, organizational transfer, local support, and continuous tasks. The persisted and
runtime relationship is `AssignedCharacters`, `CurrentOrder`, and physical `IndividualPosting` state.

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

`CampaignApplication.ResolveTurn` is the host entry point for end-of-turn processing; it advances the
turn through `AdvanceTurn`/`TurnController`, builds and persists the turn report, and returns a
`TurnReportView`, while `MainGameScene.OnEndTurnButtonPressed` remains responsible for presentation.
`AdvanceTurn` stays available for tests and headless callers that want resolution without a report.
`TurnController` is a Campaign-owned orchestration coordinator: phase behavior lives in focused
processors under the Campaign turn namespaces. The session, simulation-run and feature-read contexts separate lifetime and
responsibility:

- `GameSession` is the stable dependency set for simulations belonging to one loaded game: rules, sector, mutable campaign date, and `IRNG`. `CampaignApplication` creates or reconstructs it for new/load workflows and installs it only after successful generation or load. `TurnController` requires the public `ICampaignSimulationSession` seam; Application supplies the battle and Operations ports.
- `TurnController` constructs `FactionStrategyController` with `_session.Random` and
  `_session.Rules.FactionBehaviorRules`, so campaign planning uses the session's stream and
  behavior profile. A caller that intentionally uses the process-wide stream must supply
  `StaticRNG.Instance` when composing the `GameSession`.
- `SimulationContext` is per-run state: the session, `TurnResolutionResult`, `TurnIntelligenceLedger`, separate player/all-order lists, and an optional planet scope for generation-time forward simulation.
- `OperationsReadContext` is the Operations-only feature read context installed alongside the active
  session. It provides world/order lookup, player presence/roster facts, movement locations,
  date/week and Operations policy capabilities without exposing a general-purpose `Sector` handle
  to `OperationsScreenQueries`.
- `OperationsCommandContext` is the matching write context. It owns personnel, readiness, identity,
  movement, eligibility, order-mutation and undo-support inputs, so `OperationsScreenApplication`
  assembles commands from explicit context inputs rather than from a live session or general-purpose
  sector handle.
- `MedicalReadContext` owns the Medical projections and recovery destination facts;
  `MedicalCommandContext` owns patient/treatment revalidation and delegates the atomic recovery
  write. `FleetCommandContext` owns player-fleet lookup, route calculation and
  split/merge capabilities; `TrainingContext` owns recruitment program facts, training options,
  rating bindings and staffing synchronization. None of those screen services receives a live
  session, full rules object or general-purpose sector handle.

`ProcessTurn(Sector)` returns the run's `TurnResolutionResult`; the retained sector parameter must be the same object owned by the session, preventing rules/date/RNG from one game being combined with another sector. Result collections and notifications are owned by that returned result object.

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

`SimulatePlanetForward` reuses the same planning, mission, aftermath, planet, intelligence, and diagnostic processors for generation-time world evolution, but intentionally omits date advancement, Chapter upkeep, fleet movement, other planets, and scenario resolution. Because it has no following planning pass, it sweeps the transient AI recon parties still standing after its last week, so the world hands off to the player with nothing landed on it. Patrol screens need no sweeping: they are a battle value on the `RegionFaction` rather than landed squads (§6.2).

Non-deployed non-Scout marines receive weekly work-experience training through `ApplySoldierWorkExperience`; Scout squads are routed through `TrainScouts` with each squad's selected database-defined `ScoutTrainingOption`. Scout squads assigned to missions are excluded from weekly Scout training. The option catalog includes Balanced as an ordinary option whose linked `TrainingProfile` defines its exact distribution; the selected stable option key is persisted with the squad.

### 6.2 Faction Strategy

`FactionStrategyController.GenerateFactionOrders(Faction, Sector)` is the public planning entry point.
`TurnOrderPlanner` invokes it for every faction, including the default/PDF faction, which plans on the
same terms as anyone else. A `defensiveOnly` mode still exists for a faction that should be
posture-restricted; nothing sets it (PRD §4.24).

**One marginal-value auction per planet.** The defensive reserve is not taken off the top.
`RegionForceState.SpareTroops` is a region's whole allocatable battle value, and defence competes for it
like every other kind of work. That is the core of the design: the old fixed priority ladder compared no
margins, so it could say that all of a garrison came first but never that the last fifth of one was
worth less than the first recon squad.

1. **Task list** (`ForceTaskBuilder`). Tasks are keyed by objective rather than by region, which is what
   makes shared saturation expressible — a multi-region assault is one task with one saturation that
   several regions bid into, and two regions garrisoning against the same neighbour answer one threat
   through `SharedThreatLedger`. Kinds are Defend, Withdraw, Move, Recon, Patrol, Assault, Raid,
   Construct, ConsumptionSpread and Feed.
2. **Auction** (`ForceAllocationAuction`). Bids rank on **marginal value per battle value**, never per
   bid, so a coarse region's larger bid cannot outrank a fine region's smaller one on size alone. Tasks
   bid in batches, because greedy single increments are correct only for concave curves and both the
   defence hold curve and the assault force ratio are thresholds. Re-scoring is lazy — pop the top
   pairing, re-score only it, push it back if it fell below the new top — exact for concave curves and
   approximate for the defence threshold. That approximation is a deliberate cost trade.
3. **Commit** (`ForceTaskCommitter`). Polymorphic by design: Recon, Patrol, Assault, Raid, Construct and
   Feed produce an `Order`; Defend, Withdraw, Move and ConsumptionSpread mutate strength directly.

**Saturation is read off the task's own resolver, never authored** — the point at which the resolver
stops responding to more troops. An assault's is `OverrunForceRatio` times the estimated defender behind
its entrenchment multiplier, because the resolver keeps responding past the ratio that merely carries the
region: only that much force annihilates the defence rather than leaving a remnant. Its curve is
therefore two-part — a threshold at the carry point (the force ratio, which is also `MinimumViableAward`
and the launch gate), then a linear tail to the overrun ratio worth `AnnihilationValueMultiple` of the
carry point. Value at and below the carry point is unchanged, so the tail prices only force nothing else
was bidding for. A defence's saturation is the reconciled requirement; a construction's is the remainder
of the current level band; a sweep's is the squads needed to cross the reconnaissance threshold this
turn.

**Importance is a 0..1 score scaled by a per-faction doctrine weight** (`ForceDoctrineWeights`, one row
per faction in `FactionDoctrine`). A family may normalise only against something intrinsic to itself,
never against the other candidates: `PopulationWorth` reads the region's total population against fixed
anchors, `FrontierWorth` is a flat floor while any enemy borders the region, and reconnaissance is flat
with the target's population moved to `ForceTask.TieBreakValue`. Whether importance should instead state
an expected battle-value swing is PRD §6.18.

**A task declares `MinimumViableAward`, the auction never commits below it, and the committer reads that
same field.** `TotalValue` is zero beneath it, so a bid that completes a task prices itself at the whole
jump and the completing top-up is legal however small. Several regions may fund one task; any left short
when the auction ends is refunded before the reserve sink runs. Awarding battle value a task cannot use
is the most damaging failure in this design — the region is debited, the executor produces nothing, and
the force is destroyed rather than merely misspent.

**Defence is the reserve sink.** Above saturation its marginal value falls to a small positive epsilon,
so it absorbs whatever nothing else wanted, beats idleness, and can never outbid real work. No separate
reserve task exists. The residue is applied in one pass rather than auctioned a quantum at a time,
because on that plateau every remaining pairing scores within rounding of every other.

**Movement is bidding, not a separate pass.** Reinforcement is a neighbour winning a share of a region's
Defend task, capped by `OutsideCapacity` at the shortfall so the men already in place are counted.
`Move` is force with nothing to do where it stands marching toward the fighting, valued by a potential
field: every region holding a public enemy is a source weighted by believed strength, decaying
`FrontGradientPerHop` outward, so the gradient points forward from any depth even though bids reach one
hop. `MaxMarchFractionPerTurn` keeps it a flow rather than a teleport, and `FrontStagingMultiple` bounds
how much one region can usefully hold.

**Offensives are properties of the target.** `FactionOffensiveEvaluator` enumerates Confirmed public
hostile presences adjacent to or inside the faction's ground; `CalculateOffensiveReward` does not
multiply by available force, because a target must not score higher for an accident of who is standing
beside it. All force-dependence lives in the curve. Assault and Raid on one target share a
`TaskExclusionGroup` and are priced against each other. A raid is not offered against ground the faction
already occupies, where pulling back would mean not moving. A region keeps
`OffensiveGarrisonFloor` — the minimum defensive reserve fraction — out of any offensive bid, because an
offensive physically removes its force from the staging region.

**Reconnaissance gates the offensive.** A region under `ReconIntelThreshold` awareness produces a sweep
and no assault, so scouting precedes attacking. Occupation does not substitute for awareness:
`NeedsSweep` reads awareness alone while `CanAssault` also accepts occupation, so a contested region
under the threshold is offered both — the sweep sharpens next week's estimate while the assault stays
available this week. Sweeps are sized to be actionable in one turn, because margins pool within a turn
and a sweep that falls short buys nothing against 25% weekly decay.

Collaborators own their own policy: `FactionThreatAssessment` for belief and hostility queries and
reserve sizing; `FactionDevelopmentPlanner` for construction options and cost bands;
`FactionReconPatrolPlanner` for sweep and screen issuance, recon aggression and transient-squad cleanup;
`FactionOffensiveOrderBuilder` for assault and raid issuance, strategic-versus-tactical routing and
contribution accounting; `FactionConsumptionPlanner` for spread and feed. `FactionReinforcementPlanner`
and the old candidate machinery are deleted.

The shared planning data types are owned by the strategy boundary: `RegionForceState` and
`PotentialOffensive`. The construction path is
`FactionStrategyController(IRNG, FactionBehaviorRulesProfile, IPersistentIdAllocator, doctrines)`, with a
nullable profile preserving the `DefendedLandingRatio` fallback and absent doctrine falling back to
`ForceDoctrineWeights.Balanced`. `FactionStrategyController.LogTaskRates` prints every task's importance,
saturation and rate at Trace before the auction runs; the auction prints the winning rate per award.
Calibration figures, measured behaviour and rejected alternatives are retained in
`Design/Reference/ForceAllocation.md`.

Transient AI recon parties are generated for a planning pass and cleared at the next pass by
`ClearStaleTransientSquads`; they are not persistent campaign formations.

A **patrol screen is not a force**. Patrol is to Recon what Defend is to Assault: the auction's award
is written to `RegionFaction.PatrolScreenBattleValue` as a designation out of the organized pool, and
no soldiers exist for it until something arrives that it has to fight. It is cleared and re-posted
each pass by `ClearPatrolScreens`. Two places raise troops from it, and both size what they raise
against what they are fighting rather than against the screen: `DetectedMissionStep` generates
interceptors up to the requirement it computes for the intrusion, and
`PrepareAssaultMissionStep.AssembleDefendingForce` raises the screen into a region's defence if it
saw the attack coming, drawn down by the same proportional deduction as the defensive reserve so a
multi-day assault cannot re-fight a whole screen every morning. `GetPatrolStrength` adds the screen to
the real Patrol/Recon squads so search difficulty is unchanged in meaning and scale.

The screen was a force until 2026-09-20, and it was the only defensive task that allocated soldiers at
planning time. Because the award is a fraction of `MilitaryStrength`—a population headcount for the
Imperium—one region of a turn-1 save was handed 396,030 points and built 3,960 squads of 79,200
soldiers, while `StrategicCombatRules` caps any battle at 24 squads and 120 actors. The turn did not
finish. Making the screen abstract also removed a double-count: the generated squads were conjured on
top of `MilitaryStrength` and then added to it again by `CalculateDefenderBattleValue`.

**Swarm operations.** Consumption factions share the same per-region planning budget as other
strategy policies. Spread mutates the richest eligible adjacent region directly; feed emits a
squad-less `FeedMission` that resolves in the Operations mission phase without materializing a
tactical force. Hidden-consumer fallback remains inside the Campaign turn processor for forces that
are outside the public planning surface.

**Player construction (squad-driven fortification).** The player can order a squad in its own region to build a defense (Entrenchment / Detection / Anti-Air), creating a `ConstructionMission` targeting the player's `RegionFaction`. Unlike the NPC squad-less construction (resolved at a flat `MissionSize` in `ProcessConstructionOrders`), a construction order that carries a squad is routed in `ProcessCombatMissions` to `ResolveSquadConstruction`: every able soldier contributes its `Engineering (Fortification)` skill value, the sum is divided by `EngineeringBuildDivisor` (100) and floored (minimum 1), and the result is applied via the shared `ApplyConstruction`. The order persists, so the squad accumulates defenses over successive turns. `Engineering (Fortification)` is an Intelligence-based Tech skill trained by all combat marines at low weight.

**Multi-faction regions.** A region can hold several enemy factions at once. `Region.RegionFactionMap`
and `Mission.RegionFaction` preserve that identity through planning, intelligence, tactical contact,
and presentation; selectors and strength calculations aggregate or target explicit faction entries.

- **Order targeting.** Orders carry an explicit target `RegionFaction` instead of a `FirstOrDefault` enemy. Only the two *synthesized* enemy-directed missions need a selector — **Advance** and **Diversion**. Own-region missions (construction, DefenseInDepth, Patrol, Training, LastStand) target the player's own `RegionFaction`; **Recon is region-scoped** and takes any valid anchor, because it discovers which factions are present and so must not require pre-selecting one; and the special missions (Ambush, Assassination, Sabotage, Extermination) already carry a concrete target from generation. With one eligible enemy the selector auto-fills read-only, keeping the common case one click; with two or more a pick is required. The default/PDF faction is never targetable — it remains an ally.
- **Detection** is a property of the region, not of the mission's target: an intruder is seen by whoever watches the ground it crosses. Every term of the stealth model sums over `Region.GetDetectingEnemyFactions()`, and `Region.SelectSpotter` draws the actual spotter/interceptor from that same set weighted by the same per-faction `WatchScore` that made the crossing hard, so difficulty and interceptor cannot disagree about who was looking (§6.5). One aggregated check per day, deliberately not N independent rolls.
- **Intelligence opportunities** are budgeted proportionally to deployed strength across the region's enemy factions.
- **Belief-backed opportunities** are budgeted from the player/default observer's stored estimates. Confirmed beliefs can create targeted special opportunities even when no current `RegionFaction` exists; a phantom search consumes the assigned force allocation and records negative evidence on no contact.
- **Strength display** uses the intel ladder and stored estimates (`FactionIntelBelief`) rather than awareness-based rounding of live truth. Rumor/Suspected entries show no exact numbers; Confirmed/Located entries use the estimate stored by the observer. The same belief query feeds the target-faction dropdown, the planet/region detail panes, and NPC target enumeration.

`RegionFaction.GetDeployedStrength()` (`MilitaryStrength × Organization / 100`) is the shared
fielded-strength measure used by garrison sizing, opportunity budgets, stealth, and strategy. One
mutable `RegionForceState` per region carries that budget through the auction, debited as bids are
awarded; there is no longer a fixed order of policies each consuming a residual.

**Strategic NPC combat.** NPC-only assaults cross from tactical to `StrategicCombatResolver` when `committed + defender` reaches `MassCombatBattleValueFloor` (3,000 BV), when the attacker cannot field a tactical force at all (`MinimumFullSquadRequest` of zero), or when generated forces would exceed `MaxGeneratedSquads` (24) or `MaxTacticalActors` (120). Named/player squads always remain tactical. The battle-value floor is the binding gate in practice; both size estimators divide by the faction's highest-value non-HQ template, so they return counts an order of magnitude under their caps. At the force ratio that carries a region the crossover sits at a defender of roughly 1,000 BV — the floor is deliberately set so that mop-up resolves tactically, because strategic combat clamps defender losses at 75% and therefore cannot finish a defender. A mop-up funded to the overrun ratio can cross the floor and resolve strategically instead, where the overrun rule finishes it; the two paths meet rather than leaving a gap, since the assault is now sized to annihilate at either end of it. Strategic resolution works directly in conserved BV pools: only organized BV deploys and takes ordinary battle casualties; effective strength combines committed BV, aggression, faction quality, entrenchment, and awareness-derived surprise. A Gaussian combat ratio determines bounded casualties and whether the attacker clears the 1.10 capture threshold. Past `OverrunForceRatio` (10) times the defender's battle value scaled by `EntrenchmentMultiplier`, the defence is overrun instead: it takes total casualties rather than the clamped share and the attacker wins regardless of the roll, since an annihilated defender reporting `DefenderHeld` is the contested-for-ever state the rule exists to end. Attacker casualties are unchanged. Every participating faction receives reciprocal `BattleContact` observations at Located level with estimates based on the engaged force, not planetary totals. Invaders establish a foothold on victory, raiders return survivors, and no transient tactical squads are generated. Equations and rejected alternatives are retained in `Design/Reference/BattleLogic.md`.

**Organized and disorganized military strength.** `RegionFaction.MilitaryStrength` is partitioned into persisted `OrganizedMilitaryStrength` and derived `DisorganizedMilitaryStrength`. Newly raised troops, transferred formations, and returning survivors enter organized. Ordinary engagements remove organized BV and total BV together; disruptive effects may transfer BV into the disorganized pool without killing it. Reorganization is a fixed-BV transfer back, not a percentage increase. Ambush opportunities size against total military strength and distribute casualties proportionally across both pools. After an Advance has eliminated the organized defence, each remaining operating day destroys up to `attacker BV × UndefendedAssaultDestructionMultiplier` disorganized BV (initial multiplier 1.0).

### 6.3 Sector Entity Logic

`PlanetTurnProcessor` runs after mission aftermath and Chapter/fleet upkeep. It handles:

**Population & carrying-capacity scales.** Population is a raw headcount. Per-type population and carrying capacity are each described by a `LogNormalValueTemplate { Floor, Scale }`: a roll is `Floor + 10^z · Scale` (z standard-normal), so `Floor` is a hard minimum, `Scale` is the median of the variable part, and the distribution is right-skewed (mean ≈ `Floor + 3.77·Scale`). These are distinct from the normal `NormalizedValueTemplate { BaseValue, StandardDeviation }` used for Importance. The rules-DB columns are `PopulationFloor`/`PopulationScale` and `CarryingCapacityFloor`/`CarryingCapacityScale`. Values are canon-grounded per world type (Hive ~80B typical down to Death ~310K), with carrying capacity = population scale × a per-type headroom (Hive 1.3 … Feral 5.0). Because hive/forge populations reach billions, `Garrison` and `PlanetaryDefenseForces` are `long`.

**Population Growth (per region, per faction):**
- Carrying capacity is a per-region value (`Region.CarryingCapacity`). It is an absolute, per-type quantity rolled at sector generation from `PlanetTemplate.CarryingCapacityRange`, distributed across the planet's regions by the same power law used for population, and persisted in the save's `Region` table. Starting population is seeded as a fraction of each region's capacity, so no region begins above capacity.
- `Logistic` and baseline (`None`) growth are scaled by a logistic crowding factor: `newPop = factionPop × growthRate × (1 − regionPop / carryingCapacity)`, where `regionPop` is the region's combined population across all factions. The factor is near-maximal when the region is sparse, zero at capacity, and gently negative above capacity (so an overfull region drifts back toward capacity). A carrying capacity of 0 is treated as uncapped (legacy behavior).
- `growthRate` is the maximum (uncrowded) rate: `LogisticGrowthRate = 0.0006`, `BaselineGrowthRate = 0.0004`. These are tuned so a world at a typical fill (~50–75% of capacity) still roughly doubles per century, matching the canon "population doubles every ~100 Terran years" — not just ultra-underpopulated worlds.
- `Conversion` growth: one default-faction member is converted per week. At population > 100, additional 0.2%/week organic growth. The garrison-to-population ratio determines whether a garrison member is also converted. (Conversion is not subject to the carrying-capacity factor.)
- `Unrest` is reversible civilian allegiance rather than organic growth. Internal per-region Contentment drifts 3% of the gap toward a tax/governor/security/crowding target; the Insurrectionist population closes toward a maximum 30% target share, recruits PDF at 0.7 weight, arms a separate civilian cadre pool, and concentrates one adjacency step at 5% per week. A revolt becomes public at 2:1 rebel-to-loyal strength and hides again below 0.5:1. Hidden embedded PDF remains in the nominal player-facing PDF roster but is excluded from loyal strength. Capital control and contextual human/xenos truces use this same public-state model.

**Going Public:**
- If a hidden faction's population exceeds the configured threshold, `IsPublic` is set to `true`, making it visible and triggering conflict resolution in subsequent turns.

**Intelligence pipeline.** Each planet's intelligence pass first decays sparse `RegionAwareness` and target beliefs, then applies listening-post awareness and accumulated recon evidence. It next generates target observations from public activity, successful patrol contact, directed recon, and battle contact, applies them in stable faction/region/target order, and fans each new report once to currently Allied observers. Positive observations blend supplied estimates; negative observations affect only the named target. The ledger exposes counters for materialized awareness rows, belief rows, observations applied, and Allied copies.

Recon and patrol have distinct products: recon always changes target-agnostic regional awareness and directed recon also submits target evidence; a player patrol is an active search whose successful sweep submits `PatrolContact`, while a generated NPC patrol is a standing screen that participates in interception and is skipped by mission execution. A listening post raises awareness and improves the quality of later observations rather than creating a belief by itself. Public activity gives observers with planetary presence or existing awareness at least Confirmed evidence. Strategic and tactical encounters record contact from the forces that actually participated. Governor Investigation and Paranoia use the same observation boundary, and scenario setup may seed explicit beliefs.

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

Stealth difficulty scales with how hard enemies are *looking*, not with how many of them *live* in the region. The check uses regional awareness, patrol/search activity, ambient search, and observer quality; population is not a proxy for attention. Mass is not itself a detection factor.

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
| `AmbientSearchCap` | 1.5 | Ceiling on the standing-presence term so it remains a bounded part of detection difficulty. |

`GetPatrolStrength()` counts squads on `Patrol` or `Recon` orders only — the two orders whose whole content is "cover ground and report what you find". Every other mission type counts as static: a squad fortifying, assaulting, or holding an objective is an obstacle in the region, not a sweep of it.

**Units.** `GetPatrolStrength()` and `GetDeployedStrength()` are both **battle value**, not headcount,
because they are subtracted from each other. `Garrison`/`Population` are BV pools, and the
faction-strategy controller seeds `SpareTroops` from `GetDeployedStrength()` then decrements it by
`SquadBattleValue`. A patrol's value reflects equipment and training as well as the number of
participants.

**Logarithmic inputs.** Every difficulty term uses `log10(1 + x)`, so zero maps to exactly 0 and an empty region has difficulty 0. `MissionStealthDifficulty.TroopMagnitude` is the sole exception: it uses `log10(max(1, x))` **only** for order-of-magnitude mission-size banding in `PlanetTurnProcessor` (where 1,000,000 must band to exactly 6), never for difficulty.

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

`BattleSquadPlanner` is the battle-planning composition entry point, including ambush aim seeding and
the established planning entry points. Its policy, builders, and targeting/movement collaborators live for
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
package has no reserve. `AmmunitionType` is a caliber, not a per-weapon identity: every boltgun and
bolt pistol fires Bolt rounds, lasguns and laspistols share Las charges, and so on. Legacy
`RangedWeaponTemplate` rows name their caliber in `AmmunitionTypeId`, and the loader rejects a
magazine weapon without one, because it would otherwise reload forever from an uncounted reserve. Magazine weapons reload from that pool, incremental weapons load partial
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
mission sizing, and persistent casualty accounting. Pooled `WeaponSet` compatibility allocation uses
the intrinsic strategic value; itemized personal carriers use effective value immediately.

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
- **Take-out probability is the per-hit quantity.** Ranged, cone, blast, melee, and friendly-fire
  scoring use `CalculateTakeOutProbabilityOnHit`, which follows the resolver's hit-location, armor,
  wound-threshold, motive/vital, and functioning-hand rules over the real damage distribution. It
  reads live wound state, so concentrated and finishing damage are valued naturally.
- **Burst size (`RangedShotEvaluator.ChooseShotsToFire`, 2026-09-24)** picks the round count with
  the best net value: the burst's expected removal (`RemovalMath.ExpectedBurstRemovalFraction`,
  which models the single roll and the recoil loop), less friendly strays, less
  `scarcity × value per round` for each round spent. It climbs from the minimum and stops at the
  first round that does not pay for itself. Free rounds (scarcity 0) fire the full rate; a
  standard-issue boltgun at short range fires about 3; on the last magazine a burst shrinks to its
  most round-efficient length. This replaced a rule that fired to 75% take-out confidence while
  treating each round as an independent kill chance, which fired 6–9 round bursts whose later
  rounds almost never hit.
- **Aim, ammunition, and lanes (`RangedTargetSelector`, 2026-09-22).** Aim versus shoot compares
  value per turn: firing now against the prepared shot's value over the turns it takes, with the
  target's range projected along its direction of travel (`EvaluateFireTiming`,
  `ProjectedRangeChangePerTurn`), so aiming at a quarry running away rarely pays. A planned or stored
  aim is priced in the engagement potential at that same rate (`FireTiming.Readiness` /
  `StoredReadiness`), replacing a flat 5% of shooter battle value. Every shot is charged for its
  rounds: net value = score − scarcity × (best score per round on offer) × rounds, where scarcity
  rises from 0 at `PlentifulMagazines` (6) magazines' worth to 1 on an empty weapon. Lane spread is
  framed per `(shooter squad, target squad)` with both frontages normalized, and its penalty is a
  fraction (`BaseLaneSpreadFraction` × `FireDiscipline`) of the shot's own score.
- Shooting into a melee scrum applies `RangedFriendlyFireRules.FiringIntoMeleePenalty` in both planning and resolution. A miss inside the narrow near-miss band may strike another scrum participant, selected by footprint-size weight. `ShootAction` records the actual victim and whether the result was friendly fire so aftermath and replay do not credit or narrate the nominal target incorrectly.
- An engaged soldier compares his planned melee sequence against a point-blank ranged action in the same BV currency. The ranged option pays the firing-into-melee and weapon-`Bulk` penalties plus the expected self-BV cost of giving up parry against adjacent attackers. This whole decision lives in one place, `MeleeActionBuilder.AddMeleeActionsToBag`, and every path that puts a soldier into melee routes through it — an in-melee squad, a withdrawing squad that stands and fights, a routing squad pinned in contact. A second builder that constructed strikes directly bypassed the point-blank, gun-and-blade and multi-squad-adjacency rules and silently removed them from the game; the invariant is that nothing else may build a `MeleeAttackAction` for a soldier in contact.
- **Attacks resolve from turn-start geometry, ahead of movement.** Ranged fire, then melee, then movement, with every squad planning from the same frozen grid. Melee adjacency is therefore an ordinary turn-start fact like range, and no attack needs re-planning after movement. Movement keeps two passes: ordinary moves, then closing moves resolved against real post-move positions, because a pursuer aimed at a stale cell undershoots by the quarry's move every turn. The closing pass builds no attacks — it marks a soldier who reached contact, and `BattleSoldier.ChargedIntoContactLastTurn` carries the charge across the turn boundary so the next attack phase applies the moved-attack penalty and parry forfeit. `MeleeStrikeEstimator.EstimateChargeNet` consequently runs two clocks: the payoff discounts by the full turns-to-contact, while run-in exposure keeps the old minus-one because the crossing itself is unchanged.
- **Rejected turn orders, with the reason each fails.** Melee is the only action whose legality is created by movement inside the same turn, so the phase order is really one question: may this turn's movement create contact this turn's attack can use? *Move, then attack* deletes the melee deferral and grows a ranged one — movement must still be scored against a projected attack, the failure recorded above as a jog scored for a shot while action construction chose an illegal `Aim` — and it makes incoming-fire estimation depend on predicting enemy movement. *All at once, a squad at a time* requires an ordering rule, because one squad's resolution would feed the next squad's decision; id order is arbitrary and exploitable, random order adds variance to a deliberately deterministic layer, and initiative was rejected on design grounds. It is the only candidate that costs simultaneity. *Leading the target* — pursuers predict the quarry's move and intercept, preserving single-pass movement — replaces observation with prediction and lands the pursuer on empty ground when wrong. *Widening `MeleeContactAllowance`* so arrival need not be exact requires an allowance at least as large as the quarry's move, which makes contact a blob and disengagement nearly impossible.
- General line-of-fire tracing through friendly formations is not implemented; the scrum distribution is intentionally reusable when terrain and fire lanes are introduced.

**Template weapons:**

`RangedWeaponTemplate.TemplateType` selects normal fire (`0`), a cone (`1`), launched blast (`2`), or thrown blast (`3`). `AreaRadius` carries cone half-width or blast radius. Template attacks pre-resolve geometry, victims, scatter, and wounds once; replay reuses the stored result rather than consuming new randomness.

- `ConeTemplate` projects the weapon's full-range cone along the shooter-to-target direction. A combatant is caught when any occupied footprint cell lies inside it. `AreaAttackAction` auto-hits every caught friend or foe except the shooter, applies normal armor/hit-location/wound resolution per victim, and consumes one ammo per burst. The planner scores the entire firing line and never aims a cone weapon.
- `BlastTemplate` resolves an aim cell, then converts a failed normal-curve skill check into margin-proportional scatter in a pre-resolved random direction. A combatant is caught when any footprint cell lies inside `AreaRadius`; the thrower is not excluded. `BlastAttackAction` scales damage quadratically from full at the impact center to zero at the rim before armor.
- Thrown blast range is `Strength × MaximumRange`; launched blasts use `MaximumRange` directly.
  `WeaponSet.GrenadeWeapon` remains a pooled-compatibility slot, while itemized kits represent
  grenades as finite `ConsumableItem` equipment instances.
- The planner scores a throw as expected enemy BV removed minus expected friendly/self BV lost, integrating
  the delivery scatter and per-victim damage distributions through `BlastThrowEvaluator`. A grenade must
  beat the soldier's best conventional action; ties prefer the gun, and melee-engaged soldiers do not
  throw. Reload and movement use the ordinary weapon/readiness paths.
- `BattleValueCalculator` values cones and blasts through density-scaled expected victims, ammo/reload duty cycle, template reach, blast falloff, and the same reference-threat panel used for conventional weapons. A grenade is valued as a sidearm (`max(primary, grenade)`), matching the planner's mutually exclusive throw-or-shoot choice.
- Template and ranged evaluation use the same deterministic evaluators for planning, resolution,
  replay, and Battle Value calculation; balance tuning is outside the module-boundary architecture.

**Melee resolution.** Attacks per melee action use `AttackSpeed` and each weapon's
`AttackSpeedMultiplier`; off-hand strikes and defense use the equipped weapons' profiles and
`ParryModifier`s. `BattleSquadPlanner.BuildStrikePlan` distributes strikes across adjacent enemies,
committing to one target until cumulative take-out confidence reaches 75% before moving on.

The contested melee roll is calibrated to tabletop's intuition band rather than to raw skill differences: `MeleeDefenderAdvantage = 0` (equal skill trades at ~50%, tabletop's "hit on 4s") and a per-side roll σ of `6`, making each skill point worth ~5.6% near parity and compressing large gaps toward tabletop's clamped 33–67% ladder — a Genestealer runs ~72% out / ~28% back against a marine. `StrengthMultiplier` values are doubled against dialed-down heavy-tier `WoundMultiplier`s; the deliberate balance stance is that base marines are two-wound soldiers.

**Battle Value derivation.** `BattleValueCalculator` is an engine-faithful valuation, not a stat-line heuristic: it replays the real to-hit/damage math (recoil decay, aim-vs-fire arbitrage, single-target overkill caps, ammo duty cycle, melee closing and engagement limits) against a four-profile reference threat panel — swarm chaff, light infantry, elite infantry, monster — to derive expected kills per turn and survival turns, then computes `BV = 5 · √(offense × durability) · command`. `SoldierTemplate.BattleValue` rows are generated from it (PDF Trooper 5 as the anchor; current strategic anchors include Tactical Marine 9, Genestealer 13, and Melee Carnifex 30) and the `StrategicCombatRules` BV anchors track those values. An offline `Compute-BattleValue.ps1` harness reproduces the calculator to 6-decimal parity for bulk regeneration. **Player-soldier BV intentionally remains the template guideline rather than a live skill-tracking value** — enemy forces size their responses by estimating the player force, not by reading concrete data on every marine.

**Squad placers:** `AmbushPlacer` and `AnnihilationPlacer` handle initial squad placement for their respective engagement types. Starting range is modified by `marginOfSuccess` from the preceding stealth check.

`Species.MeleeEvasion` participates in the contested melee roll and `Species.RangedEvasion` is a flat penalty in shooting and planner distance estimates. `SpeciesAbilities.Burrow` moves eligible squads adjacent to the nearest enemy after ordinary placement, preserving valid footprints and letting ambushers erupt into melee; the same capability permits immediate tactical disengagement during withdrawal.

**Squad engagement planning.** Movement posture is selected once per squad; weapon, target, grenade, aim/reload, point-blank, and melee choices remain per soldier. `BattleEngagementFrameBuilder` derives current-capability profiles and builds both force frames from the same frozen state: pairwise geometry and allocation weights, a primary counterpart and deterministic baseline posture, withdrawal/pursuit masks, and capacity-limited screening assignments. Profiles use the able roster, functioning grips, currently usable weapons, ammo/readiness, movement and footprint/contact capacity. `SoldierTemplate.MeleeFraction` is the canonical doctrinal input generated beside BV, but a lost heavy weapon or exhausted/disabled loadout changes the runtime role immediately.

`BattleSquadPlanner` scores the legal semantic options (`Hold`, walk back/forward, jog toward, close-to-contact, pursuit run, and assigned interpose) directly in raw Battle Value: immediate outgoing minus friendly fire plus readiness, minus allocated option-dependent incoming, plus contact melee, plus the change in state potential `Φ(s′) − Φ(s)` (which carries screening value), minus contact/whole-squad-lock cost. Current outgoing calls the same deterministic ranged/template/blast evaluators Layer 3 uses and caps combined shooters at each target's remaining BV. Current incoming is allocated across likely targets and includes the candidate's feasible declared speed. Semantic hysteresis is an **indifference rule, not additive BV**: candidates within a small fraction of the best score (`EngagementIndifferenceFraction`, applied to that best score, with a floor that scales with the squad's engagement horizon — see below) prefer the previous turn's option kind, so an incumbent posture cannot buy its way past a materially better plan. **The band was scaled by squad Battle Value until 2026-09-20**, which is the wrong dimension — Battle Value is a stock and these scores are per-turn rates — so a squad whose shooting was worth less than that fraction of its own worth per turn could never escape the band, and the tie-break silently replaced the score. A 191-value marine squad carried a 3.82 band while its whole shooting was worth 1.3 a turn, and ran for seven hundred turns on stickiness alone over a `Hold` scoring 3.81 higher. That violated the invariant in `Design/Reference/BattleLogic.md` §5.1, which has always said a previous posture cannot win when another legal option is materially better. Destinations never participate in that identity, since an interpose point and a quarry position move every turn.

**Engagement horizon.** The state potential `Φ` (`EngagementPotential`) integrates exchange rates over an expected number of exchange turns, `T`. `SquadEngagementPolicy.EnsureEngagementHorizon` derives it once per planning turn from the frozen state, stores it per squad in `BattlePlanningContext`, and every candidate in that squad's decision reads the same value. An engagement ends for a squad at its **first withdrawal point**, not at annihilation, so `T` is the shorter of two clocks (`EngagementHorizonModel.DeriveSquadExchangeTurns`), clamped to 1–183 turns:

- **Enemy clock:** the enemy force's Battle Value above its withdrawal threshold, divided by this side's summed removal rate against it. The planner does not read the enemy's orders; every enemy squad is assumed **Attritional** (withdraws below 25% of its starting able strength).
- **Own clock:** this squad's Battle Value above its own withdrawal threshold (its orders' aggression, the same rule as `BattleSquad.ShouldContinueMission`, counted in whole soldiers including the loss that crosses it), divided by the fire landing on it. Each enemy squad's rate against it is weighted by that rate's share of the enemy squad's total, so an enemy is not counted as firing at every target at once.

A clock whose budget is already spent gives the one-turn minimum; a zero rate leaves that clock at the cap. The cap, 183, bounds extrapolation and is not a measured battle length. Until 2026-09-23 `T` was one side-wide value — the whole enemy force's Battle Value over the side's removal rate — which ignored how fast the squad itself was being worn down: a two-man autocannon team facing scouts planned on 83 turns, and a Neophyte squad the scouts would drive to withdrawal in ~50 turns planned on the 183 cap, where its own incoming fire saturated its pool and the danger of closing on the scouts cost it almost nothing.

`T` is not a common factor that cancels in the argmax. Immediate exchange and one-shot readiness do not scale with it, so it sets the exchange rate between a lasting positional gain and a single prepared shot. For the same reason the **indifference floor scales with it**: `0.1 × T / 183`, so the floor is unchanged at the cap and shrinks in proportion to a shorter fight; the relative term (`EngagementIndifferenceFraction` of the best score) is unchanged. `BattleSquadPlanner.EngagementHorizonDiagnosticsFor` exposes both clocks, their budgets and rates for tests and traces; nothing plans from it. Formula detail is in `Design/Reference/BattleLogic.md` §5.2.

**The scored unit is an executable root-turn action policy, not a movement enum.** Evaluating a posture and then independently reconstructing soldier actions is not planning parity: it let scoring award a jog the value of a theoretical shot while action construction independently preferred `Aim`, which is illegal at a jog, so the soldier did nothing. Each candidate therefore carries an ordered, immutable list of `PlannedSoldierAction` descriptors (`Shoot`, `Aim`, `Reload`, `Ready`, `AreaAttack`, `BlastAttack`, `None`) recording soldier, target, weapon, range, shot count, Bulk/Aim multipliers, and expected BV terms. Selection sees only actions legal under that candidate's tier, and the winning descriptors are materialized after the declaration barrier without a second tactical vote — execution may reject a structurally invalid target but may not substitute a different preference. Proposals are ordered by soldier id so the target-cap reduction is float-stable. Movement and melee remain candidate-level branches.

Incoming fire uses no invented defensive-speed multiplier. For each plausible `(attacker squad, target squad, declared target speed, attacker Bulk)` tuple the planner runs a bounded set of real shooters, loaded weapons, and nearest viable targets through the same hit/range estimator as live shooting, feeding the candidate speed to `CalculateRangeModifier`; geometry stays pre-movement because shooting precedes movement. Results are memoized in the per-turn planning context. Pair-allocation weights still approximate which squad receives a volley, and template/blast response plus exact enemy target-policy best response remain bounded approximations.

The future is valued by a **state potential, not a rollout**. Each candidate scores `immediate exchange + Φ(s′) − Φ(s) − contact commitment` (`EngagementPotential.ScoreTransition`), where `s` is the frozen turn-start state and `s′` the candidate's projected end-of-turn state: its feasible centroid, its root action descriptors, and one interval of the primary counterpart's projected movement. `Φ` takes no option kind, so the root value is identical for every candidate and each term telescopes across turns. Its components are the net-rate exchange value (outgoing and incoming opportunity over the engagement horizon, each saturated against its finite Battle Value pool), stored readiness (a held or prepared aim is worth the shot it prepares), screen role and pursuit contact progress, the pursuit fire window, morale, command aura, and contribution access (a continuous tempo cost for time spent unable to contribute, zero when the destination cannot produce removal either, so a squad with no usable offense earns nothing for merely closing). `EngagementPotentialDiscount` is 1: `Φ` is already a value over the exchange horizon, and the 0.65 rollout discount would count the same time preference twice. The root turn stays exact and executable. `Φ`'s exchange and access terms read cached capability profiles and pair removal rates; its readiness and fire-window terms evaluate the shooters' actual held or prepared shots through the memoized ranged evaluators. Derivations, including the defects each term's form corrects, are in `Design/Reference/BattleLogic.md` §5.2. The earlier bounded two-node policy rollout (`EngagementExchangeModel.EvaluateBestContinuation`) has been removed.

**Pursuit fire-window policy.** The Xibarrus pursuit review established three linked rules. First, ranged pursuit's Run term is continuous across the preferred band, so there is a reason to run outside the band and no one-yard score cliff at its edge. Contact progress is now a saturating position value, `attainable * span / (span + excess beyond the desired range)`, worth `attainable` across the whole approach rather than every turn. Second, immediate attacks use turn-start geometry because attacks resolve before movement. Candidate state values project both the pursuer's feasible endpoint and one interval of quarry withdrawal; hold therefore opens the range while an advance changes it by net movement. A Pursuit `Hold` candidate receives a separate `FireWindowValue`: aim, ready, and reload project only the remaining withdrawal intervals before the prepared shot, so target motion is applied once per interval. The value and preparation readiness are zero when no worthwhile shot survives. `ENGAGE_EVAL` exposes this term as `fire_window`.

Third, pursuit Hold has fire-cycle hysteresis. If the squad chose `Hold` last turn and any member still has a viable `Aim` on the current primary quarry, the legal option set is `[Hold]`; movement would clear the invested aim and restart the cycle. The commitment releases when `ShootAction` fires and clears the aim, or when the existing aim-viability checks reject the target because it died, left weapon range, became a bad shot, or an emergency closer invalidated the commitment. This is state-based rather than a second independent timer, so the squad cannot alternate Run/Hold while an actual aimed shot is maturing. These rules are pursuit-only and do not weaken the separate `Standoff` invariant: standoff exists only without a meaningful speed advantage and therefore only holds and fires.

A cloned grid acts as the local reservation overlay, so blocked destinations and large footprints receive only their real displacement/contact capacity without mutating live occupancy. Charge is the payoff step of the closing option: only contact-reaching members create current melee value, closing fire and forfeited defense are charged along the approach, and one member making contact prices the existing whole-squad melee lock. Future samples use capability groups and pair weights only; they never call per-soldier target selection. The candidate controls the first transition; later steps close to the preferred band, hold inside it, or back away below it, clamped at contact/band boundaries.

Planning runs as five staged passes with one bounded parallel level: build the paired force frame and role constraints serially from the frozen turn state; evaluate `(side, squad)` jobs concurrently, including each squad's candidate action policies; store results by stable job index; declare chosen postures serially in side/squad order; then materialize planned root actions and movement reservations serially in side/squad/soldier order. Workers consume no RNG and mutate no soldier, grid, reservation, action bag, or live diagnostic stream, so concurrent memo misses may duplicate work benignly while degree-1 and parallel runs still produce identical options, descriptors, and actions. Current shooting therefore observes both sides' declared feasible speeds regardless of planning order. Routing and force-assigned bounds bypass scoring. Force withdrawal heading and cover/rear-guard assignment remain force decisions, while pursuit posture (Follow, Press, Standoff, or break off) is decided per pursuing squad and the side ends the pursuit only when every squad breaks off; these roles mask the squad option set, and pursuit runs through the same masked candidate search rather than a separate posture/action API. A normal squad may step back for spacing but cannot independently run away — leaving the fight belongs to routing or force withdrawal. `ENGAGE_EVAL` emits one row per candidate with its BV term breakdown, chosen option, and margin over the runner-up; `SCREEN_EVAL` records screen capacity and counterfactual loss. Cross-turn semantic state (`LastEngagementOptionKind`, `LastScreenThreatSquadId`, `LastProtectedSquadId`) is cloned in battle snapshots, but absolute destinations are never retained.

The engagement planner selects squad posture once, then materializes legal per-soldier actions from the
selected candidate. Withdrawal and pursuit roles mask that same option search; force-level geometry,
reservations, and action construction remain separate stages, with one bounded `(side, squad)` parallel
evaluation pass and serial declaration/materialization for stable state and RNG behavior.

**Engagement range primitives.** `RangedEffectivenessCurve` is the shared engagement-range model:
expected battle value removed per turn as a smooth function of range, in the same
`hit × (takeOut + λ · woundProgress) × BV` currency as immediate fire. Two questions are asked of it:

- `BattleModifiersUtil.CalculateOptimalDistance` — the outer edge of a soldier's **useful firing range**, the largest range where it is still at least `RangedEffectivenessCurve.SaturationFraction` (**0.2** as of 2026-09-21, not the 0.5 this line asserted for months) as effective as at its best. It remains a calibrated range primitive for force-level reach gates and the explicitly approximate rear-guard forecast; follow-shot and escape opportunity estimates do not use it as executable-shot evidence.
- `BattleEngagementFrameBuilder.CalculatePreferredOpeningRange` (via `BattleSquad.GetPreferredOpeningRange` and `IEngagementResolver.GetPreferredOpeningRange`) — where a squad wants an engagement to **open**, averaged per side by `MissionOpeningRange` for meeting engagements, ambushes and assaults. It integrates the approach turn by turn against the whole opposing force, but plans only the squad's **share** of its own force's battle value (`ForceShare`): that share of the break loss as its kill target, and that share of the enemy's fire. The break loss (`ApproachRemovalShare`) assumes an Attritional enemy, the most stubborn aggression that still withdraws: 1 − `AttritionalEligibilityThreshold` = 75%. An opening range is preferred only if that expected break lands inside the squad's useful band less ten turns of enemy movement (`RetreatFireTurns`), so the retreat can still be punished; the same quantity is the floor on the result. Until 2026-09-22 each squad planned 150% of the whole enemy force under all of its fire, and large battles opened near weapon reach.
- `BattleSquadCapabilityProfile.EffectiveEngagementRange` — mid-fight standoff, the argmax of `removal(r) − incoming(r)` against the opposing force, where incoming is the enemy's own curve plus an arrival-discounted melee term. For a non-degrading weapon against a penetrable target, removal only improves as you close, so standoff is set by return fire rather than by the gun.
- `BattleSquadCapabilityProfile.UsefulFireRange` — the cached outer edge where the same squad outgoing-removal curve, against the same representative opponent, reaches `negligible floor + 0.2 × (peak − floor)`. This is not solely peak-relative, and merely clearing the negligible floor is not sufficient. It returns 0 for no available ammunition/no usable ranged weapon and for a curve ineffective at every range, but remains positive and short for a degrading weapon that becomes useful only after closing. Fire-preserving pursuit uses it for exchange access and positional progress.

Three properties are load-bearing:

- A curve has **no hard threshold cliffs** and supplies one continuous effectiveness function for
  optimal-distance and preferred-engagement calculations.
- **A target that cannot be penetrated buys no standoff.** The curve's peak must clear a floor of one thousandth of the target's battle value per turn, otherwise the range is 0 (close). The Gaussian damage model has no hard impenetrability, so this floor is where the tail stops counting as a reason to choose a range.
- Immediate fire reads the **best engageable target**, while the force frame retains pairwise threat
  geometry and a separately weighted primary counterpart for movement. A single non-engageable body
  does not determine the whole squad's stance or erase a relevant threat on another flank.

**Pursuit range targets.** `Design/Reference/BattleLogic.md` §5.5 separates three quantities — useful firing range (where shooting still contributes), optimal firing range (where the modeled exchange is best), and contact range (the melee objective). Fire-preserving squad pursuit uses useful range for exchange access and positional progress; melee and committed `Press` retain their contact/reach objectives. The per-squad follow and unpursued-escape clocks evaluate concrete pairs through the live weapon/target evaluator, with a named `sampled_live_ranged_evaluator` boundary approximation and a separate melee-contact destination. This does not recalibrate `BattleSquadCapabilityProfile.UsefulFireRange` globally. `OnlyWar.Tests/Battles/RangedPursuitBaselineTests.cs` is the reusable measured baseline and covers withdrawal timing, immediate and prepared fire, short-range advance, and equal-speed pursuit.

**Pursuit diagnostic clocks.** The diagnostic contract keeps three questions separate. `PRESS_CONTACT_PROJECTION` reports a hypothetical continuous press-to-melee-contact clock plus the first attack phase that can use movement-created contact, with the supplying/diagnostic pair, nearest-pair positions, fixed withdrawal heading, pair-local movement assumptions, destination allowance, and explicit unreachable reason. `FOLLOW_SHOT_EVAL` and the pair opportunity carried by `ESCAPE_EVAL` report a hypothetical useful-attack clock; they retain the live evaluator's shooter/target/weapon, readiness/ammunition preparation, useful-range destination, pair geometry/speeds, and unreachable reason. `PURSUIT_PROGRESS` and `PURSUIT_MOVE_RESULT` report only observed executed movement: actual versus planned displacement, nearest-soldier separation before/after, rolling history window, measured progress, membership/sample validity and reset reason, startup state, and whether observed geometry qualified as contact-maintenance evidence. `PURSUIT_EVAL` is only a posture summary, one record per pursuing squad (`squad`, `doctrine`, `press_contact_turns`, `press_attack_phase_turns`, `follow_useful_attack_turns`, which reads `not_evaluated` when the follow projection could not change the posture and was skipped); `FOLLOW_SHOT_EVAL.follow_search` names the bounded search that ran (`first_before_contact`, `shot_now`, `any_shot`), so its turn is not necessarily the earliest; the retired `press_intercept_*` names must not be reintroduced. The `shot_turns` field that remains in `FOLLOW_SHOT_EVAL` is explicitly a compatibility alias for counterfactual useful ranged timing, not an executed-shot count. Any retained declared-speed scalar (`declared_speed_delta` or `ClosingSpeed`) is diagnostic only and never contact evidence.

Complete-pursuit evidence is kept separately in the opt-in
`ActivePursuitScaleDiagnosticsTests`: its summary reports time to first shot, shots fired, maximum
consecutive advancing turns with positive Hold fire, Hold/advance switches, casualties, duration,
outcome, and turn-cap status. The current scale fixtures begin at the pursuit transition and end in
melee, so they validate contact lifecycle and termination while the focused baseline owns the ranged
weapon-shape claims; they cannot measure pre-withdrawal casualties.

Charge value is quoted in the same present-value currency as ranged fire and its closing exposure is integrated along the projected path. Lane spread biases target selection only and never alters the returned raw removal value, so it is unaffected by the squad scorer's temporal discount.

**Morale, withdrawal, and pursuit.** `BattleSideState` carries force intent (`Engaged`, fighting/rear-guard withdrawal, pursuit, rout, or disengaged), the withdrawal heading, covering/rear-guard assignments, and starting force metrics. Organized withdrawal alternates Cover and Bound roles; each pursuing squad chooses Break Off, Follow, Press, or Standoff from its own pairs, its aggression, and its equipment doctrine (`BattlePursuitPlanner`, `PursuitDoctrine`): contact-seeking squads press, fire-support squads follow, a squad with no shot chases, and a squad that cannot reach contact within `MaximumChaseTurns` (30) stands and fires with a shot now, follows with a shot later, and otherwise breaks off. The side's posture is the most committed squad posture; a squad that breaks off while others continue stands down and leaves the field, its battle value still counted in its side's force metrics (`BattleSideState.StoodDownSquadIds`). Follow compares stationary fire, jog-and-fire, and a no-fire run in the same engagement value choice; Press is the committed running posture. `WithdrawalForecast` compares projected BV preservation, including masked departure and command-collapse risk, before assigning an autonomous rear guard. Pursuit decisions retain their actual target-squad pairing. After movement, an unpursued withdrawing squad (one that is no pursuer's quarry this turn) beyond every concrete pair's attainable useful ranged or melee attack opportunity disengages when the earliest pair-local elapsed clock is beyond the engagement planner's two-turn retargeting horizon; empty magazines with reserves can produce a reloadable opportunity, while ammunition exhaustion is explicit `Unreachable`. This is an open-ground contact abstraction, not a battlefield edge. Running soldiers lose melee guard; Burrow can break contact immediately.

**Standoff invariant.** A pursuing squad may enter `Standoff` only when it cannot catch up -- no pair can ever reach contact, or the nearest contact is more than `BattlePursuitPlanner.MaximumChaseTurns` (30) away -- cannot reach melee this turn, and has a worthwhile shot available at the current range. `Standoff` means standing fire: every ordinary squad in that posture may `Hold` and aim/fire, but may not `JogToward` or `RunToward`. If movement toward the enemy is desired, the squad must receive a different pursuit posture or role; a standoff squad must never turn an unwinnable equal-speed chase into a running pursuit.

**Active-pursuit invariant.** Contact remains active only while an actual pursuer-quarry pairing has
timely smoothed observed nearest-soldier separation gain, bounded startup grace for a new assignment,
a recent executed attack, a committed fire cycle making observable progress toward a worthwhile
attack, or a quarry eliminated during its own turn. Executed attacks and aims count for the
pursuer's pair whichever withdrawing squad they were aimed at, since soldiers choose their own
targets. Declared `CurrentSpeed` advantage is diagnostic only. The observed policy keeps four
completed samples and requires more than `0.5` cell of net separation gain, and that progress counts
only if, at the observed closing rate, it reaches the pair's effect separation (useful fire range
while outside it, otherwise melee contact) within `BattlePursuitPlanner.MaximumChaseTurns`
(`PursuitPairActivity.HasTimelyClosingProgress`; slower progress is traced as `slow_progress`); it tolerates grid
rounding, leftover movement, and one brief failed/blocked move, while sustained blocking, moving
away, and equal-speed chasing age out. Exact assigned able-soldier ids invalidate history when
membership changes, and a pursuer gets at most two unproductive retarget startup windows, so
switching targets cannot grant indefinite grace. A pair-local projected opportunity is
counterfactual availability, not evidence that the pursuer actually aimed, reloaded, or fired;
`BattleRoundMetrics` and action records remain the execution evidence. A theoretical shot bridges
non-attacking turns only while the squad holds, retains a viable target, and accumulates readiness;
it cannot keep contact alive while the chosen policy repeatedly declines that fire plan. The
corrected pair interception projection still preserves the immediate-contact exception, but does
not become multi-turn pursuit evidence. `Standoff` satisfies the fire branch through its hold-and-
fire constraint, while `Follow` may instead satisfy it by genuinely closing. Force-level capability
does not replace pair-local observed progress.

After each combat round, `BattleMoraleService` checks only the squads that have a `MoraleCheckTrigger`: casualties this turn (below `MoraleConstants.CasualtyCheckStrengthFraction` of starting strength, 1.0 today), squad leader lost, the side's last command-aura provider destroyed, synapse coverage lost, or a friendly squad routed in the previous turn's pass (any range). The triggers compare against facts taken in `SnapshotTurnStart`. A squad with no trigger rolls nothing unless it is Shaken; then it rolls to rally, and the rally can only restore `Steady`, so it never routs and never invokes mob coercion. `BattleMoraleEvaluator` computes local shock from current/cumulative casualties, leader loss, nearby routing allies, and local outnumbering, then multiplies it by force-wide disadvantage. Per-soldier resolve is a convex Ego function. Synapse coverage skips the check; command auras reduce shock without granting immunity. Squads aggregate to `Steady`, `Shaken`, or sticky `Routing`; routing preempts the normal plan and enters the same pursuit, outcome, aftermath, and replay pipeline as voluntary withdrawal. Morale and withdrawal tunables live in code (`MoraleConstants` and the withdrawal planners) and are calibration surfaces rather than rules-data facts.

Battle completion produces a typed `BattleOutcome` with end reason, field holder, and disengaged/eliminated/routing/rear-guard squad ids. Stood-down pursuers are not listed as disengaged: they left the field but did not withdraw, and the mission layer reads a disengaged mission squad as the mission side withdrawing. Typed `BattleEvent`s record withdrawal, cover, rear guard, pursuit, rout, and disengagement transitions for replay and narrative consumers.

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

`OnlyWar.Medical` owns the headless medical transitions. `MedicalTurnProcessor` remains the
Application cadence adapter invoked by the `ProcessMedical` step of
`TurnController.ProcessTurn`; it supplies bodies and campaign maps to the module and applies
returned completion facts to roster/posting/event state. The module has two policy halves.

- **Natural healing.** `MedicalHealthPolicy` applies `Wounds.ApplyWeekOfHealing()` to every wounded hit location regardless of deployment, *except* severed non-vital locations that require a replacement procedure. Crippled locations do not require replacement for now. `HitLocation.IsReplacementEligible` is the single source of truth for that exclusion and is shared with the Apothecarium view and the Squad Screen, so the three surfaces cannot disagree. Cadence and the daily Astartes pass are specified in §5.3.
- **Procedure resolution.** `MedicalHealthPolicy.AdvanceProcedure` decrements weeks-remaining and, on completion, clears the location's wounds and returns a typed completion transition. The Application/Campaign adapter removes the persisted procedure and synchronizes reservation flags. Cybernetic completion sets `HitLocation.IsCybernetic`; vat-grown leaves it clear. Because wounds are not cleared until completion, a marine under a procedure stays out-of-action automatically rather than needing a separate flag.

Medical completion returns a bounded list of `CompletedMedicalProcedure` facts. Each successful
primary target emits one `BodyPartReplacement` event, recording the method, prior cybernetic state,
duration, cost, and (when applicable) the source incapacitation episode. The canonical ledger keeps
an in-memory `SoldierId → OpenNearDeathEpisode` projection: a typed qualifying incapacitation
opens it, and a recovery referencing that source closes it. `ChapterUpkeepProcessor` snapshots
deployability only for those open episodes, runs the existing daily healing, field-care, weekly
healing, and procedure order, then emits exactly one `NearDeathRecovery` when a non-deployable
brother becomes deployable. Natural/field care, cybernetic, and vat-grown recovery are distinguished
without scanning full career histories; a missing or fallen soldier closes no fictional recovery.

`MedicalProcedure` (soldier id, hit-location template id, `MedicalProcedureType { Cybernetic, VatGrown }`, weeks remaining, Requisition cost paid up front) is a Domain health primitive. `MedicalProcedureService` remains the Campaign adapter while typed duration/cost and facility rules live in `OnlyWar.Medical`; recruitment reservations and staffing/capacity are supplied as facts at the boundary. The adapter validates eligibility, surgery site, co-located staff, and affordability, then deducts cost and creates the procedure; `EvaluateRequisites` returns the per-requisite breakdown the UI renders green/red. Durations and costs live in `MedicalProcedureRules`, never in UI literals. The gates are a co-located Apothecary **and** Techmarine (same ship or same region, checked only at procedure start) plus a valid surgery site — aboard a ship, or an Imperial/player-controlled Hive/Forge/Civilised region. No fortress-monastery is modeled, so a player-held region serves as the de-facto base.

**Apothecary field care.** `OnlyWar.Medical.Treatment.FieldCarePolicy` converts explicit provider
capacity into wound demotions for explicit patient bodies. Campaign's `FieldCareService` remains the
campaign adapter that resolves an Apothecary's **Medical** rating, reach and location, then records
the returned facts and grants experience. Treatment is a **forced wound-band demotion applied the
day it happens**, not a credit settled at turn end — a brother hit in a day-2 assault and treated
that evening enters the day-3 battle at reduced severity, which is the whole point, since battles
read live wound state. All tunables live in `FieldCareConstants`, never the rules DB.

- **Reach** is the order: every wounded soldier in its assigned squads plus its attached soldiers. This is what makes order-level attachment the right shape (§5.6).
- **Capacity** is mildly superlinear in Medical rating and clamped, so a Master of the Apothecarion outworks an ordinary brother without replacing several of them.
- **Cost is flat in band *index*, not band value.** Wound bands are powers of 16, so a proportional cost would make severe wounds untreatable; a sub-linear surcharge covers extra wounds within a band, since a demotion moves the whole band at once.
- **Triage** is worst-first and deliberately not spread thin: severity by `Wounds.RecoveryTimeLeft()` — the *player-visible* number, so the order shown is the order run — then Rank desc, Subrank desc, then a random draw from the session RNG. Re-triaged after **every** treatment, so a demotion can hand the queue to someone else mid-day.
- **Greedy, no per-soldier cap, use-it-or-lose-it.** Re-triage self-levels: once the worst case drops below the next man, the queue reorders on its own.
- **Ceiling.** `IsReplacementEligible` is true for severed non-vital locations only. Crippled locations remain eligible for natural and field healing; a brother who has actually lost a non-vital part is a surgical case.

Two seams, deliberately deduped. The mission pass runs on `MissionDayScheduler`'s scheduler-level `onDayEnd`, iterating **distinct `Order`s** — never mission elements, because `BuildMissionElements` fans one order into several single-squad drivers for `IndependentSquads` and a per-driver pass would make an Apothecary silently worth 3×. Garrison care runs the same `FieldCarePolicy` routine through `ChapterUpkeepProcessor.ProcessMedical` before the weekly cascade. **Field beats garrison by construction, not by rule:** an Apothecary under an order fails the "not on a mission" test defining the garrison pool, so the pools are disjoint and no man spends a day twice. Co-location resolves through `PlayerSoldier.EffectiveRegion`, since an attached Apothecary's home squad may sit on the ship while he is forward — `MedicalProcedureService.HasCoLocatedStaff` routes through it for the same reason.

Gene-seed recovery resolves once per confirmed-dead brother in `BattleTurnResolver.RemoveSoldiersKilledInBattle` (`ResolveGeneseedRecovery`), folding any recovered gland's purity into the chapter aggregate and writing a structured `SoldierEventType.GeneseedRecovery` event onto the preserved fallen-brother dossier; the battle log reads that recorded outcome rather than recomputing it. `PlayerForce` carries a count-weighted aggregate `GeneseedPurity` float alongside `GeneseedStockpile` — seeded pristine at founding, each recovered gland contributing a purity rolled around a baseline with small downward drift (`GeneseedRules`). Both persist on the extended `GlobalData` row. Stockpile drawdown happens in the recruitment pipeline (one unit consumed on Phase 0 → Phase 1; PRD §4.9).

### 6.6.2 Chapter Operational Doctrine & Duty Readiness

Individual/squad policy lives in the headless `OnlyWar.Medical` assembly, with typed facts and
reasons in `OnlyWar.Medical.Abstractions`. It consumes supplied doctrine, personnel, posting and
recruitment-reservation facts; null inputs do not select a campaign. Presentation row context
adapts the neutral `SquadDeploymentContext`, and labels/colors stay separate from policy
decisions. Campaign's `DutyReadinessService` and `SquadReadinessService` adapt the live campaign
model to those neutral facts. Mission materialization and engagement refresh pass the
session's doctrine and reservations into the battle factory, preserving frozen in-battle
participants.

`ForceReadinessInputs` is the explicit input boundary: it applies a force's doctrine and recruitment
program to that force's own squads only, matched on the faction instance. No readiness evaluation
selects a campaign implicitly or reuses another campaign's reservations or doctrine.

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

The lower-level runtime construction API lives in `OnlyWar.Runtime`. `RuntimeSoldierFactory`
receives a detached template, an `IRNG`, and an `IEntityIdAllocator`, and returns a portable
`RuntimeSoldier`; `SoldierFactory` materializes it, and `SquadFactory` and `ForceGenerator`
build squads and forces from those soldiers. The Runtime assembly has no reference
to Generation, Engine, Godot, SQLite, or campaign/session state. Name pools and the negative
tactical allocator are Runtime-owned; the positive allocator is supplied by the caller.

`ForceGenerator.GenerateForce(ForceGenerationRequest, IRNG, IEntityIdAllocator)` dispatches by `ForceCompositionProfile`. The allocator is optional at persistent-campaign call sites; tactical missions supply a mission-local `TacticalEntityIdAllocator`, which issues negative IDs and therefore does not advance or collide with the positive campaign counters:

The irregular-strength path is opt-in through `SquadTemplateElement.RollsStrength`; `Min < Max` alone never enables it. This protects establishment formations such as Tactical Squads and chapter offices, whose minimum is an understrength floor rather than a random muster. For a rolling element, the template's Battle Value uses `ExpectedNumber` (the midpoint), while the generated force charges the budget for the actual rolled count. The roll consumes the shared tactical RNG; non-rolling elements remain exact no-ops. If a faction has no squad-template map, force generation normalizes the null map to empty and returns no force instead of dereferencing it.

- **Generic (Garrison, AssaultForce, AmbushForce):** Adds one affordable HQ squad when the budget can still field at least three full non-HQ squads afterward. It then randomly selects among affordable non-HQ squad templates, cycling through unused affordable types before repeating so large forces become mixed formations instead of copies of the most expensive squad. When no full squad fits the remaining budget, it generates the highest-value partial squad that can fit. `TargetBattleValue` is a `long` (region garrisons can reach billions on hive/forge worlds). When generating a region's *defending* force, `PrepareAssaultMissionStep` caps the mobilized garrison at `MaxMobilizedGarrison` (10,000 troopers) so battles stay tabletop-scale; very large garrisons act as a deterrent to direct assault, to be engaged at scale by future bombardment/war-machine mechanics.
- **SpecialHQTarget:** Selects an HQ template by tier index from sorted HQ templates. Adds a bodyguard squad if `TargetBattleValue ≤ 0` and a `BodyguardSquadTemplate` is defined.
- **ScoutPatrol:** Randomly selects from Scout-flagged templates, generating `Tier` squads.

`SquadFactory.GenerateSquad(...)` is the Campaign materialization adapter for the campaign
`Squad`/loadout graph. It delegates soldier stat/body/skill construction to Runtime, then applies
campaign assignment and equipment state. Randomness and temporary entity IDs are explicit on the
tactical path; persistent campaign construction uses the campaign allocator. Runtime remains
independent of Generation and the host.

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

Generation lives in `OnlyWar.Generation` and references only Domain, Abstractions, `OnlyWar.Generation.Abstractions` and Runtime. It builds a candidate campaign and publishes nothing. `SectorBuilder.GenerateSector` constructs the worlds and the founding chapter, rebuilds topology and governance through `OnlyWar.Runtime`'s `SectorTopologyBuilder`, seeds ghost populations, then hands the candidate to `ScenarioBuilder.StampPromisedWorld`. `CampaignApplication.StartNewCampaign` installs the rules, date and sector together only once generation returns, so a failure during generation or warm-up leaves the campaign already in play untouched. Warm-up does not advance the campaign date or run player upkeep, fleet travel or scenario resolution.

Every campaign capability generation needs arrives as a `GenerationSupport` bundle of ports declared in
`OnlyWar.Generation.Abstractions`: seeding, narrative, fleet setup, founding-role ranking,
training/rating policy, and `ICandidateWarmupSimulator`. `CandidateGenerationSupport` composes the
implementations. The simulator uses an `ICampaignSimulationSession` for the candidate sector, so
Generation depends on a port rather than on Campaign turn implementation and the graph remains acyclic.

On load, `SectorTopologyBuilder` reconstructs the derived subsectors, warp lanes, and governance seats from the saved sector and rules profile; loading does not invoke new-campaign generation.

Subsectors, warp lanes, and governance designations are derived runtime structures, not rules-database
entities; they are reconstructed from the saved sector and rules profile. The topology algorithm and
pixel metrics remain code-owned, while sector dimensions, density, and subsector scale are data-owned
through `SectorGenerationProfile`.

Warp lane generation: after subsector clustering, the highest-population planet in each subsector is designated its capital. A warp lane is established from each capital to every other planet in its subsector, and between each capital and the capitals of adjacent subsectors. The resulting lane graph is used by fleet movement routing (Dijkstra shortest path, weighted by Euclidean hop distance) to compute known multi-hop lane routes. Travel duration is determined by subsector relationship and Gaussian subjective/objective time multipliers rather than by Euclidean distance alone.

`FleetRouteCalculator` computes route topology and timing:

- `FleetRouteScope.SameSubsector`: 1 expected subjective warp week.
- `FleetRouteScope.AdjacentSubsector`: 3 expected subjective warp weeks.
- `FleetRouteScope.DistantSubsector`: 7 expected subjective warp weeks.
- Every journey adds 4 fixed subjective/objective weeks for in-system travel to and from warp translation points.
- Subjective warp multiplier: z = 0 maps to 1x, +0.5/-0.5 maps to 1/2x/2x, +1/-1 maps to 1/3x/3x.
- Objective warp multiplier: z = 0 maps to 1x, +5 maps to 1/10x, -5 maps to 10x.
- `FleetRoute.BaseTurns` is the objective total weeks rounded up for campaign turn processing.
- `TaskForce` stores resolved travel state rather than the full route graph: origin, destination, `FleetTravelPhase`, total and current-phase objective weeks remaining, rolled subjective warp weeks, rolled objective warp weeks, and a one-time subjective-training-applied flag.
- Route-based movement advances through `OutboundSystemTransit`, `InWarp`, `InboundSystemTransit`, and `InOrbit`; fixed-week countdown remains available for tests and simple routes.
- Turn training excludes embarked squads while their fleet is `InWarp`; when `AdvanceTravelOneWeek` reports warp exit, `FleetTurnProcessor` delegates `WeeklyTrainingPoints * WarpSubjectiveWeeks` training for embarked idle squads to `ChapterUpkeepProcessor`.

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
formation, roster-delta, and constraint rows. Both live behind `IMusterScreenApplication`, which
owns the plan instance for the active session and addresses formations by selection key rather than
handing the screen a live transfer option. `FleetCapacityPlanService` returns typed direct-placement,
rebalance-required, or impossible results; it never splits a squad. Commit is all-or-nothing after
revalidation, and canceled provisional formations do not consume ordinals.

**Recovery Operations.** `IndividualPostingService` is the single mutation boundary for detached
personnel. `MedicalFacilityService` enumerates known treatment sites with typed Ready, Resolvable, and
Ineligible outcomes; `RecoveryOperationsViewModelBuilder` and `RecoveryPlanService` expose and commit
care destination, patient movement, staff/capacity, treatment, and reunion decisions. A medical
detachment removes the soldier from physical squad presence while retaining nominal membership. After
care completes, the physical posting remains a `Medical` posting until the player explicitly reunites
the soldier, and reunion requires co-location; `AwaitingReunion` is a projection-only label and is
not persisted.

**Planetary Operations.** `RegionalOrderEligibilityService` scopes candidates to the target and adjacent
surface regions, includes squads already assigned to the selected order, and excludes orbiting or
otherwise ineligible formations. `OrderMutationService` owns typed create, assign, add, remove, and cancel
operations. `PlanetForceMovementService` performs atomic landing and whole-squad embarkation with
capacity checks and order cleanup for `MovementParty` selections containing squads and/or characters.

Every one of these commands names the campaign it mutates. `OrderAssignment` takes an explicit
`OrderCommandContext(Sector, Date)` from which it reads the order index, player faction and
recruitment reservations; `OrderMutationService`, `PlanetForceMovementService`, `OrderAttachment` and
`IndividualPostingService.AttachToOrder` take the campaign date as a parameter so operational
postings are stamped without consulting the current session. An order that loses its last
participant is retired through `Order.RegisteredSector`, the registration `Sector.AddNewOrder`
already recorded on it. `InboundOrders.ForRegion` takes the sector to scan and
`SpecialistAvailability.EnumerateRoster/EnumerateCandidates` take the chapter roster to offer.
`PlanetaryOperationsScreenController` resolves those inputs from the installed campaign; it is the
host-side adapter for this boundary, not a fallback inside the policy.

Readiness inputs follow the same rule. The Operations helper `ForceReadinessInputs` answers "which doctrine and
which reservations govern this squad" against a force the caller names, applying the one guard that
matters: doctrine and the recruitment pipeline belong to a single player force and never govern
another faction's squads. Order lifecycle policy resolves that force from `OrderCommandContext.Force`
at command entry, from the `Sector` a service was already given, or from `Order.RegisteredSector`
inside the participant-mutation boundary — an order records the sector that registered it, so it can
answer for itself. An order still being assembled has no registration, so `OrderAssignment` resolves
and passes the inputs explicitly for that case. There is no ambient fallback behind any of this: a
caller with no force to name gets no doctrine and no program.
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

Host/Presentation/UI/SectorMapLabelLayout is the engine-independent placement solver. It receives measured
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
the rules database's `ScenarioProfile` rows, with a deterministic Random choice, and persists the
resolved result in `CampaignScenario.InvaderFactionId`. A profile may carry one optional
`ScenarioInfiltratorOverride`; its presence makes the scenario own infiltrator setup, while a
profile without one keeps normal infiltrator generation. The Tyranid profile uses that override for
the Genestealer Cult; the Ork profile has no override and preserves its naturally rolled infiltrator.
The Ork opening reuses the existing Promised-World objective, victory/lapse, and Home World reward
loop, records the canonical destroyed-fleet/no-reinforcement briefing, and incorporates local feral
Orks into one opening Waaagh!. No live enemy fleet is required; feral survivors remain indelible after
victory.

**Persistence.** Current save format 19 stores regional Ork links, latent sources, persistent Waaagh
identities, scenario selection, transit state, and Chapter operational doctrine. The loader validates
references and keeps persistent command squads out of ordinary landed-squad collections. Beacon/scope
state is not part of the save model.

### 6.13 Faction Capability Decoupling

Campaign behavior is capability-owned rather than identity-owned. `FactionBehavior` exposes the
independent `HasGhostPlanets`, `HasDormantPopulations`, `GeneratesInvasions`, and `MobMentality`
flags; `FactionCapabilities` is the shared query boundary. Consumers must not reconstruct a
faction identity from hostility, indelibility, population model, faction id, or display name.
`InvadesOnVictory` is likewise consumed directly for victory aftermath decisions.

Visibility, activity, and command affiliation are separate state axes. A regional presence uses
`IsPublic`/`IsOpenlyActive` for disclosure, `DormantConsolidation` for dormant ecosystem state, and
`StrategicInvasionForceId` for persistent command affiliation. `GhostPopulationSource` and
`StrategicInvasionForce` are generic Domain models; Persistence maps legacy Ork-labeled save members
to that generic state at the load boundary.

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

The Apothecarium/Recovery controller consumes `IMedicalScreenApplication`, injected before the
scene enters the tree. `CampaignApplication` resolves the active session for each medical query
and confirmation. Queries expose detached medical records and destination IDs/names, never
soldiers, ships, regions or postings. The application rebuilds the selected treatment from its
hit-location/type identity at command execution, rejects a stale session token before mutation,
and owns procedure requisite evaluation and recovery/reunion dispatch. Session replacement
notifies the controller to discard patient, destination and treatment selections. A rejected
confirmation displays the command's reason in the recovery plan status.

Portable medical projections/builders and shared squad-row projections are owned by
`OnlyWar.Application`; Godot textures and rendering remain in the host. Medical
queries supply explicit force doctrine and recruitment inputs, and the shared squad row builder
accepts those values without a current-campaign fallback. `CareDestinationService` and
`RecoveryPlanService` resolve staffing reservations from the supplied force. Recovery validates
the combined staff/patient berth requirement and posting eligibility before movement.

`MedicalScreenApplicationTests` cover session replacement with reused patient IDs, detached values,
invalid treatment rejection, combined capacity rejection, successful treatment, and repeat-command
rejection. A recursive public-projection test exercises nested contract graphs. Godot scene smoke
covers medical navigation; interactive supported-window medical review remains release QA.

`PlanetaryOperationsScreenController` consumes `IOperationsScreenApplication` through the same
`Configure`/`SessionChanged` pattern. Order issue, reinforcement, participant release, squad
removal, cancellation, aggression, specialist attachment/detachment, landing, embarkation and
casualty detachment are commands carrying only entity IDs and the caller's session token; the
controller holds no mutation service and no undo closure. A successful command returns an
application-issued undo token that is single-use, bound to the session that produced it and
discarded when the session is replaced, so a queued undo cannot reach a campaign that merely reuses
the same order IDs. Order-formation rules — the character add-versus-release toggle, the diversion
target-faction fallback, mission-to-order equivalence and eligible-squad filtering — belong to the
application. The Operations read path is application-owned. `QueryOperations` returns one detached workspace —
header, map, region dossier, active orders, mission options, live-order editor, force tree, ship
choices and casualty rows — and `QueryWorldDossier`, `QueryRegionCards`, `QueryEntry`,
`QueryGovernorRequestEntry` and `ResolveForceSelection` cover the rest. Presentation stayed in the
host by separating classification from colour: projections carry a `UiAccent` and, for factions, an
ARGB value, and `OnlyWarStyle.Resolve` is the only place either becomes a Godot colour. Portable
projection sources—hierarchy, force-tree, region, ordering, and icon-key models—are owned by
`OnlyWar.Application`; the icon atlas, styles, and `DossierCard` rendering stay in the host. Region
adjacency travels on the map card, so the map control does not walk the region graph.

Session lifetime and reporting follow the same shape. `ISessionControlApplication` owns campaign
identity, recoverability and every save (manual, overwrite, protected pre-turn, post-turn autosave,
initial autosave, diagnostic capture); it captures the revision before writing so a failed write
cannot mark a later state recoverable, and refuses a token from a replaced session. The host composes
`SaveGameManager` at startup. `ICommandScreenApplication`
supplies the Command Brief and a `ChronicleView`. `MainGameScene.CampaignControls`,
`StartMenu.ReleaseControls`, `CommandScreenController` and `BattleReviewController` use application
contracts rather than selecting global campaign state. `SessionControlApplicationTests` covers the
session-token and recoverability boundary.
`IMainScreenApplication` completes the main screen. `QueryHeader` supplies the formatted date and
requisition, `QueryStartup` decides which world the campaign opens on and whether the founding
directive is outstanding, `AcknowledgeOpeningBrief` performs that one-shot write, `ResolveTurn`
advances the turn and — only once resolution and report construction both succeed — replaces the
persisted `LastTurnReportSnapshot` and hands back a `TurnReportView`, `QueryLastTurnReport` restores
the saved one, and `QueryNeophytePlacementTargets`/`PlaceNeophyte` own scout-squad eligibility and
the promotion itself. Every one of these is session-token guarded.

`TurnReportProjector` (Application) decides what the player
is told about each mission, NPC operation, strategic combat, construction, fortification handover,
governor request, recruitment week and campaign event, including the intel-tier redaction of enemy
activity and which battles may be reviewed; `ConstructionReportBuilder`,
`MissionReportHeadlineBuilder`, `MissionReportSummaryBuilder`, `NpcMissionReportBuilder`,
`ReconOperationReportBuilder` and `LastTurnReportSnapshotBuilder` are Application-owned. `EndOfTurnDialogController` only renders a
`TurnReportView`, opens the debrief by opaque replay id and asks the application for the detached
Battle Review display; `MissionDebriefLineGrouper` and `BattleReplaySummaryBuilder` remain host
presentation.
`MainScreenApplicationTests` cover the header, startup, stale-token refusals, turn and placement,
saved/empty reports, and the detached contract boundary.

The roster, fleet, training and map screens complete the boundary. `IFleetScreenApplication` owns
the Classis tree, transfer legality and the three fleet dialogs, with route planning and the
the divide/merge rules behind ID-and-token commands; `FleetTransferService` (Campaign) owns the
berth/shared-location rules for those transfers. `ISystemInspectorApplication` returns one
`SystemInspectorView` per selection, deciding which task forces are in orbit, which is chosen when
the player has not chosen, and whether its actions are legal. `ISectorMapApplication` supplies the
map's geometry (subsector partitioning, the Voronoi tessellation and its adjacency), planet and
fleet markers, the turn-varying label facts and the selected system's orbiting fleets; palette,
smoothing and drawing stay in the host. `IChapterScreenApplication` builds the whole chapter
browser — breadcrumbs, menus, detail, transfer options and the ordered context list — and owns the
transfer, Black Carapace and recall prompts and commands. `IMusterScreenApplication` owns the
staged Muster plan itself, so the plan is session-bound and dropped with the campaign it was drawn
against. `ILoadoutScreenApplication` covers the squad loadout screen and the chapter/theater
doctrine dialog; the operational doctrine is staged in the dialog as three plain values and only
the application writes it. `ITrainingScreenApplication` owns the 10th Company snapshot, forecast,
staff validation and scout-training writes. `ICampaignNavigationApplication` resolves Command
navigation targets and squad locations to the surface they land on; `MainGameScene` consumes those
semantic contracts instead of traversing campaign aggregates.

Portable projection sources—fleet, diplomacy, chapter, detail, muster, loadout, and recruitment
models—are owned by `OnlyWar.Application`. The loadout
surfaces still exchange authored rules templates — `WeaponSet`, `EquipmentLoadout`,
`EquipmentRulesCatalog` and the validation context built from them — which are immutable reference
data rather than the campaign graph; no live squad, soldier, force or doctrine crosses that
boundary. Every screen projection passes recruitment-program and operational-doctrine facts
explicitly. Debug bootstraps create and configure a `CampaignApplication` explicitly.
`Scenes/BattleMap` is an empty stub and carries no campaign access. Battle Review has one explicit
host projection boundary: `BattleReplaySummaryBuilder` is the composition adapter that consumes
the concrete battle replay, traverses its snapshots and actions, and immediately emits detached
`BattleReplayDisplay` and map data. No Scene names those replay types; the public application
contracts expose only detached display data.

`CampaignApplication` composes the Operations personnel capability in its constructor, with
`TestPersonnelComposition` as the test assembly's equivalent. `TurnController` composes mission
execution's tactical squad factory in production, with `TestExecutionContextFactory` in tests.
`OperationsScreenApplicationTests` cover issue-then-undo,
single-use tokens, replaced-session rejection of queued undo, cancellation, participant restoration,
and ineligible-squad rejection.

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

The report snapshot models in `OnlyWar.Domain.Reports` are independent of Godot and campaign entities:

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

`CampaignApplication.ResolveTurn` runs `TurnController.ProcessTurn`, which resolves the week and
returns one `TurnResolutionResult`. After the result has returned,
`LastTurnReportSnapshotBuilder.Build` runs `TurnReportProjector` once and returns both the persisted
`LastTurnReportSnapshot` and the live `EndOfTurnReportEntry` presentation list. This keeps immediate
post-turn wording and reloaded latest-report wording on one construction path.

On a successful resolution, `ResolveTurn` stores the snapshot on
`Sector.PlayerForce.LastTurnReportSnapshot` and returns the presentation entries to the scene as a
`TurnReportView`, which `MainGameScene` passes to `EndOfTurnDialogController.SetReport`. The
assignment occurs only after report construction succeeds. The post-turn autosave and later manual saves therefore serialize the
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
row as null for a supported-version campaign-start or protected turn-1 save with no resolved report.
The row is written in the same transaction as all other campaign data; no sidecar file is used, so
slot copying, autosave retention, rollback, and diagnostics remain on one persistence path.

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

Cancellation undo is owned by `OrderMutationService`. Its opaque `OrderRestoreToken` captures
the cancelled participant set and originating sector instance. Restore rejects tokens after
campaign replacement and validates every participant's current commitment, readiness and staging
before changing any assignment or registering the order. Single-squad restore uses the same
validation path; the controller does not implement a multi-step partial restore.

These workspaces are covered primarily by pure domain/view-model tests and shallow scene-wiring smoke;
supported-resolution visual layout remains release QA.

### 7.6 Force Legibility & Shared Squad Rows

Strength/readiness snapshots and their evaluations are Application-owned query models. The host
retains row composition, context/selection, labels, and colors. `SquadRowViewModelBuilder.Build`
accepts explicit doctrine and reservation inputs, including leader-tooltip facts; callers without
campaign context pass null rather than selecting an implicit campaign. UI, order participant
validation, and battle materialization share the same readiness facts for the same supplied inputs.

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

## 8. Current Architectural Constraints

This section records the rules that keep the current architecture coherent. It is not a refactor history
or a backlog.

### 8.1 Module boundaries

- Project references point from consumer to dependency. `OnlyWar.Abstractions` is the foundation;
  Domain depends only on it; abstraction projects expose ports and boundary records; implementation
  projects depend on Domain and the abstractions they consume.
- Runtime, Generation, Medical, Operations, Battles, and Persistence are headless implementation
  modules. Campaign owns live campaign policy and turn orchestration. Application owns session lifetime,
  load/save coordination, detached screen queries and commands, and composition.
- Persistence owns SQLite and file-format concerns and never depends on Campaign or Application.
  The Godot host is the outer consumer: it owns scene interaction, startup/path/logging wiring, and
  presentation, and is not a dependency of headless production code.
- Namespace families follow project ownership. Physical subdirectories are organizational only; new
  production types use the namespace family of the project that owns them.

### 8.2 State and public surfaces

- `GameSession` is the explicit lifetime boundary for rules, live sector, campaign date, and `IRNG`.
  The common `ICampaignSession` seam exposes identity only; `ICampaignSimulationSession` is the wider
  simulation contract used where live simulation is intentional.
- Campaign and feature contexts keep aggregate handles and mutation capabilities private. Application
  screen/query ports return detached records, not `Sector`, `Planet`, `Squad`, `Order`, battle, or
  replay objects. Aggregate-backed projectors and readiness diagnostics remain implementation-only.
- Application publishes narrow public capabilities for storage and screen workflows; the live session and
  full service graph remain internal composition details. `Host/Presentation` formats and projects data
  for display, while `Scenes` own Godot nodes, input, navigation, and view updates.
- New production paths pass session, context, allocator, and RNG dependencies explicitly. A process-wide
  random source is a composition choice, not a hidden dependency of campaign policy.

### 8.3 Data and compatibility

- Rules data owns modifiable content, profiles, and semantic assignments; code owns algorithms and closed
  vocabularies. Loaders validate stable keys, roles, flags, references, and required content before play.
- Save schema changes require a `SaveFormat` version bump. Current saves use exact-version acceptance;
  older and newer files are rejected before campaign data is loaded. Save and rules readers remain in
  Persistence, while Application/Campaign rebuild policy and projections after the domain graph exists.
- Persistence compatibility is owner-local: save-format/catalog guards, mappings for values omitted by
  older saves, legacy equipment and faction-rule readers, and typed legacy event-payload registration
  live beside the current persistence adapters. These adapters translate external data into current
  Domain structures; they do not publish gameplay façades.
- `OnlyWar.Operations` retains assembly-level type-forwarders for mission debrief and outcome values
  whose owner is `OnlyWar.Operations.Abstractions`. This preserves binary consumers without creating
  a second owner.
- Narrow source-compatible overloads or aliases may remain inside their owning type. They are not
  cross-module contracts and are not a pattern for new composition. There is no broad compatibility
  façade or parallel ownership manifest.

### 8.4 Verification and release boundaries

- The focused module-boundary checks in `OnlyWar.Tests` protect public contract shape, project-reference
  direction, SQLite package ownership, outcome-contract ownership, readiness visibility, and the
  intentional test-only friend seam. They are not a source-text ownership scanner.
- `OnlyWar.HeadlessTests` verifies headless construction and assembly boundaries without loading Godot.
  Domain, feature, persistence, and UI behavior tests remain separate from architecture checks.
- Save/replay detachment, rules validation, and scene wiring are regression concerns. Balance calibration,
  long-horizon simulation diagnostics, and supported-window visual QA are release/testing concerns rather
  than additional module-ownership rules.

---

## 9. Testing Strategy

Tests are separated by what they can observe. `OnlyWar.Tests` covers the mixed host/headless regression
surface; `OnlyWar.HeadlessTests` exercises the module assemblies without loading Godot; domain, feature,
persistence, UI, and scene tests provide behavior coverage. The architecture checks below are intentionally
narrow and executable against the public type system and project metadata.

### 9.1 Architecture checks

`OnlyWar.Tests/Architecture/ModuleBoundaryEnforcementTests.cs` contains the current architecture contract:

| Check | Boundary enforced |
|---|---|
| `SharedQueryPortsDoNotExposeCampaignAggregates` | Shared query and readiness ports do not publish live campaign aggregates. |
| `CommonCampaignSessionSeamDoesNotExposeSimulationAggregate` | The common session seam exposes identity only; the wider simulation seam is explicit. |
| `ScreenPortsReturnDetachedContractGraphs` | Public screen application contracts contain detached data rather than live campaign or replay types. |
| `AggregateBackedProjectionImplementationsAreNotPublicPorts` | Aggregate-backed screen projectors remain implementation details. |
| `PublicCampaignFacadeDoesNotPublishLiveSessionOrServiceGraph` | Application does not publish the live session or complete service graph; storage/save capabilities remain public. |
| `OperationsOutcomeContractsLiveInTheOperationsAbstractionAssembly` | Operations outcome/debrief contracts have one abstraction owner and retain assembly forwarding for binary consumers. |
| `OperationsReadinessDiagnosticsRemainImplementationPrivate` | Mission readiness diagnostics do not become public Operations contracts. |
| `ModuleProjectReferencesMatchTheApprovedAcyclicMatrix` | Actual module project references match the approved acyclic dependency graph. |
| `SqliteProviderIsOwnedByPersistenceProject` | `Microsoft.Data.Sqlite` is packaged only by `OnlyWar.Persistence`. |
| `ProductionFriendAssembliesMatchTheIntentionalAllowlist` | Only the intentional test assembly receives production internals access. |

The checks use reflection and `.csproj` metadata. They do not infer ownership from file names or scan
production source text. Namespace ownership and module responsibility are documented in §2 and enforced
where the compiler or public type system can express the boundary.

### 9.2 Verification commands

Use targeted non-slow tests while iterating:

```powershell
dotnet test OnlyWar.Tests/OnlyWar.Tests.csproj --filter "FullyQualifiedName~OnlyWar.Tests.Architecture&Category!=Slow"
dotnet test OnlyWar.HeadlessTests/OnlyWar.HeadlessTests.csproj --filter "Category!=Slow"
```

For broader confidence, build the solution and run the relevant domain or feature namespace before the
full suite:

```powershell
dotnet build OnlyWarGodot.sln --nologo -v q
dotnet test OnlyWar.Tests/OnlyWar.Tests.csproj --filter "FullyQualifiedName~OnlyWar.Tests.Domain&Category!=Slow"
```

Godot-dependent behavior is covered separately by the release-scene wiring smoke. Host compilation is
not a substitute for export/package or supported-window visual verification.

### 9.3 Regression scope

Keep behavior coverage around the boundaries most likely to drift:

- save-schema versioning, exact-version rejection, atomic writes, and load reconstruction;
- detached screen graphs and battle-replay projection redaction;
- rules-loader validation of stable keys, semantic roles, references, and optional legacy layouts;
- explicit session/context/RNG identity across turn, generation, Operations, Medical, and Battles flows;
- domain encoding and clone/round-trip invariants for wounds, soldiers, squads, and equipment;
- headless construction and Godot scene wiring at the host boundary.

Long-horizon balance diagnostics and release visual QA are separate from architecture enforcement.
