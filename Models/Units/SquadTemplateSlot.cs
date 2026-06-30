using OnlyWar.Models.Squads;

namespace OnlyWar.Models.Units
{
    // Describes how many squads of a given template a unit may hold. MinCount
    // squads are created eagerly when the unit is generated (e.g. the chapter's
    // singleton command squads); squads up to MaxCount are created on demand as
    // soldiers are assigned (e.g. a company's line squads).
    public sealed class SquadTemplateSlot
    {
        public SquadTemplate Template { get; }
        public int MinCount { get; }
        public int MaxCount { get; }

        public SquadTemplateSlot(SquadTemplate template, int minCount, int maxCount)
        {
            Template = template;
            MinCount = minCount;
            MaxCount = maxCount;
        }
    }
}
