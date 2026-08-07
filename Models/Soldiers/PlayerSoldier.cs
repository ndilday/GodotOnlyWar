using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Models.Squads;

namespace OnlyWar.Models.Soldiers
{
    // PlayerSoldier uses the decorator pattern to extend the Soldier class
    // with features we're only interested in for the player's troops
    public class PlayerSoldier : ISoldier
    {
        private readonly Soldier _soldier;
        private readonly List<SoldierEvent> _soldierEvents;
        private readonly List<SoldierEvaluation> _soldierEvaluationHistory;
        private readonly List<SoldierAward> _soldierAwards;
        private readonly Dictionary<int, ushort> _rangedWeaponCasualtyCountMap;
        private readonly Dictionary<int, ushort> _meleeWeaponCasualtyCountMap;
        private readonly Dictionary<int, ushort> _factionCasualtyCountMap;
        private Squad _assignedSquad;

        public Date ProgenoidImplantDate { get; set; }
        // Null identifies a founding-era brother whose implantation decisions were
        // made before the campaign began. Recruited neophytes retain the exact score
        // rolled as aspirants so their Phase 13 risk can be resolved later.
        public float? GeneticCompatibility { get; set; }
        public Date RecruitmentBirthDate { get; set; }
        public IReadOnlyList<SoldierEvent> SoldierEvents { get => _soldierEvents; }
        public IReadOnlyDictionary<int, ushort> RangedWeaponCasualtyCountMap { get => _rangedWeaponCasualtyCountMap; }
        public IReadOnlyDictionary<int, ushort> MeleeWeaponCasualtyCountMap { get => _meleeWeaponCasualtyCountMap; }
        public IReadOnlyDictionary<int, ushort> FactionCasualtyCountMap { get => _factionCasualtyCountMap; }
        public IReadOnlyList<SoldierEvaluation> SoldierEvaluationHistory { get => _soldierEvaluationHistory; }
        public IReadOnlyList<SoldierAward> SoldierAwards { get => _soldierAwards; }

        #region ISoldier passthrough
        public int Id => _soldier.Id;

        public string Name => _soldier.Name;

        public SoldierTemplate Template { get => _soldier.Template; set => _soldier.Template = value; }

        public float Strength => _soldier.Strength;

        public float Dexterity => _soldier.Dexterity;

        public float Constitution => _soldier.Constitution;

        public float Perception => _soldier.Perception;

        public float Intelligence => _soldier.Intelligence;

        public float Ego => _soldier.Ego;

        public float Charisma => _soldier.Charisma;

        public float PsychicPower => _soldier.PsychicPower;

        public float AttackSpeed => _soldier.AttackSpeed;

        public float Size => _soldier.Size;

        public float MoveSpeed => _soldier.MoveSpeed;

        public Body Body => _soldier.Body;

        public IReadOnlyList<int> FunctioningHandGroupIds => _soldier.FunctioningHandGroupIds;
        public int FunctioningHands => _soldier.FunctioningHands;
        public bool CanUseTwoHandedWeapon => _soldier.CanUseTwoHandedWeapon;

        public IReadOnlyCollection<Skill> Skills => _soldier.Skills;

        public Squad AssignedSquad
        {
            get { return _assignedSquad; }
            set { _assignedSquad = value; }
        }

        public bool CanFight
        {
            get
            {
                return _soldier.CanFight;
            }
        }

        public bool CanMove
        {
            get
            {
                return _soldier.CanMove;
            }
        }

        public float MotiveSpeedMultiplier
        {
            get
            {
                return _soldier.MotiveSpeedMultiplier;
            }
        }

        public bool IsCombatEffective
        {
            get
            {
                return _soldier.IsCombatEffective;
            }
        }

        public bool IsWounded
        {
            get
            {
                return _soldier.Body.HitLocations.Any(hl => hl.Wounds.WoundTotal > 0);
            }
        }

        /// <summary>
        /// May the player send this brother out with a squad? Resolved in Phase 3 of
        /// Design/Active/CasualtyRealism.md (§3.3 "Deployability"): a brother is deployable
        /// exactly when he is still combat effective -- he can bring a weapon to bear AND his
        /// motive wounds have not taken his speed to zero.
        ///
        /// This deliberately replaces the old inline motive-vs-vital split, which barred anyone
        /// with a crippled motive location. Under graded impairment that rule would bar a marine
        /// limping at 0.6 speed who can genuinely still fight, which is the exact outcome this
        /// phase exists to stop producing. The vital half is unchanged in effect (a crippled
        /// vital already clears <see cref="CanFight"/>), and the new form additionally bars a
        /// brother with no functioning hand -- which the old check missed.
        /// </summary>
        public bool IsDeployable => IsCombatEffective;

        /// <summary>
        /// The operation this brother has been attached to as an individual, without his home
        /// squad (Design/Active/SpecialistAttachment.md). Null for the overwhelming majority of
        /// the roster. He remains in <see cref="AssignedSquad"/>'s Members throughout --
        /// removing him would make him load back as a fallen brother.
        ///
        /// Deliberately NOT on ISoldier: attachment is a player-chapter concept, and ISoldier is
        /// also implemented by plain Soldier and by test doubles.
        ///
        /// Set only through Helpers/Orders/OrderAttachment, which owns both halves of the
        /// pointer pair (Order.AttachedSoldiers is the other).
        /// </summary>
        public Orders.Order AttachedOrder { get; set; }

        /// <summary>
        /// Where this brother physically is for campaign purposes: with the operation he is
        /// attached to if he is attached, otherwise wherever his squad is. An attached
        /// Apothecary's home squad may sit aboard ship while he is forward, so anything asking
        /// "where is this man" must go through here rather than AssignedSquad.CurrentRegion.
        /// </summary>
        public Planets.Region EffectiveRegion =>
            AttachedOrder?.Mission?.RegionFaction?.Region ?? AssignedSquad?.CurrentRegion;

        public void AddSkillPoints(BaseSkill skill, float points)
        {
            _soldier.AddSkillPoints(skill, points);
        }

        public void AddAttributePoints(Attribute attribute, float points)
        {
            _soldier.AddAttributePoints(attribute, points);
        }

        public float GetTotalSkillValue(BaseSkill skill)
        {
            return _soldier.GetTotalSkillValue(skill);
        }

        public Skill GetBestSkillInCategory(SkillCategory category)
        {
            return _soldier.GetBestSkillInCategory(category);
        }

        #endregion

        public PlayerSoldier(Soldier soldier, string name)
        {
            _soldier = soldier;
            _soldier.Name = name;
            _soldierEvents = [];
            _soldierEvaluationHistory = [];
            _soldierAwards = [];
            _rangedWeaponCasualtyCountMap = [];
            _meleeWeaponCasualtyCountMap = [];
            _factionCasualtyCountMap = [];
            if (soldier.AssignedSquad != null)
            {
                _assignedSquad = soldier.AssignedSquad;
                soldier.AssignedSquad = null;
                AssignedSquad.RemoveSquadMember(soldier);
                AssignedSquad.AddSquadMember(this);
            }
        }

        public PlayerSoldier(Soldier soldier, List<SoldierEvaluation> evaluations,
                             List<SoldierAward> awards, Date implantDate, List<SoldierEvent> events,
                             Dictionary<int, ushort> rangedWeaponCasualties,
                             Dictionary<int, ushort> meleeWeaponCasualties,
                             Dictionary<int, ushort> factionCasualties)
        {
            _soldier = soldier;
            _soldierEvents = events;
            _soldierEvaluationHistory = evaluations;
            _soldierAwards = awards;
            ProgenoidImplantDate = implantDate;
            _rangedWeaponCasualtyCountMap = rangedWeaponCasualties;
            _meleeWeaponCasualtyCountMap = meleeWeaponCasualties;
            _factionCasualtyCountMap = factionCasualties;
            if(soldier.AssignedSquad != null)
            {
                _assignedSquad = soldier.AssignedSquad;
                soldier.AssignedSquad = null;
                AssignedSquad.RemoveSquadMember(soldier);
                AssignedSquad.AddSquadMember(this);
            }
        }

        public object Clone()
        {
            PlayerSoldier clone = new(
                                     (Soldier)_soldier.Clone(), _soldierEvaluationHistory.ToList(),
                                     _soldierAwards.ToList(), ProgenoidImplantDate, _soldierEvents.ToList(),
                                     _rangedWeaponCasualtyCountMap.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                                     _meleeWeaponCasualtyCountMap.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                                     _factionCasualtyCountMap.ToDictionary(kvp => kvp.Key, kvp => kvp.Value))
            {
                GeneticCompatibility = GeneticCompatibility,
                RecruitmentBirthDate = RecruitmentBirthDate == null
                    ? null
                    : new Date(
                        RecruitmentBirthDate.Millenium,
                        RecruitmentBirthDate.Year,
                        RecruitmentBirthDate.Week)
            };
            return clone;
        }

        public void AddEvent(SoldierEvent soldierEvent)
        {
            _soldierEvents.Add(soldierEvent);
        }

        public void AddEvaluation(SoldierEvaluation evaluation)
        {
            _soldierEvaluationHistory.Add(evaluation);
        }

        public void AddAward(SoldierAward award)
        {
            _soldierAwards.Add(award);
        }

        public void AddRangedKill(int factionId, int weaponTemplateId)
        {
            if (_rangedWeaponCasualtyCountMap.ContainsKey(weaponTemplateId))
            {
                _rangedWeaponCasualtyCountMap[weaponTemplateId]++;
            }
            else
            {
                _rangedWeaponCasualtyCountMap[weaponTemplateId] = 1;
            }

            if (_factionCasualtyCountMap.ContainsKey(factionId))
            {
                _factionCasualtyCountMap[factionId]++;
            }
            else
            {
                _factionCasualtyCountMap[factionId] = 1;
            }
        }

        public void AddMeleeKill(int factionId, int weaponTemplateId)
        {
            if (_meleeWeaponCasualtyCountMap.ContainsKey(weaponTemplateId))
            {
                _meleeWeaponCasualtyCountMap[weaponTemplateId]++;
            }
            else
            {
                _meleeWeaponCasualtyCountMap[weaponTemplateId] = 1;
            }

            if (_factionCasualtyCountMap.ContainsKey(factionId))
            {
                _factionCasualtyCountMap[factionId]++;
            }
            else
            {
                _factionCasualtyCountMap[factionId] = 1;
            }
        }

        public override string ToString()
        {
            return _soldier.ToString();
        }
    }
}
