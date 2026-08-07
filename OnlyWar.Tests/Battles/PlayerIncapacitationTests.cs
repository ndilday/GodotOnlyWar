using OnlyWar.Helpers.Battles;
using OnlyWar.Helpers.Battles.Aftermath;
using OnlyWar.Models;
using OnlyWar.Models.Battles;
using OnlyWar.Models.Missions;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Tests.Fixtures;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Xunit;

namespace OnlyWar.Tests.Battles;

/// <summary>
/// Phase 1 of Design/Active/CasualtyRealism.md: incapacitation as a named, persisted outcome.
///
/// Three things are pinned here. That the disposition of a downed battle-brother keys on
/// <c>BattleOutcome.SideHoldingField</c> and nothing else; that power-armor biostasis means no
/// amount of waiting turns a survivable wound into a death (there is no deterioration clock to
/// run, so the test is that a repeated settlement never changes the verdict); and that the battle
/// result reports incapacitated and killed as different things.
/// </summary>
public class PlayerIncapacitationTests
{
    private static int _nextId = 90_000;

    [Fact]
    public void HoldingTheField_DownedBrotherIsIncapacitatedNotKilled()
    {
        Harness harness = new();
        CrippleVitalLocation(harness.Player);

        harness.Complete(BattleSide.Attacker);

        Assert.Empty(harness.Sink.FallenBrothers);
        Assert.DoesNotContain(
            harness.Player.SoldierEvents, e => e.Type == SoldierEventType.Death);
        Assert.Single(
            harness.Player.SoldierEvents, e => e.Type == SoldierEventType.Incapacitated);
        Assert.Contains(harness.Player.Id, harness.History.IncapacitatedSoldierIds);
        Assert.DoesNotContain(harness.Player.Id, harness.History.KilledSoldierIds);
        // He kept his place in the order of battle: nothing removed him from his squad, which is
        // what would otherwise make the loader read him as a fallen brother.
        Assert.NotNull(harness.Player.AssignedSquad);
        Assert.Contains(harness.Player, harness.Player.AssignedSquad.Members);
        // And no gene-seed was cut from a living brother.
        Assert.Empty(harness.Sink.RecoveredGeneseedPurities);
    }

    [Fact]
    public void LosingTheField_DownedBrotherIsPresumedDeadAndHisGeneseedIsLost()
    {
        Harness harness = new();
        CrippleVitalLocation(harness.Player);

        harness.Complete(BattleSide.Opposing);

        Assert.Same(harness.Player, Assert.Single(harness.Sink.FallenBrothers));
        Assert.Contains(harness.Player.Id, harness.History.KilledSoldierIds);
        Assert.DoesNotContain(harness.Player.Id, harness.History.IncapacitatedSoldierIds);
        Assert.DoesNotContain(
            harness.Player.SoldierEvents, e => e.Type == SoldierEventType.Incapacitated);

        SoldierEvent death = Assert.Single(
            harness.Player.SoldierEvents, e => e.Type == SoldierEventType.Death);
        Assert.Contains("presumed dead", death.Render());
        // Nothing was recovered: the body is on ground the Chapter does not hold.
        Assert.Empty(harness.Sink.RecoveredGeneseedPurities);
        Assert.Contains(
            harness.Player.SoldierEvents,
            e => e.Type == SoldierEventType.GeneseedRecovery
                && e.Detail.Contains("lost with the body"));
    }

    [Fact]
    public void LosingTheField_UnwoundedBrotherWalksOffItUnharmed()
    {
        // The rule is about the men who could not leave under their own power. A brother who is
        // still combat-effective withdraws with his squad however the battle ended.
        Harness harness = new();

        harness.Complete(BattleSide.Opposing);

        Assert.Empty(harness.Sink.FallenBrothers);
        Assert.Empty(harness.History.KilledSoldierIds);
        Assert.Empty(harness.History.IncapacitatedSoldierIds);
    }

    [Fact]
    public void SeveredVitalLocation_IsKilledEvenWhenTheFieldIsHeld()
    {
        Harness harness = new();
        SeverVitalLocation(harness.Player);

        harness.Complete(BattleSide.Attacker);

        Assert.Same(harness.Player, Assert.Single(harness.Sink.FallenBrothers));
        Assert.Contains(harness.Player.Id, harness.History.KilledSoldierIds);
        Assert.DoesNotContain(harness.Player.Id, harness.History.IncapacitatedSoldierIds);
        SoldierEvent death = Assert.Single(
            harness.Player.SoldierEvents, e => e.Type == SoldierEventType.Death);
        Assert.Contains("Killed in battle", death.Render());
        // The body was recovered, so the Apothecary got to the progenoids.
        Assert.Single(harness.Sink.RecoveredGeneseedPurities);
    }

    [Fact]
    public void NoSideHeldTheField_WoundedAreRecoveredByTheirOwnSide()
    {
        // A mutual disengagement or a turn-cap break-off leaves both forces free to carry their
        // own off, exactly as FinishOffAbandonedWounded already treats everyone else.
        Harness harness = new();
        CrippleVitalLocation(harness.Player);

        harness.Complete(sideHoldingField: null);

        Assert.Empty(harness.Sink.FallenBrothers);
        Assert.Contains(harness.Player.Id, harness.History.IncapacitatedSoldierIds);
    }

    [Fact]
    public void Biostasis_WaitingNeverTurnsASurvivableWoundIntoADeath()
    {
        // There is no deterioration clock by design (§2.3): a brother in power armour cannot die
        // of his wounds awaiting treatment. The observable form of that promise is that settling
        // the same battle-brother again -- as many times as you like, with time having passed --
        // still finds him alive, because nothing anywhere advances a dying state.
        Harness harness = new();
        CrippleVitalLocation(harness.Player);

        for (int settlement = 0; settlement < 5; settlement++)
        {
            harness.Complete(BattleSide.Attacker);
            MedicalTurnProcessorWeek(harness.Player);
        }

        Assert.Empty(harness.Sink.FallenBrothers);
        Assert.DoesNotContain(
            harness.Player.SoldierEvents, e => e.Type == SoldierEventType.Death);
        Assert.NotNull(harness.Player.AssignedSquad);
    }

    [Fact]
    public void DebriefReport_CountsIncapacitatedApartFromDead()
    {
        Harness harness = new();
        CrippleVitalLocation(harness.Player);
        harness.Complete(BattleSide.Attacker);

        // The debrief reads the same battle history the policy settled.
        Assert.Contains(harness.Player.Id, harness.History.IncapacitatedSoldierIds);

        BattleDebriefReport report = harness.BuildDebriefReport();

        Assert.Equal(0, report.PlayerDeaths);
        Assert.Equal(1, report.PlayerIncapacitated);
        BattleCasualtyEntry entry = Assert.Single(report.PlayerCasualties);
        Assert.Equal(BattleCasualtyDisposition.Incapacitated, entry.Disposition);
        Assert.Contains("incapacitated", BattleDebriefReportBuilder.BuildSummaryLine(report));
    }

    [Fact]
    public void DebriefReport_SaysNothingAboutIncapacitationWhenThereIsNone()
    {
        Harness harness = new();
        harness.Complete(BattleSide.Attacker);

        BattleDebriefReport report = harness.BuildDebriefReport();

        Assert.Equal(0, report.PlayerIncapacitated);
        Assert.Equal(
            "Friendly dead: 0    Opposing dead: 0",
            BattleDebriefReportBuilder.BuildSummaryLine(report));
    }

    [Theory]
    // A vital location crippled short of severed, with the field held: alive, out of the fight.
    [InlineData(true, true)]
    // The same wounds, on ground the Chapter could not hold: never found, presumed dead.
    [InlineData(false, false)]
    public void Classifier_KeysIncapacitationOnWhetherTheBodyWasRecovered(
        bool bodyRecovered, bool expectIncapacitated)
    {
        PlayerSoldier soldier = CreatePlayerSoldier("Brother Classified");
        CrippleVitalLocation(soldier);

        CasualtyState state = CasualtyStateEvaluator.Classify(soldier, bodyRecovered);

        Assert.Equal(
            expectIncapacitated ? CasualtyState.Incapacitated : CasualtyState.Killed, state);
    }

    [Fact]
    public void Classifier_WoundedButStillEffectiveIsMerelyImpaired()
    {
        PlayerSoldier soldier = CreatePlayerSoldier("Brother Scratched");
        soldier.Body.HitLocations
            .First(location => location.Template.IsMotive)
            .Wounds.AddWound(WoundLevel.Moderate);

        Assert.True(soldier.IsCombatEffective);
        // Impaired either way: losing the field only matters to men who cannot leave it.
        Assert.Equal(CasualtyState.Impaired, CasualtyStateEvaluator.Classify(soldier, true));
        Assert.Equal(CasualtyState.Impaired, CasualtyStateEvaluator.Classify(soldier, false));
    }

    [Fact]
    public void Classifier_LostWeaponHandsIncapacitateJustAsLostLegsDo()
    {
        // CanFight and CanMove are separate seams, and both feed one verdict: out of the fight.
        PlayerSoldier soldier = CreatePlayerSoldier("Brother Handless");
        foreach (HitLocation hand in soldier.Body.HitLocations
            .Where(location => location.Template.HandGroupId.HasValue
                && location.Template.Name.Contains("Hand")))
        {
            hand.Wounds.AddWound(WoundLevel.Critical);
        }

        Assert.False(soldier.CanFight);
        Assert.True(soldier.CanMove);
        Assert.Equal(CasualtyState.Incapacitated, CasualtyStateEvaluator.Classify(soldier, true));
    }

    // A week of natural healing, the only time-passing thing that touches a downed brother.
    private static void MedicalTurnProcessorWeek(PlayerSoldier soldier) =>
        OnlyWar.Helpers.MedicalTurnProcessor.ApplyWeeklyHealing(soldier.Body);

    private static void CrippleVitalLocation(ISoldier soldier)
    {
        HitLocation vital = soldier.Body.HitLocations
            .First(location => location.Template.IsVital && !location.Template.HoldsProgenoid);
        vital.Wounds.AddWound(WoundLevel.Critical);
        Assert.True(vital.IsCrippled);
        Assert.False(vital.IsSevered);
    }

    private static void SeverVitalLocation(ISoldier soldier) =>
        soldier.Body.HitLocations
            .First(location => location.Template.IsVital && !location.Template.HoldsProgenoid)
            .Wounds.AddWound(WoundLevel.Massive);

    /// <summary>
    /// One battle-brother's squad against an enemy squad, with the aftermath policy wired to a
    /// recording sink. <see cref="Complete"/> stamps the outcome and settles the battle, which is
    /// the whole surface Phase 1 changes.
    /// </summary>
    private sealed class Harness
    {
        internal PlayerSoldier Player { get; }
        internal BattleHistory History { get; } = new();
        internal RecordingSink Sink { get; } = new();
        private readonly BattleSquad _playerSquad;
        private readonly BattleSquad _enemySquad;
        private readonly BattleAftermathContext _context;
        private readonly IBattleAftermathPolicy _policy;

        internal Harness()
        {
            Faction playerFaction = CreateFaction(_nextId++, "Chapter", isPlayer: true);
            Faction enemyFaction = CreateFaction(_nextId++, "Orks", isPlayer: false);
            Player = CreatePlayerSoldier("Brother Downed");
            _playerSquad = CreateBattleSquad(playerFaction, "Strike Squad", Player);
            _enemySquad = CreateBattleSquad(enemyFaction, "Warband", CreateSoldier("Boy"));
            _context = new BattleAftermathContext(
                [_playerSquad],
                [_enemySquad],
                CreateRegion("Ash Wastes", "Calth"),
                History,
                // Well after the implant date, so a recovered brother's progenoids are mature and
                // gene-seed recovery is a real outcome rather than always reading "immature".
                new BattleAftermathDependencies(new Date(1, 10, 1), new FixedRNG(), Sink));
            _policy = BattleAftermathPolicyFactory.Create(_context);
            // Turn 0 is snapshotted before a shot is fired, exactly as BattleTurnResolver's
            // constructor does. It has to be: a BattleState built after the wounds land drops
            // soldiers who are no longer combat-effective, so a late snapshot would lose the very
            // men the debrief is about.
            History.Turns.Add(new BattleTurn(BuildState(), []));
        }

        internal void Complete(BattleSide? sideHoldingField)
        {
            History.Outcome = new BattleOutcome(BattleEndReason.Withdrawal, sideHoldingField);
            _policy.OnBattleCompleted(BuildState());
        }

        internal BattleDebriefReport BuildDebriefReport() =>
            BattleDebriefReportBuilder.Build(History);

        private BattleState BuildState() => new(
            new Dictionary<int, BattleSquad> { [_playerSquad.Id] = _playerSquad },
            new Dictionary<int, BattleSquad> { [_enemySquad.Id] = _enemySquad });
    }

    private static Region CreateRegion(string regionName, string planetName)
    {
        Planet planet = new(1, planetName, new Coordinate(0, 0), 1, null, 1, 0);
        return new Region(1, planet, 0, regionName, new RegionCoordinate(0, 0), 0);
    }

    private static PlayerSoldier CreatePlayerSoldier(string name) =>
        new(CreateSoldier(name), name) { ProgenoidImplantDate = new Date(1, 1, 1) };

    private static Soldier CreateSoldier(string name)
    {
        Soldier soldier = TestModelFactory.CreateSoldier(TestModelFactory.MarineTemplate, name);
        soldier.Id = _nextId++;
        return soldier;
    }

    private static BattleSquad CreateBattleSquad(
        Faction faction, string name, params ISoldier[] soldiers)
    {
        SquadTemplate template = new(
            _nextId++,
            $"{faction.Name} Test Squad",
            TestModelFactory.DefaultWeapons,
            [],
            TestModelFactory.TestArmor,
            [new SquadTemplateElement(TestModelFactory.MarineTemplate, 0, 4)],
            SquadTypes.None)
        {
            Faction = faction
        };
        Squad squad = new(name, null, template);
        foreach (ISoldier soldier in soldiers)
        {
            squad.AddSquadMember(soldier);
        }

        BattleSquad battleSquad = new(faction.IsPlayerFaction, squad);
        foreach (BattleSoldier soldier in battleSquad.Soldiers)
        {
            soldier.TopLeft = (_nextId++, 2);
            soldier.Orientation = 0;
        }
        return battleSquad;
    }

    private static Faction CreateFaction(int id, string name, bool isPlayer) =>
        new(
            id,
            name,
            Color.Red,
            isPlayer,
            isDefaultFaction: false,
            canInfiltrate: false,
            GrowthType.None,
            new Dictionary<int, Species> { [TestModelFactory.HumanSpecies.Id] = TestModelFactory.HumanSpecies },
            new Dictionary<int, SoldierTemplate> { [TestModelFactory.MarineTemplate.Id] = TestModelFactory.MarineTemplate },
            new Dictionary<int, SquadTemplate>(),
            new Dictionary<int, Models.Units.UnitTemplate>(),
            new Dictionary<int, Models.Fleets.BoatTemplate>(),
            new Dictionary<int, Models.Fleets.ShipTemplate>(),
            new Dictionary<int, Models.Fleets.FleetTemplate>());

    private sealed class RecordingSink : IPlayerBattleAftermathSink
    {
        public List<PlayerSoldier> FallenBrothers { get; } = [];
        public List<float> RecoveredGeneseedPurities { get; } = [];

        public void MoveToFallenBrothers(PlayerSoldier soldier) => FallenBrothers.Add(soldier);

        public void AddRecoveredGeneseed(float purity) => RecoveredGeneseedPurities.Add(purity);

        public void AddToBattleHistory(Date date, string title, IReadOnlyList<string> subEvents) { }
    }
}
