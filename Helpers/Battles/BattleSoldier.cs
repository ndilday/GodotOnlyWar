using System;
using System.Collections.Generic;
using System.Linq;

using OnlyWar.Models.Equippables;
using OnlyWar.Models.Soldiers;

namespace OnlyWar.Helpers.Battles
{
    public class BattleSoldier
    {
        public ISoldier Soldier { get; private set; }

        public Tuple<int, int> TopLeft { get; set; }
        public ushort Orientation { get; set; }
        public BattleSquad BattleSquad { get; private set; }

        public List<RangedWeapon> EquippedRangedWeapons { get; private set; }

        public List<MeleeWeapon> EquippedMeleeWeapons { get; private set; }

        public List<MeleeWeapon> MeleeWeapons { get; private set; }
        public List<RangedWeapon> RangedWeapons { get; private set; }
        public Armor Armor { get; set; }
        public int? TargetId { get; set; }
        public bool IsInMelee { get; set; }
        public ushort ReloadingPhase { get; set; }
        public Stance Stance { get; set; }
        public float CurrentSpeed { get; set; }

        public float TurnsRunning { get; set; }
        public ushort TurnsShooting { get; set; }
        public ushort TurnsSwinging { get; set; } 
        public ushort TurnsDefending { get; set; }
        public ushort TurnsAiming { get; set; }
        public uint WoundsTaken { get; set; }

        public ushort EnemiesTakenDown { get; set; }

        public int HandsFree
        {
            get
            {
                int handCount = Soldier.FunctioningHands;
                foreach(RangedWeapon weapon in EquippedRangedWeapons)
                {
                    handCount -= GetHandsForWeapon(weapon.Template);
                }
                foreach(MeleeWeapon weapon in EquippedMeleeWeapons)
                {
                    handCount -= GetHandsForWeapon(weapon.Template);
                }
                return handCount;
            }
        }

        public Tuple<int, int> BottomRight
        {
            get
            {
                if(Orientation % 2 == 0)
                {
                    return new Tuple<int, int>(TopLeft.Item1 + Soldier.Template.Species.Width,
                                               TopLeft.Item2 - Soldier.Template.Species.Depth);
                }
                else
                {
                    return new Tuple<int, int>(TopLeft.Item1 + Soldier.Template.Species.Depth,
                                               TopLeft.Item2 - Soldier.Template.Species.Width);
                }
            }
        }

        public IReadOnlyList<Tuple<int, int>> PositionList
        {
            get
            {
                List<Tuple<int, int>> list = [];
                for (int w = TopLeft.Item1; w < BottomRight.Item1; w++)
                {
                    for (int d = BottomRight.Item2; d < TopLeft.Item2; d++)
                    {
                        list.Add(new Tuple<int, int>(w, d));
                    }
                }
                return list;
            }
        }

        // aim stores the target, aiming weapon, and addiional seconds the aim has been maintained
        public Tuple<int, RangedWeapon, int> Aim { get; set; }

        public BattleSoldier(ISoldier soldier, BattleSquad squad)
        {
            Soldier = soldier;
            BattleSquad = squad;
            MeleeWeapons = [];
            RangedWeapons = [];
            EquippedMeleeWeapons = [];
            EquippedRangedWeapons = [];
            TopLeft = null;
            Aim = null;
            IsInMelee = false;
            Stance = Stance.Standing;
            CurrentSpeed = 0;
            EnemiesTakenDown = 0;
            ReloadingPhase = 0;
            TargetId = null;
        }


        // Copy constructor — the single copy path for BattleSoldier. Used by
        // BattleSquad's copy constructor to snapshot battle state for BattleHistory
        // replay. The underlying ISoldier is shared by design: the replay reads
        // per-snapshot battle fields (position, stance, wounds taken, etc.) and the
        // action log, not an independent body, and the squad back-reference must be
        // set to the cloned squad, which a parameterless Clone() cannot do.
        public BattleSoldier(BattleSoldier soldier, BattleSquad squad)
        {
            Soldier = soldier.Soldier;
            BattleSquad = squad;
            TopLeft = new Tuple<int, int>(soldier.TopLeft.Item1, soldier.TopLeft.Item2);
            Orientation = soldier.Orientation;
            Armor = soldier.Armor;
            IsInMelee = soldier.IsInMelee;
            ReloadingPhase = soldier.ReloadingPhase;
            Stance = soldier.Stance;
            CurrentSpeed = soldier.CurrentSpeed;
            TurnsRunning = soldier.TurnsRunning;
            TurnsShooting = soldier.TurnsShooting;
            TurnsSwinging = soldier.TurnsSwinging;
            TurnsDefending = soldier.TurnsDefending;
            TurnsAiming = soldier.TurnsAiming;
            WoundsTaken = soldier.WoundsTaken;
            EnemiesTakenDown = soldier.EnemiesTakenDown;
            Aim = soldier.Aim == null ? null : new Tuple<int, RangedWeapon, int>(soldier.Aim.Item1, soldier.Aim.Item2, soldier.Aim.Item3);
            EquippedMeleeWeapons = soldier.EquippedMeleeWeapons.ToList();
            EquippedRangedWeapons = soldier.EquippedRangedWeapons.ToList();
            MeleeWeapons = soldier.MeleeWeapons;
            RangedWeapons = soldier.RangedWeapons;
            TargetId = soldier.TargetId;
        }
        public bool CanFight
        {
            get
            {
                return Soldier.CanFight;
            }
        }
        
        public void AddWeapons(IReadOnlyCollection<RangedWeapon> rangedWeapons, IReadOnlyCollection<MeleeWeapon> meleeWeapons)
        {
            if (rangedWeapons?.Count > 0)
            {
                RangedWeapons.AddRange(rangedWeapons);
            }
            if (meleeWeapons?.Count > 0)
            {
                MeleeWeapons.AddRange(meleeWeapons);
            }

            // thrown weapons (grenades) ride on the belt and are thrown directly from
            // it, so they never occupy a hand or compete for the equipped slots
            List<RangedWeapon> handRangedWeapons = RangedWeapons.Where(w => !w.Template.IsThrown).ToList();
            if (handRangedWeapons.Count > 0)
            {
                if (handRangedWeapons.Count == 1 )
                {
                    EquippedRangedWeapons.AddRange(handRangedWeapons);
                }
                else if (handRangedWeapons[0].Template.Location == EquipLocation.OneHand && handRangedWeapons[1].Template.Location == EquipLocation.OneHand)
                {
                    EquippedRangedWeapons.Add(handRangedWeapons[0]);
                    EquippedRangedWeapons.Add(handRangedWeapons[1]);
                }
                else
                {
                    EquippedRangedWeapons.Add(handRangedWeapons[0]);
                }
            }
            if (MeleeWeapons.Count > 0)
            {
                if (EquippedRangedWeapons.Count == 0)
                {
                    // we have two hands free for close combat weapons
                    if (MeleeWeapons.Count == 1)
                    {
                        EquippedMeleeWeapons.AddRange(MeleeWeapons);
                    }
                    else if (MeleeWeapons[0].Template.Location == EquipLocation.OneHand && MeleeWeapons[1].Template.Location == EquipLocation.OneHand)
                    {
                        EquippedMeleeWeapons.Add(MeleeWeapons[0]);
                        EquippedMeleeWeapons.Add(MeleeWeapons[1]);

                    }
                    else
                    {
                        EquippedMeleeWeapons.Add(MeleeWeapons[0]);
                    }
                }
                else if (EquippedRangedWeapons.Count == 1 && EquippedRangedWeapons[0].Template.Location == EquipLocation.OneHand)
                {
                    if(MeleeWeapons[0].Template.Location == EquipLocation.OneHand)
                    {
                        EquippedMeleeWeapons.Add(MeleeWeapons[0]);
                    }
                    else if(MeleeWeapons.Count > 1 && MeleeWeapons[1].Template.Location == EquipLocation.OneHand)
                    {
                        EquippedMeleeWeapons.Add(MeleeWeapons[1]);
                    }
                }
            }
        }

        public float GetMoveSpeed()
        {
            float baseMoveSpeed = Soldier.MoveSpeed;
            bool isSlow = Soldier.Body.HitLocations.Where(hl => hl.Template.IsMotive)
                                                       .Any(hl => hl.Wounds.MajorWounds >= 0);

            // if leg/foot injuries, slow soldier down
            if (isSlow)
            {
                return baseMoveSpeed * 0.75f;
            }
            return baseMoveSpeed;
        }

        public MeleeWeapon GetPrimaryMeleeWeapon(MeleeWeapon defaultWeapon)
        {
            if (EquippedMeleeWeapons.Count > 0)
            {
                return EquippedMeleeWeapons[0];
            }

            return defaultWeapon;
        }

        public MeleeWeapon GetSecondaryMeleeWeapon()
        {
            return IsDualWieldingMelee() ? EquippedMeleeWeapons[1] : null;
        }

        public bool IsDualWieldingMelee()
        {
            return EquippedMeleeWeapons.Count >= 2
                && EquippedMeleeWeapons[0].Template.Location == EquipLocation.OneHand
                && EquippedMeleeWeapons[1].Template.Location == EquipLocation.OneHand;
        }

        public float GetMeleeParryModifier()
        {
            float total = 0;
            foreach (MeleeWeapon weapon in EquippedMeleeWeapons)
            {
                total += weapon.Template.ParryModifier;
            }

            return total;
        }

        public override string ToString()
        {
            return Soldier.Name;
        }

        private int GetHandsForWeapon(WeaponTemplate template)
        {
            switch (template.Location)
            {
                case EquipLocation.OneHand:
                    return 1;
                case EquipLocation.TwoHand:
                    return 2;
                default:
                    return 0;
            }
        }
    }
}
