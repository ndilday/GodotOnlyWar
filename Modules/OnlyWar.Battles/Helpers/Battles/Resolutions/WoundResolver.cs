using System.Collections.Generic;

using OnlyWar.Abstractions;
using OnlyWar.Domain.Soldiers;
using OnlyWar.Domain.Equippables;

namespace OnlyWar.Battles.Resolutions
{
    public class WoundResolver : IResolver
    {
        public delegate void SoldierDeathHandler(WoundResolution wound, WoundLevel level);
        public delegate void SoldierFallHandler(WoundResolution wound, WoundLevel level);
        public delegate void SoldierWoundedHandler(WoundResolution wound, WoundLevel level);
        public event SoldierDeathHandler OnSoldierDeath;
        public event SoldierFallHandler OnSoldierFall;
        public event SoldierWoundedHandler OnSoldierWounded;

        public string ResolutionLog { get; private set; }
        public LifoBuffer<WoundResolution> WoundQueue { get; private set; }

        // Routes hits between a location and its augmetic organs. It is drawn only for a location
        // that holds an organ, so a battle with no augmetic organs uses the stream exactly as before.
        private readonly IRNG _random;

        public WoundResolver(IRNG random = null)
        {
            WoundQueue = new LifoBuffer<WoundResolution>();
            ResolutionLog = "";
            _random = random;
        }

        public void Resolve()
        {
            ResolutionLog = "";
            while(!WoundQueue.IsEmpty)
            {
                WoundQueue.TryTake(out WoundResolution wound);
                HandleWound(wound);
            }
        }

        private void HandleWound(WoundResolution wound)
        {
            AugmeticOrgan organ = SelectAugmeticOrgan(wound.HitLocation);
            if (organ != null)
            {
                HandleOrganWound(wound, organ);
                return;
            }
            if (!wound.HitLocation.IsSevered)
            {
                bool wasCombatEffective = wound.Suffererer.IsCombatEffective;
                bool wasCrippled = wound.HitLocation.IsCrippled;
                bool wasFunctionallyDisabled = wound.HitLocation.IsSevered || wasCrippled;
                bool hadUntreatedSeveredLimb = wound.Suffererer.HasUntreatedSeveredLimb;
                // Read before the wound: a part severed by this hit stops being cybernetic.
                bool wasCybernetic = wound.HitLocation.IsCybernetic;
                // compare total damage to the constitution that applies at this location
                WoundLevel woundLevel = ClassifyWound(
                    wound,
                    wound.HitLocation.EffectiveConstitution(wound.Suffererer.Soldier));
                wound.HitLocation.Wounds.AddWound(woundLevel);
                wound.Suffererer.RefreshInjuryState();
                OnSoldierWounded?.Invoke(wound, woundLevel);
                wound.Suffererer.BattleSquad?.InvalidateAbleSoldiers();
                // A cybernetic part takes damage, not wounds.
                wound.Description = wasCybernetic
                    ? $"{wound.Suffererer.Soldier.Name} suffers {woundLevel} damage to cybernetic {wound.HitLocation.Template.Name}\n"
                    : $"{wound.Suffererer.Soldier.Name} suffers {woundLevel} wound to {wound.HitLocation.Template.Name}\n";
                if (wasCybernetic && wound.HitLocation.IsSevered)
                {
                    wound.Description += $"The cybernetic {wound.HitLocation.Template.Name} is torn away\n";
                }

                // See whether this hit crossed a battlefield incapacity boundary. A severed limb
                // is a distinct boundary from a crippled location: a foot or already-crippled
                // hand group can leave a brother combat-effective until the next hit severs it.
                // The body query keeps this anatomy-driven and also ensures a replacement, once
                // complete, is no longer treated as an untreated severance.
                bool newlyUntreatedSeveredLimb = !hadUntreatedSeveredLimb
                    && wound.Suffererer.HasUntreatedSeveredLimb;
                if ((!wasFunctionallyDisabled
                        && (wound.HitLocation.IsSevered || wound.HitLocation.IsCrippled))
                    || newlyUntreatedSeveredLimb)
                {
                    // OnSoldierFall is the "out of the fight, alive" hook -- what
                    // Design/Reference/CasualtyRealism.md §2.3 names Incapacitated. The predicate for
                    // it is IsCombatEffective, and that stays the right one now that the state has
                    // a name: a soldier is out of the battle exactly when he can no longer both
                    // fight and move, which is precisely what the battle layer removes him for.
                    // The narrower CanFight would fire on a man who can still shoot from where he
                    // stands, and there is no prone fire to let him (§2.2 cuts stance), so it
                    // would report a fall the engine does not actually perform.
                    //
                    // A crippled motive location is not necessarily the same thing as !CanMove:
                    // graded impairment keeps a crippled foot mobile. Untreated severance is an
                    // explicit exception, so the branch tests IsCombatEffective after the body
                    // refresh rather than inferring the result from this one location.
                    if (wound.HitLocation.Template.IsMotive)
                    {
                        if (wasCombatEffective && !wound.Suffererer.IsCombatEffective)
                        {
                            wound.Description += newlyUntreatedSeveredLimb
                                ? $"{wound.Suffererer.Soldier.Name} is incapacitated by limb severance\n"
                                : $"{wound.Suffererer.Soldier.Name} can no longer walk\n";
                            OnSoldierFall.Invoke(wound, woundLevel);
                        }
                    }
                    else if (wound.HitLocation.Template.HandGroupId.HasValue)
                    {
                        wound.Suffererer.DropWeaponsUsingHandGroup(
                            wound.HitLocation.Template.HandGroupId.Value);
                        if (wasCombatEffective && !wound.Suffererer.IsCombatEffective)
                        {
                            wound.Description += $"{wound.Suffererer.Soldier.Name} can no longer fight\n";
                            OnSoldierFall.Invoke(wound, woundLevel);
                        }
                    }
                    // A crippled vital is incapacitating. For a player
                    // soldier, power-armor biostasis holds him, and
                    // PlayerChapterBattleAftermathPolicy decides at battle end whether he died
                    // (severed vital, or abandoned on ground the Chapter did not hold).
                    if (wound.HitLocation.Template.IsVital && wound.HitLocation.IsCrippled)
                    {
                        wound.Description += $"{wound.Suffererer.Soldier.Name} has died\n";
                        OnSoldierDeath.Invoke(wound, woundLevel);
                    }
                }
            }
            else
            {
                wound.Description = $"The hit further mangles the {wound.HitLocation.Template.Name}\n";
            }
        }

        /// <summary>
        /// Design/Active/VitalLocationReplacement.md §3.4: a penetrating hit to a location with n
        /// augmetic organs strikes one of them with probability n x the per-organ chance, the
        /// organ chosen uniformly. Null means the hit strikes the organic part.
        /// </summary>
        private AugmeticOrgan SelectAugmeticOrgan(HitLocation location)
        {
            IReadOnlyList<AugmeticOrgan> organs = location.AugmeticOrgans;
            if (organs.Count == 0 || _random == null || location.IsSevered)
            {
                return null;
            }
            double roll = _random.GetLinearDouble();
            double perOrgan = VitalOrganRules.GetAugmeticOrganHitChance(organs.Count) / organs.Count;
            int index = (int)(roll / perOrgan);
            return index < organs.Count ? organs[index] : null;
        }

        // A wound to an augmetic organ: measured against the organ's constitution and put on the
        // organ's own wound track, with its host location's thresholds. A crippled organ has the
        // effect of a crippled vital location.
        private void HandleOrganWound(WoundResolution wound, AugmeticOrgan organ)
        {
            // The log names the location that was hit and then the augmetic organ the hit struck,
            // so a reader sees that the wound went to an augmetic and not to the flesh.
            string hostName = wound.HitLocation.Template.Name;
            string organName = $"augmetic organ {organ.Index} in the {hostName}";
            if (organ.IsSevered)
            {
                wound.Description = $"The hit to the {hostName} further mangles the destroyed {organName}\n";
                return;
            }

            bool wasCrippled = organ.IsCrippled;
            WoundLevel woundLevel = ClassifyWound(
                wound, organ.EffectiveConstitution(wound.Suffererer.Soldier));
            organ.Wounds.AddWound(woundLevel);
            wound.Suffererer.RefreshInjuryState();
            OnSoldierWounded?.Invoke(wound, woundLevel);
            wound.Suffererer.BattleSquad?.InvalidateAbleSoldiers();
            wound.Description = $"{wound.Suffererer.Soldier.Name} suffers {woundLevel} damage to {organName}\n";
            if (organ.IsSevered)
            {
                wound.Description += $"The {organName} is destroyed\n";
            }
            if (!wasCrippled && organ.IsCrippled && wound.HitLocation.Template.IsVital)
            {
                wound.Description += $"{wound.Suffererer.Soldier.Name} has died\n";
                OnSoldierDeath?.Invoke(wound, woundLevel);
            }
        }

        // wound.Damage is already past worn and natural armor: the attack actions subtract both,
        // scaled by the weapon's armor multiplier, before the penetration check.
        private static WoundLevel ClassifyWound(WoundResolution wound, float constitution)
        {
            // multiply damage by wound.HitLocation modifier
            float totalDamage = wound.Damage * wound.HitLocation.Template.WoundMultiplier;
            float ratio = totalDamage / constitution;
            if (ratio >= 8.0f) return WoundLevel.Unsurvivable;
            if (ratio >= 4.0f) return WoundLevel.Mortal;
            if (ratio >= 2f) return WoundLevel.Massive;
            if (ratio >= 1f) return WoundLevel.Critical;
            if (ratio >= 0.5f) return WoundLevel.Major;
            if (ratio >= 0.25f) return WoundLevel.Moderate;
            if (ratio >= 0.125f) return WoundLevel.Minor;
            return WoundLevel.Negligible;
        }
    }

    /// <summary>
    /// Minimal single-threaded replacement for ConcurrentBag. Add/TryTake deliberately retain
    /// the bag's existing LIFO behavior so seeded wound resolution order remains unchanged.
    /// </summary>
    public sealed class LifoBuffer<T>
    {
        private readonly List<T> _items = [];

        public bool IsEmpty => _items.Count == 0;

        public void Add(T item)
        {
            _items.Add(item);
        }

        public bool TryTake(out T item)
        {
            int index = _items.Count - 1;
            if (index < 0)
            {
                item = default;
                return false;
            }

            item = _items[index];
            _items.RemoveAt(index);
            return true;
        }
    }
}
