# OnlyWar — Product Requirements Document

**Version:** Alpha 0.7 (In Development)  
**Last Updated:** June 2026  
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
5. [Release Scoping](#5-release-scoping)
   - 5.1 [Released (Alpha 0.6 and prior)](#51-released-alpha-06-and-prior)
   - 5.2 [Alpha 0.7 — Committed](#52-alpha-07--committed)
   - 5.3 [Alpha 0.7 — To-Do](#53-alpha-07--to-do)
   - 5.4 [Alpha 0.7 — Stretch](#54-alpha-07--stretch)
   - 5.5 [Alpha 0.8 — Narrative & Cohesion](#55-alpha-08--narrative--cohesion)
   - 5.6 [Alpha 0.8+ — Cross-Faction Simulation (Relationships, Intel, Orks)](#56-alpha-08--cross-faction-simulation-relationships-intel-orks)
   - 5.7 [Post-0.7 Backlog](#57-post-07-backlog)
6. [Open Design Questions](#6-open-design-questions)
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
- Displays any awards or commendations.
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

**Acceptance Criteria (Planned — 0.7 Stretch):**
- Displays the expected time to recovery alongside each injured soldier on the Squad Screen (not just the Apothecary Screen).
- When a marine dies, the battle results and death record note whether geneseed was successfully recovered.
- **Cybernetic replacements** are available as a treatment option:
  - *Eligibility:* any hit location that has reached its cripple threshold but not its lethal threshold — limbs (arms, hands, legs, feet) and vital locations (head, torso) at crippling severity.
  - *Requirements:* both a Techmarine and an Apothecary must be available (present in the chapter and not deployed); both are consumed for the duration of the procedure.
  - *Cost:* a combination of time (weeks in the Apothecarium) and chapter resources; specific values set during implementation.
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
- The player can initiate a new intake of potential recruits from chapter-held worlds.
- The Armory allows the designation of potential Techmarines to be sent to Mars for training.

---

### 4.10 Battle Review Screen

**Description.** A post-battle replay screen that allows the player to review the turn-by-turn progression of a completed engagement.

> **Note:** The acceptance criteria below describe the original grid-plus-turn-log implementation. The **Battle screen visual overhaul** (see §5.3) has since largely replaced this layout with the four-region replay/report display it specified (force hierarchy tree, replay viewport with playback controls, selected-formation summary and event chronicle, and a battle timeline / casualties-by-round table); the structural slice has landed and only the richer playback/animation, replay overlays, and icon art remain. The criteria below are retained for the underlying replay/seek behavior they still describe — see §5.3 for the current per-region status.

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

*Post-0.7:* Marines who are deployed on a mission but see no combat that turn gain a small amount of experience from the deployment itself (field experience, maintaining readiness).

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
- Marines deployed on missions do not gain training points that week.

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
- **Known discrepancy (to resolve in 0.8):** this spec requires the fallen brother's history, kill record, and awards to be *preserved*, but the current implementation records only a thin death line (`BattleTurnResolver`) and otherwise removes the soldier from the roster without retaining his dossier. Preserving the fallen — so the chapter can remember and honor them — is a prerequisite for the eulogy and Chronicle work and is folded into the 0.8 structured event log task (see 5.5).

---

### 4.13 Turn Simulation — Mission Resolution

**Description.** The rules governing how player-issued and AI-generated orders are executed each turn.

**Acceptance Criteria:**

**Order Assignment**
- The player can assign a squad to one of the following mission types: Recon, Advance (assault), Ambush, Assassination, Sabotage, Extermination, Defense, Patrol, Construction, or Diversion.
- The player sets the target (a region and its occupying faction) and the aggression level (Avoid, Cautious, Normal, Attritional, or Aggressive).
- Multiple squads can be assigned to the same mission, combining their force for resolution.

**Mission Execution**
- If the squad is not already in the target region, an infiltration phase occurs first. Infiltration success depends on the squad's stealth skills relative to the target region's detection level and garrison size.
- If infiltration fails, the squad may be ambushed or forced into a meeting engagement before reaching the objective.
- Each mission type has a distinct execution sequence with skill-based checks and possible combat encounters along the way.
- After the mission objective is resolved, the squad exfiltrates (if in an enemy region). Exfiltration uses the same stealth-versus-detection mechanics as infiltration.
- Mission outcomes affect regional intelligence level, enemy defensive ratings, and enemy casualty counts as appropriate to mission type.

**Mission Types — Specific Behaviors**
- **Recon:** Squad conducts surveillance over multiple days, accumulating intelligence for the region. Higher skill margins produce better intelligence; critically poor margins produce false or corrupted intelligence. Intelligence decays over subsequent turns.
- **Advance:** Squad assaults an enemy-held position. A battle is resolved. Successful advance reduces enemy garrison and may convert or destroy enemy regional holdings.
- **Ambush:** Squad positions and waits for enemy movement before engaging. Success depends on positioning skill and the enemy patrol activity level.
- **Assassination:** Squad locates and eliminates a specific enemy leader target. Target tier determines the strength of the bodyguard force encountered.
- **Sabotage:** Squad degrades enemy defensive infrastructure (detection, entrenchment, or anti-air). Degree of degradation scales with skill margin and the size of the existing defenses.
- **Defense:** Squad defends a region against an incoming enemy assault. Defender advantage applies. Outcome affects whether the enemy assault order resolves successfully.
- **Patrol:** Squad conducts a security sweep, potentially intercepting enemy infiltration or patrol forces.
- **Construction:** Squad in its own region spends the turn building regional fortifications instead of fighting — Entrenchment (Fortify), Detection (Listening Post), or Anti-Air. Progress scales with squad size and the Engineering (Fortification) skill, accumulating across successive turns and sharing resolution with the NPC development path.
- **Diversion:** Squad stages an overt feint against an enemy-held region while remaining in its own, skipping infiltration. A daily show of force (a Tactics check whose difficulty rises with the target's Detection and garrison size) accumulates Impact, inflating the garrison the target's controller feels it must hold; at Normal aggression or higher it also baits a counterattack, in which the exposed feint force is pulled in as a defender.

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
  2. Remaining spare troops fund construction orders (improving organization, detection, entrenchment, or anti-air in ascending cost order).
  3. Any remaining troops fund a scout patrol order.
- Non-player faction orders are resolved alongside player orders in the same turn.

---

### 4.14 Turn Simulation — Battle

**Description.** The rules governing individual squad-level engagements on a 2D grid.

**Acceptance Criteria:**

**Grid and Positioning**
- Battles occur on a 2D grid. Grid size scales with the number and species size of the participating units.
- Each soldier occupies one or more grid cells depending on their species' physical dimensions.
- Opposing forces begin at an engagement range that scales with the battle type: ambushes begin at shorter range; ranged-dominant forces prefer longer initial ranges.

**Turn Structure**
- Each battle turn, every able soldier selects an action based on their situation and aggression setting.
- Available actions: move (at a chosen movement tier toward a destination), fire, aim (stationary only, accumulates accuracy bonus), charge into melee, melee attack, reload, ready a weapon, or change stance.

**Movement Tiers**
- Movement is modeled as four discrete tiers, each a fraction of the soldier's `MoveSpeed`. The tier chosen determines which combat actions are available that turn. A soldier may change tier freely each turn with no transition cost, except that a crouching or prone soldier must return to standing before moving.

| Tier | Speed | Aim | Shoot | Melee | Notes |
|---|---|---|---|---|---|
| Stationary | 0 | Yes | Yes | Yes | Stance effects apply |
| Walk | 1/5 MoveSpeed | Yes | Yes | Yes | Aim state is preserved between walk turns |
| Jog | 1/2 MoveSpeed | No | Yes (no aim bonus) | Yes | Entering jog or faster resets any accumulated aim |
| Run | Full MoveSpeed | No | No | No | Turning restricted to 30 degrees per turn |

- Shooting while running is not permitted in the initial implementation; a high-penalty shoot-while-running variant may be added later.

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

**Melee Combat**
- Squads that close to melee range engage in hand-to-hand combat.
- Melee hit probability is derived from the attacker's melee skill, the defender's melee skill, the defender's per-species evasion value, and the weapons involved.
- A squad in melee cannot fire two-handed ranged weapons unless it opts to disengage.

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
- A squad leaves an engagement in one of two modes:
  - **Covered Withdrawal** (player-ordered): the squad moves away from the enemy at jog speed each turn, shooting as it goes. Withdrawal fire uses normal shooting rules and counts toward the pursuer's casualty threshold. The squad remains on the map until it reaches the map edge, then exits.
  - **Rout** (morale-triggered; morale itself is still open, see §6.2): the squad moves away at run speed each turn with no shooting, remaining on the map until it reaches the edge.
- **Pursuit:** the non-withdrawing force continues to act normally — moving toward and firing at the fleeing squad according to its own aggression. Pursuit tenacity is governed by the pursuer's aggression using the same casualty thresholds as normal battle continuation: an Aggressive force pursues until it cannot reach or shoot the fleeing squad or its own casualties trigger withdrawal; an Avoid force does not pursue at all. A pursuer that takes sufficient casualties from withdrawal fire may itself begin to withdraw, ending pursuit.
- Combat ends when the two forces are outside mutual shooting range and at least one side is fully withdrawing or routing; both forces need not exit the map.
- Units capable of burrowing or flight may disengage immediately, bypassing the normal withdrawal sequence.

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
- Factions spend build points to improve regional infrastructure: Organization (enables more organized military force), Detection (increases enemy stealth difficulty), Entrenchment (increases defensive strength), and Anti-Air (resists aerial assaults).
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
- The player can save the game from the top menu bar.
- The player can load a saved game from the main menu.
- All game state is preserved across save/load: sector state, all faction populations and garrisons, all marines (attributes, skills, wounds, history, kill records), all squad assignments, fleet positions, loaded squads, active orders, active governor requests, game date, and chapter battle history.
- Loading a save produces a game state identical to the state at the time of saving.

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

**Notability Classifier.** A shared rule set determines which events are "notable" — worthy of a callout in the Turn Report and (if adopted, see below) the Chapter Chronicle. Initial notable triggers: a brother's first confirmed kill; crossing a kill milestone; the death of a veteran (above a service/rank threshold); an officer crippled or slain; a last-survivor outcome; a squad that held under orders past a heavy-casualty threshold; first contact with a new faction; a world saved or lost (by the chapter or by the wider Imperium); a hidden cult revealed. Thresholds are tunable and should live in rules data where practical.

**Per-Surface Acceptance Criteria:**

- **Turn Report (4.11):** Casualty and outcome lines name individuals, surface notable events from the classifier, and frame results against the orders the player issued. Kill milestones and acts of heroism are called out rather than buried in aggregate counts.
- **Soldier History Log (4.7):** The event vocabulary is enriched beyond recruitment/promotion/wound to include first blood, survival against odds, instructor/mentor relationships, kill milestones, oaths sworn, and near-death recoveries.
- **Death & Apothecary Records (4.12, 4.8):** A brother's death produces a eulogy-style record — where and how he fell, his final tally, years served, and whether geneseed was recovered; **lost geneseed is narrated as a compounding loss**, not a silent stat change. Injuries are described with gravity, and recovery is framed as a brother's struggle back to the line.
- **Battle Review Log (4.10):** Per-turn log entries name their actors and add flavor on critical hits, kills, and last stands, rather than reading as "Soldier 3 fires at Soldier 7."
- **Governor / Inquisition Requests (4.16, 6.5):** Request text is voiced in character, with tone driven by the requester's personality traits and authority.
- **New Game Founding (4.1):** A short founding history / chapter myth is generated at campaign start, seeding the first entry of the chapter's narrative record.
- **Wider-Imperium Dispatches:** As the uncontrolled Imperial presence grows (per the sandbox reframe), its actions reach the player as voiced dispatches — Battlefleet priorities, other chapters' deeds, Inquisitorial edicts — that texture the sector as a living theater the chapter is one actor within.

**Candidate Feature — Chapter Chronicle (decision pending).** A persistent, sector-level narrative log: the campaign-scale analogue of the per-soldier history log. It would auto-record notable events (per the classifier above) into a single readable saga — founding myth, defining battles, named heroes and their deaths, worlds won and lost, factions revealed — giving the sandbox player one place to relive and retell the campaign. **Decision deferred:** draft the per-surface text improvements above first, then revisit whether a dedicated Chronicle screen/system is warranted or whether the enriched Turn Report and history logs already carry the narrative load.

---

### 4.20 Turn Simulation — Revolt & Civil Stability

**Description.** Imperial-aligned populations are not unconditionally loyal. Overcrowding, war-weariness, hidden corruption, and the temperament of their governor erode a region's contentment; sufficiently discontented populations organize, take up arms, and revolt. A revolt is modeled by reusing the existing converting-faction machinery (the Genestealer Cult path): an **Insurrectionist faction** recruits from the discontented population, contests regions through the normal combat and order-resolution systems, and — if neglected — spreads and can tear a world out of the Imperium's grasp.

This is a behavioral specification for the simulation. It is scoped for **Alpha 0.7 Stretch** (see §5.4) on the **faction-presence model**, deliberately built as a forward-compatible subset of a possible future Pop-based population model (see Open Design Question §6.7).

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
- Intelligence is modeled as a **belief held by an observer about a target**, keyed `(observer Faction, target Faction, Region)` and carrying a **believed presence/strength** and an **`IntelLevel`**: `None | Rumor | Suspected | Confirmed | Located`. (A v1 implementation may collapse to `None | Suspected | Confirmed`; the enum is defined in full but only the transitions a feature needs are populated.)
- Crucially, **belief may diverge from reality in both directions**:
  - *False negative* — a target is present in the region but the observer believes `None` (e.g. undetected feral Orks, an organizing cult).
  - *False positive* — the observer believes a faction is present where it is not. This is materialized only by an explicit cause: a governor's **Paranoia** trait (§4.16) or deliberate **disinformation** planted by a rival. Intelligence is therefore stored state that ground truth *nudges*, never a pure function of ground truth.
- The matrix is kept **sparse**: an entry is materialized only when (a) an observer has co-located presence with a real target in the same planet, or (b) something explicitly injects a phantom belief. Everything else resolves to `None` without storage.
- An observer's `Detection` rating (and recon/intelligence missions) drives the **rate** at which a real co-located target's `IntelLevel` ratchets upward; the target's population and public/expanding status raise the **signal** it emits (a feral camp in a low-`Detection` backwater lingers at `Rumor` for a long time; a public expansion is effectively self-announcing and jumps to `Located`).
- Consumers act on belief, not truth: a defender will only generate a *targeted* response (e.g. an Ork cull, §4.22) when its `IntelLevel` on the target reaches `Confirmed` or higher — and acting on a Confirmed-but-false belief wastes force chasing a threat that is not there.
- This generalizes and absorbs the existing governor detection of hidden factions (§4.16) and the OpFor fog-of-war already shipped (§5.3): "what the Imperials know" becomes the special case "what the default-Imperial faction believes about a target in a region."

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

### 5.3 Alpha 0.7 — To-Do

Targeted for 0.7. These items were not part of the committed 0.7 set (§5.2), but several have since landed. Status reflects the current codebase (✅ done · ◐ partial · ⬜ not started):

- ◐ **Battle screen visual overhaul:** Rework the existing Battle Review Screen from a simple grid-plus-turn-log into an automated battle replay/report display, using the V3 Chronicle Formation Hybrid direction captured in `Design/BattleScreenMockups/battle_screen_mockup_v3_01_chronicle_formation_hybrid.png`. This is not a tactical command surface: battles have already resolved, so the UI should avoid order buttons, bottom command rails, movement previews, and blue/red territory ownership washes. The screen should instead help the player understand what happened in a sizable battle. *(Partial — the structural first pass has landed: the four-region layout, view-model layer, summary service, force tree, selected-formation panel, event chronicle, timeline, and casualty-by-round table are all built and rendering. Remaining work is real playback animation/speed, richer replay overlays (banners, casualty/rout markers, projectile and charge callouts), faction/type icon art, and a collapsible tree. Detail below.)*
  - ✅ **Build a battle replay display model between `BattleHistory` and the UI:** force hierarchy nodes, selected formation summary, battle event entries, round timeline entries, and casualty-by-round summaries. *(Implemented in `Models/Battles/BattleReplayModels.cs` — `BattleReplayDisplay` plus `BattleForceHierarchyNode`, `BattleFormationSummary`, `BattleEventEntry`, `BattleTimelineEntry`, `BattleCasualtyRoundSummary`, and a `BattleEventSeverity` enum.)*
  - ✅ **Add a summary/analysis service that derives those view models from `BattleHistory`/`BattleTurn`:** force tree, current round, losses per force, losses by round, selected formation stats, notable events, and event chronology rows. *(Implemented in `Helpers/Battles/BattleReplaySummaryBuilder.cs`, with unit coverage in `OnlyWar.Tests/Battles/BattleReplaySummaryBuilderTests.cs`. Derives the force hierarchy, per-round and cumulative casualties, result/phase labels, formation summary, and per-action event entries from the resolved turns.)*
  - ◐ **Replace the current Battle Review layout with a four-region structure** *(the left/center/right/bottom layout is built in `battle_review_screen.tscn` and wired in `BattleReviewView.cs`/`BattleReviewController.cs`; per-region status below):*
    - ◐ Left: collapsible force hierarchy tree with opposing forces, nested companies/squads/vehicles, strength counts, losses, selected row highlighting, and small faction/type icons. *(Done: nested player/opposing → unit → squad rows with current/starting strength, losses, and selected-row highlighting; clicking a squad row selects the formation. Not done: rows are not actually collapsible (the whole tree is always expanded), and there are no faction/type icons — the `IconKey` field exists on the node but the view renders text markers (`▸`/`•`) instead of icon art.)*
    - ◐ Center: replay viewport showing formations, banners, casualty markers, routed trails, projectile/charge/event callouts, and compact top-center playback controls (previous round, step back, play/pause, step forward, next round, speed). *(Done: a `SubViewport`-based replay viewport draws the battlefield grid, per-soldier formation markers (cyan/crimson by affiliation), centroid formation labels with live strength, a selection ring/highlight, and clickable markers that select the formation; the top-center playback button row is present. Not done: no banners, casualty markers, routed trails, or projectile/charge/event callouts — only living-soldier markers are drawn, and the viewport simply redraws the chosen turn's end state with no motion.)*
    - ✅ Right: selected formation summary at the top (commander, starting/current strength, losses, fatigue/morale/ammunition/effects where data exists), with event chronicle below it for timestamped actions, morale checks, routs, volleys, casualties, and phase summaries. *(Implemented — the top panel shows formation name/type/force, commander, starting/current strength, losses with percentage, and derived fatigue/morale/ammunition labels plus a notable-effects list; the chronicle below lists per-action event cards with timestamp, type, actor, formation, and severity-coloured borders. Fatigue/morale/ammunition are heuristic labels derived from available data rather than first-class simulation values.)*
    - ✅ Bottom: informational battle timeline and casualties-by-round table, not a command menu. *(Implemented — a horizontal clickable round timeline (each round seekable, severity-tinted) and a casualties-by-round grid showing per-round and cumulative player/enemy losses.)*
  - ◐ **Preserve the visual language established by the Sector Map and Chapter Screen:** dark panels, antique-gold borders, parchment text, smoky glass, muted cyan/crimson only for unit affiliation markers and row accents, and amber for warnings/notable events. *(Largely followed — dark/smoky panels, gold accent text, cyan/crimson affiliation colours, and amber for warning/critical events are all in place via inline styleboxes. Worth a consistency pass against the shared theme used by the other screens rather than the locally-defined colours here.)*
  - ◐ **Upgrade the current previous/next-turn behavior into playback controls.** Initial implementation may still step discretely through resolved turns; smooth animation and richer projectile/path interpolation can follow once the structural UI is in place. *(Partial — the five playback buttons exist and step discretely: step-back/step-forward move one round, previous-round/next-round jump to the first/last round, and play/pause currently just advances one round (no running playback loop). A `SpeedButton` exists in the scene but is not wired up. No automatic play loop, animation, or speed selection yet.)*
  - ✅ **Keep the first vertical slice data-driven and robust for large battles:** the force hierarchy, event chronicle, selected formation summary, and casualty timeline should remain readable with many units before investing heavily in visual animation. *(Done — all four panels are driven entirely from the view models, lists are scrollable, and the casualty table caps to the most recent rounds; the structural slice is in place ahead of any heavy animation work.)*
- **Strategic Layer Phase 2:**
  - ✅ Population growth relative to planet carrying capacity (faster growth when underpopulated, slower when near capacity). *(Implemented — carrying capacity is an absolute, per-type value rolled from new `PlanetTemplate` columns (`CarryingCapacityBase`/`CarryingCapacityStandardDeviation`), distributed across a planet's regions and persisted per region. Starting population is seeded as a fraction of each region's capacity so no world begins above capacity; dense biomes (Hive, Forge) start nearly full while sparse ones (Agri, Feral) have room to grow. Per-type population and capacity scales are canon-grounded (Hive ~80B typical down to Death ~310K) and stored as log-normal `Floor`/`Scale` values. Each turn, organic (logistic and baseline) growth is scaled by a `1 - regionPop/capacity` crowding factor — near-maximal when sparse, zero at capacity, and gently negative above capacity so an overfull region drifts back down.)*
  - ✅ Garrison attrition (0.1% of garrison retires per week, requiring replacement from population growth). *(Implemented — each week 0.1% of a faction's regional garrison retires before fresh recruitment from population growth is applied, in the same factions that recruit: PDF, player, and hidden/secret factions.)*
  - ✅ OpFor fog of war, recon orders, and special missions. *(Implemented — recon orders raise a region's intelligence and the special-mission/intelligence system already existed; this pass closed the remaining fog-of-war leak in the UI. Hidden factions are now concealed on every screen (folded into the civilian count, discovered only via the intelligence system); public-enemy population is graded by intelligence level ("Unknown" → fuzzed → exact); and enemy defenses appear only once intelligence exceeds a threshold and only as fuzzy descriptions, never raw values. The Region screen previously revealed hidden-faction identity and exact garrison/defense values regardless of intelligence — now consistent with the planet tactical screen.)*
  - ✅ Diversion missions. *(Implemented — a squad can be ordered to run an overt "Diversion" feint against an enemy-held region while remaining in its own. Unlike stealth missions it skips infiltration: a `DemonstrateForceMissionStep` makes a daily show of force (a Tactics check whose difficulty rises with the target's Detection and garrison size), accumulating Impact. Diversions resolve in a new pre-planning "shaping" phase **before** factions generate their turn orders, so the feint shapes enemy decisions that same turn. A successful feint projects a superlinear `apparentThreat = manpower × (1 + impact/scale)²` onto the target's `PerceivedThreatBonus`, inflating the garrison its controller feels it must hold; at Normal aggression or higher it also raises the feinting force's `ProvocationLevel`, which lowers the AI's force-ratio threshold for attacking (toward parity) and biases its target selection — baiting a counterattack. Because the feint force stands in the open, it is pulled into the fight as a defender if it draws that counterattack. Both effects are transient: set during the shaping phase, consumed by faction planning, then cleared the same turn (never saved). **Enemy-generated diversions (the AI running its own feints) are deferred post-0.7 — see §5.7.***)*
  - ✅ Player-constructable fortifications (Entrenchments, Listening Posts, Anti-Air batteries). *(Implemented — a squad in its own region can be ordered to Fortify (Entrenchment), Build Listening Post (Detection), or Build Anti-Air; the squad spends the turn building instead of fighting. Progress scales with squad size and a new Intelligence-based "Engineering (Fortification)" skill that all combat marines train, accumulating defenses over successive turns. The construction-mission resolution and save round-trip are shared with the existing NPC development path.)*
  - ✅ Burrowing and camouflage as ambush tactics. *(Implemented.)*
    - ✅ **Evasion / hard-to-hit modifier:** *(Implemented — a per-species evasion value is now subtracted from the attacker's total in both `ShootAction` and `MeleeAttackAction`, giving elusive bodies (serpentine Raveners, weaving Genestealers/Lictors) a defensive "harder to hit" lever in melee as well as ranged without overloading Size, which still tracks real bulk for wounds/footprint. Melee attacks now also account for the defender's melee skill. Previously the Ravener leaned on its high MoveSpeed for ranged evasion only; this closes the melee gap.)*
    - ✅ **Immediate disengagement:** burrowing- and flight-capable units may break off an engagement immediately, bypassing the normal covered-withdrawal sequence (specified under Battle Continuation in §4.14).
- ✅ **Subsector warp lanes:** *(Implemented.)* Each subsector has a capital world, determined by an importance score (population size for 0.7; strategic classification post-0.7). The capital has established warp lanes to all other planets in the subsector. Cross-subsector lanes connect the capitals of adjoining subsectors, with a spanning tree guaranteeing the whole sector is reachable. Lanes are derived deterministically from planet positions during sector creation (and rebuilt on load) rather than persisted. The fleet movement dialog routes along lanes by default. The "Chart Direct Route (Risky)" option is deferred post-0.7 (see 4.17).
- ✅ **Tyranid Infiltration Units:** Lictor and Ravener content data. *(Species, SoldierTemplate, and skill-training data added via the `migrate-tyranids` rules-DB migration — Lictor as an elite WS6 ambusher (S6, T5/6-wound, Perception-led senses, high Stealth, melee carried by skill rather than Dex) and Ravener as a fast-attack glass cannon (the fastest ground bug, Hormagaunt-level instincts, Warrior-tier body). Squad templates added via `migrate-tyranid-squads` — the Lictor as a solo unit (`Scout | Elite`, 15mm chitin) and the Ravener as a 5-strong leaderless pack (`Scout`), both with 15mm chitin and Scything Talons; both are now eligible for `ForceGenerator` dynamic forces (scout patrols and generic forces), and the per-species melee/ranged evasion lever they were designed around is now in place (see above). The fixed `UnitTemplate` armies — legacy scaffolding only the player's Space Marine chapter ever instantiated — were unused for every non-player faction, so the dead Tyranid (and Genestealer Cult) `UnitTemplate` rows were dropped from the rules DB via the `remove-unused-unit-templates` tool command rather than extended.)*

- ◐ **Apothecary Phase 2:** Cybernetic replacements; geneseed recovery noted in death records; recovery time displayed on squad screen. *(Partial — the first presentation/data pass has landed; remaining work is persisted medical procedures, staff/resource rules, death-record geneseed recovery, Squad Screen recovery-time surfacing, and any real geneseed purity simulation.)*
  - ✅ **First pass - UI direction and layout:** Reworked the Apothecary Screen around the V2 flow captured in `Design/ApothecariumScreenMockups/apothecarium_refresh_v2_01_vault_default.png`, `Design/ApothecariumScreenMockups/apothecarium_refresh_v2_02_soldier_wounds.png`, and `Design/ApothecariumScreenMockups/apothecarium_refresh_v2_03_unit_rollup.png`. The first pass replaces the old two-report layout with a persistent left panel containing a default-selected `Gene Seed Vault` button and wounded-filtered chapter/unit tree, plus a stateful detail panel for vault, unit/squad rollup, and soldier medical views.
  - ✅ **First pass - structured view data:** Added structured Apothecary view models and a medical summary builder so the controller renders data instead of formatting large UI strings. The builder derives wounded soldiers, serious wounds, out-of-action duration, unit/squad rollups, wound rows, severed body parts, cybernetic state, and gene-seed maturity data from the existing domain model.
  - ✅ **First pass - vault panel:** Shows mature gene-seed stockpile, mature implanted progenoids, immature implanted progenoids, progenoids maturing within one year, and at-risk implanted progenoids. Purity is currently a presentation status only; it should become real domain data only if purity is intended to affect play.
  - ✅ **First pass - soldier medical panel:** Selecting a soldier shows wound locations, severity, expected recovery time, whether gene-seed-bearing locations are safe/damaged/lost, replacement eligibility, and cybernetic/vat-grown replacement options. Replacement buttons are presentation-only in this pass.
  - ✅ **First pass - Vitruvian wound display:** Added a custom diagnostic body diagram that maps current human hit-location names to stable coordinates and colors them by wound severity, severed state, crippled state, and cybernetic replacement.
  - ✅ **First pass - unit/squad rollup:** Selecting a squad or unit summarizes medical readiness before drilling into individual soldiers, including healthy, wounded, out-of-action, ready-next, maximum recovery time, and serious wound rows.
  - ✅ **First pass - verification:** Added focused tests for the medical summary builder. Full test suite and headless Apothecary scene load pass.
  - ⬜ **Second pass - interactive treatment assignment:** Convert the cybernetic/vat-grown replacement options from presentation-only buttons into real assignments. Cybernetic replacement should be the faster, lower-cost option; vat-grown replacement should be rarer, slower, and more resource-intensive.
  - ⬜ **Second pass - persisted medical procedures:** Add a persisted procedure model carrying soldier id, hit-location id, procedure type, weeks remaining, requisition/resource cost, status, and any staff commitments. Add save/load support and tests.
  - ⬜ **Second pass - turn processing:** Hook medical procedures into weekly turn processing. Decrement timers, consume resources and staff availability as applicable, and apply the result to the hit location when complete. Cybernetic completion can use the existing `HitLocation.IsCybernetic` flag; vat-grown completion should restore the location without setting that flag.
  - ⬜ **Second pass - staff and cost rules:** Centralize exact procedure costs, staff requirements, and recovery times outside UI code. If the Techmarine/Apothecary availability requirements in 4.8 remain in scope, implement availability checks and staff lockout for the procedure duration.
  - ⬜ **Third pass - cross-screen recovery display:** Surface expected recovery time alongside injured soldiers on the Squad Screen, not only in the Apothecary Screen.
  - ⬜ **Third pass - death records and geneseed recovery:** When a marine dies, battle results and death records must note whether geneseed was successfully recovered. Lost geneseed should be narrated as a compounding loss per the Narrative Voice requirements.
  - ⬜ **Third pass - geneseed purity decision:** Decide whether geneseed purity is a real simulation concept. If yes, add the domain model, persistence, and effects; if not, keep purity as a non-authoritative presentation status or remove it from the final acceptance surface.

### 5.4 Alpha 0.7 — Stretch

To be drawn from if capacity allows:

- **Living Universe Phase 3B — Revolt:** Revolutionary population mechanic; evidence-based requests. Full behavioral spec in §4.20 — per-region Contentment driving a sector-wide Insurrectionist faction (reusing the converting-faction/Cult machinery), governor Severity-driven response, garrison defection, intra- and inter-planet spread, evidence-gated requests, and "for a revolt" limited to inaction-with-consequences in 0.7. Built on the faction-presence model as a forward-compatible subset of the Pop-model question (§6.7); Chaos radicalization specified but gated on Chaos content.
- **Battle Logic Phase 4:** Grenades, AoE weapons, flamers, morale, sprint/fire tradeoff, retreat mechanic, post-battle loadout recalculation.
- **Battle Visuals Phase 3:** Terrain and cover representation; line of sight; elevation-based fire advantage.
- **UX Improvement Phase 1:** Drag-and-drop where applicable; squad row redesign; zoom-adaptive planet name labels.
- **Mission System Expansion:** Talent recruitment missions; IG support missions; Chaos cult investigation; STC hunt; prisoner recovery.

### 5.5 Alpha 0.8 — Narrative & Cohesion

The connective pass that turns 0.7's broad simulation into a felt sandbox narrative. These items are mostly *connective* work over systems that already exist — making the player feel the consequences the simulation already produces — rather than net-new systems. See Section 4.19 for the governing specification.

**Implementation prerequisite — structured soldier event log.** Soldier history is currently an unstructured `List<string>` of free-text lines, written from only a handful of sites (founding, promotion/transfer, ratings/awards, a per-battle summary, and a thin death line); non-combat missions (recon, sabotage, assassination, infiltration, fortification) record nothing. Before any narration work, replace this with a **structured, queryable event log** — typed events carrying date, location, faction, weapon, magnitude, and related-soldier references — that serves as both the substrate the notability classifier queries and the source the narrator renders to text. Audit findings driving this:

- Continuity callbacks (4.19 Principle 3) and the notability classifier require *querying* the past ("first kill?", "who was his mentor?", "crossed 50 kills?", "survived the battle that killed his sergeant?"), which free-text strings cannot reliably support.
- Events that are never emitted today and must be added: first blood, kill milestones, last-survivor / survival-against-odds, mentor/instructor relationships, oaths, near-death recoveries, and **all non-combat mission outcomes**.
- The fallen brother's dossier must be *preserved* on death (see 4.12 known discrepancy) rather than discarded.

Suggested 0.8 sequencing: (1) structured event log + migration of existing call sites and death-record preservation; (2) emit the missing events; (3) notability classifier over the log; (4) narrator/voice pass rendering events and report lines.

- **Narrative Voice baseline:** Apply the 4.19 authoring principles and notability classifier across the Turn Report, Soldier history log, and death/apothecary records — named individuals, specificity, continuity callbacks, and outcomes framed against the player's orders.
- **Eulogy-style death records:** Where, how, final tally, years served, and geneseed recovered or lost (with lost geneseed narrated as a compounding loss).
- **Enriched soldier history vocabulary:** First blood, survival against odds, mentor relationships, kill milestones, oaths, near-death recoveries.
- **Voiced requests:** Governor and Inquisition request text driven by personality and authority.
- **Founding myth:** Generate a short chapter history at new-game start.
- **Battle Review log humanization:** Named actors and flavor on criticals, kills, and last stands.
- **Wider-Imperium dispatches (initial):** Voiced notifications for major uncontrolled-Imperium actions in the sector (Battlefleet priorities, worlds the Imperium addresses without the chapter), establishing the relevance/legacy stakes framing.
- **Chapter Chronicle (decision pending):** Specify and, if adopted, implement a persistent sector-level narrative log. Decision to be made after the per-surface text improvements above are drafted (see 4.19).

### 5.6 Alpha 0.8+ — Cross-Faction Simulation (Relationships, Intel, Orks)

A simulation expansion that lifts the sector beyond a binary Imperial-vs-everyone model. Sequenced **substrate first**, because the Ork feature depends on it and because the substrate independently benefits Revolt (§4.20) and future Chaos content. Full behavioral specs in §4.21 and §4.22.

- **Substrate (prerequisite): Faction Relationships & Inter-Faction Intelligence** — replace the binary `AreFactionsEnemies` test with a per-faction-pair Stance store (default Hostile; player↔Imperial seeded Allied); consolidate `Faction`'s ad-hoc booleans into a `[Flags] FactionBehavior` field (folding in `CanInfiltrate`, adding `UniversallyHostile` and `Indelible`); and build the per-faction graded **intelligence-as-belief** model (`IntelLevel` ladder, sparse materialization, false positives via paranoia/disinformation), generalizing the existing governor-detection and OpFor fog-of-war as the default-Imperial special case. Spec: §4.21.
- **Orks & Indelible Infestation** (depends on the substrate) — `UniversallyHostile | Indelible` Ork faction; indelible `RegionFaction` with pop-0 → non-public → regrow-to-1; logistic growth with an Ork multiplier and a feral efficiency penalty; two-dimensional state (awareness × expansion) yielding unnoticed-feral / noticed-feral / WAAAGH!; feral amassing migration and internal-scale WAAAGH! emergence; imperfect Imperial cull of noticed-feral Orks (gated on `Confirmed` intel and spare capacity); and WAAAGH!-as-beacon spawning unmapped Ork worlds in empty tiles plus reinforcing fleets. Spec: §4.22. Open question: terminal Ork-controlled world state (§6.8).

### 5.7 Post-0.7 Backlog

Documented for planning purposes; not scheduled:

**Content:** Dreadnoughts, Chaplains, Psykers, Chaos Troops, Necrons, Tau, Vehicles, Flying Units, Drop Pods, Fortifications, Relics, Poison Weapons, Geneseed Mutation, Power Armor Variants, The Inquisition. *(Orks are now scheduled — see §5.6.)*

**Enemy-generated diversions.** Give `FactionStrategyController` the ability to run its own diversion feints, rather than only being the target of the player's. Deferred from 0.7: it adds little to the 0.7 experience, and player/NPC order-structure symmetry — while desirable — is not blocking. Two distinct problems hide here, and they should be scoped separately:

- *NPC-vs-NPC feints* fit the existing turn loop with no changes: both the feinter and the fooled defender resolve within the same turn (shaping phase → faction planning), so this is purely a generation-heuristic addition to `FactionStrategyController`.
- *NPC-vs-player feints do not fit the current flow.* The diversion mechanic only fools a decision-maker who plans *after* the shaping phase; the player commits orders *before* `ProcessTurn` runs, and nothing consumes `PerceivedThreatBonus` on a player region (the player allocates garrisons by hand). A feint against the player therefore cannot reuse the AI's same-turn planning bonus — it must become a **one-turn-lagged intelligence deception**: the feint inflates the *displayed* enemy-strength estimate in the player's intel layer, persists past `ClearDiversionEffects` (unlike the transient AI bonus), and is acted on by the player the following turn, with the real attack landing then. This also implies an AI planning horizon that pairs a feint with a follow-up assault across turns — beyond the current per-region greedy `GenerateFactionOrders`. Resolve these (effect channel, persistence, what the player sees and when the deception resolves, AI feint+follow-through planning) before implementation.

**Strategic:** Diplomacy system, Space Combat and Boarding, Strategic Planetary Maps (regional types), Factional Fleets with independent movement, Sector Generation Customization (difficulty, era, story threads), Chapter Customization at start (founding legion, perks/disadvantages).

**Living Universe:** Imperial Guard movement and inter-planet logistics (Phase 4); Character personality development over time (Phase 5).

**Infrastructure:** Full mod support (XML data, moddable factions, name lists, planet data, chapter generation); data-layer decoupling to replace hardcoded rules-data display-name references with stable keys, validated registries, and data-driven rule profiles; Graphics overhaul; Full UX pass.

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

**Outcomes to define:** reduced combat effectiveness, forced retreat (covered withdrawal), rout (immediate disorderly exit from the map), or broken (squad cannot be assigned orders for a number of turns after the battle ends). The mechanics of covered withdrawal and rout are themselves resolved and specified under Battle Continuation in §4.14; what remains open here is what *triggers* a morale check and which of these outcomes results.

### 6.3 New Recruit Intake

This is a backlog item. The intended design is that the player has access to several different recruitment methods, each with distinct trade-offs:

- One method might yield more recruits but strain the source planet's population or reduce planetary morale.
- Achieving a high reputation with a planetary governor could unlock that governor offering recruitment rights on their world, as a relationship reward.
- Some Scout squads or Sergeants may be assigned to recruitment duties rather than training or deployment, representing the chapter's active effort to identify and select candidates.
- A dedicated scouting mission purely to find recruits is not planned; recruitment is expected to be managed through standing assignments and governor relationships rather than one-off missions.

The full design of the recruitment screen and its trade-off options is deferred to backlog.

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
- What are the consequences of a negative investigation result against the chapter? (Censure, requisition of assets, excommunication as an extreme outcome?)
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

---

## 7. Glossary

| Term | Definition |
|---|---|
| Battle Brother | A full Space Marine belonging to the player's chapter. |
| Battle Value | A numeric score representing the approximate combat power of a squad template. Used to generate balanced opposing forces. |
| Chapter | The player's Space Marine organization: nominally 1,000 Battle Brothers organized into ten companies. |
| Combat Pace | The jog movement tier. The soldier moves at half their MoveSpeed. Shooting is permitted but aiming is not. Entering jog speed or faster resets any accumulated aim state. |
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
| Inter-Faction Intelligence (Belief) | What an observer faction *believes* about a target faction's presence/strength in a region, graded by IntelLevel and allowed to diverge from ground truth in both directions (false negatives; paranoia/disinformation false positives). Stored state nudged by Detection, not a function of truth (§4.21). |
| IntelLevel | The fidelity of an inter-faction intelligence belief: `None | Rumor | Suspected | Confirmed | Located`. A targeted response (e.g. an Ork cull) requires `Confirmed` or higher (§4.21). |
| Insurrectionist Faction | A single sector-wide Conversion-growth Faction (like the Genestealer Cult faction) that recruits from discontented Imperial populations and contests regions through the normal combat systems. The mechanical embodiment of a revolt (§4.20). |
| Intelligence Level | A per-region value representing current information quality about enemy forces there. Decays over time. |
| Pop | (Proposed, §6.7) A subdivision of a region's population carrying its own loyalty/affiliation that drifts over time. Not implemented; raised as the possible long-term substrate unifying conversion, revolt, and corruption. |
| Mission | A specific operational objective assigned to one or more squads: Recon, Advance, Ambush, Assassination, Sabotage, Extermination, Defense, Patrol, Construction, or Diversion. |
| Movement Tier | One of four discrete movement states available to a soldier each battle turn: Stationary, Walk (1/5 MoveSpeed), Jog (1/2 MoveSpeed), or Run (full MoveSpeed). Tier determines shooting and aiming availability. |
| Order | The assignment of one or more squads to a Mission, specifying disposition and aggression level. |
| Order of Battle | The full hierarchical structure of the chapter: Chapter HQ → Companies → Squads → Marines. |
| OpFor | Opposing Force. Any non-player faction unit encountered in a mission or battle. |
| Orks | A fungal xenos faction (`UniversallyHostile | Indelible`) that cannot be eradicated from a region, grows inefficiently while feral, and coalesces into a WAAAGH! that erupts outward and draws more Orks to the sector (§4.22). |
| Universally Hostile | A Faction Behavior marking a faction as Hostile to every other faction regardless of stored stance, and unable to be set Neutral/Allied. Basis for Orks "fighting everyone" (§4.21). |
| WAAAGH! | The public, expanding Ork state: a Warboss has united a region's amassed Orks into an extra-territorial force that acts as a beacon, spawning unmapped Ork worlds and reinforcing fleets (§4.22). |
| Prone | A stance available to stationary soldiers. Only head and upper torso hit locations are valid ranged hit targets. Melee offense is heavily penalized; the soldier is significantly easier to hit in melee. A soldier can drop prone in one turn from any stance. Returning to crouching takes one turn; returning to standing takes two turns. A prone soldier cannot move. |
| Region | A sub-area of a planet. Each region has its own faction presences, garrison counts, intelligence level, and infrastructure ratings. |
| RegionFaction | The presence of a specific faction in a specific region, with its own population, garrison, organization, detection, entrenchment, and anti-air values. Holds the list of squads of that faction currently landed in the region. |
| Run | The fastest movement tier. The soldier moves at full MoveSpeed. No shooting or melee is permitted. Turning is restricted to 30 degrees per turn. |
| Spare Troops | The portion of a faction's organized military force in a region that exceeds the required garrison, available for offensive operations or construction. |
| Squad | A group of marines operating as a unit, assigned to a specific template that defines their role and composition. |
| Squad Template | The definition of a squad type: roles, minimum and maximum member counts, battle value, and permitted weapon options. |
| Stance | A soldier's body position when stationary: Standing, Crouching, or Prone. Stance affects which hit locations are valid ranged targets and applies melee combat modifiers. Transitions cost one turn each, except dropping prone (one turn from any stance). |
| Standing | The default upright stance. No ranged or melee modifiers apply. |
| Subsector Capital | The highest-importance planet in a subsector, determined during sector generation by an importance score (population size for 0.7; strategic classification is a post-0.7 addition to the score). The capital has established warp lanes to all other planets in its subsector. |
| Task Force | A grouping of ships within the chapter's fleet. |
| Turn | One in-game week. The smallest unit of strategic time. |
| Walk | The slowest movement tier above stationary. The soldier moves at 1/5 MoveSpeed. Shooting and aiming are both permitted. Accumulated aim state is preserved between walk turns. |
| Warp Lane | An established, well-traveled route between two planets through the Warp. Lane travel has lower transit time variance than charting a direct route. Within a subsector, all lanes radiate from the subsector capital. Cross-subsector lanes connect primarily between subsector capitals. |
| Wound Severity | A classification of how badly a hit location has been damaged: Negligible, Minor, Moderate, Major, Critical, Massive, Mortal, or Unsurvivable. |
