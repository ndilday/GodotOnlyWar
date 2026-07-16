# OnlyWar — Product Requirements Document

**Version:** Alpha 0.7 Released / Alpha 0.8 Roadmap

**Last Updated:** July 2026
**Author:** Nathan Dilday  

---

## Table of Contents

1. [Product Vision](#1-product-vision)
2. [Target Audience](#2-target-audience)
3. [Core Design Pillars](#3-core-design-pillars)
4. [Feature Specifications](#4-feature-specifications)
   - 4.1 [New Game Setup](#41-new-game-setup)
   - 4.2 [Galaxy View](#42-galaxy-view)
   - 4.3 [Planet Detail Screen](#43-planet-detail-screen)
   - 4.4 [Region Screen](#44-region-screen)
   - 4.5 [Chapter Screen](#45-chapter-screen)
   - 4.6 [Squad Screen](#46-squad-screen)
   - 4.7 [Soldier Screen](#47-soldier-screen)
   - 4.8 [Apothecary Screen](#48-apothecary-screen)
   - 4.9 [Recruiter Screen](#49-recruiter-screen)
   - 4.10 [Battle Review Screen](#410-battle-review-screen)
   - 4.11 [End of Turn Flow](#411-end-of-turn-flow)
   - 4.12 [Turn Simulation — Soldier Lifecycle](#412-turn-simulation--soldier-lifecycle)
   - 4.13 [Turn Simulation — Mission Resolution](#413-turn-simulation--mission-resolution)
   - 4.14 [Turn Simulation — Battle](#414-turn-simulation--battle)
   - 4.15 [Turn Simulation — Living Galaxy](#415-turn-simulation--living-galaxy)
   - 4.16 [Turn Simulation — Governor Relations](#416-turn-simulation--governor-relations)
   - 4.17 [Fleet Management](#417-fleet-management)
   - 4.18 [Save and Load](#418-save-and-load)
   - 4.19 [Narrative Voice & Emotional Impact](#419-narrative-voice--emotional-impact)
   - 4.20 [Turn Simulation — Revolt & Civil Stability](#420-turn-simulation--revolt--civil-stability)
   - 4.21 [Turn Simulation — Faction Relationships & Inter-Faction Intelligence](#421-turn-simulation--faction-relationships--inter-faction-intelligence)
   - 4.22 [Turn Simulation — Orks & Indelible Infestation](#422-turn-simulation--orks--indelible-infestation)
   - 4.23 [Turn Simulation — Supply, Requisition & Pledges](#423-turn-simulation--supply-requisition--pledges)
   - 4.24 [Turn Simulation — Tyranid Invasion & Biomass Consumption](#424-turn-simulation--tyranid-invasion--biomass-consumption)
   - 4.25 [Chapter Mandates & Legacy Objectives](#425-chapter-mandates--legacy-objectives)
   - 4.26 [Planet Tactical Map & Information Legibility](#426-planet-tactical-map--information-legibility)
   - 4.27 [System Menu, Accessibility & Release UX](#427-system-menu-accessibility--release-ux)
5. [Release Scoping](#5-release-scoping)
   - 5.1 [Released (Alpha 0.6 and prior)](#51-released-alpha-06-and-prior)
   - 5.2 [Alpha 0.7 — Committed](#52-alpha-07--committed)
   - 5.3 [Alpha 0.7.1 — Done](#53-alpha-071--done)
   - 5.4 [Alpha 0.7.1 — To-Do](#54-alpha-071--to-do)
   - 5.5 [Alpha 0.8 — Command, Narrative & Continuity](#55-alpha-08--command-narrative--continuity)
   - 5.6 [Alpha 0.8+ — Cross-Faction Simulation (Relationships, Intel, Orks)](#56-alpha-08--cross-faction-simulation-relationships-intel-orks)
   - 5.7 [Post-0.8 Backlog](#57-post-08-backlog)
6. [Open Design Questions](#6-open-design-questions)
   - 6.1 [Aggression Axis Split — DEFERRED](#61-aggression-axis-split--deferred)
   - 6.2 [Morale](#62-morale)
   - 6.3 [New Recruit Intake — V1 COMMITTED FOR 0.8](#63-new-recruit-intake--v1-committed-for-08)
   - 6.4 [Imperial Guard Interactions](#64-imperial-guard-interactions)
   - 6.5 [Inquisition Role](#65-inquisition-role)
   - 6.6 [Navis Nobilite Relations (Post-0.7 Backlog)](#66-navis-nobilite-relations-post-07-backlog)
   - 6.7 [Pop-Based Population Model (raised by Revolt, §4.20)](#67-pop-based-population-model-raised-by-revolt-420)
   - 6.8 [Ork Terminal Control State (raised by Orks, §4.22)](#68-ork-terminal-control-state-raised-by-orks-422)
   - 6.9 [Armory / Wargear Inventory Commitment (raised by Supply, §4.23)](#69-armory--wargear-inventory-commitment-raised-by-supply-423)
   - 6.10 [Pledge Interdiction in Transit (raised by Supply, §4.23)](#610-pledge-interdiction-in-transit-raised-by-supply-423)
   - 6.11 [Tyranid Breeding Structures as Strikable Objectives (raised by Tyranids, §4.24)](#611-tyranid-breeding-structures-as-strikable-objectives-raised-by-tyranids-424)
   - 6.12 [Region-Level Going-Public Generalization (raised by Tyranids, §4.24)](#612-region-level-going-public-generalization-raised-by-tyranids-424)
   - 6.13 [Soldier Attribute Growth & Hero Representation (raised by Field Experience, §4.12)](#613-soldier-attribute-growth--hero-representation-raised-by-field-experience-412)
7. [Glossary](#7-glossary)

---

## 1. Product Vision

OnlyWar is a single-player, turn-based strategy game set in the Warhammer 40,000 universe. The player is the Chapter Master of a Space Marine chapter — responsible for its personnel, operations, fleet, and long-term standing in a sector of space that is perpetually on the edge of catastrophe.

The game is a spiritual successor to the fan-made game *Chapter Master*, extending and modernizing its concepts with a granular individual-soldier simulation, a galaxy that evolves independent of player action, and a tactical battle layer that resolves squad engagements with real consequences for named individuals. Character statistics and combat rules take direct inspiration from the GURPS and HERO tabletop roleplaying systems.

---

## 2. Target Audience

- Fans of Warhammer 40,000 lore, especially Space Marine chapters.
- Strategy and management players familiar with XCOM, Battletech, and Chapter Master.
- Players who enjoy emergent narrative from simulation (RimWorld, Dwarf Fortress).

---

## 3. Core Design Pillars

**Soldier Individuality.** Every Battle Brother is a named individual with a body, skills, wounds, history, and a career arc. Players should feel the loss of a veteran and the promise of a rising scout.

**Living Galaxy.** The sector does not wait for the player. Genestealer Cults grow in the dark, armies maneuver, governors develop opinions, and planets fall or hold independent of whether the player intervenes.

**Strategic Depth.** Resources — brothers, geneseed, fleet capacity, political capital — are finite and competing. Every deployment decision has an opportunity cost.

**Tactical Resolution.** Battles are not abstracted. Squad-level engagements play out on a grid, with individual soldiers acting, suffering wounds, and dying. Outcomes feed directly back into the strategic layer.

**Modifiability.** Content — factions, soldiers, weapons, unit templates, sector configurations — is data-driven and intended to be moddable.

---

## 4. Feature Specifications

Each feature is described as a behavioral specification: what the system does, and what the acceptance criteria are. Observable player-facing outcomes are noted where relevant but the primary concern is correct simulation behavior.

---

### 4.1 New Game Setup

**Description.** The player configures and starts a new campaign. A sector and a Space Marine chapter are procedurally generated.

**Acceptance Criteria:**

- The player can start a new game from the main menu.
- The player can load an existing save from the main menu.
- Starting a new game opens a setup screen where the player names their Chapter and sets (or randomizes) a sector seed before founding. The setup validates input and presents a confirmation/summary step before the campaign launches. The chapter name is applied to the chapter's unit, army, and fleet and recorded in its founding history; the seed deterministically drives sector generation (re-using a seed reproduces the same sector).
- On new game creation, a sector is generated with a configurable or default number of planets distributed across subsectors.
- Planets begin in a variety of states: some are fully imperial-controlled, some are held entirely by hostile factions, some are actively contested between factions, and some are uninhabited or unknown. Not every planet will have an imperial presence at game start.
- A portion of imperial-held planets contain hidden Genestealer Cult populations at game start.
- New planets may become known during a campaign — for example, a previously uncharted Ork world launching a Waaagh! can introduce a new faction presence into the sector mid-game.
- A Space Marine chapter of 1,000 brothers is generated, evaluated, and assigned to companies and squads according to the Codex Astartes structure (HQ, 1st through 10th Companies).
- Brothers are sorted into Tactical, Assault, Devastator, and Scout roles based on their individual aptitudes.
- Each subsector has a designated capital world determined by an importance score. For 0.7, this score is based on population size. Strategic classification (Hive World, Forge World, etc.) is a post-0.7 addition that will be incorporated into the score when implemented.
- A **governance hierarchy** is derived on top of the capital designation (added with the Opening Scenario, §5.2): each subsector has a **seat of government** — its highest-importance *Imperial-controlled* world, the one with a governor — and the top seat sector-wide is the **sector capital**. The governor of the sector capital is the **Sector Lord** (the ranking secular Imperial authority in the sector); the governor of each subsector seat is its **subsector governor**. This governance seat is kept distinct from the population-based *warp* capital (§4.17) so an enemy-held warp hub is never treated as a seat of government. The designation is recomputed deterministically during sector generation and on load — like subsectors and warp lanes — rather than persisted; the governor characters themselves are persisted.
- The chapter's fleet is initialized with at least one task force.
- The game date is set to an appropriate point in the 41st Millennium.
- The player is placed in the main Galaxy View on game start.

---

### 4.2 Galaxy View

**Description.** The top-level strategic view. Displays the sector as a map of planets, color-coded by faction control. The player navigates here between turns.

**Acceptance Criteria:**

- Each planet is displayed at its generated position and colored to indicate its controlling faction.
- Planets known to the player's faction display their name. Planets not yet known to the chapter are not displayed.
- Planet discovery is driven by events external to the player's direct control. A planet becomes known when it is identified as the source of an incursion into the sector — this intelligence reaches the chapter through broader Imperium channels, not through player reconnaissance. The player's forces do not themselves discover new planets through exploration.
- *Post-0.7:* If the player's forces capture an opposing officer during a mission, interrogation may reveal that officer's planet of origin, adding it to the known sector map.
- Clicking a planet opens the Planet Detail Screen for that planet.
- Subsector regions are visually delineated on the map.
- The current game date is displayed in the top bar.
- The player can press End Turn from this screen to advance time by one week.
- The player can save the game from this screen.
- The player can navigate to the Chapter Screen from this screen.
- Chapter fleet task forces are visible on the map at their current position (at a planet or in transit between planets).
- Clicking a task force opens a menu that allows the player to: divide the task force into two separate task forces, merge it with another task force at the same location, or plot a course to another system.

---

### 4.3 Planet Detail Screen

**Description.** Displays details for a selected planet: its physical characteristics, military situation, governor, and the player's deployed forces. Also the primary interface for loading and landing squads.

**Acceptance Criteria:**

**Planet Data**
- Displays the planet's name, size classification, type, and approximate imperial population.
- Displays the estimated strength of enemy forces present on the planet.
- Displays planetary military forces (PDF garrison size).

**Governor**
- Each planet with an imperial population has a named planetary governor with a set of personality traits.
- The governor's current opinion of the chapter is displayed.
- When the governor has an active request for the chapter, this is indicated on the planet screen.
- Governors age over time and are replaced when they reach an appropriate age.

**Fleet & Troop Management**
- Displays all chapter ships in orbit above the planet in a fleet tree, organized by task force and ship.
- Displays all chapter squads landed on the planet organized by region.
- The player can select a ship and a region and land squads from the ship into the region.
- The player can select a landed squad or unit and load it back onto a ship in orbit.
- Landing and loading respect ship capacity: a ship cannot be loaded beyond its troop capacity.
- Double-clicking a landed squad opens the Region Screen for that region.
- Double-clicking a squad on a ship opens the Squad Screen for that squad.

---

### 4.4 Region Screen

**Description.** Detailed view of a single planetary region. The primary interface for assigning orders to squads deployed in the region.

**Acceptance Criteria:**

- Displays the region name and a list of all player squads currently deployed in it.
- Selecting a squad displays its current orders (mission type, target, aggression level).
- The player can open an order assignment dialog for the selected squad.
- The order dialog allows the player to set: mission type, target region/faction, aggression level, and whether the mission is the primary operation.
- The player can unassign a squad's orders, returning it to a standing garrison posture.
- The player can copy a squad's orders and paste them onto another squad.
- The player can navigate to adjacent regions directly from the region screen.
- Double-clicking a squad in the region navigates to the Squad Screen for that squad.
- Squads that cannot be deployed (e.g., due to all members being injured) are visually distinguished and cannot be assigned offensive orders.

---

### 4.5 Chapter Screen

**Description.** The player's view into the full order of battle — all companies, squads, and brothers. Primary interface for managing squad composition and pre-battle loadouts.

**Acceptance Criteria:**

- Displays the full chapter structure: Chapter HQ, companies 1–10, Armory, and Librarius.
- Selecting a company displays the squads within it.
- Selecting a squad displays the members of that squad, sorted by rank then seniority.
- The player can select a marine and move him to a different squad, subject to the destination squad's template requirements.
- Required squad slots (e.g., a squad must have a Sergeant) must be filled before the transfer is permitted.
- Optional slots (e.g., specialist roles with a min of 0) may be filled or left empty.
- The player can view and set the loadout for a squad, choosing from available weapon options defined by the squad's template.
- Double-clicking a marine opens the Soldier Screen for that individual.

---

### 4.6 Squad Screen

**Description.** Detailed view for a single squad. Displays all members, their individual status, and the squad's current orders.

**Acceptance Criteria:**

- Displays squad name, template type, and current region or ship.
- Lists all squad members with their role, current wound status, and deployment eligibility.
- Displays the squad's current orders.
- Injured members are visually distinguished from healthy ones.
- Members who cannot be deployed due to injury are indicated.
- Double-clicking a member opens the Soldier Screen for that individual.

---

### 4.7 Soldier Screen

**Description.** Detailed view for an individual Battle Brother. Displays his full history, physical condition, skills, and career record.

**Acceptance Criteria:**

- Displays the marine's name, role, current squad, and time in service.
- Displays the status of each hit location: healthy, wounded (with severity), crippled, or severed.
- Displays all skills and their current values.
- Displays the marine's personal history log: recruitment, training, promotions, notable actions, wounds received, and awards. The history event vocabulary and its narration follow the Narrative Voice specification (4.19).
- Displays confirmed kill counts by faction and weapon type.
- Displays any awards or commendations. For an award type with multiple tiers, only the most recent / highest level the marine has earned is displayed. *(Post-0.7: surface these as icons in the top panel of the screen — see §5.7.)*
- Displays geneseed implant date and maturity status.
- Displays estimated weeks to recovery if the marine is currently injured.
- The player can initiate a squad transfer from this screen (selecting a destination squad).

---

### 4.8 Apothecary Screen

**Description.** Manages the medical state of the chapter and the geneseed supply.

**Acceptance Criteria (Implemented):**
- Lists all marines currently in recovery, with the severity of their injuries and the number of weeks until each is expected to recover.
- Displays the current count of mature geneseed stored in the Reclusiam.
- Displays the number of marines with one and two mature progenoid glands.
- Displays the expected time to recovery alongside each injured soldier on the Squad Screen (not just the Apothecary Screen).
- When a marine dies, the battle results and death record note whether geneseed was successfully recovered.
- **Cybernetic replacements** are available as a treatment option:
  - *Eligibility:* any hit location that has reached its cripple threshold but not its lethal threshold — limbs (arms, hands, legs, feet) and vital locations (head, torso) at crippling severity.
  - *Requirements (staff):* an Apothecary **and** a Techmarine must be **co-located with the wounded marine to begin** the procedure — both present in the same place (the same ship, or the same region) as the marine's squad. This is a start-of-procedure check, not a duration lockout: because the surgery itself is a small time-sink against a week-long turn, the staff are not committed for the convalescence weeks that follow and are free to redeploy once the procedure is under way.
  - *Requirements (location):* the marine's squad must be at a site that can support augmetic surgery — aboard a ship, at the chapter's home world, or in an imperial-controlled region of a sufficiently advanced world (a Hive, Forge, or Civilised world; agri/feudal/feral/death worlds lack the infrastructure). A consequence of co-location plus this rule is a real logistics layer: a brother wounded while garrisoning a distant, undeveloped world must be evacuated home or to the fleet — or an Apothecary and Techmarine must campaign alongside him — before he can be treated.
  - *Cost:* a combination of time (weeks in the Apothecarium) and **Requisition**, the chapter's abstract supply/favor currency (§4.23). Cybernetic replacement is the faster, cheaper option; vat-grown replacement is rarer, slower, and more Requisition-intensive. Specific week counts and Requisition costs live as centralized rules constants, not UI literals.
  - *Presentation of requisites:* when viewing a soldier who requires a replacement, the Apothecary screen lists **every** prerequisite for each procedure option explicitly — co-located Apothecary, co-located Techmarine, a valid surgery site, and sufficient Requisition — rather than only enabling or greying the assign button. Each requisite is shown as a met/unmet line, **met requisites in green and unmet requisites in red**, so the player can see at a glance both that a procedure is unavailable and exactly why (e.g. "no Techmarine present" or "insufficient Requisition"). The assign action is enabled only when all requisites are green.
  - *Restored capability:* a cybernetic restores full capability to the replaced location in the current implementation. Granular per-attribute effects and embedded weapon options are a post-0.7 design item.
  - *Dreadnought interment* (for soldiers with multiple severe/unsurvivable injuries) is the extreme end of this same decision space and should be designed for, though it is out of scope until Dreadnoughts are implemented.

---

### 4.9 Recruiter Screen

**Description.** Manages the chapter's Scout Company and the pipeline from initiate to Battle Brother.

**Acceptance Criteria (Implemented):**
- Displays all Scout squads with their current members.
- Indicates which scouts are ready to advance to full Battle Brother status, based on their training evaluations.
- Scouts who are currently deployed on a mission are excluded from training progress that week.
- The player can designate a focus for a given Scout squad's training (e.g., prioritizing ranged skill vs. melee skill vs. leadership), which affects which skills accumulate points faster during that week.

**Acceptance Criteria (Planned — Post-0.7):**
- **Sergeant training cap.** A Sergeant's own skill level in a category is a hard cap on how far he can train a scout in that category — a scout cannot be trained beyond his instructor's level. Soldier ratings are updated every four turns; each time ratings update and a scout remains at his Sergeant's instructional limit in one or more skills, the Recruiter surfaces a notification. The player then has three options: leave the scout in the squad and accept no further improvement in the capped skill; transfer him to a Scout squad whose Sergeant has a higher level in that area; or promote him to a line squad, where development continues through deployment and combat experience rather than structured training.
- The Armory allows the designation of potential Techmarines to be sent to Mars for training.

**Acceptance Criteria (Planned — 0.8 Recruitment v1):**
- Recruitment rights are unlocked through sufficiently strong governor relationships, chapter ownership, or an explicit manpower pledge; they identify which worlds may supply candidates.
- Starting an intake consumes Requisition and available gene-seed. Both costs are shown before confirmation, and an intake cannot begin if either resource is insufficient.
- Intake throughput is limited by chapter training capacity: available Scout Sergeants and any future recruitment/training facilities. Recruitment is a pipeline, not an instant purchase.
- Candidates progress through aspirant/neophyte training into Scout squads before becoming full Battle Brothers. The Recruiter shows source world, elapsed/remaining time, training capacity used, and the expected destination squad or holding pool.
- Recruitment source and method may affect candidate quality, intake time, population/political cost, or governor opinion. The first implementation needs at least one clearly differentiated trade-off rather than several cosmetically identical buttons.
- Recruitment is managed through standing assignments and relationships, not a repeatable one-off “find recruits” mission.
- Advanced methods, detailed population extraction, and specialized recruitment facilities may follow later; v1 must close the Chapter's attrition/recovery loop without requiring the complete typed-materiel economy.

---

### 4.10 Battle Review Screen

**Description.** A post-battle replay screen that allows the player to review the turn-by-turn progression of a completed engagement.

> **Note:** The acceptance criteria below describe the original grid-plus-turn-log implementation. The **Battle screen visual overhaul** (see §5.2) has since largely replaced this layout with the four-region replay/report display it specified (force hierarchy tree, replay viewport with playback controls, selected-formation summary and event chronicle, and a battle timeline / casualties-by-round table); the structural slice has landed (including the collapsible icon-bearing force tree and shared-theme styling) and only the richer replay-viewport playback/animation and overlays remain. The criteria below are retained for the underlying replay/seek behavior they still describe — see §5.2 for the current per-region status.

**Acceptance Criteria:**
- Displays a 2D grid showing the positions of all squads at each turn of the battle.
- Player squads are shown with a distinct icon and color; opposing squads with a different icon and their faction color.
- A text log panel displays all actions taken during the currently displayed turn (movement, shots fired, hits landed, wounds inflicted). Log entries name their actors and follow the Narrative Voice specification (4.19), adding flavor on critical hits, kills, and last stands.
- The player can step forward and backward through turns.
- The Previous Turn button is disabled on the first turn; the Next Turn button is disabled on the last turn.

---

### 4.11 End of Turn Flow

**Description.** Advancing time by one week triggers all simulation systems in sequence and presents a summary to the player.

**Acceptance Criteria:**
- Pressing End Turn advances the game date by one week.
- All active player squad orders are resolved (missions run, battles fought, construction progressed).
- All non-player faction orders are resolved (attacks, patrols, construction, population growth).
- Wounded marines advance one week toward recovery.
- Non-deployed marines accumulate one week of training.
- Governor personality logic runs, potentially generating new requests.
- A turn report dialog is displayed after resolution, summarizing: battles fought, casualties, mission outcomes, and any notable strategic events.
- The player can page through turn reports if multiple missions resolved in the same turn.
- Generated report text follows the Narrative Voice specification (4.19): individuals are named, notable events are surfaced via the notability classifier, and outcomes are framed against the orders the player issued.

*Field experience:* Marines deployed on a mission earn skill growth from the operation itself — scaled by how hard the mission's skill checks were — rather than that week's garrison training, so no deployment is a wasted week for development. See §4.12 (Field Experience).

---

### 4.12 Turn Simulation — Soldier Lifecycle

**Description.** The rules governing how individual Battle Brothers develop, age, sustain wounds, and die.

**Acceptance Criteria:**

**Generation**
- Each marine is generated with attributes (Strength, Dexterity, Constitution, Intelligence, Perception, Ego, Charisma) drawn from a species template with variance.
- Each marine has a set of skills that begin at template-defined base values.
- Each marine has a distinct body composed of named hit locations, each with an armor value, wound threshold, cripple threshold, and sever threshold.
- Marines with detectable psychic ability are flagged at generation and have a separate training path.

**Training**
- Each week a marine is not deployed on a mission, he gains skill points in categories appropriate to his role and training assignment.
- Scouts in training gain points according to their current training focus.
- Marines deployed on a mission do not accumulate garrison *training* points that week; they instead earn *field experience* from the operation (see Field Experience below).

**Field Experience (Learn by Doing)**
- The experience model is *learn-by-doing*: a skill grows from being exercised under real conditions, not from an abstract end-of-mission reward. This mirrors battle, where a marine's ranged, melee, and physical skills grow from the acts of aiming, firing, and swinging rather than from winning the engagement.
- Every skill check a mission step resolves — stealth on infiltration and exfiltration, tactics and positioning at the objective, and the weapon or espionage skills a given mission type exercises — awards skill points in the skill that was tested to each able participating soldier, applied at the moment the check resolves. A soldier lost later in the same mission keeps what he learned up to that point.
- The award scales inversely with the check's *margin*: a trivial success (a large positive margin) teaches almost nothing, while a narrow success or an outright failure (a small or negative margin) teaches the most — challenge at the edge of a soldier's ability is where he grows. Failure is not rewarded over success as such (a hard-won success and a near-miss failure teach comparably), so there is no incentive to field under-skilled squads deliberately to farm experience.
- Field experience is deliberately tuned to outpace an equivalent week of garrison training for the skill exercised, reflecting the action-oriented ethos of the chapter: live operations forge better soldiers than drills. Because it concentrates on the tested skill (rather than spreading a fixed weekly budget across a training profile) and because skill cost is geometric — each additional point of a skill costs exponentially more raw points than the last — this advantage is largest for green and mid-tier marines and tapers naturally for veterans.
- No explicit per-mission cap is applied in the first pass. The geometric skill-cost curve and the margin scaling are the intended governors of runaway growth; a cap may be introduced later if playtesting shows one is needed (for example, from a squad farming repeated close-fail checks in the detect/evade loop).
- Mission field experience grows skills only; soldier attributes are treated as largely fixed by adulthood and are not advanced by this system in the current pass. (Battle retains its existing attribute growth from physical exertion.) How to represent the fiction's exceptionally-statted "hero" characters is left open — see §6.13.

**Evaluation**
- Periodically, each marine is evaluated and assigned composite scores for Melee, Ranged, and Leadership aptitude.
- Evaluation history is retained for the marine's full career.
- Squad assignment decisions (e.g., promotion to sergeant, transfer to a specialist role) are informed by evaluation scores.

**Wounds**
- When a marine takes a hit in combat, a specific body location is struck.
- Damage after armor reduction is applied to that location, accumulating wound severity from Negligible through Unsurvivable.
- A location that reaches its cripple threshold is crippled: weapon-holding locations can no longer hold weapons; motive locations reduce or eliminate movement.
- A location that reaches its sever threshold is severed: weapon-holding locations lose the equipped item.
- Any location that reaches a vital-critical threshold incapacitates or kills the marine.
- A marine with any crippled motive or vital location cannot be deployed.

**Recovery**
- Wounded marines recover over time. The number of weeks to full recovery depends on the highest wound severity present.
- Recovery progress is tracked per severity tier within each location.
- A marine fully recovers when all locations have healed to zero wounds.

**Geneseed**
- Each marine has progenoid glands with an implant date.
- Glands mature after a defined number of years.
- When a marine dies, there is a chance his geneseed is successfully recovered, adding to the chapter's stockpile.
- Stored geneseed is required to create new initiates.

**Death**
- When a marine's wounds reach a lethal threshold, he is removed from his squad and the chapter roster.
- His history, kill record, and awards are preserved.
- Geneseed recovery is attempted and recorded.
- The death produces a eulogy-style record per the Narrative Voice specification (4.19): where and how he fell, his final tally, years served, and whether geneseed was recovered — with lost geneseed narrated as a compounding loss.
- **Preservation (resolved — structured event log step 1):** the fallen brother's history, kill record, and awards are now *preserved* rather than discarded. On death `BattleTurnResolver` records a structured `Death` event (carrying faction and weapon) and moves the brother out of the active roster into `Army.FallenBrothers`, a squad-less dossier store that survives save/load (his base soldier row persists with a null `SquadId` and is rerouted into the fallen store on load). The eulogy-style *narration* of that preserved record (below, and §4.19) remains 0.8 narrative work; the data it draws on is now retained.

---

### 4.13 Turn Simulation — Mission Resolution

**Description.** The rules governing how player-issued and AI-generated orders are executed each turn.

**Acceptance Criteria:**

**Order Assignment**
- The player can assign a squad to one of the following mission types: Recon, Advance (assault), Ambush, Assassination, Sabotage, Extermination, Defense, Patrol, Construction, or Diversion.
- The player sets the target (a region and its occupying faction) and the aggression level (Avoid, Cautious, Normal, Attritional, or Aggressive).
- Multiple squads can be assigned to the same mission, combining their force for resolution.

**Mission Execution**
- If the squad is not already in the target region, an infiltration phase occurs first. Infiltration success depends on the squad's stealth skills relative to the defender's regional intel and garrison size.
- If infiltration fails, the squad may be ambushed or forced into a meeting engagement before reaching the objective.
- Each mission type has a distinct execution sequence with skill-based checks and possible combat encounters along the way.
- After the mission objective is resolved, the squad exfiltrates (if in an enemy region). Exfiltration uses the same stealth-versus-defender-intel mechanics as infiltration.
- Mission outcomes affect regional intelligence level, enemy defensive ratings, and enemy casualty counts as appropriate to mission type.

**Mission Record & Experience**
- Each mission a squad runs records a per-soldier history event describing the operation and its outcome (e.g., reconnaissance conducted, region infiltrated undetected, detected and forced to break contact, target eliminated) — the non-combat counterpart to the battle-participation events already recorded for combat. These feed the soldier's career history and periodic evaluation (§4.12), so field service, not only kills in battle, informs promotion and role decisions. Emission of these events is sequenced with the structured event-log work (§5.5).
- The skill checks resolved during the mission grow the participating soldiers' skills per the Field Experience rules (§4.12).
- Overall mission results are surfaced in the end-of-turn report alongside battle debriefs, attributed to the acting faction, per the Narrative Voice specification (§4.19).

**Mission Types — Specific Behaviors**
- **Recon:** Squad conducts surveillance over multiple days, accumulating intelligence for the region. Higher skill margins produce better intelligence; critically poor margins produce false or corrupted intelligence. Intelligence decays over subsequent turns.
- **Advance:** Squad assaults an enemy-held position. A battle is resolved. Successful advance reduces enemy garrison and may convert or destroy enemy regional holdings.
- **Ambush:** Squad positions and waits for enemy movement before engaging. Success depends on positioning skill and the enemy patrol activity level.
- **Assassination:** Squad locates and eliminates a specific enemy leader target. Target tier determines the strength of the bodyguard force encountered.
- **Sabotage:** Squad degrades enemy defensive infrastructure (listening posts, entrenchment, or anti-air). Degree of degradation scales with skill margin and the size of the existing defenses.
- **Defense:** Squad defends a region against an incoming enemy assault. Defender advantage applies. Outcome affects whether the enemy assault order resolves successfully.
- **Patrol:** Squad conducts a security sweep, potentially intercepting enemy infiltration or patrol forces.
- **Construction:** Squad in its own region spends the turn building regional fortifications instead of fighting — Entrenchment (Fortify), Listening Post, or Anti-Air. Progress scales with squad size and the Engineering (Fortification) skill, accumulating across successive turns and sharing resolution with the NPC development path.
- **Diversion:** Squad stages an overt feint against an enemy-held region while remaining in its own, skipping infiltration. A daily show of force (a Tactics check whose difficulty rises with the target's defender-held regional intel and garrison size) accumulates Impact, inflating the garrison the target's controller feels it must hold; at Normal aggression or higher it also baits a counterattack, in which the exposed feint force is pulled in as a defender.

**Aggression and Continuation**
- A squad's aggression setting determines at what casualty level it withdraws from an engagement.
  - Avoid: withdraws if casualties exceed 10% of strength.
  - Cautious: withdraws at 25%.
  - Normal: withdraws at 50%.
  - Attritional: withdraws at 75%.
  - Aggressive: does not withdraw voluntarily.
- A squad without a surviving squad leader applies a penalty to morale checks (once morale is implemented).

**Non-Player Faction Orders**
- Each turn, every non-player faction generates orders for each planet it occupies, following this priority:
  1. If a faction controls adjacent regions to an enemy with 1.5x superior force strength, it generates an assault order.
  2. Remaining spare troops fund construction orders (improving organization, listening posts, entrenchment, or anti-air in ascending cost order).
  3. Any remaining troops fund a scout patrol order.
- Non-player faction orders are resolved alongside player orders in the same turn.
- Large NPC-only `Advance` orders are resolved through the strategic large-scale combat model
  instead of the tactical battle engine once the committed regional forces exceed the tactical
  actor/battle-value caps. Tactical resolution remains mandatory for player squads and named
  player soldiers. See `Design/Reference/LargeScaleNpcCombat.md`.

---

### 4.14 Turn Simulation — Battle

**Description.** The rules governing individual squad-level engagements on a 2D grid.

**Acceptance Criteria:**

**Grid and Positioning**
- Battles occur on a 2D grid. Grid size scales with the number and species size of the participating units.
- Each soldier occupies one or more grid cells depending on their species' physical dimensions.
- Opposing forces begin at an engagement range that scales with the battle type: ambushes begin at shorter range; ranged-dominant forces prefer longer initial ranges.

**Turn Structure**
- Each battle turn, every able squad selects a movement tier based on its tactical situation and aggression setting. Individual soldiers then select legal actions within that squad tier.
- Available actions include movement toward a destination, fire, aim, charge into melee, melee attack, reload, ready or swap a weapon, or change stance. Recovering from Prone consumes the soldier's full action and cannot be combined with movement; other future utility actions specify their own tier restrictions case by case.
- Fire resolves before movement. A soldier therefore fires from their starting position, then carries out the movement selected for the turn.

**Movement Tiers — Battle Logic Phase 4A**
- Movement is modeled as five squad-level tactical tiers: Stationary, Walk, Jog, Run, and In Melee. A tier is primarily a set of restrictions: it defines the maximum distance each soldier may travel and which other activities may be combined with that movement. The squad may change tiers freely each turn with no transition cost.
- Stationary prioritizes volume and accuracy of fire. Walk supports small range adjustments while retaining reduced accuracy. Jog closes or opens distance while unaimed fire remains worthwhile. Run prioritizes changing distance over ranged fire. In Melee keeps engaged soldiers fighting while the rest of the squad closes to support them.

| Tier | Maximum movement | Aim | Ranged attack | Melee | Notes |
|---|---|---|---|---|---|
| Stationary | 0 | Yes; full bonus | Yes; no movement `Bulk` penalty | Yes | Selecting Stationary resets banked movement; stance effects apply |
| Walk | 1/5 MoveSpeed | Yes; half applied bonus | Yes; half `Bulk` penalty | Yes | Accumulated aim is preserved |
| Jog | 1/2 MoveSpeed | No | Yes; no aim bonus and full `Bulk` penalty | Yes | Entering Jog resets accumulated aim |
| Run | Full MoveSpeed | No | No | Charge only | Entering Run resets accumulated aim; turning is limited to 45 degrees (one facing step) |
| In Melee | Per soldier | No squad-wide aim behavior | Per the soldier's effective movement and engagement | Yes | Adjacent soldiers hold and fight; separated soldiers close or charge |

- **Declared tier and target speed.** The declared tier sets each soldier's `CurrentSpeed` for ranged-defense calculations: 0 for Stationary, 1/5 `MoveSpeed` for Walk, 1/2 for Jog, and full `MoveSpeed` for Run. It applies even when the soldier's actual displacement is shorter or movement fails, because the movement choice has already constrained the soldier's actions. In Melee derives `CurrentSpeed` per soldier: an adjacent combatant who holds position has speed 0, while a soldier closing or charging has the speed of that movement.
- **Banked movement.** Each soldier stores `LeftoverMovement`. A moving turn's available distance is the tier allowance plus the full banked amount; Euclidean distance actually traveled is subtracted, and all unused distance remains banked for a later moving turn. The bank is not capped and is not lost when movement is shortened or blocked by another soldier. Selecting Stationary resets it to 0. This preserves fractional grid movement over time: a Move 6 soldier walking receives 1.2 distance per turn and can eventually spend accumulated fractions on a diagonal move.
- **Walking accuracy.** Walk does not cap or discard stored aim. Instead, it halves the entire aim-derived accuracy bonus when a shot is resolved: weapon `Accuracy` plus accumulated aim. For example, an Accuracy 3 weapon fired after one turn of aiming receives +4 while Stationary and +2 while Walking, regardless of whether that aim was accumulated while Stationary or Walking. Returning to Stationary restores the full effect of preserved aim. Jog and Run provide no aim bonus and reset accumulated aim.
- **Movement `Bulk`.** Walk applies one-half of the weapon's `Bulk` penalty; Jog applies the full `Bulk` penalty. Stationary applies none. Run prohibits every ranged attack, including ordinary firearms, flamers, thrown grenades, and grenade launchers; none receive a special Run exception. Reloading and readying or swapping a weapon remain legal at Walk, Jog, and Run.
- **Charge.** Run may culminate in a melee attack only for a soldier entering a new engagement; a soldier who began adjacent to an enemy cannot use Run to make an ordinary melee attack. A charging attack uses the existing −2 moved-attack accuracy penalty. The charger forfeits all weapon `ParryModifier` benefit for the remainder of that turn, but receives no additional defensive penalty because their movement speed already makes them harder to hit at range.
- **In Melee.** This tier is a squad tactical mode rather than a uniform movement rate. A soldier already adjacent to an enemy stays in place and chooses a legal melee or point-blank action. A separated soldier moves toward an available engagement position, using up to Run allowance and charging if they can enter melee this turn; if they cannot reach melee, they continue closing without ranged fire. The selected squad tier is stored in battle state, while `CurrentSpeed` and `LeftoverMovement` remain per-soldier state.
- **Deferred interactions.** Phase 4A does not implement the later leg-wound movement changes or true stance behavior. Battle Value recalibration caused by the tier system is a separate balance body of work.

**Stance**
- Stance is only mechanically relevant when a soldier is stationary, and represents body position: Standing, Crouching, or Prone. Stance affects both incoming ranged hit probability and melee effectiveness.
- Rather than a flat accuracy penalty, stance filters the valid hit locations before the hit location probability roll: locations not exposed in a given stance are excluded. Crouching excludes lower-body locations (legs, feet); prone excludes everything but locations visible from ground level (head, upper torso depending on orientation). The exact excluded sets are defined per body template.
- In melee, crouching applies an offense penalty and makes the soldier easier to hit; prone doubles both magnitudes. Specific values are set during implementation.
- Stance transitions each cost one turn: Standing↔Crouching, Crouching↔Prone, and a direct drop to Prone from any stance. Returning from Prone to Standing takes two turns (passing through Crouching). Changing stance and moving in the same turn is not permitted.

**Ranged Combat**
- Hit probability is derived from the shooter's ranged skill, the target's range, the target's physical size, the target's per-species evasion value (an elusiveness modifier distinct from physical size), and the cover modifier of the target's squad.
- A successful hit determines a struck location via the target's hit location probability table.
- Damage after armor reduction is applied to that location at the appropriate wound severity.
- Weapons have a rate of fire. Firing multiple times in a turn incurs an accuracy penalty for each shot after the first.
- Firing a two-handed weapon with one hand (due to an injured arm) incurs an additional accuracy penalty.
- **Shooting into melee.** A shot at a figure engaged with the shooter's allies takes a -3 to-hit penalty. If the modified result is a near miss from -1 through 0, inclusive, one full-strength stray hit is resolved against the connected melee scrum. The nominal target and every connected participant are eligible, weighted by physical size; at point-blank range this can include the shooter.
- **Ranged target value.** Soldiers score every candidate in the three nearest enemy squads that are within weapon range using `imminence(target squad) x E[enemy BV removed] - E[friendly BV lost]`. Enemy value includes hit probability, expected post-armor wound damage as a fraction of Constitution, and the target template's Battle Value. Friendly loss uses the exact near-miss probability and size-weighted scrum distribution with no imminence discount. Target-squad imminence is cached for the planning turn and is `1 / (1 + turns until that squad can engage)` from distance, movement speed, and preferred engagement range.
- **Target-selection intent.** The score normally favors a clean target over an equally valuable enemy entangled with allies, but can still favor a large, high-value monster in melee because its size improves the hit chance and absorbs most size-weighted strays.
- **Future non-goal.** General dangerous fields of fire and line-of-fire/line-of-sight tracing through friendly formations are not part of this refinement. They require fire-lane tracing plus formation behavior that keeps allies out of those lanes; the reusable scrum stray-distribution rule is the seed for that later system.

**Melee Combat**
- Squads that close to melee range engage in hand-to-hand combat.
- Melee hit probability is derived from the attacker's melee skill, the defender's melee skill, the defender's per-species evasion value, and the weapons involved.
- An engaged soldier compares the projected BV removed by his planned melee strike sequence with a point-blank ranged shot scored by the same ranged target-value rule. The shot pays the firing-into-melee and weapon `Bulk` penalties and the expected self-BV cost of forfeiting parry against every adjacent attacker. This allows a loaded firearm to remain useful against a light engagement while favoring readying a melee weapon when several enemies press the shooter.

**Melee Combat — Attack Speed, Multiple Attacks & Weapon Rebalance (shipped in 0.7, §5.2)**

Before this rework every soldier got exactly one melee attack action per turn, swung with the first equipped weapon only; the per-soldier `AttackSpeed` attribute (authored per species with clear intent — humans 10, Hormagaunts 20, Genestealers 30, a Hive Tyrant 60) was generated and persisted but never read by combat, and several melee weapon columns (`ExtraDamage`, `IsPenetrating`, `ParryModifier`) were dead. Combined with flat armor subtraction, that left melee drastically under-lethal: a Hormagaunt penetrated Astartes power armor on ~3.5% of hits and inflicted only Negligible wounds when it did. The rework makes melee combat deliver what the species and weapon data promise:

- **Attacks per turn from AttackSpeed.** A soldier's base attack count per melee action is `AttackSpeed / 10`, with the fractional part resolved probabilistically (a marine at AttackSpeed 15 always swings once and has a 50% chance of a second swing). A Hive Tyrant at 60 averages six attacks; a PDF trooper averages one.
- **Weapon speed as a multiplier.** The weapon `ExtraAttacks` column was replaced by an `AttackSpeedMultiplier` applied to the soldier's attack count (total attacks = `AttackSpeed/10 × weapon speed factor`) rather than an additive bonus. The old additive-intent values did not carry over; all weapons currently sit at 1.0, leaving per-weapon speed differentiation (fast chainswords, ponderous hammers) as an open data/balance lever.
- **Dual wielding.** A second equipped melee weapon grants +1 attack, delivered with the off-hand weapon's profile. Defensive value comes solely from the weapons' `ParryModifier`s (summed across everything in hand) — an off-hand *parrying* weapon helps defense because its parry value says so, not because dual wielding grants a flat bonus. *(An earlier draft granted dual wielders +1 defense; that was removed once it proved to hand a free defensive bonus to every dual-natural-weapon creature — effectively the whole Tyranid roster — stacking with the evasion values that already model their 5+ invulnerable-save analog.)*
- **Attack distribution.** A soldier with multiple attacks decides at the start of the turn how to distribute them among adjacent enemies, using decision logic analogous to how shot count is chosen when firing. Wounds still resolve at end of turn, so committing every attack into one target risks overkill — that tradeoff is intentional.
- **Parry.** `ParryModifier` is implemented on the *defender's* side of the contested melee roll: attacks against a soldier wielding a clumsy weapon (e.g., power fist, −1) land more easily; a deft weapon makes its wielder harder to hit.
- **Damage rebalance.** All melee `StrengthMultiplier` values are doubled — under flat armor subtraction the old values left low-Strength melee mathematically incapable of harming power armor at any roll — with heavy-tier `WoundMultiplier` values dialed down in compensation. The dead `ExtraDamage` and `IsPenetrating` columns are dropped (their effects are reproducible with the remaining columns). Weapon values remain subject to ongoing balance passes against the targets below.
- **Unarmed combat.** Soldiers without a melee weapon fight with their species' data-selected default unarmed weapon for both attack and defense; that weapon's `RelatedSkill` determines the skill used. The current Space Marine species selects the Fist/Fist-skill profile, while the other shipped species select the stat-identical Fist/Generic-Melee profile to preserve their current training and balance. Neither profile is Astartes- or faction-restricted—an ordinary-human species can select either in rules data. Basic unarmed-combat competence is data, not a code fallback: every Space Marine template carries a point of Fist MOS training, so a marine caught weaponless fights trained—below his blade-work, above helpless. A future refinement may replace bare fists with a default *close combat weapon* (knife/bayonet).
- **Non-goal.** AttackSpeed affects attack count only; melee *defense* remains a function of skill, evasion, stance, and parry — a fast creature is not additionally harder to hit.
- **Data fixes.** The Ork Warboss's `AttackSpeed` was 3.06 — equal to his Size, a mislinked attribute template — and his default weapon set was the Space Marine "Bolter + Bolt Pistol". Both are corrected: AttackSpeed 30, a new "Slugga + Big Choppa" weapon set, and Generic Melee/Ranged training.
- **Balance targets.** Tabletop lethality is a guide, not a spec: base marines are deliberately "two-wound" soldiers where tabletop made them one-wound. Concretely: a lone Hormagaunt loses to a marine well over 95% of the time, and one-shot marine kills are rare (lucky head hits only); a Genestealer is roughly even money against a marine in melee; a Carnifex defeats ~8–10 PDF troopers head-to-head.
- **BattleValue recompute.** `SoldierTemplate.BattleValue` is recomputed from the engine-math valuation model (per-template offense × durability against a reference threat panel, BV = k·√(O×D), implemented in `Helpers/Battles/BattleValueCalculator.cs`), and the strategic constants calibrated in BV-space (`StrategicCombatRules` anchors, mass-combat floor, NPC recon cap, §4.24 invasion budgets) track the recomputed scale. Player-soldier BV deliberately remains the template guideline rather than a live skill-tracking value: enemies size their responses by *estimating* the player force, not by concrete data on every marine, so a veteran chapter over-performs its paper strength by design.

**Template Weapons — Flamers (0.7, implemented)**

Flamer-type weapons were previously modeled as ordinary single-target guns differentiated only by stats (short range, high accuracy), which lost everything that makes a flamer a flamer. This rework makes them true template weapons. Grenades reuse the same machinery with a thrown/launched blast delivery (see *Template Weapons — Grenades* below; shipped in §5.3).

- **Cone template, not a shot.** A flamer burst projects a cone from the shooter along the aiming line toward its target. The cone always extends to the weapon's full `MaximumRange` (the target sets the direction, not the extent — a flame stream cannot be stopped short), and its half-width grows linearly from the nozzle to the weapon's `AreaRadius` at maximum range. Weapon data: `TemplateType` (0 = normal, 1 = spray/cone; future values reserved for grenade bursts), `AreaRadius`, `FuelPerBurst`.
- **Auto-hit, indiscriminately.** Every soldier — friend or foe — whose footprint falls inside the cone is struck. There is no to-hit roll and no aim bonus; size, speed/range, and per-species `RangedEvasion` modifiers do not apply (you cannot weave away from a wall of fire). Armor and wound resolution work normally per victim (hit location roll, armor × `ArmorMultiplier`, `WoundMultiplier`).
- **Firing lines matter.** Because the cone burns everything along its length, allies standing between the bearer and his target are hit, and a burst aimed at a melee scrum engulfs every participant on both sides. This *replaces* the near-miss/stray-shot rule for template weapons — engulfment is certain, not a mishap. Target selection must therefore score firing lines (enemy value caught in the cone minus friendly value caught), not individual targets.
- **Fuel, not shots.** `AmmoCapacity` is a fuel tank; each burst consumes `FuelPerBurst` (rate of fire remains 1 — one burst per action). An empty tank forces the weapon's long reload (a tank swap). The bearer's decision logic weighs a burst's expected value against remaining fuel.
- **Battle Value.** Template weapons are valued by expected victims per burst against the reference threat panel — density-scaled (multiple chaff caught per burst, one monster) — with the fuel duty cycle replacing the ammo/recoil model, feeding the standard BV = k·√(offense × durability) pipeline.
- **Gated follow-ons (Battle Logic Phase 4, §5.4).** An "on fire" damage-over-time and panic condition is specified in intent but gated on the morale system (§6.2). Cover/terrain interaction with templates is gated on Battle Visuals Phase 3 line of sight.

**Template Weapons — Grenades (0.7.1, implemented)**

Blast templates extend the cone-template machinery with a second delivery mode: a circular blast of `AreaRadius` centered on an **impact point**, delivered either **thrown** (frag grenades) or **launched** (the Grenade Launcher, currently a Genestealer Cult weapon). Design decisions locked 2026-07-15; execution plan in `Design/grenades.md`.

- **Margin-driven scatter.** The throw/launch makes the standard normal-curve skill check. A success lands the grenade on the aimed cell; a failure deviates the impact point by a distance proportional to the margin of failure, in a random direction — since the underlying roll is already a normal curve, deviation distance is naturally half-normal. Scatter *replaces* the near-miss/stray-shot rule for blasts; there is no separate mishap mechanic. Range and movement (`Bulk`) modify the check; target size, `RangedEvasion`, aim, and rate-of-fire modifiers do not (a grenade is thrown at a spot, not a person, and blasts cannot be aimed).
- **Auto-hit inside the blast, with realistic falloff.** Everyone whose footprint falls inside the circle — friend, foe, and **the thrower himself** (unlike the flamer's shooter exclusion, danger-close is legal and self-inflicted casualties are possible) — is struck, with the damage roll scaled down quadratically from full at the impact center to zero at the template rim. Armor and wound resolution are otherwise unchanged per victim.
- **Skills.** Player soldiers throw with the `Throwing` skill (launcher fire will get a dedicated weapon skill if/when marines carry launchers); NPC soldiers use `Generic Ranged` for both, mirroring the flamer's dual-row pattern.
- **Strength-scaled throw range.** A thrown grenade's maximum range scales with the thrower's Strength (the template stores range-per-Strength-point), so a marine out-throws a PDF trooper without separate weapon rows. Launched blasts use the weapon's normal `MaximumRange`.
- **A grenade is a single-shot ranged weapon with a fast reload** (grabbing the next from the belt). Grenade counts are deliberately not tracked: the action economy (throwing forfeits the primary weapon that round) and target opportunity (worthwhile only against a mass of relatively poorly-armored soldiers at close range) are the governors.
- **Ubiquitous via weapon sets, no UI.** Weapon sets gain a third ranged slot for the grenade; all Space Marine sets, the Imperial/PDF sets, and the human-tier Genestealer Cult sets carry frag grenades. No squad-screen changes.
- **Frag only.** Krak grenades (thrown single-target anti-armor, not a blast template) are deferred to the Vehicles backlog item (§5.7), where they matter.
- **Decision logic.** The planner scores a throw exactly like a flamer firing line — expected enemy BV removed (falloff-scaled) minus expected friendly BV lost, with the thrower's own expected loss included — and throws only when that beats the soldier's best conventional action, keeping lone targets on the rifle and clusters on the grenade.
- **Battle Value.** Blast weapons are valued by a sibling of the cone-template branch: auto-hit density-scaled victims per blast (same bodies-per-template panel densities as the flamer — the 6m circle's area roughly matches the cone's, and centered placement offsets the cone's raking reach), an average quadratic-falloff factor (`BlastAverageFalloffFactor` 0.5, splitting the range between the uniform-disc average of 1/6 and a centered hit at 1.0 — placement quality and the throw's skill roll fold into this one tunable), the shots-per-magazine/reload duty cycle (frag ½, launcher ⅘), and Strength-scaled thrown reach in the standoff term. The grenade is valued as a *sidearm*: per panel profile the ranged rate is max(primary, grenade), never the sum, matching the planner's throw-or-shoot choice. Regeneration confirmed the model's verdict that a frag grenade on a soldier with a working gun adds real but sub-rounding value — **no shipped `SoldierTemplate.BattleValue` changed** (the Grenade Launcher's new nonzero valuation landed the Brood Brother exactly on its already-stored 7), and the `StrategicCombatRules` anchors are untouched (PDF Trooper regenerates to exactly 5).

**Wounds in Battle**
- All wound rules from the Soldier Lifecycle apply during battle.
- A soldier whose wounds reach the incapacitation threshold is removed from combat.
- A soldier whose wounds reach a lethal threshold dies during the battle.

**Cover**
- Each squad has a cover modifier that reduces incoming hit probability. Cover modifier is set based on the squad's orders and terrain (once terrain is implemented).

**Battle Continuation**
- After each turn, squads evaluate whether to continue based on their remaining strength relative to their starting strength and their aggression setting.
- A squad with no able soldiers always withdraws.
- Battle ends when one side has no squads willing to continue.

**Disengagement — Covered Withdrawal and Rout**

**Implementation status (post-0.7).** The sequence below is a design specification, not current battle behavior. The current mission layer applies casualty/aggression continuation thresholds after an engagement, and the battle resolver forces an unresolved battle to disengage at its turn cap, but battles do not yet model covered withdrawal to the map edge, pursuit, morale-triggered rout, or a burrow/flight-specific immediate exit. These behaviors remain together in Battle Logic Phase 4 (§5.4).

- A squad leaves an engagement in one of two modes:
  - **Covered Withdrawal** (player-ordered): the squad moves away from the enemy at jog speed each turn, shooting as it goes. Withdrawal fire uses normal shooting rules and counts toward the pursuer's casualty threshold. The squad remains on the map until it reaches the map edge, then exits.
  - **Rout** (morale-triggered; morale itself is still open, see §6.2): the squad moves away at run speed each turn with no shooting, remaining on the map until it reaches the edge.
- **Pursuit:** the non-withdrawing force continues to act normally — moving toward and firing at the fleeing squad according to its own aggression. Pursuit tenacity is governed by the pursuer's aggression using the same casualty thresholds as normal battle continuation: an Aggressive force pursues until it cannot reach or shoot the fleeing squad or its own casualties trigger withdrawal; an Avoid force does not pursue at all. A pursuer that takes sufficient casualties from withdrawal fire may itself begin to withdraw, ending pursuit.
- Combat ends when the two forces are outside mutual shooting range and at least one side is fully withdrawing or routing; both forces need not exit the map.
- A squad capable of burrowing or flight may disengage immediately when it elects to retreat, bypassing the normal withdrawal sequence.

**Battle Record**
- A full record of the battle is stored: all actions taken per turn, soldier positions, wounds inflicted, and outcome.
- This record is used to populate the Battle Review Screen.
- Soldiers gain skill experience from the battle proportional to their level of participation.

---

### 4.15 Turn Simulation — Living Galaxy

**Description.** The rules governing how non-player factions grow, spread, and conduct operations across the sector independently of player action.

**Acceptance Criteria:**

**Population Growth**
- Each week, every faction's population in each region they occupy grows according to their growth type.
- Standard factions (Imperial PDF, Tyranids) grow logistically: faster when below planetary carrying capacity, slower above it.
- Converting factions (Genestealer Cults) grow by converting members of the default imperial population to their cause. One member of the default faction is converted per week at base. At sufficiently large cult sizes, organic growth also occurs.
- A faction whose population is converted loses both population and a proportional share of its garrison.

**Faction Status**
- Some factions begin hidden: their presence on a planet is not known to the player or to other factions unless discovered.
- A hidden faction whose population exceeds a threshold transitions to public status, becoming visible to all and triggering open conflict.
- A hidden faction's cultists who discover a weakness may ambush the player's forces if they assess they can win the engagement.

**Offensive Planning**
- Each turn, each non-player faction evaluates its strength in every region adjacent to an enemy.
- If its combined available attack force (garrison minus required defense troops) exceeds the defender's strength by a ratio of 1.5:1 or greater, it commits troops to an assault order targeting the enemy region.
- Committed troops are deducted from the faction's garrison.

**Construction**
- Remaining spare troops (after offensive commitments) are converted to build points.
- Factions spend build points to improve regional infrastructure: Organization (enables more organized military force), Listening Post (feeds defensive regional intel and increases enemy stealth difficulty), Entrenchment (increases defensive strength), and Anti-Air (resists aerial assaults).
- Each upgrade tier costs exponentially more than the previous.

**Patrol**
- Any spare troops remaining after construction are committed to a scout patrol order.

**Intelligence Decay**
- Regional intelligence gathered by player squads decays by 25% each week.
- Special mission opportunities (assassination targets, sabotage targets) identified through intelligence have a 25% chance of expiring each week.

---

### 4.16 Turn Simulation — Governor Relations

**Description.** Planetary governors are named characters with personalities that influence how they interact with the chapter. The Inquisition also issues requests to the chapter (see Section 6.5); the mechanics below apply to governor requests specifically.

**Acceptance Criteria:**

**Personality**
- Each governor has six personality traits: Investigation (likelihood of detecting hidden threats), Paranoia (likelihood of imagining false threats), Neediness (likelihood of requesting chapter aid), Patience (how long they wait before their opinion degrades from an ignored request), Appreciation (how much their opinion improves when a request is fulfilled), and Severity (how harshly they respond to civil unrest — see §4.20). Investigation and Paranoia govern detection of civil unrest exactly as they govern detection of hidden factions; Severity governs the *response*.

**Opinion**
- Each governor has an opinion of the player's chapter, ranging from hostile to highly favorable.
- A governor's opinion improves when the chapter fulfills a request.
- A governor's opinion degrades when the chapter ignores an active request.

**Request Generation**
- Each week, a governor with a positive opinion of the chapter may generate an aid request if they have detected (or believe they have detected) a threat on their planet.
- Detection is a probability check weighted by the governor's Investigation trait and the size of the hidden faction's population relative to the planet's total population.
- A governor with no real detected threat may still generate a request based on their Paranoia trait alone.
- A governor generates a request by multiplying their Neediness by their current Opinion to determine the final probability.
- Only one active request per governor is permitted at a time.

**Request Fulfillment**
- For the current `PresenceRequest` type, a request is fulfilled when at least one player squad lands on the planet surface, regardless of the squad's assigned orders or mission target — a squad landing for an entirely unrelated reason satisfies the request.
- Fulfillment improves the governor's opinion.
- Ignoring a request degrades the governor's opinion over time according to their Patience trait.
- Future request types (e.g., investigate a region, eliminate a specific threat) will define their own fulfillment conditions when designed, as part of the broader mission system expansion (backlog).

**Governor Replacement**
- Governors age one year at the turn of each year. Each week they face a chance of death that rises with age and is reduced on higher-importance worlds (representing better rejuvenat care). On death, the governor is removed and a successor is generated to lead the planet.
- When a governor dies, any active request they had is cancelled.
- The successor's current opinion of the chapter is shown on the Planet Detail Screen like any other governor's.
- *Post-0.7:* A newly generated successor is currently assigned a random initial opinion of the chapter. This is a placeholder; the intended long-term behavior is that a new governor's starting opinion should be informed by the previous governor's final opinion of the chapter and the chapter's general reputation in the sector, rather than being independent of history.

---

### 4.17 Fleet Management

**Description.** The chapter's fleet consists of ships organized into task forces. Ships transport squads between locations in the sector. Travel between systems occurs through the Warp, which is inherently unpredictable.

**Acceptance Criteria (Implemented):**
- The chapter fleet is organized into task forces, each composed of one or more named ships.
- Each ship has a defined troop capacity measured in individual marines.
- Squads can be loaded onto ships or landed in planetary regions via the Planet Detail Screen.
- A ship cannot be loaded beyond its capacity.
- Ship capacity is correctly reduced by the size of squads currently loaded.

**Acceptance Criteria (Implemented — 0.7):**
- Ships can be ordered to move between planets via the task force menu in the Galaxy View.
- The movement dialog presents available warp lane routes to the destination. If no established lane connects the origin to the destination directly, the route is composed of lane hops through intermediate planets.
- A task force is displayed on the Galaxy View while it is in realspace — in orbit or in system transit (the outbound and inbound legs of a journey) — anchored to its origin or destination system.
- A task force in the Warp is not displayed on the Galaxy View. Ships in the Warp are out of contact and cannot be communicated with, selected, or interacted with from any map view until they translate back into realspace.
- Transit time is variable. The player is shown an estimated arrival range when plotting a course, not a guaranteed date.
- A task force in transit cannot be loaded or unloaded until it arrives.
- On arrival, the task force's position is updated to the destination planet.
- Fleet positions are saved and restored correctly across save/load.

**Deferred (Post-0.7):**
- A player-selectable "Chart Direct Route (Risky)" option that bypasses the lane network for a shorter-distance but higher-variance passage. This is deferred because the current transit-time model derives base time from subsector scope rather than raw distance, and applies the same variance regardless of route type — so a direct route is presently neither faster nor riskier than a lane route between the same two planets. Implementing the option meaningfully requires first making route type mechanically distinct (distance-aware base time and/or wider variance for direct routes). Until then, a direct route is used only as an automatic fallback when the lane network cannot connect two planets, and no risky-route choice is shown to the player.

**Warp Travel Time Model.**

*Sector scale.* The sector grid is 200×200 light years (each grid unit is 1×1 ly). A subsector has a maximum diameter of 20 ly, typically containing 2–8 star systems within a 10 ly radius.

*Warp lanes.* Each subsector has a designated capital world, determined during sector generation by an importance score (population size for 0.7; strategic classification post-0.7). The capital has an established warp lane to every other planet within its subsector. Across subsectors, lanes connect primarily between subsector capitals, with additional cross-subsector lanes possible between high-importance non-capital planets near boundaries. Lanes are derived deterministically during sector generation (and rebuilt on load).

*Transit time formula.* The campaign layer tracks objective real-space travel time in one-week turns; subjective time aboard ship is computed separately for future crew experience, healing, and event hooks.
- Every interstellar trip carries a fixed 4-week base cost (roughly two weeks out to the warp translation point and two weeks back from the destination point).
- Warp passage base time is set by subsector relationship: same subsector = 1 week expected subjective warp time; adjacent subsectors = 3 weeks; non-adjacent in-sector = 7 weeks.
- Subjective warp time is multiplied by a Gaussian-derived factor inspired by the Rogue Trader subjective-duration table (z=0 → 1×; z=±0.5 → ½×/2×; z=±1 → ⅓×/3×; continuing by the same pattern).
- Objective warp time is then multiplied by a second Gaussian-derived factor (z=0 → 1× subjective; z=+5 → 1/10×; z=−5 → 10×).
- Total objective travel time is `4 weeks + objective warp time`, rounded up to whole campaign turns for arrival.

*Phases and persistence.* Fleet movement is tracked in three phases: 2 objective weeks outbound system transit, the rolled objective warp duration, and 2 objective weeks inbound system transit. Fleets are visible and communicable during system transit but not while `InWarp`. The resolved journey state is saved with the fleet: origin, destination, current phase, remaining phase/total objective weeks, rolled subjective warp weeks, rolled objective warp weeks, and whether the subjective warp training payout has already been applied.

*Training in transit.* Embarked soldiers do not receive ordinary weekly training while `InWarp`. On exit into inbound system transit, embarked idle squads receive one training payout based on the rolled subjective warp weeks. Crew in system transit accumulate training and healing at the same rate as those on the ground. The end-of-turn simulation runs normally each week regardless of ship position.

*Routing.* Lane topology determines known routes: the lane route is the shortest path through the warp-lane graph (Dijkstra weighted by hop distance). If no lane path exists, the journey is treated as a direct/charted route (also the automatic fallback when the lane network cannot connect two planets). Player-selectable route choice and a distinct risky direct route are a post-0.7 refinement (see the deferred "Chart Direct Route (Risky)" option above). Navigator modifiers to the Gaussian rolls are also post-0.7 (see §6.6). Warp storms are deferred post-0.7; the direct-route long-tail variance covers disrupted passages for now.

Example ranges for reference:

| Journey type | Typical base turns | Equivalent days | Canonical target |
|---|---|---|---|
| Same subsector | 5 before variance | 35 days | 5–10 days warp passage, plus in/out-system travel |
| Adjacent subsector | 7 before variance | 49 days | 12–30 days warp passage, plus in/out-system travel |
| Non-adjacent in-sector | 11 before variance | 77 days | 30–60 days warp passage, plus in/out-system travel |

**Fleet Screen (sector-wide).** The Fleet Screen lists all of the chapter's task forces and their ships regardless of location, including those in the Warp, so the player always has an accounting of the whole fleet.

- Each task force shows its current status: in orbit at a named planet, in system transit, or "In Warp to {Destination}".
- A task force in the Warp, its ships, and the marines aboard those ships are listed for visibility but are **not selectable** and expose no actions. They cannot be reorganized, re-tasked, or inspected at the individual-soldier level while out of contact.
- Task forces and ships in realspace (in orbit or system transit) remain selectable subject to the normal in-transit restrictions (e.g., a task force in transit still cannot be loaded or unloaded).

---

### 4.18 Save and Load

**Description.** The player can save their campaign at any time and resume it later.

**Acceptance Criteria:**
- The player can save the game from the global System menu.
- The player can choose and load a saved game from either the title screen or the in-campaign System menu.
- All game state is preserved across save/load: sector state, all faction populations and garrisons, all marines (attributes, skills, wounds, history, kill records), all squad assignments, fleet positions, loaded squads, active orders, active governor requests, game date, and chapter battle history.
- Loading a save produces a game state identical to the state at the time of saving.

**Acceptance Criteria (Implemented — 0.7.1 Release Confidence):**
- The save UI exposes named manual slots and a visible chooser showing campaign/chapter name, campaign date, last-write time, and compatibility state. “Load” must not silently mean “load whichever compatible file is newest.”
- The game maintains several rolling autosaves, including a protected pre-turn autosave immediately before turn resolution mutates campaign state. Autosaves are distinct from and never overwrite named manual slots.
- Save writes remain atomic. A failed save leaves the prior valid file intact and presents an actionable error rather than creating an empty or half-written campaign.
- Alpha 0.7.1 keeps exact-version compatibility and does not change the save format. Ordered schema migration support is deferred until the first intentional save-format version change.
- The load chooser displays incompatible or failed saves with the reason they cannot be opened. It does not hide them or replace the error with a generic “no save found” state.

---

### 4.19 Narrative Voice & Emotional Impact

**Description.** OnlyWar is a sandbox whose value is the emergent narrative its simulation produces. The simulation already generates the events — named brothers fight, develop, are maimed, and die; worlds fall or hold; governors plead and the wider Imperium triages. This section specifies how those events are *narrated back to the player*, because in a sandbox the narration is not polish — it is the feature that converts simulation state into a story the player remembers and retells. The player's felt stakes are not "avoid sector failure" (the wider Imperium makes total collapse unlikely) but **relevance and legacy**: did the chapter matter here, and at what cost in irreplaceable brothers?

This is a cross-cutting specification. The per-surface acceptance criteria below govern text generated by the Turn Report (4.11), Soldier Screen (4.7), Death/Apothecary records (4.12, 4.8), Battle Review (4.10), Governor/Inquisition requests (4.16, 6.5), and New Game founding (4.1).

**Authoring Principles (apply to all generated text):**

1. **Always name the individual.** Never abstract a named soldier into "a marine." The simulation tracks the brother; the text must never lose him. "Brother Kaelan was killed," not "a casualty was sustained."
2. **Specificity over summary.** State where, by what weapon, against which enemy, and the defining moment ("shielding Scout Aldric as the brood broke the line at Hesperus Gate").
3. **Continuity and callbacks.** Reference the subject's own recorded history — first kill, instructing sergeant, prior wounds, kill milestones, years in service. A death lands harder when the report remembers the recruitment.
4. **Tie outcome to player choice.** Frame consequences against the order that produced them ("ordered to hold at Attritional aggression, the squad did not break until two remained").
5. **In-universe voice.** A formal, liturgical, grimdark Astartes register: duty, honor, sacrifice, the Emperor. External authorities each carry a distinct voice — a paranoid governor *sounds* paranoid; the Inquisition commands rather than requests; Battlefleet dispatches are curt and bureaucratic.
6. **Restraint and variation.** The simulation supplies the drama; text frames it and never melodramatizes. Each event type draws from a pool of phrasings to avoid repetition fatigue across a long campaign.
7. **Design for player-authored meaning.** The data model should anticipate the player later eulogizing, honoring, or renaming notable dead, even if the UI for it is deferred.

**Notability Classifier.** A shared rule set determines which events are "notable" — worthy of a callout in the Turn Report and the Command Brief/Chapter Chronicle. Initial notable triggers: a brother's first confirmed kill; crossing a kill milestone; the death of a veteran (above a service/rank threshold); an officer crippled or slain; a last-survivor outcome; a squad that held under orders past a heavy-casualty threshold; first contact with a new faction; a world saved or lost (by the chapter or by the wider Imperium); a hidden cult revealed. Thresholds are tunable and should live in rules data where practical.

**Per-Surface Acceptance Criteria:**

- **Turn Report (4.11):** Casualty and outcome lines name individuals, surface notable events from the classifier, and frame results against the orders the player issued. Kill milestones and acts of heroism are called out rather than buried in aggregate counts.
- **Soldier History Log (4.7):** The event vocabulary is enriched beyond recruitment/promotion/wound to include first blood, survival against odds, instructor/mentor relationships, kill milestones, oaths sworn, and near-death recoveries.
- **Death & Apothecary Records (4.12, 4.8):** A brother's death produces a eulogy-style record — where and how he fell, his final tally, years served, and whether geneseed was recovered; **lost geneseed is narrated as a compounding loss**, not a silent stat change. Injuries are described with gravity, and recovery is framed as a brother's struggle back to the line.
- **Battle Review Log (4.10):** Per-turn log entries name their actors and add flavor on critical hits, kills, and last stands, rather than reading as "Soldier 3 fires at Soldier 7."
- **Governor / Inquisition Requests (4.16, 6.5):** Request text is voiced in character, with tone driven by the requester's personality traits and authority.
- **New Game Founding (4.1):** A short founding history / chapter myth is generated at campaign start, seeding the first entry of the chapter's narrative record.
- **Wider-Imperium Dispatches:** As the uncontrolled Imperial presence grows (per the sandbox reframe), its actions reach the player as voiced dispatches — Battlefleet priorities, other chapters' deeds, Inquisitorial edicts — that texture the sector as a living theater the chapter is one actor within.

**Committed Feature — Command Brief & Chapter Chronicle.** This is one persistent command surface with two complementary lenses over the same typed event/state substrate:

- **Command Brief (present and future):** an actionable operations overview showing active operations and expected status; idle deployable squads and squads lacking orders; fleets in transit with arrival ranges; medical procedures and recovery milestones; active requests; expiring intelligence and special-mission opportunities; current campaign mandates; and major sector changes since the previous turn. Every actionable entry deep-links to the relevant planet, region, fleet, squad, soldier, request, or mission.
- **Chapter Chronicle (past):** a persistent sector-level narrative log that records notable events from the classifier into one readable saga — founding myth, defining battles, named heroes and their deaths, worlds won and lost, factions revealed, and mandates completed or failed.
- The first turn uses the Command Brief as lightweight onboarding: a short checklist points the player toward the Promised World, landing forces, issuing orders, and understanding End Turn without creating a separate tutorial mode.
- The transient Turn Report remains the account of the week just resolved. Its notable events feed the Chronicle, while unresolved consequences and next actions feed the next Command Brief; the three surfaces must not maintain contradictory copies of campaign facts.

---

### 4.20 Turn Simulation — Revolt & Civil Stability

**Description.** Imperial-aligned populations are not unconditionally loyal. Overcrowding, war-weariness, hidden corruption, and the temperament of their governor erode a region's contentment; sufficiently discontented populations organize, take up arms, and revolt. A revolt is modeled by reusing the existing converting-faction machinery (the Genestealer Cult path): an **Insurrectionist faction** recruits from the discontented population, contests regions through the normal combat and order-resolution systems, and — if neglected — spreads and can tear a world out of the Imperium's grasp.

This behavioral specification is **implemented for Alpha 0.7.1** (see §5.3) on the **faction-presence model**, deliberately built as a forward-compatible subset of a possible future Pop-based population model (see Open Design Question §6.7).

**Acceptance Criteria:**

**Contentment**
- Each default-Imperial `RegionFaction` carries a **Contentment** value (a 0–100 scalar), tracked per region. A planet-level figure is presented to the player and the governor as a population-weighted rollup of its regions' contentment.
- Contentment drifts toward a neutral baseline each turn, modified by inputs the simulation already computes:
  - **Overcrowding** — derived from the existing carrying-capacity crowding factor; a region at or above capacity loses contentment.
  - **War-weariness** — battles resolved in the region (player- or NPC-initiated) and active public-enemy presence depress contentment.
  - **Hidden-faction drain** — an undisclosed converting faction (e.g., a Genestealer Cult) eroding the population also erodes contentment, so a corrupted world "feels wrong" before the threat surfaces.
  - **Garrison adequacy** — a garrison sized appropriately to the regional population sustains baseline order; a thin garrison drifts contentment down. A garrison used coercively (see Governor Response) sustains short-term order at a long-term contentment cost.
  - **Governor competence** — a per-governor baseline drift term.
- Contentment is intended as a forward-compatible proxy for a "loyal share" of the population under a future Pop model; it must not be implemented in a way that contradicts that model (§6.7).

**The Insurrectionist Faction**
- A single sector-wide **Insurrectionist `Faction`** instance exists (one `Faction` object with `RegionFaction` presences in many regions — the same shape as the Genestealer Cult or Tyranid faction, **not** a per-revolt instance). It is a `Conversion`-growth faction that recruits from the default-Imperial population.
- At sector generation, a portion of Imperial worlds are seeded with latent discontent and/or a small hidden Insurrectionist presence, parameterized so some worlds begin chronically restive (heavily-tithed, overcrowded, or recently-conquered populations). The system therefore has unrest to act on from turn 1 rather than only in response to player action.

**Revolt Lifecycle** (mirrors the hidden→public arc of a converting faction)
1. **Latent** — contentment below a threshold; a rising risk meter, no faction presence yet.
2. **Organizing (hidden)** — a small hidden Insurrectionist `RegionFaction` is seeded, folded into the civilian count and discoverable only through the intelligence/fog-of-war layer, exactly as a hidden cult is. Recruitment rate is a function of `f(contentmentDeficit) × regionCivilianPopulation`, capped so organizing takes several turns — a revolt is a slow burn the player can catch, not a single-turn surprise.
3. **Open revolt** — when insurgent strength crosses a ratio against the regional garrison (or contentment floors out), the faction becomes public: it seizes organization, contests the region, and can generate assault orders against adjacent Imperial regions through the existing offensive-planning path.

**Armed Defection**
- Each armed faction has a **loyalty/discipline coefficient** governing how quickly its garrison defects to a co-located public revolt: civilians (highest) > PDF garrison > Imperial Guard > Astartes (immune).
- Defection transfers garrison from the default-Imperial `RegionFaction` to the Insurrectionist `RegionFaction` in the same region — the same population-and-garrison transfer the conversion code already performs, here targeting the garrison pool rather than only the civilian population. A neglected revolt therefore compounds: the world's own defenders progressively join it.

**Spread**
- **Intra-planet** — an unsuppressed public revolt projects an accumulating contentment drag onto adjacent regions (a persistent region-level modifier in the spirit of the Diversion shaping modifiers, but persisting and accumulating while the revolt is active rather than being cleared each turn).
- **Inter-planet** — a revolt that flips a world and survives raises a subsector- (or sector-) level **unrest-climate** scalar that applies a small contentment drag and raises seed probability on other discontented worlds, strongest within the subsector. This is a cheap global/subsector scalar, not explicit contagion pathing.

**Governor Response (imperfect, personality-driven)**
- A governor detects civil unrest via Investigation (with Paranoia producing false positives), identically to detecting a hidden faction.
- The governor's **Severity** trait drives response style, and the response is frequently the wrong tool for the situation given the governor's temperament — this imperfection is intentional:
  - **High Severity → crackdown:** commits PDF garrison to suppression. This slows insurgent growth in the short term but further lowers contentment — an authoritarian spiral in which the response deepens the grievance.
  - **Low Severity → concession/appeasement:** raises contentment but reads as weakness and can embolden organizing insurgents.

**Evidence-Based Requests**
- Revolt introduces a request family distinct from the current always-available `PresenceRequest`, gated on real or imagined **evidence** (intelligence on an organizing/insurgent faction, or contentment below a threshold):
  - **Unrest advisory** (low tier) — fulfilled by a show of force (a squad landing/present), as with the current presence request.
  - **Suppress rebellion** (escalated) — fulfilled by engaging and defeating the insurgent `RegionFaction`, or by restoring regional contentment above a threshold for a sustained number of turns.
- Generation continues to flow through Neediness × Opinion, so a proud (low-Neediness) governor under-asks and lets unrest fester while a paranoid governor over-asks on imagined threats.
- An ignored request may redirect to Imperial Guard / PDF commanders (§6.4), and the wider Imperium may act on a revolt independently of the chapter (per the sandbox reframe); a world's survival never strictly depends on the player.

**Endings**
- **Military defeat** — the insurgent garrison is reduced to zero across all regions via the existing Extermination/Advance resolution.
- **Political resolution** — regional contentment held above a threshold for a sustained number of turns dries up recruitment; garrison attrition then bleeds the insurgents out and the presence dissolves.
- **Revolt victory (failure state)** — insurgents take every Imperial region on the planet; the world flips to a Renegade/Secessionist controller. The wider Imperium may later move against it.
- **Smoldering stalemate** *(optional)* — a long, unresolved revolt may settle into a persistent low-grade insurgency that acts as an ongoing contentment drag rather than resolving.

**Player Verbs**
- **Against a revolt:** the existing mission verbs apply unchanged — Extermination/Advance/Patrol against the insurgent faction, show-of-force presence to deter and lift contentment, assassination of revolt leadership (special-target hook), and garrison/fortification support.
- **For a revolt:** for 0.7, supporting a revolt is limited to **inaction with reputational consequences** — the player may withhold aid and let a world burn, taking governor-opinion and sector-standing penalties. *Active* pro-revolt support (fighting alongside insurgents, flipping a world, the chapter going renegade) is deferred to backlog, as it opens renegade-chapter and Inquisition-investigates-the-chapter design (§6.5).

**Chaos Linkage (design now, implement when Chaos lands)**
- Secular Insurrectionists and Chaos cults are **separate factions with a light affinity relation**, not parties to a full diplomacy system:
  - **Shared enemy** — they do not fight one another and may coincidentally assault the same Imperial region.
  - **Radicalization** — a Chaos cult can convert a festering secular revolt (insurgent population/garrison flips to the cult), modeling the lore truth that neglected secular rebellion is a vector for Chaos. A revolt the player could have quelled cheaply can therefore degrade into a corruption problem.
- This relation is specified now but its implementation is gated on Chaos troops existing (post-0.7 backlog); Insurrection ships standalone first. Full variable-relations faction diplomacy remains backlog.

---

### 4.21 Turn Simulation — Faction Relationships & Inter-Faction Intelligence

**Description.** The current simulation determines hostility with a single binary test: a faction is either Imperial-aligned (player or default) or not, and the two sides are enemies (`FactionStrategyController.AreFactionsEnemies`). This is sufficient while every non-Imperial faction is a monolithic "them," but it cannot express a sector with **multiple mutually hostile non-Imperial factions** — Orks that fight everyone including other xenos, a Chaos world that also carries Ork spores, a renegade revolt that is no friend of the Tyranids. This section replaces the binary model with two cross-cutting substrates that several features (Orks §4.22, and retroactively Revolt §4.20 and future Chaos) build on:

1. a **relationship model** — what each faction *feels* about every other; and
2. an **inter-faction intelligence model** — what each faction *believes it knows* about every other's presence in a region.

These are specified together because they share the same per-faction-pair shape and the same consumers (offensive planning, garrison sizing, the fog-of-war/intelligence UI, governor requests).

This is a behavioral specification. It is scoped as the **substrate prerequisite** for Orks (see §5.6) and is designed so the Revolt and governor systems can adopt it without rework.

**Acceptance Criteria:**

**Faction Relationships**
- The default posture between any two distinct factions is **Hostile**. Factions are no longer sorted into two Imperial/non-Imperial camps; hostility is a property of the *pair*, not of a side.
- A relationship store maps an unordered faction pair to a **Stance**: `Hostile | Neutral | Allied`. Only non-default stances need be stored; an absent entry resolves to Hostile.
- At sector generation the player chapter and the default-Imperial faction are seeded **Allied**. This alliance is itself a tracked value and may later degrade (a renegade arc), so nothing may assume it is permanent.
- `AreFactionsEnemies` becomes a lookup into this store (Hostile ⇒ enemies; Neutral/Allied ⇒ not), and every consumer that currently relies on the Imperial-vs-non-Imperial test — offensive target selection, required-garrison threat assessment, and patrol targeting in `FactionStrategyController`, plus the intelligence/visibility layer — routes through it.
- A faction may carry the **`UniversallyHostile`** behavior flag (see the behavior-flag consolidation below): it is Hostile to every *other* faction regardless of stored stance and can never be set Allied or Neutral. This is the mechanical basis for Orks "fighting everyone." (Infighting *within* a single faction is not modeled as region combat; see the feral efficiency penalty in §4.22.)

**Faction Behavior Flags**
- The ad-hoc booleans on `Faction` are consolidated into a single `[Flags]`-style **`FactionBehavior`** field. `CanInfiltrate` folds into it, joined by `UniversallyHostile`, `Indelible` (§4.22), and room for future behaviors. A faction's identity becomes a *composition* of behaviors rather than a special-cased type — e.g. Orks = `UniversallyHostile | Indelible` over `Logistic` growth — so later factions (Tyranids, Chaos) can reuse individual behaviors.

**Inter-Faction Intelligence (Belief, not ground truth)**
- Intelligence is modeled as **belief held by an observer**, not as a direct property of the target. The conceptual/full form is keyed `(observer Faction, target Faction, Region)` and carries a **believed presence/strength** plus an **`IntelLevel`**: `None | Rumor | Suspected | Confirmed | Located`. The current/v1 implementation may store this as a sparse numeric `PlanetFaction.RegionIntel` value keyed `(observer PlanetFaction, Region)`: one awareness value for how well that faction believes it understands the region, used both defensively on its own ground and offensively against regions it may attack. Later target-specific beliefs can split out of that value without changing the core rule: consumers read belief, not truth.
- Crucially, **belief may diverge from reality in both directions**:
  - *False negative* — a target is present in the region but the observer believes `None` (e.g. undetected feral Orks, an organizing cult).
  - *False positive* — the observer believes a faction is present where it is not. This is materialized only by an explicit cause: a governor's **Paranoia** trait (§4.16) or deliberate **disinformation** planted by a rival. Intelligence is therefore stored state that ground truth *nudges*, never a pure function of ground truth.
- The belief store is kept **sparse**: an entry is materialized only when (a) an observer has reason to hold non-zero awareness of a region (local presence, patrols, listening posts, recon, battle contact, or scenario seeding), or (b) something explicitly injects a phantom belief. Everything else resolves to `None`/0 without storage.
- `ListeningPost` is the buildable/sabotageable sensor infrastructure, not the belief itself. Listening posts, patrols, battle contact, and recon/intelligence missions drive the **rate** at which an observer's regional belief ratchets upward; decay pulls stale belief back down. A region with low `ListeningPost` coverage and no patrols remains easy to misunderstand, while a public expansion or a seeded insider revolt can be effectively self-announcing.
- Consumers act on belief, not truth: a defender will only generate a *targeted* response (e.g. an Ork cull, §4.22) when its `IntelLevel` on the target reaches `Confirmed` or higher — and acting on a Confirmed-but-false belief wastes force chasing a threat that is not there.
- This generalizes and absorbs the existing governor detection of hidden factions (§4.16) and the OpFor fog-of-war already shipped (§5.2): "what the Imperials know" becomes the special case "what the default-Imperial faction believes about a target in a region."

**Relationship to existing features**
- Revolt (§4.20): the Insurrectionist faction's hostility and its Chaos-affinity relation are expressible as stance entries rather than bespoke rules; its "light affinity" with Chaos cults becomes a `Neutral`/`Allied` pairing.
- Governor relations (§4.16): governor Investigation/Paranoia become inputs to the observer = default-Imperial faction's intelligence beliefs, including the false-positive path.

---

### 4.22 Turn Simulation — Orks & Indelible Infestation

**Description.** Orks are a fungal xenos whose biology makes them categorically different from the factions modeled so far: once their spores take root in a region they **cannot be eradicated** by force — exterminating every Ork only resets a decades-long regrowth, never clears the ground. They spend their feral phase fighting amongst themselves, growing but squandering their numbers, until a population coalesces and a Warboss unites it into a WAAAGH! that erupts outward and acts as a beacon drawing yet more Orks across the sector. This section specifies that lifecycle on top of the cross-faction substrate (§4.21), which it requires.

This is a behavioral specification. It depends on §4.21 (relationships + intelligence) and is scoped as the dependent Ork line item in §5.6.

**Acceptance Criteria:**

**Faction Definition & Seeding**
- The Ork faction is defined by composition over the substrate: `FactionBehavior = UniversallyHostile | Indelible`, with `GrowthType.Logistic`. `UniversallyHostile` makes it an enemy of every other faction (§4.21); `Indelible` drives the no-eradication rules below.
- At sector generation, a portion of inhabited worlds are seeded with a latent Ork presence — generally **feral and undetected** (low or zero population, not yet public), so the infestation exists to act on from turn 1 rather than only arriving mid-game.

**Indelible Presence (cannot be eradicated)**
- An Ork `RegionFaction` is **never removed** from a region once it exists. Reducing its population to zero does **not** delete the presence: instead it flips to non-public (`IsPublic = false`) and, the following turn, regrows to a population of 1 and resumes ordinary growth from there. There is no separate dormancy timer — the slowness of logistic growth from a population of 1 *is* the decades-long ramp.
  - *Design note (math grounding):* at the current `LogisticGrowthRate` (0.0006/week) an empty region's Ork presence grows ~0.0006 individuals/week at population 1 — on the order of decades merely to climb into the hundreds, and roughly two centuries to reach ~1,000 ("real Orks" again) before crowding even matters. The Ork growth multiplier (below) tunes this from "centuries" down to the intended "decades."
- Because presence is indelible and persistent, the no-eradication property lives on the **`RegionFaction`** (via the faction's `Indelible` behavior) rather than as a separate region-level infestation flag — there is no state in which the infestation exists but the `RegionFaction` object does not.

**Growth — feral inefficiency**
- Ork growth uses the existing logistic carrying-capacity model (§4.15) with two Ork-specific modifiers:
  - a **growth multiplier** (Orks breed fast) applied to the base rate, tuned so a *unified* (public) Ork population fills a region in a couple of decades;
  - a **feral efficiency penalty** applied while the presence is **not** public, representing infighting and cannibalism — feral Orks still grow but waste much of their increase fighting each other, so their effective threat ramps slowly until a Warboss unifies them, at which point the penalty lifts and numbers surge.
- Note the existing crowding factor uses *total region population*, so feral Orks grow fastest in sparsely populated regions and are suppressed inside dense human regions — Orks naturally fester in the badlands, which dovetails with the amassing behavior below. This interaction is intentional and load-bearing.

**Two-Dimensional State**
- Ork status is tracked along two independent dimensions rather than a single public/hidden bool:
  1. **Imperial (observer) awareness** — derived from the §4.21 intelligence model: do the region's defenders *believe* Orks are present, and at what `IntelLevel`? A feral camp may be unnoticed (`None`/`Rumor`) for a long time.
  2. **Expansion stage** (`IsPublic`) — *feral* (internal only; not a strategic actor) vs. *WAAAGH!* (public; an extra-territorial actor and a beacon).
- These yield three meaningful states without a third enum: **unnoticed-feral** (growing quietly), **noticed-feral** (defenders aware and able to cull — see below), and **WAAAGH!** (public, expanding, broadcasting).

**Amassing & WAAAGH! Emergence**
- Each turn a fraction of a region's feral Ork population **migrates toward the adjacent region holding the largest Ork population**, creating a gradient that converges the planet's feral Orks onto a single region over time.
- The transition to public (`IsPublic = true`) is triggered by **internal scale** — the converged Ork population in a region crossing a threshold — not by relative military strength against the local garrison. The Warboss emerges because there are enough Orks to unite, not merely because they out-muscle the PDF. Emergence therefore occurs in the amassing/convergence region.

**Imperial Cull (defender response to noticed-feral Orks)**
- When a defender's intelligence on a feral Ork presence reaches `Confirmed`/`Located` (§4.21), and the defender has **spare capacity** (it is not already committed against a revolt, invasion, or other public enemy), it generates culling missions (Extermination/Advance) against the feral Ork `RegionFaction` to keep it suppressed — the canonical "keep the feral Orks down before they get out of hand."
- The cull is intentionally imperfect: a defender with no spare capacity ignores known feral Orks, and a defender acting on a **false-positive** belief (paranoia/disinformation, §4.21) wastes force culling Orks that are not there while real ones grow elsewhere. Culling can never *clear* a region (indelible), only hold it down.

**WAAAGH! as Beacon (sector-level)**
- A public Ork presence at sufficient scale acts as a beacon that periodically:
  - **spawns previously-unmapped Ork worlds** in empty sector tiles. This is physically justified: at solar-neighborhood stellar density (~0.004 stars/ly³; equivalently ~12–13 systems per full-galactic-height column per the 200×200 ly grid), an "empty" tile plausibly contains uncharted systems, so a new Ork world appearing there is honest, not a cheat. Such worlds become known to the player through the §4.2 discovery rules (a WAAAGH! announcing itself), not via player exploration.
  - **dispatches reinforcing Ork fleets** toward the beacon world. Reinforcements originate from real spawned/existing Ork worlds in the sector rather than from an abstract off-map pressure pool — the spawned worlds *are* the reservoir.

**Endings & Persistence**
- A WAAAGH! can be broken (its public forces reduced, its regions re-suppressed), reverting affected regions to feral and, where reduced to zero, to the population-1 regrowth path. The world is never "cleansed": the indelible presence guarantees the threat can re-emerge, making Ork worlds a recurring management problem rather than a clearable objective. (Whether Orks can instead *win* a world outright — extinguish its population and hold it as an Ork-controlled terminal state — is an open question, §6.8.)

---

### 4.23 Turn Simulation — Supply, Requisition & Pledges

**Description.** A Space Marine chapter produces almost none of its own materiel: it sustains itself on the tithes and gifts of the worlds and institutions it serves. This section specifies the supply economy that closes the loop between the request systems (governors §4.16; post-0.7 the Imperial Guard/PDF §6.4 and the Inquisition §6.5) and the chapter's standing need to replace losses and upgrade its forces. Fulfilling a request earns not only opinion but a **pledge** of material support; pledges deliver resources over time along supply lines the Living Galaxy can interdict; and resources are spent to rebuild and improve the chapter.

This is a behavioral specification, delivered in phases. **Phase 1** (0.7 — §5.2) is a minimal Requisition currency: an instant grant on request fulfillment, spent on medical procedures, backing the costs already assumed in §4.8. **Phase 2** (0.7.1 — §5.3) adds pledges that deliver Requisition over time along source-bound supply lines. The remaining **typed-materiel** layer (world-type-driven wargear / vehicles / ships), the Armory wargear inventory (§6.9), pledge interdiction (§6.10), and Inquisition negative-requisition are **post-0.7** (§5.7), as each depends on a system not present in 0.7.

This is deliberately **not** a survival economy. Consistent with the relevance/legacy stakes framing (§4.19), resources gate the *rate* at which the chapter recovers and upgrades — they never produce a starvation or game-over state. A poorly-supplied chapter rebuilds slowly and fights with what it has.

**Acceptance Criteria:**

**Resource Model (two-tier)**
- **Requisition** — a single abstract favor/supply-credit pool held by the chapter; the "political capital" design pillar (§3) made concrete. It is the universal currency: earned from most fulfillments and spent flexibly on small or intangible costs.
- **Materiel pledges** — discrete, typed promises of big-ticket support (a ship hull, a vehicle squadron, a pattern of wargear, recruitment rights, a relic, an intelligence lead). Each is a tracked object with a source, a type, a quantity/payload, and a delivery schedule — not folded into the Requisition number.
- The model is deliberately a forward-compatible subset of a richer typed economy: additional fungible pools (e.g. ammunition, raw materials) may be added later without discarding this structure — the same approach by which Contentment (§4.20) is a forward-compatible proxy for the Pop model (§6.7).

**Pledge Generation**
- When the chapter fulfills a request (§4.16; later §6.4 / §6.5), the fulfilling authority generates a pledge in addition to the existing opinion change. Pledge richness scales with the authority's opinion of the chapter and the significance of the request fulfilled.
- A pledge's possible contents are constrained by the **strategic classification** of the pledging world (a dependency on the post-0.7 classification work noted in §4.1):
  - **Forge worlds** pledge vehicles and ships (shipyards are treated as a flavor of forge world, not a separate class).
  - **Civilized worlds** pledge wargear (weapons and armor).
  - **Hive worlds** pledge recruits/manpower and ammunition.
  - **Agri / Mining worlds** pledge raw materials (feedstock).
  - **Any** world may pledge Requisition, recruitment rights, or an intelligence / plot hook.

**Pledge Types**
- *Standing tithe* — a recurring delivery on a fixed cadence (e.g. a forge world supplying a pattern of bolters each year). Persists until its source is lost or the relationship lapses.
- *One-off* — a single promised delivery (a frigate; a Land Raider squadron).
- *Rights* — unlocks an ongoing capability rather than delivering goods (recruitment rights on a world; folds in §6.3).
- *Intelligence / hook* — a narrative lead that surfaces a mission opportunity (feeding the Mission System Expansion §5.4 and the Inquisition §6.5) rather than crediting a resource.

**Delivery & Supply Lines (hybrid)**
- Requisition earned from a fulfillment credits immediately.
- Materiel pledges deliver over subsequent turns: a delivery is scheduled from the pledging world and arrives after a transit interval.
- A pledge is bound to its source. If the pledging world revolts (§4.20), is overrun, or its governor turns hostile, standing tithes suspend and undelivered one-offs default. Holding a world's loyalty therefore protects its supply, wiring the economy into the Living Galaxy.
- *(Open — §6.10):* whether in-transit deliveries can be interdicted by hostile factional fleets is gated on factional fleet movement existing (§5.7).

**Expenditure (sinks)**
- **Medical procedures** — cybernetic and vat-grown replacements consume Requisition (the cost model already assumed in §4.8 and the §5.2 Apothecary second pass).
- **Recruitment** — recruiting beyond the geneseed constraint draws on manpower pledges and recruitment rights (§6.3); geneseed remains the separate hard constraint (§4.12).
- **Wargear replacement & upgrade** — re-equipping squads and upgrading loadouts. *Phased:* initially abstracted against Requisition; a later phase introduces a real **Armory inventory** — a finite per-pattern wargear pool, depleted as brothers fall and replenished by wargear pledges — housed in the existing Armory node on the Chapter Screen (§4.5). The commitment to a real inventory model is an open question (§6.9).
- **Ships & vehicles** — acquisition and repair, fed by forge-world pledges.
- **Fortification materiel** — supporting the construction missions in §4.13.

**Negative Requisition (Inquisition)**
- The relationship is two-way for high authorities. The Inquisition may **requisition assets *from*** the chapter — drawing down Requisition or seizing materiel as a censure outcome — realizing the "requisition of assets" consequence raised in §6.5. This mechanic is specified here; its triggers and severity remain part of the open Inquisition questions (§6.5).

**Presentation**
- The chapter's current Requisition and its outstanding pledges (source, type, cadence, next delivery) are visible to the player. The **Armory** node on the Chapter Screen (§4.5) is the intended home for resource and, later, wargear-inventory management.

---

### 4.24 Turn Simulation — Tyranid Invasion & Biomass Consumption

**Description.** A Tyranid incursion is not a rival civilization contesting a world; it is an ecological catastrophe that eats the world itself. A splinter of a hive fleet, called down by a Genestealer Cult's psychic beacon, makes planetfall, strips the biomass of every region it reaches, and grows *only* by that consumption. This section specifies the Tyranid faction's growth and behavior, the Imperial population's collapse into hiding beneath it, the Genestealer Cult's doomed uprising, and the opening-scenario sequencing that produces the "Promised World" the chapter is pledged to retake. It builds on the cross-faction substrate (§4.21) and most resembles the Ork growth-faction structure (§4.22).

This is a behavioral specification. It depends on §4.21 (behavior flags, growth types, intelligence-as-belief) and slots alongside the cross-faction simulation work (§5.6). The current `ScenarioBuilder.StampPromisedWorld` static stamp (§4.1) is the shipped subset this enhances.

**Acceptance Criteria:**

**Faction Definition & Growth Type**
- The Tyranid faction is defined by composition over the substrate (§4.21): `FactionBehavior = UniversallyHostile`, with a new `GrowthType.Consumption`. `Consumption` joins the existing `Logistic` (§4.15) and `Conversion` (Genestealer Cult) growth types; the growth-type dispatch replaces the scattered per-faction checks in the population loop.
- A Consumption faction has **no organic birthrate**. Every point of population it gains comes from biomass it has eaten (below). Its numbers are therefore bounded by, and drawn from, the biomass actually present on the planet.

**Biomass: Consume vs. Predate (two distinct actions)**
- Tyranid forces convert biomass to Tyranid population through two separate actions with different targets and different permanence:
  - **Predate** — hunts *headcount*: the population of co-located non-Consumption factions (Imperial civilians, PDF, Genestealer Cult). Distributed across all edible populations **proportional to their share** of the region's non-Tyranid population — a region that is 90% Imperial / 10% Cult is predated in that 9:1 ratio, with no special-casing. Predation is **recoverable** damage: kill every inhabitant, but if the land's capacity survives, a retaken region can be repopulated.
  - **Consume** — attacks the *land*: it reduces the region's current `CarryingCapacity` (scouring flora, fauna, and soil into the digestion pools). Consumption is the **deep wound** — it heals only slowly, so a region whose capacity is eaten is a near-dead world even after liberation.
- Both actions feed the swarm: each point of biomass consumed, headcount or land, adds to Tyranid population. Predation and Consumption together are the Tyranid form of the generic **predation** behavior (a public enemy killing hidden civilians); a non-Consumption enemy (the Cult) only ever performs the killing half, never the land-scouring half.

**Carrying Capacity — Maximum & Recovery**
- Each region gains a **`MaximumCarryingCapacity`** — a new field on the **save** schema's `Region` table (not the rules DB), initialized equal to `CarryingCapacity` at generation. `CarryingCapacity` becomes the current, degradable value; `MaximumCarryingCapacity` is the natural ceiling it recovers toward.
- Consumption depresses `CarryingCapacity`; each turn it creeps back toward `MaximumCarryingCapacity` at a slow recovery rate. While Tyranids are present and eating, consumption outpaces recovery and the land stays blighted; once they are cleared, recovery gradually restores it. No explicit "are Tyranids here?" check is needed — the two rates net out on their own.
- Because the crowding factor (§4.15) turns growth negative above capacity, scouring the land while population is still high *accelerates* the death of the survivors: the ecology collapses under them. Consumption, predation, and crowding all pull the same direction.

**The Biomass Budget (emergent)**
- Because Consumption growth is bounded by available biomass and each point of growth draws the pool down, the planet has a finite biomass budget the swarm consumes over time. Two properties fall out for free:
  - **Forced expansion.** Once a region's biomass is exhausted the Tyranids there stop growing and must attack adjacent regions to reach fresh biomass — the spreading tide is emergent, not scripted.
  - **Winnable by construction.** Combined with the stranded-fleet premise (below), the swarm cannot grow without limit; the campaign is a race to break it before it has drawn down enough of the world to become unstoppable. Longer pre-arrival delays therefore mean both a larger swarm *and* a more blighted world to inherit.

**Tyranid Troop AI**
- The mobile combat force is the existing `organizedTroops = Population × Organization / 100` (`FactionStrategyController`). The *unorganized* remainder is the abstraction for the swarm's **feeding pools and gestating brood** — immobile, non-combat, and the engine of Consumption growth. Modeling those pools as discrete, strikable structures (a "destroy the birthing pits" objective) is deferred (§6.11); until then `Organization` carries the troop-vs-structure split.
- Each turn the AI allocates `organizedTroops` by priority:
  1. **Fight** — commit force to any in-region military threat (a PDF garrison, or a Cult fighting the swarm); the swarm is drawn to resistance first.
  2. **Expand** — send a share of the remainder to neighbors, biased toward the richest / most-resistant region. The share scales with local **depletion** (`depletion = 1 − ½·(civiliansLeft/civiliansAtStart + capacity/maxCapacity)`): a rich region keeps its forces home to gorge, a stripped one pushes them onward.
  3. **Predate + Consume** — the remainder strips the current region: predate while headcount remains, consume the land in parallel, shifting fully to consumption once the survivors are gone.

**Genestealer Cult Behavior**
- The Cult reveals itself (all its hidden `RegionFaction`s across the planet flip public) when the hive fleet is known to be inbound — sowing chaos to cripple the defense, not realizing the swarm will devour it as readily as everything else.
- A public Cult **fights the local PDF**. When the local Imperial garrison collapses into hiding, the Cult's spare force **relocates to adjacent regions that still hold an active PDF** to keep fighting. Where no neighboring PDF remains, idle Cult forces **predate** — but never consume: they are cultists, not a devouring swarm.
- **Cult predation does not grow the Cult.** Conversion (implanting genestealer genes) is their growth path; this stage is the slaughter of non-believers as sacrifices to the Star Children — pure killing, no population gain, no land damage.
- **Conversion growth only happens while hidden.** A `Conversion` faction converts (and enjoys its accelerated at-scale organic growth) *only while its `RegionFaction` is hidden* (`IsPublic = false`). Converting the populace is clandestine — stealing individuals away at night to be implanted — which depends on cover, opportunity, and a populace still going about its life. Once the Cult reveals into open warfare, that collapses: a **public** converting faction makes no new converts and does not grow that turn (it can still fight, relocate, and sacrificially predate). This is a general rule of the `Conversion` growth type, not Cult-specific.
- The Cult is prey like any other headcount: Tyranid predation hits it proportional to its population share. Welcoming the hive earns it no protection.

**Imperial Remnant — Hide/Unhide Stages (region-level)**
- The default-Imperial `RegionFaction` moves through four stages per region, driven by conditions rather than a scripted stamp:
  1. **Governing** — no public enemy (or garrison holding); normal growth and PDF drafting. `IsPublic = true`.
  2. **Besieged** — a public enemy is present and grinding the garrison down (strategic combat, below). Still `IsPublic = true`.
  3. **Overrun / Hidden** — the garrison hits zero **with a public enemy present**; the population goes to ground. `IsPublic = false`. While hidden it accrues **no garrison and no organic growth** (surviving, not drafting), and is subject to emigration and predation.
  4. **Liberated** — the region's last public enemy is cleared; the remnant returns to `IsPublic = true` and rebuilds a garrison from its survivors. (If the population reached zero, the region is simply held and depopulated.)
- `IsPublic = false` on the Imperial remnant here means "gone to ground," and this hide/unhide is evaluated **per region** for the default faction — distinct from the existing planet-level `CheckForPlanetaryRevolt` / `CheckForRevoltSuppression`, which exclude the default faction and only surface infiltrators. (Generalizing all going-public transitions to region granularity is an open question — §6.12.)

**Civilian Emigration (Stage 3)**
- Each turn, ~5% of a hidden remnant's population flees to **adjacent Imperial-controlled regions**, distributed **weighted by the destination's population** (refugees pour toward the nearest dense fortification — the suburb, not the desert). A fully-surrounded remnant (no eligible neighbor) cannot flee and faces predation in place.
- Emigration is **not** clamped to destination capacity: overfilling a refuge is intended. The crowding term (§4.15) then models the resulting deprivation die-off, and the swollen population makes that region a worse massacre if the swarm reaches it next — a deliberate grimdark feedback loop.

**PDF as a Defensive Actor**
- For the pre-arrival and invasion simulation to produce believable outcomes (regions where a dug-in PDF drives the Cult back), the default-Imperial faction must become a strategic actor, resolving the standing TODO in `UpdateRegionFactionForces` — today only non-player, non-default factions run through `FactionStrategyController`, so PDFs cannot build defenses.
- First cut is **defensive only**: the PDF fortifies (Entrenchment), builds `ListeningPost` sensor infrastructure, and holds — it runs the development/construction slice of `FactionStrategyController` but launches no offensives. It is deliberately **less effective than the Imperial Guard** forces specified later (§6.4): the PDF holds the line and buys time; it does not maneuver or counterattack.

**Strategic Combat Model**
- PDF ↔ Tyranid ↔ Cult fighting resolves as a lightweight **strategic attrition** step — committed force × effectiveness against defender garrison, modified by the existing `Entrenchment` / `ListeningPost`-fed regional intel / `AntiAir` / `Organization` ratings. This is distinct from the tactical Battle engine (§4.14), which is reserved for Astartes engagements; running tactical resolution for every AI skirmish across a multi-turn headless pre-simulation would be far too costly.

**Opening Scenario Application (the Promised World)**
- The opening scenario sequences the two enemies in time so the Tyranid beachhead stays authored while the Cult war is emergent (extends `ScenarioBuilder.StampPromisedWorld` and the OpeningScenario design doc):
  1. Seed the hidden Cult across the planet (existing).
  2. **Cult reveals** — flip the Cult public planet-wide (the beacon lit).
  3. **Seed insider belief** — give the Cult strong per-region belief about public non-Cult forces before it plans, representing embedded cells and sympathizers rather than omniscient ground truth.
  4. **Pre-landing simulation** — run ~2–3 headless turns of *this planet only*: the Cult uprising fights the PDF, some cells spreading, others suppressed and driven back underground; defenders weakened.
  5. **Tyranids make planetfall** — stamp the authored beachhead (a contiguous landing cluster) *after* the pre-sim, fresh and controlled.
  6. **The Navy destroys the hive fleet** — narrative, plus the mechanical fact that the ground swarm has **no reinforcement** (there is no orbital-drop mechanism today, so "stranded" is largely the default). This is what makes the world winnable and leaves orbit clear for the player to land at will.
  7. **Post-landing simulation** — run a Gaussian-rolled number of headless turns: `postLandingTurns = max(0, round(4 + z))`, `z` a standard normal (turns are weekly; widen the coefficient if the rare two-month apocalypse should occur more than a ~4σ event). Rarely the player arrives with the Navy to a fresh beachhead; sometimes the swarm has had a month-plus to eat the PDF and citizenry.
  8. **The player arrives** — the fleet is stamped into orbit (existing) and control passes to the player.

**Endings & Persistence**
- Unlike Orks (§4.22), Tyranids are **not** indelible: a `RegionFaction` reduced to zero is cleared, and a planet swept of the swarm is genuinely retaken — its blighted regions then recovering capacity toward `MaximumCarryingCapacity` over years. Victory is a drawn-down world slowly healing, not a permanent management problem.

---

### 4.25 Chapter Mandates & Legacy Objectives

**Description.** The Promised World gives a new campaign a strong opening horizon, but its resolution must not leave the sandbox directionless. Chapter Mandates are generated, medium-term objectives that express what the Chapter is trying to become without imposing a rigid victory screen or ending the campaign.

**Acceptance Criteria (Planned — 0.8):**
- Once the Promised World resolves, the campaign offers a small set of state-aware mandates rather than a fixed universal quest chain. The player chooses or accepts a limited number so the system supplies direction without becoming a task list.
- Initial mandate families include: establish or secure a Chapter World; rebuild a depleted company; protect several strategic Imperial worlds; eradicate a named regional threat; reach a defined reputation with sector authorities; and establish a functioning supply network.
- Mandates are generated only when their prerequisites and completion conditions are meaningful in the current sector. A mandate cannot ask the player to interact with content or systems absent from that campaign.
- Progress, stakes, and rewards are visible in the Command Brief. Relevant events deep-link to the affected worlds, forces, characters, or supply lines.
- Completion produces an appropriate persistent consequence — reputation, Requisition, a pledge/right, a Chronicle entry, or a new mandate horizon. Failure or expiration changes relationships or sector state but is not a campaign-ending fail state.
- Mandates coexist with free-form play. The player may ignore them and continue the sandbox, accepting the simulated and political consequences.

---

### 4.26 Planet Tactical Map & Information Legibility

**Description.** The current tactical-region hexes attempt to communicate ownership, several force types, orders, intelligence, fortifications, and multiple hostile factions through icons too small to read reliably. Alpha 0.8 receives a legibility-first redesign of the map's information hierarchy rather than another incremental icon addition.

**Acceptance Criteria (Planned — 0.8):**
- Produce and approve a visual baseline before implementation. The redesign may change icon placement, tile bands, labels, color hierarchy, and zoom behavior; compatibility with the current single-icon slots is not a constraint.
- At the normal decision-making zoom, a player can distinguish controlling faction, player presence, public hostile presence, active orders, and selected state without relying on tooltips or memorizing tiny glyphs.
- Information is layered and zoom-adaptive: the normal view shows the most important state, while closer zoom or the selected-region presentation may reveal faction names, intel-gated magnitude, fortifications, and secondary forces.
- A region containing multiple public enemy factions visibly reads as contested and preserves each disclosed faction's identity. Hidden factions remain absent, and ordering/color must not leak undisclosed raw strength.
- The map and the selected-region dossier use the same intel-gated presence model and terminology. The roomy Region detail header continues to show one pill per public hostile faction.
- Icon size, contrast, label scaling, and color choices remain readable at the supported minimum UI scale and common color-vision deficiencies.

---

### 4.27 System Menu, Accessibility & Release UX

**Description.** The top-level System Options action must provide the campaign-control and presentation settings expected of a deployable desktop game. These controls are release infrastructure, not simulation features.

**Acceptance Criteria (0.7.1 — Implemented):**
- The System Options button opens a working modal with Resume, Save, Load, Return to Title, and Quit. Destructive navigation prompts when unsaved progress would be lost. Escape opens/closes the System menu globally during campaign play; X closes the top gameplay dialog while text fields retain normal typing behavior.
- End Turn uses a conditional preflight only when there is meaningful unresolved attention: idle deployable squads, actionable fleets without orders, or unassigned special missions at risk. Special missions are described accurately as having an independent 25% disappearance chance per turn while visible (and being cleared at zero intelligence), not as having a fixed expiry. The player can proceed immediately and may configure global warning preferences; routine turns do not require a redundant confirmation.
- The System menu can export a diagnostic bundle containing the game/build version, global settings, and recent logs, with a fresh current-campaign snapshot included only through an explicit player choice.
- Headless scene-wiring smoke tests instantiate the release-control surfaces and verify that required top-level actions have subscribers and can open their intended surfaces. A visible but inert button is a release-blocking failure.

**Acceptance Criteria (Planned — 0.8):**
- Add fullscreen/windowed display mode and UI/text scaling, with an implementation and visual-QA pass across all supported screens.

---

## 5. Release Scoping

### 5.1 Released (Alpha 0.6 and prior)

- Galaxy generation with faction-controlled planets
- Full battle simulation: movement, ranged combat, melee, wounds, experience gain
- Battle Review Screen
- Chapter Screen: squad composition management and loadout selection
- Planet Detail Screen: fleet/troop management, planetary data, governor and reputation
- Region Screen: order assignment with copy/paste
- Apothecary Screen: injury display and geneseed inventory
- Recruiter Screen: training readiness reporting
- Squad Screen and Soldier Screen
- Living galaxy: Genestealer Cult growth and outbreak; faction construction and offensive planning; patrol generation
- Governor personality, detection, and request generation
- Full save/load
- Tyranid faction: Warriors, Carnifex, Ripper Swarms, Genestealer Cult troops, expanded species
- Space Marine faction: Tactical, Assault, Devastator, Scout, Veteran squads and full weapon set

### 5.2 Alpha 0.7 — Committed

The following must ship in 0.7. Status reflects the current codebase (✅ done · ◐ partial · ⬜ not started):

- ✅ **Training for non-deployed forces:** Non-deployed marines accumulate training skill points each turn. *(Implemented.)*
- ✅ **Recruiter Screen Phase 2:** Deployed scouts excluded from training; squad-specific training focus. *(Implemented; the screen now displays the 10th Company / TrainingUnit.)*
- ✅ **Game Start Phase 1:** Complete new game setup screen and flow. *(Implemented — New Game opens a styled setup screen where the player names their Chapter and sets (or randomizes) a sector seed, with validation and a confirm/summary step before the campaign launches. Deeper chapter customization remains post-0.7.)*
- ✅ **Planet View Phase 4 completion:** Governor aging and replacement; visible opinion signal on planet screen; request fulfillment requiring meaningful engagement. *(Implemented — governors now age, die, and are replaced; opinion shows on the Planet Screen.)*
- ✅ **Diplomacy/Requests display:** Active governor requests visible in a dedicated screen or panel. *(Implemented — the footer Diplomacy button opens a Sector Requests screen listing every active governor request with its planet, concern, date, and engagement status; requests also still surface on the Planet Screen.)*
- ✅ **Fleet movement:** Ships can be ordered to move between planets; transit time applies. *(Implemented — task-force context menu with plot-course / divide / merge, phased warp travel with variable transit and estimated arrival range, warp-lane routing, and in-warp fleets hidden and out of contact. The "Chart Direct Route (Risky)" option is deferred post-0.7, see 4.17.)*

- ✅ **Battle screen visual overhaul:** Rework the existing Battle Review Screen from a simple grid-plus-turn-log into an automated battle replay/report display, using the V3 Chronicle Formation Hybrid direction captured in `Design/VisualBaselines/BattleReview/battle_screen_mockup_v3_01_chronicle_formation_hybrid.png`. This is not a tactical command surface: battles have already resolved, so the UI should avoid order buttons, bottom command rails, movement previews, and blue/red territory ownership washes. The screen should instead help the player understand what happened in a sizable battle. *(Done for 0.7 — the four-region layout, view-model layer, summary service, collapsible force tree with atlas icons, selected-formation panel, event chronicle, timeline, and casualty-by-round table are all built and rendering from the shared `OnlyWarStyle`/`IconAtlas` UI helpers, and the replay viewport now draws formation banners, casualty and rout markers, projectile/charge/melee callouts, and runs an automatic round-by-round playback loop with a wired-up speed selector. Playback advances discretely per round (each round's end-state is redrawn) rather than interpolating motion; smooth position tweening and in-flight projectile animation are deferred to Battle Visuals Phase 3, §5.4. Detail below.)*
  - ✅ **Build a battle replay display model between `BattleHistory` and the UI:** force hierarchy nodes, selected formation summary, battle event entries, round timeline entries, and casualty-by-round summaries. *(Implemented in `Models/Battles/BattleReplayModels.cs` — `BattleReplayDisplay` plus `BattleForceHierarchyNode`, `BattleFormationSummary`, `BattleEventEntry`, `BattleTimelineEntry`, `BattleCasualtyRoundSummary`, and a `BattleEventSeverity` enum.)*
  - ✅ **Add a summary/analysis service that derives those view models from `BattleHistory`/`BattleTurn`:** force tree, current round, losses per force, losses by round, selected formation stats, notable events, and event chronology rows. *(Implemented in `Helpers/Battles/BattleReplaySummaryBuilder.cs`, with unit coverage in `OnlyWar.Tests/Battles/BattleReplaySummaryBuilderTests.cs`. Derives the force hierarchy, per-round and cumulative casualties, result/phase labels, formation summary, and per-action event entries from the resolved turns.)*
  - ✅ **Replace the current Battle Review layout with a four-region structure** *(the left/center/right/bottom layout is built in `battle_review_screen.tscn` and wired in `BattleReviewView.cs`/`BattleReviewController.cs`; per-region status below):*
    - ✅ Left: collapsible force hierarchy tree with opposing forces, nested companies/squads/vehicles, strength counts, losses, selected row highlighting, and small faction/type icons. *(Implemented — nested player/opposing → unit → squad rows with current/starting strength, losses, and selected-row highlighting; clicking a squad row selects the formation. Parent rows are now collapsible (per-node `[+]`/`[-]` disclosure backed by `_collapsedForceNodes`), and each row carries a real faction/type icon resolved from `IconKey` via `IconAtlas`.)*
    - ✅ Center: replay viewport showing formations, banners, casualty markers, routed trails, projectile/charge/event callouts, and compact top-center playback controls (previous round, step back, play/pause, step forward, next round, speed). *(Done: a `SubViewport`-based replay viewport draws the battlefield grid, per-soldier formation markers (cyan/crimson by affiliation), centroid formation labels with live strength, a selection ring/highlight, and clickable markers that select the formation. Each round also draws formation banners (`DrawFormationBanner`), casualty X-markers and rout trails derived by diffing the previous and current `BattleState` (`DrawCasualtyMarkers`/`DrawRoutMarkers`), and projectile/charge/melee callout arrows from the round's actions (`DrawActionCallouts`). The top-center playback row is fully wired. Remaining (deferred to Battle Visuals Phase 3, §5.4): markers snap between round end-states with no position interpolation, and callouts are static annotations rather than animated motion.)*
    - ✅ Right: selected formation summary at the top (commander, starting/current strength, losses, fatigue/morale/ammunition/effects where data exists), with event chronicle below it for timestamped actions, morale checks, routs, volleys, casualties, and phase summaries. *(Implemented — the top panel shows formation name/type/force, commander, starting/current strength, losses with percentage, and derived fatigue/morale/ammunition labels plus a notable-effects list; the chronicle below lists per-action event cards with timestamp, type, actor, formation, and severity-coloured borders. Fatigue/morale/ammunition are heuristic labels derived from available data rather than first-class simulation values.)*
    - ✅ Bottom: informational battle timeline and casualties-by-round table, not a command menu. *(Implemented — a horizontal clickable round timeline (each round seekable, severity-tinted) and a casualties-by-round grid showing per-round and cumulative player/enemy losses.)*
  - ✅ **Preserve the visual language established by the Sector Map and Chapter Screen:** dark panels, antique-gold borders, parchment text, smoky glass, muted cyan/crimson only for unit affiliation markers and row accents, and amber for warnings/notable events. *(Implemented — the screen now draws its colours and styleboxes from the shared `OnlyWarStyle` helper (player/opposing accents, gold, muted text, critical/warning tones, `ApplyAccentButtonRow`, `ApplyEventPanel`) rather than locally-defined literals, bringing it in line with the other screens.)*
  - ✅ **Upgrade the current previous/next-turn behavior into playback controls.** Initial implementation may still step discretely through resolved turns; smooth animation and richer projectile/path interpolation can follow once the structural UI is in place. *(Done for 0.7 — the five playback buttons work (step-back/step-forward move one round, previous-round/next-round jump to the first/last round, play/pause), and play now runs a real automatic loop in `_Process` that advances rounds on a time budget scaled by the selected speed. The `SpeedButton` is wired to `CyclePlaybackSpeed` (0.5×/1×/1.5×/2×). Playback is still discrete per round; smooth animation and projectile/path interpolation are deferred to Battle Visuals Phase 3, §5.4.)*
  - ✅ **Keep the first vertical slice data-driven and robust for large battles:** the force hierarchy, event chronicle, selected formation summary, and casualty timeline should remain readable with many units before investing heavily in visual animation. *(Done — all four panels are driven entirely from the view models, lists are scrollable, and the casualty table caps to the most recent rounds; the structural slice is in place ahead of any heavy animation work.)*
- ✅ **Strategic Layer Phase 2:**
  - ✅ Population growth relative to planet carrying capacity (faster growth when underpopulated, slower when near capacity). *(Implemented — carrying capacity is an absolute, per-type value rolled from new `PlanetTemplate` columns (`CarryingCapacityBase`/`CarryingCapacityStandardDeviation`), distributed across a planet's regions and persisted per region. Starting population is seeded as a fraction of each region's capacity so no world begins above capacity; dense biomes (Hive, Forge) start nearly full while sparse ones (Agri, Feral) have room to grow. Per-type population and capacity scales are canon-grounded (Hive ~80B typical down to Death ~310K) and stored as log-normal `Floor`/`Scale` values. Each turn, organic (logistic and baseline) growth is scaled by a `1 - regionPop/capacity` crowding factor — near-maximal when sparse, zero at capacity, and gently negative above capacity so an overfull region drifts back down.)*
  - ✅ Garrison attrition (0.1% of garrison retires per week, requiring replacement from population growth). *(Implemented — each week 0.1% of a faction's regional garrison retires before fresh recruitment from population growth is applied, in the same factions that recruit: PDF, player, and hidden/secret factions.)*
  - ✅ OpFor fog of war, recon orders, and special missions. *(Implemented — recon orders raise a region's intelligence and the special-mission/intelligence system already existed; this pass closed the remaining fog-of-war leak in the UI. Hidden factions are now concealed on every screen (folded into the civilian count, discovered only via the intelligence system); public-enemy population is graded by intelligence level ("Unknown" → fuzzed → exact); and enemy defenses appear only once intelligence exceeds a threshold and only as fuzzy descriptions, never raw values. The Region screen previously revealed hidden-faction identity and exact garrison/defense values regardless of intelligence — now consistent with the planet tactical screen.)*
  - ✅ Diversion missions. *(Implemented — a squad can be ordered to run an overt "Diversion" feint against an enemy-held region while remaining in its own. Unlike stealth missions it skips infiltration: a `DemonstrateForceMissionStep` makes a daily show of force (a Tactics check whose difficulty rises with the target's defender-held regional intel and garrison size), accumulating Impact. Diversions resolve in a new pre-planning "shaping" phase **before** factions generate their turn orders, so the feint shapes enemy decisions that same turn. A successful feint projects a superlinear `apparentThreat = manpower × (1 + impact/scale)²` onto the target's `PerceivedThreatBonus`, inflating the garrison its controller feels it must hold; at Normal aggression or higher it also raises the feinting force's `ProvocationLevel`, which lowers the AI's force-ratio threshold for attacking (toward parity) and biases its target selection — baiting a counterattack. Because the feint force stands in the open, it is pulled into the fight as a defender if it draws that counterattack. Both effects are transient: set during the shaping phase, consumed by faction planning, then cleared the same turn (never saved). **Enemy-generated diversions (the AI running its own feints) are deferred post-0.7 — see §5.7.***)*
  - ✅ Player-constructable fortifications (Entrenchments, Listening Posts, Anti-Air batteries). *(Implemented — a squad in its own region can be ordered to Fortify (Entrenchment), Build Listening Post, or Build Anti-Air; the squad spends the turn building instead of fighting. Progress scales with squad size and a new Intelligence-based "Engineering (Fortification)" skill that all combat marines train, accumulating defenses over successive turns. The construction-mission resolution and save round-trip are shared with the existing NPC development path.)*
  - ✅ Burrowing and camouflage as ambush tactics. *(The 0.7 scope is complete: species evasion and burrow-arrival placement are implemented. Immediate burrow/flight retreat belongs to the broader withdrawal system in Battle Logic Phase 4 rather than this completed 0.7 commitment; see §5.4.)*
    - ✅ **Evasion / hard-to-hit modifier:** *(Implemented — a per-species evasion value is now subtracted from the attacker's total in both `ShootAction` and `MeleeAttackAction`, giving elusive bodies (serpentine Raveners, weaving Genestealers/Lictors) a defensive "harder to hit" lever in melee as well as ranged without overloading Size, which still tracks real bulk for wounds/footprint. Melee attacks now also account for the defender's melee skill. Previously the Ravener leaned on its high MoveSpeed for ranged evasion only; this closes the melee gap.)*
    - ✅ **Burrow arrival:** *(Implemented — a wholly burrow-capable squad erupts around the nearest enemy squad after normal battle placement, spilling into successively wider rings when the immediate perimeter is full.)*
- ✅ **Subsector warp lanes:** *(Implemented.)* Each subsector has a capital world, determined by an importance score (population size for 0.7; strategic classification post-0.7). The capital has established warp lanes to all other planets in the subsector. Cross-subsector lanes connect the capitals of adjoining subsectors, with a spanning tree guaranteeing the whole sector is reachable. Lanes are derived deterministically from planet positions during sector creation (and rebuilt on load) rather than persisted. The fleet movement dialog routes along lanes by default. The "Chart Direct Route (Risky)" option is deferred post-0.7 (see 4.17).
- ✅ **Tyranid Infiltration Units:** Lictor and Ravener content data. *(Species, SoldierTemplate, and skill-training data added via the `migrate-tyranids` rules-DB migration — Lictor as an elite WS6 ambusher (S6, T5/6-wound, Perception-led senses, high Stealth, melee carried by skill rather than Dex) and Ravener as a fast-attack glass cannon (the fastest ground bug, Hormagaunt-level instincts, Warrior-tier body). Squad templates added via `migrate-tyranid-squads` — the Lictor as a solo unit (`Scout | Elite`, 15mm chitin) and the Ravener as a 5-strong leaderless pack (`Scout`), both with 15mm chitin and Scything Talons; both are now eligible for `ForceGenerator` dynamic forces (scout patrols and generic forces), and the per-species melee/ranged evasion lever they were designed around is now in place (see above). The fixed `UnitTemplate` armies — legacy scaffolding only the player's Space Marine chapter ever instantiated — were unused for every non-player faction, so the dead Tyranid (and Genestealer Cult) `UnitTemplate` rows were dropped from the rules DB via the `remove-unused-unit-templates` tool command rather than extended.)*

- ✅ **Apothecary Phase 2:** Cybernetic replacements; geneseed recovery noted in death records; recovery time displayed on squad screen. *(Implemented — the first presentation/data pass, the second pass (weekly healing, persisted/interactive medical procedures with staff/location/cost gates, backed by the Supply & Requisition Phase 1 currency), and the third pass (Squad Screen recovery surfacing, death-record geneseed recovery, and geneseed purity as real aggregate domain data) have all landed.)*
  - ✅ **First pass - UI direction and layout:** Reworked the Apothecary Screen around the V2 flow captured in `Design/VisualBaselines/Apothecarium/apothecarium_refresh_v2_01_vault_default.png`, `Design/VisualBaselines/Apothecarium/apothecarium_refresh_v2_02_soldier_wounds.png`, and `Design/VisualBaselines/Apothecarium/apothecarium_refresh_v2_03_unit_rollup.png`. The first pass replaces the old two-report layout with a persistent left panel containing a default-selected `Gene Seed Vault` button and wounded-filtered chapter/unit tree, plus a stateful detail panel for vault, unit/squad rollup, and soldier medical views.
  - ✅ **First pass - structured view data:** Added structured Apothecary view models and a medical summary builder so the controller renders data instead of formatting large UI strings. The builder derives wounded soldiers, serious wounds, out-of-action duration, unit/squad rollups, wound rows, severed body parts, cybernetic state, and gene-seed maturity data from the existing domain model.
  - ✅ **First pass - vault panel:** Shows mature gene-seed stockpile, mature implanted progenoids, immature implanted progenoids, progenoids maturing within one year, and at-risk implanted progenoids. *(Purity began as a presentation-only status here; it is now backed by real aggregate domain data — see the third-pass purity item below.)*
  - ✅ **First pass - soldier medical panel:** Selecting a soldier shows wound locations, severity, expected recovery time, whether gene-seed-bearing locations are safe/damaged/lost, replacement eligibility, and cybernetic/vat-grown replacement options. Replacement buttons are presentation-only in this pass.
  - ✅ **First pass - Vitruvian wound display:** Added a custom diagnostic body diagram that maps current human hit-location names to stable coordinates and colors them by wound severity, severed state, crippled state, and cybernetic replacement.
  - ✅ **First pass - unit/squad rollup:** Selecting a squad or unit summarizes medical readiness before drilling into individual soldiers, including healthy, wounded, out-of-action, ready-next, maximum recovery time, and serious wound rows.
  - ✅ **First pass - verification:** Added focused tests for the medical summary builder. Full test suite and headless Apothecary scene load pass.
  - ✅ **Second pass - weekly natural-healing pass (foundation):** *(Implemented.)* Wired wound recovery into turn processing. `Wounds.ApplyWeekOfHealing()` was dead code; `TurnController.ProcessTurn` now runs a `ProcessMedical` step (via the new `MedicalTurnProcessor`) that applies a week of healing to every wounded player-soldier hit location regardless of deployment, *except* locations that need a replacement procedure (severed, or a crippled functional/vital location — `HitLocation.IsReplacementEligible`, now the single source of truth shared with the Apothecarium view). Covered by `MedicalTurnProcessorTests`.
  - ✅ **Second pass - persisted medical procedures:** *(Implemented.)* Added the `MedicalProcedure` model (`Models/Soldiers/MedicalProcedure.cs`: soldier id, hit-location template id, `MedicalProcedureType`, weeks remaining, Requisition cost paid up front) and the `MedicalProcedureType { Cybernetic, VatGrown }` enum (promoted to the domain, replacing the old view-layer `ReplacementType`). The list lives on `Army` beside the Requisition pool and roster. Persisted via a new `MedicalProcedure` table (FK to `Soldier`) and `MedicalProcedureDataAccess`, with the Requisition pool stored on the extended `GlobalData` row. Save/load round-trip tested.
  - ✅ **Second pass - turn processing (procedures):** *(Implemented.)* `MedicalTurnProcessor.ResolveProcedures` runs in `ProcessMedical` after natural healing: it decrements each procedure's weeks-remaining and, on completion, clears the hit location's wounds and removes the procedure — cybernetic completion also sets `HitLocation.IsCybernetic`, vat-grown leaves it clear. A marine under a procedure stays out-of-action automatically (wounds aren't cleared until completion). Covered by `MedicalTurnProcessorTests`.
  - ✅ **Second pass - interactive treatment assignment:** *(Implemented.)* `MedicalProcedureService.TryAssign` validates eligibility/location/co-located staff/affordability, deducts the cost, and creates the procedure; the Apothecarium controller wires the replacement buttons to it and refreshes on success. The controller enriches each option with the per-requisite breakdown (co-located Apothecary, co-located Techmarine, valid surgery site, sufficient Requisition) via `MedicalProcedureService.EvaluateRequisites` and drops a location already under treatment; the view renders each requisite green (met) / red (unmet) and enables the assign button only when all are met (§4.8). Covered by `MedicalProcedureServiceTests`.
  - ✅ **Second pass - staff, location, and cost rules:** *(Implemented.)* Procedure week-counts and Requisition costs live in `MedicalProcedureRules` (not UI literals). `MedicalProcedureService` enforces the §4.8 gates: an Apothecary **and** a Techmarine **co-located** (same ship or same region) with the wounded marine to *begin* the procedure (start-of-procedure check, no duration lockout; staff identified by template name), and a valid surgery site (aboard a ship, or an imperial/player-controlled Hive/Forge/Civilised region via `Planet.Template.Name`). No fortress-monastery home world is modeled in 0.7, so the player-held region serves as the de-facto base. Costs are backed by the Supply & Requisition Phase 1 currency below.
  - ✅ **Third pass - cross-screen recovery display:** *(Implemented.)* The Squad Screen member roster now surfaces each brother's expected recovery time, mirroring the Apothecary screen (`SquadScreenController.GetRecoveryStatus` uses the same primitives — per-location `Wounds.RecoveryTimeLeft()`, `HitLocation.IsReplacementEligible`, and any in-progress `MedicalProcedure` — so the figures agree). Injured members are visually distinguished (crimson for out-of-action / replacement-required, amber for lighter wounds), closing the still-unmet §4.6 "injured members visually distinguished" criterion as well.
  - ✅ **Third pass - death records and geneseed recovery:** *(Implemented.)* Gene-seed recovery was a stockpile side-effect buried in battle-log string building; it is now a clear `ResolveGeneseedRecovery` resolved once per confirmed-dead brother in `BattleTurnResolver.RemoveSoldiersKilledInBattle`, which folds any recovered gland's purity into the chapter aggregate and writes a structured `SoldierEventType.GeneseedRecovery` event (Recovered/Destroyed/Immature, with purity magnitude) onto the preserved fallen-brother dossier. The battle log reads the recorded outcome rather than recomputing it. *(The eulogy-style narration of "lost geneseed as a compounding loss" remains 0.8 narrative work per §4.19; the structured record it draws on is now retained.)*
  - ✅ **Third pass - geneseed purity decision:** *(Resolved — purity is real domain data, aggregate form.)* Rather than itemizing the stockpile, `PlayerForce` now carries a count-weighted aggregate `GeneseedPurity` float alongside the stockpile count. Seeded pristine at founding; each recovered gland contributes a purity rolled around a baseline with small downward drift (`GeneseedRules`), updating the running average. Both the count and the purity are now **persisted** on the extended `GlobalData` row (closing a latent bug — the stockpile count was previously not saved at all). The vault panel surfaces the real aggregate purity and a derived quality label (Pristine/Stable/Degraded/Corrupt). Purity is tracked and displayed now; its *consumption* — initiate creation reading and drawing down the stockpile — lands with the recruitment pipeline (§4.9, post-0.7). Covered by `GeneseedPurityTests` (folding + roll bounds), an extended save/load round-trip, and vault-summary tests.

- ✅ **Supply & Requisition Phase 1 — minimal currency:** The faucet-sink-balance spine that makes the Apothecary procedure costs real. Full spec in §4.23. *(Implemented alongside the Apothecary second pass.)*
  - ✅ **Requisition pool:** *(Implemented.)* `Army.Requisition` integer, seeded at founding (`NewChapterBuilder`, generous 1000) and persisted across save/load via the extended `GlobalData` row.
  - ✅ **Faucet (instant grant):** *(Implemented.)* `TurnController` grants a flat Requisition amount when a governor request is fulfilled, at the existing `IsRequestCompleted()` hook that already applies the opinion change. No pledges/delivery yet (that is Phase 2). Covered by a faucet test.
  - ✅ **Sink:** *(Implemented.)* The Apothecary procedures spend Requisition; `MedicalProcedureService.TryAssign` rejects an unaffordable procedure. Costs live in `MedicalProcedureRules`.
  - ✅ **Display:** *(Implemented.)* The Apothecary vault panel surfaces the current Requisition balance; each replacement option shows its cost and a `Requisition N (have M)` requisite line, with the assign action disabled when unaffordable.
  - ✅ **Tuning note:** Founding balance starts generous (1000) for end-to-end verification, to be tuned down later.

- ✅ **Game Start Phase 2 — Opening Scenario / "Promised World":** Replace the current "name your chapter, pick a seed, get dropped into a sandbox" launch (§4.1) with a framed, *in-media-res* opening that gives the new player a concrete first objective whose reward is the thing the rest of the game is built around: their Chapter World. The intent is to motivate the Supply & Requisition economy (§4.23) and the governor-request loop (§4.16) before the player understands either, and to use the time spent on the objective to let the rest of the sector advance so there is content waiting when the player looks up. *Detailed design (with locked decisions and an implementation sequence) lives in `Design/Reference/OpeningScenario.md`. Starting posture is decided: **the chapter begins in orbit and the player must land** (teaching the landing UI as the first action), rather than starting pre-landed.* **Status: implemented end-to-end (generation, governance, briefing UI, turn-loop resolution, persistence), covered by tests (`OnlyWar.Tests/Turns/ScenarioTurnTests.cs`), and the numeric balance pass has been validated in-engine. Per-item status follows.**
  - ✅ **Founding briefing pop-up.** After founding (chapter naming + seed confirmation, §4.1), present a briefing screen: the newly formed chapter has been assigned to a sector experiencing a Tyranid incursion; a world has been invaded; the Navy has destroyed the splinter fleet but no Astra Militarum forces are available to liberate it; if the chapter retakes the world it has been **promised to them as their Chapter World**. This is the player's clear initial goal. *(Implemented — `Scenes/MainGameScreen/briefing_dialog.tscn` + `BriefingDialogController`/`BriefingDialogView`, shown once from `MainGameScene._Ready` when `Scenario is { State: Pending, BriefingAcknowledged: false }`; the one-shot guard is persisted so it never re-shows on load.)*
  - ✅ **Generate the briefing through the narrative text system (§5 / §4.1 founding myth), not hardcoded prose**, so the planet name, sector name, and the *named authority who makes the promise* are stitched in. **Decided: the authority is the Sector Lord** — the governor of the sector capital, the ranking secular Imperial authority in the sector — rather than an invented free-standing commander or the Inquisition. Attributing the promise to a real, persistent character — rather than "the Imperium" — costs nothing now and leaves room for the promise to later be honored, contested, or reneged on; basing the *ongoing* relationship on the seat (not a stored character id) means the lord's death/succession transfers the obligation automatically. *(Implemented — `Helpers/Narrative/BriefingComposer.cs` fills one of three hand-authored templates (deterministic, selected from the promised planet's id) with chapter/planet/subsector/authority/enemy tokens; the authority and its derived title resolve from the Sector Lord via the governance layer. A deliberate placeholder for the eventual §4.19 narrator.)*
  - ✅ **Governance hierarchy (folded into this work).** Add a lightweight, *derived* layer that designates a **sector capital** and per-subsector **seats of government**, and exposes who governs them — the **Sector Lord** and the **subsector governors** — so the briefing authority is a real character, not an invention. The seat is the highest-importance Imperial-controlled world (the one that actually has a governor), kept distinct from the population-based *warp* capital so an enemy-held warp hub is never mistaken for a seat of government. The designation is recomputed deterministically during sector generation/load (like subsectors and warp lanes), not persisted; the governor characters are already persisted, so "who is the Sector Lord" round-trips for free. Independently useful beyond the scenario: capital markers on the Galaxy/Planet screens, a ranked set of request-issuers for §4.16/§6.5, and a cleaner "higher-importance world" signal for the governor death chance (§4.16). Full design in `Design/Reference/OpeningScenario.md`. *(Implemented — `SectorBuilder.AssignGovernance`, run at the end of `GenerateWarpNetwork` (new game and load), tags `Planet.GovernanceTier` and `Subsector.GovernanceSeat` and exposes `Sector.GetSectorCapital/GetSectorLord/GetSubsectorGovernor`; the designation is recomputed rather than persisted. The independently-useful capital markers on the Galaxy/Planet screens remain a later UI add.)*
  - ✅ **Scenario as an override layer on top of normal sector generation, not a fork of the generator.** The seed still deterministically generates the sector (§4.1); the scenario then *stamps* the starting world: Tyranid `RegionFaction` presence confined to a few regions, with the rest of the world default-Imperial. Determinism/reproducibility must be preserved (re-using a seed + scenario reproduces the same opening). *(Implemented — `Builders/ScenarioBuilder.StampPromisedWorld`, called from `GenerateSector` after generation + warp/governance; it selects the promised world order-deterministically, stamps a contiguous run of 2–3 Tyranid regions (which grow by biomass consumption per §4.24, not a growth throttle), places the fleet in orbit (no landed squads), and attaches a persisted `CampaignScenario`. `FoundTakebackPlanet` was deleted; persistence rides on the extended `GlobalData` row with `FieldCount` guards for legacy saves.)*
  - ✅ **Tune starting chapter strength and Tyranid growth together — this is the load-bearing balance decision.** A freshly founded chapter is plausibly understrength; lean into that (it explains why a Guard-less liberation fell to them specifically, and gives recruitment/buildup immediate purpose once they own the world). The swarm must stay winnable so a dawdling new player is not soft-locked, while still applying enough pressure (spread) to create urgency. **Superseded mechanism:** the original plan was to cap/slow Tyranid *logistic* growth below the default curve; the swarm is now a `Consumption` faction (§4.24) with no organic growth at all, so winnability is instead structural — the swarm is stranded on a **finite biomass budget** and cannot grow without fresh food. *(Implemented — the balance knobs are centralized in `Helpers/ScenarioRules.cs`: Tyranid strength is sized relative to the world's own PDF (`TyranidStrengthFraction = 0.5`, after a headless diagnostic showed absolute constants were ~3 orders of magnitude too small); how fast/far the swarm grows and spreads is governed by the §4.24 consumption rates plus the pre/post-landing delays. The old `GrowthMultiplier = 0.4` throttle was **removed** — consumption ignores it — once the Tyranid faction was flipped to `GrowthType.Consumption` (rules-DB migration `migrate-tyranid-consumption`), which activated the §4.24 model for real Tyranids. The target win window and the idle-player lapse behavior have been validated in-engine; the current knobs are the tuned values, subject to normal ongoing balance adjustment.)*
  - ✅ **Let the sector simulate forward during liberation** so governor/Inquisition requests (§4.16, §6.5) have already populated by the time the objective completes — no artificial early-game content lull. *(Implemented — the scenario gates nothing; the normal turn loop (`FactionStrategyController`, governor logic, growth) runs every turn while the objective is pending, and `ProcessScenario` resolves win/lapse from the live board state. Pacing comes from the balance numbers above, not artificial gates.)*
  - ✅ **Failure/abandonment handling — decided: promise lapses + reputation hit.** If the player ignores the objective, loses the assault, or the world is overrun (the Tyranids hold every region, no Imperial/player presence remaining), the promise is withdrawn — no Chapter World granted, and the Sector Lord's opinion of the chapter drops. The game continues as a normal sandbox rather than leaving a dangling goal. The finite biomass budget (§4.24) is tuned so a fully-overrun world is the consequence of genuine neglect, not a soft-lock. (Considered and deferred: redirecting the chapter to a replacement promised world on loss.) *(Implemented — `TurnController.ProcessScenario` resolves **Win** (no *enemy* presence remains — the Tyranid swarm **and** the revealed Genestealer Cult both cleared, i.e. the world fully back in Imperial/player hands, not merely Tyranid-free → grant the player the planet-wide `PlanetFaction`, raise the current Sector Lord's opinion) and **Lapse** (fully overrun → withdraw the promise, lower the Sector Lord's opinion; a vacant seat is a no-op that still resolves), with a notification surfaced via `ScenarioNotification`; the game then continues as a sandbox. Opinion swings (`±0.5`) share the balance-pass caveat above.)*

- ✅ **Melee Combat Rework — Attack Speed, Multiple Attacks & Weapon Rebalance:** Make melee lethality deliver what the species and weapon data already promise — AttackSpeed-driven multiple attacks, weapon speed multipliers, dual wielding, parry, a doubled melee damage scale, and a `SoldierTemplate.BattleValue` recompute. Explicit balance stance: tabletop is a guide, but base marines are deliberately "two-wound" soldiers. Full behavioral spec in §4.14 (*Melee Combat — Attack Speed, Multiple Attacks & Weapon Rebalance*). Distinct from Battle Logic Phase 4 (§5.4), which covers morale, on-fire, and the withdrawal/rout mechanic. *(Implemented — attacks per melee action are `AttackSpeed/10 × weapon AttackSpeedMultiplier` with the fraction resolved probabilistically (`MeleeMath`); the `ExtraAttacks` column was replaced by `AttackSpeedMultiplier` (all values currently 1.0 — per-weapon speed differentiation remains a data/balance lever); dual wielding (two one-handed melee weapons) grants one off-hand strike with the off-hand weapon's profile, with defensive value carried entirely by weapon `ParryModifier`s (an initial flat +1 dual-wield defense bonus was removed — it gave every dual-natural-weapon Tyranid a free defensive stack on top of the evasion that already models their invulnerable-save analog); the planner distributes strikes across adjacent enemies, committing to a target until cumulative take-out confidence reaches 75% before moving on (`BattleSquadPlanner.BuildStrikePlan`); `ParryModifier` now counts on the defender's side of the contested roll (summed across equipped weapons; the unarmed fist is −1); `StrengthMultiplier` values were doubled with heavy-tier `WoundMultiplier`s dialed down in compensation; dead columns `ExtraDamage` and `IsPenetrating` were dropped; a follow-up calibration pass re-anchored the contested roll to tabletop's intuition band — `MeleeDefenderAdvantage` 3→0 (equal skill trades at ~50%, tabletop's "hit on 4s") and the per-side roll σ 3→6 (each skill point worth ~5.6% near parity, compressing large gaps toward tabletop's clamped 33–67% ladder so a Genestealer is mega-scary but killable — ~72% out / ~28% back vs a marine; see `Design/Active/EvasionBurrowAndAmbush.md`); every Space Marine template gained 1 point of Fist MOS training so an unarmed marine fights trained rather than helpless (a default "close combat weapon" — knife/bayonet — is a possible future refinement); Ork Warboss data fixed (AttackSpeed attribute was mislinked to his Size template — now 30; marine "Bolter + Bolt Pistol" set replaced with a new "Slugga + Big Choppa" set; Generic Melee/Ranged training added). `BattleValueCalculator` was rewritten as an engine-faithful valuation — expected kills/turn and survival-turns against a four-profile reference threat panel (swarm chaff / light infantry / elite infantry / monster), BV = 5·√(offense × durability)·command, replaying the engine's real to-hit/damage math including recoil decay, aim-vs-fire arbitrage, single-target overkill caps, ammo duty cycle, and melee closing/engagement limits — and all `SoldierTemplate.BattleValue` rows were regenerated with it (PDF Trooper 5 anchor; Tactical Marine 10, Genestealer 14, Hive Tyrant 95), with the `StrategicCombatRules` BV anchor constants updated to match. Player-soldier BV intentionally remains the template guideline rather than a live skill-tracking value: enemy forces size their responses by estimating the player force, not by concrete data on every marine. Values are subject to normal ongoing balance adjustment.)*

- ✅ **Mission Field Experience & Records:** Before this work, non-battle missions granted no soldier development and left no trace on soldier history — a successful recon that infiltrated a hive undetected taught its scouts nothing and was never recorded. The implemented learn-by-doing field-experience model (§4.12) grants per-check skill growth on the skill each mission step tested, margin-scaled so close shaves and failures teach most, tuned above garrison training, and uncapped in this pass (the geometric skill-cost curve and margin scaling are the governors). It is paired with per-soldier mission history events and end-of-turn mission-result reporting (§4.13). Event emission reuses the structured event-log substrate already landed as §5.5 step (1), pulling the non-combat mission-outcome events forward from the 0.8 event work so field service is visible now. Attributes stay static in this pass (open question §6.13). *(Implemented — field XP is awarded inside `MissionCheck.RunMissionCheck` (a Gaussian-bump margin curve in `Helpers/Missions/MissionExperienceCalculator.cs`) to every able PlayerSoldier on each check; `Helpers/Missions/MissionOutcomeRecorder.cs` writes a per-soldier `MissionOutcome` `SoldierEvent` at mission end (hooked from `TurnController.ProcessCombatMissions`/`ProcessDiversionMissions`, player orders only); `Helpers/MissionReportSummaryBuilder.cs` produces the outcome-classified, acting-faction-attributed end-of-turn summary. Mission steps now set structured, monotonic outcome signals on `MissionContext` (`ForceBrokeContact`, `ForceLostContact`, `ForceWithdrewUnderFire`, `ObjectiveAborted`, `NoViableTarget`, and assassination-target facts); `MissionOutcomeClassifier` derives a single classification from those signals, shared by the career-log recorder, player Turn Report, and redacted NPC reporting. Log-string matching is no longer used for outcome classification. Covered by unit tests.)*

- ✅ **Template Weapons — Flamers:** Convert flamer-type weapons from stat-line guns into true cone-template weapons: auto-hit against everything in a cone (both sides), evasion/size modifiers bypassed, fuel-per-burst ammo, firing-line target selection, template-aware Battle Value. Full behavioral spec in §4.14 (*Template Weapons — Flamers*). Pulled forward from Battle Logic Phase 4 (§5.4) so the flamer is not rebuilt twice; on-fire/morale stay in Phase 4, and grenades shipped separately in §5.3. *(Implemented — `RangedWeaponTemplate` and the rules DB now carry `TemplateType`, `AreaRadius`, and `FuelPerBurst`; both Flamer rows project a deterministic full-range cone widening from a 0.5-cell nozzle to a 3-cell half-width, auto-hit every friendly or enemy footprint caught while excluding the shooter, resolve normal hit-location/armor/wound math per victim without stray-shot rules, and spend 10 of their 50 fuel per burst. `BattleSquadPlanner` bypasses aim and ordinary single-target scoring for templates, evaluates complete firing lines by expected enemy BV removed minus friendly BV lost, prefers dense positive-value lines, and closes instead of firing unsafe ones, including the engaged point-blank path. `BattleValueCalculator` and the offline harness value template auto-hits with density-scaled victims and a fuel/reload duty cycle; the flamer-option Tactical and Assault Marine rows remain BV 9 after regeneration. Area wounds are queued by the live resolver, reported as fire-phase volleys in replay summaries, and friendly-fire casualties no longer receive enemy-kill credit. Covered by focused cone, action, planner, database, BV, and aftermath tests.)*

### 5.3 Alpha 0.7.1 — Done

Alpha 0.7.1 is deliberately limited to protecting and operating the released campaign. It does not add a new faction or major simulation system.

- ✅ **System menu and release-support baseline** — the specialized Save button has been removed in favor of a global System menu with Resume/Save/Load/Return to Title/Quit, dirty-state prompts, global Escape toggle, X-to-close gameplay dialogs, global End Turn warning preferences, and diagnostic export. Title Load is now an explicit chooser; title Options/Quit use the same release-support surfaces (§4.27). *(Implemented.)*
- ✅ **Save confidence** — unlimited named manual slots; visible manual/autosave chooser; initial, protected pre-turn, and three rolling post-turn recovery points; atomic save/metadata replacement; explicit corrupt/incompatible states; and recoverability-aware destructive navigation (§4.18). The save format remains version 1 and speculative migration support is deferred until the first intentional format change. *(Implemented.)*
- ✅ **Conditional End Turn preflight** — warns only for combat-capable idle squads that can deploy now, actionable in-orbit task forces without destinations, and unassigned special missions at their real independent 25% per-turn disappearance risk; allows immediate override and has global per-category preferences. Routine turns advance without confirmation; governor-request expiry is outside this body of work (§4.27). *(Implemented.)*
- ✅ **Scene-wiring release tests** — the Godot 4.7 headless smoke instantiates the campaign/title release controls and exercises the real System Options, Save, Load, Resume, Diagnostics, End Turn, title Load, and title Options buttons plus Escape/X input behavior. Visible-but-inert controls or broken scene paths fail the smoke. *(Implemented — `Scenes/Debug/release_scene_wiring_smoke.tscn`.)*
- ✅ **Living Universe Phase 3B — Revolt:** Revolutionary population mechanic; evidence-based requests. Full behavioral spec in §4.20 — per-region Contentment driving a sector-wide Insurrectionist faction (reusing the converting-faction/Cult machinery), governor Severity-driven response, garrison defection, intra- and inter-planet spread, evidence-gated requests, and "for a revolt" limited to inaction-with-consequences in 0.7. Built on the faction-presence model as a forward-compatible subset of the Pop-model question (§6.7); Chaos radicalization specified but gated on Chaos content.
- ✅ **Supply & Requisition Phase 2 — pledges & delivery:** Layer the pledge system onto the Phase 1 currency (§4.23). Fulfilling a request generates a tracked **pledge** — a *standing tithe* (recurring) or *one-off* — that delivers **Requisition over subsequent turns** along a **supply line bound to its source world**: a world that revolts (§4.20) or falls suspends or defaults its pledges, wiring supply into the Living Galaxy. Broaden sinks beyond the Apothecary to other 0.7 systems (e.g. fortification materiel §4.13, recruitment costs). Pledges deliver Requisition only in this phase; **typed materiel** (wargear / vehicles / ships driven by strategic classification §4.1), the **Armory wargear inventory** (§6.9), **pledge interdiction** (§6.10), and **Inquisition negative-requisition** (§6.5) remain post-0.7, each gated on a system not present in 0.7.
- ✅ **Grenades / blast templates:** Frag grenades and the Grenade Launcher as blast-template weapons — margin-driven scatter, Strength-scaled throw range, quadratic damage falloff, thrower-included danger-close, third weapon-set ranged slot. Full behavioral spec in §4.14 (*Template Weapons — Grenades*); execution plan in `Design/grenades.md`. *(Implemented — `WeaponSet` gained a nullable `GrenadeWeaponId` third ranged slot; two Frag Grenade rows (marine/Throwing and generic/Generic Ranged, TemplateType 3) ship on all Space Marine, PDF, and human-tier Genestealer Cult sets, and the GSC Grenade Launcher converted to launched-blast TemplateType 2. `BlastTemplate` resolves the aim cell and margin-driven scatter (1 cell per point of failure, half-normal by construction) and returns per-victim distances; `BlastAttackAction` pre-resolves once for replay determinism, auto-hits everyone in the circle including the thrower, scales the damage roll by `(1 − d/AreaRadius)²` before armor, and spends its single shot into the 1-turn belt reload. The planner scores nominal-impact blast circles (enemy expected BV removed minus friendly expected BV lost, thrower included) and throws only when that beats the soldier's best conventional action — ties go to the gun; melee-engaged soldiers never throw; empty grenades restock via the normal reload branches on idle turns. Blast volleys queue wounds through the turn resolver and surface as fire-phase volley events in replay summaries. `BattleValueCalculator` (and the offline `Compute-BattleValue.ps1` harness, verified to 6-decimal parity) value blasts via density-scaled auto-hit victims, an average-falloff tunable, reload duty cycle, Strength-scaled reach, and a sidearm max(primary, grenade) rule; regeneration changed no shipped BV row and left the `StrategicCombatRules` anchors untouched. Covered by blast-template, action, planner, reporting, data-access, and BV tests.)*

### 5.4 Alpha 0.7.1 — To-Do

- **Battle Logic Phase 4:** Morale, on-fire damage-over-time/panic (§4.14 gated follow-on), explicit movement tiers and the sprint/fire tradeoff (**Phase 4A**, specified in §4.14), the full covered-withdrawal/rout/pursuit mechanic (including immediate disengagement for burrowing- and flight-capable squads), and post-battle loadout recalculation. *(Flamers/cone templates were pulled forward to §5.2; grenades shipped as their own item in §5.3. Phase 4A deliberately defers leg-wound/true-stance integration and Battle Value recalibration.)*
- **Leg Wound & Prone-Combat Realism (maybe):** Rework the leg-hit outcome so a single solid hit staggers rather than reliably felling. Motivated by combat GSW data: even for 40k-tier energies (nothing softer than .45/7.62), only ~45–55% of solid leg hits fracture bone/joint or major vessel and actually prevent movement; the rest slow but don't stop. Scope:
  - **Raise the "can no longer walk" bar one level to Massive.** Motive (leg/foot) locations currently drop a soldier at their *cripple* threshold (Critical, i.e. damage ≥ Constitution); require the higher *Massive* band instead so a Critical leg wound impairs but does not fell. Torso/vital lethality is unchanged; this narrows the current asymmetry where a leg is fight-ending at half the damage the torso needs to be decisive.
  - **Downed-but-armed enemies keep firing prone.** A soldier felled by a motive-location wound who still holds a ranged weapon may continue firing from prone, after a short delay (a few rounds) to represent going down and re-orienting rather than instantly returning accurate fire.
  - **Depends on stance modifiers being live.** Requires the §4.14 Stance rules (hit-location filtering by stance; prone as a valid firing position) to be actually implemented and exercised, not just specified.
  - **Depends on prone melee effects.** Requires implementing prone's melee impact per §4.14 (prone soldiers take the doubled crouch offense penalty and are significantly easier to hit in melee), so a downed shooter is correspondingly vulnerable to being finished in close combat.
- **Battle Visuals Phase 3:** Terrain and cover representation; line of sight; elevation-based fire advantage. Also the deferred battle-replay *motion* work carried over from the 0.7 visual overhaul (§5.2): smooth position interpolation/tweening of formation markers between round end-states, in-flight projectile and charge-path animation, and timed reveal of casualty/rout overlays at the moment they occur, replacing the current discrete round-by-round redraw.
- **UX Improvement Phase 1:** Drag-and-drop where applicable; squad row redesign; zoom-adaptive planet name labels.
- **Mission System Expansion:** Talent recruitment missions; IG support missions; Chaos cult investigation; STC hunt; prisoner recovery.

### 5.5 Alpha 0.8 — Command, Narrative & Continuity

The connective pass that turns 0.7's broad simulation into a legible, felt, sustainable sandbox campaign. The first half makes existing state understandable and memorable; the second closes the Chapter recovery loop and supplies a medium-term horizon. See §§4.19 and 4.25–4.27 for the governing specifications.

**Implementation prerequisite — structured soldier event log.** Soldier history was an unstructured `List<string>` of free-text lines, written from only a handful of sites (founding, promotion/transfer, ratings/awards, a per-battle summary, and a thin death line); non-combat missions (recon, sabotage, assassination, infiltration, fortification) recorded nothing. Before any narration work, this is being replaced with a **structured, queryable event log** — typed events carrying date, location, faction, weapon, magnitude, and related-soldier references — that serves as both the substrate the notability classifier queries and the source the narrator renders to text. Audit findings driving this:

- Continuity callbacks (4.19 Principle 3) and the notability classifier require *querying* the past ("first kill?", "who was his mentor?", "crossed 50 kills?", "survived the battle that killed his sergeant?"), which free-text strings cannot reliably support.
- Events that are never emitted today and must be added: first blood, kill milestones, last-survivor / survival-against-odds, mentor/instructor relationships, oaths, near-death recoveries, and **all non-combat mission outcomes**.
- The fallen brother's dossier must be *preserved* on death (see 4.12) rather than discarded.

0.8 sequencing — status reflects the current codebase (✅ done · ⬜ not started):

- ✅ **(1) Structured event log substrate + migration + death preservation.** *(Implemented.)* Added a typed `SoldierEvent` / `SoldierEventType` model (`Models/Soldiers/SoldierEvent.cs`) carrying date, faction, weapon, magnitude, location, and related-soldier ids, with `Render()` reproducing the legacy display lines so the history surface is unchanged. `PlayerSoldier` now holds a `List<SoldierEvent>` (with a `SoldierHistory` string projection for existing readers); all existing write sites (`NewChapterBuilder`, `RatingCalculator`, `SoldierTransferService`, `BattleTurnResolver`) emit typed events. Persistence moved to a structured `PlayerSoldierEvent` table (old free-text `PlayerSoldierHistory` table dropped; save compatibility intentionally broken). Fallen brothers are preserved in `Army.FallenBrothers` and round-trip through save/load (see 4.12). Covered by unit tests for `Render()` fidelity and save/load round-trips for events and fallen brothers.
- ⬜ **(2) Emit the missing events** — first blood, kill milestones, last-survivor / survival-against-odds, mentor relationships, oaths, and near-death recoveries (reserved enum values already exist for these). *Non-combat mission-outcome events are pulled forward to 0.7 — see §5.2 "Mission Field Experience & Records" — so field service is recorded ahead of the rest of this event pass.*
- ⬜ **(3) Notability classifier** over the log.
- ⬜ **(4) Narrator / voice pass** rendering events and report lines.
- ⬜ **(5) Command Brief & Chapter Chronicle** — commit the persistent two-lens command/narrative surface in §4.19, including deep links and the first-turn checklist.
- ⬜ **(6) Planet tactical-map legibility redesign** — approve a visual baseline and replace the current tiny-glyph information hierarchy, including honest multi-faction presentation (§4.26).
- ⬜ **(7) Recruitment v1** — connect recruitment rights, governor relationships, Requisition, gene-seed, training capacity, and the aspirant/neophyte-to-Scout pipeline (§4.9, §6.3).
- ⬜ **(8) Chapter Mandates** — add state-aware, non-terminal medium-term objectives after the Promised World (§4.25).
- ⬜ **(9) Display mode and UI/text scaling** — add fullscreen/windowed presentation settings and visually verify scaling across all supported screens (§4.27).

- **Narrative Voice baseline:** Apply the 4.19 authoring principles and notability classifier across the Turn Report, Soldier history log, and death/apothecary records — named individuals, specificity, continuity callbacks, and outcomes framed against the player's orders.
- **Eulogy-style death records:** Where, how, final tally, years served, and geneseed recovered or lost (with lost geneseed narrated as a compounding loss).
- **Enriched soldier history vocabulary:** First blood, survival against odds, mentor relationships, kill milestones, oaths, near-death recoveries.
- **Voiced requests:** Governor and Inquisition request text driven by personality and authority.
- **Founding myth:** Generate a short chapter history at new-game start.
- **Battle Review log humanization:** Named actors and flavor on criticals, kills, and last stands.
- **Wider-Imperium dispatches (initial):** Voiced notifications for major uncontrolled-Imperium actions in the sector (Battlefleet priorities, worlds the Imperium addresses without the chapter), establishing the relevance/legacy stakes framing.
- **Command Brief & Chapter Chronicle:** Implement the committed persistent operations/saga surface defined in §4.19. The Chronicle records what mattered; the Brief shows what now requires command attention.

**Engineering follow-through supporting 0.8:** Finish the simulation seams documented in TDD §8: split the remaining large `PlanetTurnProcessor` by domain, replace the production-wide random sequence with deterministic named streams that can be reproduced from saved campaign/turn inputs, and retire transitional `TurnController` compatibility shims and unused prototypes as callers migrate. These are enabling changes, not separate player-facing features.

### 5.6 Alpha 0.8+ — Cross-Faction Simulation (Relationships, Intel, Orks)

A simulation expansion that lifts the sector beyond a binary Imperial-vs-everyone model. Sequenced **substrate first**, because the Ork feature depends on it and because the substrate independently benefits Revolt (§4.20) and future Chaos content. Full behavioral specs in §4.21 and §4.22.

- **Substrate (prerequisite): Faction Relationships & Inter-Faction Intelligence** — replace the binary `AreFactionsEnemies` test with a per-faction-pair Stance store (default Hostile; player↔Imperial seeded Allied); consolidate `Faction`'s ad-hoc booleans into a `[Flags] FactionBehavior` field (folding in `CanInfiltrate`, adding `UniversallyHostile` and `Indelible`); and build the per-faction graded **intelligence-as-belief** model (v1 sparse numeric `PlanetFaction.RegionIntel`, later target-specific `IntelLevel` ladder, false positives via paranoia/disinformation), generalizing the existing governor-detection and OpFor fog-of-war as the default-Imperial special case. Spec: §4.21.
- **Orks & Indelible Infestation** (depends on the substrate) — `UniversallyHostile | Indelible` Ork faction; indelible `RegionFaction` with pop-0 → non-public → regrow-to-1; logistic growth with an Ork multiplier and a feral efficiency penalty; two-dimensional state (awareness × expansion) yielding unnoticed-feral / noticed-feral / WAAAGH!; feral amassing migration and internal-scale WAAAGH! emergence; imperfect Imperial cull of noticed-feral Orks (gated on `Confirmed` intel and spare capacity); and WAAAGH!-as-beacon spawning unmapped Ork worlds in empty tiles plus reinforcing fleets. Spec: §4.22. Open question: terminal Ork-controlled world state (§6.8).
- **Tyranid Invasion & Biomass Consumption** (depends on the substrate) — `UniversallyHostile` Tyranid faction on a new `GrowthType.Consumption` (no birthrate; grows only by eating); Predate (proportional headcount kills) vs. Consume (degrade `CarryingCapacity` toward a new `MaximumCarryingCapacity`, slow recovery); depletion-driven troop allocation (fight → expand → predate+consume); doomed Genestealer Cult uprising (reveal-on-inbound, seeded insider belief, relocate to active-PDF neighbors, sacrificial predation with no growth); region-level Imperial hide/unhide with civilian emigration; the PDF made a defensive strategic actor (fortify/hold, weaker than the Guard §6.4); a strategic attrition combat model distinct from the tactical Battle engine; and the opening-scenario sequencing (cult reveal → seed insider belief → pre-landing sim → authored beachhead → Navy strands the swarm → Gaussian post-landing sim → player arrival). Spec: §4.24. Open questions: breeding structures (§6.11), region-level going-public generalization (§6.12).
- **Large-Scale NPC Combat** — NPC-only regional assaults above tactical scale resolve in
  battle-value space against `RegionFaction.MilitaryStrength`, applying weekly attrition,
  defender preparation, attacker aggression, and conquest/withdrawal outcomes without
  generating transient tactical armies. This is the concrete strategic attrition model called
  for by the Tyranid/PDF opening-scenario work; named player forces remain tactical. Spec:
  `Design/Reference/LargeScaleNpcCombat.md`.

### 5.7 Post-0.8 Backlog

Documented for planning purposes; not scheduled:

**Content:** Dreadnoughts, Chaplains, Psykers, Chaos Troops, Necrons, Tau, Vehicles, Flying Units, Drop Pods, Fortifications, Relics, Poison Weapons, Geneseed Mutation, Power Armor Variants, The Inquisition. *(Orks are now scheduled — see §5.6. When Vehicles arrive, add krak grenades alongside them — thrown single-target anti-armor attacks, not blast templates; deferred from the §4.14 grenade work because they matter little without armored targets.)*

**Enemy-generated diversions.** Give `FactionStrategyController` the ability to run its own diversion feints, rather than only being the target of the player's. Deferred from 0.7: it adds little to the 0.7 experience, and player/NPC order-structure symmetry — while desirable — is not blocking. Two distinct problems hide here, and they should be scoped separately:

- *NPC-vs-NPC feints* fit the existing turn loop with no changes: both the feinter and the fooled defender resolve within the same turn (shaping phase → faction planning), so this is purely a generation-heuristic addition to `FactionStrategyController`.
- *NPC-vs-player feints do not fit the current flow.* The diversion mechanic only fools a decision-maker who plans *after* the shaping phase; the player commits orders *before* `ProcessTurn` runs, and nothing consumes `PerceivedThreatBonus` on a player region (the player allocates garrisons by hand). A feint against the player therefore cannot reuse the AI's same-turn planning bonus — it must become a **one-turn-lagged intelligence deception**: the feint inflates the *displayed* enemy-strength estimate in the player's intel layer, persists past `ClearDiversionEffects` (unlike the transient AI bonus), and is acted on by the player the following turn, with the real attack landing then. This also implies an AI planning horizon that pairs a feint with a follow-up assault across turns — beyond the current per-region greedy `GenerateFactionOrders`. Resolve these (effect channel, persistence, what the player sees and when the deception resolves, AI feint+follow-through planning) before implementation.

**Strategic:** Supply economy remainder (§4.23 — the typed **materiel pledges** driven by strategic classification §4.1, the **Armory wargear inventory** §6.9, **pledge interdiction** §6.10, and **Inquisition negative-requisition** §6.5; the Requisition currency and the Requisition-delivering pledge/supply-line layer ship earlier as Phases 1–2 in 0.7 and 0.7.1, §5.2/§5.3), Diplomacy system, Space Combat and Boarding, Strategic Planetary Maps (regional types), Factional Fleets with independent movement, Sector Generation Customization (difficulty, era, story threads), Chapter Customization at start (founding legion, perks/disadvantages).

**Living Universe:** Imperial Guard movement and inter-planet logistics (Phase 4); Character personality development over time (Phase 5).

**Soldier Screen — awards as icons.** Replace the textual awards list on the Soldier Screen (§4.7) with an icon strip in the screen's top panel: one icon per award the marine holds, with the date the award was earned shown as hover text. The existing display rule still applies — for a multi-tier award type, only the most recent / highest tier is shown (one icon, not one per tier). Small UI/presentation item; depends on an icon set for the award types.

**Infrastructure:** Full mod support (XML data, moddable factions, name lists, planet data, chapter generation); data-layer decoupling to replace hardcoded rules-data display-name references with stable keys, validated registries, and data-driven rule profiles; graphics work beyond the scheduled planet tactical-map redesign; and UX work beyond the 0.7.1 system/accessibility baseline and 0.8 Command Brief.

**Parallel turn processing.** Investigate bounded multithreading for the planet-scoped portions of campaign turn resolution once profiling shows that turn latency warrants the added complexity. Planets are the preferred concurrency boundary: resolve each planet's local simulation on a worker with deterministic per-planet/per-phase randomness and planet-local output buffers, then merge generated requests, characters, missions, intelligence gains, logs, requisition changes, and identifiers in stable planet-ID order. Region-level parallelism should be limited to explicitly local subphases; expansion, migration, cult relocation, revolts, and other neighbor- or planet-wide logic require phase barriers or snapshot-and-apply processing. Before enabling parallel execution, remove or isolate the current shared RNG streams, static ID allocators, process-wide scenario metrics, and mutable turn-result collections. A separate, lower-risk interim improvement is to move otherwise single-threaded turn resolution off the Godot main thread so the progress UI remains responsive.

**Rules Data / Modding:** Move tunable simulation rules out of code where practical. Priority candidates include soldier work-experience training profiles, scout training focus profiles, mission skill requirements, sector-generation faction selection, chapter organization roles, and soldier rating formulas. The goal is that renaming a displayed skill, faction, template, or unit does not break game logic.

---

## 6. Open Design Questions

The following require design decisions before their associated features can be implemented. Resolving these is the expected focus after the initial PRD and TDD drafts are locked.

### 6.1 Aggression Axis Split — DEFERRED

The single `Aggression` axis (Avoid → Cautious → Normal → Attritional → Aggressive) currently conflates two behaviors: willingness to seek contact and willingness to absorb casualties before withdrawing. Splitting into two axes would allow behaviors like "seek contact but withdraw readily on losses" or "avoid contact but hold ground if engaged."

This split is deferred indefinitely. Aggression is a per-order, per-mission setting with no mid-mission adjustment. The AI generates orders with hardcoded aggression values per order type (Normal for assaults, Cautious for patrols, Avoid for construction). No concrete playtesting scenario has been identified where the single axis produces wrong behavior. If such a scenario emerges, the question should be reopened at that time.

### 6.2 Morale

**Question:** What triggers a morale check and what are its outcomes?

**Agreed triggers to consider:**
- Casualty threshold: losing a significant percentage of the squad's strength in a single turn.
- Death of the squad leader.
- Being significantly outnumbered.

**Outcomes to define:** reduced combat effectiveness, forced retreat (covered withdrawal), rout (immediate disorderly exit from the map), or broken (squad cannot be assigned orders for a number of turns after the battle ends). The intended mechanics of covered withdrawal and rout are specified—but not yet implemented—under Battle Continuation in §4.14; what remains open here is what *triggers* a morale check and which of these outcomes results.

### 6.3 New Recruit Intake — V1 COMMITTED FOR 0.8

Recruitment v1 is committed in §4.9 and closes the current one-way attrition loop. The player gains recruitment rights through relationships or chapter ownership, spends Requisition and gene-seed, and moves candidates through a capacity-limited aspirant/neophyte pipeline into Scout squads.

The open design space is how many additional recruitment methods should eventually coexist and how sharply their trade-offs should differ:

- One method might yield more recruits but strain the source planet's population or reduce planetary morale.
- Achieving a high reputation with a planetary governor could unlock that governor offering recruitment rights on their world, as a relationship reward.
- Some Scout squads or Sergeants may be assigned to recruitment duties rather than training or deployment, representing the chapter's active effort to identify and select candidates.
- A dedicated scouting mission purely to find recruits is not planned; recruitment is managed through standing assignments and governor relationships rather than one-off missions.

V1 needs one complete, legible path and at least one meaningful source/method trade-off. A broader menu of recruitment cultures, facilities, population consequences, and chapter-specific methods remains later expansion rather than a blocker for 0.8.

### 6.4 Imperial Guard Interactions

**Decided design:**

- IG units are visible on all relevant map levels (galaxy view, planet view, region view) as distinct military forces separate from PDF.
- The player can request support from IG or PDF commanders. The outcome of the request depends on the requesting character's opinion of the chapter and whether they assess their forces as having more pressing duties elsewhere.
- Named individual characters outside the chapter are limited to governors, admirals, and generals. There are no chapter-monitored individuals at a lower level of granularity than these command-level figures.
- IG armies and fleets operate independently. They move between planets, conduct operations, reinforce or destabilize worlds, and respond to threats without requiring player involvement. The sector continues to function without the player's hand in everything.

**Remaining open sub-questions:**
- What is the request UI flow for IG/PDF support? (Is it a dialog from the planet or region screen, or something triggered through a governor character interaction?)
- What specific support types can be requested? (Reinforcing a region, providing fire support to a player-led assault, holding a position while the chapter deploys elsewhere.)

### 6.5 Inquisition Role

**Decided design:**

- The Inquisition is the primary investigative force for xenos and chaos influence across the sector. They operate independently, conducting their own investigations and responding to threats.
- Inquisitors are a source of requests directed at the player's chapter, similar in structure to governor requests but with a different authority and tone. Inquisitor requests may include: investigate a region for xenos influence, purge a world, support an ongoing investigation with military force.
- The Inquisition may also investigate the chapter itself for signs of chaos taint or gene-seed corruption. This creates a threat relationship alongside the cooperative one.

**Remaining open sub-questions:**
- What are the consequences of refusing or failing Inquisition requests, versus governor requests? The Inquisition carries considerably more authority.
- What are the consequences of a negative investigation result against the chapter? (Censure, requisition of assets, excommunication as an extreme outcome?) The *mechanic* for requisition of assets — the Inquisition drawing down the chapter's Requisition or seizing materiel — is specified under the supply economy (§4.23, "Negative Requisition"); what remains open here is its triggers and severity.
- Does the player interact with a specific named Inquisitor character, or is the Inquisition an anonymous institutional force?

### 6.6 Navis Nobilite Relations (Post-0.7 Backlog)

Space Marine chapters obtain Navigators through formal pacts with the Navis Nobilite, the ancient mutant Navigator houses based on Terra. These pacts are centuries-long relationships, with specific Navigator families assigned to specific chapters across generations.

The chapter's relationship with its assigned Navigator house is a candidate for a tracked reputation value, analogous to governor opinion. Better relations would result in a more skilled Navigator being assigned, translating to reduced travel time variance on lane routes and potentially shorter base durations. Poor relations could result in a less capable Navigator, increasing variance and extending journeys.

This is a post-0.7 design item. Navigator quality should be designed as a chapter-level modifier to the transit time formula rather than as an individually tracked NPC, unless the decision is made to model Navigators as named characters in a future pass.

### 6.7 Pop-Based Population Model (raised by Revolt, §4.20)

**Question:** Should Imperial (and ultimately all) populations be refactored from the current single-number-per-`RegionFaction` representation into a Victoria-3-style **Pop** model — a population subdivided into groups carrying their own loyalty/affiliation (loyal, discontented, revolutionary-sympathizer, chaos-curious, etc.) that drift between states before anyone defects into an armed faction?

**Why it comes up.** A `RegionFaction` is already a population bucketed by faction loyalty, and Genestealer Cult conversion is already "pops shifting loyalty from Imperial to cult" — but with no intermediate latent state (conversion is effectively binary). A Pop model adds that latent distribution and would unify Genestealer Cults, secular revolt (§4.20), Chaos corruption, and PDF/IG disloyalty under a single substrate, replacing what would otherwise be a combinatorial pile of pairwise faction-conversion rules. It is strongly aligned with the Living Galaxy pillar.

**Decision (for now):** **Do not build Pops for 0.7.** The 0.7 Revolt ships on the faction-presence model, with Contentment as a proxy for "loyal share" and the hidden Insurrectionist `RegionFaction` as "pops who have crossed from sympathy into active rebellion." These are deliberately a forward-compatible *subset* of a Pop model: a later refactor would make Contentment a derived readout over the loyalty distribution and reframe the insurgent presence as a loyalty band, without discarding 0.7 work. Whether to commit to that refactor — a multi-phase change touching growth, carrying capacity, recruitment for every faction, save/load, and UI — is the open question, and it outlives the Revolt feature.

### 6.8 Ork Terminal Control State (raised by Orks, §4.22)

**Question:** Can Orks *win* a world outright — extinguish its inhabited population and hold it as an Ork-controlled world that the player and the wider Imperium treat as a standing objective — or are Orks always a recurring infestation to be managed rather than a faction that can take and keep a world?

**Why it comes up.** The indelible-presence rules (§4.22) deliberately make an Ork world un-cleansable, which frames Orks as a *management* problem. But that is about the floor (you can never reach zero Orks), not the ceiling (whether Orks can become the planet's controller). A WAAAGH! that overruns every region implies a terminal Ork-controlled state with its own downstream questions: does such a world still generate reinforcing fleets, can it ever be retaken, and how does it interact with the discovery and importance-scoring systems.

**Leaning (not yet decided):** allow Orks to seize regional control through the normal combat path (consistent with §4.20's renegade-victory failure state for revolts), but treat a fully Ork-held world as a *persistent beacon* rather than a clean win condition — re-takeable in principle, never cleansable. Decide before implementing the WAAAGH!-beacon spawning in §4.22.

### 6.9 Armory / Wargear Inventory Commitment (raised by Supply, §4.23)

**Question:** Should wargear replacement be modeled as a real **Armory inventory** — a finite, per-pattern pool of weapons and armor that is depleted as brothers fall and is replenished by wargear pledges — or remain permanently abstracted against the Requisition currency?

**Why it comes up.** §4.23 makes "replace losses" a core sink, but "losses" can mean *bodies* (recruitment, already constrained by geneseed) or also *gear*. A real inventory makes a civilized world's wargear tithe tangible and creates meaningful scarcity in re-equipping a depleted company, but it is a substantial new model touching squad loadouts (§4.5), death/maiming resolution (the point at which gear is lost or recovered), save/load, and the Armory UI. The system is designed to ship abstract-first (Requisition only) so this commitment can be deferred without rework.

**Leaning (not yet decided):** ship abstract-first; adopt the inventory model only if playtesting shows wargear scarcity adds a decision the body/geneseed constraint does not already provide.

### 6.10 Pledge Interdiction in Transit (raised by Supply, §4.23)

**Question:** Once factional fleets move independently (§5.7), can a materiel delivery in transit from a pledging world to the chapter be **interdicted** — delayed, reduced, or lost — by a hostile fleet along the route?

**Why it comes up.** §4.23 already binds a pledge to the *fate of its source world* (a world that falls stops paying). Interdiction extends that to the *route*, making supply lines themselves contestable terrain and giving raiders/blockades a strategic purpose. It is gated on factional fleet movement existing and on whether deliveries are modeled as discrete moving objects (like fleet task forces) rather than abstract scheduled credits. Until then, deliveries are treated as guaranteed-on-schedule once pledged.

### 6.11 Tyranid Breeding Structures as Strikable Objectives (raised by Tyranids, §4.24)

**Question:** Should a Tyranid region's feeding pools / gestating brood be promoted from the current abstraction (the *unorganized* remainder of population, carried by `Organization` — §4.24) into discrete, immobile, strikable structures the player can target directly?

**Why it comes up.** Discrete breeding structures would give the player a high-value objective — destroy the digestion pools to halt local Consumption growth — richer than attritioning an undifferentiated blob, and they read well as mission targets (a "destroy the birthing pits" special mission). But they require a sub-region unit/structure category, targeting, and battle-resolution hooks that do not exist yet. The system ships abstract-first: the Consumption *rate* stands in for the pools until this is committed.

### 6.12 Region-Level Going-Public Generalization (raised by Tyranids, §4.24)

**Question:** The Tyranid model requires the default-Imperial faction to hide/unhide **per region**, whereas the existing revolt machinery (§4.20) flips factions public/hidden at **planet** granularity and excludes the default faction. Should *all* going-public transitions (Orks §4.22, Revolt §4.20, Cult) be unified at region granularity, or should planet-level be kept for infiltrators with a separate region-level path added only for the Imperial remnant?

**Why it comes up.** Region-level hide/unhide for the Imperial remnant is the committed near-term choice (§4.24), and a region-level model is arguably truer for every faction (a cult can be crushed in one region while holding another). But generalizing touches the Ork amassing/WAAAGH! emergence and the revolt lifecycle, both currently reasoned at planet scale. The broader unification is deferred; the remnant's region-level path is built first as the concrete need.

### 6.13 Soldier Attribute Growth & Hero Representation (raised by Field Experience, §4.12)

**Question:** Mission field experience (§4.12) grows skills but not attributes, on the working assumption that a marine's core attributes are largely fixed by the time he reaches active service. The fiction, however, gives its exceptional characters markedly higher attributes, so how should such "heroes" arise mechanically?

**Why it comes up.** Options include leaving attributes entirely fixed from generation (heroes are simply generated with better rolls); allowing *extreme-margin* events — surviving a check far above one's ability — to grant rare attribute growth (a "crucible forges heroes" model that reuses the same margin-scaled field-experience hook); or a separate milestone/award-driven attribute track. Each has different implications for how legible and how deterministic hero emergence feels. Parked for a later pass; attributes remain static for now, so the field-experience system ships skills-only.

---

## 7. Glossary

| Term | Definition |
|---|---|
| Armory Inventory | (Proposed, §4.23/§6.9) A finite, per-pattern pool of weapons and armor held by the chapter, depleted as brothers fall and replenished by wargear pledges. Not yet committed; wargear replacement may instead stay abstracted against Requisition. |
| Battle Brother | A full Space Marine belonging to the player's chapter. |
| Battle Value | A numeric score representing the approximate combat power of a squad template. Used to generate balanced opposing forces. |
| Chapter | The player's Space Marine organization: nominally 1,000 Battle Brothers organized into ten companies. |
| Combat Pace | The Jog movement tier. The soldier moves at half their MoveSpeed. Shooting is permitted with the full weapon `Bulk` penalty, but no aim bonus applies. Entering Jog resets accumulated aim. |
| Contentment | A per-region 0–100 scalar on the default-Imperial RegionFaction representing civilian loyalty/stability. Eroded by overcrowding, war-weariness, hidden-faction drain, thin garrison, and governor temperament; low Contentment drives revolt (§4.20). Presented at planet level as a population-weighted rollup. |
| Crouching | A stance available to stationary soldiers. Lower body hit locations are excluded from ranged hit rolls. Melee offense is penalized; the soldier is easier to hit in melee. Requires one turn to enter or exit. A crouching soldier must stand before moving. |
| Disposition | The tactical posture of a squad on an order: Mobile (active operations), Dug In (defensive), or Raiding (moving to engage and then returning). |
| Faction | Any organized force in the sector: Space Marines, Tyranids, Genestealer Cults, Imperial PDF, etc. |
| Faction Behavior | A `[Flags]` field on a Faction composing its special behaviors (e.g. `CanInfiltrate`, `UniversallyHostile`, `Indelible`), replacing the previous ad-hoc booleans. A faction's identity is the composition of its behaviors (§4.21). |
| Faction Relationship (Stance) | The stored posture between an unordered pair of factions: `Hostile | Neutral | Allied`. Default is Hostile; the player chapter and default-Imperial faction start Allied. Replaces the old binary Imperial-vs-non-Imperial enemy test (§4.21). |
| Feral (Orks) | The pre-WAAAGH! Ork state: present and growing but not public — fighting internally, not yet an extra-territorial actor. Subject to a growth efficiency penalty representing infighting (§4.22). |
| Garrison | The number of troops a faction keeps assigned to defending a specific region, as distinct from troops available for offensive operations. |
| Genestealer Cult (GC) | A hidden faction that infects and converts a planet's population, growing covertly until strong enough to reveal itself. |
| Geneseed | The biological material (progenoid glands) harvested from Space Marines. Required to create new initiates. |
| Governor | A named NPC character who leads an imperial-aligned planet's civilian and military administration. Has personality traits that affect requests and opinion. |
| Hit Location | A specific body part (head, torso, arm, leg, etc.) that can be individually wounded, crippled, or severed. |
| Indelible Infestation | The property (via the `Indelible` Faction Behavior) that an Ork RegionFaction can never be eradicated: reducing its population to zero flips it non-public and it regrows from 1, rather than the presence being removed (§4.22). |
| Inter-Faction Intelligence (Belief) | What an observer faction *believes* about a region or target faction's presence/strength there, allowed to diverge from ground truth in both directions (false negatives; paranoia/disinformation false positives). V1 is sparse numeric `PlanetFaction.RegionIntel`; the fuller model adds target-specific `IntelLevel`. Stored state is fed by listening posts, patrols, recon, battle contact, and scenario seeding, not a function of truth (§4.21). |
| IntelLevel | The fidelity of an inter-faction intelligence belief: `None | Rumor | Suspected | Confirmed | Located`. A targeted response (e.g. an Ork cull) requires `Confirmed` or higher (§4.21). |
| Insurrectionist Faction | A single sector-wide Conversion-growth Faction (like the Genestealer Cult faction) that recruits from discontented Imperial populations and contests regions through the normal combat systems. The mechanical embodiment of a revolt (§4.20). |
| Intelligence Level | A per-region value representing current information quality about enemy forces there. Decays over time. |
| Listening Post | A buildable/sabotageable regional sensor infrastructure value on `RegionFaction`, replacing the old `Detection` defense. It feeds that faction's regional belief (`PlanetFaction.RegionIntel`) each turn rather than directly being the belief itself. |
| Pledge | A promise of material support generated when the chapter fulfills a request, in addition to the opinion change. Carries a source, type (Requisition, wargear, vehicle, ship, recruits, raw materials, recruitment rights, or intelligence hook), payload, and delivery schedule. May be a standing tithe, a one-off, a rights grant, or a hook (§4.23). |
| Pop | (Proposed, §6.7) A subdivision of a region's population carrying its own loyalty/affiliation that drifts over time. Not implemented; raised as the possible long-term substrate unifying conversion, revolt, and corruption. |
| Mission | A specific operational objective assigned to one or more squads: Recon, Advance, Ambush, Assassination, Sabotage, Extermination, Defense, Patrol, Construction, or Diversion. |
| In Melee | A squad-level movement tier for maintaining and completing an engagement. Soldiers already adjacent to an enemy hold and fight; separated squad members close toward an engagement position and charge when they can reach it. |
| Leftover Movement | Per-soldier unused movement distance banked without a cap across consecutive moving turns. It is added to the next moving turn's tier allowance and reduced only by distance actually traveled; selecting Stationary resets it. |
| Movement Tier | One of five squad-level tactical states selected each battle turn: Stationary, Walk (1/5 MoveSpeed), Jog (1/2 MoveSpeed), Run (full MoveSpeed), or In Melee (hold engaged soldiers while separated soldiers close). The tier determines movement allowance, action restrictions, aim treatment, and defensive `CurrentSpeed`. |
| Order | The assignment of one or more squads to a Mission, specifying disposition and aggression level. |
| Order of Battle | The full hierarchical structure of the chapter: Chapter HQ → Companies → Squads → Marines. |
| OpFor | Opposing Force. Any non-player faction unit encountered in a mission or battle. |
| Orks | A fungal xenos faction (`UniversallyHostile | Indelible`) that cannot be eradicated from a region, grows inefficiently while feral, and coalesces into a WAAAGH! that erupts outward and draws more Orks to the sector (§4.22). |
| Universally Hostile | A Faction Behavior marking a faction as Hostile to every other faction regardless of stored stance, and unable to be set Neutral/Allied. Basis for Orks "fighting everyone" (§4.21). |
| WAAAGH! | The public, expanding Ork state: a Warboss has united a region's amassed Orks into an extra-territorial force that acts as a beacon, spawning unmapped Ork worlds and reinforcing fleets (§4.22). |
| Prone | A stance available to stationary soldiers. Only head and upper torso hit locations are valid ranged hit targets. Melee offense is heavily penalized; the soldier is significantly easier to hit in melee. A soldier can drop prone in one turn from any stance. Returning to crouching takes one turn; returning to standing takes two turns. A prone soldier cannot move. |
| Region | A sub-area of a planet. Each region has its own faction presences, garrison counts, intelligence level, and infrastructure ratings. |
| RegionFaction | The presence of a specific faction in a specific region, with its own population, garrison, organization, listening-post, entrenchment, and anti-air values. Holds the list of squads of that faction currently landed in the region. |
| Requisition | The chapter's abstract favor/supply-credit pool — the universal currency of the supply economy and the "political capital" pillar made concrete. Earned from most request fulfillments and spent on procedures, recruitment, wargear, and repairs (§4.23). |
| Run | The fastest ordinary movement tier. The soldier moves at full MoveSpeed and may reload or swap weapons, but cannot make a ranged attack of any kind. A soldier not already engaged may finish the Run with a penalized charge into melee. Turning is restricted to 45 degrees (one facing step) per turn. |
| Spare Troops | The portion of a faction's organized military force in a region that exceeds the required garrison, available for offensive operations or construction. |
| Squad | A group of marines operating as a unit, assigned to a specific template that defines their role and composition. |
| Squad Template | The definition of a squad type: roles, minimum and maximum member counts, battle value, and permitted weapon options. |
| Sector Capital | The top governance seat in the sector — the highest-importance Imperial-controlled world across all subsectors. Its governor is the Sector Lord. A derived designation, recomputed during sector generation and on load (§4.1). |
| Sector Lord | The governor of the sector capital; the ranking secular Imperial authority in the sector. The character who promises the player's chapter its Chapter World in the Opening Scenario (§5.2). |
| Seat of Government | A subsector's governing world — its highest-importance Imperial-controlled planet (the one with a governor). Distinct from the population-based warp capital, which may be enemy-held. Its governor is the subsector governor (§4.1). |
| Stance | A soldier's body position when stationary: Standing, Crouching, or Prone. Stance affects which hit locations are valid ranged targets and applies melee combat modifiers. Transitions cost one turn each, except dropping prone (one turn from any stance). |
| Standing | The default upright stance. No ranged or melee modifiers apply. |
| Standing Tithe | A recurring pledge that delivers materiel to the chapter on a fixed cadence (e.g. a forge world's annual wargear quota), persisting until its source world is lost or the relationship lapses (§4.23). |
| Subsector Capital | The highest-importance planet in a subsector, determined during sector generation by an importance score (population size for 0.7; strategic classification is a post-0.7 addition to the score). The capital has established warp lanes to all other planets in its subsector. Used for warp topology; the subsector's *seat of government* (which must be Imperial-held) is tracked separately (§4.1). |
| Subsector Governor | The governor of a subsector's seat of government. Ranks below the Sector Lord in the governance hierarchy (§4.1). |
| Supply Line | The route along which a pledging world's materiel deliveries reach the chapter. Bound to the fate of its source world (a world that revolts or falls suspends or defaults its pledges) and, post-factional-fleets, potentially interdictable in transit (§4.23, §6.10). |
| Task Force | A grouping of ships within the chapter's fleet. |
| Turn | One in-game week. The smallest unit of strategic time. |
| Walk | The slowest movement tier above Stationary. The soldier moves at 1/5 MoveSpeed. Shooting and aiming are permitted, and accumulated aim is preserved, but the applied bonus from weapon Accuracy plus accumulated aim is halved and only half the weapon's `Bulk` penalty applies. |
| Warp Lane | An established, well-traveled route between two planets through the Warp. Lane travel has lower transit time variance than charting a direct route. Within a subsector, all lanes radiate from the subsector capital. Cross-subsector lanes connect primarily between subsector capitals. |
| Wound Severity | A classification of how badly a hit location has been damaged: Negligible, Minor, Moderate, Major, Critical, Massive, Mortal, or Unsurvivable. |
