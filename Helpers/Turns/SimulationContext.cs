using OnlyWar.Helpers.Simulation;
using OnlyWar.Models;
using OnlyWar.Models.Orders;
using OnlyWar.Models.Planets;
using System;
using System.Collections.Generic;

namespace OnlyWar.Helpers.Turns
{
    /// <summary>
    /// Mutable state shared by processors during one campaign or planet-scoped simulation run.
    /// </summary>
    internal sealed class SimulationContext
    {
        internal GameSession Session { get; }
        internal TurnResolutionResult Result { get; }
        internal TurnIntelLedger IntelLedger { get; }
        internal List<Order> PlayerOrders { get; }
        internal List<Order> AllOrders { get; }
        internal Planet PlanetScope { get; }

        internal Sector Sector => Session.Sector;
        internal GameRulesData Rules => Session.Rules;
        internal Date Date => Session.CurrentDate;
        internal bool IsPlanetSimulation => PlanetScope != null;

        internal SimulationContext(
            GameSession session,
            TurnResolutionResult result,
            TurnIntelLedger intelLedger,
            IEnumerable<Order> playerOrders = null,
            Planet planetScope = null)
        {
            Session = session ?? throw new ArgumentNullException(nameof(session));
            Result = result ?? throw new ArgumentNullException(nameof(result));
            IntelLedger = intelLedger ?? throw new ArgumentNullException(nameof(intelLedger));
            PlayerOrders = playerOrders == null ? new List<Order>() : new List<Order>(playerOrders);
            AllOrders = new List<Order>(PlayerOrders);
            PlanetScope = planetScope;
        }
    }
}
