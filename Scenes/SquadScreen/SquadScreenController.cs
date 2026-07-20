using Godot;
using OnlyWar.Helpers.Extensions;
using OnlyWar.Models;
using OnlyWar.Models.Equippables;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Linq;

public partial class SquadScreenController : DialogController
{
    private Squad _squad;
    private SquadScreenView _view;
    private int _ableBodied;
    private List<WeaponSet> _savedLoadout;
    private int _savedLoadoutSquadTemplateId;

    public event EventHandler CampaignChanged;

    public override void _Ready()
    {
        base._Ready();
        _view = GetNode<SquadScreenView>("DialogView");
        _view.WeaponSetSelectionWeaponSetCountChanged += OnWeaponSetSelectionWeaponSetCountChanged;
        _view.CopyLoadout += OnCopyLoadout;
        _view.PasteLoadout += OnPasteLoadout;
    }

    private void OnCopyLoadout(object sender, EventArgs e)
    {
        // **You need to implement the logic to copy the current loadout to the clipboard.**
        _savedLoadout = _squad.Loadout;
        _savedLoadoutSquadTemplateId = _squad.SquadTemplate.Id;
        _view.DisablePasteLoadout(false);
    }

    private void OnPasteLoadout(object sender, EventArgs e)
    {
        // **You need to implement the logic to paste the loadout from the clipboard.**
        _squad.Loadout = _savedLoadout.ToList();
        CampaignChanged?.Invoke(this, EventArgs.Empty);
        PopulateSquadLoadout();

    }

    private void OnWeaponSetSelectionWeaponSetCountChanged(object sender, ValueTuple<string, int> args)
    {
        WeaponSetSelectionView view = (WeaponSetSelectionView)sender;
        string optionName = view.OptionName;
        var matchingLoadouts = _squad.Loadout.Where(ws => ws.Name == args.Item1);
        bool changed = matchingLoadouts.Count() != args.Item2;
        if(matchingLoadouts.Count() > args.Item2)
        {
            // remove the difference between the current count and the new count
            for (int i = 0; i < matchingLoadouts.Count() - args.Item2; i++)
            {
                _squad.Loadout.Remove(matchingLoadouts.First());
                _squad.Loadout.Add(_squad.SquadTemplate.DefaultWeapons);
            }
        }
        else if(matchingLoadouts.Count() < args.Item2)
        {
            // add the difference between the current count and the new count
            for (int i = 0; i < args.Item2 - matchingLoadouts.Count(); i++)
            {
                _squad.Loadout.Remove(_squad.SquadTemplate.DefaultWeapons);
                _squad.Loadout.Add(_squad.SquadTemplate.WeaponOptions.First(o => o.Name == optionName).Options.First(o => o.Name == args.Item1));
            }
        }
        int defaultCount = _ableBodied - _squad.Loadout.Where(ws => ws != _squad.SquadTemplate.DefaultWeapons).Count();
        _view.SetDefaultWeaponSetCount(defaultCount);
        _view.DisableCountIncreases(defaultCount == 0);
        if (changed) CampaignChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetSquad(Squad squad)
    {
        _squad = squad;
        _ableBodied = _squad.Members.Where(s => CanFight(s)).Count();
        PopulateSquadDetails();
        PopulateSquadLoadout();
        PopulateSquadMembers();
        _view.DisablePasteLoadout(_savedLoadout == null || _savedLoadoutSquadTemplateId != _squad.SquadTemplate.Id);
    }

    private void PopulateSquadDetails()
    {
        List<ValueTuple<string, string>> lines = [];
        lines.Add(new ValueTuple<string, string>("Squad Name", _squad.Name));
        lines.Add(new ValueTuple<string, string>("Squad Type", _squad.SquadTemplate.Name));
        if (_squad.CurrentRegion != null)
        {
            lines.Add(new ValueTuple<string, string>("Location", _squad.CurrentRegion.Name));
        }
        else if (_squad.BoardedLocation != null)
        {
            lines.Add(new ValueTuple<string, string>("Ship", _squad.BoardedLocation.Name));
        }
        else
        {
            lines.Add(new ValueTuple<string, string>("Location", "Unknown"));
        }
        lines.Add(new ValueTuple<string, string>("Squad Size", _squad.Members.Count.ToString()));
        lines.Add(new ValueTuple<string, string>("Combat Ready Brothers", _squad.Members.Where(s => CanFight(s)).Count().ToString()));

        _view.PopulateSquadData(lines);
    }

    private void PopulateSquadLoadout()
    {
        List<ValueTuple<List<ValueTuple<string, int>>, string, int, int>> weaponSets = new List<ValueTuple<List<ValueTuple<string, int>>, string, int, int>>();
        WeaponSet defaultWs = _squad.SquadTemplate.DefaultWeapons;
        
        Dictionary<string, int> weaponSetCounts = new Dictionary<string, int>();
        foreach (WeaponSet ws in _squad.Loadout)
        {
            if(ws != defaultWs)
            {
                if(weaponSetCounts.ContainsKey(ws.Name))
                {
                    weaponSetCounts[ws.Name]++;
                }
                else
                {
                    weaponSetCounts[ws.Name] = 1;
                }
            }
        }
        foreach (var weaponOptions in _squad.SquadTemplate.WeaponOptions)
        {
            List <ValueTuple<string, int>> choices = new List<ValueTuple<string, int>>();
            foreach(var option in weaponOptions.Options)
            {
                int count = 0;
                if (weaponSetCounts.ContainsKey(option.Name))
                {
                    count = weaponSetCounts[option.Name];
                }
                choices.Add(new ValueTuple<string, int>(option.Name, count));
            }

            ValueTuple<List<ValueTuple<string, int>>, string, int, int> options =
                new ValueTuple<List<ValueTuple<string, int>>, string, int, int>(
                    choices,
                    weaponOptions.Name,
                    weaponOptions.MinNumber,
                    Math.Min(weaponOptions.MaxNumber, _ableBodied));
            weaponSets.Add(options);
        }
        int defaultCount = _ableBodied - weaponSetCounts.Values.Sum();
        ValueTuple<string, int> defaultOptions = new ValueTuple<string, int>(defaultWs.Name, defaultCount);
        _view.PopulateSquadLoadout(weaponSets, defaultOptions);
    }

    private void PopulateSquadMembers()
    {
        List<SquadMemberRow> memberList = new List<SquadMemberRow>();
        if (_squad?.Members != null)
        {
            // Order members, e.g., leader first, then by rank/name
            foreach (var soldier in _squad.Members.OrderByDescending(s => s.Template.IsSquadLeader)
                                                  .ThenByDescending(s => s.Template.Rank)
                                                  .ThenBy(s => s.Name))
            {
                // Show the soldier's title (e.g. Sergeant, Battle-Brother) rather than the raw rank number.
                string title = soldier.Template.Name;
                (string recovery, bool injured, bool outOfAction) = GetRecoveryStatus(soldier);
                memberList.Add(new SquadMemberRow(
                    soldier.Id, $"{title} {soldier.Name}", recovery, injured, outOfAction));
            }
        }
        _view.PopulateSquadMembers(memberList);
    }

    // Surfaces expected recovery time alongside each member, mirroring the Apothecary screen
    // (PRD 4.6 / 4.8 third pass). Uses the same primitives the Apothecary builder does so the
    // numbers agree: per-location recovery weeks, replacement eligibility, and any in-progress
    // medical procedure.
    private (string status, bool injured, bool outOfAction) GetRecoveryStatus(ISoldier soldier)
    {
        MedicalProcedure procedure = GameDataSingleton.Instance.Sector.PlayerForce.Army
            .MedicalProcedures.FirstOrDefault(p => p.SoldierId == soldier.Id);
        bool replacementNeeded = soldier.Body.HitLocations.Any(hl => hl.IsReplacementEligible);
        int recoveryWeeks = soldier.Body.HitLocations
            .Select(hl => (int)hl.Wounds.RecoveryTimeLeft())
            .DefaultIfEmpty(0).Max();
        bool wounded = soldier.Body.HitLocations.Any(hl => hl.Wounds.WoundTotal > 0 || hl.IsSevered);
        bool canFight = CanFight(soldier);

        if (procedure != null)
        {
            return ($"Augmetic surgery: {procedure.WeeksRemaining} wk", true, true);
        }
        if (replacementNeeded)
        {
            return ("Replacement required", true, true);
        }
        if (!canFight)
        {
            return (recoveryWeeks > 0 ? $"Out of action: {recoveryWeeks} wk" : "Out of action", true, true);
        }
        if (recoveryWeeks > 0)
        {
            return ($"Recovering: {recoveryWeeks} wk", true, false);
        }
        if (wounded)
        {
            return ("Lightly wounded", true, false);
        }
        return ("Ready", false, false);
    }

    private bool CanFight(ISoldier soldier)
    {
        bool canWalk = !soldier.Body.HitLocations.Where(hl => hl.Template.IsMotive)
                                                .Any(hl => hl.IsCrippled || hl.IsSevered);
        bool canFuncion = !soldier.Body.HitLocations.Where(hl => hl.Template.IsVital)
                                                    .Any(hl => hl.IsCrippled || hl.IsSevered);
        bool canFight = !soldier.Body.HitLocations.Where(hl => hl.Template.IsRangedWeaponHolder)
                                                .All(hl => hl.IsCrippled || hl.IsSevered);
        return canWalk && canFuncion && canFight;
    }

    private Dictionary<string, TacticalRegionController> GetAdjacentRegionDisplayMap()
    {
        var regionDirectionMap = new Dictionary<string, TacticalRegionController>();
        regionDirectionMap["NW"] = GetNode<TacticalRegionController>("DialogView/RegionPanel/TacticalRegionNorthwest");
        regionDirectionMap["N"] = GetNode<TacticalRegionController>("DialogView/RegionPanel/TacticalRegionNorth");
        regionDirectionMap["NE"] = GetNode<TacticalRegionController>("DialogView/RegionPanel/TacticalRegionNortheast");
        regionDirectionMap["SW"] = GetNode<TacticalRegionController>("DialogView/RegionPanel/TacticalRegionSouthwest");
        regionDirectionMap["S"] = GetNode<TacticalRegionController>("DialogView/RegionPanel/TacticalRegionSouth");
        regionDirectionMap["SE"] = GetNode<TacticalRegionController>("DialogView/RegionPanel/TacticalRegionSoutheast");
        foreach(var region in regionDirectionMap.Values)
        {
            region.Visible = false;
        }
        return regionDirectionMap;
    }

    private string GetDirectionFromCurrentToNeighbour(Region currentRegion, Region neighbourRegion)
    {
        // Hex board: row is X (increasing downward = south) and horizontal offset is (2*Y - X).
        // These six offsets match the flat-top tiling used by the planet-detail map; see
        // RegionExtensions.GetAdjacentRegions.
        int dx = neighbourRegion.Coordinates.X - currentRegion.Coordinates.X;
        int dy = neighbourRegion.Coordinates.Y - currentRegion.Coordinates.Y;

        if (dx == -2 && dy == -1) return "N";
        if (dx == -1 && dy == 0) return "NE";
        if (dx == 1 && dy == 1) return "SE";
        if (dx == 2 && dy == 1) return "S";
        if (dx == 1 && dy == 0) return "SW";
        if (dx == -1 && dy == -1) return "NW";

        return null; // Or throw an exception if direction cannot be determined (error case)
    }
}
