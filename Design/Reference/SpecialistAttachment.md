# Specialist Attachment — Order-Level Attachment of Individuals

Status: **IMPLEMENTED — historical reference record.** Moved from `Active/` to `Reference/` on 2026-08-07.
Produced to answer `CasualtyRealism.md` §3.1, which decided order-level attachment but left the
sub-questions open and asked for a doc of its own; built as Phase 2a of that plan.

The current source of truth is `OnlyWar_TDD.md` §5.3, §5.6, and §6.10. The original `OrderSoldier`
table and direct attachment pointers described in the implementation record below were superseded by
the persisted `IndividualPosting` model in save format 13; the historical details remain only for the
resolved design rationale and load-ordering trap.

Retained rather than distilled away for three things that stay useful: the resolved sub-question
table in §4, the rationale in §2.2 for carrying the detachment flag as a `SquadTypes` bit rather
than a new column, and the load-ordering trap in §6.3 (`PlayerSoldier`'s constructor evicts the base
`Soldier` from its squad, so attachment hydration must run after soldiers load and be pinned by a
reference-equality assertion). The shipped architecture is summarized in `OnlyWar_TDD.md` §5.6.

**Historical corrections from the original implementation**, recorded so this document is not read as
the final word:
- §3.1's four-argument `CanAttach` could not work as written — order issue must validate *before* the
  `Order` object exists — so an overload taking the staging squads explicitly was added, and the
  documented signature delegates to it.
- The original save-format version had to be bumped (`SaveFormat.CurrentVersion` 5 → 6). §6 says
  correctly that the writer recreates the schema on every save and therefore needs no migration; that
  reasoning does **not** extend to the reader. The later format-13 posting redesign replaced the
  historical `OrderSoldier` table; see PRD §4.18 / TDD §4.2.

Scope: attachment as a purely **organizational** concept. An attached specialist is *with* the force,
not *in* the engagement — no battlefield presence, no battle-time squad binding, cannot become a
casualty. That defers the whole "characters as units of one" problem (Phase 2c).

---

## 0. Ground truth established by reading the code

- `Order` is `Models/Orders/Order.cs` — 5 members, two constructors (`:31` runtime, `:34` load), one
  invariant in the ctor: every squad in `AssignedSquads` gets `squad.CurrentOrders = this`
  (`:46-52`). `AssignedSquads` is a mutable `List<Squad>` handed in by the caller and mutated in
  place by `OrderAssignment`.
- Save files are **recreated from scratch on every save** (`Helpers/Storage/GameStorage.cs:30`,
  `OnlyWar_TDD.md:196`), so there is **no save migration** — only a new `CREATE TABLE` in
  `Database/SaveStructure.sql`.
- The rules DB `SquadTemplate` is read with `SELECT *` and **positional** indexing
  (`Helpers/Database/GameRules/SquadTemplateDataAccess.cs:436-470`, cols 0-7 with a
  `reader.FieldCount > 7` guard), so appending a column is safe — but so is reusing the existing
  `SquadType` integer, which is cheaper.

---

## 1. Model changes

### 1.1 `Order.AttachedSoldiers`

```csharp
public List<PlayerSoldier> AttachedSoldiers { get; } = [];
```

**Initialize in the field declaration, not as a constructor parameter:**
- Every construction site stays untouched — `OrderAssignment.cs:88`, `UnitDataAccess.cs:131`,
  `FactionStrategyController` (NPC orders), and ~8 test sites.
- Non-null on *every* order including NPC ones, which Phase 2b iterates unconditionally.
- Mutation goes through one service only, so a ctor param would be a second, bypassable path.

Typed `PlayerSoldier`, not `ISoldier`: only player specialists attach, and Phase 2b needs
`SoldierEvaluation.MedicalRating` (`Models/Soldiers/SoldierEvaluation.cs:21`), which lives on
`PlayerSoldier`.

### 1.2 Soldier-side backpointer

`Models/Soldiers/PlayerSoldier.cs`:

```csharp
public Order AttachedOrder { get; set; }

public Region EffectiveRegion =>
    AttachedOrder?.Mission?.RegionFaction?.Region ?? AssignedSquad?.CurrentRegion;
```

**Do not put `AttachedOrder` on `ISoldier`.** `ISoldier` is implemented by `Soldier`,
`PlayerSoldier`, and test doubles; attachment is a player-chapter concept, and `PlayerSoldier`
already carries player-only state (`GeneticCompatibility`, awards, evaluations).

`EffectiveRegion` is cheap insurance for Phase 2b — see §7 trap 2.

### 1.3 New service — `Helpers/Orders/OrderAttachment.cs`

Modelled on `Helpers/Orders/OrderAssignment.cs` (static, reads `GameDataSingleton.Instance.Sector`
only where it must). Owns **both sides** of the pointer pair so nothing can half-attach:

- `Attach(PlayerSoldier, Order)` / `Detach(PlayerSoldier)` / `ReleaseAll(Order)`
- `CanAttach(PlayerSoldier, Order, Region originRegion, out string reason)` — §3 validation
- `bool IsAttachedElsewhere(Squad squad, Order target)` — the reverse guard

---

## 2. The squad-template detachment flag

### 2.1 Actual schema (verified with the SQLite CLI)

```
CREATE TABLE "SquadTemplate" (
  "Id" INTEGER NOT NULL UNIQUE, "FactionId" INTEGER NOT NULL, "Name" STRING NOT NULL,
  "DefaultArmorId" INTEGER NOT NULL, "DefaultWeaponSetId" INTEGER NOT NULL,
  "SquadType" INTEGER NOT NULL, "BodyguardSquadTemplateId" INTEGER,
  LeaderWorkExperienceProfileId INTEGER, PRIMARY KEY("Id"), ...);
```

Player-faction rows and their current `SquadType`:

```
 0 Veteran Squad 4 | 1 Tactical 0 | 2 Assault 8 | 3 Devastator 16 | 4 Scout 2
 5 Veteran HQ 5 | 6 HQ 1 | 7 Scout HQ 3 | 19 Chapter HQ 1
 8 Armory 0 | 9 Librarius 0 | 10 Apothecarion 0 | 11 Reclusium 0
```

**The trap:** Armory/Librarius/Apothecarion/Reclusium are `SquadType = 0`, identical to Tactical
Squad. The flag cannot be derived from existing data — it must be authored.

### 2.2 Decision: a new `SquadTypes` flag bit in the rules DB, not a new column

Add `PermitsIndividualDetachment = 0x80` to the `[Flags] SquadTypes` enum in
`Models/Squads/SquadTemplate.cs:10-24`. (`0x40 Administrative` is the highest in use, and it is only
ever set at runtime — **no DB row carries 64**, verified.)

Why the bit rather than a column:
- **Zero loader change.** `SquadTemplateDataAccess.cs:445` already casts col 5 to `SquadTypes`. A new
  column means a new positional read, a new `SquadTemplate` ctor parameter, and every test-factory
  call site (`TestModelFactory.cs:237`, `MissionDayBudgetTests.cs:200`, `EndTurnPreflightTests.cs:389`,
  `SoldierTransferServiceTests.cs:817`, …).
- **Safe.** Every `SquadType` consumer (40+) is a `&`-mask test — `ForceGenerator.cs:143`,
  `ChapterController.cs:977-994`, `ApothecariumMedicalRecordBuilder.cs:608-614`, `UnitTemplate.cs:31`,
  `Unit.cs:28`, … There is no equality comparison and no exhaustive switch, so a new bit cannot
  change any existing branch. `FleetScreenController.GetSquadTypeOrder`
  (`Scenes/FleetScreen/FleetScreenController.cs:253`) orders by template **Id**, not type value.
- `SquadTypes` is already the vocabulary for formation classification and already carries a
  capability bit (`Administrative`, which gates `IsOperational`).

Derived predicate beside `IsOperational` (`Models/Squads/SquadTemplate.cs:61`):

```csharp
public bool PermitsIndividualDetachment =>
    (SquadType & SquadTypes.PermitsIndividualDetachment) != 0;
```

### 2.3 Migration — `Database/RulesMigration_SpecialistDetachment.sql` (new)

```sql
-- Specialist detachment (rules database).
--
-- Order-level specialist attachment (Design/Reference/SpecialistAttachment.md) lets an individual be
-- attached to an operation without his squad. Only formations whose function is to SUPPLY
-- specialists to other formations may give up a member: the four command HQ templates and the four
-- chapter offices. A line squad's strength is its cohesion, so it is all-or-nothing.
--
-- The flag is two-sided: a formation that may give up individuals is ALSO a formation that never
-- deploys as a unit (see §3.3). These eight become personnel pools; their people reach the field
-- only by attachment. This is a behavior change for the four chapter offices and the four HQ
-- squads, all of which are ordinary deployable squads today.
--
-- Carried as SquadTypes.PermitsIndividualDetachment = 0x80 inside the existing SquadType bitfield
-- rather than a new column: every consumer of SquadType is a bit test (verified), the positional
-- SELECT * loader in SquadTemplateDataAccess is unaffected, and 0x40 (Administrative) is the
-- highest bit any row uses today.
--
-- Idempotent: bitwise OR, safe to re-run.

UPDATE SquadTemplate
SET SquadType = SquadType | 128
WHERE FactionId = 1
  AND Id IN (
        5,   -- Veteran HQ Squad
        6,   -- HQ Squad
        7,   -- Scout HQ Squad
        19,  -- Chapter HQ Squad
        8,   -- Armory
        9,   -- Librarius
        10,  -- Apothecarion
        11   -- Reclusium
      );
```

Verification (expect exactly 8 rows):

```sql
SELECT Id, Name, SquadType FROM SquadTemplate WHERE SquadType & 128;
```

**Which templates and why:** the four HQ squads (5/6/7/19) and the four chapter offices (8/9/10/11 —
`UnitTemplate` 0 "Space Marine Chapter" slots at MinCount/MaxCount 1/1, confirmed from
`UnitTemplateSquadTemplate`). **Not** line squads 0-4. **Not** any NPC faction — NPC orders come from
`FactionStrategyController` and never attach individuals, so the flag would be inert data.

Pin it in `OnlyWar.Tests/Data/RulesDatabaseValidationTests.cs` (which already asserts SquadType facts
at `:284`): the flagged set is exactly `{5,6,7,8,9,10,11,19}`, and no line template carries it.

---

## 3. Availability validation

**Where order issue happens:** `Helpers/Orders/OrderAssignment.AssignSquadsToMission` (`:31-92`) is
the single choke point — the Region Ops screen (`RegionScreenController.cs:211`) is its only caller.
It already does exactly this shape of validation at `:38-53` (operational check, distinct-by-id,
Black Carapace reservation).

### 3.1 Signature change

```csharp
public static Order AssignSquadsToMission(
    IReadOnlyList<Squad> squads,
    Region targetRegion,
    AvailableMission mission,
    int targetFactionId,
    Aggression aggression,
    IReadOnlyList<PlayerSoldier> attachedSoldiers = null)   // new, optional
```

Optional keeps every existing caller and all 8 tests in `OnlyWar.Tests/Orders/OrderAssignmentTests.cs`
compiling unchanged.

### 3.2 Guards in `OrderAttachment.CanAttach`

Run before any mutation; the method returns `null` and creates nothing on failure (existing contract):

1. **Detachable formation** — `soldier.AssignedSquad?.SquadTemplate?.PermitsIndividualDetachment == true`
2. **Not already attached elsewhere** — `soldier.AttachedOrder == null || ReferenceEquals(soldier.AttachedOrder, targetOrder)`
3. ~~His home squad is not itself deployed~~ — **vacuous, removed.** Under §3.3 a detachable
   formation is never orderable, so its members' home squad can never be under orders.
4. **Fit to march** — `soldier.IsCombatEffective` (`ISoldier.cs:31`, from CasualtyRealism Phase 0)
5. **Co-located with the operation's staging point** — his squad's `CurrentRegion` equals the origin
   region, or its `BoardedLocation` matches. Reuse the shape of `MedicalProcedureService.SameLocation`
   (`Helpers/MedicalProcedureService.cs:85-100`) rather than reinventing it.
6. **Not reserved for a procedure** — extend the Black Carapace check at `OrderAssignment.cs:48-53`
   to cover attached soldiers, and add `RecruitmentPromotionService`'s Apothecary reservation
   (`Helpers/Recruitment/RecruitmentPromotionService.cs:223-231` — a staff Apothecary assigned to an
   implantation is not free this week).

### 3.3 Detachable formations are never orderable (decided — supersedes the mirror guard)

**A squad whose template carries `PermitsIndividualDetachment` cannot be assigned to an order at
all.** Inside `AssignSquadsToMission`, beside the `IsOperational` check at `:38`:

```csharp
if (squads.Any(s => s.SquadTemplate?.PermitsIndividualDetachment == true)) return null;
```

**Why this replaced the earlier design.** The first draft used two mutual-exclusion guards — "cannot
attach while the home squad is deployed" (guard 3) and "cannot deploy a squad while a member is
attached" (this one). Those two are *self-contradictory* against the §4 answer that claimed the
exclusion was per-man and the squad stayed orderable; and what they actually produced was mutual
exclusion **decided by ordering**: attach the Apothecary first and the whole Apothecarion is
grounded, issue the Apothecarion an order first and no specialist can be lent out. That is more
confusing to the player than either clean rule, and it produces the quirk the flag exists to prevent
— an HQ squad on one order with its members scattered across others.

One rule instead: **a detachable formation is a personnel pool, not a manoeuvre element.** Its people
reach the field only by attachment. Guard 3 becomes vacuous and is deleted.

**Do NOT implement this by marking these templates `Administrative`.** It is the obvious move and it
is a trap. `Squad.IsOperational` is load-bearing for exactly the services these formations exist to
supply:
- `Helpers/MedicalProcedureService.cs:80` requires `member.AssignedSquad?.IsOperational == true` to
  count someone as co-located **surgical staff** — an administrative Apothecarion stops supplying
  surgeons.
- `Helpers/Recruitment/RecruitmentPromotionService.cs:281` gates **recruitment/implantation** the
  same way.

Also note `Squad`'s ctor (`Models/Squads/Squad.cs:90,111`) sets `_isAdministrative = template?.IsOperational == false`,
so the DB bit would make them administrative from birth. Keep `IsOperational` **true**; put the
deployability rule in `OrderAssignment`, keyed on the new flag.

The UI consequence is that these squads must stop appearing in the Region Ops squad roster
(`RegionScreenController.cs:430` filters on `IsOperational && Members.Count > 0` — add the flag test)
while their *members* start appearing in the ATTACHMENTS group.

### 3.4 Transfer guard

`Helpers/SoldierTransferService.ApplyTransfer` (`:185`) must refuse an attached soldier — return
`false` early, same shape as its `RequiresBlackCarapace` guard at `:199`. The UI hides the options
anyway; the service is the enforcement point.

---

## 4. Resolved sub-questions (all of `CasualtyRealism.md` §3.1)

**How he is marked detached:** by the pointer pair `Order.AttachedSoldiers` ⟷
`PlayerSoldier.AttachedOrder`. `Squad.Members` is **not** modified — he is still on his squad's roll.

> Rationale: `Squad.AddSquadMember`/`RemoveSquadMember` (`Models/Squads/Squad.cs:131-147`) also drive
> `soldier.AssignedSquad`, which feeds save (`SaveStructure.sql:131` `Soldier.SquadId`), transfers,
> promotion eligibility, the Apothecarium, and fallen-brother detection
> (`GameStateDataAccess.cs:151-153` treats a null squad as **dead**). Removing him from `Members`
> would make a detached specialist read as a fallen brother on the next save. Disqualifying.

| Question | Answer | Rationale |
| --- | --- | --- |
| Which squad types carry the flag? | Template-level flag on the four HQ templates and four chapter offices (5,6,7,8,9,10,11,19) | Detachability is a property of the formation's *function* — these exist to supply specialists to others. A per-role marker would need a second flag on `SoldierTemplate` for no gain this pass. |
| Can a specialist attach to more than one order? | **No**, enforced by guard 2 at order issue | He is one man, and §2.6's reach rule ("every wounded soldier under the same order") is only unambiguous if the mapping is 1:1. |
| Does he count toward home-squad strength/readiness? | **Headcount yes, available strength no** | `Squad.Members.Count` unchanged (chapter screen keeps reading "10 soldiers"); anything meaning *ready right now* subtracts attached members — `RegionScreenController.SquadRosterLabel` (`:461-465`), `UpdateSelectionSummary` (`:467`), `EndTurnPreflight.IsIdleDeployableSquad`. Changing `Members` would ripple through save/transfer/promotion for a display concern. |
| Does attachment persist across turns, and what releases it? | **It persists exactly as long as the order does.** Three existing release paths (below) | The order *is* the unit of attachment, so order lifetime is attachment lifetime — no separate clock to persist, tick, or test. |
| What if his home squad is disbanded/destroyed mid-mission? | **The attachment survives; only his own death ends it** | Attachment is to the operation, not routed through the home squad, so the home squad's fate is irrelevant except when the man himself is gone. |
| Does his squad become non-deployable while members are dispersed? | **The squad is never deployable at all** (§3.3) — flagged formations are personnel pools, not manoeuvre elements | The per-man reading was self-contradictory with the mirror guard and produced order-dependent mutual exclusion. A pool that never manoeuvres has no ordering trap and no mixed state: an HQ squad can never be on one order while its members are on others. |
| *(Phase 2c)* What governs join/leave? | **The squad planner, via a specialist-specific heuristic seeded by the order's aggression** | Not a player order (micromanagement at the wrong altitude), not the generic planner (which scores squads, not individuals). The operative constraint on 2a: **nothing in it may encode a squad binding**, so 2c stays free to choose. |

**The three release paths**, all existing:
- Player unassigns in Region Ops → `OrderAssignment.UnassignSquads` → `ReleaseAll`
- `MissionAftermathProcessor.CleanupResolvedPlayerOrders` (`:142-159`) removes the order at turn end —
  so an ordinary one-week mission releases automatically, while Construction/ShowOfForce orders
  (`ShouldPersistPlayerOrder`, `:161-169`) keep him, matching their squads
- `OrderAssignment.DetachFromCurrentOrder` empties an order and calls `sector.RemoveOrder` (`:204-207`)

Death: `PlayerBattleAftermathSink.MoveToFallenBrothers` (`:34-37`) calls `OrderAttachment.Detach`.

---

## 5. Consumers that enumerate an order's personnel

Audited via `AssignedSquads` across the repo.

| File:line | What it does | Change |
| --- | --- | --- |
| `Helpers/Database/GameState/UnitDataAccess.cs:308` | `SaveOrder` writes `OrderSquad` rows | Add `OrderSoldier` rows (§6) |
| `Helpers/Orders/OrderAssignment.cs:197-208` | `DetachFromCurrentOrder` removes an order when its last squad leaves | Must `OrderAttachment.ReleaseAll(oldOrder)` before `sector.RemoveOrder` |
| `Helpers/Orders/OrderAssignment.cs:114-119` | `IsPlayerOrder` gate | No change — squads still decide ownership |
| `Helpers/Turns/MissionAftermathProcessor.cs:142-159` | `CleanupResolvedPlayerOrders` releases squads at turn end | Also release attachments — **primary release path** |
| `Helpers/Orders/InboundOrders.cs:24-31,80-88` | Dossier summary label / origin | Append "+N attached" to `SummaryLabel` |
| `Helpers/Turns/MissionTurnProcessor.cs:128-131` | Builds `BattleSquad`s from `AssignedSquads` | **No change** — no battlefield presence this pass |
| `Helpers/Turns/MissionTurnProcessor.cs:309-311` | Sums `EngineeringFortification` over members | **No change in 2a.** An attached Techmarine *should* contribute to a fortification order, but that is a rules change, not plumbing. Noted, not done. |
| `Models/Squads/Squad.cs:22-58` | `IsAdministrative` setter clears orders/berth/region | Must also release its members' attachments |
| `Helpers/Battles/Aftermath/PlayerBattleAftermathSink.cs:34-37` | Death path | Call `OrderAttachment.Detach(soldier)` |
| `Helpers/SoldierTransferService.cs:295-348` | `UpdateSquadLocations` / `DetachDeployment` | Covered by the §3.4 guard |
| `Scenes/RegionScreen/RegionScreenController.cs:167,189,278` | Inbound-order origin, re-selection, assigned-mission keys | §7 |
| `Helpers/TurnController.cs:114,118`, `Helpers/Turns/PlanetForwardSimulator.cs:71,83` | Partition orders into combat vs construction on `AssignedSquads.Any()` | **No change** — but see the invariant below, which §3.3 makes load-bearing rather than incidental |

**Invariant: an order must have at least one assigned squad.** `AssignSquadsToMission` already
rejects an empty squad list (`:38`), and that must stay — a specialists-only order is not permitted.
Several sites partition orders on `AssignedSquads.Any()` and would silently drop an order that had
only attached soldiers. §3.3 makes this sharper than it was: since flagged formations no longer
deploy as units, an order carrying an Apothecary *necessarily* also carries line squads, which is
also the fictional reading (a specialist attaches to an operation, he does not constitute one).
Worth an explicit test in `OrderAttachmentTests`.

---

## 6. Persistence

### 6.1 Schema

`Database/SaveStructure.sql`, immediately after `:173` (`OrderSquad`):

```sql
-- Table: OrderSoldier
-- Individuals attached to an operation without their home squad (Order.AttachedSoldiers,
-- Design/Reference/SpecialistAttachment.md). The soldier's Soldier.SquadId still points at his home
-- squad; this table is the only record that he is currently detached to an operation.
CREATE TABLE OrderSoldier (OrderId INTEGER NOT NULL REFERENCES Assignment (Id), SoldierId INTEGER NOT NULL REFERENCES Soldier (Id));
```

Foreign keys are enforced (`GameStateDataAccess.cs:395-400`), and the existing insert order already
works: soldiers at `:319-334`, orders at `:349-359`.

### 6.2 Save

`UnitDataAccess.SaveOrder` (`:293-320`) — after the `OrderSquad` loop, add the mirror loop.

**Close a latent gap while there:** `GameStateDataAccess.cs:346-348` collects orders as
`squads.Select(s => s.CurrentOrders).Distinct()`. Under the ≥1-squad invariant that is currently
sufficient — make it explicit rather than accidental:

```csharp
var orders = squads.Select(s => s.CurrentOrders)
    .Concat(playerSoldiers.Select(s => s.AttachedOrder))
    .Where(o => o != null && o.Mission != null)
    .Distinct();
```

### 6.3 Load — **the ordering trap**

`UnitDataAccess.PopulateOrdersBySquadId` (`:110-139`) constructs the `Order` objects, but runs inside
`GetSquadsByUnitId`, called at `GameStateDataAccess.cs:133` — **before soldiers exist** (`:137`).

Worse: `PlayerSoldierDataAccess.GetData` at `:145` does not decorate loaded soldiers in place.
`PlayerSoldier`'s constructor (`Models/Soldiers/PlayerSoldier.cs:173-179`) **evicts the base
`Soldier` from its squad and inserts the wrapper**. Anything capturing soldier references before
`:145` captures objects that are no longer in any squad.

So attachment hydration must run **after `:145`** and resolve against `playerSoldiers`. Add to
`UnitDataAccess`:

```csharp
public void PopulateOrderAttachments(
    IDbConnection connection,
    IReadOnlyDictionary<int, Squad> squadMap,
    IReadOnlyDictionary<int, PlayerSoldier> playerSoldierMap)
```

resolving orders as `squadMap.Values.Select(s => s.CurrentOrders).Where(o => o != null).Distinct()
.ToDictionary(o => o.Id)`, called from `GameStateDataAccess.cs` directly after `:145`.

`Helpers/SavedGameLoader.cs:75-83` (rebuilds `Sector.Orders` from `squad.CurrentOrders`) needs **no
change** — it re-registers the same `Order` instances, which now carry their attachments.

### 6.4 Round-trip tests

`OnlyWar.Tests/Data/SaveLoadRoundTripTests.cs`:

- In `SaveThenLoad_MutatedGeneratedSector_PreservesRoundTripFeatures`, at the order-creation block
  (`:145-153`): attach a member of a `PermitsIndividualDetachment` squad, and after reload assert
  1. the loaded order's `AttachedSoldiers` contains that soldier id;
  2. **that instance is reference-equal to the one in his home squad's `Members`** — this is the
     assertion that catches the `PlayerSoldier`-wrapper trap in §6.3. Without it the test passes on
     ids while the game holds two divergent objects;
  3. `soldier.AttachedOrder` is reference-equal to the loaded order.
- Extend `Load_RepopulatesSectorOrders_FromLoadedPlayerSquads` (`:486-523`).
- `OnlyWar.Tests/Data/MissionSaveTests.cs:181` builds a partial schema copied from
  `SaveStructure.sql`; add `OrderSoldier` if that fixture creates `Assignment`/`OrderSquad`.

---

## 7. UI surfaces

### 7.1 Order-issue specialist picker — Region Ops

**`Scenes/RegionScreen/RegionScreenController.cs`** carries all of it. The old `OrderDialogController`
is gone; this is the only order-issue surface. The roster is a **code-built tree** via
`CommandWorkspaceView.PopulateSelectionTree` (`Scenes/Common/CommandWorkspaceView.cs:102`) fed by
`CommandTreeNode` (`Helpers/UI/CommandWorkspaceModels.cs:17`), and multi-select is already on
(`:58`). **No `.tscn` edit needed.**

Add an `"ATTACHMENTS"` top-level group beside the unit nodes, children keyed `soldier:<id>`:

| Method | Line | Change |
| --- | --- | --- |
| `BuildUnitNodes` | `:423-459` | **Two changes.** (a) The existing squad filter (`IsOperational && Members.Count > 0`, `:430`) must now also exclude `PermitsIndividualDetachment` templates — those squads no longer deploy as units (§3.3). (b) Append the ATTACHMENTS group: members of landed flagged squads that pass `CanAttach`. Badge with current status. |
| `RecomputeSelectedSquads` | `:477-491` | Add a sibling `RecomputeSelectedSpecialists` reading `soldier:` keys (the existing method filters `key.StartsWith("squad:")`, so it already ignores them safely) |
| `OnAssignPressed` | `:203-223` | Pass specialists to `AssignSquadsToMission`; on `null`, the existing `GD.PushWarning` path should name the rejected specialist |
| `UnassignSelectedSquads` | `:500-510` | Release selected specialists too |
| `RefreshCommitBar` | `:330-338` | Count both in the button label; enable Unassign when a specialist is attached |
| `OnInboundOrderActivated` | `:165-193` | `SetSelectedKeys` must include `order.AttachedSoldiers` keys alongside squad keys (`:189`) |
| `ResolveSquadFromKey` | `:493-498` | Return null for `soldier:` keys, or route double-click to the soldier screen |

**Testability:** `RegionScreenController` is a Godot `partial class` and cannot be unit-tested.
Extract the eligible-specialist enumeration into a static `Helpers/Orders/SpecialistAvailability.cs`
so the *rules* get C# tests even though the tree wiring does not — the same pattern `OrderAssignment`
was extracted for (see its header comment, `:14-18`).

`Helpers/Orders/InboundOrders.cs` — extend `SummaryLabel` (`:24-31`) with "· +N attached".
`Scenes/PlanetDetailScreen/PlanetTacticalScreenController.cs` renders the same `InboundOrderInfo` and
inherits this for free.

### 7.2 Chapter-side detach / return

**`Scenes/ChapterScreen/ChapterController.cs`**
- `RenderSoldierDetail` (`:535-550`) — when `AttachedOrder != null`, surface it and call
  `ChapterView.SetTransferOptions([])` (transferring an attached man is the state §3.4 forbids).
- Recall affordance: `ChapterBrowserDetailCard` already declares `PrimaryActionText` /
  `PrimaryActionIconKey` (`Scenes/ChapterScreen/ChapterBrowserModels.cs:138-139`) — use it for
  "Recall from operation" rather than inventing a control. Route through the existing confirmation
  dialog (`:49-60`, `_transferConfirmationDialog`) and fire `CampaignChanged` (`:269`) on success.
- `BuildSquadDetail` (~`:854-877`) — add "N attached elsewhere" beside the wounded count.

**`Scenes/ChapterScreen/ChapterView.cs`** — wire the pressed event if `PrimaryActionText` has no
handler yet.

### 7.3 End-turn preflight

`Helpers/Turns/EndTurnPreflight.cs` — `IsIdleDeployableSquad` (~`:275`, keyed on
`squad.CurrentOrders == null`) will start nagging that the Apothecarion is idle while its Apothecary
is forward. Exclude squads with attached members. **C#-testable**
(`OnlyWar.Tests/UI/EndTurnPreflightTests.cs`).

### 7.4 Godot verification checklist (user-performed)

1. **Region Ops → roster:** an `ATTACHMENTS` group appears only when a flagged squad (Apothecarion /
   HQ / Armory / Librarius / Reclusium) is landed in the current region. Line squads never contribute.
2. Squads + a specialist + a mission → **Assign** creates one order; button label counts both
   ("Assign 2 Squads + 1 Specialist").
3. Inbound-orders dossier row shows "+1 attached"; clicking it re-selects **both**.
4. **Unassign** releases both.
5. The attached specialist no longer appears in the picker for a second order. **His home squad does
   not appear in the squad roster at all** — HQ squads and the four chapter offices are no longer
   assignable to orders as units (§3.3). This is the most visible behavior change in Phase 2a and
   the thing most worth a careful look: confirm nothing else in the campaign depended on ordering an
   HQ squad.
   *Regression check:* surgery staffing and recruitment/implantation must still work — both read
   `IsOperational`, which stays true (§3.3).
6. **Chapter screen → that soldier:** attachment shown, transfer dropdown empty, "Recall from
   operation" returns him and he reappears in Region Ops.
7. **End turn:** preflight does not flag his home squad as idle; after resolution an ordinary mission
   order is gone and he is back (a Fortify / Show-of-Force order keeps him).
8. **Save → quit → load:** 1-7 still hold; the specialist is **not** listed as a fallen brother.

---

## 8. What this does to Phase 2b (Apothecary field care)

**Easier:**
- `MissionContext.Order` already exists (`Models/Missions/MissionContext.cs:104,239`), so "everyone
  under this order" is one expression:
  `order.AssignedSquads.SelectMany(s => s.Members).Concat(order.AttachedSoldiers)`. No new plumbing —
  the payoff §3.1 predicted.
- `MissionDayScheduler.Run` (`Helpers/Missions/MissionDayScheduler.cs:39-67`) already takes an
  `onDayStart` callback supplied by `MissionTurnProcessor` (`:168-172`). The daily medical pass drops
  into that seam with **zero scheduler change**.
- Typing `AttachedSoldiers` as `PlayerSoldier` makes `SoldierEvaluation.MedicalRating` directly
  reachable — no downcast, no `OfType<>` filter.
- `EffectiveRegion` plus the release paths mean 2b never has to ask "is this man on a mission?" — it
  is `AttachedOrder != null || AssignedSquad?.CurrentOrders != null`, which also settles the
  garrison-vs-field partition in CasualtyRealism §3.3.

**Three traps to record now:**

1. **One order can produce several `MissionContext`s.** `MissionTurnProcessor.BuildMissionElements`
   (`:253-264`) splits an order into independent single-squad elements for
   `MissionForceMode.IndependentSquads`, each getting its own `MissionStepDriver` (`:142-162`). A
   daily medical pass hung off a *driver* would treat the same order's wounded once per element — an
   Apothecary silently worth 3× on a Recon order. **The pass must run per distinct `Order`, deduped,
   from `onDayStart`** — never inside a mission step.
2. **Co-location is computed from `AssignedSquad`.** `MedicalProcedureService.SameLocation`
   (`:85-100`) and `HasCoLocatedStaff` (`:71-83`) read `member.AssignedSquad`'s ship/region. An
   attached Apothecary's home squad may sit on the ship while he is forward, so garrison care would
   believe he is in two places and surgery gating would accept him at a site he has left. 2b must
   route `HasCoLocatedStaff` through `EffectiveRegion` (§1.2).
3. **The attached specialist is in no `BattleSquad`,** so `MissionContext.StartingPlayerParticipants`
   (`:241-246`, built from `playerSquads.SelectMany(squad => squad.Soldiers)`) excludes him. He earns
   no field XP (`MissionFieldExperienceLog`, `MissionTurnProcessor.cs:159,182`) and appears in no
   debrief (`Scenes/MissionDebriefDialogController.cs`). That is *consistent* with the
   no-battlefield-presence rule, but it will read as a bug the first time a player sends an Apothecary
   out and sees no trace of him. 2b should add him explicitly to the mission report; CasualtyRealism
   §3.2's "does the Apothecary earn Medical XP" question lands here.

---

## 9. Task list

| # | Step | Files | Verification |
| --- | --- | --- | --- |
| 1 | Rules-DB flag + model predicate | `Database/RulesMigration_SpecialistDetachment.sql` (new); `Models/Squads/SquadTemplate.cs`; `OnlyWar.Tests/Data/RulesDatabaseValidationTests.cs` | **C#-testable.** Run migration via SQLite CLI, verify with the `SquadType & 128` SELECT, then `--filter "FullyQualifiedName~OnlyWar.Tests.Data"` |
| 2 | `Order.AttachedSoldiers`, `PlayerSoldier.AttachedOrder` + `EffectiveRegion`, `OrderAttachment` | `Models/Orders/Order.cs`; `Models/Soldiers/PlayerSoldier.cs`; `Helpers/Orders/OrderAttachment.cs` (new); `OnlyWar.Tests/Orders/OrderAttachmentTests.cs` (new) | **C#-testable.** Pointer-pair symmetry, `ReleaseAll` |
| 3 | Validation + all release paths | `Helpers/Orders/OrderAssignment.cs`; `Helpers/Orders/SpecialistAvailability.cs` (new); `Models/Squads/Squad.cs`; `Helpers/Battles/Aftermath/PlayerBattleAftermathSink.cs`; `Helpers/Turns/MissionAftermathProcessor.cs`; `Helpers/SoldierTransferService.cs`; `OnlyWar.Tests/Orders/OrderAssignmentTests.cs` | **C#-testable.** Six guards, both mirror directions, three release paths |
| 4 | Persistence | `Database/SaveStructure.sql`; `Helpers/Database/GameState/UnitDataAccess.cs`; `GameStateDataAccess.cs`; `OnlyWar.Tests/Data/SaveLoadRoundTripTests.cs`; `MissionSaveTests.cs` | **C#-testable.** The reference-equality assertion in §6.4 is the one that matters |
| 5 | Strength/readiness display + preflight | `Helpers/Turns/EndTurnPreflight.cs`; `Helpers/Orders/InboundOrders.cs`; `OnlyWar.Tests/UI/EndTurnPreflightTests.cs` | **C#-testable** |
| 6 | Region Ops specialist picker | `Scenes/RegionScreen/RegionScreenController.cs` | **Godot-verify only** — checks 1-5 |
| 7 | Chapter screen status + recall | `Scenes/ChapterScreen/ChapterController.cs`; `ChapterView.cs` | **Godot-verify only** — check 6 |
| 8 | Docs | `Design/Reference/CasualtyRealism.md` §3.1 (mark resolved, cite this doc); `OnlyWar_TDD.md` save-schema section (~`:196`) | Review |

**Sequencing:** 1 → 2 → 3 → 4 is strict. 5 depends on 2. 6 depends on 3 and 5. 7 depends on 2. Steps
1-5 are one continuous C# session; a single `dotnet build --nologo -v q` then
`dotnet test --no-build --filter "FullyQualifiedName~OnlyWar.Tests.Data"` at the end of step 4 (the
round-trip test is `[Trait("Category","Slow")]`, so it will **not** run under the fast Battles filter
— invoke it deliberately).

**Expected non-regressions:** nothing in steps 1-5 touches battle resolution, the squad planner, or
`SquadFactory`'s seeded RNG walk, so **seeded battle and generation baselines should be unchanged**.
If they move, something in step 1 leaked into a code path reading `SquadType` by value — check that
first.
