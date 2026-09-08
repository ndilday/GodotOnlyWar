using OnlyWar.Battles.Abstractions;
using OnlyWar.Models.Recruitment;
using OnlyWar.Helpers.Readiness;
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
            ChapterOperationalDoctrine doctrine = null,
            RecruitmentProgram program = null, IBattleEquipmentSource equipment = null)
        {
            if (!isPlayerSquad)
            {
                return new BattleSquad(false, squad, equipment: equipment);
            }

            ChapterOperationalDoctrine resolvedDoctrine =
                doctrine;
            IReadOnlyList<ISoldier> participants = GetParticipants(squad, resolvedDoctrine, program);
            return new BattleSquad(true, squad, participants, equipment);
        }

        public static IReadOnlyList<ISoldier> GetParticipants(
            Squad squad,
            ChapterOperationalDoctrine doctrine = null,
            RecruitmentProgram program = null, IBattleEquipmentSource equipment = null)
        {
            if (squad == null) return Array.Empty<ISoldier>();

            ChapterOperationalDoctrine resolvedDoctrine =
                doctrine;
            SquadReadinessSnapshot readiness = SquadReadinessService.Evaluate(squad, program: program, doctrine: resolvedDoctrine);
            if (resolvedDoctrine != null && readiness.StructuralBlockers.Count > 0)
            {
                return Array.Empty<ISoldier>();
            }

            return SoldierPresenceService.PresentMembers(squad)
                .Where(member => DutyReadinessService.Evaluate(member, doctrine: resolvedDoctrine, recruitmentProgram: program).IsDutyReady)
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
            ChapterOperationalDoctrine doctrine = null,
            RecruitmentProgram program = null, IBattleEquipmentSource equipment = null)
        {
            if (character == null) return null;
            ChapterOperationalDoctrine resolvedDoctrine =
                doctrine;
            if (!DutyReadinessService.Evaluate(character, doctrine: resolvedDoctrine, recruitmentProgram: program).IsDutyReady)
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
                CampaignCharacter: character), equipment);
        }
    }
}

