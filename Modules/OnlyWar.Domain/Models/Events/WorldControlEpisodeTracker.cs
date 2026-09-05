using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Models.Events
{
    /// <summary>
    /// Persistable state machine for Imperial world-control episodes. A contested observation
    /// opens an episode but never completes one. Callers persist <see cref="States"/> with the
    /// campaign and restore them through the constructor.
    /// </summary>
    public sealed record WorldControlEpisodeState(
        int PlanetId,
        int ImperialFactionId,
        int? LastControllingFactionId,
        bool WasImperialControlled,
        int? ContestedSinceWeek,
        bool ChapterParticipated);

    public sealed class WorldControlEpisodeTracker
    {
        private readonly Dictionary<int, WorldControlEpisodeState> _states;

        public WorldControlEpisodeTracker(IEnumerable<WorldControlEpisodeState> states = null)
        {
            _states = (states ?? []).ToDictionary(state => state.PlanetId);
        }

        public IReadOnlyCollection<WorldControlEpisodeState> States => _states.Values;

        public WorldControlChangedPayload Observe(
            int planetId,
            string planetName,
            int imperialFactionId,
            int? controllingFactionId,
            bool isContested,
            int week,
            bool chapterParticipatedThisWeek = false,
            bool? isImperialControlled = null)
        {
            if (!_states.TryGetValue(planetId, out WorldControlEpisodeState state))
            {
                _states[planetId] = new WorldControlEpisodeState(
                    planetId,
                    imperialFactionId,
                    controllingFactionId,
                    isImperialControlled ?? controllingFactionId == imperialFactionId,
                    null,
                    false);
                return null;
            }

            bool participated = state.ChapterParticipated || chapterParticipatedThisWeek;
            if (isContested)
            {
                _states[planetId] = state with
                {
                    LastControllingFactionId = controllingFactionId,
                    ContestedSinceWeek = state.WasImperialControlled
                        ? state.ContestedSinceWeek ?? week
                        : state.ContestedSinceWeek,
                    ChapterParticipated = participated
                };
                return null;
            }

            bool nowImperial = isImperialControlled ?? controllingFactionId == imperialFactionId;
            WorldControlChangedPayload completed = null;
            if (state.WasImperialControlled && !nowImperial)
            {
                completed = new WorldControlChangedPayload(
                    planetId,
                    planetName,
                    imperialFactionId,
                    state.LastControllingFactionId ?? imperialFactionId,
                    controllingFactionId ?? -1,
                    state.ContestedSinceWeek ?? week,
                    week,
                    participated,
                    false);
            }
            else if (state.ContestedSinceWeek.HasValue && nowImperial)
            {
                completed = new WorldControlChangedPayload(
                    planetId,
                    planetName,
                    imperialFactionId,
                    state.LastControllingFactionId ?? imperialFactionId,
                    imperialFactionId,
                    state.ContestedSinceWeek.Value,
                    week,
                    participated,
                    true);
            }

            _states[planetId] = new WorldControlEpisodeState(
                planetId,
                imperialFactionId,
                controllingFactionId,
                nowImperial,
                null,
                false);
            return completed;
        }
    }
}
