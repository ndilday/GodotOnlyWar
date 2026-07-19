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
                bool wasCrippled = wound.HitLocation.IsCrippled;
                bool wasFunctionallyDisabled = wound.HitLocation.IsSevered || wasCrippled;
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
                OnSoldierWounded?.Invoke(wound, woundLevel);
                wound.Suffererer.BattleSquad?.InvalidateAbleSoldiers();
                wound.Description = $"{wound.Suffererer.Soldier.Name} suffers {woundLevel.ToString()} wound to {wound.HitLocation.Template.Name}\n";

                // see if wound.HitLocation is now severed
                if (!wasFunctionallyDisabled && (wound.HitLocation.IsSevered || wound.HitLocation.IsCrippled))
                {
                    // if severed, see if it's an arm or leg
                    if (wound.HitLocation.Template.IsMotive)
                    {
                        wound.Description += $"{wound.Suffererer.Soldier.Name} can no longer walk\n";
                        OnSoldierFall.Invoke(wound, woundLevel);
                    }
                    else if(wound.HitLocation.Template.IsRangedWeaponHolder)
                    {
                        if(wound.Suffererer.EquippedRangedWeapons.Count > 0 && wound.Suffererer.EquippedRangedWeapons[0].Template.Location == EquipLocation.OneHand)
                        {
                            wound.Suffererer.EquippedRangedWeapons.RemoveAt(0);
                        }
                        if (!wound.Suffererer.CanFight)
                        {
                            OnSoldierFall.Invoke(wound, woundLevel);
                        }
                    }
                    else if(wound.HitLocation.Template.IsMeleeWeaponHolder)
                    {
                        if (wound.Suffererer.EquippedMeleeWeapons.Count > 0 && wound.Suffererer.EquippedMeleeWeapons[0].Template.Location == EquipLocation.OneHand)
                        {
                            wound.Suffererer.EquippedMeleeWeapons.RemoveAt(0);
                        }
                        if (!wound.Suffererer.CanFight)
                        {
                            OnSoldierFall.Invoke(wound, woundLevel);
                        }
                    }
                    if (wound.HitLocation.Template.IsVital && wound.HitLocation.IsCrippled)
                    {
                        wound.Description += $"{wound.Suffererer.Soldier.Name} has died\n";
                        OnSoldierDeath.Invoke(wound, woundLevel);
                    }
                }
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
