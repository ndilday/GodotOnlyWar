using System;
using System.Collections.Generic;

using OnlyWar.Models.Battles;
using OnlyWar.Models.Soldiers;

namespace OnlyWar.Helpers.Battles
{
    /// <summary>
    /// Per-turn command-aura modifier (Design/Active/MoraleAndRout.md §4.3, Phase 6): the
    /// weak-coefficient generalisation of synapse. An HQ squad (SquadTypes.HQ — the RIGHT
    /// set for command, unlike synapse; §3.2) projects a shorter, weaker aura that supplies
    /// a negative stress term to nearby friendly squads, and whose loss supplies a positive
    /// one. Same geometric pattern as <see cref="SynapseCoverageEvaluator"/>, different
    /// semantics: this feeds the signed -w6 stress term and is NEVER a check skip.
    ///
    /// Stateless and recomputed every call (§5.2), like everything else in the morale
    /// system. HQ-LOSS READING (chosen over tracking where each HQ died, which would need
    /// resolver-side death-position state the morale system deliberately has none of): a
    /// side whose every fielded HQ squad has been destroyed applies the loss term to squads
    /// that are in no living HQ's aura — "the force knows its command is gone." Because the
    /// check is stateless the loss term persists for the rest of the battle, which reads as
    /// decapitation continuing to pressure the force rather than a one-turn shock.
    /// </summary>
    public static class CommandAuraEvaluator
    {
        /// <summary>
        /// The signed §5.2 commandAuraSupport term for <paramref name="squad"/>:
        /// +<see cref="MoraleConstants.CommandAuraSupportStrength"/> when a living,
        /// same-faction HQ squad other than itself has an able soldier within
        /// that HQ's <see cref="BattleSquad.GetCommandAuraRadius"/> of one of its able soldiers
        /// (NOT stacking — one HQ is enough and multiple HQs never sum; every source
        /// supplies the same constant, so first-hit == max);
        /// -<see cref="MoraleConstants.CommandLossStress"/> when the side fielded HQ
        /// squad(s) and every one has been destroyed;
        /// otherwise 0 (no HQ ever fielded, or a commander survives but is out of range —
        /// alive-and-elsewhere is no aura, not a death shock; a disengaged HQ likewise
        /// counts as alive-but-gone, never as destroyed).
        /// </summary>
        /// <param name="squad">The squad whose aura state is being read.</param>
        /// <param name="friendlySquads">
        /// The side's FULL roster (e.g. <c>BattleState.AllAttackerSquads.Values</c>), not
        /// just the active squads — destroyed HQ squads must remain visible for the loss
        /// reading. May include <paramref name="squad"/> itself; a squad never sources its
        /// own aura.
        /// </param>
        /// <param name="grid">Grid used to measure soldier-to-soldier distance.</param>
        /// <param name="tacticsSkill">
        /// The Tactics base skill, used to derive each provider's personal aura radius
        /// (see <see cref="BattleSquad.GetCommandAuraRadius"/>).
        /// </param>
        public static float ComputeCommandAuraModifier(
            BattleSquad squad,
            IEnumerable<BattleSquad> friendlySquads,
            BattleGridManager grid,
            BaseSkill tacticsSkill)
        {
            ArgumentNullException.ThrowIfNull(squad);
            ArgumentNullException.ThrowIfNull(friendlySquads);
            ArgumentNullException.ThrowIfNull(grid);
            ArgumentNullException.ThrowIfNull(tacticsSkill);

            List<BattleSoldier> receivers = squad.AbleSoldiers;
            if (receivers.Count == 0)
            {
                return 0f;
            }

            int? factionId = squad.Squad?.Faction?.Id;
            bool sawHq = false;
            bool anySurvivingHq = false;

            foreach (BattleSquad provider in friendlySquads)
            {
                if (provider == null || provider.Id == squad.Id)
                {
                    continue;
                }
                if (!provider.SquadProvidesCommandAura)
                {
                    continue;
                }
                if (provider.Squad?.Faction?.Id != factionId)
                {
                    continue;
                }
                sawHq = true;
                // "Destroyed" mirrors the synapse liveness rule (§4.2/§5.1): post-round
                // state, so an HQ wiped this round is dead this very turn — no grace period.
                bool destroyed = provider.Status == BattleSquadStatus.Eliminated
                    || provider.AbleSoldiers.Count == 0;
                if (destroyed)
                {
                    continue;
                }
                anySurvivingHq = true;
                if (provider.Status == BattleSquadStatus.Active
                    && IsWithinRadius(
                        receivers,
                        provider.AbleSoldiers,
                        provider.GetCommandAuraRadius(tacticsSkill),
                        grid))
                {
                    return MoraleConstants.CommandAuraSupportStrength;
                }
            }

            return sawHq && !anySurvivingHq ? -MoraleConstants.CommandLossStress : 0f;
        }

        private static bool IsWithinRadius(
            List<BattleSoldier> receivers,
            List<BattleSoldier> providerSoldiers,
            float radius,
            BattleGridManager grid)
        {
            foreach (BattleSoldier receiver in receivers)
            {
                foreach (BattleSoldier provider in providerSoldiers)
                {
                    float distance = grid.GetDistanceBetweenSoldiers(
                        receiver.Soldier.Id, provider.Soldier.Id);
                    if (distance <= radius)
                    {
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
