using System.Collections.Concurrent;

using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Equippables;

namespace OnlyWar.Helpers.Battles.Resolutions
{
    public class WoundResolver : IResolver
    {
        public delegate void SoldierDeathHandler(WoundResolution wound, WoundLevel level);
        public delegate void SoldierFallHandler(WoundResolution wound, WoundLevel level);
        public event SoldierDeathHandler OnSoldierDeath;
        public event SoldierFallHandler OnSoldierFall;

        public string ResolutionLog { get; private set; }
        public ConcurrentBag<WoundResolution> WoundQueue { get; private set; }

        public WoundResolver()
        {
            WoundQueue = [];
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
                wound.Description = $"{wound.Suffererer.Soldier.Name} suffers {woundLevel.ToString()} wound to {wound.HitLocation.Template.Name}\n";

                // see if wound.HitLocation is now severed
                if (wound.HitLocation.IsSevered || wound.HitLocation.IsCrippled)
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
                    }
                    else if(wound.HitLocation.Template.IsMeleeWeaponHolder)
                    {
                        if (wound.Suffererer.EquippedMeleeWeapons.Count > 0 && wound.Suffererer.EquippedMeleeWeapons[0].Template.Location == EquipLocation.OneHand)
                        {
                            wound.Suffererer.EquippedMeleeWeapons.RemoveAt(0);
                        }
                    }
                    if(wound.HitLocation.Template.IsVital && wound.HitLocation.IsCrippled)
                    {
                        wound.Description += $"{wound.Suffererer.Soldier.Name} has died\n";
                        OnSoldierDeath.Invoke(wound, woundLevel);
                    }
                }
            }
        }
    }
}
