
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
        ShowOfForce
    }
}
