using OnlyWar.Domain;
using OnlyWar.Domain.Mandates;
using OnlyWar.Domain.Soldiers;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Campaign.Mandates
{
    /// <summary>
    /// Writes the legacy entries for every source other than mandates (PRD §4.25, TDD §6.15):
    /// worlds saved and lost where the Chapter took part, governor
    /// requests, large battles and the Chapter's own dead. It reads the week's campaign events and
    /// the requests resolved, and each entry references its source, so a second pass over the same
    /// week writes nothing. Sources stack: one outcome can write entries under several sources.
    /// </summary>
    public static class LegacyService
    {
        public static int RecordWeek(PlayerForce force, IEnumerable<CampaignEvent> weekEvents, int currentWeek)
        {
            if (force == null) return 0;
            ChapterMandates mandates = force.Mandates;
            int written = 0;
            foreach (CampaignEvent @event in (weekEvents ?? []).OrderBy(item => item.Id))
            {
                (int amount, LegacySourceKind kind, string description)? entry = @event.Payload switch
                {
                    WorldControlChangedPayload { ChapterParticipated: true } world =>
                        world.EventType == CampaignEventType.WorldSaved
                            ? (MandateConstants.WorldSavedLegacy, LegacySourceKind.WorldSaved,
                                $"World saved: {world.PlanetName}.")
                            : (MandateConstants.WorldLostLegacy, LegacySourceKind.WorldLost,
                                $"World lost: {world.PlanetName}."),
                    DeathPayload death => DeathEntry(@event, death),
                    BattleResolvedPayload battle => BattleEntry(battle),
                    _ => null
                };
                if (entry is not { } value || value.amount == 0) continue;
                string key = $"event/{@event.Id}";
                if (mandates.HasLegacyEntry(key)) continue;
                mandates.AddLegacy(currentWeek, value.amount, value.kind, key, @event.Id, value.description);
                written++;
            }

            foreach (IRequest request in force.Requests
                .Where(request => request.Status is RequestStatus.Fulfilled or RequestStatus.Failed)
                .OrderBy(request => request.Id))
            {
                string key = $"request/{request.Id}";
                if (mandates.HasLegacyEntry(key)) continue;
                int value = MandateConstants.RequestLegacy(request.Severity);
                bool fulfilled = request.Status == RequestStatus.Fulfilled;
                string world = request.TargetPlanet?.Name ?? "an unknown world";
                mandates.AddLegacy(
                    request.DateRequestResolved?.GetTotalWeeks() ?? currentWeek,
                    fulfilled ? value : -value,
                    fulfilled ? LegacySourceKind.RequestFulfilled : LegacySourceKind.RequestFailed,
                    key,
                    null,
                    fulfilled
                        ? $"{request.Severity} petition from {world} answered."
                        : $"{request.Severity} petition from {world} failed.");
                written++;
            }
            return written;
        }

        private static (int, LegacySourceKind, string)? DeathEntry(CampaignEvent @event, DeathPayload death)
        {
            bool senior = NarrativeEventRules.Initial.IsNotableCasualty(death.SoldierRank, death.SoldierSubrank);
            string name = @event.Entities
                .FirstOrDefault(entity => entity.Kind == CampaignEntityKind.Soldier
                    && entity.Role == CampaignEventEntityRole.Subject)
                ?.DisplayNameSnapshot ?? "A battle-brother";
            string rank = string.IsNullOrWhiteSpace(death.SoldierTemplateName)
                ? string.Empty
                : $"{death.SoldierTemplateName} ";
            return (senior ? MandateConstants.SeniorBrotherKilledLegacy : MandateConstants.BrotherKilledLegacy,
                LegacySourceKind.BrotherKilled,
                $"{rank}{name} fell.");
        }

        private static (int, LegacySourceKind, string)? BattleEntry(BattleResolvedPayload battle)
        {
            if (battle.StartingStrategicValue is not long size || battle.ChapterHeldField is not bool held)
            {
                return null;
            }
            int amount = MandateConstants.LargeBattleLegacy(size);
            if (amount == 0) return null;
            return held
                ? (amount, LegacySourceKind.LargeBattleWon, $"Large battle won: {battle.Title}.")
                : (-amount, LegacySourceKind.LargeBattleLost, $"Large battle lost: {battle.Title}.");
        }
    }

    /// <summary>What changed in the office of Chapter Master this pass.</summary>
    public sealed record ChapterMasterOfficeChange(int? EndedSoldierId, int? BegunSoldierId);

    /// <summary>
    /// Chapter Master tenure and succession (TDD §6.15 *Succession*). The office
    /// is held by the living soldier with the Chapter Master template.
    /// </summary>
    public static class ChapterMasterOffice
    {
        public static PlayerSoldier FindHolder(PlayerForce force, GameRulesData rules)
        {
            int? templateId = rules?.ChapterDoctrine?.ChapterMaster?.Id;
            if (templateId == null) return null;
            return force?.Army?.PlayerSoldierMap.Values
                .Where(soldier => soldier.Template?.Id == templateId)
                .OrderBy(soldier => soldier.Id)
                .FirstOrDefault();
        }

        public static bool IsVacant(PlayerForce force, GameRulesData rules) =>
            FindHolder(force, rules) == null;

        /// <summary>Brings the tenure record into line with who holds the office now.</summary>
        public static ChapterMasterOfficeChange Synchronize(PlayerForce force, GameRulesData rules, int week)
        {
            if (force?.Mandates == null) return new ChapterMasterOfficeChange(null, null);
            PlayerSoldier holder = FindHolder(force, rules);
            ChapterMasterTenure current = force.Mandates.CurrentTenure;
            int? ended = null;
            int? begun = null;
            if (current != null && current.SoldierId != holder?.Id)
            {
                ended = current.SoldierId;
                force.Mandates.EndTenure(week);
            }
            if (holder != null && force.Mandates.CurrentTenure == null)
            {
                force.Mandates.BeginTenure(holder.Id, holder.Name, week);
                begun = holder.Id;
            }
            return new ChapterMasterOfficeChange(ended, begun);
        }

        /// <summary>
        /// Only a living Captain can take an empty office. He need not be duty-ready.
        /// Returns null when he can, else the reason he cannot.
        /// </summary>
        public static string WhyCannotPromote(PlayerForce force, GameRulesData rules, PlayerSoldier soldier)
        {
            if (soldier == null || force?.Army?.PlayerSoldierMap.ContainsKey(soldier.Id) != true)
                return "That brother is no longer on the rolls.";
            if (!IsVacant(force, rules))
                return $"The office is held by {FindHolder(force, rules).Name}.";
            if (soldier.Template?.Id != rules?.ChapterDoctrine?.Captain?.Id)
                return $"Only a Captain can become Chapter Master; {soldier.Name} is a {soldier.Template?.Name}.";
            return null;
        }

        public static IReadOnlyList<PlayerSoldier> Candidates(PlayerForce force, GameRulesData rules) =>
            IsVacant(force, rules)
                ? force.Army.PlayerSoldierMap.Values
                    .Where(soldier => soldier.Template?.Id == rules?.ChapterDoctrine?.Captain?.Id)
                    .OrderBy(soldier => soldier.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(soldier => soldier.Id)
                    .ToList()
                : [];

        /// <summary>
        /// Raises a Captain to Chapter Master. Only his rank changes: he stays in his squad
        /// until the player moves him, and his company's Captain post is left vacant.
        /// </summary>
        public static void Promote(PlayerForce force, GameRulesData rules, PlayerSoldier soldier, Date date)
        {
            ArgumentNullException.ThrowIfNull(date);
            string reason = WhyCannotPromote(force, rules, soldier);
            if (reason != null) throw new InvalidOperationException(reason);
            int week = date.GetTotalWeeks();
            string previous = force.Mandates.Tenures.LastOrDefault()?.SoldierName;
            string former = soldier.Template?.Name;
            soldier.Template = rules.ChapterDoctrine.ChapterMaster;
            Synchronize(force, rules, week);
            force.GetCampaignEventRecorder().Record(new CampaignEventCandidate(
                CampaignEventType.ChapterMasterAppointed,
                week,
                week,
                null,
                $"chapter-master/appointed/{soldier.Id}/{week}",
                1,
                new ChapterMasterAppointedPayload(soldier.Id, soldier.Name, former, previous),
                [
                    new CampaignEventEntityRef(CampaignEntityKind.Soldier, soldier.Id,
                        CampaignEventEntityRole.Subject, soldier.Name),
                    new CampaignEventEntityRef(CampaignEntityKind.Chapter, 0,
                        CampaignEventEntityRole.Related,
                        force.Army?.OrderOfBattle?.Name ?? force.Faction?.Name ?? "Chapter")
                ]));
        }
    }
}
