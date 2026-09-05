using OnlyWar.Models.Equippables;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using System;
using System.Collections.Generic;

namespace OnlyWar.Helpers
{
    /// <summary>
    /// Resolves itemized personal equipment without consulting the pooled squad doctrine. A
    /// personal override remains stored while its soldier occupies a pooled element, but it is
    /// inactive until a personal-equipment role makes it eligible again.
    /// </summary>
    public static class EquipmentLoadoutService
    {
        public static bool IsPersonallyEquipped(SquadTemplateElement element) =>
            element?.PersonalEquipmentRole != null;

        public static ResolvedEquipmentLoadout Resolve(
            int soldierId,
            SquadTemplateElement element,
            EquipmentLoadoutDoctrine doctrine,
            EquipmentKitTemplate authoredRoleKit,
            EquipmentKitTemplate elementFallbackKit,
            EquipmentKitTemplate squadFallbackKit,
            EquipmentValidationContext validationContext = null)
        {
            if (!IsPersonallyEquipped(element))
            {
                return new ResolvedEquipmentLoadout(
                    null,
                    EquipmentLoadoutSource.Pooled,
                    null,
                    false,
                    Array.Empty<EquipmentValidationIssue>());
            }

            EquipmentLoadout loadout;
            EquipmentLoadoutSource source;
            bool activePersonal = false;
            if (doctrine?.TryGetPersonalLoadout(soldierId, out EquipmentLoadout personal) == true)
            {
                loadout = personal;
                source = EquipmentLoadoutSource.Personal;
                activePersonal = true;
            }
            else if (doctrine?.TryGetRoleDefault(element.PersonalEquipmentRole.Id, out EquipmentLoadout roleDefault) == true)
            {
                loadout = roleDefault;
                source = EquipmentLoadoutSource.ChapterRole;
            }
            else if (authoredRoleKit != null)
            {
                loadout = authoredRoleKit.ToLoadout();
                source = EquipmentLoadoutSource.AuthoredRole;
            }
            else if (elementFallbackKit != null)
            {
                loadout = elementFallbackKit.ToLoadout();
                source = EquipmentLoadoutSource.ElementFallback;
            }
            else if (squadFallbackKit != null)
            {
                loadout = squadFallbackKit.ToLoadout();
                source = EquipmentLoadoutSource.SquadFallback;
            }
            else
            {
                loadout = null;
                source = EquipmentLoadoutSource.ElementFallback;
            }

            EquipmentValidationResult validation = loadout == null
                ? new EquipmentValidationResult([new("loadout.missing", "No equipment default is authored for this role.")])
                : EquipmentLoadoutValidator.Validate(loadout, validationContext);
            return new ResolvedEquipmentLoadout(
                loadout,
                source,
                element.PersonalEquipmentRole,
                activePersonal,
                validation.Issues);
        }

        public static void SetPersonalLoadout(
            EquipmentLoadoutDoctrine doctrine,
            int soldierId,
            EquipmentLoadout loadout,
            EquipmentValidationContext context = null)
        {
            EnsureValid(loadout, context);
            doctrine?.SetPersonalLoadout(soldierId, loadout);
        }

        public static void SetRoleDefault(
            EquipmentLoadoutDoctrine doctrine,
            PersonalEquipmentRole role,
            EquipmentLoadout loadout,
            EquipmentValidationContext context = null)
        {
            if (role == null) throw new ArgumentNullException(nameof(role));
            EnsureValid(loadout, context);
            doctrine?.SetRoleDefault(role.Id, loadout);
        }

        private static void EnsureValid(EquipmentLoadout loadout, EquipmentValidationContext context)
        {
            EquipmentValidationResult validation = EquipmentLoadoutValidator.Validate(loadout, context);
            if (!validation.IsValid)
            {
                throw new ArgumentException(
                    string.Join(" ", validation.Issues),
                    nameof(loadout));
            }
        }
    }
}
