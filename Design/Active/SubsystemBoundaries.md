# Subsystem Boundaries and Orchestrator Ownership

**Status:** Phases 0–3 (SB-00–SB-04, SB-07 and SB-08) complete in the working tree. In Phase 4, SB-05a, SB-05b-1, SB-05b-2, SB-06, SB-09 and SB-10 are complete: no active-campaign lookup remains in mission execution, order/movement commands or generation; `OnlyWar.Battles`, `OnlyWar.Generation`, `OnlyWar.Operations`, `OnlyWar.Campaign` and `OnlyWar.Application` are extracted; and the host's new/load/turn paths use the application API. Operations retains a transitional `BattleSquad` handle reference, while the application/campaign projects retain transitional feature references, until SB-12 finalizes the allowlist. Phase 6 is in progress: SB-11a is code-complete — the Apothecarium/Recovery and Planetary Operations screens use typed application commands, application-issued undo tokens and fully detached projections, and name no campaign entity. SB-11b is code-complete: session lifetime, recoverability, saving, the end-turn gate, the Command/Battle Review screens, the turn report and debrief dialogs, and `MainGameScene`'s own campaign facts and writes are all on the application boundary; the scene's remaining singleton reads only resolve entities for unmigrated SB-11c screens. SB-11c and final graph enforcement have not been started.
**Created:** 2026-09-03
**Updated:** 2026-09-06 (Operations command boundary)
**Purpose:** Independently assignable work to establish subsystem boundaries around Battles, Planetary Operations, Readiness/Medical, Persistence, Generation, and UI, building on the existing orchestrator extractions.

## 1. Outcome and scope

Build a modular monolith with explicit rule ownership, controlled mutation paths, and compiler-enforced dependencies. Keep campaign and battle sequencing readable. Continue breaking up orchestrators only when an extraction establishes ownership, narrows access to state, or isolates an invariant worth testing.

This is a behavior-preserving architecture project. Preserve deployment eligibility, tactical behavior, mission/day/turn ordering, medical cadence, transport/posting rules, reports, save compatibility, generation behavior, and UI workflows. Product changes discovered along the way must be recorded separately rather than folded into an extraction without a decision.

Completion means all six areas have enforceable boundaries; a new folder or facade accepting the entire mutable campaign does not qualify. The application coordinates cross-subsystem workflows. Subsystems own their policies and implementation details. UI receives projections and sends commands. Persistence receives explicit data and does not select or install the active session. Generation produces initial state without publishing a partial campaign globally.

Out of scope: new gameplay, combat calibration, event sourcing, distributed services, a general message bus, independent subsystem RNG streams, guaranteed replay after save/load, wholesale immutable copies of the campaign, and unrelated UI redesign. Preserve TDD §8.13's production RNG policy. Preserve enumeration and random-draw ordering during mechanical refactoring wherever possible; explain and investigate any observed change.

## 2. Sources and current starting points

Read `AGENTS.md`, `Design/README.md`, and the relevant TDD/PRD sections before each ticket. Paths below are repository-relative starting points; recheck them at handoff because earlier tickets may move code.

| Existing seam or coupling | Starting point | Implication |
|---|---|---|
| Explicit session and turn processors | `Helpers/Simulation/GameSession.cs`, `Helpers/TurnController.cs`, `Helpers/Turns/` | Build on the split; narrow access rather than restart it. |
| Established battle collaborators and aftermath sinks | `Helpers/Battles/BattleExecutionContext.cs`, `Helpers/Battles/Aftermath/`, TDD §6.6 | Prioritize the campaign boundary; retain tactical sequencing and planning lifetimes. |
| Squad deployment policy in presentation code | `Helpers/UI/SquadRowViewModels.cs`, `Helpers/PlanetaryOperations/OrderMutationService.cs` | Move authoritative readiness out of UI. |
| Readiness fallback to current singleton | `Helpers/DutyReadinessService.cs` | Explicit inputs must replace hidden session selection. |
| Rules catalog coupled to storage | `Models/GameRulesData.cs` | Separate in-memory rules from database loading before project extraction. |
| Generation publishes incomplete state for warm-up | `Builders/SectorBuilder.cs`, `Builders/ScenarioBuilder.cs`, `Models/GameDataSingleton.cs` | Application must coordinate generation, simulation, and final publication. |
| Load/save chooses current campaign through globals | `Helpers/Storage/CampaignLoader.cs`, `CurrentCampaignSaveWriter.cs` | Separate persistence adapters from application session management. |
| Godot dependencies outside scenes | `Models/Planets/Subsector.cs`, `Builders/SubsectorBuilder.cs`, `Helpers/VoronoiSubsectorMapper.cs`, presentation helpers | Identify engine types and move or adapt them at the host boundary. |

TDD §8.14 already records the completed `PlanetTurnProcessor` decomposition. Do not reopen it based on class size. TDD §8.15 identifies the remaining narrow turn compatibility surface; remove it only after migrating its real callers.

## 3. Ownership and dependency rules

These are target responsibilities, not claims about current implementation.

| Owner | Exclusive responsibility | Cross-boundary interaction |
|---|---|---|
| Battles | Tactical state, actions, morale, withdrawal, battle-local injury execution and history | Explicit engagement input and typed outcome; narrow medical primitives; campaign effects through an adapter outside tactical implementation. |
| Planetary Operations | Order lifecycle, commitments, regional eligibility, mission scheduling and operational consequences | Readiness queries, battle resolution, and transport/posting capabilities. Does not implement treatment policy or UI formatting. |
| Readiness/Medical | Duty policy, medical eligibility, reservations, care, healing and recovery transitions | Explicit personnel/health inputs; typed decisions and results. Recruitment supplies reservation facts rather than requiring a dependency on its full workflow. |
| Persistence | Rules/save SQLite adapters, mapping, validation, format migration and reconstruction | Explicit catalogs/state and file paths; returns a fully reconstructed result or failure. |
| Generation | Initial world/force construction and generation policy | Explicit rules/options/RNG/ID allocation; returns constructed state. Runtime entity creation has a lower-level home shared with its runtime callers. |
| UI/Godot host | Scenes, rendering, selection, navigation, input and engine adapters | Application commands and read projections; host startup composes concrete implementations. |
| Campaign/Application | Session lifetime, turn ordering, cross-domain workflows, publication and report coordination | Calls subsystem contracts in explicit sequence; owns remaining campaign policies through focused collaborators. |

The shared domain/contracts layer may contain identity, coordinates, dates, health/equipment primitives, and deliberately shared campaign data. It must not become a relocated `Helpers` directory. Inventory shared mutable entities explicitly: identify who can mutate each relevant part, how another module obtains permitted access, and which adapters enforce that permission. A read-only collection of writable entities is not a safe UI projection.

Allowed dependency direction:

- Shared domain/contracts depend on neither application, feature implementations, Godot nor SQLite.
- Feature implementations depend on shared types and explicit lower-level contracts. They never depend on the Godot host or current-session singleton.
- Operations can consume battle and readiness contracts; Medical cannot reach back into Operations implementation. Put cross-domain coordination above both.
- Runtime simulation must not depend on the top-level new-game generator. Extract runtime factories from `Builders` before introducing this restriction.
- Application workflows consume subsystem contracts. The host composition root may reference concrete adapters to wire them together; scene controllers may not use that privilege to bypass commands.
- Persistence implements storage contracts and may understand shared save/domain representations. Other subsystems do not issue SQL or depend on its implementation.

Ticket SB-00 records the concrete assembly graph and ownership of remaining campaign domains such as fleets, recruitment, supply, diplomacy and narrative. They remain in a named campaign module or focused existing collaborators; they must not be silently absorbed into Planetary Operations or the shared domain. Contract names in this plan are descriptive, not mandated interface names. Do not add interfaces for every class.

### Mutation and orchestration requirements

- Separate deployment eligibility from the ability to continue an active battle. Freeze participants at the existing engagement boundary and retain injury-driven tactical capability updates.
- Keep wound mechanics reusable without letting the medical scheduler own the battle loop. No simultaneous second authoritative soldier/health roster.
- Preserve day versus week healing and field-care order. Medical rules determine transitions; the appropriate mission/turn coordinator determines when they run.
- Validate compound commands before mutation where feasible. Rejected orders, medical transfers and treatment commands must leave no partial commitment, posting, capacity or resource changes. Preserve successful undo behavior; record any limitation requiring a separate product decision.
- Resolve session state when a command executes. Do not retain commands or projections pointing into a campaign that has been replaced by load/new game.
- Keep battle and campaign phase ordering inspectable in their coordinators. Move embedded policy, not sequencing solely to shorten a file.
- The current campaign must survive a failed load or generation attempt. Install the replacement only once construction, warm-up, reconstruction and validation succeed.

### 3.1 SB-00 decision: concrete assembly graph

This was the proposed destination, inspected against revision `360e3ec8f6c5064b2f415bba1dbe2b9e958655af` on 2026-09-03. SB-10 has now introduced the ten headless assemblies listed below alongside the root `OnlyWarGodot` host (`Godot.NET.Sdk/4.7.0`, `net8.0`). Keep the existing `OnlyWar` namespace root; namespaces need not change solely because sources move.

New projects live at `Modules/<assembly>/<assembly>.csproj`, using `Microsoft.NET.Sdk` and `net8.0`. The existing root host project remains `OnlyWarGodot.csproj`. In the matrix, references are an exhaustive **direct project-reference allowlist**, not permission to use every public member transitively. `OnlyWar.Contracts` groups small boundary contracts by owner (`Medical`, `Battles`, `Operations`, `Campaign`, `Storage`, `Generation`, `Application`); it contains no service implementations or current-session locator.

| Assembly | Allowed direct project references | Owns / boundary |
|---|---|---|
| `OnlyWar.Domain` | None | Identity, neutral coordinates, dates, body/wound/equipment primitives, template/catalog data, deliberately shared campaign entities listed in §3.3. No feature orchestration. |
| `OnlyWar.Contracts` | `OnlyWar.Domain` | Explicit inputs, decisions, immutable report/view values, narrow capabilities, `IRNG`, ID-allocation contracts. No contract refers to an implementation type. |
| `OnlyWar.Runtime` | `OnlyWar.Domain`, `OnlyWar.Contracts` | Runtime soldier/squad/force construction, name allocation, persistent and tactical ID implementations, deterministic topology/governance reconstruction. Shared by generation and live simulation. |
| `OnlyWar.Medical` | `OnlyWar.Domain`, `OnlyWar.Contracts` | Individual/squad readiness, treatment/procedure eligibility, reservation decisions, field care and daily/weekly healing. Receives staffing, posting, recruitment and capacity facts. |
| `OnlyWar.Battles` | `OnlyWar.Domain`, `OnlyWar.Contracts` | Tactical state, planning, actions, wounds, morale, withdrawal, battle-local casualty disposition and replay. Uses medical primitives/contracts, never a medical scheduler. |
| `OnlyWar.Operations` | `OnlyWar.Domain`, `OnlyWar.Contracts`, `OnlyWar.Runtime` | Order/attachment policy, regional eligibility, mission execution/day scheduling, strategic combat and operational consequences. Calls battle/readiness/transport contracts. |
| `OnlyWar.Campaign` | `OnlyWar.Domain`, `OnlyWar.Contracts`, `OnlyWar.Runtime`, `OnlyWar.Battles`, `OnlyWar.Medical`, `OnlyWar.Operations`, `OnlyWar.Persistence`, `OnlyWar.Generation` | Fleet, postings, roster/training/loadouts, recruitment, supply/governors, diplomacy/intelligence, planetary ecology/control, faction strategy/invasions, scenario resolution, narrative policies and the transitional live-model/persistence adapters. See §3.2. SB-12 tightens these direct references toward the final feature allowlist. |
| `OnlyWar.Persistence` | `OnlyWar.Domain`, `OnlyWar.Contracts` | Rules/save SQLite loading, mapping, reconstruction/validation and atomic files. Calls an injected derived-state reconstruction port; does not reference Generation, Runtime or Application. |
| `OnlyWar.Generation` | `OnlyWar.Domain`, `OnlyWar.Contracts`, `OnlyWar.Runtime` | Initial sector/chapter construction and authored scenario stamps. Receives training and scenario support ports; never references runtime campaign orchestration. |
| `OnlyWar.Application` | `OnlyWar.Domain`, `OnlyWar.Contracts`, `OnlyWar.Runtime`, `OnlyWar.Campaign`, `OnlyWar.Battles`, `OnlyWar.Medical`, `OnlyWar.Operations`, `OnlyWar.Persistence`, `OnlyWar.Generation` | Session lifetime, command/query dispatch, cross-owner workflows, turn/planet sequencing, scenario warm-up and report coordination. SB-10 uses these direct references as the transitional composition surface; SB-12 narrows them to the final application allowlist. |
| `OnlyWarGodot` | All ten assemblies above | Scenes, presentation helpers, engine adapters and a single composition root. Concrete feature references are startup wiring privileges only. |

Topological order remains the target: Domain → Contracts → Runtime → Medical/Battles/Operations/Campaign/Persistence/Generation → Application → Godot host. SB-10 introduces the Application/Campaign composition seam and removes Engine; its current direct feature references are a transitional bridge for the live model and persistence adapters. Feature-to-feature implementation references and the broader application graph remain SB-12 enforcement work. The explicit ports already prevent the prior Operations↔Battles, Medical↔postings/recruitment, Persistence↔Generation and Generation↔turn-simulation cycles in the extracted policy modules.

SQL is permitted only in Persistence; Godot only in the host. Framework-only primitives/calculations needed by Domain move with their callers or become explicit inputs; Domain must not reference Contracts to obtain policy services. A method requiring a policy service belongs in that policy's owner. Do not move all `Models` into Domain or all `Helpers` into Campaign.

**Incremental introduction:**

1. SB-01/02 remove readiness/presentation and catalog/storage inversions within the current project. SB-03a inventories the exact neutral source set, including the model/helper inversions in §3.5. SB-03b creates Domain, Contracts and temporary `OnlyWar.Engine` for the remaining portable code. Engine may temporarily contain SQLite adapters and feature wiring; it references Domain/Contracts, never the host. Host-dependent paths/presentation stay outside. This is a build seam, not a claim of completed feature boundaries.
2. SB-04 and SB-07 extract Medical and Persistence. For SB-07, Engine supplies the derived-state port until SB-08 moves its implementation into Runtime. Thus SB-07 and SB-08 retain their existing independent dependencies; neither must wait for the other. SB-08 extracts Runtime and replaces runtime uses of top-level builders. An extracted module may never reference Engine.
3. SB-05b/06/09 extract Operations, Battles and Generation. Engine temporarily supplies their application adapters; move any shared signature types to Domain/Contracts first. SB-09 must split scenario stamps from simulation and supply candidate-session orchestration even while it is physically in Engine.
4. SB-10 partitions the remaining Engine sources into Campaign policies and Application coordination, moves the adapters to their final locations, and removes Engine. SB-11 removes scene bypasses. SB-12 enforces the matrix and removes remaining compatibility entry points. Do not defer an incomplete feature extraction to the catch-all enforcement ticket.

SB-03b must exclude `Modules/**/*.cs` (including nested `obj` output) from the root host's default compile glob, keep each moved source compiled exactly once, and retain the test exclusions. Soldier-name resources move with the Runtime name provider at SB-08; until then they live in Engine, with logical names `OnlyWar.SoldierNames.Given` and `OnlyWar.SoldierNames.Surnames` preserved and lookup based on the owning assembly. Keep SQLite packages with storage sources until SB-07. The current `InternalsVisibleTo("OnlyWar.Tests")` is test-only; replacing it with production friend grants is not an extraction strategy. SB-03b owns explicit headless test references/smoke; SB-13 owns export inclusion of the new assemblies and the loose rules DB.

### 3.2 Existing collaborators and remaining campaign ownership

Locations in this table are current; destinations are relative to the corresponding proposed project. A coordinator can keep its name. Do not reopen the completed planet or battle planning decompositions to achieve cosmetic size reductions.

| Current collaborators / domain | Final owner and location | Boundary decision / migration owner |
|---|---|---|
| `GameSession`, `TurnController`, `SimulationContext`, `PlanetForwardSimulator` | Application: `Sessions/`, `Turns/`, `Generation/` | Session is application-owned. Feature processors receive bounded phase inputs, not `GameSession`. Per-run aggregation belongs to Application; feature working state stays private. SB-09/10. |
| `PlanetTurnProcessor`, `ChapterUpkeepProcessor`, `FleetTurnProcessor` | Application: `Turns/` | Keep visible planet phase order, medical-before-training order and warp-exit training coordination. Delegate their policies to Campaign/Medical. SB-04/10. |
| `TurnOrderPlanner`, `FactionStrategyController`, all `Helpers/Strategy/` collaborators | Campaign: `Strategy/` | NPC planning, staging and transient-force lifecycle remain Campaign policy; submit operational intents through Contracts. SB-08/10; retire default facade in SB-12. |
| `MissionTurnProcessor`, `MissionAftermathProcessor`, `Helpers/Missions/`, `Builders/MissionStepOrchestrator`, `Helpers/StrategicCombat/` | Operations: `Missions/`, `Orders/`, `StrategicCombat/` | Mission/day ordering and region-level mission consequences; cross-owner event/recruitment/invasion effects via Application adapters. SB-05b. |
| `BattleTurnResolver`, `BattleState`, `BattleSquad`, `BattleSoldier`, planning/actions/placers/resolutions | Battles: `Execution/`, `Planning/`, `Aftermath/`, `Replay/` | Keep planning context/cache lifetimes and frozen participants. Campaign equipment/participants supplied at engagement entry; no fallback to active game. SB-06. |
| `PlayerChapterBattleAftermathPolicy`, `NpcBattleAftermathPolicy`, aftermath factory/context | Battles plus Application: `Adapters/Battles/` | Battle-local casualty/experience decisions stay tactical. Move campaign roster, gene-seed, history and narrative application from `PlayerBattleAftermathSink` to the external adapter. Event context is explicit values, not live Order/Region access. SB-06. |
| `DutyReadinessService`, readiness/strength types in `Helpers/UI/SquadRowViewModels.cs`, medical/procedure/field-care services | Medical: `Readiness/`, `Treatment/` | Typed facts/reasons in Contracts; colors/text and row composition remain host. SB-01/04. |
| `IndividualPostingService`, `SoldierPresenceService`, `CharacterAvailabilityService`, `CampaignLocationService`, `AdministrativeStationService` | Campaign: `Personnel/` | Own physical posting/presence and administrative stations; Operations owns order commitments; Medical supplies health/reservation decisions. SB-04 defines the port, SB-10 final placement. |
| `SoldierTransferService`, `MusterPlanService`, `SquadLifecycleService`, training/rating/role/loadout services | Campaign: `Personnel/`, `Training/`, `Equipment/` | Own roster identity, formation lineage, training/promotion and equipment allocation; presentation builders stay host. Generation consumes explicit training/role services. SB-08/09/10. |
| `FleetRouteCalculator`, `ShipCapacityService`, `FleetCapacityPlanService`, `FlagshipService`, fleet reorganization | Campaign: `Fleets/` | Fleet travel, passenger capacity and manifests; cross-owner embark/evacuate commands coordinated above. SB-05a/10. |
| `RecruitmentTurnProcessor`, `Helpers/Recruitment/`, `OrganicPopulationGrowthLedger` | Campaign: `Recruitment/`, `Population/` | Pipeline, staff/Black Carapace reservations, promotions and population accounting. Medical receives reservation facts, not `RecruitmentProgram`. SB-04/08/10. |
| `GovernorTurnProcessor`, `ChapterSupplyTurnProcessor`, `Helpers/Supply/`, request policy | Campaign: `Supply/`, `Governance/` | Requests, pledges, appreciation, Requisition and deliveries. `RequestFactory` is runtime construction; selection/valuation remains Campaign. SB-08/10. |
| `PlanetDemographicsProcessor`, `ConsumptionTurnProcessor`, `ConversionTurnProcessor`, `RegionControlTurnProcessor`, `CivilUnrestTurnProcessor`, `DormantPopulationProcessor` | Campaign: `Population/`, `WorldControl/` | Planet ecology, cults, civil stability and territorial state. Operations produces operational effects rather than taking ownership of the living galaxy. SB-10. |
| `FactionRelationshipService`, `FactionIntelligenceService`, `PlanetIntelligenceProcessor`, `TurnIntelligenceLedger`, reveal services | Campaign: `Diplomacy/`, `Intelligence/` | Relationship/awareness/belief rules and opportunity generation. Operations supplies observations; Application sequences application and shares ledgers. SB-05b/10. |
| `FactionCapabilityCampaignProcessor`, `StrategicInvasionLifecycleProcessor`, `InvasionGenerationProcessor`, `GhostPlanetSeeder`, `InvaderPresenceService` | Campaign: `Factions/` | Capability state, ghost sources, persistent invasions and footholds. Generation uses explicit seeding/initial-invasion ports; Operations uses consequence ports. SB-08/09/10. |
| `ScenarioTurnProcessor`, `ScenarioMetricsCollector` | Campaign: `Scenarios/`; optional diagnostic sink composed by host | Runtime objective/reward policy differs from authored Generation stamps and Application warm-up. Move `SectorBuilder.ReplaceChapterPlanetFaction` to Campaign world-control policy. SB-08/09/10. |
| `Models/Events/` recorders/projectors/narrators, `BriefingComposer`, report builders | Campaign: `Narrative/`; Application: `Reports/`; host: presentation | Event payload/ledger storage and stable report values may be shared; emission/classification policy is Campaign. Application captures last-turn results; host renders and navigates replay. SB-06/10/11b. |
| `SectorBuilder`, `ScenarioBuilder`, `NewChapterBuilder`, `PlanetBuilder`, `CharacterBuilder` | Generation: `World/`, `Chapter/`, `Scenarios/` | Construction only; remove runtime policy and geometry helpers before extraction. SB-08/09. |
| `ForceGenerator`, `SquadFactory`, `SoldierFactory`, `RequestFactory`, name/ID providers, subsector/warp builders and mapper | Runtime: `Factories/`, `Identity/`, `Names/`, `WorldGeometry/` | Force generation returns ordinary squads, never tactical wrappers; its `Helpers.Battles` import is unused. Pure template battle-value arithmetic does not require a Battles reference. SB-08. |
| `Helpers/Database/`, `SavedGameLoader`, storage catalog/manager | Persistence: `Rules/`, `Saves/` | Explicit reconstruction and file operations. Extract shared reservation-rebuild primitives or inject a validator; `SavedGameLoader` must not reference Medical implementation. Session/slot workflow selection moves to Application. SB-02/07/10. |
| `Scenes/`, `Helpers/UI/`, operations/recovery/muster/dossier view builders, navigation/formatting/settings | Godot host: existing scene folders plus `Presentation/`, `Adapters/`, `Composition/` | Engine types, selection, navigation and presentation are host-owned even if currently under Helpers/Models. SB-03a/11. |

### 3.3 Shared mutable data and permitted access

Shared data location does not confer shared write permission. The initial Domain move can retain entity representations for save compatibility, but subsequent tickets must narrow setters/collections or expose owner-controlled mutation capabilities. Persisted reconstruction is a separate construction path operating on an unpublished candidate. A host query returns copied values/IDs and a session identity, never `Sector`, `Squad`, `ISoldier`, `Order`, `Ship`, writable body parts, or a read-only list containing those objects.

| Data currently shared | Authoritative writer after migration | Permitted cross-owner path |
|---|---|---|
| `GameRulesData`, templates and faction definitions | Persistence constructs/validates; Domain holds in-memory catalog | Explicit catalog/projections. SB-02 isolates mutable `Faction.Units`: currently new/load registers roots there and `CurrentCampaignSaveWriter` enumerates them. Keep a campaign-owned faction/unit registry with the same identity links; never share runtime faction graphs between candidate and active sessions. |
| `Sector`, `PlayerForce`, `Army`, `Date`, active session/recoverability | Application owns root/session/date installation; Campaign owns named fields below | Resolve commands against the current session at execution. Provide each feature only its state/capability slice. Save receives an explicit stable capture during the single-threaded workflow. |
| Soldier body, hit locations, wounds, casualty state | Battles applies engagement wounds/casualty disposition; Medical applies care/healing/procedure transitions at coordinator boundaries | Same authoritative body, bounded health operations and explicit phase access. No duplicate medical roster; tactical capability may change inside an engagement, deployment doctrine is not rechecked there. |
| Medical procedures/reservation flags | Medical owns replacement procedures; Campaign recruitment owns pipeline reservations | Unified reservation facts into readiness. Application coordinates posting/resource debits with procedure creation. Persistence restores facts without invoking treatment policy. |
| Active/fallen rosters, units, squads, ranks, training, loadouts and lineage | Campaign personnel/equipment/training services | Battle aftermath adapter applies identified casualty/experience facts once. Runtime constructs new entities without registering them; Campaign registers promotions/recruits. Battle-local ammunition/equipment usage stays on the same issued equipment graph. |
| `Order`, mission assignment, `Squad.CurrentOrder`, `PlayerSoldier.CurrentOrder`, sector order index | Operations order service | Commands validate all commitments and update forward/reverse links together. Campaign planners/recruitment request changes through Operations contracts. Persistence reconstructs references; host retains only IDs/undo tokens. |
| Physical squad locations, `IndividualPosting`, ship passenger/embarkation lists, regional landed lists | Campaign posting/fleet services | Application movement/evacuation adapters coordinate with Operations to release commitments and with Medical to validate casualties/reservations; no module edits another owner's lists directly. |
| Fleets/task forces, routes, flagship and capacity | Campaign fleet services | Explicit transport queries/commands. Application turn coordinator invokes warp-exit training at the existing point. |
| Planet/region populations, garrisons, defenses, control, invasion/ghost state | Campaign population/world-control/faction services | Operations reports typed deltas/outcomes through a campaign-effect port. Preserve sequential application and existing invariants such as garrison ≤ population. Generation/Persistence initialize candidate state only. |
| Relationships, directed beliefs, intel ledgers, special opportunities | Campaign diplomacy/intelligence services | Mission/battle observations enter a narrow sink; turn coordinator applies decay, gains and sharing once. Operations owns the lifecycle of orders consuming opportunities. |
| Recruitment program, applicants/cohorts, staff, geneseed stocks/purity | Campaign recruitment/resource services | Medical sees reservations/staff facts; battle casualty adapter submits recovery facts; application coordinates resource changes. |
| Requests, pledges, Requisition, governor state and scenario rewards | Campaign supply/governance/scenario services | Operations submits mission completion facts; Medical requests validated resource spend. No treatment implementation takes ownership of the supply economy. |
| Scenario state, capitals/subsectors/warp lanes | Campaign updates objective state; Runtime rebuilds derived topology/governance | Generation authors initial scenario data; Application sequences warm-up/rebuild. Load uses the same deterministic rebuild port without calling new-game generation. |
| Campaign event ledger, chronicle, world-control episodes, report snapshots | Campaign event policy; Application report capture | Battles/Operations return facts through explicit sinks. Host gets detached report/replay projections. Preserve correlations, chronology and one event per application. |
| `BattleState`, planning caches, `BattleHistory`, `MissionContext`, replay/action snapshots | Battles or Operations implementation, not shared Domain working state | Split transportable battle outcomes/replay values and mission report contracts from implementation graphs. Existing `BattleTurn`, `BattleStateSnapshot` and `MissionContext` imports prove they cannot move wholesale to Domain. |

The last row also governs `TurnResolutionResult`: its current `MissionContext`/battle graphs are transient implementation data, not a safe public application response. SB-05b/06 define detached outcome contracts; SB-10 keeps working aggregation private and returns report data. Existing snapshot cloning shares underlying `ISoldier` deliberately; retain battle behavior but build a separate host-facing replay projection rather than declaring that clone immutable.

### 3.4 Named workflow coordinators and migration contracts

Proposed contract names below describe intent; introduce interfaces only where an assembly boundary needs one. Ports live in Contracts; Application adapters live at `Modules/OnlyWar.Application/Adapters/<owner>/`. Before SB-10 they can live in `Helpers/Application/Adapters/` compiled into Engine, with the same dependency direction. Host startup composition is `Composition/GameCompositionRoot.cs`; scene controllers receive the application API, not the service registry.

| Flow | Existing entry / coordination | Destination and required contract |
|---|---|---|
| Order issue/cancel/undo and operational embark/land | `OrderMutationService`, `OrderAssignment`, `OrderAttachment`, `OrderForceService`, `PlanetForceMovementService`; undo closures and multi-step `RestoreOrder` in `PlanetaryOperationsScreenController` | Application `Commands/OperationsCommandCoordinator` resolves session/IDs and calls Operations. `OrderCommand`/`OrderDecision` include expected session and typed blockers; an owner-issued undo token carries original commitments and is revalidated as one command. Fleet/posting adapters supply movement capabilities. SB-05a, final UI migration SB-11a. |
| Casualty evacuation, recovery plan, procedure/rejoin | `MedicalDetachmentService.DetachToOrbit`, `RecoveryPlanService.Commit/Rejoin`, `CareDestinationService`, `IndividualPostingService`, `MedicalProcedureService` | Application `Commands/RecoveryCommandCoordinator`, adapters `Medical/` and `Transport/`. Medical evaluates patient/procedure/reservation facts; Campaign evaluates posting/staff/capacity/resources; Operations evaluates commitment release. Validate the complete plan before applying it or roll back all applied changes. No Medical→Operations/Campaign implementation reference. SB-04 defines contracts, SB-05a integrates movement, SB-10 owns final coordinator. |
| Mission → battle → campaign aftermath | `MissionTurnProcessor`, mission steps, `BattleExecutionContext`, `BattleTurnResolver`, aftermath policies/sinks, `MissionAftermathProcessor` | Operations owns engagement scheduling; Application `Adapters/Battles/CampaignBattleAdapter` implements battle invocation/effect ports. `EngagementInput` carries frozen participant identities, scoped live combat access, equipment, rules, RNG and event context; `BattleOutcome` carries tactical results/replay facts. `BattleCampaignEffectsAdapter` applies roster/geneseed/history facts once; mission consequences remain Operations, other campaign effects use its adapter. SB-05b/06, integrated SB-10. |
| End turn and care cadence | `TurnController`, `MissionDayScheduler`, `ChapterUpkeepProcessor`, planet/fleet/scenario processors | Application `Turns/TurnController` preserves the outer sequence; Operations scheduler preserves daily sequencing and calls Medical via its port. Daily Astartes healing precedes field care; field care runs per distinct order, not per mission element; garrison daily/field care precedes weekly healing and procedure completion. Preserve per-turn recovery-event detection and training-day credit. SB-04/05b/10. |
| Save / selected load | Start menu and `MainGameScene.CampaignControls` → `CurrentCampaignSaveWriter` / `CampaignLoader.LoadIntoSingleton` / `SavedGameLoader` | Application `Sessions/CampaignSessionCoordinator` captures explicit save data or requests `LoadCampaign(path, catalog)` returning a reconstructed candidate/date/catalog/ID high-water marks. Persistence handles atomic files and validation; Runtime rebuilds derived geometry via a port. Only Application installs a successful candidate and clears upgrade/recoverability flags at the existing success boundary. SB-07/10. |
| New game and scenario warm-up | `GameDataSingleton.InitializeNewGameData` → `SectorBuilder.GenerateSector` → `ScenarioBuilder.StampPromisedWorld` → default `TurnController` | Application `Generation/NewCampaignCoordinator` owns candidate lifetime; `Generation/ScenarioWarmupCoordinator` runs explicit candidate-session simulation between authored stamps. Generation receives options/catalog/RNG/IDs plus training/seeding ports. Sequence: construct worlds/chapter → topology/governance → ghost seed → infiltrator/reveal → pre-landing sim when applicable → invader stamp/initial invasion → post-landing sim → fleet/authority/briefing/founding/chronicle → validation → install. Warm-up never advances the campaign date or runs player upkeep, fleet travel or scenario resolution. SB-09, final host installation SB-10. |

Commands/projections carry a session identity and stable entity IDs. Session identity guards stale selections; entity IDs alone are insufficient because a new campaign can reuse them. Application resolves the live graph when executing, rejects an obsolete token, and invalidates host views on replacement. Persistent ID high-water marks must be staged during load rather than installed while reading individual tables. Preserve TDD §8.13: explicit RNG injection does not promise independent streams, concurrent sessions or replay after load. Candidate failure must preserve the active campaign graph and allocator state; stream reset/consumption behavior must be recorded without inventing a new replay guarantee.

### 3.5 Inspection inventory and compatibility exits

The audit used `rg` over `Models`, `Helpers`, `Builders` and `Scenes`; paths below are concrete starting points, not a permanent global-access allowlist. Comments mentioning RNG are not runtime calls. Re-run the searches at each extraction because the existing TDD global-debt summary is broader than the present code supports.

| Current coupling / bridge | Evidence and required replacement | Implementation / removal owner |
|---|---|---|
| Readiness/presence defaults | `DutyReadinessService`, `SquadRowViewModels`, `SoldierPresenceService`, `CharacterAvailabilityService`, `IndividualPostingService` infer doctrine/reservations from `GameDataSingleton`. Orders and loadout services call those defaults. | SB-01 explicit readiness inputs; SB-04 posting/reservation port; SB-10 campaign callers. SB-11c made every screen projection pass the program and doctrine into `SquadRowViewModelBuilder.Build` explicitly; `BuildBattleSnapshot` (a historical formation with no live force) is the fallback's last caller, so removing it is SB-12. |
| Additional campaign/global consumers | `LoadoutDoctrineService`, `CharacterLoadoutService`, `CareDestinationService`, `RecoveryPlanService`, `RecruitmentStaffService`; `Models/Sector.cs` event date, `Request.cs` player faction/doctrine, `PlayerSoldier.cs` posting date. | SB-04 medical workflows and SB-10 Campaign policies receive explicit context; remove domain-to-session selection. |
| ~~Operations globals~~ **resolved 2026-09-05** | Was: `OrderAssignment`, `OrderAttachment`, `OrderForceService`, `InboundOrders`, `SpecialistAvailability`, `PlanetForceMovementService`; the assault/detected/assassination/exfiltrate mission steps; `StrategicCombatResolver` reading persistent invasion forces. All now take explicit campaign inputs (`OrderCommandContext`, campaign date, sector, roster, invasion forces), and readiness inputs resolve through `ForceReadinessInputs` against a named force rather than `CurrentCampaignReadinessContext`. | Done in SB-05a/b. `PlanetaryOperationsScreenController` stopped being the mutation adapter on 2026-09-06 (SB-11a): commands go through `IOperationsScreenApplication`, and its remaining campaign access is read-only presentation, exiting with the Operations projection work. `StrategicCombatResolver` still defaults its RNG to `StaticRNG` (SB-10). |
| Tactical globals | `BattleSquad` equipment setup/refresh and `CharacterLoadoutService` use resolved through an explicit `IBattleEquipmentSource` — **resolved in SB-06**. `BattleAftermathDependencies.CreateBattleEventContext` still accepts live Region/Order, while Operations retains only transitional `BattleSquad` handles behind `IEngagementResolver`. | SB-06 done for equipment/participants; remaining event-snapshot and handle projection work is tracked by SB-12. |
| Current-session composition | Default `TurnController` constructors, parameterless `FactionStrategyController`, `GameDataSingleton` new/load/clear APIs. Generation no longer uses any of them (SB-09, 2026-09-05); the remaining callers are the host and turn entry points. | SB-10 composition/session API; SB-12 removes default engine entry points after actual callers migrate. `TurnController.Compatibility.cs` has only three result accessors and `ProcessScenario`; audit those real callers, not already-deleted shims. |
| ~~Generation publication and runtime builder calls~~ **resolved 2026-09-05** | `SetSectorDuringGeneration` is removed; `OnlyWar.Generation` receives every campaign capability as a port and cannot reference turn simulation at all; `InitializeNewGameData` installs rules/date/sector only after generation returns; `ScenarioTurnProcessor` now calls `ChapterHomeworldService`; `SoldierFactory`, topology builders, `ForceGenerator` and `SquadFactory` are Runtime-owned. Still open: `GovernorTurnProcessor` uses `RequestFactory`; SB-10 removes the host generation bridge. | SB-05b-2 moved the force factories to Runtime; SB-10 owns the remaining host bridge. |
| Rules and persistence globals | `GameRulesData` constructor opens `GameStorage.RulesDatabasePath` via `GameRulesDataAccess.Instance`. `CampaignLoader` constructs rules, rebuilds through SectorBuilder and installs singleton; `CurrentCampaignSaveWriter` reads active state and clears `UpgradePending`. | SB-02 explicit catalog loader; SB-07 explicit load/save/rebuild contracts; SB-10 installation and post-save status in Application. Static data-access entry points must not select a session or retain candidate data. |
| Static RNG calls | Executable `RNG.*` calls in `SectorBuilder`, `ScenarioBuilder`, `PlanetBuilder`, `NewChapterBuilder`, `CharacterBuilder`, `NameGenerator`, `FleetRouteCalculator`, `FieldCareService.BuildTieBreak`; `StaticRNG` delegates to RNG. Additional `StaticRNG.Instance` defaults occur in turn/strategy/strategic combat and generation; Chapter controller passes it for training. | SB-04 field-care IRNG (keep soldier-ID tie-break draw order); SB-08 name provider; SB-09 generation; SB-10 fleet/campaign; SB-11c host commands. Final `RNG`/`StaticRNG` production adapter composed only at `Composition/GameCompositionRoot.cs`, checked SB-12. |
| Persistent and tactical IDs / static name state | `Order` and `Mission` convenience constructors call `IdGenerator`; save readers `UnitDataAccess`/`PlanetDataAccess` set counters. `SoldierFactory` and `RequestFactory` singleton counters are updated by `GameStateDataAccess`/`RequestDataAccess`. `Unit`, `Squad` and `TaskForce` constructors allocate/advance static counters, including during reconstruction; `Boat` in `Models/Fleets/Ship.cs` allocates from a static counter starting at 10000. `PlanetBuilder` owns planet/leader counters and shuffled names; `NameGenerator` owns mutable shuffle bags. `TacticalEntityIdAllocator` allocates negative IDs and reserves -1. | SB-03 neutral contract seam, SB-07 stage all load high-water marks including constructor side effects, SB-08 Runtime providers and explicit model IDs, SB-09 candidate generation state, SB-10 install. Preserve positive-vs-negative identity behavior and name/draw ordering; no thread-safety claim. |
| Domain-to-helper dependencies | `MissionContext` imports UI readiness and concrete battle/mission types and stores `FieldCareReport`; `BattleTurn`, `BattleStateSnapshot`, `SquadEngagementModels` import tactical implementation; `Models/Command/CommandWorkspaceModels` imports turn types. `Unit`, `Planet`, `PlanetFaction`, `Region`, faction profiles/intelligence and generation doctrine also import Helpers. Qualified calls add `Ship`→capacity/presence, `Squad`→`OrderAttachment.Detach`, `Request`→governor policy and `PlayerSoldier`→location services. | SB-01 removes readiness UI types; SB-03a classifies actual members, moving intrinsic primitives down and service-dependent methods to owners. SB-04 moves field-care report values to Contracts. SB-05b/06 split runtime contexts from DTOs; SB-10/11 relocate command projections. No Domain→Engine bridge. |
| Godot outside scenes | `Subsector.Cells`/builders/`VoronoiSubsectorMapper` use `Vector2I`; SectorBuilder creates Godot grid dimensions. `RegionFaction` has a Godot import to audit/remove. Engine presentation appears in operations view/tree models, UI hierarchy/palette/style/icons, `ColorExtensions`, settings and `GameStorage`. | SB-03a neutral cell values preserve coordinate order/rounding; host presentation and filesystem-path adapter remain outside. `GameLog` already has an injectable sink, composed by `Scenes/GodotLogBridge`; keep engine logging callbacks out of domain code. |
| UI/session bypasses and live projections | Scene controllers broadly select/mutate current state; `SoldierDetailBuilder`, operations view/tree builders and command/squad view models also read globals. The Apothecarium and Planetary Operations controllers no longer mutate (2026-09-06); undo closures capturing writable orders/squads are gone, replaced by application-issued session-bound tokens. Operations presentation builders still read the live sector and singleton. | SB-11a operations/recovery **done**; SB-11b start/main/menu/reports/battle **done** (2026-09-06) — turn-report content moved to `TurnReportProjector` in Application and `MainGameScene`'s surviving singleton reads only resolve entities for unmigrated screens; SB-11c remaining roster/fleet/training/command/diplomacy/inspectors and `SectorMap`. SB-12 exact startup allowlist and negative dependency fixture. |

**Observed risks, not Phase 0 behavior changes:** `RecoveryPlanService.Commit` moves staff/patient before `TryAssign`; a later rejection/exception returns failure without rollback. `PlanetaryOperationsScreenController.RestoreOrder` restores squads and specialists sequentially, so partial restoration needs characterization. `CampaignLoader` publishes only after reconstruction, but table readers already alter global ID counters. (New-game initialization no longer publishes an incomplete sector — SB-09 made it atomic on 2026-09-05, with failure coverage in `SectorBuilderTests`.) SB-04/05a/07 must add focused failure coverage and satisfy the existing all-or-nothing/session-preservation requirements; do not silently call the current paths transactional. These are tracked migration risks, not authorization for unrelated gameplay changes.

TDD reconciliation notes for later owners: §8.6 currently says tactical missions/battles no longer read the singleton, contradicted by the listed call sites; §6.1 lists compatibility properties that §8.15 and the current partial have already narrowed. SB-05b/06/10/12 correct the relevant facts as they migrate code. SB-00 leaves TDD/PRD implementation claims unchanged.

### 3.6 Behavior baselines and verification handoff

These are existing behavior/source baselines and **future targeted gates**, not claims that every listed suite passed during Phase 0. Test class names are under `OnlyWar.Tests`; use `FullyQualifiedName~<class>` with `Category!=Slow`, inspect nonzero counts, and select only the affected slice. PRD review for SB-00: §§4.1, 4.8, 4.10–4.19 and the relevant operations/force requirements reviewed; no PRD change required for this documentation-only ticket. The gaps above remain explicit acceptance work, not new product rules.

| Migration | Behavior to preserve / establish | Existing test targets and missing coverage to add in that ticket |
|---|---|---|
| SB-01 readiness | Inclusive worst-location threshold, physical/severance/procedure gates before doctrine, leader/minimum squad rules, individual attachments, frozen current engagement | `ChapterOperationalDoctrineTests`, `MissionAvailabilityTests`, `SquadRowViewModelTests`, `OrderAssignmentTests`; add detached/unrelated-session equivalence checks. |
| SB-02 catalog | Template identity, lookup defaults, validation/error behavior; runtime units not shared as immutable rules | `RulesDatabaseValidationTests`, `FactionGenerationPolicyTests`, `ChapterGenerationTemplatesTests`; add catalog construction without file access. |
| SB-03a/b headless | Geometry/cell/warp ordering, embedded-name lookup, exact-once compilation, host resource paths | `SubsectorBuilderTests`, `WarpLaneBuilderTests`, `NameGeneratorTests`, `SessionSimulationContextPrimitiveTests`; build host and headless projects, add representative host-free smoke. |
| SB-04 Medical | Daily healing before field care, distinct-order care, garrison care before weekly cascade/procedures, no auto-rejoin, unchanged resource/capacity on failure | `FieldCareServiceTests`, `AstartesDailyHealingTests`, `WoundHealingCadenceTests`, `MedicalTurnProcessorTests`, `MedicalProcedureServiceTests`, `IndividualPostingServiceTests`; add compound recovery-plan rollback/rejection coverage. |
| SB-05a orders/movement | One commitment, bidirectional membership, typed rejection, cancel/restore, no partial multi-entity undo or embarkation | `OrderAssignmentTests`, `OrderAttachmentTests`, `InboundOrdersTests`, `PlanetaryOperationsServiceTests`, `IndividualPostingServiceTests`; add session-stale and compound undo failure cases. |
| SB-05b missions | NPC planning before day execution; strategic → tactical → construction → feed; mission day budget, reciprocal engagements, one outcome application | `MissionDaySchedulerTests`, `MissionDayBudgetTests`, `MissionContinuationTests`, `ReciprocalAssaultCoordinatorTests`, `MissionOutcomeRecorderTests`, `MissionStepOutcomeSignalTests`; verify detached battle outcomes plus one turn integration slice. |
| SB-06 Battles | Participant freeze, physical injury updates, one death/geneseed/experience/event application, withdrawal/abandonment and replay continuity | `BattleRefactorCharacterizationTests`, `BattleAftermathPolicyTests`, `WoundResolverTests`, `BattleAbandonedWoundedTests`, `BattleTurnResolverWithdrawalTests`, `BattleStateSnapshotTests`, `BattleReplaySummaryBuilderTests`; preserve inert-battle errors, no balance sweep. |
| SB-07 Persistence | Format 19 exact-version guard, identity links, doctrine/orders/postings/procedures, deterministic rebuild, atomic-save recovery, failed load leaves active state/IDs intact | `SaveGameManagerTests`, `NewGameSaveTests`, `MissionSaveTests`, `DeploymentStorageTests`, `EquipmentDoctrinePersistenceTests`, `RecruitmentPersistenceTests`, `LastTurnReportDataAccessTests`; `SaveLoadRoundTripTests` has opt-in Slow coverage needed for the full graph. Add failed-load allocator/publication cases. |
| SB-08 Runtime | Force budgets/composition, template ordering, persistent vs negative tactical IDs, resource/name behavior, runtime scenario ownership | `EntityIdAllocationTests`, `ForceGeneratorTests`, `ForceGeneratorSynapseTests`, `NameGeneratorTests`, `RecruitmentForecastServiceTests`, `ScenarioTurnTests`; add runtime recruitment-construction coverage where absent and verify runtime projects cannot reference Generation. |
| SB-09 Generation | Founding assignment, seed/draw/ID ordering, scenario stamp/simulation order, no publication during warm-up, candidate failure isolation | `SectorBuilderTests`, `ScenarioBuilderTests`, `NewChapterBuilderTests`, `GovernanceHierarchyTests`, `SubsectorBuilderTests`; inspect Slow traits and add explicit candidate-session failure tests. Never enable trace diagnostics by default. |
| SB-10 Application/Campaign | Turn order shown in `TurnController`, medical → training → fleet → planets → recruitment/intel/supply → scenario/cleanup/events; preserve planet subphase order and growth/intel ledgers | `GameSessionTurnControllerTests`, `TurnControllerResultTests`, `TurnTrainingTests`, `FleetMovementTests`, `SectorEntityLogicTests`, `FactionStrategyCharacterizationTests`, `ScenarioTurnTests`, `CampaignEventSpineTests`; add session replacement/independent-session and failure tests. |
| SB-11a/b/c UI | Selection/reasons, order undo, evacuation/rejoin, save/load/new-game navigation, replay vs saved report behavior; immutable query values | `PlanetaryOperationsServiceTests`, `ApothecariumMedicalRecordBuilderTests`, `ChapterMusterViewModelBuilderTests`, `BattleReviewControllerTests`, `EndTurnPreflightTests`, `ChapterControllerTests`, `FleetScreenControllerTests`, `TrainingUnitScreenControllerTests`, scene-wiring smoke and ticket-specific manual flows. |
| SB-12/13 enforcement/package | No forbidden references/globals, no writable UI graph, loose DB and extracted assemblies/resources included in export | Add architecture negative fixture; run all project builds, headless smoke, final non-Slow suite and required Slow save/generation slices, then `Scripts/Publish-Windows.ps1` and release smoke per §6. |
| SB-14 reconciliation | Authoritative docs match shipped code; no unresolved requirement lost at retirement | Validate paths/section references, reconcile every bridge above, then remove this plan and active-index link together. |

## 4. Assignment, status and integration protocol

Each ticket below is intended as one focused handoff, normally one reviewable PR. If inspection shows a ticket cannot remain reviewable, split it in this document into numbered children with distinct deliverables and dependencies before implementation. Do not turn a ticket into an untracked multi-subsystem rewrite or leave compatibility scaffolding without an owner and removal ticket.

SB-03, SB-05 and SB-11 are already split below: assign their child tickets, not the entire parent. Parent dependencies mean all children are integrated; the parent is a roll-up milestone. Child tickets inherit the parent's behavioral, verification and documentation requirements for their own scope.

Status values: **Planned**, **In progress**, **Blocked**, **Ready for integration**, **Complete**. An implementation agent marks its work ready with evidence; the integrating agent marks complete only after the merged result passes its acceptance gate.

For each assignment, record the owner, starting revision, actual file scope, status, validation commands/results, and remaining issues in the ledger. Before parallel work, agree on shared contracts and exclusive file ownership. Use separate branches/worktrees if available. Each branch must build; adapters may bridge old callers temporarily, with removal tracked in SB-12.

Project/solution files, shared contracts, `TurnController`, the TDD, PRD, this plan, and `Design/README.md` are integration hotspots. One integrating agent owns those files during parallel work. Other agents supply their exact documentation edits and status/evidence in their handoff; the integrator applies them before marking the ticket complete. Sequential workers can update the documentation directly.

| ID | Phase / deliverable | Dependencies | Status | Owner / revision / evidence |
|---|---|---|---|---|
| SB-00 | 0: Ownership and contract map | None | Complete | Codex; start `360e3ec8f6c5064b2f415bba1dbe2b9e958655af`; integrated in working tree; scope/evidence below. |
| SB-01 | 1: Authoritative readiness policy | SB-00 | Complete | Codex; start `360e3ec`; explicit policy/contracts, callers and isolation tests; evidence below. |
| SB-02 | 1: Rules catalog independent of loading | SB-00 | Complete | Codex; start `360e3ec`; in-memory catalog, explicit rules/equipment loaders and identity validation; evidence below. |
| SB-03 | 2: Headless foundation and build seams | SB-01, SB-02 | Complete | Codex; integrated SB-03a/b in working tree; headless and host builds pass. |
| SB-03a | 2: Neutral types and host adapters | SB-01, SB-02 | Complete | Codex; neutral geometry, path/log adapters, source classification in `Modules/source-ownership.json`. |
| SB-03b | 2: Project/resource/test separation | SB-03a | Complete | Codex; Domain/Contracts/temporary bridge seam, solution and compile/resource ownership, separate headless smoke project. The temporary Engine project was removed at SB-10. |
| SB-04 | 3: Readiness/Medical module | SB-03 | Complete | Codex; `OnlyWar.Medical` headless policy module, Campaign/Application cadence and recovery adapters, 11 headless boundary tests. |
| SB-05 | 4: Operations command and mission boundary | SB-04 | Complete | Explicit-input work complete for SB-05a and mission execution; `OnlyWar.Operations` is extracted with Campaign/Application composition adapters and headless boundary coverage. |
| SB-05a | 4: Order/movement command policy | SB-04 | Complete | Atomic participant restore, session-bound cancellation token, and explicit `OrderCommandContext`/date inputs for every order, attachment and movement command; evidence below. |
| SB-05b | 4: Mission execution and Operations assembly | SB-05a | Complete | Split into SB-05b-1/SB-05b-2; explicit campaign inputs, engagement/readiness/personnel contracts, the Operations assembly, Runtime force factories and headless boundary coverage are complete. |
| SB-05b-1 | 4: Engagement, readiness and personnel contracts | SB-05a, SB-06 | Complete | Engagement resolver port, neutral participant projection, readiness decisions, field-care values, and Operations personnel surface delivered 2026-09-05; focused mission/order/movement coverage passes. |
| SB-05b-2 | 4: Operations assembly | SB-05b-1 | Complete | Codex; added `OnlyWar.Operations`, moved mission/order/planetary/strategic policy and mission turn/aftermath sources, moved force factories to Runtime, added explicit `MissionTurnDependencies`, Engine composition adapters and the headless negative fixture. Transitional Operations→Battles handles remain tracked for SB-12. |
| SB-06 | 4: Tactical battle and aftermath boundary | SB-04 | Complete | `Modules/OnlyWar.Battles` referencing only Domain/Contracts; explicit `IBattleEquipmentSource`, participants and aftermath sinks; negative reference fixture added. |
| SB-07 | 3: Persistence module | SB-03 | Complete | Codex; `OnlyWar.Persistence` atomic file/catalog/format module, explicit persistence contracts and headless boundary coverage. |
| SB-08 | 3: Runtime construction separated from new game | SB-03 | Complete | Codex; `OnlyWar.Runtime` factories/names/allocators, Campaign materialization façades and runtime independence coverage. |
| SB-09 | 4: Generation and scenario warm-up boundary | SB-08 | Complete | `Modules/OnlyWar.Generation` referencing only Domain/Contracts/Runtime; six explicit generation ports; candidate warm-up and atomic installation; negative reference fixture row added. Evidence below. |
| SB-10 | 5: Application/session workflow integration | SB-05, SB-06, SB-07, SB-09 | Complete | Codex; introduced `OnlyWar.Campaign` and `OnlyWar.Application`, explicit `ICampaignSession`/`CampaignApplication`, detached generation/load/save/turn workflows, Engine removal, host new/load/turn wiring, stale-session/failure isolation tests, source-ownership updates and solution/headless validation. Compatibility singleton/default entry points remain explicitly scoped to SB-11/SB-12. |
| SB-11 | 6: UI command/query boundary | SB-10 | In progress | Codex; SB-11a, SB-11b and SB-11c code complete. Godot smoke and the manual interaction flows are the outstanding acceptance items across all three. |
| SB-11a | 6: Operations and medical screens | SB-10 | Ready for integration | Codex; medical and Operations screens use typed commands, application-issued undo tokens and fully detached projections; no campaign entity is named in either screen's sources. Godot smoke and manual interaction checks are the only outstanding acceptance items. |
| SB-11b | 6: Session controls, reports and battle screens | SB-11a | Ready for integration | Codex; session lifetime, recoverability, all five save paths, the end-turn gate/preflight, the Command Brief/Chronicle, Battle Review, the turn report/debrief/briefing dialogs and `MainGameScene`'s own campaign facts and writes are on the application boundary. Turn-report content moved to `TurnReportProjector` in Application. Godot smoke and manual save/load/new-game, end-turn and debrief flows are the outstanding acceptance items. |
| SB-11c | 6: Roster, fleet, training and remaining screens | SB-11b | Ready for integration | Chapter, Chapter Muster, Squad/loadouts, Fleet, Training, Diplomacy, `SectorMap` and `SystemInspector` all take an application; `MainGameScene`'s entity-resolution helpers are gone. Startup-only composition exceptions recorded for SB-12. Godot smoke and manual interaction checks are the only outstanding acceptance items. |
| SB-12 | 6: Dependency enforcement and bridge removal | SB-11 | In progress | Claude; global-state bridges removed and the source-level enforcement fixture landed. The two heavy extractions — Campaign's SQL layer and the Operations→Battles handles — and retiring `GameDataSingleton` itself remain. |
| SB-13 | 7: Integrated verification and packaging | SB-12 | Planned | Unassigned |
| SB-14 | 7: TDD/PRD reconciliation and plan retirement | SB-13 | Planned | Unassigned |

Parallel opportunities are optional, not authorization to spawn agents automatically:

- SB-01 and SB-02 can proceed independently after SB-00 defines their shared types.
- After SB-03, SB-04, SB-07 and SB-08 can proceed independently with serialized shared-model/project edits.
- SB-05 and SB-06 may overlap only after agreeing on battle invocation/outcome contracts and assigning mission/aftermath adapter files to one owner. Otherwise run them sequentially.
- SB-09 may overlap those tickets if scenario and turn-adapter edits are isolated. SB-10 is the integration barrier.
- SB-11 through SB-14 are sequential acceptance work. Do not run assembly moves in parallel with broad controller migration or final documentation reconciliation.

**SB-00 integration evidence (2026-09-03):** Actual edit scope is only `Design/Active/SubsystemBoundaries.md`; the plan and active-index edit were already present as user working-tree changes. Inspected project/reference/resource settings, current source dependency and mutation paths, `AGENTS.md`, `Design/README.md`, and relevant TDD/PRD architecture/behavior sections. §§3.1–3.6 record the acyclic graph, remaining campaign owners, scoped state access, named workflows, compatibility exits and migration baselines. No production, test, database, TDD or PRD files changed. No Phase 1 ticket started.

Baseline command (also builds the current host/test projects):

```powershell
dotnet test OnlyWar.Tests\OnlyWar.Tests.csproj --filter "(FullyQualifiedName~OnlyWar.Tests.Domain.ChapterOperationalDoctrineTests|FullyQualifiedName~OnlyWar.Tests.Turns.GameSessionTurnControllerTests|FullyQualifiedName~OnlyWar.Tests.Battles.BattleRefactorCharacterizationTests|FullyQualifiedName~OnlyWar.Tests.Domain.EntityIdAllocationTests)&Category!=Slow" --verbosity minimal
```

Result: **15 passed, 0 failed, 0 skipped**; existing obsolete-API compiler warnings. This is a small characterization baseline, not full-game or export certification. Phase 0 acceptance is document/source review plus graph/reference validation: a PowerShell topological check of the §3.1 matrix found all 11 final assemblies acyclic; named test targets were checked against existing test source files. Future tests/gaps in §3.6 remain owned by their migration tickets. Full-suite, Slow, Godot interaction and export gates were not run for this documentation-only change. Remaining issues are the explicitly assigned couplings and failure risks in §3.5; none blocks handing off SB-01 or SB-02.

## 5. Ticket briefs

### Phases 1–2 integration evidence (2026-09-04)

Implemented sequentially from `360e3ec8f6c5064b2f415bba1dbe2b9e958655af`; the Phase 1–2
production source moves and remaining host helpers
are enumerated in `Modules/source-ownership.json`. Domain/Contracts/Campaign/Application entries are
relative to `Modules/OnlyWar.<owner>/`; Host entries are repository-relative. Earlier
starting-point tables retain the pre-migration paths.

`Modules/source-ownership.json` was refreshed during SB-10 to include the current Medical,
Persistence, Runtime, Battles, Generation, Campaign and Application ownership. Treat the csproj
files and the headless negative fixture as the authority on dependency edges; the manifest is the
source inventory used to audit exact-once compilation.

- **SB-01:** Individual/squad readiness moved out of UI into Engine policy and Contracts
  values. Core policy never selects a singleton. UI composition accepts explicit doctrine
  and reservations; mission/battle boundaries carry session reservations through refresh.
  The named `CurrentCampaignReadinessContext` application adapter resolves legacy callers
  at execution and checks faction reference identity. Shared reservation evaluation remains
  one predicate. Added agreement, rejected-order, duplicate-ID/unrelated-campaign and
  replaced-session tests; existing injury/leader/minimum-force coverage remains intact.
- **SB-02:** `GameRulesData(GameRulesBlob)` accepts hydrated values only;
  `GameRulesLoader.Load(path)` owns database entry. Equipment SQL was also removed from
  `EquipmentRulesCatalog` into `EquipmentCatalogLoader`; legacy conversion is an Engine
  builder. In-memory construction and source-file-independent identity tests pass. Runtime
  `Faction.Units`, runtime templates/fire discipline and mutable supply settings are explicitly
  campaign-owned, not declared immutable or safe to share between campaigns.
- **SB-03a/b:** Added the standard .NET 8 projects and direct test references. Neutral
  `GridCell`/`PlanePoint` preserve geometry ordering/arithmetic; the host converts to Godot
  vectors. `GodotHostPaths`, configured by the existing logging autoload, owns virtual user
  paths; headless storage/preferences take ordinary paths. The two name resources retain
  their logical names through the Runtime provider; SB-08 now owns them in Runtime and lookup uses the provider's owning assembly. MSBuild
  reports no module/test sources in the host compile list. Production friend grants were
  not introduced. A separate headless test process verifies no Godot/host assembly loads.

Validation (all filters exclude Slow):

```powershell
dotnet build Modules/OnlyWar.Application/OnlyWar.Application.csproj
dotnet build Modules/OnlyWar.Campaign/OnlyWar.Campaign.csproj
dotnet build OnlyWarGodot.csproj
dotnet test OnlyWar.HeadlessTests/OnlyWar.HeadlessTests.csproj
dotnet test OnlyWar.Tests/OnlyWar.Tests.csproj --filter "(FullyQualifiedName~ChapterOperationalDoctrineTests|FullyQualifiedName~ReadinessBoundaryTests|FullyQualifiedName~SquadRowViewModelTests|FullyQualifiedName~OrderAssignmentTests|FullyQualifiedName~MissionAvailabilityTests|FullyQualifiedName~RulesDatabaseValidationTests|FullyQualifiedName~FactionGenerationPolicyTests|FullyQualifiedName~ChapterGenerationTemplatesTests|FullyQualifiedName~SubsectorBuilderTests|FullyQualifiedName~WarpLaneBuilderTests|FullyQualifiedName~NameGeneratorTests|FullyQualifiedName~SessionSimulationContextPrimitiveTests)&Category!=Slow"
```

Both builds pass; headless smoke **8 passed**; focused regression **86 passed**. Existing
obsolete-API warnings remain (iteration commands suppressed warnings for concise output).
The broader UI/battle-characterization/mission-scheduler slice had **118 passed, 14 failed**.
All 14 failures are the identical pre-existing `PlanetaryOperationsServiceTests` failures
reproduced from the starting revision in an isolated source snapshot (22 passed/14 failed
in that class). They concern legacy readiness fixtures and presentation expectations;
SB-11a owns reconciliation, with SB-13 enforcing the final integrated gate. Eight original
order-assignment test failures were likewise reproduced on the starting revision; those
positive-case fixtures now supply their required sergeants, with separate leaderless
rejection coverage. No gameplay eligibility rule was relaxed to satisfy a fixture.

TDD §§1–3, 4.1–4.3, 6.6.1–6.6.2, 6.7, 7.6 and 9 describe the intermediate implementation.
PRD §§4.1,
4.3–4.4, 4.8, 4.12, 4.26 reviewed; no PRD change required. No rules DB/save schema/player
save changes, balance diagnostics, full-suite, manual Godot workflow or export claims.

Remaining migration ownership:

| Bridge / source classification | Exit owner |
|---|---|
| Engine was the temporary bridge for campaign globals, model-specific SQLite readers and application wiring. SB-10 removed the project and partitioned its sources into Campaign/Application; compatibility defaults and the singleton remain only as named transitional host bridges. | SB-11 migrates scene projections/commands; SB-12 enforces the final graph and removes the bridges. |
| `CurrentCampaignReadinessContext` remains a Campaign compatibility adapter; recruitment models still supply reservation facts. | SB-11/12 migrate remaining host callers and remove the adapter. |
| Faction/unit/squad/personnel/planet entities and rules graph remain in Campaign where they still call policies, IDs or compatibility services; runtime mission/battle contexts remain in their owning modules. Only independent primitives/templates moved to Domain. | SB-11/12 relocate service-dependent members and tighten implementation references. No Domain→Campaign/Application bridge is permitted. |
| Medical procedure option values retain their legacy presentation-shaped API in Campaign; recovery view models and last-turn presentation builder stay in host. | SB-04 typed medical commands; SB-11 projection/report boundaries. |
| Existing host-facing APIs are public across the interim assembly seam, with only test friend access. | SB-10/11 replace scene bypasses; SB-12 tightens visibility. |
| Runtime now owns the name provider/resources and explicit construction/ID adapters; installed rules-file lookup and model-specific persistence readers remain transitional in Campaign. | SB-07/10 persistence/session paths; SB-08 model materialization; SB-12 bridge removal. |
| ~~Pre-existing operations UI fixture/expectation failures from the adjacent slice.~~ **resolved 2026-09-06** — they were an unconfigured Operations personnel capability and a missing test battle-squad factory, both now composed at their owning composition roots. The non-Slow suite is green. | Closed in SB-11a; SB-13 still owns the full integrated gate. |

### Phase 3 integration evidence (2026-09-04)

Implemented in the working tree as ten headless owners plus the Godot host. The former Engine
sources are now split between Campaign and Application; the extracted policy modules do not
reference the host or current-session state, and Generation remains independent of Application.

- **SB-04:** `OnlyWar.Medical` owns individual/squad readiness precedence, health healing and
  procedure transitions, facility eligibility, and typed medical facts/results. Application's
  `MedicalTurnProcessor` adapts live bodies/maps and retains cadence, reservation synchronization,
  reunion eligibility, and campaign event bookkeeping. `FieldCarePolicy` owns wound demotion and
  triage over explicit provider/patient facts; Campaign's `FieldCareService` retains reach,
  resource/rating, report and experience bookkeeping until the campaign/personnel extraction.
- **SB-07:** `OnlyWar.Persistence` owns atomic file writes, save catalog inspection, metadata,
  retention/recovery helpers, and format-version constants. `IAtomicCampaignFileStore` and
  `PersistenceLoadResult` expose bytes/state plus format information, never SQLite connections,
  readers, commands, slot UI, or active-session publication. Campaign owns the transitional
  model-specific SQLite readers; full candidate reconstruction and installation now move behind
  the Application boundary.
- **SB-08:** `OnlyWar.Runtime` owns explicit soldier/squad/force construction APIs, embedded name
  pools, positive campaign ID compatibility service, and negative tactical allocation. Runtime
  factories receive detached templates, `IRNG`, and `IEntityIdAllocator`; they do not reference
  Generation or Application. Campaign's SoldierFactory delegates portable stat/body/skill construction
  to Runtime before materializing the live campaign model. The legacy ForceGenerator still owns
  campaign-specific faction/synapse/loadout materialization and is tracked for the later model
  migration rather than duplicated inside Runtime.

Validation:

```powershell
dotnet build Modules/OnlyWar.Medical/OnlyWar.Medical.csproj --no-restore
dotnet build Modules/OnlyWar.Persistence/OnlyWar.Persistence.csproj --no-restore
dotnet build Modules/OnlyWar.Runtime/OnlyWar.Runtime.csproj --no-restore
dotnet build OnlyWarGodot.csproj --no-restore
dotnet test OnlyWar.HeadlessTests/OnlyWar.HeadlessTests.csproj --no-restore
```

Result: all four builds pass; headless smoke is **11 passed**. The adjacent non-Slow regression
slice built and passed **2,775 tests**, with the same **14 pre-existing** planetary-operations UI
fixture/presentation failures recorded for SB-11a; no Phase 3 module test failed. The full suite,
Slow diagnostics, manual Godot workflow, export packaging, and active-session failure isolation
remain owned by later acceptance tickets.

### Phase 0 — Establish ownership

#### SB-00 — Record the concrete map and migration contracts

**Scope:** Inspect current dependencies and mutation paths. Expand §3 with actual assembly names, allowed references, shared data ownership, and locations for application adapters. Map existing turn/battle collaborators to owners. Inventory singleton/static RNG/ID access and domain-to-UI/storage dependencies. Record baseline behavior and test targets for each migration; list compatibility bridges with removal owners.

**Acceptance:** Every target subsystem and remaining campaign domain has a home. The proposed graph is acyclic, includes runtime construction and scenario warm-up, and can be introduced incrementally. Cross-domain casualty evacuation, battle aftermath, order issue/undo, save/load and new-game flows each have a named coordinator. No production changes required for this ticket.

**Docs:** Update this plan with decisions and the dependency matrix. Do not describe proposed architecture as shipped in the TDD or PRD.

### Phase 1 — Remove policy and data-loading inversions

#### SB-01 — Extract readiness from UI and globals

**Start:** `DutyReadinessService`, `SquadReadinessService` and related types in `Helpers/UI/SquadRowViewModels.cs`; order, mission, loadout and UI callers.

**Scope:** Move authoritative individual/squad deployment evaluations and reason codes to the domain policy location. Pass doctrine and reservation context explicitly. Separate readiness facts from row labels/colors. Migrate callers; an explicitly named host compatibility adapter may temporarily obtain current-session inputs, but core policy cannot silently fall back to globals.

**Acceptance:** UI selection, order validation and mission materialization agree for the same explicit inputs. No policy depends on UI. Cover leader/minimum-force rules, injury thresholds, untreated severance, procedure reservation and detached/unrelated-session cases. Preserve existing active-battle participation semantics.

**Verification/docs:** Target `ChapterOperationalDoctrineTests`, relevant readiness/mission-availability/order tests, then adjacent UI projection tests. Update TDD §6.6.2 and §7.6 to match the actual move; review PRD deployment/medical requirements and record whether wording needs correction.

#### SB-02 — Separate the rules catalog from persistence

**Start:** `Models/GameRulesData.cs`, `Helpers/Database/GameRules/`, catalog consumers and loading call sites.

**Scope:** Construct an in-memory rules catalog without file paths, SQL or `GameStorage`. Move database loading/validation to an explicit loader. Preserve template identity and lookup behavior. Identify runtime-mutated objects currently reachable through rules (for example faction-owned units) and assign their state correctly rather than declaring the entire graph immutable.

**Acceptance:** A focused test constructs/uses rules without opening a database. The production loader retains validation/error behavior and produces the same usable catalogs. No rules-domain dependency on the persistence implementation remains.

**Verification/docs:** Existing rules validation/data tests and targeted generation consumers. Update TDD §4.1; record PRD review as behavior unchanged unless evidence shows otherwise.

### Phase 2 — Make the engine independently buildable

#### SB-03 — Introduce the headless foundation

**Scope:** Introduce the shared domain/contracts and temporary headless campaign/engine project layout agreed in SB-00. Remove Godot types from simulation/data boundaries using neutral values or host adapters. Keep presentation helpers in the host. Adjust project references, compile globs, resources and test access so moved sources compile exactly once.

**Start:** `OnlyWarGodot.csproj`, solution/test projects, `Properties/AssemblyInfo.cs`, engine-dependent model/generation geometry, logging/storage-path adapters and embedded soldier-name resources.

**Acceptance:** Headless projects build without Godot SDK/native initialization. Godot host still builds and retains resources. Tests can reference headless projects directly. Add a small representative headless test project/smoke if the existing mixed suite still transitively requires the host; maintain the existing suite during migration. A temporary engine project is scaffolding, not the final home for all six modules.

**Verification/docs:** Build headless and host projects; run representative domain, generation-geometry and resource-loading tests. Update TDD §§1–3 and §9 with the actual intermediate layout. Record each remaining extraction in the ledger.

**Assign separately:**

- **SB-03a:** Inventory and remove engine-type dependencies from the intended headless source set while still building in the current project. Introduce neutral coordinates/presentation tokens and host logging/path adapters as needed. Acceptance: the agreed headless source set has no Godot imports or engine API use, with geometry/resource behavior covered by targeted tests. Supply the exact source/resource ownership manifest for SB-03b.
- **SB-03b:** Create the projects/references and move sources according to that manifest. Own solution files, compile exclusions, embedded-resource names and test project changes. Acceptance: separate headless and Godot builds succeed, moved files compile once, and the representative headless test runs without loading the host. Do not combine this with feature-policy changes.

### Phase 3 — Establish independent owners

#### SB-04 — Establish Readiness/Medical as a module

**Start:** Readiness policy from SB-01, `Helpers/Medical/`, medical procedure/turn services, recovery plans, medical models, posting services and recruitment reservations.

**Scope:** Put readiness/medical implementation behind its own headless assembly/API. Separate medical decisions from posting/transport orchestration and presentation builders. Expose narrow health operations usable during combat plus daily/weekly care operations. Supply recruitment reservations and facilities/capacity facts explicitly. Retain health primitives in the agreed lower-level domain location where needed to prevent cycles.

**Acceptance:** Module cannot access UI, SQLite, current-session globals or Operations implementation. One owner applies each health/procedure transition. Preserve daily/weekly cadence, field care, treatment costs/reservations, recovery completion and reunion eligibility. Existing callers use contracts or tracked adapters.

**Verification/docs:** Target wound/healing, medical procedure, medical turn, readiness and recovery/posting tests; one adjacent mission/turn test for cadence. Update TDD §§6.6.1–6.6.2 and §6.10; review PRD §§4.8 and 4.12.

#### SB-07 — Establish Persistence as a module

**Start:** `Helpers/Database/`, `Helpers/Storage/`, `SavedGameLoader`, rules loader from SB-02.

**Scope:** Isolate SQLite implementation in its assembly. Load/save explicit inputs and return a reconstructed candidate plus format/upgrade information. Keep slot UI, active-session selection/publication and recoverability notifications outside the adapter. Extract deterministic derived-state reconstruction (such as warp networks) to its agreed owner rather than depending on the top-level sector generator.

**Acceptance:** Domain/application storage contracts do not expose connections, readers or commands. Preserve atomic save behavior, supported save formats, entity identity/references, derived state and failure handling. Save tests use temporary files and do not alter player saves. Schema changes are not an expected consequence of this refactor; justify and separately track any unavoidable migration.

**Verification/docs:** Target save/load round trips, mission saves, save manager/recovery and relevant doctrine/medical persistence tests. Update TDD §§4.1–4.3 and applicable debt entries; review PRD §4.18. Full active-session failure isolation is also verified at SB-10.

#### SB-08 — Separate runtime factories from campaign generation

**Start:** `Builders/ForceGenerator.cs`, soldier/squad factories, `IEntityIdAllocator`, runtime invasion/recruitment/mission consumers.

**Scope:** Move construction used during play to a lower-level runtime construction home with explicit rules, RNG and ID allocation. Keep new-game/world generation above it. Avoid changing global-ID or random-stream policy as an incidental cleanup; make dependencies explicit and preserve current allocation semantics.

**Acceptance:** Runtime simulation can create required entities without referencing the top-level Generation implementation. No duplicate factories, parallel ID counters or competing template/eligibility logic. Preserve force composition, shortfall/failure behavior, IDs and seeded test expectations.

**Verification/docs:** Target force generation, recruitment/invasion consumers and allocator tests. Update TDD §6.7, affected generation sections and applicable ID/global-state debt. Record compatibility bridges for SB-12.

### Phase 4 — Establish workflow-facing subsystem boundaries

### Phase 4 integration evidence (2026-09-05)

Phase 4's *explicit-input* work is complete: no code in `Helpers/Missions/`, `Helpers/Orders/`,
`Helpers/PlanetaryOperations/`, `Helpers/StrategicCombat/` or `Builders/` reads
`GameDataSingleton` any more, and `OnlyWar.Battles` is extracted. Two of the four assembly moves
are still outstanding and are named in the ledger.

- **SB-05a (complete):** `OrderMutationService.RestoreParticipants` validates the complete
  participant selection, current readiness, commitments and staging before registering an order or
  assigning any participant; `RestoreSquad` uses the same path. Cancellation undo uses an opaque
  `OrderRestoreToken` tied to the original sector instance. On top of that slice, order lifecycle
  policy now takes an explicit `OrderCommandContext(Sector, Date)`
  (`Modules/OnlyWar.Contracts/Operations/OrderCommandContext.cs`): `OrderAssignment` no longer
  resolves a sector, player faction or recruitment program from the current campaign, and an order
  that empties out is retired through `Order.RegisteredSector` — the registration the order already
  carries — rather than through an ambient one. `OrderAttachment.Attach`,
  `PlanetForceMovementService.Land/Embark`, `OrderMutationService.CreateOrAdd/AttachSpecialist/
  Restore/RestoreSquad/RestoreParticipants` and `IndividualPostingService.AttachToOrder` take the
  campaign date explicitly; `InboundOrders.ForRegion` takes the sector and
  `SpecialistAvailability.EnumerateRoster/EnumerateCandidates` take the chapter roster.
  `PlanetaryOperationsScreenController` was the named host adapter supplying them; SB-11a moved that
  role to `CampaignApplication`'s operations commands on 2026-09-06.
- **SB-05b (mission execution and assembly complete):** `StrategicCombatResolver` receives the
  campaign's persistent invasion forces as a constructor/parameter input instead of reading
  `Sector.StrategicInvasionForces` off the current session; `MissionTurnProcessor`,
  `StrategicInvasionLifecycleProcessor` and `FactionStrategyController`/`FactionOffensiveEvaluator`
  each supply the campaign they are resolving for, so the NPC planner prices a defender exactly as
  strategic combat will. `LightningRaidMissionStep` uses `MissionCampaignInputs.PhysicalForces` and
  `ExfiltrateMissionStep` uses `MissionCampaignInputs.Date`. Mission, order, planetary-operation and
  strategic-combat policy now compile into `Modules/OnlyWar.Operations`; Engine supplies the explicit
  composition adapters.
- **SB-06 (complete):** `Modules/OnlyWar.Battles` references only Domain and Contracts. Tactical
  equipment resolution goes through `IBattleEquipmentSource` rather than the current rules/force,
  and the readiness refresh that needed campaign services moved out to a Campaign/Application adapter.
  `HeadlessBoundaryTests.AssembliesHaveOnlyTheirAllowedProductionDependencies` now asserts the
  Battles assembly's allowed references and the transitional composition references.
- **SB-09 (complete):** `ScenarioBuilder` drives its planet-scoped
  simulations through a `TurnController` built on a `GameSession` for the *candidate* sector, so
  `SectorBuilder.GenerateSector` no longer publishes a half-built sector and
  `GameDataSingleton.SetSectorDuringGeneration` is deleted. `InitializeNewGameData` builds the whole
  candidate first and installs rules, date and sector together, so a failed generation leaves the
  campaign the player is already in untouched. `Modules/OnlyWar.Generation` is the independent
  generation assembly and cannot reference Application or the host.

Coverage added: `SectorBuilderTests.GenerateSector_WithNoInstalledCampaign_
BuildsCandidateWithoutPublishingIt` and `.InitializeNewGameData_WhenGenerationFails_
LeavesTheActiveCampaignInstalled`; `OrderRestoreBoundaryTests.IssueAndCancel_OnDetachedSessions_
MutateOnlyTheSuppliedCampaign`; `StrategicCombatResolverTests.CalculateDefenderBattleValue_
CountsOnlyTheSuppliedInvasionForces`; plus the Battles row in the headless negative fixture.
`SectorSimulationFixture` gained `CurrentDate`, `OrderCommands` and `ChapterRoster` so order tests
name the campaign they mutate.

Validation:

```powershell
dotnet build OnlyWarGodot.sln --nologo -v q
dotnet test OnlyWar.HeadlessTests\OnlyWar.HeadlessTests.csproj --nologo --no-build
dotnet test OnlyWar.Tests\OnlyWar.Tests.csproj --nologo --no-build --filter "Category!=Slow"
dotnet test OnlyWar.Tests\OnlyWar.Tests.csproj --nologo --no-build --filter "Category=Slow"
```

Result: solution builds with 0 errors; headless smoke **11 passed**; non-Slow **3,039 passed,
0 failed**; Slow **11 passed, 0 failed**. The 14 planetary-operations UI failures recorded against
Phase 1–2 no longer reproduce. No rules DB, save schema or player save changed. Manual Godot
workflows and export packaging were not run and remain owned by SB-13.

TDD §§1–3 (project table), 6.9, 6.10 and 8.6 updated to the shipped implementation. PRD §§4.1,
4.3–4.4, 4.10, 4.13 and 4.14 reviewed; behavior is preserved and no PRD change is required.

Deliberately left in place as compatibility bridges (owners already assigned elsewhere in this plan):
`PresenceRequest`, `LoadoutDoctrineService`, `CharacterLoadoutService`, `CareDestinationService`,
`RecoveryPlanService`, `RecruitmentStaffService` and `IndividualPostingService`'s readiness default
(SB-04/SB-11/12); `CampaignLoader`, `CurrentCampaignSaveWriter`, `TurnController`'s current-session
constructors and `GameDataSingleton` itself (SB-11/12); `FactionStrategyController`'s parameterless
facade and `CurrentCampaignReadinessContext` (SB-12). The new/load/turn host paths now enter
through `CampaignApplication`; these compatibility APIs remain only for existing presentation and
legacy callers.

### Phase 4 assembly extraction (2026-09-05)

**SB-09 — complete.** `Modules/OnlyWar.Generation` holds `SectorBuilder`, `ScenarioBuilder`,
`NewChapterBuilder`, `PlanetBuilder` and `CharacterBuilder`, and references only Domain, Contracts
and Runtime. Everything it needs from the campaign arrives as a `GenerationSupport` bundle of six
ports in `Modules/OnlyWar.Contracts/Generation/`: seeding (ghost populations, faction reveal,
opening invasion), narrative (authority title, briefing, event recorder, founding record, chronicle
reconcile), fleet (initial flagship, administrative stationing), founding roles, the candidate
warm-up simulator, and `ISoldierTrainingService`. The implementations live in
  `Helpers/Application/Adapters/Generation/GenerationSupportAdapters.cs`, compiled into Application,
  with the same dependency direction.

`ICandidateWarmupSimulator.BeginCandidate` marks the point in the authored sequence where the
candidate session opens, and one turn controller then drives both the pre- and post-landing passes,
exactly as the generator's own controller did — a fresh controller per pass would silently reset the
turn intelligence ledger and planning state between them, and the campaign event bindings would
attach at a different point in the sequence.

Supporting moves this required, each landing a type with its assigned owner:

- Runtime gains `WorldGeometry/` (`SubsectorBuilder`, `WarpLaneBuilder`, and a new
  `SectorTopologyBuilder` carrying `GenerateWarpNetwork`/`AssignGovernance` out of `SectorBuilder`).
  Load and new game now rebuild derived topology through the same Runtime path, so Persistence no
  longer needs the top-level generator — closing the §3.1 Persistence↔Generation concern.
- Runtime also gains `SoldierFactory` (plan §3.2) and `RNG`/`StaticRNG`/`SeededRNG` plus the
  `NameGenerator` façade, so generation and live simulation share one construction path, one
  persistent ID counter and one name bag.
- Domain gains the intrinsic members that generation needs: `FactionRoles.IsImperial`,
  `PlanetExtensions`, `SquadExtensions`, `CivilUnrestRules`, `Fortifications/`,
  `FactionRelationshipService`, `IntelligenceTargetService`, `CampaignLocationService`, and the
  intrinsic halves of `RegionExtensions` and `RegionFactionExtensions`. The service-dependent halves
  stay with their owners as `RegionDetectionExtensions` (mission detection policy) and
  `RegionFactionDescriptionExtensions` (intel-gated presentation); extension resolution is by
  namespace, so no caller changed.
- `SectorBuilder.ReplaceChapterPlanetFaction` became `Helpers/Turns/ChapterHomeworldService`: it is
  the campaign consequence of winning the opening scenario, not part of world construction.
- `BriefingTokens` moved to Contracts as a boundary value; `ISoldierTrainingService` moved to
  Contracts beside `IRNG`.

**SB-05b — delivered in two slices.** The initial trial extraction of `Helpers/Missions`,
`Helpers/Orders`, `Helpers/PlanetaryOperations`, `Helpers/StrategicCombat` and `MissionContext` into
`Modules/OnlyWar.Operations` exposed the contract work now delivered by SB-05b-1. SB-05b-2 then
completed the physical assembly move, explicit Engine composition and boundary fixture. The remaining
transitional tactical-handle dependency is intentionally tracked for SB-12:

- **Engagement is ported with a tracked transitional handle.** Mission steps now consume
  `IEngagementResolver` and detached `EngagementResult` values. `MissionContext` retains
  `BattleSquad` handles for multi-day state and aftermath, so Operations still has a transitional
  `OnlyWar.Battles` reference; SB-12 owns the final replay/handle projection.
- **Readiness is contractual.** Order and mission code consumes the Contracts readiness decisions;
  the Engine medical adapter is composed at the turn boundary rather than referenced by Operations.
- **Personnel is adapter-backed.** Operations consumes `IOperationsPersonnelSurface` and
  `IOrderCommitmentSurface`, while Engine composes the concrete posting/availability services.
- The remaining moved `Helpers.Turns`, `Helpers.Strategy` and recruitment helpers are explicit
  Operations inputs or local policies; `FieldCareReport` values live in Contracts.

**SB-05b-1 — Engagement, readiness and personnel contracts.** Define and adopt the battle
invocation/outcome port from §3.4 so mission steps stop constructing tactical types; give
`MissionContext` a participant representation that is not a tactical wrapper; move Operations'
remaining `SquadReadinessService`/`DutyReadinessService` calls behind the Contracts readiness
decisions; split posting/availability into an Operations commitment surface and a Campaign personnel
surface; move `FieldCareReport` values to Contracts. Coordinate the battle contract with SB-06's
owner before starting, as §4 already requires.

**SB-05b-1 — delivered (2026-09-05).** All five strands are now in place: the engagement port,
neutral participant projection, readiness decisions, field-care values, and the Operations→Campaign
personnel surface. SB-05b-2 now consumes those contracts from the extracted Operations assembly.

*Readiness — done.* `DutyReadinessService`, `SquadReadinessService`, `SquadStrengthSnapshotBuilder`
and `ReadinessReservations` moved from Engine to `OnlyWar.Medical/Readiness/`, their rightful owner
under §3.1; the namespace is unchanged, so no caller's `using` moved with them. Contracts gained
`IReadinessDecisions` beside the decision types it already held, and `MedicalReadinessDecisions`
implements it by forwarding to that same policy — the interface reverses the reference direction
and introduces no second rule. Every readiness call under `Helpers/Orders/`,
`Helpers/PlanetaryOperations/`, `Helpers/Missions/`, `Helpers/StrategicCombat/` and
`MissionTurnProcessor` now goes through the capability; a grep of those directories for either
service name returns nothing. It is carried on `OrderCommandContext` and `MissionCampaignInputs`
(beside the `IBattleEquipmentSource` precedent) and passed explicitly to `OrderAttachment`,
`OrderForceService`, `OrderMutationService`, `RegionalOrderEligibilityService` and
`SpecialistAvailability`. `CommandAttentionEvaluator` still calls the services directly and is
deliberately left alone: it is not on SB-05b-2's move list.

The capability is **required, not nullable**. A nullable port with a permissive fallback would let
an unfit formation onto an order silently, so `OrderCommandContext.RequireReadiness()` throws
instead, and every entry point takes it as a compile-enforced parameter — which is what made the
~45 call sites visible rather than merely reachable.
`ReadinessBoundaryTests.IssuingAnOrderWithoutAReadinessCapabilityFailsRatherThanSkippingTheDeploymentGate`
pins both directions.

*Personnel — done.* Contracts gained `IOrderCommitmentSurface`
(`ReleaseCharacter`/`ReleaseSquad`), implemented by `Helpers/Orders/OrderCommitmentSurface`, and
`IndividualPostingService` takes it by constructor instead of calling
`OrderForceService.RemoveCharacter` and `OrderAssignment.UnassignSquads` directly. Campaign→
Operations is therefore gone: neither `IndividualPostingService` nor `CharacterAvailabilityService`
names an Operations type any more. Both files' `Helpers.Orders`/`Helpers.Missions` imports turned
out to be **stale** — removing them changed nothing — so the trial's "genuinely circular" finding
was narrower than it looked; the one real edge was the two commitment calls above. The second port,
`IOperationsPersonnelSurface`, now projects availability decisions and present headcount and owns
the posting operations required by order attachment, movement, medical detachment and exfiltration.
`OperationsPersonnelSurface` is the Campaign-side composition adapter; Operations callers accept the
port and no longer construct `CharacterAvailabilityService`, `IndividualPostingService` or call
`SoldierPresenceService` directly.

*Field care — done.* `FieldCareReport` and `FieldCareTreatment` moved to
`Contracts/Medical/FieldCareReportValues.cs` (namespace unchanged). `RecordTreatment` had to become
public: its producer no longer shares an assembly with the value.

*Engagement — done.* `IEngagementResolver` now owns placement, tactical construction, equipment
reallocation and the turn loop in `BattleEngagementResolver`. Mission entry points consume only
`EngagementInput`/`EngagementResult`; the `MissionContext` exposes neutral engagement participants
while retaining transitional `BattleSquad` handles internally for multi-day state and aftermath.
`EngagementResult` carries detached outcome, casualty, debrief and opaque replay facts, preserving
the existing mission policy without leaking tactical resolver types into the call sites.

Validation for what landed: solution builds 0 errors; non-Slow **3,041 passed**, headless smoke
**11 passed**, and the focused mission/order/planetary-operations slice passed **255 tests**. The
remaining build warnings are pre-existing `CS0618`
obsolete-member uses, untouched by this work.

**Readiness strand — done (2026-09-05).** No file under `Helpers/Orders/`,
`Helpers/PlanetaryOperations/`, `Helpers/Missions/` or `Helpers/StrategicCombat/` references
`CurrentCampaignReadinessContext` any more. The predicate it applied — doctrine and reservations
govern only their own force's squads — moved to `ForceReadinessInputs` in Contracts, which takes the
force explicitly; the adapter now delegates to it, so there is one rule rather than two. Order
lifecycle policy resolves that force three ways, none of them ambient: `OrderCommandContext.Force`
for command entry points, `sector.PlayerForce` where the service already receives a sector
(`RegionalOrderEligibilityService`, `OrderMutationService`), and `Order.RegisteredSector.PlayerForce`
inside `OrderForceService`/`OrderAttachment`, which is the registration the order already carries.
An order still being assembled has no registration, so `OrderAssignment` resolves and passes the
inputs for that case rather than letting anything fall back.

`RegionalOrderEligibilityService` now threads a `PlayerForce` where it threaded a
`RecruitmentProgram`, so doctrine and reservations come from one guarded source instead of the
program being resolved unguarded and the doctrine ambiently.

`OrderRestoreBoundaryTests.IssueOrder_AppliesTheNamedCampaignsDoctrineNotTheInstalledOnes` pins both
directions — the named campaign's doctrine applies, the installed campaign's does not leak in — and
was confirmed to fail against the pre-change call site.

**SB-05b-2 — Operations assembly delivered (2026-09-05).** `Helpers/Missions`, `Helpers/Orders`,
`Helpers/PlanetaryOperations`, `Helpers/StrategicCombat`, `MissionStepOrchestrator`,
`MissionContext` and the mission turn/aftermath processors now compile in
`Modules/OnlyWar.Operations`. `ForceGenerator`, `SquadFactory` and the tactical ID allocator moved
to Runtime (§3.2). `MissionTurnDependencies` makes the turn boundary explicit, while Campaign/Application own
the personnel, battle, medical and diagnostic composition adapters. The headless negative fixture
now asserts Operations' references; its transitional `OnlyWar.Battles` handle dependency is
documented and reserved for SB-12 cleanup.

Down-moves from the trial that were correct on their own terms and have been kept: `Fortifications/`,
`FactionRelationshipService`, `IntelligenceTargetService` and `CampaignLocationService` are now
Domain-owned, which removes 17 Operations→Campaign references that SB-05b-2 would otherwise have to
deal with.

Validation for this slice:

```powershell
dotnet build OnlyWarGodot.sln --nologo -v q
dotnet test OnlyWar.HeadlessTests\OnlyWar.HeadlessTests.csproj --nologo --no-build
dotnet test OnlyWar.Tests\OnlyWar.Tests.csproj --nologo --no-build --filter "Category!=Slow"
dotnet test OnlyWar.Tests\OnlyWar.Tests.csproj --nologo --no-build --filter "Category=Slow"
```

Result: the Operations, Runtime, Campaign, Application and test projects build; the headless boundary fixture passes
**11 tests**; focused mission/order coverage passes **105 tests**, the biomass/combat slice passes
**28 tests**, and the turn-integration slice passes **24 tests**. No rules DB, save schema or player
save changed.
Manual Godot workflows and export packaging remain owned by SB-13.

**SB-10 — Application/session workflow integration delivered (2026-09-05).** The former Engine
sources are now split between `Modules/OnlyWar.Campaign` (live campaign policies, model/storage
adapters and compatibility façades) and `Modules/OnlyWar.Application` (session, turn, generation
adapters and cross-subsystem orchestration); `OnlyWar.Engine` is removed from the solution and
source ownership manifest. `ICampaignSession` is the explicit campaign contract, and
`CampaignApplication` provides detached create/load, install, advance-turn and save workflows.
Generation and load reconstruct candidates before publication, so failed generation/load leaves
the previously installed campaign intact; installation replaces the application session without
retaining references to the prior one.

The title new-game/load paths, in-campaign load path and main-scene turn path now enter through
`CampaignApplication`. `GameDataSingleton`, `CampaignLoader.LoadIntoSingleton`, the parameterless
turn constructor and `CampaignRuntimeDefaults` remain deliberately named compatibility bridges for
the remaining scene projections and legacy callers; removing those bypasses is SB-11/SB-12 work.
`Modules/source-ownership.json` now reflects the current Campaign/Application ownership.

Coverage added in `OnlyWar.Tests.Application.CampaignApplicationTests` verifies session replacement
and failed-load isolation. `HeadlessBoundaryTests` verifies the new assembly references and the
solution build excludes the removed Engine project. Targeted turn/generation/session tests and
headless smoke are the SB-10 verification gate; manual Godot workflows and export packaging remain
owned by SB-13. Validation completed with solution build **0 errors**, headless smoke **11 passed**,
Application isolation **2 passed**, and the adjacent turn/generation/session slice **33 passed**.

#### SB-05 — Establish Planetary Operations commands and mission ownership

**Start:** `Helpers/PlanetaryOperations/`, `Helpers/Orders/`, `Helpers/Missions/`, mission turn/aftermath processors and the operations screen.

**Scope:** Put operational policy and mission execution behind the Operations assembly/API. Define typed commands/results for order lifecycle and operational movement/attachments. Revalidate readiness and commitments at execution. Put multi-owner transport/medical/posting coordination in an application adapter. Move display choices out of eligibility/mutation policy. Preserve mission scheduling and report facts.

**Acceptance:** Operations depends on readiness/battle contracts rather than UI or their implementation details. Duplicate commitment, stale selection, leaderlessness, failed movement and invalid attachments reject without partial changes. Existing cancel/restore/undo semantics survive. The mission scheduler retains its day ordering and the current treatment/battle/report sequence.

**Verification/docs:** Target orders, planetary operations services, mission availability/scheduling and mission aftermath tests; adjacent turn test. Update TDD §§5.6, 6.4, 6.10 and 7.5; review PRD §§4.3–4.4, 4.13 and relevant fleet/medical workflows. Coordinate invocation/outcome contracts with SB-06 before concurrent edits.

**Assign separately:**

- **SB-05a:** Establish typed order, attachment and operational movement commands with explicit validation inputs. Migrate the service entry points and provide a temporary controller adapter. Acceptance: command tests cover rejection without partial mutations and successful cancel/restore; domain decisions no longer depend on presentation types. Do not migrate every screen yet.
- **SB-05b:** Establish mission scheduling/execution contracts and the Operations assembly; migrate processors and wire battle/medical calls through agreed contracts. Acceptance: headless Operations builds with allowed references, and mission/day ordering plus outcome/report integration tests pass. Coordinate battle adapter files with SB-06; retain a tracked application wiring adapter until SB-10.

#### SB-06 — Establish the tactical battle boundary

**Start:** `BattleExecutionContext`, battle resolver/planning collaborators, `BattleSquadFactory`, `Helpers/Battles/Aftermath/`, mission battle invocation and battle review projections.

**Scope:** Extract tactical implementation into the Battles assembly. Define explicit participant/rules/context inputs and typed outcomes. Move player campaign bookkeeping, narrative and mission consequence coordination into external adapters while retaining narrow aftermath sinks where useful. Preserve the single live battle state and current planning/execution/aftermath lifetimes. Narrow access to health and participant state; do not clone the whole campaign to achieve isolation.

**Acceptance:** Battles cannot reach session globals, SQL, Godot or operational orchestration. There is one authoritative injury application and one campaign consequence application per event. Preserve casualty disposition, experience, chronology, replay/history, withdrawal and participant freezing. No speculative extra splitting of an already focused battle coordinator.

**Verification/docs:** Target battle aftermath, wounds, abandonment/withdrawal, participation and replay tests, then one mission integration slice. Preserve inert-battle diagnostics behavior. Update TDD §6.6 and relevant report architecture; review PRD §§4.10 and 4.14. Do not run balance diagnostics unless evidence requires them.

#### SB-09 — Establish Generation and explicit scenario warm-up

**Start:** Sector/scenario/chapter builders, generation helpers, `SetSectorDuringGeneration`, scenario simulation entry points.

**Scope:** Extract top-level Generation into its assembly. Return constructed state with explicit seed/options/rules/RNG/ID dependencies. Coordinate initial construction, authored scenario stamps, planet simulation warm-up and final validation through an application workflow. Supply an explicit simulation session/port for the candidate sector; never publish it just to satisfy warm-up dependencies. Separate derived-state builders used by load from new-game policy.

**Acceptance:** Generation and runtime simulation do not form a reference cycle. Generation/warm-up can run without an active singleton campaign. Preserve authored scenario phase order, founding/chapter behavior, IDs and seeded fixtures. A failure leaves the previous active campaign untouched; SB-10 covers the final host publication path.

**Verification/docs:** Target sector, scenario, chapter, governance and subsector generation tests. Update TDD §§5.8, 6.8–6.9 and affected global-state debt; review PRD §4.1. New-game application wiring may use a tracked host adapter until SB-10.

### Phase 5 — Integrate application orchestration

#### SB-10 — Centralize session workflows and cross-subsystem coordination

**Start:** `GameSession`, `GameDataSingleton`, `TurnController`, storage/new-game adapters, scenario warm-up and medical evacuation/posting coordination.

**Scope:** Establish the application API used by the host: create/load/install campaign, issue commands, obtain views, advance turn and save. Wire explicit subsystem dependencies at the composition root. Give subsystem calls only the state/capabilities required for that phase. Move residual business policy from turn/session coordinators to its assigned owner; keep the visible phase sequence. Route cross-domain commands through the coordinator decided in SB-00.

**Acceptance:** New game, load and turn resolution operate without subsystem access to a process-wide current campaign. Failed load/generation does not replace it. Switching campaigns does not retain stale session references. Mission aftermath, medical, training, movement, planet/scenario processing and reports retain their dependency ordering. Tests can create independent sessions without singleton setup. This does not promise concurrent execution or independent production RNG streams.

**Verification/docs:** Target session/turn controller, turn-result, scenario and save/new-game integration tests; add focused failure/stale-session coverage where absent. Update TDD §§3, 6.1 and 8.6/8.15 to the actual remaining state; review PRD §§4.1, 4.11–4.13 and 4.18. Do not mark global-state debt resolved while host/controller bypasses remain.

### Phase 6 — Close bypasses and enforce dependencies

#### SB-11 — Migrate UI consumers to application commands and projections

**Start:** All scene controllers, prioritizing Planetary Operations, Apothecarium, main game, start/system menu and battle review; presentation builders under `Helpers`.

**Scope:** Replace current-campaign graph reads/writes with explicit query projections and application commands. Keep colors/textures/Godot resources in presentation. Keep transient selection/navigation/undo interaction in UI; actual undo validation/mutation goes through the application. Migrate remaining screens in small controller groups; create child tickets if needed, rather than one enormous screen rewrite.

**Acceptance:** Controllers cannot mutate live soldiers, orders, fleets, procedures or campaign collections via returned results. UI contains no deployment/treatment/mission rules. Session changes refresh views and discard stale command context. Existing screen navigation, actionable reasons, reports, selection and undo remain usable. Startup composition is the only host location allowed to wire concrete adapters.

**Verification/docs:** Relevant UI/controller projection tests and existing scene-wiring smoke, plus manual interaction checks for order issue/cancel/undo, evacuation/treatment and load/new game. Update TDD §7 and actual presentation locations. Review the corresponding PRD screen requirements; record pass, discrepancy or needed correction for each migrated group.

**Assign separately:**

- **SB-11a:** Planetary Operations and Apothecarium/Recovery screens plus their presentation builders. Acceptance: typed command/query usage, no writable campaign references returned to those screens, preserved eligibility reasons, order undo and casualty/treatment interactions. Establish the reusable controller/session-refresh pattern for the following tickets.
- **SB-11b:** Start/new-game, main-game and system-menu controllers, end-turn/debrief/briefing dialogs, Battle Map and Battle Review. Acceptance: session replacement invalidates stale views/actions; turn/save/load commands and battle/report views use the application boundary; scene-wiring and workflow smoke pass.
- **SB-11c:** Chapter/Muster, Squad, Fleet, Training, Command, Diplomacy, sector/system inspector and remaining controllers/helpers. Inventory all remaining scene access first and split this child further by screen family if needed. Acceptance: each family has focused projection/command checks, navigation remains intact, and the final scene-wide audit finds no live campaign mutation or business-rule bypass. Record startup-only composition exceptions for SB-12.

**SB-11a medical implementation, 2026-09-06:** `ApothecariumScreenController` now receives
`IMedicalScreenApplication`; all live medical selection, requisite evaluation and recovery/reunion
mutation moved behind `CampaignApplication`. `MedicalLocationId` and `CareDestinationView` replace
ship/region/staff references in recovery results. Tokens reject confirmations from a replaced
session, and the controller discards transient context on session change. Portable medical and
shared squad-row builders moved to Application/Queries; icon resources stay in the host.
The shared row builder's legacy fallback remains for other screens (exit SB-11c/SB-12).
Recovery staffing uses the supplied force; combined staff/patient capacity and posting checks
run before movement. PRD §4.8 reviewed; no PRD change required. The insufficient combined-berth
case is a code correction to the existing no-partial-mutation requirement.

**Breakpoint / verification:** Host `dotnet build OnlyWarGodot.csproj --no-restore -v quiet`
passed. Targeted `OnlyWar.Tests` filters for `ApothecariumMedicalRecordBuilderTests`,
`CampaignApplicationTests`, `SquadRowViewModelTests`, `MedicalScreenApplicationTests` and
`MedicalProcedureServiceTests` (all excluding Slow) passed: 34 tests across two runs.
`OnlyWar.HeadlessTests` filtered to `HeadlessBoundaryTests&Category!=Slow` passed all 8 tests.
`git diff --check` is clean. The real Godot console smoke was run with
`--headless --path C:\Projects\GodotOnlyWar --log-file TestResults/phase6-scene-smoke.log res://Scenes/Debug/release_scene_wiring_smoke.tscn`.
The added Apothecarium open/refresh/close check reported no failure, but the overall smoke exited
1: the End Turn preflight X did not close its dialog. It also logged an Operations-personnel
composition exception while constructing `PlanetaryOperationsScreenController` during the
pre-bootstrap overlay-load check. These remain SB-11a/b integration follow-ups, not a passing
scene-wide gate. Full manual order/treatment/load flows remain unverified. Work stops here at the
user-requested reasonable breakpoint; resume with Operations, then SB-11b/c and SB-12.

**SB-11a operations command implementation, 2026-09-06:** `PlanetaryOperationsScreenController`
now receives `IOperationsScreenApplication` (`Modules/OnlyWar.Application/OperationsScreenApplication.cs`)
and issues no campaign mutation of its own. Order issue/reinforce/release, squad removal,
cancellation, aggression, specialist attach/detach, landing, embarkation and casualty detachment are
typed ID-and-token commands; the screen's captured undo closures are replaced by an
application-issued `Guid` token that is single-use, session-bound and dropped on session change.
Order-formation rules that lived in the screen — the character add-versus-release toggle, the
diversion target-faction fallback, mission-to-order equivalence and eligible-squad filtering — moved
into the application. The controller gained the medical screens' `Configure`/`SessionChanged`
pattern and clears its transient selection, pending confirmation and undo offer on replacement.

Two latent composition defects were fixed with it. `OperationsPersonnelDefaults` was configured only
as a side effect of touching `OperationsPersonnelSurface`, so a screen that constructed an Operations
service before any turn ran threw "No Operations personnel capability has been composed" — the
exception the previous smoke logged. `CampaignApplication` now composes it explicitly, and
`OnlyWar.Tests/Fixtures/TestPersonnelComposition.cs` is the test assembly's equivalent; that alone
cleared the 44 tracked Orders/UI fixture failures. Separately, `TestExecutionContextFactory` never
supplied `CreateBattleSquad`, so eight interception/stealth mission tests threw instead of exercising
their sizing rule; the fixture now composes it as `TurnController` does. This is SB-05b fixture debt
found here, not a behavior change.

**Verification (2026-09-06):** `dotnet build OnlyWarGodot.csproj` succeeded with no new warnings.
`dotnet test OnlyWar.Tests --filter "Category!=Slow"` passed **3066 of 3066** (previously 52 failing
across Orders, UI and Missions). `OnlyWar.HeadlessTests` filtered to
`HeadlessBoundaryTests&Category!=Slow` passed all 8. New coverage is
`OnlyWar.Tests/Application/OperationsScreenApplicationTests.cs`: issue-then-undo, single-use tokens,
replaced-session rejection of a queued undo and cancellation, cancellation-undo participant restore,
ineligible-squad rejection leaving no commitment, a reflection audit that the command boundary
exposes no live domain graph, and a source audit that the controller names no mutation service.
The Godot console smoke and manual order/movement/casualty interaction checks were **not** re-run.

**SB-11a operations read path, 2026-09-06:** The Operations screen now receives only detached
projections. `QueryOperations` returns one `OperationsWorkspaceView` — header, map, region dossier,
active orders, mission options, live-order editor, force tree, ship choices and casualty rows — built
by `OperationsScreenProjector` from the session the caller named. `QueryWorldDossier`,
`QueryRegionCards`, `QueryEntry`, `QueryGovernorRequestEntry` and `ResolveForceSelection` cover the
remaining reads, including which region a planet opens on and which participants a force-tree row
key stands for. The controller and view hold no `Sector`, `Squad`, `Order`, `Region` or
`PlayerSoldier`, and an audit test asserts their sources never name one.

Presentation stayed in the host by splitting classification from colour. `UiAccent` is the
projection's vocabulary and `OnlyWarStyle.Resolve` is the one place it becomes a Godot colour;
faction colours travel as ARGB and are converted at the same boundary. `HierarchyTreeItem`,
`PlanetaryForceTreeBuilder`, `RegionPresentation`, `ForceOrdering`, `MapIconKeys`, `MissionIconKeys`
and `SquadIconKeys` moved to `Modules/OnlyWar.Application/Queries/`; the icon atlas, styles and
`DossierCard` rendering remain host-owned, with `DossierCard.Create(DossierCardView)` as the mapping.
Region adjacency for keyboard navigation is now carried on the map card, so `PlanetRegionMapView`
no longer walks the region graph. `SystemInspector` takes the application for its world dossier.
`PlanetaryOperationsServiceTests` were reconciled onto the projection boundary rather than the
deleted host builders.

Remaining SB-11a work is verification only: the Godot console smoke and manual order issue/cancel/
undo, land/embark and casualty-detach checks.

**SB-11b session, report and battle screens, 2026-09-06:** `ISessionControlApplication` moves
campaign identity, recoverability and every save behind the application. `SaveCampaign` covers the
manual, overwrite, protected pre-turn, post-turn autosave and initial autosave paths; it captures the
revision before writing so a failed write cannot mark a later state recoverable, and rejects a token
from a replaced session. `QueryStatus`, `MarkChanged`, `CampaignStatusChanged`,
`RequiresRecruitmentSetup`, `QueryEndTurnPreflight` and `WriteDiagnosticCapture` replace the
singleton reads in `MainGameScene.CampaignControls` and `StartMenu.ReleaseControls`;
`GameStorage`/`SaveGameManager` composition at startup is the host's only remaining concrete wiring.
`ICommandScreenApplication` supplies the Command Brief and a `ChronicleView` (filters, page, has-older,
has-any), so the Command screen no longer reaches for `PlayerForce` or the chronicle browser, and it
adopts the shared `Configure`/`SessionChanged` pattern. Battle Review's only singleton read was a
dead guard around presentation constants and is gone. `SessionControlApplicationTests` cover
stale-session and no-storage save rejection, status/dirty/close behaviour, the preflight and
recruitment gate, a detached-boundary audit and a source audit that these four files name neither
`GameDataSingleton` nor `CurrentCampaignSaveWriter`.

**Verification (2026-09-06):** host build clean; `dotnet test OnlyWar.Tests --filter
"Category!=Slow"` **3083 of 3083** passed; `OnlyWar.HeadlessTests` 11 of 11. The Godot console smoke
and manual save/load/new-game and battle-review flows were **not** run.

**SB-11b main screen, turn report and dialogs, 2026-09-06:** `IMainScreenApplication` closes the
rest of the ticket. `QueryHeader` supplies the formatted date and requisition, `QueryStartup` decides
which world the campaign opens on and whether the founding directive is outstanding,
`AcknowledgeOpeningBrief` performs that one-shot write, `ResolveTurn` advances the turn and — only
once resolution and report construction both succeed — replaces the persisted
`LastTurnReportSnapshot` and returns a `TurnReportView`, `QueryLastTurnReport` restores the saved
one, and `QueryNeophytePlacementTargets`/`PlaceNeophyte` own scout-squad eligibility, the
"no squad available" wording and the promotion. Each command is session-token guarded, so a replaced
campaign cannot be advanced, acknowledged or posted to through a stale token. `MainGameScene` no
longer constructs its own `GameSession`, calls `RecruitmentPromotionService`, writes
`BriefingAcknowledged`, or assigns the report snapshot, and it asks `TryAttachCurrentCampaign`
instead of reading `IsInitialized`.

Turn-report *content* moved with it. `EndOfTurnDialogController` was the report builder: it decided
the intel-tier redaction of enemy activity, which battles could be reviewed, and every outcome
wording, from live `MissionContext`, `Region`, `RegionFaction` and `StrategicCombatResult`. That is
campaign policy and now lives in `TurnReportProjector` (`Modules/OnlyWar.Application/Queries/`), with
`ConstructionReportBuilder`, `MissionReportHeadlineBuilder`, `MissionReportSummaryBuilder`,
`NpcMissionReportBuilder`, `ReconOperationReportBuilder`, `LastTurnReportSnapshotBuilder` and
`EndOfTurnReportEntry` moved there too. The dialog now renders a `TurnReportView`, opens the debrief
and hands a `BattleHistory` to Battle Review; `MissionDebriefLineGrouper` and
`BattleReplaySummaryBuilder` stay host presentation. The briefing dialog was already plain text, and
`Scenes/BattleMap` is an empty stub with no campaign access.

`MainGameScene`'s remaining `GameDataSingleton` reads resolve live planets, fleets, squads, regions,
missions and orders **only** to hand them to screens SB-11c has yet to migrate; they exit with those
screens, not with this ticket. SB-12 remains unimplemented. The medical, operations,
session and main-screen contract surfaces all live in Application alongside their portable projection
types; final contract placement and internal-visibility cleanup remain SB-12.

**SB-11c roster, fleet, training and map screens, 2026-09-06:** Every remaining campaign screen now
takes an application. `IFleetScreenApplication` covers the Classis tree and the three fleet dialogs;
transfer legality moved to `FleetTransferService` (Campaign) and route planning, divide and merge
became ID-and-token commands. `ISystemInspectorApplication` returns one `SystemInspectorView` per
selection, deciding which task forces are in orbit, which one is chosen when the player has not, and
whether its actions are legal. `ISectorMapApplication` supplies the subsector partition, the Voronoi
tessellation and its adjacency, planet/fleet markers, turn-varying label facts and the selected
system's orbiting fleets — the map keeps palette, smoothing and drawing and no longer calls
`SubsectorBuilder` or `VoronoiSubsectorMapper` itself. `IChapterScreenApplication` builds the whole
browser (breadcrumbs, menus, detail, transfer options and the ordered context list) and owns the
transfer, Black Carapace and recall prompts; `IMusterScreenApplication` owns the staged Muster plan,
so it is session-bound and dropped with its campaign. `ILoadoutScreenApplication` covers the squad
loadout screen and the chapter/theater doctrine dialog, with the operational doctrine staged as
three plain values. `ITrainingScreenApplication` owns the 10th Company snapshot, forecast, staff
validation and scout-training writes. `ICampaignNavigationApplication` resolves Command navigation
targets and squad locations, which is what let `MainGameScene` drop `TryGetPlayerFleet`/`Squad`/
`Soldier` and `TryGetPlanet`/`Region`/`Mission`/`Order` along with the fleet-context and
squad-double-click lookups; three dead handlers (`OnRegionDoubleClicked`,
`OnCharacterDoubleClicked`, `OnOrbitalSquadDoubleClicked`) and the unused `RosterFormat` went with
them. `TreeNode`, `ChapterBrowserModels`, `SoldierDetailBuilder` (now taking an explicit
`SoldierDetailContext`), `ChapterMusterViewModelBuilder`, `ElementLoadoutSections` and the
recruitment screen models moved to `Modules/OnlyWar.Application/Queries/`, joined by new
`FleetScreenProjector` and `DiplomacyScreenProjector`. Every screen projection now passes the
recruitment program and operational doctrine into `SquadRowViewModelBuilder.Build` explicitly
(`resolveLegacyContext: false`) — the muster and planetary force-tree builders were the last two
still taking the default, and were corrected here. The one remaining caller of the
`CurrentCampaignReadinessContext` fallback is `BuildBattleSnapshot`, which renders a historical
battle formation and has no live force to be given; retiring the fallback is SB-12.

**Recorded exceptions for SB-12.** The loadout surfaces exchange authored rules templates —
`WeaponSet`, `EquipmentLoadout`, `EquipmentRulesCatalog` and the validation context built from them.
Those are immutable reference data, not the campaign graph, and the detached-boundary audit for
`SquadLoadoutView` allows exactly those namespaces. The only remaining `GameDataSingleton` readers
under `Scenes/` are `Debug/MainGamePreviewBootstrap.cs` and `Debug/ReleaseSceneWiringSmoke.cs`
(startup composition for the preview and smoke scenes) plus a comment in `GodotLogBridge.cs`; a test
asserts that allowlist exactly. `MainGameScene` builds the application in `_EnterTree` rather than
`_Ready`, because the sector map is a scene child whose `_Ready` would otherwise run first.

**Verification (2026-09-06):** `dotnet build OnlyWarGodot.sln` clean, no warnings.
`dotnet test OnlyWar.Tests --filter "Category!=Slow"` passed **3119 of 3119** (3096 before this
ticket); `OnlyWar.HeadlessTests` filtered to `Category!=Slow` passed 11 of 11. New coverage is
`OnlyWar.Tests/Application/RosterAndMapScreenApplicationTests.cs` (23 tests): berth/shared-location
transfer legality and its stale-token refusal, the divide rule refusing to empty a task force,
actionability of a warped or distant fleet, the inspector's preselection and its fallback when the
chosen fleet has left orbit, the sector map dropping a warped fleet from its markers, the selected
system's orbiting fleets, the diplomacy empty state, the chapter browser's no-chapter and squad-
roster renders, a stale-token transfer refusal, the Muster plan being discarded with its campaign,
squad-loadout read/write round trip, operational doctrine staging and its stale-token refusal, the
training lock and its stale-token refusal, navigation resolution, detached-boundary audits over
every new view type, and two scene-wide source audits (no gameplay screen names the compatibility
singleton or a mutation service). `ChapterControllerTests`, `TrainingUnitScreenControllerTests` and
the renamed `FleetScreenProjectionTests` were reconciled onto the application rather than deleted.
The Godot console smoke and the manual fleet-transfer, chapter-transfer, muster-commit, loadout and
recruitment-setup flows were **not** run.

**PRD review (SB-11c).** Every migrated group preserves its wording, ordering and reasons verbatim:
Galaxy View and the sector map (PRD §4.2), Chapter/Muster and Chapter doctrine (§4.5), Squad
loadouts (§4.6), Recruiter/10th Company (§4.9), Fleet Management and its dialogs (§4.17), governor
relations behind the Diplomacy board (§4.16) and squad-row legibility (§4.26) reviewed; **no PRD
change required**. Two
oddities were preserved rather than corrected, because they are pre-existing behavior outside this
ticket's scope: the system inspector's empty state reads "No task forces are in orbit." rather than
naming the missing system, and the sector map passes 0–255 byte channels into Godot's 0–1 `Color`
constructor when tinting a star. Both are recorded here so the reader knows they were seen and left
alone. The doctrine dialog's planetary-theater scope (`OpenPlanet`) has no caller and was migrated
as a scope parameter rather than deleted; whether to keep that tier is a product decision, not an
architecture one.

**Verification (2026-09-06):** `dotnet build OnlyWarGodot.csproj` clean (pre-existing obsolete-member
warnings only). `dotnet test OnlyWar.Tests --filter "Category!=Slow"` passed **3096 of 3096**;
`OnlyWar.HeadlessTests` filtered to `Category!=Slow` passed 11 of 11. New coverage is
`OnlyWar.Tests/Application/MainScreenApplicationTests.cs`: header from the active session and empty
after close, startup opening on the orbited world and falling back to a charted one, the opening
brief refusing a replaced session's token, the saved and absent last-turn report, `ResolveTurn`
refusing a stale token while leaving both campaigns' reports untouched, neophyte placement's
unavailable reason and stale-token refusal, a detached-boundary reflection audit over the new view
types (`MissionDebriefLine` is the one allowed passenger — a mission runtime record and battle
replay, not the campaign graph), and source audits that the dialog names no mission or intel rule and
that the scene no longer resolves those campaign facts itself. PRD §§4.12/4.13 reviewed; no PRD
change required — report wording, redaction and the empty-state message are unchanged. The Godot
console smoke and manual end-turn/debrief/replay and new-game briefing flows were **not** run.

#### SB-12 — Enforce the final module graph and remove migration bridges

**Scope:** Compare the actual graph against SB-00; finish internal visibility and contract cleanup. Remove tracked compatibility APIs/global fallbacks after migrating all callers. Add architecture checks for forbidden assembly references, host/controller implementation bypasses, singleton/static RNG access outside approved composition and SQL/Godot dependencies outside their adapters. Inspect shared domain mutation exposure so project separation cannot disguise unrestricted campaign access.

**Acceptance:** Each target implementation has its own assembly boundary (UI is the Godot host); all feature extracts from earlier tickets are complete. The remaining campaign module contains only its assigned domains/coordinators. No circular references or broad production `InternalsVisibleTo` grants recreate unrestricted access. A focused negative fixture or equivalent check demonstrates that forbidden references are detected. Test-only friend access is narrow and documented. The global-access allowlist names exact composition locations and reasons; it cannot exempt whole subsystems.

**Verification/docs:** Build all projects, architecture tests and affected headless smoke. Update TDD §§2–3, §8 and §9 with actual enforcement and any explicitly scoped residual debt. Inspect tests for singleton setup that can now be removed, without requiring unrelated fixture rewrites.

**SB-12 global-state bridges and enforcement, 2026-09-06.** The working tree was mid-removal and did
not build; that removal is now finished and the bridges below are gone rather than deprecated.

`CampaignRuntimeDefaults` (the ambient rules/sector accessor) and `CurrentCampaignReadinessContext`
are deleted. `IRequest.IsRequestStarted`/`ProcessTurn` take `GameRulesData`, which
`GovernorTurnProcessor` supplies from the session and `DiplomacyScreenProjector.Build` from
`_activeSession.Rules`; `PresenceRequest` demands rules only where it actually measures presence, so
an expired deadline still resolves without them. `SoldierPresenceService` and
`CommandAttentionEvaluator` take the program and doctrine explicitly — the evaluator now reads the
doctrine of the sector it is evaluating rather than of whichever campaign was installed, which is the
same value in production and a behavior change only for a test whose campaign was never installed
(`EndTurnPreflightTests` now states the minimum squad strength its one- and two-brother squads are
meant to clear). `SquadRowViewModelBuilder.Build` has no `resolveLegacyContext` parameter left, and
the trailing-argument call sites that removal left half-edited are repaired.

The parameterless `FactionStrategyController` facade is gone; its 26 test call sites name
`StaticRNG.Instance` themselves. `StrategicCombatResolver` and `FieldCareService` take the session's
`IRNG` and throw on null instead of falling back to the process stream — `ChapterUpkeepProcessor` and
`TurnController` pass `_session.Random`.

`OnlyWar.Tests/Architecture/ModuleBoundaryEnforcementTests.cs` is the new enforcement fixture: it
scans the shipping sources (Modules, Scenes, Helpers, Composition, Builders, Models, minus bin/obj)
for `GameDataSingleton`, `StaticRNG`, SQL and `using Godot`, each against an allowlist that names
exact files and reasons. Comment lines are ignored deliberately — a rule that forbade the words would
only stop people writing down where a bridge is installed. A fifth test runs the same scanner over a
synthetic tree that does violate the rule, so a check that could never fail cannot pass unnoticed.
The assembly-reference matrix stays in `HeadlessBoundaryTests`, which is the half that can see
project references.

**Verification (2026-09-06):** `dotnet build OnlyWarGodot.sln` clean (pre-existing obsolete-member
warnings only). `dotnet test OnlyWar.Tests --filter "Category!=Slow"` passed **3124 of 3124**;
`OnlyWar.HeadlessTests` filtered to `Category!=Slow` passed 11 of 11. Godot smoke not run (SB-13).

**Still open in SB-12, with why each is its own piece of work:**

1. **SQL in Campaign.** All of it sits under `Modules/OnlyWar.Campaign/Helpers/Database/**` (~41
   readers) and is allowlisted as one folder. It cannot simply move: Persistence may reference only
   Domain and Contracts, and these readers materialize live campaign models that still live in
   Campaign. Moving the SQL means first deciding which of those models move to Domain.
2. **Operations→Battles.** 19 Operations sources still hold `BattleSquad` handles; removing the
   reference is the replay/handle projection §3.6 describes, not a reference edit.
3. **`GameDataSingleton` itself.** `CampaignApplication` already owns the active session, but
   `CampaignRecoverabilityTracker` hangs off the singleton and 35 test files install campaigns
   through it. The allowlist above makes the remaining readers exact and few; retiring the type is a
   separate pass.

### Phase 7 — Verify, reconcile and retire

#### SB-13 — Verify the integrated game and package

**Scope:** Run the final verification gates in §6 on the integrated revision. Check resource lookup, rules/save file paths, Godot scene wiring and exported assembly/resource inclusion. Correct integration regressions within their owners. Do not broaden into unrelated gameplay changes.

**Acceptance:** Headless builds/tests, host build, relevant data/generation integrations, default non-Slow suite and representative Godot workflows pass. Verify the Windows export/package with `Scripts/Publish-Windows.ps1` and the documented release smoke when the required engine/export templates are available; verify using temporary/output paths, not player saves. Record unavailable gates as unverified with an owner and next action. SB-13 remains blocked/unfinished until required evidence is obtained or the user explicitly accepts the limitation.

**Docs:** Record the integrated revision, exact commands, meaningful results and manual smoke outcomes in the ledger/handoff. Update durable test/run instructions where project extraction changed commands or resources. Proceed to final retirement only after the gate is satisfied.

#### SB-14 — Reconcile TDD/PRD and retire this planning document

**Scope and acceptance:** Follow every item in §7. The final commit updates authoritative documents and removes this active plan together with its active-index link. Do not copy phase histories to `Reference/`. No unresolved requirement of this architecture project may disappear when the plan is removed.

## 6. Verification policy

Follow `AGENTS.md`: Python is unavailable. Use the SQLite CLI for ad hoc database inspection, not one-off C# database utilities. Do not change the production rules DB, save schema or player saves as a convenience for testing.

During implementation run the smallest relevant existing test slice, excluding `Category=Slow`, and broaden only for a concrete cross-boundary risk. Representative commands before project/test renames:

```powershell
dotnet test OnlyWar.Tests\OnlyWar.Tests.csproj --filter "FullyQualifiedName~OnlyWar.Tests.Domain.ChapterOperationalDoctrineTests&Category!=Slow"
dotnet test OnlyWar.Tests\OnlyWar.Tests.csproj --filter "FullyQualifiedName~OnlyWar.Tests.Orders&Category!=Slow"
dotnet test OnlyWar.Tests\OnlyWar.Tests.csproj --filter "FullyQualifiedName~OnlyWar.Tests.Battles&Category!=Slow"
dotnet test OnlyWar.Tests\OnlyWar.Tests.csproj --filter "FullyQualifiedName~OnlyWar.Tests.Data&Category!=Slow"
dotnet test OnlyWar.Tests\OnlyWar.Tests.csproj --filter "FullyQualifiedName~OnlyWar.Tests.Generation&Category!=Slow"
dotnet test OnlyWar.Tests\OnlyWar.Tests.csproj --filter "FullyQualifiedName~OnlyWar.Tests.Turns&Category!=Slow"
dotnet test OnlyWar.Tests\OnlyWar.Tests.csproj --filter "FullyQualifiedName~OnlyWar.Tests.UI&Category!=Slow"
```

These are choices, not a checklist to run for every ticket. Update paths when tests move and inspect the reported test count so an empty filter is not accepted as a pass. Add tests for ownership/invariant risks rather than tests mirroring an extraction's private method structure.

At SB-13, run `dotnet test OnlyWar.Tests\OnlyWar.Tests.csproj --filter "Category!=Slow"` plus any new test projects. Expect the full suite to take at least five minutes. Review Slow-category tests and run specifically relevant save/generation integrations if they are the only coverage for required behavior. Do not enable `ScenarioTraceDiagnostics` or balance sweeps by default.

Required integrated scenarios:

- Same explicit personnel context gives consistent readiness in UI, order issue and engagement assembly; active combat does not reapply deployment policy mid-fight.
- Battle casualties flow through aftermath, field/daily/weekly care, reports and save/load once, in the existing order.
- Rejected order, attachment, evacuation or procedure command leaves state unchanged; valid cancellation/undo restores commitments correctly.
- Fresh generation and scenario warm-up operate without publishing an incomplete campaign. Failed load/generation preserves the previous session.
- Save round-trip preserves identity links, orders, wounds/procedures, postings, doctrine, reports and required derived state; atomic-save recovery remains covered.
- After switching sessions, commands and views use the new campaign. Independent test sessions do not implicitly adopt each other's rules/personnel.
- Headless core runs without Godot initialization; the exported game includes the extracted assemblies, name resources and database assets.

## 7. Documentation lifecycle and completion gate

### During each ticket

- **TDD:** Update implementation facts at integration, including moved ownership, contracts, lifetimes, actual dependency direction and changed test entry points. Use existing relevant sections; keep proposed future state in this plan. Update document dates when editing. Reconcile references/TOC entries if sections move.
- **PRD:** Review affected behavior and acceptance requirements. If behavior is preserved and text remains accurate, explicitly record “reviewed; no PRD change required” in the ticket evidence. Do not add assembly names, work-item checklists or architecture history to product requirements. If implementation exposes a discrepancy, record whether code must be corrected or a separately agreed product decision is needed.
- **Plan:** Update status, owner, dependencies and evidence. Record adapters/deferred work with an owner and exit ticket. Keep it accurate while active, without retaining lengthy logs or test-count diaries.

### Final SB-14 checklist

- [ ] All SB-00–SB-13 deliverables are integrated and accepted; all child tickets and tracked bridges are reconciled.
- [ ] TDD project structure, architecture, persistence, readiness/medical, operations, battles, generation, UI and testing sections describe the shipped implementation, not the initial proposal.
- [ ] TDD debt entries (especially globals, transitional turn APIs and ID allocation) reflect evidence. Do not claim independent RNG streams, replay guarantees or thread safety introduced by dependency injection alone.
- [ ] PRD requirements for new game, deployment/orders, medical/recovery, battle review, turn flow and save/load have been checked against integrated behavior. Correct stale wording only where supported; keep new product decisions explicit.
- [ ] Necessary residual work outside this project's completion criteria has a named durable owner in TDD technical debt, PRD backlog, or a separately scoped active plan. Missing required boundaries/tests remain blockers, not “deferred” completion.
- [ ] Durable design rationale is distilled into the TDD. Retain a `Reference/` appendix only for substantial derivations/decision material the TDD cannot reasonably absorb, with a live citation and no duplicate source of truth, following `Design/README.md`.
- [ ] Remove `Design/Active/SubsystemBoundaries.md` and its listing in `Design/README.md`. Do not retain a completed acceptance diary or move this plan wholesale into `Reference/`.
- [ ] Search for `SubsystemBoundaries.md` references across tracked code/docs and replace them with durable TDD/PRD references. Check links, section references and document dates.
- [ ] Final handoff/PR records integrated revision, verification results, limitations explicitly accepted by the user, and the final documentation changes. Version history retains the completed plan and its evidence.

SB-14's final result is the reconciled authoritative documentation and the plan's deletion. It does not need a lasting “Complete” row in a file whose purpose has ended.

## 8. Copyable agent handoff

> Implement **SB-XX** from `Design/Active/SubsystemBoundaries.md` on the agreed starting revision. Read `AGENTS.md`, the plan's ownership/dependency rules and your ticket's TDD/PRD sections. Confirm prerequisites are integrated. Restrict edits to the agreed scope; coordinate shared contracts/project files/documentation with the integrating agent. Preserve behavior and existing sequencing/RNG policy. Complete the ticket's acceptance criteria and targeted verification. Report changed ownership/APIs, migrated callers, validation commands/results, remaining adapters/issues, and exact TDD/PRD/plan updates. Mark ready for integration only when the ticket is reviewable and its required checks are satisfied. Do not start dependent tickets automatically.
