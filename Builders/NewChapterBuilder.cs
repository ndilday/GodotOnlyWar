﻿
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using OnlyWar.Models;
using OnlyWar.Models.Fleets;
using OnlyWar.Helpers;
using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;
using OnlyWar.Models.Units;

namespace OnlyWar.Builders
{
    internal static class NewChapterBuilder
    {
        private const int INITIAL_TRAINING_DURATION_WEEKS = 104;
        private delegate void TrainingFunction(PlayerSoldier playerSoldier);

        internal static PlayerForce CreateChapter(GameRulesData data,
                                                  ISoldierTrainingService trainingService,
                                                  Date trainingStartDate,
                                                  Date date)
        {
            Date trainingEndDate =
                new Date(trainingStartDate.GetTotalWeeks() + INITIAL_TRAINING_DURATION_WEEKS);
            List<PlayerSoldier> soldiers = GenerateInitialSoldiers(data, trainingService, trainingStartDate, date, trainingEndDate);

            PlayerForce chapter = BuildChapterStructure(data, trainingEndDate, soldiers);
            foreach (PlayerSoldier soldier in soldiers)
            {
                ApplySoldierTypeTraining(soldier);
                trainingService.EvaluateSoldier(soldier, date);

            }
            // write soldier ratings to a csv file
            //string csv = GetSoldierRatingCsv(soldiers);
            chapter.Fleet.TaskForces.Add(new TaskForce(data.PlayerFaction, data.PlayerFaction.FleetTemplates.First().Value));
            List<string> foundingHistoryEntries = new List<string>
            {
                $"{data.PlayerFaction.UnitTemplates.First().Value.Name} officially forms with its first 1,000 battle brothers."
            };
            chapter.AddToBattleHistory(date, "Chapter Founding", foundingHistoryEntries);
            return chapter;
        }

        private static PlayerForce BuildChapterStructure(GameRulesData data, Date trainingEndDate, List<PlayerSoldier> soldiers)
        {
            Dictionary<int, PlayerSoldier> unassignedSoldierMap = soldiers.ToDictionary(s => s.Id);
            PlayerForce chapter = BuildChapterFromUnitTemplate(data.PlayerFaction,
                                                                           data.PlayerFaction.UnitTemplates.Values.First(ut => ut.IsTopLevelUnit),
                                                                           soldiers);
            PopulateOrderOfBattle(trainingEndDate.ToString(), unassignedSoldierMap, chapter.Army.OrderOfBattle, data.PlayerFaction);
            chapter.Army.PopulateSquadMap();
            return chapter;
        }

        private static List<PlayerSoldier> GenerateInitialSoldiers(GameRulesData data, ISoldierTrainingService trainingService, Date trainingStartDate, Date date, Date trainingEndDate)
        {
            SoldierTemplate soldierTemplate = data.PlayerFaction.SoldierTemplates[0];
            List<PlayerSoldier> soldiers =
                SoldierFactory.Instance.GenerateNewSoldiers(
                    1000,
                    soldierTemplate.Species,
                    data.SkillTemplateList)
                .Select(s => new PlayerSoldier(s, $"{TempNameGenerator.GetName()} {TempNameGenerator.GetName()}"))
                .ToList();

            foreach (PlayerSoldier soldier in soldiers)
            {
                soldier.AddEntryToHistory(trainingStartDate + ": accepted into training");
                if (soldier.PsychicPower > 0)
                {
                    soldier.AddEntryToHistory(trainingStartDate + ": psychic ability detected, acolyte training initiated");
                    // add psychic specific training here
                }
                trainingService.EvaluateSoldier(soldier, trainingEndDate);
                soldier.ProgenoidImplantDate = new Date(date.Millenium, date.Year - 2, RNG.GetIntBelowMax(1, 53));
            }
            //string csv = GetSoldierRatingCsv(soldiers);
            return soldiers;
        }

        private static string GetSoldierRatingCsv(List<PlayerSoldier> soldiers)
        {
            string csv = "";
            foreach (PlayerSoldier soldier in soldiers)
            {
                SoldierEvaluation eval = soldier.SoldierEvaluationHistory[soldier.SoldierEvaluationHistory.Count - 1];
                csv += $"{soldier.Id},{eval.LeadershipRating},{eval.MeleeRating},{eval.RangedRating},{eval.TechRating},{eval.MedicalRating},{eval.AncientRating},{eval.PietyRating}\n";
            }
            return csv;
        }

        private static void PopulateOrderOfBattle(string year, 
                                                  Dictionary<int, PlayerSoldier> unassignedSoldierMap, 
                                                  Unit oob, Faction faction)
        {
            // first, assign the Librarians
            AssignLibrarians(unassignedSoldierMap, oob, year, faction);
            // then, assign up to the top 50 as Techmarines
            AssignTechMarines(unassignedSoldierMap, oob, year, faction);
            // then, assign the top leader as Chapter Master
            AssignChapterMaster(unassignedSoldierMap, oob, year, faction);
            // then, assign Captains
            AssignCaptains(unassignedSoldierMap, oob, year, faction);
            // then, assigned twenty apothecaries
            AssignApothecaries(unassignedSoldierMap, oob, year, faction);
            // then, assign twenty Chaplains
            AssignChaplains(unassignedSoldierMap, oob, year, faction);
            // any dual gold awards are assigned to the first company
            AssignVeterans(unassignedSoldierMap, oob, year, faction);

            // assign Champtions to the CM and each Company
            List<PlayerSoldier> champions = unassignedSoldierMap.Values
                                                .OrderByDescending(s => s.SoldierEvaluationHistory[0].MeleeRating)
                                                .ToList();
            SoldierTemplate championType = faction.SoldierTemplates
                                              .Values
                                              .First(st => st.Name == "Champion");
            AssignSpecialistsToUnit(unassignedSoldierMap, oob, year, championType, champions);

            // assign Ancients to the CM and each Company
            List<PlayerSoldier> ancients = unassignedSoldierMap.Values
                                               .OrderByDescending(s => s.SoldierEvaluationHistory[0].AncientRating)
                                               .ToList();
            SoldierTemplate ancientType = faction.SoldierTemplates
                                             .Values
                                             .First(st => st.Name == "Ancient");
            AssignSpecialistsToUnit(unassignedSoldierMap, oob, year, ancientType, ancients);

            // assign all other soldiers who got at least bronze in one skill, starting with the second company
            AssignMarines(unassignedSoldierMap, oob, year, faction);
            //Assign excess to scouts
            AssignExcessToScouts(unassignedSoldierMap, oob, year, faction);
        }

        private static PlayerForce BuildChapterFromUnitTemplate(Faction faction, UnitTemplate rootTemplate, IEnumerable<PlayerSoldier> soldiers)
        {
            Unit unit = rootTemplate.GenerateUnitFromTemplateWithoutChildren("Heart of the Emperor");
            Army army = new Army("Heart of the Emperor Ground Forces", null, null, unit, soldiers);
            Fleet fleet = new Fleet("Heart of the Emperor Fleet", null, null);
            PlayerForce chapter = new PlayerForce(faction, army, fleet);
            BuildUnitTreeHelper(chapter.Army.OrderOfBattle, rootTemplate);
            return chapter;
        }

        private static void BuildUnitTreeHelper(Unit rootUnit, UnitTemplate rootTemplate)
        {
            string[] companyStrings = { "First", "Second", "Third", "Fourth", "Fifth", "Sixth", "Seventh", "Eighth", "Ninth", "Tenth" };
            int stringIndex = 0;

            foreach (UnitTemplate child in rootTemplate.GetChildUnits())
            {
                string name;
                if (child.Name.Contains("Company"))
                {
                    name = companyStrings[stringIndex] + " Company";
                    stringIndex++;
                }
                else
                {
                    name = child.Name;
                }
                Unit newUnit = child.GenerateUnitFromTemplateWithoutChildren(name);
                rootUnit.ChildUnits.Add(newUnit);
                newUnit.ParentUnit = rootUnit;
                BuildUnitTreeHelper(newUnit, child);
            }
        }

        private static void AssignChapterMaster(Dictionary<int, PlayerSoldier> unassignedSoldierMap, 
                                                Unit chapter, string year, Faction faction)
        {
            PlayerSoldier master = unassignedSoldierMap.Values.OrderByDescending(s => s.SoldierEvaluationHistory[0].LeadershipRating).First();
            master.Template = faction.SoldierTemplates.Values.First(st => st.Name == "Chapter Master");
            chapter.HQSquad.AddSquadMember(master);
            master.AddEntryToHistory(year + ": voted by the chapter to become the first Chapter Master");
            unassignedSoldierMap.Remove(master.Id);
        }

        private static void AssignLibrarians(Dictionary<int, PlayerSoldier> unassignedSoldierMap, 
                                             Unit chapter, string year, Faction faction)
        {
            // assume for now that there's a single unit to hold all of the Librarians as a squad on the chapter
            Squad library = chapter.Squads.First(s => s.Name == "Librarius");
            IOrderedEnumerable<PlayerSoldier> psychers = 
                unassignedSoldierMap.Values.Where(s => s.PsychicPower > 0).OrderByDescending(s => s.Ego);
            // TODO: add 24 points
            foreach (PlayerSoldier soldier in psychers)
            {
                if (soldier.Ego >= 18)
                {
                    if (library.SquadLeader == null)
                    {
                        soldier.Template = faction.SoldierTemplates.Values.First(st => st.Name == "Master of the Librarium");
                    }
                    else
                    {
                        soldier.Template = faction.SoldierTemplates.Values.First(st => st.Name == "Codiciers");
                    }
                }
                else
                {
                    soldier.Template = faction.SoldierTemplates.Values.First(st => st.Name == "Lexicanium");
                }
                library.AddSquadMember(soldier);
                soldier.AddEntryToHistory(year + ": Promoted to " + soldier.Template.Name + " and assigned to " + soldier.AssignedSquad.Name);
                unassignedSoldierMap.Remove(soldier.Id);
            }
        }

        private static void AssignTechMarines(Dictionary<int, PlayerSoldier> unassignedSoldierMap, 
                                              Unit chapter, string year, Faction faction)
        {
            IEnumerable<PlayerSoldier> techMarines = 
                unassignedSoldierMap.Values.Where(s => s.SoldierEvaluationHistory[0].TechRating > 75).OrderByDescending(s => s.SoldierEvaluationHistory[0].TechRating).Take(50);
            // assume for now that there's a single unit to hold all of the Techmarines
            Squad armory = chapter.Squads.First(s => s.Name == "Armory");
            foreach (PlayerSoldier soldier in techMarines)
            {
                if (armory.SquadLeader == null && soldier.SoldierEvaluationHistory[0].TechRating > 100 && soldier.SoldierEvaluationHistory[0].LeadershipRating > 60)
                {
                    soldier.Template = faction.SoldierTemplates.Values.First(st => st.Name == "Master of the Forge");
                }
                else
                {
                    soldier.Template = faction.SoldierTemplates.Values.First(st => st.Name == "Techmarine");
                }
                armory.AddSquadMember(soldier);
                soldier.AddEntryToHistory(year + ": Returned from Mars, promoted to " 
                    + soldier.Template.Name + " and assigned to " + soldier.AssignedSquad.Name);
                unassignedSoldierMap.Remove(soldier.Id);
            }
        }

        private static void AssignApothecaries(Dictionary<int, PlayerSoldier> unassignedSoldierMap, 
                                               Unit chapter, string year, Faction faction)
        {
            IEnumerable<PlayerSoldier> apothecaries = unassignedSoldierMap.Values
                                                   .Where(s => s.SoldierEvaluationHistory[0].MedicalRating > 115)
                                                   .OrderByDescending(s => s.SoldierEvaluationHistory[0].MedicalRating)
                                                   .Take(20);
            // assume for now that there's a single unit to hold all of the Techmarines
            Squad apo = chapter.Squads.First(s => s.Name == "Apothecarion");
            foreach (PlayerSoldier soldier in apothecaries)
            {
                if (apo.SquadLeader == null && soldier.SoldierEvaluationHistory[0].MedicalRating > 135 && soldier.SoldierEvaluationHistory[0].LeadershipRating > 60)
                {
                    soldier.Template = faction.SoldierTemplates.Values.First(st => st.Name == "Master of the Apothecarion");
                }
                else
                {
                    soldier.Template = faction.SoldierTemplates.Values.First(st => st.Name == "Apothecary");
                }
                apo.AddSquadMember(soldier);
                soldier.AddEntryToHistory(year + ": finished medical and genetic training, promoted to " 
                    + soldier.Template.Name + " and assigned to " + soldier.AssignedSquad.Name);
                unassignedSoldierMap.Remove(soldier.Id);
            }
        }

        private static void AssignChaplains(Dictionary<int, PlayerSoldier> unassignedSoldierMap, 
                                            Unit chapter, string year, Faction faction)
        {
            IEnumerable<PlayerSoldier> chaplains = unassignedSoldierMap.Values.Where(s => s.SoldierEvaluationHistory[0].PietyRating > 50)
                                                    .OrderByDescending(s => s.SoldierEvaluationHistory[0].PietyRating)
                                                    .Take(20);
            // assume for now that there's a single unit to hold all of the Techmarines
            Squad reclusium = chapter.Squads.First(s => s.Name == "Reclusium");
            foreach (PlayerSoldier soldier in chaplains)
            {
                if (reclusium.SquadLeader == null && soldier.SoldierEvaluationHistory[0].PietyRating > 65 && soldier.SoldierEvaluationHistory[0].LeadershipRating > 60)
                {
                    soldier.Template = faction.SoldierTemplates.Values.First(st => st.Name == "Master of Sanctity");
                }
                else
                {
                    soldier.Template = faction.SoldierTemplates.Values.First(st => st.Name == "Chaplain");
                }
                reclusium.AddSquadMember(soldier);
                soldier.AddEntryToHistory(year + ": promoted to " + soldier.Template.Name 
                    + " and assigned to " + soldier.AssignedSquad.Name);
                unassignedSoldierMap.Remove(soldier.Id);
            }
        }

        private static void AssignCaptains(Dictionary<int, PlayerSoldier> unassignedSoldierMap, 
                                           Unit chapter, string year, Faction faction)
        {
            // see if there is an impressive enough leader to be the Veteran Captain
            List<PlayerSoldier> veteranLeaders = 
                unassignedSoldierMap.Values
                                    .Where(s => s.SoldierEvaluationHistory[0].LeadershipRating > 75 && s.SoldierEvaluationHistory[0].MeleeRating > 105 && s.SoldierEvaluationHistory[0].RangedRating > 110)
                                    .OrderByDescending(s => s.SoldierEvaluationHistory[0].LeadershipRating)
                                    .ToList();
            if (veteranLeaders.Count > 0)
            {
                Unit firstCompany = chapter.ChildUnits.First(u => u.Name == "First Company");
                AssignSoldier(unassignedSoldierMap, veteranLeaders, firstCompany.HQSquad,
                    faction.SoldierTemplates.Values.First(st => st.Name == "Captain"), year);
            }
            List<PlayerSoldier> leaders = unassignedSoldierMap.Values.OrderByDescending(s => s.SoldierEvaluationHistory[0].LeadershipRating).Take(20).ToList();
            // assign Recruitment Captain next
            // assuming Tenth Company for now
            Unit tenthCompany = chapter.ChildUnits.First(u => u.Name == "Tenth Company");
            AssignSoldier(unassignedSoldierMap, leaders, tenthCompany.HQSquad,
                faction.SoldierTemplates.Values.First(st => st.Name == "Captain"), year);

            foreach (Unit company in chapter.ChildUnits)
            {
                if (company.HQSquad.SquadLeader == null && company.Name != "First Company" )
                {
                    // is a true company, needs a captain
                    AssignSoldier(unassignedSoldierMap, leaders, company.HQSquad,
                        faction.SoldierTemplates.Values.First(st => st.Name == "Captain"), year);
                }
            }
        }
    
        private static void AssignVeterans(Dictionary<int, PlayerSoldier> unassignedSoldierMap, 
                                           Unit chapter, string year, Faction faction)
        {
            IEnumerable<PlayerSoldier> veterans = unassignedSoldierMap.Values.Where(s => s.SoldierEvaluationHistory[0].MeleeRating > 95 && s.SoldierEvaluationHistory[0].RangedRating > 105);
            List<PlayerSoldier> veteranLeaders = veterans.Where(s => s.SoldierEvaluationHistory[0].LeadershipRating > 60).OrderByDescending(s => s.SoldierEvaluationHistory[0].LeadershipRating).ToList();
            // if there are no veteran sgts, leave First Company empty for now
            if (veteranLeaders.Count == 0) return;
            List<PlayerSoldier> vetList = veterans.Except(veteranLeaders).OrderByDescending(s => s.SoldierEvaluationHistory[0].MeleeRating).ToList();
            int squadSize = (vetList.Count / veteranLeaders.Count) + 1;
            if(squadSize < 5)
            {
                // set squad size to five, and we'll use the other veteran sgts elsewhere
                squadSize = 5;
            }
            if(squadSize > 10)
            {
                // set squad size to ten, and we'll use the other veterans elsewhere
                squadSize = 10;
            }

            // look for Veteran Squads
            foreach(Unit company in chapter.ChildUnits)
            {
                foreach (Squad squad in company.Squads)
                {
                    if ((squad.SquadTemplate.SquadType & SquadTypes.Elite) > 0)
                    {
                        if (vetList.Count == 0 || veteranLeaders.Count == 0)
                        {
                            break;
                        }
                        // assign sgt to squad
                        squad.Name = veteranLeaders[0].Name.Split(' ')[1] + " Squad";
                        AssignSoldier(unassignedSoldierMap, veteranLeaders, squad,
                            faction.SoldierTemplates.Values.First(st => st.Name == "Sergeant"), year);

                        while (squad.Members.Count < squadSize && vetList.Count > 0)
                        {
                            AssignSoldier(unassignedSoldierMap, vetList, squad,
                                faction.SoldierTemplates.Values.First(st => st.Name == "Veteran"), year);
                        }
                    }
                }
            }
        }

        private static void AssignExcessToScouts(Dictionary<int, PlayerSoldier> unassignedSoldierMap, 
                                                 Unit chapter, string year, Faction faction)
        {
            int sgtNeed = ((unassignedSoldierMap.Count - 1) / 10) + 1;
            List<PlayerSoldier> leaderList = unassignedSoldierMap.Values.OrderByDescending(s => s.SoldierEvaluationHistory[0].LeadershipRating).Take(sgtNeed).ToList();
            List<PlayerSoldier> scoutList = unassignedSoldierMap.Values.Except(leaderList).ToList();
            Unit lastCompany = null;
            Squad lastSquad = null;
            SoldierTemplate scoutSgt = 
                faction.SoldierTemplates.Values.First(st => st.Name == "Scout Sergeant");
            SoldierTemplate scout =
                faction.SoldierTemplates.Values.First(st => st.Name == "Scout Marine");
            foreach (Unit company in chapter.ChildUnits)
            {
                lastCompany = company;
                foreach (Squad squad in company.Squads)
                {
                    if ((squad.SquadTemplate.SquadType & SquadTypes.Scout) > 0)
                    {
                        lastSquad = squad;
                        // assign sgt to squad
                        squad.Name = leaderList[0].Name.Split(' ')[1] + " Squad";
                        AssignSoldier(unassignedSoldierMap, leaderList, squad, scoutSgt, year);

                        while (squad.Members.Count < 10 && scoutList.Count > 0)
                        {
                            AssignSoldier(unassignedSoldierMap, scoutList, squad, scout, year);
                        }
                    }
                }
            }
            while (scoutList.Count > 0 || leaderList.Count > 0)
            {
                if (leaderList.Count > 0)
                {
                    int id = lastSquad.Id + 1;
                    // add a new Scout Squad to the company
                    Squad squad = new Squad("Scout Squad", lastCompany,
                        faction.SquadTemplates.Values.First(st => st.Name == "Scout Squad"));
                    lastCompany.AddSquad(squad);
                    id++;
                    lastSquad = squad;
                    // assign sgt to squad
                    squad.Name = leaderList[0].Name.Split(' ')[1] + " Squad";
                    AssignSoldier(unassignedSoldierMap, leaderList, squad, scoutSgt, year);
                }
                while (scoutList.Count > 0 && 
                      (lastSquad.Members.Count < 10 || leaderList.Count <= 0))
                {
                    AssignSoldier(unassignedSoldierMap, scoutList, lastSquad, scout, year);
                }
            }
            if (unassignedSoldierMap.Count > 0) Debug.WriteLine("Still did it wrong");
        }

        private static void AssignSpecialistsToUnit(Dictionary<int, PlayerSoldier> unassignedSoldierMap,
                                             Unit chapter,
                                             string year,
                                             SoldierTemplate specialistType,
                                             List<PlayerSoldier> sortedCandidates)
        {
            AssignSpecialistsToSquad(unassignedSoldierMap, chapter.HQSquad, year, 
                                     specialistType, sortedCandidates);
            foreach (Squad squad in chapter.Squads)
            {
                AssignSpecialistsToSquad(unassignedSoldierMap, squad, year, 
                                         specialistType, sortedCandidates);
            }
            foreach (Unit company in chapter.ChildUnits)
            {
                AssignSpecialistsToSquad(unassignedSoldierMap, company.HQSquad, 
                                         year, specialistType, sortedCandidates);
                foreach (Squad squad in chapter.Squads)
                {
                    AssignSpecialistsToSquad(unassignedSoldierMap, squad, year, 
                                             specialistType, sortedCandidates);
                }
            }
        }

        private static void AssignSpecialistsToSquad(Dictionary<int, PlayerSoldier> unassignedSoldierMap,
                                                     Squad squad,
                                                     string year,
                                                     SoldierTemplate specialistType,
                                                     List<PlayerSoldier> sortedCandidates)
        {
            // minor hack to avoid assigning non-veteran specialists to veteran HQ squads
            if (squad == null 
                || sortedCandidates.Count == 0 
                || (squad.SquadTemplate.SquadType & SquadTypes.Elite) > 0) return;
            IEnumerable<SquadTemplateElement> elements = squad.SquadTemplate.Elements
                .Where(e => e.SoldierTemplate == specialistType);
            foreach (SquadTemplateElement element in elements)
            {
                if (sortedCandidates.Count == 0) return;
                for (int i = 0; i < element.MinimumNumber; i++)
                {
                    if (sortedCandidates.Count == 0) return;
                    // assign top to HQ
                    AssignSoldier(unassignedSoldierMap, sortedCandidates, squad,
                        specialistType, year);
                }
            }
        }

        private static void AssignMarines(Dictionary<int, PlayerSoldier> unassignedSoldierMap, 
                                          Unit chapter, string year, Faction faction)
        {
            List<PlayerSoldier> devList = unassignedSoldierMap.Values.Where(s => s.SoldierEvaluationHistory[0].MeleeRating > 80 
                                                              && s.SoldierEvaluationHistory[0].MeleeRating < 90
                                                              && s.SoldierEvaluationHistory[0].RangedRating > 95
                                                              && s.SoldierEvaluationHistory[0].LeadershipRating < 50)
                                                     .OrderByDescending(s => s.SoldierEvaluationHistory[0].RangedRating)
                                                     .ToList();

            List<PlayerSoldier> assList = unassignedSoldierMap.Values.Where(s => s.SoldierEvaluationHistory[0].MeleeRating > 90 
                                                              && s.SoldierEvaluationHistory[0].RangedRating > 95
                                                              && s.SoldierEvaluationHistory[0].RangedRating < 105
                                                              && s.SoldierEvaluationHistory[0].LeadershipRating < 50)
                                                     .OrderByDescending(s => s.SoldierEvaluationHistory[0].MeleeRating)
                                                     .ToList();
            
            List<PlayerSoldier> tactList = unassignedSoldierMap.Values.Where(s => s.SoldierEvaluationHistory[0].MeleeRating > 90
                                                               && s.SoldierEvaluationHistory[0].RangedRating > 105 
                                                               && s.SoldierEvaluationHistory[0].LeadershipRating < 50)
                                                      .ToList();
            
            List<PlayerSoldier> devSgtList = unassignedSoldierMap.Values.Where(s => s.SoldierEvaluationHistory[0].MeleeRating > 80
                                                                 && s.SoldierEvaluationHistory[0].MeleeRating < 90
                                                                 && s.SoldierEvaluationHistory[0].RangedRating > 95
                                                                 && s.SoldierEvaluationHistory[0].LeadershipRating > 50)
                                                        .OrderByDescending(s => s.SoldierEvaluationHistory[0].LeadershipRating)
                                                        .ToList();
            
            List<PlayerSoldier> assSgtList = unassignedSoldierMap.Values.Where(s => s.SoldierEvaluationHistory[0].MeleeRating > 90
                                                                 && s.SoldierEvaluationHistory[0].RangedRating > 95
                                                                 && s.SoldierEvaluationHistory[0].RangedRating < 105
                                                                 && s.SoldierEvaluationHistory[0].LeadershipRating > 50)
                                                        .OrderByDescending(s => s.SoldierEvaluationHistory[0].LeadershipRating)
                                                        .ToList();
            
            List<PlayerSoldier> tactSgtList = unassignedSoldierMap.Values.Where(s => s.SoldierEvaluationHistory[0].MeleeRating > 90 
                                                                  && s.SoldierEvaluationHistory[0].RangedRating > 105 
                                                                  && s.SoldierEvaluationHistory[0].LeadershipRating > 50)
                                                         .OrderByDescending(s => s.SoldierEvaluationHistory[0].LeadershipRating)
                                                         .ToList();

            BalanceLists(assList, devList, tactList, assSgtList, devSgtList, tactSgtList);
            AssignTacticalMarines(unassignedSoldierMap, chapter, year, faction, tactList, tactSgtList);
            assList.AddRange(tactList);
            AssignAssaultMarines(unassignedSoldierMap, chapter, year, faction, assList, assSgtList);
            if (assList.Count > 0)
            {
                devList.AddRange(assList.Where(a => a.SoldierEvaluationHistory[0].RangedRating > 80));
                devList = devList.OrderByDescending(s => s.SoldierEvaluationHistory[0].RangedRating).ToList();
            }
            AssignDevastatorMarines(unassignedSoldierMap, chapter, year, faction, devList, devSgtList);
        }

        private static void AssignDevastatorMarines(Dictionary<int, PlayerSoldier> unassignedSoldierMap, 
                                                    Unit chapter, string year, Faction faction,
                                                    List<PlayerSoldier> devList, 
                                                    List<PlayerSoldier> devSgtList)
        {
            SoldierTemplate devType = 
                faction.SoldierTemplates.Values.First(st => st.Name == "Devastator Marine");
            SoldierTemplate devSgtType =
                faction.SoldierTemplates.Values.First(st => st.Name == "Sergeant (D)");
            // since Devastators are assigned last, make sure the dev to sgt list is reasonable
            while (devSgtList.Count() * 9 >= devList.Count())
            {
                // turn the last Sgt into a dev
                PlayerSoldier demote = devSgtList[devSgtList.Count - 1];
                devList.Add(demote);
                devSgtList.Remove(demote);
            }
            foreach (Unit company in chapter.ChildUnits)
            {
                foreach (Squad squad in company.Squads)
                {
                    /*foreach(SquadTemplateElement element in squad.SquadTemplate.Elements)
                    {
                        if(element.SoldierType == devType)
                        {

                        }
                    }*/
                    if (squad.SquadTemplate.Name == "Devastator Squad")
                    {
                        if (devSgtList.Count > 0)
                        {
                            squad.Name = devSgtList[0].Name.Split(' ')[1] + " Squad";
                            AssignSoldier(unassignedSoldierMap, devSgtList, squad,
                                devSgtType, year);
                        }
                        int devSquadSize = CalculateSquadSize(devList, devSgtList);
                        while (devList.Count > 0 && squad.Members.Count < devSquadSize)
                        {
                            AssignSoldier(unassignedSoldierMap, devList, squad, devType, year);
                        }
                    }
                }
            }
        }

        private static void AssignAssaultMarines(Dictionary<int, PlayerSoldier> unassignedSoldierMap, 
                                                 Unit chapter, string year, Faction faction,
                                                 List<PlayerSoldier> assList, 
                                                 List<PlayerSoldier> assSgtList)
        {
            SoldierTemplate assType = 
                faction.SoldierTemplates.Values.First(st => st.Name == "Assault Marine");
            SoldierTemplate assSgtType =
                faction.SoldierTemplates.Values.First(st => st.Name == "Sergeant (A)");
            foreach (Unit company in chapter.ChildUnits)
            {
                foreach (Squad squad in company.Squads)
                {
                    if (squad.SquadTemplate.Name == "Assault Squad")
                    {
                        if (assSgtList.Count > 0)
                        {
                            squad.Name = assSgtList[0].Name.Split(' ')[1] + " Squad";
                            AssignSoldier(unassignedSoldierMap, assSgtList, squad,
                                assSgtType, year);
                        }
                        int assSquadSize = CalculateSquadSize(assList, assSgtList);
                        while (assList.Count > 0 && squad.Members.Count < assSquadSize)
                        {
                            AssignSoldier(unassignedSoldierMap, assList, squad, assType, year);
                        }
                    }
                }
            }
        }

        private static void AssignTacticalMarines(Dictionary<int, PlayerSoldier> unassignedSoldierMap, 
                                                  Unit chapter, string year, Faction faction,
                                                  List<PlayerSoldier> tactList, 
                                                  List<PlayerSoldier> tactSgtList)
        {
            SoldierTemplate tactType =
                faction.SoldierTemplates.Values.First(st => st.Name == "Tactical Marine");
            SoldierTemplate tactSgtType =
                faction.SoldierTemplates.Values.First(st => st.Name == "Sergeant");
            foreach (Unit company in chapter.ChildUnits)
            {
                foreach (Squad squad in company.Squads)
                {
                    if (squad.SquadTemplate.Name == "Tactical Squad")
                    {
                        if (tactSgtList.Count > 0)
                        {
                            squad.Name = tactSgtList[0].Name.Split(' ')[1] + " Squad";
                            AssignSoldier(unassignedSoldierMap, tactSgtList, squad,
                                tactSgtType, year);
                        }
                        int tactSquadSize = CalculateSquadSize(tactList, tactSgtList);
                        while (tactList.Count > 0 && squad.Members.Count < tactSquadSize)
                        {
                            AssignSoldier(unassignedSoldierMap, tactList, squad, tactType, year);
                        }
                    }
                }
            }
        }

        private static int CalculateSquadSize(List<PlayerSoldier> soldierList, List<PlayerSoldier> sgtList)
        {
            if ((soldierList.Count - 9) >= (sgtList.Count * 4))
            {
                return 10;
            }
            else if (sgtList.Count == 0)
            {
                return soldierList.Count;
            }
            else
            {
                return soldierList.Count - (sgtList.Count * 4);
            }
        }

        private static void AssignSoldier(Dictionary<int, PlayerSoldier> unassignedSoldierMap, 
                                                   List<PlayerSoldier> soldierList, 
                                                   Squad squad, 
                                                   SoldierTemplate type, 
                                                   string year)
        {
            PlayerSoldier soldier = soldierList[0];
            soldier.Template = type;
            squad.AddSquadMember(soldier);
            soldier.AddEntryToHistory(year + ": promoted to " + soldier.Template.Name 
                + " and assigned to " + soldier.AssignedSquad.Name);
            unassignedSoldierMap.Remove(soldier.Id);
            soldierList.RemoveAt(0);
        }

        private static void BalanceLists(List<PlayerSoldier> assList, List<PlayerSoldier> devList, List<PlayerSoldier> tactList, 
            List<PlayerSoldier> assSgtList, List<PlayerSoldier> devSgtList, List<PlayerSoldier> tactSgtList)
        {
            // we want 18 squads of 9 devs each, 18 squads of 9 ass each, and 52 squads of 9 tact each, so 162, 162, and 468
            while(assSgtList.Count > 18)
            {
                PlayerSoldier soldier = assSgtList[assSgtList.Count - 1];
                assList.Add(soldier);
                assSgtList.RemoveAt(assSgtList.Count - 1);
            }
            while(devSgtList.Count > 18)
            {
                PlayerSoldier soldier = devSgtList[devSgtList.Count - 1];
                devList.Add(soldier);
                devSgtList.RemoveAt(devSgtList.Count - 1);
            }
            if(assSgtList.Count == 18 && devSgtList.Count == 18)
            {
                while(tactSgtList.Count > 52)
                {
                    PlayerSoldier soldier = tactSgtList[tactSgtList.Count - 1];
                    tactList.Add(soldier);
                    tactSgtList.RemoveAt(tactSgtList.Count - 1);
                }
            }
            int minAssSgtNeeded = (assList.Count - 1)/9 + 1;
            int minDevSgtNeeded = (devList.Count - 1)/9 + 1;
            int minTactSgtNeeded = (tactList.Count - 1)/9 + 1;

            if (minDevSgtNeeded > 18) minDevSgtNeeded = 18;
            if (minAssSgtNeeded > 18) minAssSgtNeeded = 18;
            if (minTactSgtNeeded > 52) minTactSgtNeeded = 52;


            int spareTactSgt = tactSgtList.Count - minTactSgtNeeded;
            int spareAssSgt = assSgtList.Count - minAssSgtNeeded;
            int spareDevSgt = devSgtList.Count - minDevSgtNeeded;
            while (spareAssSgt > 0 && spareDevSgt < 0)
            {
                // shift an Ass Sgt to be a Dev Sgt
                devSgtList.Add(assSgtList[assSgtList.Count - 1]);
                spareDevSgt++;
                assSgtList.RemoveAt(assSgtList.Count - 1);
                spareAssSgt--;
            }
            while (spareTactSgt > 0 && spareDevSgt < 0)
            {
                // shift a Tact Sgt to be a Dev Sgt
                devSgtList.Add(tactSgtList[tactSgtList.Count - 1]);
                spareDevSgt++;
                tactSgtList.RemoveAt(tactSgtList.Count - 1);
                spareTactSgt--;
            }
            while (spareTactSgt > 0 && spareAssSgt < 0)
            {
                // shift a Tact Sgt to be an Ass Sgt
                assSgtList.Add(tactSgtList[tactSgtList.Count - 1]);
                spareAssSgt++;
                tactSgtList.RemoveAt(tactSgtList.Count - 1);
                spareTactSgt--;
            }
            if (spareTactSgt < 0 && (spareAssSgt > 0 || spareDevSgt > 0))
            {
                // shift tact soldiers to be other soldiers
                while (spareTactSgt < 0 && spareAssSgt > 0)
                {
                    // move nine tactMarines to be assMarines
                    for (int i = 0; i < 9; i++)
                    {
                        assList.Add(tactList[0]);
                        tactList.RemoveAt(0);
                    }
                    spareTactSgt++;
                    spareAssSgt--;
                }
                while (spareTactSgt < 0 && spareDevSgt > 0)
                {
                    // move nine tactMarines to be devMarines
                    for (int i = 0; i < 9; i++)
                    {
                        devList.Add(tactList[0]);
                        tactList.RemoveAt(0);
                    }
                    spareTactSgt++;
                    spareDevSgt--;
                }
            }
            int tactSgtNeeded = ((tactSgtList.Count + tactList.Count - 1) / 10) + 1;
            while(tactSgtList.Count > tactSgtNeeded)
            {
                tactList.Add(tactSgtList[tactSgtList.Count - 1]);
                tactSgtList.RemoveAt(tactSgtList.Count - 1);
            }
            int assSgtNeeded = ((assSgtList.Count + assList.Count - 1) / 10) + 1;
            while (assSgtList.Count > assSgtNeeded)
            {
                assList.Add(assSgtList[assSgtList.Count - 1]);
                assSgtList.RemoveAt(assSgtList.Count - 1);
            }
            int devSgtNeeded = ((devSgtList.Count + devList.Count - 1) / 10) + 1;
            while (devSgtList.Count > devSgtNeeded)
            {
                devList.Add(devSgtList[devSgtList.Count - 1]);
                devSgtList.RemoveAt(devSgtList.Count - 1);
            }
        }

        private static void ApplySoldierTypeTraining(PlayerSoldier soldier)
        {
            foreach(Tuple<BaseSkill, float> tuple in soldier.Template.MosTraining)
            {
                soldier.AddSkillPoints(tuple.Item1, tuple.Item2);
            }
        }
    }
}
