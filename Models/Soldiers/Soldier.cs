using OnlyWar.Models.Squads;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace OnlyWar.Models.Soldiers
{
    public class Soldier : ISoldier
    {
        protected readonly Dictionary<int, Skill> _skills;
        private Body _body;
        private KeyValuePair<int, HitLocation[]>[] _handLocationsByGroupId = [];
        private IReadOnlyList<int> _functioningHandGroupIds = Array.Empty<int>();
        private Body _functioningHandBody;
        private int _functioningHandRevision = -1;

        public Soldier(BodyTemplate body)
        {
            _skills = [];
            Body = new Body(body);
        }

        public Soldier(List<HitLocation> hitLocations, List<Skill> skills)
        {
            _skills = skills.ToDictionary(skill => skill.BaseSkill.Id);
            Body = new Body(hitLocations);
        }

        public object Clone()
        {
            Soldier newSoldier = new Soldier(Body.HitLocations.ToList(), _skills.Values.ToList());
            newSoldier.Id = Id;
            newSoldier.Strength = Strength;
            newSoldier.Dexterity = Dexterity;
            newSoldier.Perception = Perception;
            newSoldier.Intelligence = Intelligence;
            newSoldier.Ego = Ego;
            newSoldier.Charisma = Charisma;
            newSoldier.Constitution = Constitution;
            newSoldier.PsychicPower = PsychicPower;
            newSoldier.AttackSpeed = AttackSpeed;
            newSoldier.Size = Size;
            newSoldier.MoveSpeed = MoveSpeed;
            newSoldier.Name = Name;
            newSoldier.Template = Template;
            return newSoldier;
        }

        /// <summary>
        /// The hand groups this soldier can still use. Recomputed only when his injuries actually
        /// change: the getter used to allocate a fresh <c>List&lt;int&gt;</c> on every read, and it
        /// is read repeatedly per soldier per battle turn -- by <see cref="FunctioningHands"/>,
        /// <see cref="CanUseTwoHandedWeapon"/>, weapon gripping, and the battle planners' injury
        /// snapshot. The result is a pure function of the body's wound state, so
        /// <see cref="Body.InjuryRevision"/> is an exact cache key.
        /// </summary>
        public IReadOnlyList<int> FunctioningHandGroupIds
        {
            get
            {
                Body body = _body;
                if (!ReferenceEquals(_functioningHandBody, body)
                    || _functioningHandRevision != body.InjuryRevision)
                {
                    RefreshFunctioningHandGroupIds(body);
                }
                return _functioningHandGroupIds;
            }
        }

        private void RefreshFunctioningHandGroupIds(Body body)
        {
            List<int> functioningGroupIds = [];
            foreach ((int groupId, HitLocation[] locations) in _handLocationsByGroupId)
            {
                bool isFunctioning = true;
                foreach (HitLocation location in locations)
                {
                    uint wounds = location.Wounds.WoundTotal;
                    if (wounds >= location.Template.CrippleWound
                        || wounds >= location.Template.SeverWound)
                    {
                        isFunctioning = false;
                        break;
                    }
                }

                if (isFunctioning)
                {
                    functioningGroupIds.Add(groupId);
                }
            }

            // Wrapped once, here, rather than by every caller: the collection is handed out to
            // battle code that holds it across a turn, so it must not be mutable through the cast.
            _functioningHandGroupIds = new ReadOnlyCollection<int>(functioningGroupIds);
            _functioningHandBody = body;
            _functioningHandRevision = body.InjuryRevision;
        }

        public int FunctioningHands => FunctioningHandGroupIds.Count;
        public bool CanUseTwoHandedWeapon => FunctioningHands >= 2;

        public float Strength { get; set; }
        public float Dexterity { get; set; }
        public float Perception { get; set; }
        public float Intelligence { get; set; }
        public float Ego { get; set; }
        public float Charisma { get; set; }
        public float Constitution { get; set; }
        public float PsychicPower { get; set; }
        public float AttackSpeed { get; set; }
        public float Size { get; set; }
        public float MoveSpeed { get; set; }
        public Body Body
        {
            get => _body;
            set
            {
                _body = value;
                _handLocationsByGroupId = value.HitLocations
                    .Where(location => location.Template.HandGroupId.HasValue)
                    .GroupBy(location => location.Template.HandGroupId.Value)
                    .OrderBy(group => group.Key)
                    .Select(group => new KeyValuePair<int, HitLocation[]>(group.Key, group.ToArray()))
                    .ToArray();
            }
        }
        public int Id { get; set; }
        public string Name { get; set; }
        public SoldierTemplate Template { get; set; }
        public IReadOnlyCollection<Skill> Skills { get => _skills.Values; }

        public Squad AssignedSquad { get; set; }

        /// <summary>
        /// Can this soldier still bring a weapon to bear? Hands and vital (consciousness)
        /// locations only -- motive locations are deliberately excluded; see
        /// <see cref="CanMove"/>. Callers that mean "is this soldier in the fight at all"
        /// want <see cref="IsCombatEffective"/>, not this.
        /// </summary>
        public bool CanFight
        {
            get
            {
                foreach (HitLocation location in Body.HitLocations)
                {
                    HitLocationTemplate template = location.Template;
                    if (!template.IsVital) continue;
                    if (IsDisabled(location)) return false;
                }

                return FunctioningHands > 0;
            }
        }

        /// <summary>
        /// How much foot speed his motive wounds leave him -- the product of every motive
        /// location's banded multiplier (<see cref="MotiveImpairment"/>).
        /// </summary>
        public float MotiveSpeedMultiplier => MotiveImpairment.CalculateSpeedMultiplier(Body);

        /// <summary>
        /// Can this soldier still walk? Motive locations only, and since Phase 3 of the
        /// casualty-realism plan this is exactly "his speed has not reached zero" rather than
        /// "no motive location is crippled". The difference is the whole point of the phase: a
        /// marine with one Critical leg keeps fighting at 0.6 speed instead of dropping, and a
        /// man with a shattered foot never drops at all.
        /// </summary>
        public bool CanMove => MotiveSpeedMultiplier > 0f;

        /// <summary>
        /// The old combined <c>CanFight</c>: able to both fight and move, i.e. still a
        /// participant in a battle rather than a casualty. This is what deployment gating,
        /// strength counts, and target selection mean.
        /// </summary>
        public bool IsCombatEffective => CanFight && CanMove;

        private static bool IsDisabled(HitLocation location)
        {
            uint wounds = location.Wounds.WoundTotal;
            return wounds >= location.Template.CrippleWound
                || wounds >= location.Template.SeverWound;
        }

        public void AddSkillPoints(BaseSkill skill, float points)
        {
            if(!_skills.ContainsKey(skill.Id))
            {
                _skills[skill.Id] = new Skill(skill, points);
            }
            else
            {
                _skills[skill.Id].AddPoints(points);
            }
        }

        public float GetTotalSkillValue(BaseSkill skill)
        {
            float attribute = GetStatForBaseAttribute(skill.BaseAttribute);
            if(!_skills.ContainsKey(skill.Id))
            {
                return attribute - 4;
            }

            return _skills[skill.Id].SkillBonus + attribute;
        }

        public float GetStatForBaseAttribute(Attribute attribute)
        {
            switch (attribute)
            {
                case Attribute.Dexterity:
                    return Dexterity;
                case Attribute.Intelligence:
                    return Intelligence;
                case Attribute.Ego:
                    return Ego;
                case Attribute.Presence:
                    return Charisma;
                case Attribute.Strength:
                    return Strength;
                case Attribute.Constitution:
                    return Constitution;
                default:
                    return Dexterity;
            }
        }

        public void AddAttributePoints(Attribute attribute, float points)
        {
            float curPoints;
            switch(attribute)
            {
                case Attribute.Constitution:
                    curPoints = (float)Math.Pow(2, Constitution - 11) * 10;
                    Constitution = (float)Math.Log((curPoints + points) / 10.0f, 2) + 11;
                    break;
                case Attribute.Dexterity:
                    curPoints = (float)Math.Pow(2, Dexterity - 11) * 10;
                    Dexterity = (float)Math.Log((curPoints + points) / 10.0f, 2) + 11;
                    break;
                case Attribute.Ego:
                    curPoints = (float)Math.Pow(2, Ego - 11) * 10;
                    Ego = (float)Math.Log((curPoints + points) / 10.0f, 2) + 11;
                    break;
                case Attribute.Intelligence:
                    curPoints = (float)Math.Pow(2, Intelligence - 11) * 10;
                    Intelligence = (float)Math.Log((curPoints + points) / 10.0f, 2) + 11;
                    break;
                case Attribute.Presence:
                    curPoints = (float)Math.Pow(2, Charisma - 11) * 10;
                    Charisma = (float)Math.Log((curPoints + points) / 10.0f, 2) + 11;
                    break;
                case Attribute.Strength:
                    curPoints = (float)Math.Pow(2, Strength - 11) * 10;
                    Strength = (float)Math.Log((curPoints + points) / 10.0f, 2) + 11;
                    break;
            }

        }
    
        public Skill GetBestSkillInCategory(SkillCategory category)
        {
            return _skills.Values.Where(s => s.BaseSkill.Category == category).OrderByDescending(s => s.SkillBonus).First();
        }

        public override string ToString()
        {
            return Template + " " + Name;
        }
    }
}
