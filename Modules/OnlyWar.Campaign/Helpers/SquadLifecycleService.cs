using OnlyWar.Models;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Recruitment;
using OnlyWar.Models.Squads;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers
{
    public enum EmptySquadLifecycleResult
    {
        NotEmpty,
        Retained,
        Discarded
    }

    /// <summary>Owns deployment cleanup and retention whenever a player squad becomes empty.</summary>
    public sealed class SquadLifecycleService
    {
        private readonly Army _army;
        private readonly RecruitmentProgram _recruitmentProgram;
        private readonly IDictionary<int, Squad> _squadMap;

        public SquadLifecycleService(PlayerForce force)
            : this(force?.Army, force?.RecruitmentProgram, force?.Army?.SquadMap) { }

        public SquadLifecycleService(
            Army army = null,
            RecruitmentProgram recruitmentProgram = null,
            IDictionary<int, Squad> squadMap = null)
        {
            _army = army;
            _recruitmentProgram = recruitmentProgram;
            _squadMap = squadMap;
        }

        public EmptySquadLifecycleResult HandleEmptySquad(Squad squad)
        {
            if (squad == null) throw new ArgumentNullException(nameof(squad));
            if (squad.Members.Count != 0) return EmptySquadLifecycleResult.NotEmpty;

            DetachDeployment(squad);
            bool scout = (squad.SquadTemplate?.SquadType & SquadTypes.Scout) != 0;
            if (!scout || squad.HasBattleHistory)
            {
                return EmptySquadLifecycleResult.Retained;
            }

            if (_recruitmentProgram != null)
            {
                foreach (RecruitmentProcedure procedure in _recruitmentProgram.Procedures
                    .Where(procedure => procedure.ReservedSquadId == squad.Id))
                {
                    procedure.ReservedSquadId = null;
                }
            }
            squad.ParentUnit?.RemoveSquad(squad);
            if (_army != null) _army.UnregisterSquad(squad);
            else _squadMap?.Remove(squad.Id);
            return EmptySquadLifecycleResult.Discarded;
        }

        public static void DetachDeployment(Squad squad)
        {
            if (squad.CurrentOrders != null)
            {
                squad.CurrentOrders.AssignedSquads.Remove(squad);
                squad.CurrentOrders = null;
            }
            squad.BoardedLocation?.RemoveSquad(squad);
            squad.BoardedLocation = null;
            FindRegionFaction(squad)?.LandedSquads.Remove(squad);
            squad.CurrentRegion = null;
        }

        private static RegionFaction FindRegionFaction(Squad squad)
        {
            if (squad?.CurrentRegion == null) return null;
            if (squad.Faction != null && squad.CurrentRegion.RegionFactionMap.TryGetValue(
                    squad.Faction.Id, out RegionFaction factionPresence))
            {
                return factionPresence;
            }
            return squad.CurrentRegion.RegionFactionMap.Values
                .FirstOrDefault(entry => entry.LandedSquads.Contains(squad));
        }
    }
}
