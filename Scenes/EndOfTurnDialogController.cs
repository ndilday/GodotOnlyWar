using OnlyWar.Models.Missions;
using System.Collections.Generic;
using System.Linq;


public partial class EndOfTurnDialogController : DialogController
{
    private EndOfTurnDialogView _view;

    public override void _Ready()
    {
        base._Ready();
        _view = GetNode<EndOfTurnDialogView>("DialogView");
    }

    public void AddData(IEnumerable<MissionContext> missionContexts, IEnumerable<Mission> specialMissions)
    {
        List<string> strings = new List<string>();
        foreach (MissionContext context in missionContexts)
        {
            strings.AddRange(context.Log);
        }
        foreach (Mission mission in specialMissions)
        {
            strings.Add($"We have a {mission.MissionType} opportunity in {mission.RegionFaction.Region.Name}");
        }

        _view.AddData(strings);
    }
}
