
namespace OnlyWar.Models.Missions
{
    public enum MissionType
    {
        LightningRaid,
        Infiltrate,
        EstablishAirhead,
        CloseAirSupport,
        HitAndRun,
        Recon,
        Patrol,
        Advance,
        DeepStrike,
        Fortify,
        DefenseInDepth,
        LastStand,
        Assassination,
        ObjectiveRaid,
        Sabotage,
        Ambush,
        Diversion,
        Extermination,
        Training,
        Construction,
        // A sustained, visible Astartes presence posted in answer to a planetary governor's
        // petition - see PresenceRequest and RequestFulfillmentKind.ForceCommitment. Squads
        // holding this order accrue the squad-weeks that fulfil the request; it is the only
        // order that does so. Appended deliberately: MissionType persists as an int ordinal
        // (PlanetDataAccess.SaveMission), so inserting above this point would corrupt saves.
        ShowOfForce,
        // A Consumption swarm's biomass feeding, planned and budgeted like any other tasking
        // (Design/Reference/ConsumptionFeedingAsMission.md). Squad-less: the committed battle value
        // lives on FeedMission and resolves instantly in the mission phase. Appended for the same
        // save-ordinal reason as ShowOfForce above.
        Feed,
        // The 10th Company's standing recruitment task. It is an order for character
        // participants, but resolves through RecruitmentTurnProcessor rather than combat.
        Recruitment
    }
}
