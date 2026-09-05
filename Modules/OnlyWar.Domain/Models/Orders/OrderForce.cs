using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Models.Orders
{
    /// <summary>
    /// The participant projection shared by orders, turn routing, reports, and support effects.
    /// Squads and characters remain separate for persistence, but consumers no longer need to
    /// decide whether an order is "really" present by checking only its squads.
    /// </summary>
    public sealed class OrderForce
    {
        private readonly Order _order;

        public IReadOnlyList<Squad> Squads => _order?.AssignedSquads ?? [];
        public IReadOnlyList<PlayerSoldier> Characters => _order?.AssignedCharacters ?? [];
        public Faction OwnerFaction => _order?.OwnerFaction;
        public bool IsEmpty => ParticipantCount == 0;
        public int ParticipantCount => Squads.Count + Characters.Count;

        public IEnumerable<PlayerSoldier> AllPlayerSoldiers =>
            Squads.SelectMany(squad => squad?.Members ?? [])
                .OfType<PlayerSoldier>()
                .Where(soldier => soldier.IndividualPosting == null
                    || ReferenceEquals(soldier.CurrentOrder, _order))
                .Concat(Characters)
                .Distinct();

        public IEnumerable<ISoldier> AllSoldiers =>
            Squads.SelectMany(squad => squad?.Members ?? [])
                .Where(soldier => soldier is not PlayerSoldier player
                    || player.IndividualPosting == null
                    || ReferenceEquals(player.CurrentOrder, _order))
                .Concat(Characters)
                .Distinct();

        public OrderForce(Order order)
        {
            _order = order;
        }

        public OrderForce(
            Faction ownerFaction,
            IReadOnlyList<Squad> squads,
            IReadOnlyList<PlayerSoldier> characters)
        {
            _order = new Order(
                squads?.ToList() ?? [],
                isQuiet: true,
                isActivelyEngaging: false,
                Aggression.Normal,
                mission: null,
                ownerFaction,
                characters);
        }
    }
}
