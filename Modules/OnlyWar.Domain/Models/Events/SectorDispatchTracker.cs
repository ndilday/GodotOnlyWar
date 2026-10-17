using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Domain.Events
{
    /// <summary>
    /// Per-world state that sector dispatch detection must remember across turns and saves
    /// (OnlyWar_TDD.md §6.1): an open danger episode, and an active revolt with
    /// whether the Chapter fought it at any time while it lasted.
    /// </summary>
    public sealed record SectorDispatchPlanetState(
        int PlanetId,
        bool InDanger,
        bool RevoltActive,
        bool RevoltChapterParticipated);

    /// <summary>The last Sector Lord the dispatches knew of, and the world that held the seat.</summary>
    public sealed record SectorLordState(int? CharacterId, int? CapitalPlanetId);

    /// <summary>What one end-of-turn observation of a world found.</summary>
    public readonly record struct SectorDispatchPlanetChange(
        bool DangerOpened,
        bool RevoltEnded,
        bool RevoltChapterParticipated);

    /// <summary>
    /// Persistable state machine for the dispatch triggers that need memory. Like
    /// <see cref="WorldControlEpisodeTracker"/>, the first observation of a world (or of the Sector
    /// Lord) only records it: a campaign never opens with a burst of reports about the state it
    /// started in.
    /// </summary>
    public sealed class SectorDispatchTracker
    {
        private readonly Dictionary<int, SectorDispatchPlanetState> _planets;

        public SectorDispatchTracker(
            IEnumerable<SectorDispatchPlanetState> planets = null,
            SectorLordState sectorLord = null)
        {
            _planets = (planets ?? []).ToDictionary(state => state.PlanetId);
            SectorLord = sectorLord;
        }

        public IReadOnlyCollection<SectorDispatchPlanetState> States => _planets.Values;

        /// <summary>Null until the first observation of the Sector Lord seat.</summary>
        public SectorLordState SectorLord { get; private set; }

        /// <summary>
        /// A world is in danger once at least one third of its regions have public hostile
        /// presence, and the episode ends only when the share falls below one sixth.
        /// </summary>
        public static bool OpensDanger(int hostileRegions, int totalRegions) =>
            totalRegions > 0 && hostileRegions * 3 >= totalRegions;

        public static bool ClosesDanger(int hostileRegions, int totalRegions) =>
            totalRegions <= 0 || hostileRegions * 6 < totalRegions;

        /// <summary>Records a world not seen before, without reporting anything.</summary>
        public void Seed(int planetId, int hostileRegions, int totalRegions, bool isImperial, bool revoltActive)
        {
            if (_planets.ContainsKey(planetId)) return;
            _planets[planetId] = new SectorDispatchPlanetState(
                planetId,
                isImperial && OpensDanger(hostileRegions, totalRegions),
                revoltActive,
                false);
        }

        public SectorDispatchPlanetChange Observe(
            int planetId,
            int hostileRegions,
            int totalRegions,
            bool isImperial,
            bool revoltActive,
            bool chapterParticipatedThisWeek)
        {
            if (!_planets.TryGetValue(planetId, out SectorDispatchPlanetState state))
            {
                Seed(planetId, hostileRegions, totalRegions, isImperial, revoltActive);
                return default;
            }

            bool dangerOpened = false;
            bool inDanger = state.InDanger;
            if (!isImperial)
            {
                // A lost world is reported as lost, not as in danger. If it is retaken while the
                // enemy still holds a third of it, a new episode opens.
                inDanger = false;
            }
            else if (!inDanger && OpensDanger(hostileRegions, totalRegions))
            {
                inDanger = true;
                dangerOpened = true;
            }
            else if (inDanger && ClosesDanger(hostileRegions, totalRegions))
            {
                inDanger = false;
            }

            bool revoltParticipated = (state.RevoltActive && state.RevoltChapterParticipated)
                || chapterParticipatedThisWeek;
            bool revoltEnded = state.RevoltActive && !revoltActive;
            _planets[planetId] = new SectorDispatchPlanetState(
                planetId,
                inDanger,
                revoltActive,
                revoltActive && revoltParticipated);
            return new SectorDispatchPlanetChange(dangerOpened, revoltEnded, revoltEnded && revoltParticipated);
        }

        /// <summary>Records the Sector Lord seat the first time, without reporting it.</summary>
        public void SeedSectorLord(int? characterId, int? capitalPlanetId)
        {
            SectorLord ??= new SectorLordState(characterId, capitalPlanetId);
        }

        /// <summary>
        /// Returns the previous holder when a new character holds the seat, otherwise null. A seat
        /// that falls vacant is remembered as vacant but is not itself reported: the loss of the
        /// capital is reported as a lost world.
        /// </summary>
        public SectorLordState ObserveSectorLord(int? characterId, int? capitalPlanetId)
        {
            if (SectorLord == null)
            {
                SeedSectorLord(characterId, capitalPlanetId);
                return null;
            }

            SectorLordState previous = SectorLord;
            SectorLord = new SectorLordState(characterId, capitalPlanetId);
            return characterId.HasValue && characterId != previous.CharacterId ? previous : null;
        }
    }
}
