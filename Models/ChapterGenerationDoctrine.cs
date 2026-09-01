using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using OnlyWar.Helpers;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;

namespace OnlyWar.Models
{
    /// <summary>
    /// Closed vocabulary of template kinds. The vocabulary is code-owned; the row that fills
    /// each role is rules data.
    /// </summary>
    public enum ChapterTemplateKind
    {
        Soldier = 1,
        Squad = 2,
        Unit = 3
    }

    public enum ChapterSoldierRole
    {
        ChapterMaster,
        Captain,
        Champion,
        Ancient,
        MasterOfTheLibrarium,
        Codicier,
        Lexicanium,
        MasterOfTheForge,
        Techmarine,
        MasterOfTheApothecarion,
        Apothecary,
        MasterOfSanctity,
        Reclusiarch,
        Chaplain,
        Judiciar,
        Veteran,
        VeteranSergeant,
        TacticalMarine,
        AssaultMarine,
        DevastatorMarine,
        ScoutMarine,
        ScoutSergeant,
        Sergeant
    }

    public enum ChapterSquadRole
    {
        VeteranSquad,
        TacticalSquad,
        AssaultSquad,
        DevastatorSquad,
        ScoutSquad,
        ScoutCompanyHeadquarters,
        ChapterHeadquarters,
        VeteranCompanyHeadquarters,
        BattleCompanyHeadquarters,
        Librarius,
        Armory,
        Apothecarion,
        Reclusium
    }

    public enum ChapterUnitRole
    {
        Root,
        VeteranCompany,
        BattleCompany,
        TacticalCompany,
        AssaultCompany,
        DevastatorCompany,
        ScoutCompany
    }

    public enum ChapterFormationRole
    {
        Veteran,
        Tactical,
        Assault,
        Devastator,
        Scout
    }

    /// <summary>
    /// Stable keys used by the rules database. Display names are never part of this contract.
    /// </summary>
    public static class ChapterGenerationRoleKeys
    {
        public static string Soldier(ChapterSoldierRole role) =>
            "chapter.soldier." + ToSnakeCase(role.ToString());

        public static string Squad(ChapterSquadRole role) =>
            "chapter.squad." + ToSnakeCase(role.ToString());

        public static string Unit(ChapterUnitRole role) =>
            "chapter.unit." + ToSnakeCase(role.ToString());

        public static string Formation(ChapterFormationRole role) =>
            "chapter.formation." + ToSnakeCase(role.ToString());

        public static string FoundingRole(FoundingRole role) =>
            "chapter.founding." + ToSnakeCase(role.ToString());

        public static bool TryGetSoldierRole(string key, out ChapterSoldierRole role) =>
            TryGetRole(key, "chapter.soldier.", Enum.GetValues<ChapterSoldierRole>(),
                Soldier, out role);

        public static bool TryGetSquadRole(string key, out ChapterSquadRole role) =>
            TryGetRole(key, "chapter.squad.", Enum.GetValues<ChapterSquadRole>(),
                Squad, out role);

        public static bool TryGetUnitRole(string key, out ChapterUnitRole role) =>
            TryGetRole(key, "chapter.unit.", Enum.GetValues<ChapterUnitRole>(),
                Unit, out role);

        public static bool TryGetFormationRole(string key, out ChapterFormationRole role) =>
            TryGetRole(key, "chapter.formation.", Enum.GetValues<ChapterFormationRole>(),
                Formation, out role);

        public static bool TryGetFoundingRole(string key, out FoundingRole role)
        {
            foreach (FoundingRole candidate in Enum.GetValues<FoundingRole>())
            {
                if (string.Equals(key, FoundingRole(candidate), StringComparison.OrdinalIgnoreCase))
                {
                    role = candidate;
                    return true;
                }
            }

            role = default;
            return false;
        }

        private static bool TryGetRole<T>(
            string key,
            string prefix,
            IEnumerable<T> roles,
            Func<T, string> keyFactory,
            out T role)
            where T : struct, Enum
        {
            if (key != null && key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                foreach (T candidate in roles)
                {
                    if (string.Equals(key, keyFactory(candidate), StringComparison.OrdinalIgnoreCase))
                    {
                        role = candidate;
                        return true;
                    }
                }
            }

            role = default;
            return false;
        }

        private static string ToSnakeCase(string value)
        {
            StringBuilder result = new();
            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                if (char.IsUpper(character) && i > 0)
                {
                    result.Append('_');
                }
                result.Append(char.ToLowerInvariant(character));
            }
            return result.ToString();
        }
    }

    public sealed class ChapterGenerationProfileData
    {
        public string ProfileKey { get; init; }
        public int FactionId { get; init; }
        public int RootUnitTemplateId { get; init; }
        public bool IsDefault { get; init; }
        public IReadOnlyList<ChapterTemplateAssignmentData> TemplateAssignments { get; init; }
        public IReadOnlyList<ChapterFormationAssignmentData> FormationAssignments { get; init; }
        public IReadOnlyList<ChapterUnitOrderData> UnitOrders { get; init; }
    }

    public sealed class ChapterTemplateAssignmentData
    {
        public string RoleKey { get; init; }
        public ChapterTemplateKind TemplateKind { get; init; }
        public int TemplateId { get; init; }
        public string FoundingRoleKey { get; init; }
        public bool IsRequired { get; init; }
    }

    public sealed class ChapterFormationAssignmentData
    {
        public string FormationKey { get; init; }
        public string SquadRoleKey { get; init; }
        public string MemberSoldierRoleKey { get; init; }
        public string LeaderSoldierRoleKey { get; init; }
        public string MemberFoundingRoleKey { get; init; }
        public string LeaderFoundingRoleKey { get; init; }
    }

    public sealed class ChapterUnitOrderData
    {
        public int ParentUnitTemplateId { get; init; }
        public int ChildUnitTemplateId { get; init; }
        public int InstanceIndex { get; init; }
        public int Sequence { get; init; }
    }

    public sealed class ChapterFormationBinding
    {
        public ChapterFormationRole FormationRole { get; }
        public SquadTemplate SquadTemplate { get; }
        public SoldierTemplate MemberTemplate { get; }
        public SoldierTemplate LeaderTemplate { get; }
        public FoundingRole? MemberFoundingRole { get; }
        public FoundingRole LeaderFoundingRole { get; }

        internal ChapterFormationBinding(
            ChapterFormationRole formationRole,
            SquadTemplate squadTemplate,
            SoldierTemplate memberTemplate,
            SoldierTemplate leaderTemplate,
            FoundingRole? memberFoundingRole,
            FoundingRole leaderFoundingRole)
        {
            FormationRole = formationRole;
            SquadTemplate = squadTemplate;
            MemberTemplate = memberTemplate;
            LeaderTemplate = leaderTemplate;
            MemberFoundingRole = memberFoundingRole;
            LeaderFoundingRole = leaderFoundingRole;
        }
    }

    /// <summary>
    /// The validated, runtime form of a chapter-generation profile. It is intentionally a
    /// compiled doctrine rather than a general-purpose rules interpreter: the database chooses
    /// concrete templates and formation bindings, while code owns the founding algorithm.
    /// </summary>
    internal sealed class ChapterGenerationDoctrine
    {
        private readonly IReadOnlyDictionary<ChapterSoldierRole, SoldierTemplate> _soldiers;
        private readonly IReadOnlyDictionary<ChapterSquadRole, SquadTemplate> _squads;
        private readonly IReadOnlyDictionary<ChapterUnitRole, UnitTemplate> _units;
        private readonly IReadOnlyDictionary<int, ChapterFormationBinding> _formationsBySquadId;
        private readonly IReadOnlyDictionary<int, FoundingRole> _foundingRolesBySoldierId;
        private readonly IReadOnlySet<int> _companyUnitTemplateIds;
        private readonly IReadOnlyList<ChapterUnitOrderData> _unitOrders;

        public string ProfileKey { get; }
        public bool IsDefault { get; }
        public UnitTemplate RootUnit => GetUnit(ChapterUnitRole.Root);

        // Compatibility-shaped accessors keep the consumer migration small. Every accessor is
        // resolved from a data-owned semantic role; none performs a display-name lookup.
        public SoldierTemplate ChapterMaster => GetSoldier(ChapterSoldierRole.ChapterMaster);
        public SoldierTemplate Captain => GetSoldier(ChapterSoldierRole.Captain);
        public SoldierTemplate Champion => GetSoldier(ChapterSoldierRole.Champion);
        public SoldierTemplate Ancient => GetSoldier(ChapterSoldierRole.Ancient);
        public SoldierTemplate MasterOfTheLibrarium => GetSoldier(ChapterSoldierRole.MasterOfTheLibrarium);
        public SoldierTemplate Codicier => GetSoldier(ChapterSoldierRole.Codicier);
        public SoldierTemplate Lexicanium => GetSoldier(ChapterSoldierRole.Lexicanium);
        public SoldierTemplate MasterOfTheForge => GetSoldier(ChapterSoldierRole.MasterOfTheForge);
        public SoldierTemplate Techmarine => GetSoldier(ChapterSoldierRole.Techmarine);
        public SoldierTemplate MasterOfTheApothecarion => GetSoldier(ChapterSoldierRole.MasterOfTheApothecarion);
        public SoldierTemplate Apothecary => GetSoldier(ChapterSoldierRole.Apothecary);
        public SoldierTemplate MasterOfSanctity => GetSoldier(ChapterSoldierRole.MasterOfSanctity);
        public SoldierTemplate Reclusiarch => GetSoldier(ChapterSoldierRole.Reclusiarch);
        public SoldierTemplate Chaplain => GetSoldier(ChapterSoldierRole.Chaplain);
        public SoldierTemplate Judiciar => GetSoldier(ChapterSoldierRole.Judiciar);
        public SoldierTemplate Veteran => GetSoldier(ChapterSoldierRole.Veteran);
        public SoldierTemplate VeteranSergeant => GetSoldier(ChapterSoldierRole.VeteranSergeant);
        public SoldierTemplate TacticalMarine => GetSoldier(ChapterSoldierRole.TacticalMarine);
        public SoldierTemplate AssaultMarine => GetSoldier(ChapterSoldierRole.AssaultMarine);
        public SoldierTemplate DevastatorMarine => GetSoldier(ChapterSoldierRole.DevastatorMarine);
        public SoldierTemplate ScoutMarine => GetSoldier(ChapterSoldierRole.ScoutMarine);
        public SoldierTemplate ScoutSergeant => GetSoldier(ChapterSoldierRole.ScoutSergeant);
        public SoldierTemplate Sergeant => GetSoldier(ChapterSoldierRole.Sergeant);

        public SquadTemplate VeteranSquad => GetSquad(ChapterSquadRole.VeteranSquad);
        public SquadTemplate TacticalSquad => GetSquad(ChapterSquadRole.TacticalSquad);
        public SquadTemplate AssaultSquad => GetSquad(ChapterSquadRole.AssaultSquad);
        public SquadTemplate DevastatorSquad => GetSquad(ChapterSquadRole.DevastatorSquad);
        public SquadTemplate ScoutSquad => GetSquad(ChapterSquadRole.ScoutSquad);
        public SquadTemplate ScoutCompanyHeadquarters => GetSquad(ChapterSquadRole.ScoutCompanyHeadquarters);
        public SquadTemplate ChapterHeadquarters => GetSquad(ChapterSquadRole.ChapterHeadquarters);
        public SquadTemplate VeteranCompanyHeadquarters => GetSquad(ChapterSquadRole.VeteranCompanyHeadquarters);
        public SquadTemplate BattleCompanyHeadquarters => GetSquad(ChapterSquadRole.BattleCompanyHeadquarters);
        public SquadTemplate Librarius => GetSquad(ChapterSquadRole.Librarius);
        public SquadTemplate Armory => GetSquad(ChapterSquadRole.Armory);
        public SquadTemplate Apothecarion => GetSquad(ChapterSquadRole.Apothecarion);
        public SquadTemplate Reclusium => GetSquad(ChapterSquadRole.Reclusium);

        public UnitTemplate VeteranCompany => GetUnit(ChapterUnitRole.VeteranCompany);
        public UnitTemplate BattleCompany => GetUnit(ChapterUnitRole.BattleCompany);
        public UnitTemplate TacticalCompany => GetUnit(ChapterUnitRole.TacticalCompany);
        public UnitTemplate AssaultCompany => GetUnit(ChapterUnitRole.AssaultCompany);
        public UnitTemplate DevastatorCompany => GetUnit(ChapterUnitRole.DevastatorCompany);
        public UnitTemplate ScoutCompany => GetUnit(ChapterUnitRole.ScoutCompany);

        public IReadOnlyList<SquadTemplate> AdministrativeFormations { get; }

        internal ChapterGenerationDoctrine(Faction faction, ChapterGenerationProfileData profile)
        {
            if (faction == null) throw new ArgumentNullException(nameof(faction));
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (profile.FactionId != faction.Id)
            {
                throw new InvalidOperationException(
                    $"Chapter profile '{profile.ProfileKey}' belongs to faction id {profile.FactionId}, "
                    + $"not faction '{faction.Name}' ({faction.Id}).");
            }

            ProfileKey = RequireText(profile.ProfileKey, "profile key");
            IsDefault = profile.IsDefault;
            _unitOrders = (profile.UnitOrders ?? Array.Empty<ChapterUnitOrderData>()).ToList().AsReadOnly();
            _soldiers = ResolveSoldiers(faction, profile);
            _squads = ResolveSquads(faction, profile);
            _units = ResolveUnits(faction, profile);

            if (!_units.TryGetValue(ChapterUnitRole.Root, out UnitTemplate root)
                || root.Id != profile.RootUnitTemplateId)
            {
                throw new InvalidOperationException(
                    $"Chapter profile '{ProfileKey}' root unit assignment does not match "
                    + $"RootUnitTemplateId {profile.RootUnitTemplateId}.");
            }

            _formationsBySquadId = ResolveFormations(faction, profile);
            _foundingRolesBySoldierId = ResolveFoundingRoles(profile);
            _companyUnitTemplateIds = _units
                .Where(pair => pair.Key != ChapterUnitRole.Root)
                .Select(pair => pair.Value.Id)
                .ToHashSet();
            ValidateUnitGraph(root);

            AdministrativeFormations = new[]
            {
                ChapterHeadquarters,
                VeteranCompanyHeadquarters,
                BattleCompanyHeadquarters,
                ScoutCompanyHeadquarters,
                Librarius,
                Armory,
                Apothecarion,
                Reclusium
            }.Distinct().ToList().AsReadOnly();

            List<SquadTemplate> invalidAdministrativeTemplates = AdministrativeFormations
                .Where(template => !template.IsAdministrative
                    || template.MobilityPolicy != FormationMobilityPolicy.MembersOnly)
                .ToList();
            if (invalidAdministrativeTemplates.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Chapter profile '{ProfileKey}' must mark all administrative formations "
                    + "Administrative + MembersOnly: "
                    + string.Join(", ", invalidAdministrativeTemplates.Select(template => template.Id)));
            }
        }

        public SoldierTemplate GetSoldier(ChapterSoldierRole role) => _soldiers[role];
        public SquadTemplate GetSquad(ChapterSquadRole role) => _squads[role];
        public UnitTemplate GetUnit(ChapterUnitRole role) => _units[role];

        public bool IsUnitRole(UnitTemplate template, ChapterUnitRole role) =>
            template != null && GetUnit(role).Id == template.Id;

        public bool IsCompanyUnit(UnitTemplate template) =>
            template != null && _companyUnitTemplateIds.Contains(template.Id);

        public bool TryGetFormationBinding(
            SquadTemplate template,
            out ChapterFormationBinding binding)
        {
            if (template != null && _formationsBySquadId.TryGetValue(template.Id, out binding))
            {
                return true;
            }
            binding = null;
            return false;
        }

        public bool TryGetFoundingRole(
            SoldierTemplate template,
            out FoundingRole role)
        {
            if (template != null && _foundingRolesBySoldierId.TryGetValue(template.Id, out role))
            {
                return true;
            }
            role = default;
            return false;
        }

        public IReadOnlyList<UnitTemplate> GetOrderedChildUnits(UnitTemplate parent)
        {
            List<UnitTemplate> children = parent?.GetChildUnits()?.ToList() ?? new List<UnitTemplate>();
            List<ChapterUnitOrderData> orders = _unitOrders
                .Where(order => order.ParentUnitTemplateId == parent?.Id)
                .ToList();
            if (orders.Count == 0)
            {
                return children.AsReadOnly();
            }

            Dictionary<int, List<ChapterUnitOrderData>> orderGroups = orders
                .GroupBy(order => order.ChildUnitTemplateId)
                .ToDictionary(group => group.Key,
                    group => group.OrderBy(order => order.InstanceIndex).ToList());
            Dictionary<int, int> occurrences = new();
            List<(UnitTemplate Template, int Sequence, int OriginalIndex)> ordered = new();
            for (int index = 0; index < children.Count; index++)
            {
                UnitTemplate child = children[index];
                int occurrence = occurrences.TryGetValue(child.Id, out int prior) ? prior : 0;
                occurrences[child.Id] = occurrence + 1;
                if (orderGroups.TryGetValue(child.Id, out List<ChapterUnitOrderData> childOrders)
                    && occurrence < childOrders.Count)
                {
                    ordered.Add((child, childOrders[occurrence].Sequence, index));
                }
                else
                {
                    ordered.Add((child, int.MaxValue, index));
                }
            }

            return ordered
                .OrderBy(item => item.Sequence)
                .ThenBy(item => item.OriginalIndex)
                .Select(item => item.Template)
                .ToList()
                .AsReadOnly();
        }

        private static IReadOnlyDictionary<ChapterSoldierRole, SoldierTemplate> ResolveSoldiers(
            Faction faction,
            ChapterGenerationProfileData profile)
        {
            Dictionary<ChapterSoldierRole, SoldierTemplate> result = new();
            ResolveAssignments(profile, ChapterTemplateKind.Soldier,
                assignment => ChapterGenerationRoleKeys.TryGetSoldierRole(
                    assignment.RoleKey, out ChapterSoldierRole role) ? role : (ChapterSoldierRole?)null,
                faction.SoldierTemplates?.Values,
                template => template.Id,
                result,
                "soldier");
            return RequireAll(result, Enum.GetValues<ChapterSoldierRole>(), "soldier");
        }

        private static IReadOnlyDictionary<ChapterSquadRole, SquadTemplate> ResolveSquads(
            Faction faction,
            ChapterGenerationProfileData profile)
        {
            Dictionary<ChapterSquadRole, SquadTemplate> result = new();
            ResolveAssignments(profile, ChapterTemplateKind.Squad,
                assignment => ChapterGenerationRoleKeys.TryGetSquadRole(
                    assignment.RoleKey, out ChapterSquadRole role) ? role : (ChapterSquadRole?)null,
                faction.SquadTemplates?.Values,
                template => template.Id,
                result,
                "squad");
            return RequireAll(result, Enum.GetValues<ChapterSquadRole>(), "squad");
        }

        private static IReadOnlyDictionary<ChapterUnitRole, UnitTemplate> ResolveUnits(
            Faction faction,
            ChapterGenerationProfileData profile)
        {
            Dictionary<ChapterUnitRole, UnitTemplate> result = new();
            ResolveAssignments(profile, ChapterTemplateKind.Unit,
                assignment => ChapterGenerationRoleKeys.TryGetUnitRole(
                    assignment.RoleKey, out ChapterUnitRole role) ? role : (ChapterUnitRole?)null,
                faction.UnitTemplates?.Values,
                template => template.Id,
                result,
                "unit");
            return RequireAll(result, Enum.GetValues<ChapterUnitRole>(), "unit");
        }

        private static void ResolveAssignments<TEnum, TTemplate>(
            ChapterGenerationProfileData profile,
            ChapterTemplateKind kind,
            Func<ChapterTemplateAssignmentData, TEnum?> parseRole,
            IEnumerable<TTemplate> templates,
            Func<TTemplate, int> getId,
            IDictionary<TEnum, TTemplate> result,
            string kindName)
            where TEnum : struct, Enum
        {
            Dictionary<int, TTemplate> byId = (templates ?? Enumerable.Empty<TTemplate>())
                .ToDictionary(getId);
            foreach (ChapterTemplateAssignmentData assignment in (profile.TemplateAssignments
                ?? Array.Empty<ChapterTemplateAssignmentData>())
                .Where(item => item.TemplateKind == kind))
            {
                TEnum? role = parseRole(assignment);
                if (!role.HasValue)
                {
                    throw new InvalidOperationException(
                        $"Chapter profile '{profile.ProfileKey}' has an unknown {kindName} role "
                        + $"'{assignment.RoleKey}'.");
                }
                if (result.ContainsKey(role.Value))
                {
                    throw new InvalidOperationException(
                        $"Chapter profile '{profile.ProfileKey}' assigns duplicate {kindName} role "
                        + $"'{assignment.RoleKey}'.");
                }
                if (!byId.TryGetValue(assignment.TemplateId, out TTemplate template))
                {
                    throw new InvalidOperationException(
                        $"Chapter profile '{profile.ProfileKey}' {kindName} role '{assignment.RoleKey}' "
                        + $"references missing {kindName} template id {assignment.TemplateId}.");
                }
                result.Add(role.Value, template);
            }
        }

        private static IReadOnlyDictionary<TEnum, TTemplate> RequireAll<TEnum, TTemplate>(
            IDictionary<TEnum, TTemplate> result,
            IEnumerable<TEnum> roles,
            string kindName)
            where TEnum : struct, Enum
        {
            List<string> missing = roles
                .Where(role => !result.ContainsKey(role))
                .Select(role => role.ToString())
                .ToList();
            if (missing.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Chapter generation profile is missing required {kindName} roles: "
                    + string.Join(", ", missing));
            }
            return new Dictionary<TEnum, TTemplate>(result);
        }

        private static IReadOnlyDictionary<int, ChapterFormationBinding> ResolveFormations(
            Faction faction,
            ChapterGenerationProfileData profile)
        {
            Dictionary<int, ChapterFormationBinding> result = new();
            Dictionary<ChapterSoldierRole, SoldierTemplate> soldiers = ResolveSoldiers(faction, profile)
                .ToDictionary(pair => pair.Key, pair => pair.Value);
            Dictionary<ChapterSquadRole, SquadTemplate> squads = ResolveSquads(faction, profile)
                .ToDictionary(pair => pair.Key, pair => pair.Value);

            foreach (ChapterFormationAssignmentData assignment in profile.FormationAssignments
                ?? Array.Empty<ChapterFormationAssignmentData>())
            {
                if (!ChapterGenerationRoleKeys.TryGetFormationRole(
                        assignment.FormationKey, out ChapterFormationRole formationRole)
                    || !ChapterGenerationRoleKeys.TryGetSquadRole(
                        assignment.SquadRoleKey, out ChapterSquadRole squadRole)
                    || !ChapterGenerationRoleKeys.TryGetSoldierRole(
                        assignment.MemberSoldierRoleKey, out ChapterSoldierRole memberRole)
                    || !ChapterGenerationRoleKeys.TryGetSoldierRole(
                        assignment.LeaderSoldierRoleKey, out ChapterSoldierRole leaderRole)
                    || !ChapterGenerationRoleKeys.TryGetFoundingRole(
                        assignment.LeaderFoundingRoleKey, out FoundingRole leaderFoundingRole))
                {
                    throw new InvalidOperationException(
                        $"Chapter profile '{profile.ProfileKey}' has an invalid formation assignment "
                        + $"'{assignment.FormationKey}'.");
                }
                FoundingRole parsedMemberFoundingRole = default;
                FoundingRole? memberFoundingRole = null;
                if (!string.IsNullOrWhiteSpace(assignment.MemberFoundingRoleKey)
                    && !ChapterGenerationRoleKeys.TryGetFoundingRole(
                        assignment.MemberFoundingRoleKey, out parsedMemberFoundingRole))
                {
                    throw new InvalidOperationException(
                        $"Chapter profile '{profile.ProfileKey}' formation '{assignment.FormationKey}' "
                        + $"has an invalid member founding role '{assignment.MemberFoundingRoleKey}'.");
                }
                if (!string.IsNullOrWhiteSpace(assignment.MemberFoundingRoleKey))
                {
                    memberFoundingRole = parsedMemberFoundingRole;
                }
                SquadTemplate squad = squads[squadRole];
                SoldierTemplate member = soldiers[memberRole];
                SoldierTemplate leader = soldiers[leaderRole];
                if (result.ContainsKey(squad.Id))
                {
                    throw new InvalidOperationException(
                        $"Chapter profile '{profile.ProfileKey}' assigns squad template id "
                        + $"{squad.Id} to multiple formation roles.");
                }
                if (!squad.Elements.Any(element => element.SoldierTemplate == member)
                    || !squad.Elements.Any(element => element.SoldierTemplate == leader
                        && element.SoldierTemplate.IsSquadLeader))
                {
                    throw new InvalidOperationException(
                        $"Chapter profile '{profile.ProfileKey}' formation '{assignment.FormationKey}' "
                        + "does not match the assigned squad's member/leader slots.");
                }
                result.Add(squad.Id, new ChapterFormationBinding(
                    formationRole, squad, member, leader, memberFoundingRole, leaderFoundingRole));
            }

            List<string> missing = Enum.GetValues<ChapterFormationRole>()
                .Where(role => !result.Values.Any(binding => binding.FormationRole == role))
                .Select(role => role.ToString())
                .ToList();
            if (missing.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Chapter generation profile '{profile.ProfileKey}' is missing formation roles: "
                    + string.Join(", ", missing));
            }
            return result;
        }

        private static IReadOnlyDictionary<int, FoundingRole> ResolveFoundingRoles(
            ChapterGenerationProfileData profile)
        {
            Dictionary<int, FoundingRole> result = new();
            foreach (ChapterTemplateAssignmentData assignment in profile.TemplateAssignments
                ?? Array.Empty<ChapterTemplateAssignmentData>())
            {
                if (assignment.TemplateKind != ChapterTemplateKind.Soldier
                    || string.IsNullOrWhiteSpace(assignment.FoundingRoleKey))
                {
                    continue;
                }
                if (!ChapterGenerationRoleKeys.TryGetFoundingRole(
                        assignment.FoundingRoleKey, out FoundingRole foundingRole))
                {
                    throw new InvalidOperationException(
                        $"Chapter profile '{profile.ProfileKey}' has an invalid founding role "
                        + $"'{assignment.FoundingRoleKey}'.");
                }
                if (result.ContainsKey(assignment.TemplateId)
                    && result[assignment.TemplateId] != foundingRole)
                {
                    throw new InvalidOperationException(
                        $"Chapter profile '{profile.ProfileKey}' maps soldier template id "
                        + $"{assignment.TemplateId} to conflicting founding roles.");
                }
                result[assignment.TemplateId] = foundingRole;
            }
            return result;
        }

        private void ValidateUnitGraph(UnitTemplate root)
        {
            VisitUnitGraph(root, new HashSet<int>(), new HashSet<int>());
        }

        private void VisitUnitGraph(
            UnitTemplate unit,
            ISet<int> visited,
            ISet<int> active)
        {
            if (!active.Add(unit.Id))
            {
                throw new InvalidOperationException(
                    $"Chapter profile '{ProfileKey}' contains a cycle at unit template id {unit.Id}.");
            }

            if (!visited.Add(unit.Id))
            {
                active.Remove(unit.Id);
                return;
            }

            List<UnitTemplate> children = unit.GetChildUnits()?.ToList() ?? new List<UnitTemplate>();
            List<ChapterUnitOrderData> orders = _unitOrders
                .Where(order => order.ParentUnitTemplateId == unit.Id)
                .ToList();
            if (children.Count != orders.Count)
            {
                throw new InvalidOperationException(
                    $"Chapter profile '{ProfileKey}' must define one unit-order row for each "
                    + $"child of unit template id {unit.Id}; found {orders.Count} rows for "
                    + $"{children.Count} children.");
            }

            HashSet<int> childIds = children.Select(child => child.Id).ToHashSet();
            if (orders.Any(order => order.ChildUnitTemplateId < 0
                || order.InstanceIndex < 0
                || order.Sequence < 0
                || !childIds.Contains(order.ChildUnitTemplateId)))
            {
                throw new InvalidOperationException(
                    $"Chapter profile '{ProfileKey}' contains an invalid unit-order row for "
                    + $"parent unit template id {unit.Id}.");
            }
            if (orders.GroupBy(order => order.Sequence).Any(group => group.Count() > 1))
            {
                throw new InvalidOperationException(
                    $"Chapter profile '{ProfileKey}' contains duplicate unit-order sequences "
                    + $"under parent unit template id {unit.Id}.");
            }

            foreach (IGrouping<int, UnitTemplate> childGroup in children.GroupBy(child => child.Id))
            {
                List<ChapterUnitOrderData> childOrders = orders
                    .Where(order => order.ChildUnitTemplateId == childGroup.Key)
                    .OrderBy(order => order.InstanceIndex)
                    .ToList();
                if (childOrders.Count != childGroup.Count()
                    || !childOrders.Select(order => order.InstanceIndex)
                        .SequenceEqual(Enumerable.Range(0, childGroup.Count())))
                {
                    throw new InvalidOperationException(
                        $"Chapter profile '{ProfileKey}' has incomplete unit-order instances for "
                        + $"child unit template id {childGroup.Key} under parent unit template id "
                        + $"{unit.Id}.");
                }

                if (!_units.Values.Any(assigned => assigned.Id == childGroup.Key))
                {
                    throw new InvalidOperationException(
                        $"Chapter profile '{ProfileKey}' unit template id {childGroup.Key} is "
                        + "present in the root unit graph but has no chapter.unit assignment.");
                }
            }

            foreach (UnitTemplate child in children)
            {
                VisitUnitGraph(child, visited, active);
            }
            active.Remove(unit.Id);
        }

        private static string RequireText(string value, string label)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"Chapter generation {label} is required.");
            }
            return value.Trim();
        }
    }
}
