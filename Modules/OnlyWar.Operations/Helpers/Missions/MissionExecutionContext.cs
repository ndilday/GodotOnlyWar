using OnlyWar.Contracts.Operations;
using OnlyWar.Contracts.Battles;
using OnlyWar.Builders;
using OnlyWar.Models;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Recruitment;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
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
        /// <summary>
        /// The only tactical dependency visible to mission policy. The concrete Battles resolver is
        /// composed by the Application adapter and never constructed by a mission step.
        /// </summary>
        public IEngagementResolver Engagements { get; }
        public IEntityIdAllocator EntityIds { get; }
        public MissionCampaignInputs Campaign { get; }
        public IOperationsPersonnelSurface Personnel { get; }
        public IEngagementElementFactory EngagementElements { get; }

        public MissionExecutionContext(
            MissionContext state,
            MissionRules rules,
            IRNG random,
            IEngagementResolver engagements)
            : this(state, rules, random, engagements, new TacticalEntityIdAllocator())
        {
        }

        public MissionExecutionContext(
            MissionContext state,
            MissionRules rules,
            IRNG random,
            IEngagementResolver engagements,
            IEntityIdAllocator entityIds,
            MissionCampaignInputs campaign = null,
            IEngagementElementFactory engagementElements = null)
        {
            Campaign = campaign ?? new MissionCampaignInputs(new OnlyWar.Models.Date(1), state?.OperationalDoctrine, state?.RecruitmentProgram);
            State = state ?? throw new ArgumentNullException(nameof(state));
            Rules = rules ?? throw new ArgumentNullException(nameof(rules));
            Random = random ?? throw new ArgumentNullException(nameof(random));
            Engagements = engagements ?? throw new ArgumentNullException(nameof(engagements));
            EntityIds = entityIds ?? throw new ArgumentNullException(nameof(entityIds));
            Personnel = Campaign.Personnel ?? OperationsPersonnelDefaults.Current;
            EngagementElements = engagementElements;
        }
    }
}

