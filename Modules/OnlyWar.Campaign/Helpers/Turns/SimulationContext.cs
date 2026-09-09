using OnlyWar.Campaign.Simulation;
using OnlyWar.Domain;
using OnlyWar.Domain.Orders;
using OnlyWar.Domain.Planets;
using System;
using System.Collections.Generic;

namespace OnlyWar.Campaign.Turns
{
    /// <summary>
    /// Mutable state shared by processors during one campaign or planet-scoped simulation run.
    /// </summary>
    internal sealed class SimulationContext
    {
        internal CampaignTurnContext Turn { get; }
        internal TurnResolutionResult Result { get; }
        internal TurnIntelligenceLedger IntelLedger { get; }
        internal List<Order> PlayerOrders { get; }
        internal List<Order> AllOrders { get; }
        internal Planet PlanetScope { get; }

        internal Sector Sector => Turn.Sector;
        internal GameRulesData Rules => Turn.Rules;
        internal Date Date => Turn.CurrentDate;
        internal bool IsPlanetSimulation => PlanetScope != null;

        internal SimulationContext(
            CampaignTurnContext turn,
            TurnResolutionResult result,
            TurnIntelligenceLedger intelLedger,
            IEnumerable<Order> playerOrders = null,
            Planet planetScope = null)
        {
            Turn = turn ?? throw new ArgumentNullException(nameof(turn));
            Result = result ?? throw new ArgumentNullException(nameof(result));
            IntelLedger = intelLedger ?? throw new ArgumentNullException(nameof(intelLedger));
            PlayerOrders = playerOrders == null ? new List<Order>() : new List<Order>(playerOrders);
            AllOrders = new List<Order>(PlayerOrders);
            PlanetScope = planetScope;
        }
    }
}

