using System.Collections.Generic;

using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Equippables;

namespace OnlyWar.Helpers.Battles.Resolutions
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

        public WoundResolver()
        {
            WoundQueue = new LifoBuffer<WoundResolution>();
            ResolutionLog = "";
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
            if (!wound.HitLocation.IsSevered)
            {
                bool wasCombatEffective = wound.Suffererer.IsCombatEffective;
                bool wasCrippled = wound.HitLocation.IsCrippled;
                bool wasFunctionallyDisabled = wound.HitLocation.IsSevered || wasCrippled;
                bool hadUntreatedSeveredLimb = wound.Suffererer.HasUntreatedSeveredLimb;
                float totalDamage = wound.Damage;
                WoundLevel woundLevel;
                // check wound.HitLocation for natural armor
                totalDamage -= wound.HitLocation.Template.NaturalArmor;
                // for now, natural armor reducing the damange below 0 will still cause a Negligible injury
                // multiply damage by wound.HitLocation modifier
                totalDamage *= wound.HitLocation.Template.WoundMultiplier;
                // compare total damage to soldier Constitution
                float ratio = totalDamage / wound.Suffererer.Soldier.Constitution;
                if (ratio >= 8.0f)
                {
                    woundLevel = WoundLevel.Unsurvivable;
                }
                else if (ratio >= 4.0f)
                {
                    woundLevel = WoundLevel.Mortal;
                }
                else if (ratio >= 2f)
                {
                    woundLevel = WoundLevel.Massive;
                }
                else if (ratio >= 1f)
                {
                    woundLevel = WoundLevel.Critical;
                }
                else if (ratio >= 0.5f)
                {
                    woundLevel = WoundLevel.Major;
                }
                else if (ratio >= 0.25f)
                {
                    woundLevel = WoundLevel.Moderate;
                }
                else if (ratio >= 0.125f)
                {
                    woundLevel = WoundLevel.Minor;
                }
                else
                {
                    woundLevel = WoundLevel.Negligible;
                }
                wound.HitLocation.Wounds.AddWound(woundLevel);
                wound.Suffererer.RefreshInjuryState();
                OnSoldierWounded?.Invoke(wound, woundLevel);
                wound.Suffererer.BattleSquad?.InvalidateAbleSoldiers();
                wound.Description = $"{wound.Suffererer.Soldier.Name} suffers {woundLevel.ToString()} wound to {wound.HitLocation.Template.Name}\n";

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
