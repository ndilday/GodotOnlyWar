using System;
using OnlyWar.Helpers.Battles;
using OnlyWar.Models.Soldiers;
using OnlyWar.Tests.Fixtures;
using Xunit;

namespace OnlyWar.Tests.Battles;

public class BattleSoldierCloneTests
{
    [Fact]
    public void CopyConstructor_CopiesBattleState()
    {
        BattleSquad squad = new(true, TestModelFactory.CreateSquad(
            "Test Squad",
            TestModelFactory.CreateSoldier(name: "Clone Source")));
        BattleSoldier source = squad.Soldiers[0];
        source.TopLeft = new Tuple<int, int>(3, 7);
        source.Orientation = 1;
        source.IsInMelee = true;
        source.ReloadingPhase = 2;
        source.Stance = Stance.Kneeling;
        source.CurrentSpeed = 4.5f;
        source.LeftoverMovement = 1.75f;
        source.TurnsRunning = 2;
        source.TurnsShooting = 3;
        source.TurnsSwinging = 4;
        source.TurnsDefending = 5;
        source.TurnsAiming = 5;
        source.WoundsTaken = 6;
        source.EnemiesTakenDown = 7;
        source.TargetId = 42;

        BattleSoldier clone = new BattleSoldier(source, squad);

        Assert.NotSame(source, clone);
        // The copy constructor is the single copy path; it shares the underlying
        // ISoldier by design (see BattleSoldier copy ctor comment).
        Assert.Same(source.Soldier, clone.Soldier);
        Assert.Equal(source.TopLeft, clone.TopLeft);
        Assert.Equal(source.Orientation, clone.Orientation);
        Assert.Equal(source.IsInMelee, clone.IsInMelee);
        Assert.Equal(source.ReloadingPhase, clone.ReloadingPhase);
        Assert.Equal(source.Stance, clone.Stance);
        Assert.Equal(source.CurrentSpeed, clone.CurrentSpeed);
        Assert.Equal(source.LeftoverMovement, clone.LeftoverMovement);
        Assert.Equal(source.TurnsRunning, clone.TurnsRunning);
        Assert.Equal(source.TurnsShooting, clone.TurnsShooting);
        Assert.Equal(source.TurnsSwinging, clone.TurnsSwinging);
        Assert.Equal(source.TurnsDefending, clone.TurnsDefending);
        Assert.Equal(source.TurnsAiming, clone.TurnsAiming);
        Assert.Equal(source.WoundsTaken, clone.WoundsTaken);
        Assert.Equal(source.EnemiesTakenDown, clone.EnemiesTakenDown);
        Assert.Equal(source.TargetId, clone.TargetId);
    }
}
