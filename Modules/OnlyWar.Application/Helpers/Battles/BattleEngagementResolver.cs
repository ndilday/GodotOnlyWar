using OnlyWar.Battles.Abstractions;
using OnlyWar.Battles.Aftermath;
using OnlyWar.Battles.Placers;
using OnlyWar.Operations.Abstractions;
using OnlyWar.Domain;
using OnlyWar.Battles.Models;
using OnlyWar.Domain.Missions;
using OnlyWar.Domain.Planets;
using OnlyWar.Domain.Recruitment;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Squads;
using OnlyWar.Runtime;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Application.Battles;

/// <summary>
/// Composition adapter between Operations' engagement contract and the tactical Battles
/// implementation. The adapter owns tactical construction, placement, resolver lifetime, replay
/// translation, and the retained BattleSquad state for each operational element.
/// </summary>
public sealed class BattleEngagementResolver : IEngagementResolver, IEngagementElementFactory
{
    private readonly BattleExecutionContext _execution;
    private readonly IBattleEquipmentSource _equipment;
    private readonly Func<int, Region> _regionResolver;
    private readonly TurnProgress _progress;

    public BattleEngagementResolver(
        BattleExecutionContext execution,
        IBattleEquipmentSource equipment = null,
        Func<int, Region> regionResolver = null,
        TurnProgress progress = null)
    {
        _execution = execution ?? throw new ArgumentNullException(nameof(execution));
        _equipment = equipment;
        _regionResolver = regionResolver;
        _progress = progress;
    }

    public IRNG Random => _execution.Random;

    public OperationalMissionElement CreateSquad(
        bool isPlayerSquad,
        Squad squad,
        ChapterOperationalDoctrine doctrine = null,
        RecruitmentProgram program = null) =>
        CreateElement(BattleSquadFactory.Create(
            isPlayerSquad,
            squad,
            doctrine,
            program,
            _equipment));

    public OperationalMissionElement CreateAttachedCharacter(
        PlayerSoldier character,
        int tacticalId,
        Faction fallbackFaction,
        ChapterOperationalDoctrine doctrine = null,
        RecruitmentProgram program = null) =>
        CreateElement(BattleSquadFactory.CreateAttachedCharacter(
            character,
            tacticalId,
            fallbackFaction,
            doctrine,
            program,
            _equipment));

    public void Update(OperationalMissionElement element)
    {
        if (element == null) throw new ArgumentNullException(nameof(element));
        if (element.State is not BattleSquadEngagementState state)
        {
            throw new InvalidOperationException(
                "Only Application-created engagement elements can be updated by the Battles adapter.");
        }

        ApplyParticipantSelection(state.BattleSquad, element.FrozenParticipantIds);
    }

    public int GetPreferredOpeningRange(
        EngagementParticipant element,
        IReadOnlyList<EngagementParticipant> opposingElements,
        IReadOnlyList<EngagementParticipant> friendlyElements = null)
    {
        if (element == null) throw new ArgumentNullException(nameof(element));
        List<BattleSquad> opposing = MaterializeAll(opposingElements);
        if (opposing.Count == 0)
        {
            throw new InvalidOperationException(
                "An opening-range query requires at least one opposing element.");
        }

        return Materialize(element).GetPreferredOpeningRange(
            opposing,
            MaterializeAll(friendlyElements));
    }

    private List<BattleSquad> MaterializeAll(IReadOnlyList<EngagementParticipant> elements) =>
        (elements ?? Array.Empty<EngagementParticipant>())
            .Where(element => element != null)
            .Select(Materialize)
            .Where(squad => squad != null)
            .ToList();

    public EngagementResult Resolve(EngagementInput input)
    {
        if (input == null) throw new ArgumentNullException(nameof(input));
        input.Validate();
        Region region = ResolveRegion(input.Location);

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
            region,
            _execution,
            ToBattleProfile(input.FirstProfile),
            ToBattleProfile(input.SecondProfile));

        bool battleDone = false;
        resolver.OnBattleComplete += (_, _) => battleDone = true;
        int battleTurn = 0;
        while (!battleDone)
        {
            battleTurn++;
            _progress?.Report($"Resolving Battle in {region.Name}: Turn {battleTurn}");
            resolver.ProcessNextTurn();
        }

        BattleHistory history = resolver.BattleHistory;
        return Translate(history, firstSide, secondSide);
    }

    private BattleSquad Materialize(EngagementParticipant participant)
    {
        if (participant?.State is BattleSquadEngagementState state)
        {
            // The state handle is scoped to the mission execution context. Replacing this wrapper
            // would lose the physical mission weapon pools and the battle-local equipment state
            // that must survive a multi-day mission.
            ApplyParticipantSelection(state.BattleSquad, participant.FrozenParticipantIds);
            return state.BattleSquad;
        }

        throw new InvalidOperationException(
            "Engagement participants must carry an Application-created operational element state.");
    }

    private Region ResolveRegion(EngagementLocation location)
    {
        Region region = _regionResolver?.Invoke(location.RegionId);
        if (region == null)
        {
            throw new InvalidOperationException(
                $"No live Region was registered for engagement location {location.RegionId}.");
        }
        return region;
    }

    private static void ApplyParticipantSelection(
        BattleSquad battleSquad,
        IReadOnlyList<int> frozenParticipantIds)
    {
        if (battleSquad == null || frozenParticipantIds == null) return;
        HashSet<int> selected = frozenParticipantIds.ToHashSet();
        battleSquad.RefreshEngagementParticipants(
            battleSquad.Soldiers
                .Select(soldier => soldier.Soldier)
                .Where(soldier => selected.Contains(soldier.Id)));
    }

    private static OperationalMissionElement CreateElement(BattleSquad battleSquad)
    {
        if (battleSquad == null) return null;
        return new OperationalMissionElement(
            battleSquad.Id,
            battleSquad.Name,
            battleSquad.Faction,
            battleSquad.Soldiers.Select(soldier => soldier.Soldier),
            battleSquad.IsPlayerSquad,
            new EngagementElementTraits(
                battleSquad.Traits.ProvidesCommandAura,
                battleSquad.Traits.ProvidesSynapse,
                battleSquad.Traits.IsHeadquarters),
            battleSquad.CampaignSquad,
            battleSquad.CampaignCharacter,
            new BattleSquadEngagementState(battleSquad),
            battleSquad.EngagementParticipantIds,
            battleSquad.AbleSoldiers.Count);
    }

    internal sealed class BattleSquadEngagementState : IEngagementState
    {
        internal BattleSquadEngagementState(BattleSquad battleSquad)
        {
            BattleSquad = battleSquad ?? throw new ArgumentNullException(nameof(battleSquad));
        }

        internal BattleSquad BattleSquad { get; }
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
            // Operations reports unique enemy bodies, while BattleHistory.EnemiesKilled is the
            // player-career credit total and can include multiple credits for one body.
            history.FirstSideEnemyDeaths,
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
