using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using OnlyWar.Models;

namespace OnlyWar.Helpers.Database.GameRules
{
    internal sealed class ChapterGenerationPolicyDataAccess
    {
        public IReadOnlyList<ChapterGenerationProfileData> GetProfiles(IDbConnection connection)
        {
            Dictionary<string, ChapterGenerationProfileDataBuilder> profiles =
                new(StringComparer.OrdinalIgnoreCase);
            using (var command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT ProfileKey, FactionId, RootUnitTemplateId, IsDefault "
                    + "FROM ChapterGenerationProfile ORDER BY ProfileKey";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    string profileKey = reader.GetString(0);
                    if (profiles.ContainsKey(profileKey))
                    {
                        throw new InvalidOperationException(
                            $"Rules database contains duplicate chapter generation profile '{profileKey}'.");
                    }
                    profiles.Add(profileKey, new ChapterGenerationProfileDataBuilder
                    {
                        ProfileKey = profileKey,
                        FactionId = reader.GetInt32(1),
                        RootUnitTemplateId = reader.GetInt32(2),
                        IsDefault = Convert.ToBoolean(reader[3])
                    });
                }
            }

            LoadTemplateAssignments(connection, profiles);
            LoadFormationAssignments(connection, profiles);
            LoadUnitOrders(connection, profiles);

            if (profiles.Count == 0)
            {
                throw new InvalidOperationException(
                    "Rules database must define at least one chapter generation profile.");
            }

            return profiles.Values
                .Select(builder => builder.Build())
                .ToList()
                .AsReadOnly();
        }

        private static void LoadTemplateAssignments(
            IDbConnection connection,
            IReadOnlyDictionary<string, ChapterGenerationProfileDataBuilder> profiles)
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT ProfileKey, RoleKey, TemplateKind, TemplateId, FoundingRoleKey, IsRequired "
                + "FROM ChapterGenerationTemplateAssignment ORDER BY ProfileKey, RoleKey";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                ChapterGenerationProfileDataBuilder profile = GetProfile(profiles, reader.GetString(0));
                profile.TemplateAssignments.Add(new ChapterTemplateAssignmentData
                {
                    RoleKey = reader.GetString(1),
                    TemplateKind = (ChapterTemplateKind)reader.GetInt32(2),
                    TemplateId = reader.GetInt32(3),
                    FoundingRoleKey = reader.IsDBNull(4) ? null : reader.GetString(4),
                    IsRequired = Convert.ToBoolean(reader[5])
                });
            }
        }

        private static void LoadFormationAssignments(
            IDbConnection connection,
            IReadOnlyDictionary<string, ChapterGenerationProfileDataBuilder> profiles)
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT ProfileKey, FormationKey, SquadRoleKey, MemberSoldierRoleKey, "
                + "LeaderSoldierRoleKey, MemberFoundingRoleKey, LeaderFoundingRoleKey "
                + "FROM ChapterGenerationFormationAssignment ORDER BY ProfileKey, FormationKey";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                ChapterGenerationProfileDataBuilder profile = GetProfile(profiles, reader.GetString(0));
                profile.FormationAssignments.Add(new ChapterFormationAssignmentData
                {
                    FormationKey = reader.GetString(1),
                    SquadRoleKey = reader.GetString(2),
                    MemberSoldierRoleKey = reader.GetString(3),
                    LeaderSoldierRoleKey = reader.GetString(4),
                    MemberFoundingRoleKey = reader.IsDBNull(5) ? null : reader.GetString(5),
                    LeaderFoundingRoleKey = reader.GetString(6)
                });
            }
        }

        private static void LoadUnitOrders(
            IDbConnection connection,
            IReadOnlyDictionary<string, ChapterGenerationProfileDataBuilder> profiles)
        {
            using var command = connection.CreateCommand();
            command.CommandText =
                "SELECT ProfileKey, ParentUnitTemplateId, ChildUnitTemplateId, InstanceIndex, Sequence "
                + "FROM ChapterGenerationUnitOrder ORDER BY ProfileKey, ParentUnitTemplateId, Sequence";
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                ChapterGenerationProfileDataBuilder profile = GetProfile(profiles, reader.GetString(0));
                profile.UnitOrders.Add(new ChapterUnitOrderData
                {
                    ParentUnitTemplateId = reader.GetInt32(1),
                    ChildUnitTemplateId = reader.GetInt32(2),
                    InstanceIndex = reader.GetInt32(3),
                    Sequence = reader.GetInt32(4)
                });
            }
        }

        private static ChapterGenerationProfileDataBuilder GetProfile(
            IReadOnlyDictionary<string, ChapterGenerationProfileDataBuilder> profiles,
            string profileKey)
        {
            if (!profiles.TryGetValue(profileKey, out ChapterGenerationProfileDataBuilder profile))
            {
                throw new InvalidOperationException(
                    $"Rules database chapter generation data references unknown profile '{profileKey}'.");
            }
            return profile;
        }

        private sealed class ChapterGenerationProfileDataBuilder
        {
            public string ProfileKey { get; init; }
            public int FactionId { get; init; }
            public int RootUnitTemplateId { get; init; }
            public bool IsDefault { get; init; }
            public List<ChapterTemplateAssignmentData> TemplateAssignments { get; } = new();
            public List<ChapterFormationAssignmentData> FormationAssignments { get; } = new();
            public List<ChapterUnitOrderData> UnitOrders { get; } = new();

            public ChapterGenerationProfileData Build() => new()
            {
                ProfileKey = ProfileKey,
                FactionId = FactionId,
                RootUnitTemplateId = RootUnitTemplateId,
                IsDefault = IsDefault,
                TemplateAssignments = TemplateAssignments.AsReadOnly(),
                FormationAssignments = FormationAssignments.AsReadOnly(),
                UnitOrders = UnitOrders.AsReadOnly()
            };
        }
    }
}
