using OnlyWar.Helpers.Simulation;
using OnlyWar.Helpers.Turns;
using OnlyWar.Models;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Squads;
using OnlyWar.Tests.Fixtures;
using System;
using System.Collections;
using System.Collections.Generic;
using Xunit;

namespace OnlyWar.Tests.Turns;

public sealed class SessionSimulationContextPrimitiveTests
{
    [Fact]
    public void GameSession_ExposesConstructorDependenciesByIdentity()
    {
        GameRulesData rules = new();
        Sector sector = new();
        Date date = new(42, 123, 7);
        FixedRNG random = new();

        GameSession session = new(rules, sector, date, random);

        Assert.Same(rules, session.Rules);
        Assert.Same(sector, session.Sector);
        Assert.Same(date, session.CurrentDate);
        Assert.Same(random, session.Random);
    }

    [Fact]
    public void SimulationContext_ExposesCollaboratorsAndUsesSeparatedOrderCopies()
    {
        GameSession session = CreateSession();
        TurnResolutionResult result = new();
        TurnIntelLedger intelLedger = new();
        Planet planet = new(7, "Scope", new Coordinate(1, 2), 1, null, 0, 0);
        Order firstOrder = CreateOrder(101);
        Order secondOrder = CreateOrder(102);
        CountingEnumerable<Order> source = new([firstOrder, secondOrder]);

        SimulationContext context = new(session, result, intelLedger, source, planet);

        Assert.Same(session, context.Session);
        Assert.Same(result, context.Result);
        Assert.Same(intelLedger, context.IntelLedger);
        Assert.Same(planet, context.PlanetScope);
        Assert.Same(session.Sector, context.Sector);
        Assert.Same(session.Rules, context.Rules);
        Assert.Same(session.CurrentDate, context.Date);
        Assert.True(context.IsPlanetSimulation);
        Assert.Equal(1, source.EnumerationCount);
        Assert.NotSame(context.PlayerOrders, context.AllOrders);
        Assert.Equal(new[] { firstOrder, secondOrder }, context.PlayerOrders);
        Assert.Equal(new[] { firstOrder, secondOrder }, context.AllOrders);

        context.PlayerOrders.Clear();

        Assert.Equal(new[] { firstOrder, secondOrder }, context.AllOrders);
    }

    [Fact]
    public void SimulationContext_NullOrderSourceProducesSeparateEmptyLists()
    {
        SimulationContext context = new(
            CreateSession(),
            new TurnResolutionResult(),
            new TurnIntelLedger(),
            playerOrders: null);

        Assert.Empty(context.PlayerOrders);
        Assert.Empty(context.AllOrders);
        Assert.NotSame(context.PlayerOrders, context.AllOrders);
        Assert.Null(context.PlanetScope);
        Assert.False(context.IsPlanetSimulation);
    }

    [Fact]
    public void GameSession_RejectsNullRequiredDependencies()
    {
        GameRulesData rules = new();
        Sector sector = new();
        Date date = new(42, 123, 7);
        FixedRNG random = new();

        Assert.Equal("rules", Assert.Throws<ArgumentNullException>(
            () => new GameSession(null, sector, date, random)).ParamName);
        Assert.Equal("sector", Assert.Throws<ArgumentNullException>(
            () => new GameSession(rules, null, date, random)).ParamName);
        Assert.Equal("currentDate", Assert.Throws<ArgumentNullException>(
            () => new GameSession(rules, sector, null, random)).ParamName);
        Assert.Equal("random", Assert.Throws<ArgumentNullException>(
            () => new GameSession(rules, sector, date, null)).ParamName);
    }

    [Fact]
    public void SimulationContext_RejectsNullRequiredCollaborators()
    {
        GameSession session = CreateSession();
        TurnResolutionResult result = new();
        TurnIntelLedger intelLedger = new();

        Assert.Equal("session", Assert.Throws<ArgumentNullException>(
            () => new SimulationContext(null, result, intelLedger)).ParamName);
        Assert.Equal("result", Assert.Throws<ArgumentNullException>(
            () => new SimulationContext(session, null, intelLedger)).ParamName);
        Assert.Equal("intelLedger", Assert.Throws<ArgumentNullException>(
            () => new SimulationContext(session, result, null)).ParamName);
    }

    private static GameSession CreateSession() => new(
        new GameRulesData(),
        new Sector(),
        new Date(42, 123, 7),
        new FixedRNG());

    private static Order CreateOrder(int id) => new(
        id,
        new List<Squad>(),
        Disposition.DugIn,
        isQuiet: false,
        isActivelyEngaging: false,
        Aggression.Avoid,
        mission: null);

    private sealed class CountingEnumerable<T>(IEnumerable<T> values) : IEnumerable<T>
    {
        internal int EnumerationCount { get; private set; }

        public IEnumerator<T> GetEnumerator()
        {
            EnumerationCount++;
            return values.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
