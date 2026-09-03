using OnlyWar.Helpers.UI;
using OnlyWar.Models;
using OnlyWar.Models.Battles;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Battles
{
    /// <summary>
    /// The campaign-to-battle boundary for squad force construction. Player squads receive an
    /// explicit duty-ready participant set; NPC squads retain their existing combat-effective
    /// construction because Chapter doctrine belongs only to the player's Army.
    /// </summary>
    public static class BattleSquadFactory
    {
        public static BattleSquad Create(
            bool isPlayerSquad,
            Squad squad,
            ChapterOperationalDoctrine doctrine = null)
        {
            if (!isPlayerSquad)
            {
                return new BattleSquad(false, squad);
            }

            ChapterOperationalDoctrine resolvedDoctrine =
                SquadStrengthSnapshotBuilder.ResolveDoctrine(squad, doctrine);
            IReadOnlyList<ISoldier> participants = GetParticipants(squad, resolvedDoctrine);
            return new BattleSquad(true, squad, participants);
        }

        public static IReadOnlyList<ISoldier> GetParticipants(
            Squad squad,
            ChapterOperationalDoctrine doctrine = null)
        {
            if (squad == null) return Array.Empty<ISoldier>();

            ChapterOperationalDoctrine resolvedDoctrine =
                SquadStrengthSnapshotBuilder.ResolveDoctrine(squad, doctrine);
            SquadReadinessSnapshot readiness = SquadReadinessService.Evaluate(
                squad, doctrine: resolvedDoctrine);
            if (resolvedDoctrine != null && readiness.StructuralBlockers.Count > 0)
            {
                return Array.Empty<ISoldier>();
            }

            return SoldierPresenceService.PresentMembers(squad)
                .Where(member => DutyReadinessService.Evaluate(member, resolvedDoctrine).IsDutyReady)
                .ToList();
        }

        /// <summary>
        /// Builds the one-person battle element for an individually attached character. The
        /// character is checked independently of its home formation's squad gates; the campaign
        /// squad remains attached to the element as the identity/equipment/history anchor.
        /// </summary>
        public static BattleSquad CreateAttachedCharacter(
            PlayerSoldier character,
            int tacticalId,
            Faction fallbackFaction,
            ChapterOperationalDoctrine doctrine = null)
        {
            if (character == null) return null;
            ChapterOperationalDoctrine resolvedDoctrine =
                SquadStrengthSnapshotBuilder.ResolveDoctrine(character.AssignedSquad, doctrine);
            if (!DutyReadinessService.Evaluate(character, resolvedDoctrine).IsDutyReady)
            {
                return null;
            }

            return new BattleSquad(new BattleElementSpec(
                tacticalId,
                character.Name,
                character.AssignedSquad?.Faction ?? fallbackFaction,
                new ISoldier[] { character },
                new BattleElementTraits(
                    IsHeadquarters: character.AssignedSquad?.SquadTemplate?.SquadType
                        .HasFlag(SquadTypes.HQ) == true),
                CampaignSquad: character.AssignedSquad,
                CampaignCharacter: character));
        }
    }
}
