using OnlyWar.Operations.Abstractions;
using OnlyWar.Battles.Abstractions;
using OnlyWar.Domain;
using OnlyWar.Domain.Planets;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Squads;
using System.Linq;

namespace OnlyWar.Operations.Missions
{
    /// <summary>
    /// Physically moves a mission force into the region its mission took it to.
    /// </summary>
    /// <remarks>
    /// Before this existed, nothing moved a player force after an Advance. MissionReturnPolicy.Hold
    /// only suppressed the exfiltration step, so a victorious assault "held" the ground from its
    /// staging region: it stayed in LandedSquads there, and its next order into the same region
    /// began with a fresh infiltration. The one path that did relocate a force was the exfiltration
    /// timeout, and its move code now lives here so both share it.
    /// </remarks>
    internal static class MissionForceRelocation
    {
        /// <summary>
        /// Moves a Hold-policy force into its target region when it got there and was not thrown
        /// back out. Returns true when the force was moved.
        /// </summary>
        /// <remarks>
        /// "Got there" is <see cref="MissionContext.ForceEnteredTargetRegion"/>, which only
        /// InfiltrateMissionStep sets, so a force that began in the target region is left alone -
        /// it is already where it should be. A force that withdrew under fire, was lost, broke off
        /// its assault on losses, or was beaten in a reciprocal assault falls back to where it
        /// started. A force that spent the week in the region without clearing it stays: it holds a
        /// foothold, which is what Hold means.
        /// </remarks>
        internal static bool ResolveHeldGround(
            MissionContext context,
            Date currentDate,
            IPhysicalPostingCommands personnel)
        {
            if (context?.Order?.Mission?.RegionFaction == null) return false;
            if (MissionReturnPolicies.GetPolicy(context.Order.Mission.MissionType)
                != MissionReturnPolicy.Hold)
            {
                return false;
            }
            if (!context.ForceEnteredTargetRegion
                || context.ForceWithdrewUnderFire
                || context.ForceLostContact
                || context.ForceReturnedToBase
                || context.AssaultBrokenOff
                || context.ReciprocalAssaultDefeated
                || !context.MissionSquads.Any(squad => squad.AbleMembers.Count > 0))
            {
                return false;
            }

            Region target = context.Order.Mission.RegionFaction.Region;
            MoveForce(context, target, registerAsLanded: true, currentDate, personnel);
            context.ForceHeldTargetRegion = true;
            GameLog.Debug(() =>
                $"Held ground {context.Order.Mission.MissionType} -> "
                + $"{target.Planet?.Name}/{target.Name}: "
                + $"squads={context.MissionSquads.Count(squad => squad.CampaignSquad != null)}");
            return true;
        }

        /// <summary>
        /// Moves every squad and attached character of the mission force into <paramref name="region"/>.
        /// </summary>
        /// <param name="registerAsLanded">
        /// True to register each squad as a landed, public presence of its faction there. False for a
        /// force lost behind enemy lines, which is in the region but is not an organised presence.
        /// </param>
        internal static void MoveForce(
            MissionContext context,
            Region region,
            bool registerAsLanded,
            Date currentDate,
            IPhysicalPostingCommands personnel)
        {
            foreach (OperationalMissionElement missionSquad in context.MissionSquads)
            {
                if (missionSquad?.CampaignSquad != null)
                {
                    Squad squad = missionSquad.CampaignSquad;
                    Region previousRegion = squad.CurrentRegion;
                    if (previousRegion != null
                        && squad.Faction != null
                        && previousRegion.RegionFactionMap.TryGetValue(
                            squad.Faction.Id, out RegionFaction previousPresence))
                    {
                        previousPresence.LandedSquads.Remove(squad);
                    }
                    squad.CurrentRegion = region;

                    if (!registerAsLanded || squad.Faction == null || region?.Planet == null)
                    {
                        continue;
                    }

                    if (!region.Planet.PlanetFactionMap.TryGetValue(
                        squad.Faction.Id, out PlanetFaction planetPresence))
                    {
                        planetPresence = new PlanetFaction(squad.Faction) { IsPublic = true };
                        region.Planet.PlanetFactionMap[squad.Faction.Id] = planetPresence;
                    }
                    if (!region.RegionFactionMap.TryGetValue(
                        squad.Faction.Id, out RegionFaction regionalPresence))
                    {
                        regionalPresence = new RegionFaction(planetPresence, region) { IsPublic = true };
                        region.RegionFactionMap[squad.Faction.Id] = regionalPresence;
                    }
                    else
                    {
                        regionalPresence.IsPublic = true;
                    }
                    if (!regionalPresence.LandedSquads.Contains(squad))
                    {
                        regionalPresence.LandedSquads.Add(squad);
                    }
                }
                else if (missionSquad?.CampaignCharacter != null)
                {
                    PlayerSoldier character = missionSquad.CampaignCharacter;
                    IndividualPostingPurpose purpose = character.IndividualPosting?.Purpose
                        ?? IndividualPostingPurpose.Independent;
                    personnel?.RestorePhysical(
                        character,
                        purpose,
                        CampaignLocation.Landed(region),
                        currentDate ?? new Date(1));
                }
            }
        }
    }
}
