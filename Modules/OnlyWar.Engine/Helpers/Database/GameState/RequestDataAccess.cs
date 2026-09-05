using OnlyWar.Builders;
using OnlyWar.Models;
using OnlyWar.Models.Planets;
using OnlyWar.Models.Supply;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;

namespace OnlyWar.Helpers.Database.GameState
{
    public class RequestDataAccess
    {
        public List<IRequest> GetRequests(
            IDbConnection connection,
            IReadOnlyDictionary<int, Character> characterMap,
            IReadOnlyDictionary<int, Faction> factionMap,
            List<Planet> planetList)
        {
            List<IRequest> requests = [];
            using IDbCommand command = connection.CreateCommand();
            command.CommandText = @"SELECT Id, CharacterId, PlanetId, ThreatFactionId,
                RequestDate, ResolutionDate, Deadline, Status, CommitmentKey,
                CommitmentDisplayName, CommitmentDisplayUnit, PackageCount, ServiceWeeks,
                DeadlineWeeks, ReferenceBattleValue, MaximumEffectivePackageCount,
                QualificationTags, ProgressBattleValueTime, OfferedRequisition,
                OfferedScheduleKind, OfferedCadenceWeeks, OfferedDeliveryDelayWeeks,
                Severity, Hazard, HasPlayerResponded FROM Request";
            using IDataReader reader = command.ExecuteReader();
            while (reader.Read())
            {
                int id = reader.GetInt32(0);
                Character requester = characterMap[reader.GetInt32(1)];
                Planet planet = planetList[reader.GetInt32(2)];
                Faction threat = reader[3] is DBNull ? null : factionMap[reader.GetInt32(3)];
                Date requestDate = Date.FromTotalWeeks(reader.GetInt32(4));
                Date resolvedDate = reader[5] is DBNull ? null : Date.FromTotalWeeks(reader.GetInt32(5));
                Date deadline = Date.FromTotalWeeks(reader.GetInt32(6));
                RequestStatus status = (RequestStatus)reader.GetInt32(7);
                string tags = reader[16] is DBNull ? null : reader.GetString(16);
                ForceCommitmentPackage commitment = new(
                    reader.GetString(8),
                    reader.GetString(9),
                    reader.GetString(10),
                    reader.GetInt32(11),
                    reader.GetInt32(12),
                    reader.GetInt32(13),
                    reader.GetInt64(14),
                    string.IsNullOrWhiteSpace(tags)
                        ? []
                        : tags.Split('|', StringSplitOptions.RemoveEmptyEntries),
                    reader.GetInt32(15));
                PresenceRequest request = new(
                    id, planet, requester, threat, requestDate, deadline, commitment,
                    reader.GetInt32(18),
                    (PledgeScheduleKind)reader.GetInt32(19),
                    reader.GetInt32(20),
                    reader.GetInt32(21),
                    (RequestSeverity)reader.GetInt32(22),
                    (RequestHazard)reader.GetInt32(23),
                    reader.GetInt64(17),
                    reader.GetBoolean(24),
                    status,
                    resolvedDate);
                requests.Add(request);
                if (status is RequestStatus.Open or RequestStatus.InProgress)
                {
                    requester.ActiveRequest = request;
                }
            }

            RequestFactory.Instance.SetCurrentHighestRequestId(
                requests.Count == 0 ? -1 : requests.Max(request => request.Id));
            return requests;
        }

        public void SaveRequest(IDbTransaction transaction, IRequest request)
        {
            using IDbCommand command = transaction.Connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = @"INSERT INTO Request
                (Id, CharacterId, PlanetId, ThreatFactionId, RequestDate, ResolutionDate,
                 Deadline, Status, CommitmentKey, CommitmentDisplayName,
                 CommitmentDisplayUnit, PackageCount, ServiceWeeks, DeadlineWeeks,
                 ReferenceBattleValue, MaximumEffectivePackageCount, QualificationTags,
                 ProgressBattleValueTime, OfferedRequisition, OfferedScheduleKind,
                 OfferedCadenceWeeks, OfferedDeliveryDelayWeeks, Severity, Hazard,
                 HasPlayerResponded) VALUES
                (@id, @characterId, @planetId, @threatFactionId, @requestDate,
                 @resolutionDate, @deadline, @status, @commitmentKey,
                 @commitmentDisplayName, @commitmentDisplayUnit, @packageCount,
                 @serviceWeeks, @deadlineWeeks, @referenceBattleValue,
                 @maximumEffectivePackageCount, @qualificationTags,
                 @progressBattleValueTime, @offeredRequisition, @offeredScheduleKind,
                 @offeredCadenceWeeks, @offeredDeliveryDelayWeeks, @severity, @hazard,
                 @hasPlayerResponded);";
            command.AddParam("@id", request.Id);
            command.AddParam("@characterId", request.Requester.Id);
            command.AddParam("@planetId", request.TargetPlanet.Id);
            command.AddParam("@threatFactionId", request.ThreatFaction?.Id);
            command.AddParam("@requestDate", request.DateRequestMade.GetTotalWeeks());
            command.AddParam("@resolutionDate", request.DateRequestResolved?.GetTotalWeeks());
            command.AddParam("@deadline", request.Deadline.GetTotalWeeks());
            command.AddParam("@status", (int)request.Status);
            command.AddParam("@commitmentKey", request.Commitment.Key);
            command.AddParam("@commitmentDisplayName", request.Commitment.DisplayName);
            command.AddParam("@commitmentDisplayUnit", request.Commitment.DisplayUnitName);
            command.AddParam("@packageCount", request.Commitment.PackageCount);
            command.AddParam("@serviceWeeks", request.Commitment.ServiceWeeks);
            command.AddParam("@deadlineWeeks", request.Commitment.CompletionDeadlineWeeks);
            command.AddParam("@referenceBattleValue", request.Commitment.ReferenceBattleValuePerPackage);
            command.AddParam("@maximumEffectivePackageCount", request.Commitment.MaximumEffectivePackageCount);
            command.AddParam("@qualificationTags", string.Join('|', request.Commitment.QualificationTags));
            command.AddParam("@progressBattleValueTime", request.ProgressBattleValueTime);
            command.AddParam("@offeredRequisition", request.OfferedRequisition);
            command.AddParam("@offeredScheduleKind", (int)request.OfferedScheduleKind);
            command.AddParam("@offeredCadenceWeeks", request.OfferedCadenceWeeks);
            command.AddParam("@offeredDeliveryDelayWeeks", request.OfferedDeliveryDelayWeeks);
            command.AddParam("@severity", (int)request.Severity);
            command.AddParam("@hazard", (int)request.Hazard);
            command.AddParam("@hasPlayerResponded", request.HasPlayerResponded);
            command.ExecuteNonQuery();
        }
    }
}
