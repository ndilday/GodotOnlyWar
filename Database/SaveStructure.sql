--
-- File generated with SQLiteStudio v3.2.1 on Mon Oct 12 11:20:58 2020
--
-- Text encoding used: System
--
PRAGMA foreign_keys = off;
BEGIN TRANSACTION;

-- Table: Character
CREATE TABLE Character (Id INTEGER PRIMARY KEY UNIQUE NOT NULL, Name STRING NOT NULL, Age INTEGER NOT NULL, Investigation REAL NOT NULL, Paranoia REAL NOT NULL, Neediness REAL NOT NULL, Patience REAL NOT NULL, Appreciation REAL NOT NULL, Influence REAL NOT NULL, LoyalFactionId INTEGER NOT NULL, OpinionOfPlayer REAL NOT NULL, NextRequestEligibleDate INTEGER);

-- Table: Fleet
CREATE TABLE Fleet (Id INTEGER PRIMARY KEY UNIQUE NOT NULL, FactionId INTEGER NOT NULL, x REAL NOT NULL, y REAL NOT NULL, DestinationPlanetId INTEGER REFERENCES Planet (Id), TravelWeeksRemaining INTEGER NOT NULL DEFAULT 0, OriginPlanetId INTEGER REFERENCES Planet (Id), TravelPhase INTEGER NOT NULL DEFAULT 0, CurrentPhaseWeeksRemaining INTEGER NOT NULL DEFAULT 0, WarpSubjectiveWeeks REAL NOT NULL DEFAULT 0, WarpObjectiveWeeks REAL NOT NULL DEFAULT 0, WarpSubjectiveTrainingApplied BOOLEAN NOT NULL DEFAULT 1);

-- Table: GlobalData
-- Scenario* columns carry the optional Opening Scenario state (Design/OpeningScenario.md §7).
-- ScenarioType 0 (None) means no scenario; legacy saves that predate these columns load with
-- Scenario == null via a column-count guard in GlobalDataAccess.
CREATE TABLE GlobalData (Millenium INTEGER NOT NULL, Year INTEGER NOT NULL, Week INTEGER NOT NULL, SaveVersion INTEGER NOT NULL, Requisition INTEGER NOT NULL DEFAULT 0, GeneseedStockpile INTEGER NOT NULL DEFAULT 0, GeneseedPurity REAL NOT NULL DEFAULT 1.0, ScenarioType INTEGER NOT NULL DEFAULT 0, ScenarioPromisedPlanetId INTEGER NOT NULL DEFAULT 0, ScenarioState INTEGER NOT NULL DEFAULT 0, ScenarioBriefingAcknowledged BOOLEAN NOT NULL DEFAULT 0, ScenarioBriefingText TEXT, ScenarioOriginalAuthorityCharacterId INTEGER NOT NULL DEFAULT 0);

-- Table: HitLocation
CREATE TABLE HitLocation (SoldierId INTEGER NOT NULL REFERENCES Soldier (Id), HitLocationTemplateId INTEGER NOT NULL, IsCybernetic BOOLEAN NOT NULL, Armor REAL NOT NULL, WoundTotal INTEGER NOT NULL, WeeksOfHealing INTEGER);

-- Table: MedicalProcedure
-- A medical procedure in progress in the Apothecarium (PRD 4.8 / 5.3). HitLocationTemplateId
-- is a rules-data id (no save-DB table), matching HitLocation's column of the same name.
CREATE TABLE MedicalProcedure (SoldierId INTEGER NOT NULL REFERENCES Soldier (Id), HitLocationTemplateId INTEGER NOT NULL, ProcedureType INTEGER NOT NULL, WeeksRemaining INTEGER NOT NULL, RequisitionCost INTEGER NOT NULL);

-- Table: Planet
CREATE TABLE Planet (Id INTEGER PRIMARY KEY UNIQUE NOT NULL, PlanetTemplateId INTEGER NOT NULL, Name STRING NOT NULL UNIQUE, x INTEGER NOT NULL, y INTEGER NOT NULL, Importance INTEGER NOT NULL, TaxLevel INTEGER NOT NULL);

-- Table: Region
CREATE TABLE Region (Id INTEGER PRIMARY KEY UNIQUE NOT NULL, PlanetId INTEGER NOT NULL REFERENCES Planet (Id), RegionNumber INTEGER NOT NULL, RegionName STRING NOT NULL, RegionType INTEGER NOT NULL, IsUnderAssault BOOLEAN NOT NULL, IntelligenceLevel REAL NOT NULL, CarryingCapacity BIGINT NOT NULL, MaximumCarryingCapacity BIGINT NOT NULL);

-- Table: RegionFaction
-- GrowthMultiplier (default 1.0) throttles organic growth; legacy rows default to 1.0 via a
-- column-count guard in PlanetDataAccess.PopulateRegionFactions (Design/OpeningScenario.md §2.2, §7).
-- ListeningPost is a sensor structure (formerly "Detection"); it now feeds PlanetFactionRegionIntel
-- rather than providing an awareness bonus directly. Column is positional in the loader.
CREATE TABLE RegionFaction (RegionId INTEGER REFERENCES Region (Id) NOT NULL, FactionId INTEGER NOT NULL, IsPublic BOOLEAN NOT NULL, Population BIGINT NOT NULL, Garrison INTEGER NOT NULL, Organization INTEGER NOT NULL, Entrenchment REAL NOT NULL, ListeningPost REAL NOT NULL, AntiAir REAL NOT NULL, GrowthMultiplier REAL NOT NULL DEFAULT 1.0);

-- Table: PlanetFaction
CREATE TABLE PlanetFaction (PlanetId INTEGER REFERENCES Planet (Id) NOT NULL, FactionId INTEGER NOT NULL, IsPublic BOOLEAN NOT NULL, PlanetaryControl INTEGER NOT NULL, PlayerReputation REAL NOT NULL, LeaderId INTEGER REFERENCES Character (Id));
-- Table: PlanetFactionRegionIntel
-- A faction's single per-region situational-awareness value (replaces RegionFactionObserverIntel and
-- the awareness role of RegionFaction.Detection). FactionId is the faction that holds the awareness;
-- serves both its offensive knowledge of enemy regions and its defensive sight of its own ground.
CREATE TABLE PlanetFactionRegionIntel (PlanetId INTEGER REFERENCES Planet (Id) NOT NULL, FactionId INTEGER NOT NULL, RegionId INTEGER REFERENCES Region (Id) NOT NULL, IntelLevel REAL NOT NULL);

-- Table: PlayeFactionSubEvent
CREATE TABLE PlayerFactionSubEvent (PlayerFactionEventId INTEGER REFERENCES PlayerFactionEvent (Id) NOT NULL, Entry TEXT NOT NULL);

-- Table: PlayerFactionEvent
CREATE TABLE PlayerFactionEvent (Id INTEGER PRIMARY KEY UNIQUE NOT NULL, Millenium INTEGER NOT NULL, Year INTEGER NOT NULL, Week INTEGER NOT NULL, Title TEXT NOT NULL);

-- Table: PlayerSoldier
CREATE TABLE PlayerSoldier (SoldierId INTEGER PRIMARY KEY REFERENCES Soldier (Id) UNIQUE NOT NULL, ImplantMillenium INTEGER NOT NULL, ImplantYear INTEGER NOT NULL, ImplantWeek INTEGER NOT NULL);

-- Table: SoldierEvaluation
CREATE TABLE SoldierEvaluation (SoldierId INTEGER NOT NULL REFERENCES Soldier (Id), Millenium INTEGER NOT NULL, Year INTEGER NOT NULL, Week INTEGER NOT NULL);

-- Table: SoldierEvaluationRating (open-ended: one row per rating value)
CREATE TABLE SoldierEvaluationRating (SoldierId INTEGER NOT NULL REFERENCES Soldier (Id), Millenium INTEGER NOT NULL, Year INTEGER NOT NULL, Week INTEGER NOT NULL, RatingKey STRING NOT NULL, Value REAL NOT NULL);

-- Table: SoldierAward
CREATE TABLE SoldierAward (SoldierId INTEGER NOT NULL REFERENCES Soldier (Id), Millenium INTEGER NOT NULL, Year INTEGER NOT NULL, Week INTEGER NOT NULL, Name STRING NOT NULL, Type STRING NOT NULL, Level INTEGER NOT NULL);

-- Table: PlayerSoldierFactionCasualtyCount
CREATE TABLE PlayerSoldierFactionCasualtyCount (PlayerSoldierId INTEGER NOT NULL REFERENCES PlayerSoldier (SoldierId), FactionId INTEGER NOT NULL, Count INTEGER NOT NULL);

-- Table: PlayerSoldierEvent
-- Structured soldier history. FactionId/WeaponTemplateId are rules-data ids (no save-DB
-- table), so they carry no foreign key, matching PlayerSoldierFactionCasualtyCount.
-- RelatedSoldierIds is a CSV reserved for later passes (unused in step 1).
CREATE TABLE PlayerSoldierEvent (PlayerSoldierId INTEGER NOT NULL REFERENCES PlayerSoldier (SoldierId), Millenium INTEGER NOT NULL, Year INTEGER NOT NULL, Week INTEGER NOT NULL, EventType INTEGER NOT NULL, FactionId INTEGER, WeaponTemplateId INTEGER, Magnitude INTEGER, LocationName STRING, Detail STRING, RelatedSoldierIds STRING);

-- Table: PlayerSoldierMeleeWeaponCasualtyCount
CREATE TABLE PlayerSoldierMeleeWeaponCasualtyCount (PlayerSoldierId INTEGER REFERENCES PlayerSoldier (SoldierId) NOT NULL, MeleeWeaponTemplateId INTEGER, Count INTEGER NOT NULL);

-- Table: PlayerSoldierRangedWeaponCasualtyCount
CREATE TABLE PlayerSoldierRangedWeaponCasualtyCount (PlayerSoldierId INTEGER REFERENCES PlayerSoldier (SoldierId) NOT NULL, RangedWeaponTemplateId INTEGER, Count INTEGER NOT NULL);

-- Table: Request
CREATE TABLE Request (Id INTEGER PRIMARY KEY UNIQUE NOT NULL, CharacterId INTEGER REFERENCES Character (Id) NOT NULL, PlanetId INTEGER REFERENCES Planet (Id) NOT NULL, ThreatFactionId INTEGER, RequestDate INTEGER NOT NULL, ResolutionDate INTEGER, Deadline INTEGER NOT NULL, Status INTEGER NOT NULL, CommitmentKey TEXT NOT NULL, CommitmentDisplayName TEXT NOT NULL, CommitmentDisplayUnit TEXT NOT NULL, PackageCount INTEGER NOT NULL, ServiceWeeks INTEGER NOT NULL, DeadlineWeeks INTEGER NOT NULL, ReferenceBattleValue INTEGER NOT NULL, MaximumEffectivePackageCount INTEGER NOT NULL, QualificationTags TEXT, ProgressBattleValueTime INTEGER NOT NULL, OfferedRequisition INTEGER NOT NULL, OfferedScheduleKind INTEGER NOT NULL, OfferedCadenceWeeks INTEGER NOT NULL, OfferedDeliveryDelayWeeks INTEGER NOT NULL, Severity INTEGER NOT NULL, Hazard INTEGER NOT NULL, HasPlayerResponded BOOLEAN NOT NULL);

CREATE TABLE Pledge (Id INTEGER PRIMARY KEY UNIQUE NOT NULL, SourcePlanetId INTEGER REFERENCES Planet (Id) NOT NULL, GrantingAuthorityId INTEGER NOT NULL, PayloadKind INTEGER NOT NULL, PayloadAmount INTEGER NOT NULL, ScheduleKind INTEGER NOT NULL, CadenceWeeks INTEGER NOT NULL, Status INTEGER NOT NULL, NextDeliveryDate INTEGER NOT NULL);

-- Table: Ship
CREATE TABLE Ship (Id INTEGER PRIMARY KEY UNIQUE NOT NULL, ShipTemplateId INTEGER NOT NULL, FleetId INTEGER REFERENCES Fleet (Id) NOT NULL, Name STRING NOT NULL);

-- Table: Soldier
CREATE TABLE Soldier (Id INTEGER PRIMARY KEY NOT NULL UNIQUE, SoldierTemplateId INTEGER NOT NULL, SquadId INTEGER REFERENCES Squad (Id), Name STRING NOT NULL, Strength REAL NOT NULL, Dexterity REAL NOT NULL, Constitution REAL NOT NULL, Intelligence REAL NOT NULL, Perception REAL NOT NULL, Ego REAL NOT NULL, Charisma REAL NOT NULL, PsychicPower REAL NOT NULL, AttackSpeed REAL NOT NULL, Size REAL NOT NULL, MoveSpeed REAL NOT NULL);

-- Table: SoldierSkill
CREATE TABLE SoldierSkill (SoldierId INTEGER NOT NULL REFERENCES Soldier (Id), BaseSkillId INTEGER NOT NULL, PointsInvested REAL NOT NULL);

-- Table: Squad
CREATE TABLE Squad (Id INTEGER PRIMARY KEY UNIQUE NOT NULL, SquadTemplateId INTEGER NOT NULL, ParentUnitId INTEGER NOT NULL REFERENCES Unit (Id), Name STRING NOT NULL, LoadedShipId INTEGER REFERENCES Ship (Id), LandedRegionId INTEGER REFERENCES Region(Id), TrainingFocus INTEGER NOT NULL DEFAULT 0);

-- Table: SquadWeaponSet
CREATE TABLE SquadWeaponSet (SquadId INTEGER NOT NULL REFERENCES Squad (Id), WeaponSetId INTEGER NOT NULL);

-- Table: Unit
CREATE TABLE Unit (Id INTEGER PRIMARY KEY UNIQUE NOT NULL, FactionId INTEGER NOT NULL, UnitTemplateId INTEGER NOT NULL, ParentUnitId INTEGER REFERENCES Unit (Id), Name STRING NOT NULL);

-- Table: Mission
-- FactionId has no foreign key: factions live in the read-only rules database, not
-- in the save file, and are matched by id at load time. A cross-database reference
-- cannot be a real SQLite foreign key.
CREATE TABLE Mission (Id INTEGER PRIMARY KEY UNIQUE NOT NULL, MissionType INTEGER NOT NULL, RegionId INTEGER NOT NULL REFERENCES Region (Id), FactionId INTEGER NOT NULL, MissionSize INTEGER NOT NULL, DefenseTypeId INTEGER, IsRegionMission BOOLEAN NOT NULL);

-- Table: Order
CREATE TABLE Assignment (Id INTEGER PRIMARY KEY UNIQUE NOT NULL, MissionId INTEGER NOT NULL REFERENCES Mission (Id), Disposition INTEGER NOT NULL, IsQuiet BOOLEAN NOT NULL, IsActivelyEngaging BOOLEAN NOT NULL, Aggression INTEGER NOT NULL);

-- Table: SquadOrder
CREATE TABLE OrderSquad (OrderId INTEGER NOT NULL REFERENCES Assignment (Id), SquadId INTEGER NOT NULL REFERENCES Squad (Id));


COMMIT TRANSACTION;
PRAGMA foreign_keys = on;
