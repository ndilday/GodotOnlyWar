using OnlyWar.Contracts.Battles;
using OnlyWar.Helpers.Battles.Aftermath;
using OnlyWar.Helpers.Battles.Placers;
using OnlyWar.Models;
using OnlyWar.Models.Battles;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Helpers.Battles;

/// <summary>
/// Transitional composition adapter between Operations' engagement contract and the tactical
/// Battles implementation. The adapter owns tactical construction, placement, resolver lifetime,
/// and replay translation; mission policy receives only <see cref="EngagementResult"/>.
/// </summary>
    public sealed class BattleEngagementResolver : IEngagementResolver
    {
    private readonly BattleExecutionContext _execution;
    private readonly IBattleEquipmentSource _equipment;

        public BattleEngagementResolver(
        BattleExecutionContext execution,
        IBattleEquipmentSource equipment = null)
    {
        _execution = execution ?? throw new ArgumentNullException(nameof(execution));
        _equipment = equipment;
    }

    public IRNG Random => _execution.Random;

    public EngagementResult Resolve(EngagementInput input)
    {
        if (input == null) throw new ArgumentNullException(nameof(input));
        input.Validate();

        List<BattleSquad> firstSide = input.EffectiveFirstSide
            .Select(Materialize)
            .Where(squad => squad != null)
            .ToList();
        List<BattleSquad> secondSide = input.EffectiveSecondSide
            .Select(Materialize)
            .Where(squad => squad != null)
            .ToList();

        if (firstSide.Count == 0 || secondSide.Count == 0)
        {
            throw new InvalidOperationException(
                "An engagement requires at least one element on each side.");
        }

        if (input.ReallocateFirstSideEquipment)
        {
            foreach (BattleSquad squad in firstSide) squad.ReallocateEquipment();
        }
        if (input.ReallocateSecondSideEquipment)
        {
            foreach (BattleSquad squad in secondSide) squad.ReallocateEquipment();
        }

        BattleGridManager grid = new();
        Place(input, grid, firstSide, secondSide);
        BattleTurnResolver resolver = new(
            grid,
            firstSide,
            secondSide,
            input.Region,
            _execution,
            ToBattleProfile(input.FirstProfile),
            ToBattleProfile(input.SecondProfile));

        bool battleDone = false;
        resolver.OnBattleComplete += (_, _) => battleDone = true;
        while (!battleDone)
        {
            resolver.ProcessNextTurn();
        }

        BattleHistory history = resolver.BattleHistory;
        return Translate(history, firstSide, secondSide);
    }

    private BattleSquad Materialize(EngagementParticipant participant)
    {
        if (participant.State is BattleSquad existing)
        {
            // The state handle is scoped to the mission execution context. Replacing this wrapper
            // would lose the physical mission weapon pools and the battle-local equipment state
            // that must survive a multi-day mission.
            if (participant.FrozenParticipantIds != null)
            {
                HashSet<int> selected = participant.FrozenParticipantIds.ToHashSet();
                existing.RefreshEngagementParticipants(
                    existing.Soldiers
                        .Select(soldier => soldier.Soldier)
                        .Where(soldier => selected.Contains(soldier.Id)));
            }
            return existing;
        }

        EngagementElementTraits traits = participant.Traits ?? new EngagementElementTraits();
        BattleElementSpec spec = new(
            participant.TacticalId,
            participant.Name,
            participant.Faction,
            participant.EffectiveMembers,
            new BattleElementTraits(
                traits.ProvidesCommandAura,
                traits.ProvidesSynapse,
                traits.IsHeadquarters),
            participant.CampaignSquad,
            participant.CampaignCharacter);
        BattleSquad result = new(spec, _equipment);
        if (participant.FrozenParticipantIds != null)
        {
            HashSet<int> selected = participant.FrozenParticipantIds.ToHashSet();
            result.RefreshEngagementParticipants(
                participant.EffectiveMembers.Where(soldier => selected.Contains(soldier.Id)));
        }
        return result;
    }

    private static void Place(
        EngagementInput input,
        BattleGridManager grid,
        IReadOnlyList<BattleSquad> firstSide,
        IReadOnlyList<BattleSquad> secondSide)
    {
        switch (input.Placement)
        {
            case EngagementPlacement.Meeting:
                new AnnihilationPlacer(grid, input.OpeningRange)
                    .PlaceSquads(firstSide, secondSide);
                break;
            case EngagementPlacement.FirstSideAmbushed:
                new AmbushPlacer(grid, input.OpeningRange)
                    .PlaceSquads(firstSide, secondSide);
                break;
            case EngagementPlacement.SecondSideAmbushed:
                new AmbushPlacer(grid, input.OpeningRange)
                    .PlaceSquads(secondSide, firstSide);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(input.Placement));
        }

        IEnumerable<BattleSquad> all = firstSide.Concat(secondSide);
        switch (input.BurrowSide)
        {
            case EngagementBurrowSide.None:
                return;
            case EngagementBurrowSide.First:
                BurrowPlacer.PlaceBurrowers(grid, all, firstSide);
                break;
            case EngagementBurrowSide.Second:
                BurrowPlacer.PlaceBurrowers(grid, all, secondSide);
                break;
            case EngagementBurrowSide.Both:
                BurrowPlacer.PlaceBurrowers(grid, all);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(input.BurrowSide));
        }
    }

    private static EngagementResult Translate(
        BattleHistory history,
        IReadOnlyList<BattleSquad> firstSide,
        IReadOnlyList<BattleSquad> secondSide)
    {
        BattleOutcome outcome = history.Outcome
            ?? throw new InvalidOperationException("A completed engagement has no tactical outcome.");
        HashSet<int> firstIds = firstSide
            .SelectMany(squad => squad.Soldiers)
            .Select(soldier => soldier.Soldier.Id)
            .ToHashSet();
        HashSet<int> secondIds = secondSide
            .SelectMany(squad => squad.Soldiers)
            .Select(soldier => soldier.Soldier.Id)
            .ToHashSet();

        EngagementOutcome detachedOutcome = new(
            ToContractEndReason(outcome.EndReason),
            outcome.SideHoldingField switch
            {
                BattleSide.Attacker => EngagementSide.First,
                BattleSide.Opposing => EngagementSide.Second,
                _ => null
            },
            outcome.DisengagedSquadIds,
            outcome.EliminatedSquadIds,
            outcome.RoutingSquadIds,
            outcome.RearGuardSquadIds);

        return new EngagementResult(
            detachedOutcome,
            BattleDebriefReportBuilder.Build(history),
            BattleDebriefReportBuilder.BuildSummaryLine(
                BattleDebriefReportBuilder.Build(history)),
            history,
            history.EnemiesKilled,
            history.FirstSideEnemiesKilled,
            history.KilledSoldierIds.Count(secondIds.Contains),
            history.KilledSoldierIds.Count(firstIds.Contains),
            history.KilledSoldierIds.ToList(),
            history.IncapacitatedSoldierIds.ToList(),
            history.DamagedSoldierIds.ToList(),
            history.ClosingSummary);
    }

    private static BattleSideProfile ToBattleProfile(EngagementSideProfile profile) =>
        new(profile.Aggression, profile.Role switch
        {
            EngagementRole.Attacker => BattleRole.Attacker,
            EngagementRole.Defender => BattleRole.Defender,
            EngagementRole.Ambusher => BattleRole.Ambusher,
            EngagementRole.Ambushed => BattleRole.Ambushed,
            EngagementRole.AssassinationAttacker => BattleRole.AssassinationAttacker,
            _ => throw new ArgumentOutOfRangeException(nameof(profile.Role))
        });

    private static EngagementEndReason ToContractEndReason(BattleEndReason reason) => reason switch
    {
        BattleEndReason.Annihilation => EngagementEndReason.Annihilation,
        BattleEndReason.Withdrawal => EngagementEndReason.Withdrawal,
        BattleEndReason.Rout => EngagementEndReason.Rout,
        BattleEndReason.MutualDisengagement => EngagementEndReason.MutualDisengagement,
        BattleEndReason.TurnCap => EngagementEndReason.TurnCap,
        _ => throw new ArgumentOutOfRangeException(nameof(reason))
    };
}

/// <summary>Builds a contract request from the transitional mission-side battle handles.</summary>
public static class BattleEngagementInputBuilder
{
    public static EngagementParticipant From(BattleSquad squad)
    {
        if (squad == null) throw new ArgumentNullException(nameof(squad));
        return new EngagementParticipant(
            squad.Id,
            squad.Name,
            squad.Faction,
            squad.Soldiers.Select(soldier => soldier.Soldier).ToArray(),
            squad.EngagementParticipantIds?.ToArray(),
            squad.IsPlayerSquad,
            new EngagementElementTraits(
                squad.Traits.ProvidesCommandAura,
                squad.Traits.ProvidesSynapse,
                squad.Traits.IsHeadquarters),
            squad.CampaignSquad,
            squad.CampaignCharacter,
            squad);
    }

    public static IReadOnlyList<EngagementParticipant> From(IEnumerable<BattleSquad> squads) =>
        (squads ?? Enumerable.Empty<BattleSquad>()).Select(From).ToArray();

    public static EngagementInput Create(
        IEnumerable<BattleSquad> firstSide,
        IEnumerable<BattleSquad> secondSide,
        Region region,
        ushort openingRange,
        EngagementSideProfile firstProfile,
        EngagementSideProfile secondProfile,
        EngagementPlacement placement = EngagementPlacement.Meeting,
        EngagementBurrowSide burrowSide = EngagementBurrowSide.None,
        bool reallocateFirstSideEquipment = true,
        bool reallocateSecondSideEquipment = false) =>
        new(
            From(firstSide),
            From(secondSide),
            region,
            openingRange,
            firstProfile,
            secondProfile,
            placement,
            burrowSide,
            reallocateFirstSideEquipment,
            reallocateSecondSideEquipment);
}
