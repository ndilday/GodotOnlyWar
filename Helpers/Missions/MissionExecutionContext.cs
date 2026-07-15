using OnlyWar.Builders;
using OnlyWar.Helpers.Battles;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Soldiers;
using System;

namespace OnlyWar.Helpers.Missions
{
    /// <summary>
    /// The named rules a mission may use. Keeping this projection deliberately small prevents
    /// mission steps from reaching back into the complete game-rules database.
    /// </summary>
    public sealed class MissionRules
    {
        public BaseSkill Stealth { get; }
        public BaseSkill Tactics { get; }

        public MissionRules(BaseSkill stealth, BaseSkill tactics)
        {
            Stealth = stealth ?? throw new ArgumentNullException(nameof(stealth));
            Tactics = tactics ?? throw new ArgumentNullException(nameof(tactics));
        }
    }

    /// <summary>
    /// Bounded runtime for one mission. The mutable outcome remains in <see cref="MissionContext"/>;
    /// this wrapper adds only the explicit dependencies mission execution needs.
    /// </summary>
    public sealed class MissionExecutionContext
    {
        public MissionContext State { get; }
        public MissionRules Rules { get; }
        public IRNG Random { get; }
        public BattleExecutionContext Battle { get; }
        public IEntityIdAllocator EntityIds { get; }

        public MissionExecutionContext(
            MissionContext state,
            MissionRules rules,
            IRNG random,
            BattleExecutionContext battle)
            : this(state, rules, random, battle, new TacticalEntityIdAllocator())
        {
        }

        public MissionExecutionContext(
            MissionContext state,
            MissionRules rules,
            IRNG random,
            BattleExecutionContext battle,
            IEntityIdAllocator entityIds)
        {
            State = state ?? throw new ArgumentNullException(nameof(state));
            Rules = rules ?? throw new ArgumentNullException(nameof(rules));
            Random = random ?? throw new ArgumentNullException(nameof(random));
            Battle = battle ?? throw new ArgumentNullException(nameof(battle));
            EntityIds = entityIds ?? throw new ArgumentNullException(nameof(entityIds));
            if (!ReferenceEquals(random, battle.Random))
            {
                throw new ArgumentException(
                    "Mission and embedded battles must share the same random stream.",
                    nameof(battle));
            }
        }
    }
}
