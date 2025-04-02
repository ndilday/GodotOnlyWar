using OnlyWar.Models.Squads;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OnlyWar.Models.Soldiers
{
    public class Soldier : ISoldier
    {
        protected readonly Dictionary<int, Skill> _skills;

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

        public int FunctioningHands
        {
            get
            {
                int functioningHands = 2;
                if (Body.HitLocations.Any(hl => hl.Template.IsMeleeWeaponHolder && hl.IsCrippled))
                {
                    functioningHands--;
                }
                if (Body.HitLocations.Any(hl => hl.Template.IsRangedWeaponHolder && hl.IsCrippled))
                {
                    functioningHands--;
                }
                return functioningHands;
            }
        }

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
        public Body Body { get; set; }
        public int Id { get; set; }
        public string Name { get; set; }
        public SoldierTemplate Template { get; set; }
        public IReadOnlyCollection<Skill> Skills { get => _skills.Values; }

        public Squad AssignedSquad { get; set; }

        public bool CanFight
        {
            get
            {
                bool canWalk = !Body.HitLocations.Where(hl => hl.Template.IsMotive)
                                                        .Any(hl => hl.IsCrippled || hl.IsSevered);
                bool canFuncion = !Body.HitLocations.Where(hl => hl.Template.IsVital)
                                                           .Any(hl => hl.IsCrippled || hl.IsSevered);
                bool canShoot = !Body.HitLocations.Where(hl => hl.Template.IsRangedWeaponHolder)
                                                        .All(hl => hl.IsCrippled || hl.IsSevered);
                bool canFight = !Body.HitLocations.Where(hl => hl.Template.IsMeleeWeaponHolder)
                                                        .All(hl => hl.IsCrippled || hl.IsSevered);
                return canWalk && canFuncion && canShoot && canFight;
            }
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
