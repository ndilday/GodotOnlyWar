# OnlyWar — Product Requirements Document

**Version:** Alpha 0.7 (In Development)  
**Last Updated:** March 2026  
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
5. [Release Scoping](#5-release-scoping)
   - 5.1 [Released (Alpha 0.6 and prior)](#51-released-alpha-06-and-prior)
   - 5.2 [Alpha 0.7 — Committed](#52-alpha-07--committed)
   - 5.3 [Alpha 0.7 — To-Do](#53-alpha-07--to-do)
   - 5.4 [Alpha 0.7 — Stretch](#54-alpha-07--stretch)
   - 5.5 [Post-0.7 Backlog](#55-post-07-backlog)
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
- Displays the marine's personal history log: recruitment, training, promotions, notable actions, wounds received, and awards.
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
- Cybernetic replacements are available as a treatment option for marines with severed or permanently crippled limbs (requires Techmarine availability and a time cost).

---

### 4.9 Recruiter Screen

**Description.** Manages the chapter's Scout Company and the pipeline from initiate to Battle Brother.

**Acceptance Criteria (Implemented):**
- Displays all Scout squads with their current members.
- Indicates which scouts are ready to advance to full Battle Brother status, based on their training evaluations.

**Acceptance Criteria (Planned — 0.7 To-Do):**
- Scouts who are currently deployed on a mission are excluded from training progress that week.
- The player can designate a focus for a given Scout squad's training (e.g., prioritizing ranged skill vs. melee skill vs. leadership), which affects which skills accumulate points faster during that week.

**Acceptance Criteria (Planned — Post-0.7):**
- The Recruiter can report when a Sergeant feels he has nothing further to teach a scout in a given area.
- The player can initiate a new intake of potential recruits from chapter-held worlds.
- The Armory allows the designation of potential Techmarines to be sent to Mars for training.

---

### 4.10 Battle Review Screen

**Description.** A post-battle replay screen that allows the player to review the turn-by-turn progression of a completed engagement.

**Acceptance Criteria:**
- Displays a 2D grid showing the positions of all squads at each turn of the battle.
- Player squads are shown with a distinct icon and color; opposing squads with a different icon and their faction color.
- A text log panel displays all actions taken during the currently displayed turn (movement, shots fired, hits landed, wounds inflicted).
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

---

### 4.13 Turn Simulation — Mission Resolution

**Description.** The rules governing how player-issued and AI-generated orders are executed each turn.

**Acceptance Criteria:**

**Order Assignment**
- The player can assign a squad to one of the following mission types: Recon, Advance (assault), Ambush, Assassination, Sabotage, Extermination, Defense, or Patrol.
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
- Available actions: move (at current speed toward a destination), fire (accuracy dependent on current speed), aim (stationary only, accumulates accuracy bonus), charge into melee, melee attack, reload, or ready a weapon.
- A unit always has at least its base move available. Each consecutive turn it moves, its current speed increases by its acceleration value up to its top speed. Stopping decelerates at the same rate.
- When moving faster than base move, a unit can turn no more than 30 degrees per turn.
- Ranged accuracy penalties scale with current speed: the existing moving penalty applies at base move; above base move the penalty is 3× weapon bulk, making shooting at sprint speed practically ineffective.

**Ranged Combat**
- Hit probability is derived from the shooter's ranged skill, the target's range, the target's physical size, and the cover modifier of the target's squad.
- A successful hit determines a struck location via the target's hit location probability table.
- Damage after armor reduction is applied to that location at the appropriate wound severity.
- Weapons have a rate of fire. Firing multiple times in a turn incurs an accuracy penalty for each shot after the first.
- Firing a two-handed weapon with one hand (due to an injured arm) incurs an additional accuracy penalty.

**Melee Combat**
- Squads that close to melee range engage in hand-to-hand combat.
- Melee hit probability is derived from the attacker's melee skill, the defender's melee skill, and the weapons involved.
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

**Description.** Planetary governors are named characters with personalities that influence how they interact with the chapter. The Inquisition also issues requests to the chapter (see Section 6.10); the mechanics below apply to governor requests specifically.

**Acceptance Criteria:**

**Personality**
- Each governor has five personality traits: Investigation (likelihood of detecting hidden threats), Paranoia (likelihood of imagining false threats), Neediness (likelihood of requesting chapter aid), Patience (how long they wait before their opinion degrades from an ignored request), and Appreciation (how much their opinion improves when a request is fulfilled).

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
- A request is fulfilled when at least one player squad lands on the planet surface, regardless of the squad's assigned orders.
- Fulfillment improves the governor's opinion.
- Ignoring a request degrades the governor's opinion over time according to their Patience trait.

**Governor Replacement**
- Governor replacement is not yet implemented.
- When implemented: governors age each year and are replaced upon reaching an advanced age.
- Currently, newly generated governors are assigned a random initial opinion of the chapter. This is a placeholder; the intended long-term behavior (post-0.7) is that a new governor's starting opinion should be informed by the previous governor's final opinion of the chapter and the chapter's general reputation in the sector, rather than being independent of history.

---

### 4.17 Fleet Management

**Description.** The chapter's fleet consists of ships organized into task forces. Ships transport squads between locations in the sector. Travel between systems occurs through the Warp, which is inherently unpredictable.

**Acceptance Criteria (Implemented):**
- The chapter fleet is organized into task forces, each composed of one or more named ships.
- Each ship has a defined troop capacity measured in individual marines.
- Squads can be loaded onto ships or landed in planetary regions via the Planet Detail Screen.
- A ship cannot be loaded beyond its capacity.
- Ship capacity is correctly reduced by the size of squads currently loaded.

**Acceptance Criteria (Planned — 0.7 To-Do):**
- Ships can be ordered to move between planets via the task force menu in the Galaxy View.
- The movement dialog presents available warp lane routes to the destination. If no established lane connects the origin to the destination directly, the route is composed of lane hops through intermediate planets.
- Where a direct route would bypass the established warp lane network and likely be faster in distance terms, the dialog additionally offers a "Chart Direct Route (Risky)" option. This option carries higher transit time variance than lane travel.
- A task force in transit is displayed on the Galaxy View at a position along its route, with a line connecting it to the destination planet.
- Transit time is variable. The player is shown an estimated arrival range when plotting a course, not a guaranteed date.
- A task force in transit cannot be loaded or unloaded until it arrives.
- On arrival, the task force's position is updated to the destination planet.
- Fleet positions are saved and restored correctly across save/load.

---

### 4.18 Save and Load

**Description.** The player can save their campaign at any time and resume it later.

**Acceptance Criteria:**
- The player can save the game from the top menu bar.
- The player can load a saved game from the main menu.
- All game state is preserved across save/load: sector state, all faction populations and garrisons, all marines (attributes, skills, wounds, history, kill records), all squad assignments, fleet positions, loaded squads, active orders, active governor requests, game date, and chapter battle history.
- Loading a save produces a game state identical to the state at the time of saving.

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

The following must ship in 0.7:

- **Training for non-deployed forces:** Non-deployed marines accumulate training skill points each turn.
- **Game Start Phase 1:** Complete new game setup screen and flow.
- **Planet View Phase 4 completion:** Governor aging and replacement; visible opinion signal on planet screen; request fulfillment requiring meaningful engagement.
- **Diplomacy/Requests display:** Active governor requests visible in a dedicated screen or panel.
- **Fleet movement:** Ships can be ordered to move between planets; transit time applies.

### 5.3 Alpha 0.7 — To-Do

Targeted for 0.7, not yet committed:

- **Battle screen visual overhaul:** Updated visual presentation of the battle UI.
- **Recruiter Screen Phase 2:** Deployed scouts excluded from training; squad-specific training focus.
- **Strategic Layer Phase 2:**
  - Population growth relative to planet carrying capacity (faster growth when underpopulated, slower when near capacity).
  - Garrison attrition (0.1% of garrison retires per week, requiring replacement from population growth).
  - OpFor fog of war, recon orders, and special missions.
  - Diversion missions.
  - Player-constructable fortifications (Entrenchments, Listening Posts, Anti-Air batteries).
  - Burrowing and camouflage as ambush tactics.
- **Subsector warp lanes:** Each subsector has a capital world, determined by an importance score (population size and strategic classification). The capital has established warp lanes to all other planets in the subsector. Cross-subsector lanes connect primarily between subsector capitals. Lanes are generated during sector creation. The fleet movement dialog routes along lanes by default and offers a "Chart Direct Route (Risky)" option where applicable.
- **Tyranid Infiltration Units:** Lictor and Ravener content data.

### 5.4 Alpha 0.7 — Stretch

To be drawn from if capacity allows:

- **Apothecary Phase 2:** Cybernetic replacements; geneseed recovery noted in death records; recovery time displayed on squad screen.
- **Living Universe Phase 3B — Revolt:** Revolutionary population mechanic; evidence-based requests.
- **Battle Logic Phase 4:** Grenades, AoE weapons, flamers, morale, sprint/fire tradeoff, retreat mechanic, post-battle loadout recalculation.
- **Battle Visuals Phase 3:** Terrain and cover representation; line of sight; elevation-based fire advantage.
- **UX Improvement Phase 1:** Drag-and-drop where applicable; squad row redesign; zoom-adaptive planet name labels.
- **Mission System Expansion:** Talent recruitment missions; IG support missions; Chaos cult investigation; STC hunt; prisoner recovery.

### 5.5 Post-0.7 Backlog

Documented for planning purposes; not scheduled:

**Content:** Dreadnoughts, Chaplains, Psykers, Chaos Troops, Orks, Necrons, Tau, Vehicles, Flying Units, Drop Pods, Fortifications, Relics, Poison Weapons, Geneseed Mutation, Power Armor Variants, The Inquisition.

**Strategic:** Diplomacy system, Space Combat and Boarding, Strategic Planetary Maps (regional types), Factional Fleets with independent movement, Sector Generation Customization (difficulty, era, story threads), Chapter Customization at start (founding legion, perks/disadvantages).

**Living Universe:** Imperial Guard movement and inter-planet logistics (Phase 4); Character personality development over time (Phase 5).

**Infrastructure:** Full mod support (XML data, moddable factions, name lists, planet data, chapter generation); Graphics overhaul; Full UX pass.

---

## 6. Open Design Questions

The following require design decisions before their associated features can be implemented. Resolving these is the expected focus after the initial PRD and TDD drafts are locked.

### 6.1 Governor Request Fulfillment — RESOLVED

For the current `PresenceRequest` type: a request is fulfilled when at least one player squad lands on the planet surface, regardless of the squad's assigned orders or mission target. A squad landing for an entirely unrelated reason satisfies the request.

Future request types (e.g., investigate a region, eliminate a specific threat) will define their own fulfillment conditions when they are designed. This is a backlog item pending the broader mission system expansion.

### 6.2 Aggression Axis Split — DEFERRED

The single `Aggression` axis (Avoid → Cautious → Normal → Attritional → Aggressive) currently conflates two behaviors: willingness to seek contact and willingness to absorb casualties before withdrawing. Splitting into two axes would allow behaviors like "seek contact but withdraw readily on losses" or "avoid contact but hold ground if engaged."

This split is deferred indefinitely. Aggression is a per-order, per-mission setting with no mid-mission adjustment. The AI generates orders with hardcoded aggression values per order type (Normal for assaults, Cautious for patrols, Avoid for construction). No concrete playtesting scenario has been identified where the single axis produces wrong behavior. If such a scenario emerges, the question should be reopened at that time.

### 6.3 Morale

**Question:** What triggers a morale check and what are its outcomes?

**Agreed triggers to consider:**
- Casualty threshold: losing a significant percentage of the squad's strength in a single turn.
- Death of the squad leader.
- Being significantly outnumbered.

**Outcomes to define:** reduced combat effectiveness, forced retreat (covered withdrawal), rout (immediate disorderly exit from the map), or broken (squad cannot be assigned orders for a number of turns after the battle ends).

### 6.4 Retreat Mechanics — RESOLVED

Two distinct retreat modes:

**Covered Withdrawal** (player-ordered):
- The withdrawing squad moves away from the enemy at jog speed each turn, shooting as it goes.
- Withdrawal fire uses normal shooting rules and counts toward the pursuer's casualty threshold for the purpose of triggering the pursuer's own withdrawal check.
- The withdrawing squad remains on the map until it reaches the map edge, at which point it exits.

**Rout** (morale-triggered, see 6.3):
- The routing squad moves away at run speed each turn with no shooting.
- The routing squad remains on the map until it reaches the map edge.

**Pursuit:**
- The non-withdrawing/routing force continues to act normally each turn: moving toward and firing at the fleeing squad according to their own aggression setting.
- Pursuit tenacity is governed by the pursuing force's aggression setting, using the same casualty thresholds that govern normal battle continuation. An Aggressive force pursues until it cannot reach or shoot the fleeing squad, or until its own casualties trigger its withdrawal threshold. An Avoid force does not pursue at all.
- Combat ends when the two forces are outside of mutual shooting range and at least one side is fully withdrawing or routing. There is no need for both forces to exit the map.

**Special cases:**
- Units capable of burrowing or flight may disengage immediately, bypassing the normal withdrawal sequence.
- Pursuit and withdrawal fire interact with the aggression-based withdrawal check: a pursuer that takes sufficient casualties from withdrawal fire may itself begin to withdraw, ending pursuit.

### 6.5 Movement Tiers and Stance — RESOLVED

This item replaces the earlier momentum-based sprint design. Movement is modeled as four discrete tiers, and stance is a separate tracked state that interacts with both ranged and melee combat.

---

**Movement Tiers**

Each tier defines a speed fraction of the soldier's `MoveSpeed` value and determines what combat actions are available that turn.

| Tier | Speed | Aim | Shoot | Melee | Notes |
|---|---|---|---|---|---|
| Stationary | 0 | Yes | Yes | Yes | Stance effects apply |
| Walk | 1/5 MoveSpeed | Yes | Yes | Yes | Aim state is preserved between walk turns |
| Jog | 1/2 MoveSpeed | No | Yes (no aim bonus) | Yes | Entering jog or faster resets any accumulated aim |
| Run | Full MoveSpeed | No | No | No | Turning restricted to 30 degrees per turn |

A soldier may change movement tier freely each turn with no transition cost, with the exception that a crouching or prone soldier must return to standing before moving (see Stance below).

Shooting while running is not permitted in the initial implementation. A "shooting at massive penalties while running" variant may be added in a later pass.

---

**Stance**

Stance is only mechanically relevant when a soldier is stationary. It represents the soldier's body position and affects both hit probability (ranged) and melee combat effectiveness.

| Stance | Transition Cost | Ranged (incoming) | Melee offense | Melee defense |
|---|---|---|---|---|
| Standing | — | Baseline | Baseline | Baseline |
| Crouching | 1 turn | Reduced | Penalty | Bonus to attacker |
| Prone | 1 turn from standing | Further reduced | Further penalty | Further bonus to attacker |

**Transition rules:**
- Standing → Crouching: 1 turn
- Standing → Prone: 1 turn (can drop directly)
- Crouching → Standing: 1 turn
- Crouching → Prone: 1 turn
- Prone → Crouching: 1 turn (only available transition from prone)
- Prone → Standing: 2 turns (must pass through crouching)

A crouching or prone soldier must return to standing before they can walk, jog, or run. Changing stance and moving in the same turn is not permitted.

**Ranged modifier implementation:** Rather than a flat accuracy penalty, stance filters the valid hit locations before the hit location probability roll. Locations that are not exposed due to crouch or prone position are excluded from the roll. This ties the defensive benefit directly to the existing hit location system and scales naturally with the target's body template without requiring per-species tuning.

- Crouching: lower body locations (legs, feet) are not valid hit targets.
- Prone: only locations visible from ground level (head, upper torso depending on orientation) are valid hit targets.
- The specific location sets to exclude will be defined per body template during implementation.

**Melee modifier implementation:** The specific penalty and bonus values for crouching and prone in melee will be determined during implementation. The magnitude is double for prone relative to crouching (i.e., if crouching carries a -2 melee offense penalty, prone carries -4).

---

**Future considerations (not in scope for current implementation):**
- A prone soldier with a disabled leg or foot automatically transitions to prone and cannot stand until the injury is treated or a cybernetic is fitted.
- A full-tilt charge as a distinct action that converts accumulated running momentum into a melee bonus.
- Accidental collisions when a running unit enters a cell occupied by an unexpected obstacle or unit.

### 6.6 Cybernetic Replacements — RESOLVED

**Eligibility:** Any hit location that has reached its cripple threshold but not its lethal threshold is a candidate for cybernetic replacement. This includes limbs (arms, hands, legs, feet) and vital locations (head, torso) at crippling severity.

**Requirements:** Both a Techmarine and an Apothecary must be available (present in the chapter and not deployed) for a cybernetic procedure to be performed. Both are consumed for the duration of the procedure.

**Cost:** A combination of time (weeks in the Apothecarium) and chapter resources. Specific values to be determined during implementation.

**Restored capability:** Cybernetic replacements restore full capability to the replaced location in the current implementation. More granular effects — penalties or bonuses to specific attributes, embedded weapon options — are a post-0.7 design item.

**Dreadnought interment:** For soldiers with multiple severe or unsurvivable injuries, interment in a Dreadnought chassis is an alternative to conventional treatment. This is out of scope until Dreadnoughts are implemented, but the cybernetics system should be designed with this pathway in mind — interment is the extreme end of the same decision space, not a separate system.

### 6.7 Sergeant Training Cap — RESOLVED

**Decided design:**
- A Sergeant's own skill level in a category acts as a hard cap on how far he can train a scout in that category. A scout cannot be trained beyond his instructor's level.
- Soldier ratings are updated every four turns (four weeks). The sergeant cap notification surfaces each time ratings are updated and a scout remains at his Sergeant's instructional limit in one or more skills.
- The player has three options when a scout hits his cap:
  1. Leave the scout in the squad and accept no further improvement in the capped skill.
  2. Transfer the scout to a different Scout squad with a Sergeant who has a higher skill level in that area.
  3. Promote the scout to a line squad, accepting that his development will continue through deployment and combat experience rather than structured training.

### 6.8 New Recruit Intake

This is a backlog item. The intended design is that the player has access to several different recruitment methods, each with distinct trade-offs:

- One method might yield more recruits but strain the source planet's population or reduce planetary morale.
- Achieving a high reputation with a planetary governor could unlock that governor offering recruitment rights on their world, as a relationship reward.
- Some Scout squads or Sergeants may be assigned to recruitment duties rather than training or deployment, representing the chapter's active effort to identify and select candidates.
- A dedicated scouting mission purely to find recruits is not planned; recruitment is expected to be managed through standing assignments and governor relationships rather than one-off missions.

The full design of the recruitment screen and its trade-off options is deferred to backlog.

### 6.9 Imperial Guard Interactions

**Decided design:**

- IG units are visible on all relevant map levels (galaxy view, planet view, region view) as distinct military forces separate from PDF.
- The player can request support from IG or PDF commanders. The outcome of the request depends on the requesting character's opinion of the chapter and whether they assess their forces as having more pressing duties elsewhere.
- Named individual characters outside the chapter are limited to governors, admirals, and generals. There are no chapter-monitored individuals at a lower level of granularity than these command-level figures.
- IG armies and fleets operate independently. They move between planets, conduct operations, reinforce or destabilize worlds, and respond to threats without requiring player involvement. The sector continues to function without the player's hand in everything.

**Remaining open sub-questions:**
- What is the request UI flow for IG/PDF support? (Is it a dialog from the planet or region screen, or something triggered through a governor character interaction?)
- What specific support types can be requested? (Reinforcing a region, providing fire support to a player-led assault, holding a position while the chapter deploys elsewhere.)

### 6.10 Inquisition Role

**Decided design:**

- The Inquisition is the primary investigative force for xenos and chaos influence across the sector. They operate independently, conducting their own investigations and responding to threats.
- Inquisitors are a source of requests directed at the player's chapter, similar in structure to governor requests but with a different authority and tone. Inquisitor requests may include: investigate a region for xenos influence, purge a world, support an ongoing investigation with military force.
- The Inquisition may also investigate the chapter itself for signs of chaos taint or gene-seed corruption. This creates a threat relationship alongside the cooperative one.

**Remaining open sub-questions:**
- What are the consequences of refusing or failing Inquisition requests, versus governor requests? The Inquisition carries considerably more authority.
- What are the consequences of a negative investigation result against the chapter? (Censure, requisition of assets, excommunication as an extreme outcome?)
- Does the player interact with a specific named Inquisitor character, or is the Inquisition an anonymous institutional force?

### 6.11 Warp Travel Time Model — RESOLVED

**Decided design:**

**Sector Scale**
- The sector grid is 200×200 light years. Each grid unit is 1×1 light year.
- A subsector has a maximum diameter of 20 light years, typically containing 2–8 star systems within a 10 light year radius.

**Warp Lanes**
- Each subsector has a designated capital world, determined during sector generation by an importance score. For 0.7, the score is based on population size alone. Strategic classification (Hive World, Forge World, etc.) will be incorporated as a post-0.7 addition.
- The capital world has an established warp lane to every other planet within its subsector.
- Across subsectors, established warp lanes connect primarily between subsector capitals. Additional cross-subsector lanes may exist between high-importance non-capital planets near subsector boundaries.
- Warp lanes are generated as part of sector generation.

**Transit Time Formula**
- Distance between two planets is computed as Euclidean distance in light years from their grid coordinates.
- Base transit time per lane hop: `max(1, round(sqrt(hopDistanceInLightYears) × 0.5))`
- Two routes are always computed and compared:
  1. **Lane route:** The shortest path through the warp lane graph (Dijkstra weighted by hop distance). Total base time is the sum of per-hop base times.
  2. **Direct route:** Straight Euclidean distance from origin to destination using the same formula, but treated as a single uncharted hop.
- The route with the lower total base time is selected automatically in 0.7. Player agency over route selection is a post-0.7 refinement.
- Variance is applied to the selected route after comparison:
  - Lane route: ±1 turn (uniform)
  - Direct route: -1 to +5 turns (skewed toward longer outcomes), with a 10% chance of 2–3× the base time representing a severely disrupted passage
- The player is shown an estimated arrival range derived from the selected route's variance band rather than a guaranteed date.

This routing model produces correct behavior naturally: same-subsector planets are usually reached faster by direct route (skipping the capital hop); cross-subsector journeys through well-connected capitals usually favor the lane route.

Example ranges for reference:

| Journey type | Typical base turns | Equivalent days | Canonical target |
|---|---|---|---|
| Same subsector, direct (≤20 ly) | 1–2 | 7–14 days | 5–10 days |
| Adjacent subsector via capitals (~2–3 hops) | 2–4 | 14–28 days | 12–30 days |
| Cross-sector via lane graph | 4–8 | 28–56 days | 33–60 days |

- Transit time represents warp time (crew experience). Crew aboard ships in transit accumulate training and healing at the same rate as those on the ground.
- Real-space time elapsed during transit is not tracked separately. The end-of-turn simulation runs normally each week regardless of ship position.

**Orbit-to-Jump-Point Transit**
- The ~2-week transit from planetary orbit to the Warp jump point (and vice versa) is folded into the overall travel time and not modeled separately in 0.7. This may be revisited as a post-0.7 addition for additional strategic depth.

**Warp Storms**
- Deferred post-0.7. The direct route long-tail variance covers disrupted passages for now.

---

### 6.12 Navis Nobilite Relations (Post-0.7 Backlog)

Space Marine chapters obtain Navigators through formal pacts with the Navis Nobilite, the ancient mutant Navigator houses based on Terra. These pacts are centuries-long relationships, with specific Navigator families assigned to specific chapters across generations.

The chapter's relationship with its assigned Navigator house is a candidate for a tracked reputation value, analogous to governor opinion. Better relations would result in a more skilled Navigator being assigned, translating to reduced travel time variance on lane routes and potentially shorter base durations. Poor relations could result in a less capable Navigator, increasing variance and extending journeys.

This is a post-0.7 design item. Navigator quality should be designed as a chapter-level modifier to the transit time formula rather than as an individually tracked NPC, unless the decision is made to model Navigators as named characters in a future pass.

---

## 7. Glossary

| Term | Definition |
|---|---|
| Battle Brother | A full Space Marine belonging to the player's chapter. |
| Battle Value | A numeric score representing the approximate combat power of a squad template. Used to generate balanced opposing forces. |
| Chapter | The player's Space Marine organization: nominally 1,000 Battle Brothers organized into ten companies. |
| Combat Pace | The jog movement tier. The soldier moves at half their MoveSpeed. Shooting is permitted but aiming is not. Entering jog speed or faster resets any accumulated aim state. |
| Crouching | A stance available to stationary soldiers. Lower body hit locations are excluded from ranged hit rolls. Melee offense is penalized; the soldier is easier to hit in melee. Requires one turn to enter or exit. A crouching soldier must stand before moving. |
| Disposition | The tactical posture of a squad on an order: Mobile (active operations) or Dug In (defensive). |
| Faction | Any organized force in the sector: Space Marines, Tyranids, Genestealer Cults, Imperial PDF, etc. |
| Garrison | The number of troops a faction keeps assigned to defending a specific region, as distinct from troops available for offensive operations. |
| Genestealer Cult (GC) | A hidden faction that infects and converts a planet's population, growing covertly until strong enough to reveal itself. |
| Geneseed | The biological material (progenoid glands) harvested from Space Marines. Required to create new initiates. |
| Governor | A named NPC character who leads an imperial-aligned planet's civilian and military administration. Has personality traits that affect requests and opinion. |
| Hit Location | A specific body part (head, torso, arm, leg, etc.) that can be individually wounded, crippled, or severed. |
| Intelligence Level | A per-region value representing current information quality about enemy forces there. Decays over time. |
| Mission | A specific operational objective assigned to one or more squads: Recon, Advance, Ambush, Assassination, Sabotage, Defense, Patrol, or Construction. |
| Movement Tier | One of four discrete movement states available to a soldier each battle turn: Stationary, Walk (1/5 MoveSpeed), Jog (1/2 MoveSpeed), or Run (full MoveSpeed). Tier determines shooting and aiming availability. |
| Order | The assignment of one or more squads to a Mission, specifying disposition and aggression level. |
| Order of Battle | The full hierarchical structure of the chapter: Chapter HQ → Companies → Squads → Marines. |
| OpFor | Opposing Force. Any non-player faction unit encountered in a mission or battle. |
| Prone | A stance available to stationary soldiers. Only head and upper torso hit locations are valid ranged hit targets. Melee offense is heavily penalized; the soldier is significantly easier to hit in melee. A soldier can drop prone in one turn from any stance. Returning to crouching takes one turn; returning to standing takes two turns. A prone soldier cannot move. |
| Region | A sub-area of a planet. Each region has its own faction presences, garrison counts, intelligence level, and infrastructure ratings. |
| RegionFaction | The presence of a specific faction in a specific region, with its own population, garrison, organization, detection, entrenchment, and anti-air values. Holds the list of squads of that faction currently landed in the region. |
| Run | The fastest movement tier. The soldier moves at full MoveSpeed. No shooting or melee is permitted. Turning is restricted to 30 degrees per turn. |
| Spare Troops | The portion of a faction's organized military force in a region that exceeds the required garrison, available for offensive operations or construction. |
| Squad | A group of marines operating as a unit, assigned to a specific template that defines their role and composition. |
| Squad Template | The definition of a squad type: roles, minimum and maximum member counts, battle value, and permitted weapon options. |
| Stance | A soldier's body position when stationary: Standing, Crouching, or Prone. Stance affects which hit locations are valid ranged targets and applies melee combat modifiers. Transitions cost one turn each, except dropping prone (one turn from any stance). |
| Standing | The default upright stance. No ranged or melee modifiers apply. |
| Subsector Capital | The highest-importance planet in a subsector, determined during sector generation by a score derived from population size and strategic classification. The capital has established warp lanes to all other planets in its subsector. |
| Task Force | A grouping of ships within the chapter's fleet. |
| Turn | One in-game week. The smallest unit of strategic time. |
| Walk | The slowest movement tier above stationary. The soldier moves at 1/5 MoveSpeed. Shooting and aiming are both permitted. Accumulated aim state is preserved between walk turns. |
| Warp Lane | An established, well-traveled route between two planets through the Warp. Lane travel has lower transit time variance than charting a direct route. Within a subsector, all lanes radiate from the subsector capital. Cross-subsector lanes connect primarily between subsector capitals. |
| Wound Severity | A classification of how badly a hit location has been damaged: Negligible, Minor, Moderate, Major, Critical, Massive, Mortal, or Unsurvivable. |
