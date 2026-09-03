--
-- File generated with SQLiteStudio v3.2.1 on Mon Oct 12 11:20:58 2020
--
-- Text encoding used: System
--
PRAGMA foreign_keys = off;
BEGIN TRANSACTION;

-- Table: Character
CREATE TABLE Character (Id INTEGER PRIMARY KEY UNIQUE NOT NULL, Name STRING NOT NULL, Age INTEGER NOT NULL, Investigation REAL NOT NULL, Paranoia REAL NOT NULL, Neediness REAL NOT NULL, Patience REAL NOT NULL, Appreciation REAL NOT NULL, Influence REAL NOT NULL, LoyalFactionId INTEGER NOT NULL, OpinionOfPlayer REAL NOT NULL, NextRequestEligibleDate INTEGER, Competence REAL NOT NULL DEFAULT 0.5, Severity REAL NOT NULL DEFAULT 0.5);

-- Table: Fleet
CREATE TABLE Fleet (Id INTEGER PRIMARY KEY UNIQUE NOT NULL, FactionId INTEGER NOT NULL, x REAL NOT NULL, y REAL NOT NULL, DestinationPlanetId INTEGER REFERENCES Planet (Id), TravelWeeksRemaining INTEGER NOT NULL DEFAULT 0, OriginPlanetId INTEGER REFERENCES Planet (Id), TravelPhase INTEGER NOT NULL DEFAULT 0, CurrentPhaseWeeksRemaining INTEGER NOT NULL DEFAULT 0, WarpSubjectiveWeeks REAL NOT NULL DEFAULT 0, WarpObjectiveWeeks REAL NOT NULL DEFAULT 0, WarpSubjectiveTrainingApplied BOOLEAN NOT NULL DEFAULT 1);

-- Table: GlobalData
-- Scenario* columns carry the optional Opening Scenario state (Design/Reference/OpeningScenario.md §7).
-- ScenarioType 0 (None) means no scenario. HomeWorldPlanetId remains null until the
-- Promised World is won. Format v5 is an intentional clean break from older saves and adds
-- chapter/planet loadout doctrine plus character (role and individual) loadout persistence.
CREATE TABLE GlobalData (Millenium INTEGER NOT NULL, Year INTEGER NOT NULL, Week INTEGER NOT NULL, SaveVersion INTEGER NOT NULL, Requisition INTEGER NOT NULL DEFAULT 0, GeneseedStockpile INTEGER NOT NULL DEFAULT 0, GeneseedPurity REAL NOT NULL DEFAULT 1.0, ScenarioType INTEGER NOT NULL DEFAULT 0, ScenarioPromisedPlanetId INTEGER NOT NULL DEFAULT 0, ScenarioInvaderFactionId INTEGER NOT NULL DEFAULT 0, ScenarioState INTEGER NOT NULL DEFAULT 0, ScenarioBriefingAcknowledged BOOLEAN NOT NULL DEFAULT 0, ScenarioBriefingText TEXT, ScenarioOriginalAuthorityCharacterId INTEGER NOT NULL DEFAULT 0, HomeWorldPlanetId INTEGER REFERENCES Planet (Id), CampaignId TEXT, CampaignSeed INTEGER, RandomAlgorithmVersion INTEGER NOT NULL DEFAULT 1);

-- The latest resolved turn report is intentionally one bounded JSON payload. A missing row is
-- valid for a campaign that has not resolved a turn yet.
CREATE TABLE LastTurnReport (Id INTEGER PRIMARY KEY CHECK (Id = 1), ResolvedDate INTEGER NOT NULL, PayloadJson TEXT NOT NULL);

-- Table: HitLocation
CREATE TABLE HitLocation (SoldierId INTEGER NOT NULL REFERENCES Soldier (Id), HitLocationTemplateId INTEGER NOT NULL, IsCybernetic BOOLEAN NOT NULL, Armor REAL NOT NULL, WoundTotal INTEGER NOT NULL, WeeksOfHealing INTEGER);

-- Table: MedicalProcedure
-- A medical procedure in progress in the Apothecarium (PRD 4.8 / 5.3). HitLocationTemplateId
-- is a rules-data id (no save-DB table), matching HitLocation's column of the same name.
CREATE TABLE MedicalProcedure (SoldierId INTEGER NOT NULL REFERENCES Soldier (Id), HitLocationTemplateId INTEGER NOT NULL, ProcedureType INTEGER NOT NULL, WeeksRemaining INTEGER NOT NULL, RequisitionCost INTEGER NOT NULL);

-- Table: RecruitmentProgram
-- Recruitment is limited to the Chapter Home World in v1. Dates are stored as total
-- campaign weeks, matching Request/Pledge persistence, so the data layer stays independent
-- of the evolving recruitment domain model.
CREATE TABLE RecruitmentProgram (Id INTEGER PRIMARY KEY UNIQUE NOT NULL, HomeWorldPlanetId INTEGER NOT NULL REFERENCES Planet (Id), IsConfigured BOOLEAN NOT NULL, Policy INTEGER NOT NULL, WorldType INTEGER NOT NULL, StrengthThreshold INTEGER NOT NULL, ConstitutionThreshold INTEGER NOT NULL, IntelligenceThreshold INTEGER NOT NULL, DexterityThreshold INTEGER NOT NULL, EgoThreshold INTEGER NOT NULL, GeneticCompatibilityThreshold REAL NOT NULL, EstablishedDate INTEGER NOT NULL, LastProcessedDate INTEGER);

-- Table: RecruitmentUnscreenedCohort
-- Aggregate cohorts avoid materializing planetary populations. RemainingPopulation is REAL
-- because expected-value screening and the founding-pool decay both preserve fractions.
CREATE TABLE RecruitmentUnscreenedCohort (Id INTEGER PRIMARY KEY UNIQUE NOT NULL, ProgramId INTEGER NOT NULL REFERENCES RecruitmentProgram (Id), CreatedDate INTEGER NOT NULL, RemainingPopulation REAL NOT NULL, MinimumAgeAtCreation REAL NOT NULL, MaximumAgeAtCreation REAL NOT NULL, IsFoundingPool BOOLEAN NOT NULL);

-- Table: RecruitmentCandidate
-- Qualified candidates are individual records; threshold changes never rewrite these rows.
CREATE TABLE RecruitmentCandidate (Id INTEGER PRIMARY KEY UNIQUE NOT NULL, ProgramId INTEGER NOT NULL REFERENCES RecruitmentProgram (Id), SourcePlanetId INTEGER NOT NULL REFERENCES Planet (Id), BirthDate INTEGER NOT NULL, Strength REAL NOT NULL, Constitution REAL NOT NULL, Intelligence REAL NOT NULL, Dexterity REAL NOT NULL, Ego REAL NOT NULL, GeneticCompatibility REAL NOT NULL, QualificationDate INTEGER NOT NULL, Designation STRING NOT NULL);

-- Table: RecruitmentAspirant
-- Aspirant ids share the soldier id sequence so a promoted neophyte can retain identity.
CREATE TABLE RecruitmentAspirant (Id INTEGER PRIMARY KEY UNIQUE NOT NULL, ProgramId INTEGER NOT NULL REFERENCES RecruitmentProgram (Id), SourcePlanetId INTEGER NOT NULL REFERENCES Planet (Id), BirthDate INTEGER NOT NULL, Strength REAL NOT NULL, Constitution REAL NOT NULL, Intelligence REAL NOT NULL, Dexterity REAL NOT NULL, Ego REAL NOT NULL, GeneticCompatibility REAL NOT NULL, AdmissionDate INTEGER NOT NULL, CurrentPhase INTEGER NOT NULL, PhaseStartedDate INTEGER NOT NULL, WeeksInCurrentPhase INTEGER NOT NULL, TrainingProgress REAL NOT NULL, Designation STRING NOT NULL);

-- Table: RecruitmentAspirantSkill
CREATE TABLE RecruitmentAspirantSkill (AspirantId INTEGER NOT NULL REFERENCES RecruitmentAspirant (Id), BaseSkillId INTEGER NOT NULL, PointsInvested REAL NOT NULL, PRIMARY KEY (AspirantId, BaseSkillId));

-- Table: RecruitmentAspirantEvent
-- Aspirant histories remain private to recruitment and do not enter the campaign event ledger.
CREATE TABLE RecruitmentAspirantEvent (AspirantId INTEGER NOT NULL REFERENCES RecruitmentAspirant (Id), EventDate INTEGER NOT NULL, EventType INTEGER NOT NULL, Detail STRING NOT NULL);

-- Table: RecruitmentProcedure
-- Phase 12 aspirant-to-neophyte promotion is immediate and has no procedure row.
CREATE TABLE RecruitmentProcedure (Id INTEGER PRIMARY KEY UNIQUE NOT NULL, ProgramId INTEGER NOT NULL REFERENCES RecruitmentProgram (Id), AspirantId INTEGER NOT NULL, GeneticCompatibility REAL NOT NULL, ProcedureType INTEGER NOT NULL, Phase INTEGER NOT NULL, Status INTEGER NOT NULL, AssignedApothecarySoldierId INTEGER NOT NULL, WeeksRemaining INTEGER NOT NULL, ReservedSquadId INTEGER REFERENCES Squad (Id));

-- Table: RecruitmentProgramLog
-- Dead aspirants are deleted; their aggregate outcome survives only in this bounded program log.
CREATE TABLE RecruitmentProgramLog (ProgramId INTEGER NOT NULL REFERENCES RecruitmentProgram (Id), EventDate INTEGER NOT NULL, EventType INTEGER NOT NULL, EventCount INTEGER NOT NULL, Entry STRING NOT NULL);

-- Table: Planet
CREATE TABLE Planet (Id INTEGER PRIMARY KEY UNIQUE NOT NULL, PlanetTemplateId INTEGER NOT NULL, Name STRING NOT NULL UNIQUE, x INTEGER NOT NULL, y INTEGER NOT NULL, Importance INTEGER NOT NULL, TaxLevel INTEGER NOT NULL, CapitalRegionId INTEGER);

-- Table: Region
CREATE TABLE Region (Id INTEGER PRIMARY KEY UNIQUE NOT NULL, PlanetId INTEGER NOT NULL REFERENCES Planet (Id), RegionNumber INTEGER NOT NULL, RegionName STRING NOT NULL, RegionType INTEGER NOT NULL, IsUnderAssault BOOLEAN NOT NULL, IntelligenceLevel REAL NOT NULL, CarryingCapacity BIGINT NOT NULL, MaximumCarryingCapacity BIGINT NOT NULL);

-- Table: RegionFaction
-- GrowthMultiplier (default 1.0) throttles organic growth; legacy rows default to 1.0 via a
-- column-count guard in PlanetDataAccess.PopulateRegionFactions (Design/Reference/OpeningScenario.md §2.2, §7).
-- ListeningPost is a sensor structure (formerly "Detection"); it now feeds PlanetFactionRegionAwareness
-- rather than providing an awareness bonus directly. Column is positional in the loader.
-- AssignedDefensiveBattleValue is nullable on purpose: NULL means the region has never been through a
-- planning pass (including every row in a save written before the column existed), which the loader
-- keeps distinct from an assignment of zero so the assault path derives the clamp instead of fielding
-- no defence. Read by name via GetOrdinalOrDefault, so older saves load unchanged.
CREATE TABLE RegionFaction (RegionId INTEGER REFERENCES Region (Id) NOT NULL, FactionId INTEGER NOT NULL, IsPublic BOOLEAN NOT NULL, Population BIGINT NOT NULL, Garrison INTEGER NOT NULL, Organization INTEGER NOT NULL, Entrenchment REAL NOT NULL, ListeningPost REAL NOT NULL, AntiAir REAL NOT NULL, GrowthMultiplier REAL NOT NULL DEFAULT 1.0, Contentment REAL NOT NULL DEFAULT 70.0, ArmedCivilians INTEGER NOT NULL DEFAULT 0, HasEmergenceAdvantage BOOLEAN NOT NULL DEFAULT 0, OrganizedMilitaryStrength BIGINT, AssignedDefensiveBattleValue BIGINT, StrategicInvasionForceId BIGINT, DormantConsolidation REAL NOT NULL DEFAULT 0.0);

-- Table: PlanetFaction
CREATE TABLE PlanetFaction (PlanetId INTEGER REFERENCES Planet (Id) NOT NULL, FactionId INTEGER NOT NULL, IsPublic BOOLEAN NOT NULL, PlanetaryControl INTEGER NOT NULL, PlayerReputation REAL NOT NULL, LeaderId INTEGER REFERENCES Character (Id));

-- Latent Ork ecosystems are not planets. A Waaagh! is a persistent identity linked to its real
-- command squad; the squad itself remains in Unit/Squad so normal battle and casualty persistence
-- applies to it.
CREATE TABLE GhostPopulationSource (Id INTEGER PRIMARY KEY UNIQUE NOT NULL, FactionId INTEGER, x INTEGER NOT NULL, y INTEGER NOT NULL, PlanetTemplateId INTEGER NOT NULL, Population BIGINT NOT NULL, PopulationCapacity BIGINT NOT NULL, Consolidation REAL NOT NULL);
CREATE TABLE StrategicInvasionForce (Id INTEGER PRIMARY KEY UNIQUE NOT NULL, FactionId INTEGER NOT NULL, CommandSquadId INTEGER NOT NULL REFERENCES Squad (Id), CurrentRegionId INTEGER REFERENCES Region (Id), OriginPlanetId INTEGER REFERENCES Planet (Id), DestinationPlanetId INTEGER REFERENCES Planet (Id), TravelWeeksRemaining INTEGER NOT NULL DEFAULT 0, TransitBattleValue BIGINT NOT NULL DEFAULT 0, IsActive BOOLEAN NOT NULL DEFAULT 1);

-- Balance inputs for the code-owned Ork lifecycle equations.
CREATE TABLE FactionBehaviorRulesProfile (ProfileKey TEXT PRIMARY KEY NOT NULL, GhostSourceChancePerEmptyTile REAL NOT NULL, MinimumGhostSourcesPerSector INTEGER NOT NULL, WeeklyConsolidationSigmaDivisor REAL NOT NULL, WeeklyConsolidationDrift REAL NOT NULL, MobilizationMedian REAL NOT NULL, MobilizationSigma REAL NOT NULL, MobilizationMinimum REAL NOT NULL, MobilizationMaximum REAL NOT NULL, DefendedLandingRatio REAL NOT NULL, UndefendedLandingBattleValue BIGINT NOT NULL, SuccessorGenerationMinimumBattleValue BIGINT NOT NULL, SuccessorMergeLeaderLoss REAL NOT NULL, TravelMultiplier REAL NOT NULL, GhostLogisticGrowthRate REAL NOT NULL, OccupiedCivilianDeclineRate REAL NOT NULL, ExceptionalAssassinationMargin REAL NOT NULL, PublicGrowthMultiplier REAL NOT NULL, DormantGrowthMultiplier REAL NOT NULL, DormantEmergenceMinimumPopulation BIGINT NOT NULL, DormantEmergenceChance REAL NOT NULL, DormantCullingPopulationReductionFraction REAL NOT NULL, DormantCullingConsolidationReductionFraction REAL NOT NULL, DormantCullingOutsideHelpEffectivePdfFloor REAL NOT NULL, DormantCullingFalsePositiveCapacityCost REAL NOT NULL, MoraleNearbyMobSupport REAL NOT NULL, MoraleLivingLeaderSupport REAL NOT NULL, MoraleCasualtyPenalty REAL NOT NULL, MoraleRoutPenalty REAL NOT NULL, MoraleSeparatedPenalty REAL NOT NULL, MoraleCommandLossPenalty REAL NOT NULL, MoraleMaximumSupport REAL NOT NULL, DormantInitialBeliefChance REAL NOT NULL DEFAULT 0.35, DormantInitialBeliefEvidence REAL NOT NULL DEFAULT 3.0);
-- Table: PlanetFactionRegionAwareness
-- A faction's single per-region situational-awareness value. FactionId is the faction that holds
-- the awareness; it serves both offensive knowledge of enemy regions and defensive sight of its
-- own ground. Faction ids refer to the read-only rules database.
CREATE TABLE PlanetFactionRegionAwareness (
    PlanetId INTEGER NOT NULL REFERENCES Planet (Id),
    FactionId INTEGER NOT NULL,
    RegionId INTEGER NOT NULL REFERENCES Region (Id),
    Awareness REAL NOT NULL,
    PRIMARY KEY (PlanetId, FactionId, RegionId)
);

-- Only non-default, canonical faction-pair stances are stored. An absent row is Hostile.
CREATE TABLE FactionRelationship (
    LowerFactionId INTEGER NOT NULL,
    HigherFactionId INTEGER NOT NULL,
    Stance INTEGER NOT NULL,
    PRIMARY KEY (LowerFactionId, HigherFactionId),
    CHECK (LowerFactionId < HigherFactionId),
    CHECK (Stance IN (1, 2))
);

-- Target-specific intelligence is a sparse belief store. IntelLevel is derived from Evidence and
-- deliberately is not persisted; the record never says whether the target is really present.
CREATE TABLE PlanetFactionTargetIntel (
    PlanetId INTEGER NOT NULL REFERENCES Planet (Id),
    ObserverFactionId INTEGER NOT NULL,
    RegionId INTEGER NOT NULL REFERENCES Region (Id),
    TargetFactionId INTEGER NOT NULL,
    Evidence REAL NOT NULL,
    EstimatedPopulation BIGINT,
    EstimatedMilitaryStrength BIGINT,
    LastEvidenceWeek INTEGER NOT NULL,
    PRIMARY KEY (PlanetId, ObserverFactionId, RegionId, TargetFactionId),
    CHECK (ObserverFactionId <> TargetFactionId),
    CHECK (Evidence >= 0.25)
);

-- Table: PlayerSoldier
CREATE TABLE PlayerSoldier (SoldierId INTEGER PRIMARY KEY REFERENCES Soldier (Id) UNIQUE NOT NULL, ImplantMillenium INTEGER NOT NULL, ImplantYear INTEGER NOT NULL, ImplantWeek INTEGER NOT NULL, GeneticCompatibility REAL, RecruitmentBirthMillenium INTEGER, RecruitmentBirthYear INTEGER, RecruitmentBirthWeek INTEGER);

-- Table: SoldierEvaluation
CREATE TABLE SoldierEvaluation (SoldierId INTEGER NOT NULL REFERENCES Soldier (Id), Millenium INTEGER NOT NULL, Year INTEGER NOT NULL, Week INTEGER NOT NULL);

-- Table: SoldierEvaluationRating (open-ended: one row per rating value)
CREATE TABLE SoldierEvaluationRating (SoldierId INTEGER NOT NULL REFERENCES Soldier (Id), Millenium INTEGER NOT NULL, Year INTEGER NOT NULL, Week INTEGER NOT NULL, RatingKey STRING NOT NULL, Value REAL NOT NULL);

-- Table: SoldierAward
-- Type is the stable award-family key (not a closed enum). Name is the historical
-- display-name snapshot, so a save remains readable even if a later mod changes
-- its current family display name or icon.
CREATE TABLE SoldierAward (SoldierId INTEGER NOT NULL REFERENCES Soldier (Id), Millenium INTEGER NOT NULL, Year INTEGER NOT NULL, Week INTEGER NOT NULL, Name STRING NOT NULL, Type STRING NOT NULL, Level INTEGER NOT NULL);

-- Table: PlayerSoldierFactionCasualtyCount
CREATE TABLE PlayerSoldierFactionCasualtyCount (PlayerSoldierId INTEGER NOT NULL REFERENCES PlayerSoldier (SoldierId), FactionId INTEGER NOT NULL, Count INTEGER NOT NULL);

-- Table: PlayerSoldierMeleeWeaponCasualtyCount
CREATE TABLE PlayerSoldierMeleeWeaponCasualtyCount (PlayerSoldierId INTEGER REFERENCES PlayerSoldier (SoldierId) NOT NULL, MeleeWeaponTemplateId INTEGER, Count INTEGER NOT NULL);

-- Table: PlayerSoldierRangedWeaponCasualtyCount
CREATE TABLE PlayerSoldierRangedWeaponCasualtyCount (PlayerSoldierId INTEGER REFERENCES PlayerSoldier (SoldierId) NOT NULL, RangedWeaponTemplateId INTEGER, Count INTEGER NOT NULL);

-- Table: Request
CREATE TABLE Request (Id INTEGER PRIMARY KEY UNIQUE NOT NULL, CharacterId INTEGER REFERENCES Character (Id) NOT NULL, PlanetId INTEGER REFERENCES Planet (Id) NOT NULL, ThreatFactionId INTEGER, RequestDate INTEGER NOT NULL, ResolutionDate INTEGER, Deadline INTEGER NOT NULL, Status INTEGER NOT NULL, CommitmentKey TEXT NOT NULL, CommitmentDisplayName TEXT NOT NULL, CommitmentDisplayUnit TEXT NOT NULL, PackageCount INTEGER NOT NULL, ServiceWeeks INTEGER NOT NULL, DeadlineWeeks INTEGER NOT NULL, ReferenceBattleValue INTEGER NOT NULL, MaximumEffectivePackageCount INTEGER NOT NULL, QualificationTags TEXT, ProgressBattleValueTime INTEGER NOT NULL, OfferedRequisition INTEGER NOT NULL, OfferedScheduleKind INTEGER NOT NULL, OfferedCadenceWeeks INTEGER NOT NULL, OfferedDeliveryDelayWeeks INTEGER NOT NULL, Severity INTEGER NOT NULL, Hazard INTEGER NOT NULL, HasPlayerResponded BOOLEAN NOT NULL);

CREATE TABLE Pledge (Id INTEGER PRIMARY KEY UNIQUE NOT NULL, SourcePlanetId INTEGER REFERENCES Planet (Id) NOT NULL, GrantingAuthorityId INTEGER NOT NULL, PayloadKind INTEGER NOT NULL, PayloadAmount INTEGER NOT NULL, ScheduleKind INTEGER NOT NULL, CadenceWeeks INTEGER NOT NULL, Status INTEGER NOT NULL, NextDeliveryDate INTEGER NOT NULL);

-- Table: Ship
CREATE TABLE Ship (Id INTEGER PRIMARY KEY UNIQUE NOT NULL, ShipTemplateId INTEGER NOT NULL, FleetId INTEGER REFERENCES Fleet (Id) NOT NULL, Name STRING NOT NULL, IsFlagship BOOLEAN NOT NULL DEFAULT 0);

-- Table: Soldier
CREATE TABLE Soldier (Id INTEGER PRIMARY KEY NOT NULL UNIQUE, SoldierTemplateId INTEGER NOT NULL, SquadId INTEGER REFERENCES Squad (Id), Name STRING NOT NULL, Strength REAL NOT NULL, Dexterity REAL NOT NULL, Constitution REAL NOT NULL, Intelligence REAL NOT NULL, Perception REAL NOT NULL, Ego REAL NOT NULL, Charisma REAL NOT NULL, PsychicPower REAL NOT NULL, AttackSpeed REAL NOT NULL, Size REAL NOT NULL, MoveSpeed REAL NOT NULL);

-- Table: SoldierSkill
CREATE TABLE SoldierSkill (SoldierId INTEGER NOT NULL REFERENCES Soldier (Id), BaseSkillId INTEGER NOT NULL, PointsInvested REAL NOT NULL);

-- Table: Squad
-- Doctrine-following is the default. Only an explicitly customized squad uses its persisted
-- SquadWeaponSet rows directly.
CREATE TABLE Squad (Id INTEGER PRIMARY KEY UNIQUE NOT NULL, SquadTemplateId INTEGER NOT NULL, ParentUnitId INTEGER NOT NULL REFERENCES Unit (Id), Name STRING NOT NULL, LoadedShipId INTEGER REFERENCES Ship (Id), LandedRegionId INTEGER REFERENCES Region(Id), ScoutTrainingOptionKey STRING NOT NULL DEFAULT 'scout.balanced', UsesLoadoutDoctrine BOOLEAN NOT NULL DEFAULT 1, FormationOrdinal INTEGER, HasBattleHistory BOOLEAN NOT NULL DEFAULT 0, DutyStationShipId INTEGER REFERENCES Ship (Id), DutyStationRegionId INTEGER REFERENCES Region(Id), CHECK ((DutyStationShipId IS NOT NULL) <> (DutyStationRegionId IS NOT NULL) OR (DutyStationShipId IS NULL AND DutyStationRegionId IS NULL)), CHECK (LoadedShipId IS NULL AND LandedRegionId IS NULL OR (DutyStationShipId IS NULL AND DutyStationRegionId IS NULL)));

-- Table: SquadWeaponSet
CREATE TABLE SquadWeaponSet (SquadId INTEGER NOT NULL REFERENCES Squad (Id), WeaponSetId INTEGER NOT NULL);

-- Chapter defaults are sparse: no row means the squad template's built-in standard loadout.
CREATE TABLE ChapterLoadout (SquadTemplateId INTEGER PRIMARY KEY NOT NULL);
CREATE TABLE ChapterLoadoutWeaponSet (SquadTemplateId INTEGER NOT NULL REFERENCES ChapterLoadout (SquadTemplateId), WeaponSetId INTEGER NOT NULL);

-- Planetary theater overrides are likewise sparse and inherit from ChapterLoadout when absent.
CREATE TABLE PlanetLoadout (PlanetId INTEGER NOT NULL REFERENCES Planet (Id), SquadTemplateId INTEGER NOT NULL, PRIMARY KEY (PlanetId, SquadTemplateId));
CREATE TABLE PlanetLoadoutWeaponSet (PlanetId INTEGER NOT NULL, SquadTemplateId INTEGER NOT NULL, WeaponSetId INTEGER NOT NULL, FOREIGN KEY (PlanetId, SquadTemplateId) REFERENCES PlanetLoadout (PlanetId, SquadTemplateId));

-- Characters (command staff and specialists) are equipped by role and by individual rather than
-- by squad type, so they sit outside the squad doctrine hierarchy above. Both layers are sparse:
-- no row means "inherit", ending at the role's authored default in the rules database. There is
-- no planetary layer — a chapter fields few enough characters that the individual layer covers
-- what the theater tier does for interchangeable line squads.
CREATE TABLE ChapterCharacterLoadout (SoldierTemplateId INTEGER PRIMARY KEY NOT NULL, WeaponSetId INTEGER NOT NULL);
CREATE TABLE SoldierLoadout (SoldierId INTEGER PRIMARY KEY NOT NULL REFERENCES Soldier (Id), WeaponSetId INTEGER NOT NULL);

-- Itemized equipment doctrine. These rows persist complete equipment compositions rather than
-- fixed weapon columns. Rules-database equipment ids are intentionally not foreign keys because
-- the rules database is read-only and is loaded alongside a save.
CREATE TABLE ChapterEquipmentRoleLoadout (PersonalEquipmentRoleId INTEGER PRIMARY KEY NOT NULL, ArmorEquipmentId INTEGER);
CREATE TABLE ChapterEquipmentRoleLoadoutItem (PersonalEquipmentRoleId INTEGER NOT NULL REFERENCES ChapterEquipmentRoleLoadout (PersonalEquipmentRoleId), EquipmentId INTEGER NOT NULL, Quantity INTEGER NOT NULL, InitialReadyOrder INTEGER);
CREATE TABLE SoldierEquipmentLoadout (SoldierId INTEGER PRIMARY KEY NOT NULL REFERENCES Soldier (Id), ArmorEquipmentId INTEGER);
CREATE TABLE SoldierEquipmentLoadoutItem (SoldierId INTEGER NOT NULL REFERENCES SoldierEquipmentLoadout (SoldierId), EquipmentId INTEGER NOT NULL, Quantity INTEGER NOT NULL, InitialReadyOrder INTEGER);

-- Table: Unit
CREATE TABLE Unit (Id INTEGER PRIMARY KEY UNIQUE NOT NULL, FactionId INTEGER NOT NULL, UnitTemplateId INTEGER NOT NULL, ParentUnitId INTEGER REFERENCES Unit (Id), Name STRING NOT NULL);

-- Canonical campaign-event ledger (introduced in format 8; current schema format 14).
-- PayloadJson is typed by the (EventType,
-- PayloadVersion) registry in code; CLR type names are never persisted.
CREATE TABLE CampaignEvent (Id INTEGER PRIMARY KEY, EventType INTEGER NOT NULL, OccurredWeek INTEGER NOT NULL, RecordedWeek INTEGER NOT NULL, CorrelationKey TEXT, DedupeKey TEXT NOT NULL UNIQUE, PayloadVersion INTEGER NOT NULL, PayloadJson TEXT NOT NULL);
CREATE TABLE CampaignEventEntity (CampaignEventId INTEGER NOT NULL REFERENCES CampaignEvent (Id), EntityKind INTEGER NOT NULL, EntityId INTEGER NOT NULL, EntityRole INTEGER NOT NULL, DisplayName TEXT NOT NULL, SortOrder INTEGER NOT NULL, PRIMARY KEY (CampaignEventId, EntityKind, EntityId, EntityRole));
CREATE TABLE CampaignEventPublication (CampaignEventId INTEGER PRIMARY KEY REFERENCES CampaignEvent (Id), PublishServiceRecord BOOLEAN NOT NULL, PublishTurnReport BOOLEAN NOT NULL, PublishChapterChronicle BOOLEAN NOT NULL, Importance INTEGER NOT NULL, ReasonFlags INTEGER NOT NULL, ChronicleTreatment INTEGER NOT NULL, ClassifierVersion INTEGER NOT NULL);
CREATE TABLE ChapterChronicleEntry (Id INTEGER PRIMARY KEY, OccurredWeek INTEGER NOT NULL, RecordedWeek INTEGER NOT NULL, Importance INTEGER NOT NULL, CorrelationKey TEXT, DedupeKey TEXT NOT NULL UNIQUE, Title TEXT NOT NULL, Body TEXT NOT NULL, NarratorKey TEXT NOT NULL, NarratorVersion INTEGER NOT NULL, NarrativeVariant INTEGER NOT NULL);
CREATE TABLE ChapterChronicleEvent (ChronicleEntryId INTEGER NOT NULL REFERENCES ChapterChronicleEntry (Id), CampaignEventId INTEGER NOT NULL REFERENCES CampaignEvent (Id), SortOrder INTEGER NOT NULL, PRIMARY KEY (ChronicleEntryId, CampaignEventId));
CREATE TABLE ChapterChronicleCallback (ChronicleEntryId INTEGER NOT NULL REFERENCES ChapterChronicleEntry (Id), CampaignEventId INTEGER NOT NULL REFERENCES CampaignEvent (Id), SortOrder INTEGER NOT NULL, PRIMARY KEY (ChronicleEntryId, CampaignEventId));
CREATE TABLE ChapterChronicleAnnotation (Id INTEGER PRIMARY KEY, ChronicleEntryId INTEGER NOT NULL REFERENCES ChapterChronicleEntry (Id), EvidenceEventId INTEGER NOT NULL REFERENCES CampaignEvent (Id), RecordedWeek INTEGER NOT NULL, Body TEXT NOT NULL, NarratorKey TEXT NOT NULL, NarratorVersion INTEGER NOT NULL, DedupeKey TEXT NOT NULL UNIQUE, IsCorrection BOOLEAN NOT NULL);
CREATE TABLE WorldControlEpisode (PlanetId INTEGER PRIMARY KEY REFERENCES Planet (Id), ImperialFactionId INTEGER NOT NULL, LastControllingFactionId INTEGER, WasImperialControlled BOOLEAN NOT NULL, ContestedSinceWeek INTEGER, ChapterParticipated BOOLEAN NOT NULL);
CREATE INDEX IX_CampaignEvent_OccurredWeek ON CampaignEvent (OccurredWeek, Id);
CREATE INDEX IX_CampaignEvent_RecordedWeek ON CampaignEvent (RecordedWeek, Id);
CREATE INDEX IX_CampaignEvent_Correlation ON CampaignEvent (CorrelationKey, Id);
CREATE INDEX IX_CampaignEventEntity_Entity ON CampaignEventEntity (EntityKind, EntityId, CampaignEventId);
CREATE INDEX IX_CampaignEventPublication_Surface ON CampaignEventPublication (PublishChapterChronicle, Importance, CampaignEventId);
CREATE INDEX IX_ChapterChronicleEntry_Date ON ChapterChronicleEntry (OccurredWeek DESC, Id DESC);

-- Table: Mission
-- FactionId has no foreign key: factions live in the read-only rules database, not
-- in the save file, and are matched by id at load time. A cross-database reference
-- cannot be a real SQLite foreign key.
CREATE TABLE Mission (Id INTEGER PRIMARY KEY UNIQUE NOT NULL, MissionType INTEGER NOT NULL, RegionId INTEGER NOT NULL REFERENCES Region (Id), FactionId INTEGER NOT NULL, MissionSize INTEGER NOT NULL, DefenseTypeId INTEGER, IsRegionMission BOOLEAN NOT NULL, TargetBattleValue BIGINT);

-- Table: Order
CREATE TABLE Assignment (Id INTEGER PRIMARY KEY UNIQUE NOT NULL, MissionId INTEGER NOT NULL REFERENCES Mission (Id), IsQuiet BOOLEAN NOT NULL, IsActivelyEngaging BOOLEAN NOT NULL, Aggression INTEGER NOT NULL, OwnerFactionId INTEGER NOT NULL);

-- Table: SquadOrder
CREATE TABLE OrderSquad (OrderId INTEGER NOT NULL REFERENCES Assignment (Id), SquadId INTEGER NOT NULL REFERENCES Squad (Id));

-- Character participants are persisted separately from physical postings. A character may be
-- assigned to an order while remaining physically at the location produced by movement/exfiltration.
CREATE TABLE OrderCharacter (OrderId INTEGER NOT NULL REFERENCES Assignment (Id), SoldierId INTEGER NOT NULL REFERENCES Soldier (Id), PRIMARY KEY (OrderId, SoldierId));

-- Table: IndividualPosting
-- Save-owned physical location for a soldier away from his home formation. Order membership lives
-- in OrderCharacter; this table intentionally contains no order lifetime/commitment column.
CREATE TABLE IndividualPosting (SoldierId INTEGER PRIMARY KEY REFERENCES Soldier (Id), Purpose INTEGER NOT NULL, LoadedShipId INTEGER REFERENCES Ship (Id), LandedRegionId INTEGER REFERENCES Region (Id), StartedDate INTEGER NOT NULL, CHECK ((LoadedShipId IS NOT NULL) <> (LandedRegionId IS NOT NULL)));


COMMIT TRANSACTION;
PRAGMA foreign_keys = on;
