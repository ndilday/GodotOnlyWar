using System.Runtime.CompilerServices;
using OnlyWar.Domain;
using OnlyWar.Domain.Planets;

namespace OnlyWar.Campaign.Simulation;

/// <summary>Application-owned event subscriptions for one explicit campaign and date.</summary>
public static class CampaignEventBindings
{
    private sealed class Binding
    {
        public Date Date;
        public Binding(Sector sector, Date date)
        {
            Date = date;
            sector.TargetIntelChanged += (sender, change) =>
            {
                if (sender is not PlanetFaction observer
                    || (!observer.Faction.IsPlayerFaction && !observer.Faction.IsDefaultFaction)) return;
                var belief = change.Current ?? change.Previous;
                if (belief?.Region?.Planet == null) return;
                sector.PlayerForce?.GetCampaignEventRecorder()?.RecordFactionIntel(
                    change, belief.Region.Planet.Id, change.Observation.EvidenceWeek);
            };
            sector.StanceChanged += (_, change) =>
            {
                if (!sector.RelationshipLedger.KnownFactions.TryGetValue(change.Pair.LowerFactionId, out var lower)
                    || !sector.RelationshipLedger.KnownFactions.TryGetValue(change.Pair.HigherFactionId, out var higher)) return;
                sector.PlayerForce?.GetCampaignEventRecorder()?.RecordFactionRelationship(
                    change, lower, higher, Date.GetTotalWeeks());
            };
        }
    }

    private static readonly ConditionalWeakTable<Sector, Binding> Bindings = new();
    public static void Attach(Sector sector, Date date)
    {
        if (sector == null) return;
        sector.PlayerForce?.GetCampaignEventRecorder();
        Bindings.GetValue(sector, key => new Binding(key, date)).Date = date;
    }
}
