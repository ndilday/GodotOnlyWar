
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
        // Starting Requisition pool seeded at chapter founding (PRD 4.23 / Supply &
        // Requisition Phase 1). Deliberately generous: the loop is easier to verify
        // end-to-end before scarcity is tuned in (PRD tuning note).
        private const int FOUNDING_REQUISITION = 1000;
        private delegate void TrainingFunction(PlayerSoldier playerSoldier);

        internal static PlayerForce CreateChapter(GameRulesData data,
                                                  ISoldierTrainingService trainingService,
                                                  Date trainingStartDate,
                                                  Date date,
                                                  string chapterName = null)
        {
            chapterName = string.IsNullOrWhiteSpace(chapterName) ? "Heart of the Emperor" : chapterName.Trim();
            Date trainingEndDate =
                new Date(trainingStartDate.GetTotalWeeks() + INITIAL_TRAINING_DURATION_WEEKS);
            List<PlayerSoldier> soldiers = GenerateInitialSoldiers(data, trainingService, trainingStartDate, date, trainingEndDate);

            PlayerForce chapter = BuildChapterStructure(data, trainingEndDate, soldiers, chapterName);
            chapter.Army.Requisition = FOUNDING_REQUISITION;
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
                $"The {chapterName} officially forms with its first 1,000 battle brothers."
            };
            chapter.AddToBattleHistory(date, "Chapter Founding", foundingHistoryEntries);
            return chapter;
        }

        private static PlayerForce BuildChapterStructure(GameRulesData data, Date trainingEndDate, List<PlayerSoldier> soldiers, string chapterName)
        {
            Dictionary<int, PlayerSoldier> unassignedSoldierMap = soldiers.ToDictionary(s => s.Id);
            PlayerForce chapter = BuildChapterFromUnitTemplate(data.PlayerFaction,
                                                                           data.PlayerFaction.UnitTemplates.Values.First(ut => ut.IsTopLevelUnit),
                                                                           soldiers,
                                                                           chapterName);
            PopulateOrderOfBattle(trainingEndDate, unassignedSoldierMap, chapter.Army.OrderOfBattle, data.ChapterTemplates);
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
                .Select(s => new PlayerSoldier(s, $"{NameGenerator.GetName()} {NameGenerator.GetName()}"))
                .ToList();

            foreach (PlayerSoldier soldier in soldiers)
            {
                soldier.AddEvent(new SoldierEvent(trainingStartDate, SoldierEventType.AcceptedToTraining,
                    "accepted into training"));
                if (soldier.PsychicPower > 0)
                {
                    soldier.AddEvent(new SoldierEvent(trainingStartDate, SoldierEventType.PsychicDetected,
                        "psychic ability detected, acolyte training initiated"));
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

        private static void PopulateOrderOfBattle(Date year,
                                                  Dictionary<int, PlayerSoldier> unassignedSoldierMap,
                                                  Unit oob, ChapterGenerationTemplates templates)
        {
            // first, assign the Librarians
            AssignLibrarians(unassignedSoldierMap, oob, year, templates);
            // then, assign up to the top 50 as Techmarines
            AssignTechMarines(unassignedSoldierMap, oob, year, templates);
            // then, assign the top leader as Chapter Master
            AssignChapterMaster(unassignedSoldierMap, oob, year, templates);
            // then, assign Captains
            AssignCaptains(unassignedSoldierMap, oob, year, templates);
            // then, assigned twenty apothecaries
            AssignApothecaries(unassignedSoldierMap, oob, year, templates);
            // then, assign twenty Chaplains
            AssignChaplains(unassignedSoldierMap, oob, year, templates);
            // tactical-baseline marines with an Adamantium-level combat spike are assigned to the first company
            AssignVeterans(unassignedSoldierMap, oob, year, templates);

            // assign Champtions to the CM and each Company
            List<PlayerSoldier> champions = unassignedSoldierMap.Values
                                                .OrderByDescending(s => s.SoldierEvaluationHistory[0].MeleeRating)
                                                .ToList();
            SoldierTemplate championType = templates.Champion;
            AssignSpecialistsToUnit(unassignedSoldierMap, oob, year, championType, champions);

            // assign Ancients to the CM and each Company
            List<PlayerSoldier> ancients = unassignedSoldierMap.Values
                                               .OrderByDescending(s => s.SoldierEvaluationHistory[0].AncientRating)
                                               .ToList();
            SoldierTemplate ancientType = templates.Ancient;
            AssignSpecialistsToUnit(unassignedSoldierMap, oob, year, ancientType, ancients);

            // assign all other soldiers who got at least bronze in one skill, starting with the second company
            AssignMarines(unassignedSoldierMap, oob, year, templates);
            //Assign excess to scouts
            AssignExcessToScouts(unassignedSoldierMap, oob, year, templates);
        }

        private static PlayerForce BuildChapterFromUnitTemplate(Faction faction, UnitTemplate rootTemplate, IEnumerable<PlayerSoldier> soldiers, string chapterName)
        {
            Unit unit = rootTemplate.GenerateUnitFromTemplateWithoutChildren(chapterName);
            Army army = new Army($"{chapterName} Ground Forces", null, null, unit, soldiers);
            Fleet fleet = new Fleet($"{chapterName} Fleet", null, null);
            PlayerForce chapter = new PlayerForce(faction, army, fleet);
            BuildUnitTreeHelper(chapter.Army.OrderOfBattle, rootTemplate);
            // Register the army's root unit on the faction so it matches the post-load model:
            // the save path enumerates units via Faction.Units, so a freshly generated chapter
            // must be registered here or its soldiers are never written (FK failure on save).
            if (!faction.Units.Contains(unit))
            {
                faction.Units.Add(unit);
            }
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
                                                Unit chapter, Date year, ChapterGenerationTemplates templates)
        {
            PlayerSoldier master = unassignedSoldierMap.Values.OrderByDescending(s => s.SoldierEvaluationHistory[0].LeadershipRating).First();
            master.Template = templates.ChapterMaster;
            chapter.HQSquad.AddSquadMember(master);
            master.AddEvent(new SoldierEvent(year, SoldierEventType.Founding,
                "voted by the chapter to become the first Chapter Master"));
            unassignedSoldierMap.Remove(master.Id);
        }

        private static void AssignLibrarians(Dictionary<int, PlayerSoldier> unassignedSoldierMap, 
                                             Unit chapter, Date year, ChapterGenerationTemplates templates)
        {
            // assume for now that there's a single unit to hold all of the Librarians as a squad on the chapter
            Squad library = chapter.Squads.First(s => s.SquadTemplate == templates.Librarius);
            IOrderedEnumerable<PlayerSoldier> psychers = 
                unassignedSoldierMap.Values.Where(s => s.PsychicPower > 0).OrderByDescending(s => s.Ego);
            // TODO: add 24 points
            foreach (PlayerSoldier soldier in psychers)
            {
                if (soldier.Ego >= 18)
                {
                    if (library.SquadLeader == null)
                    {
                        soldier.Template = templates.MasterOfTheLibrarium;
                    }
                    else
                    {
                        soldier.Template = templates.Codicier;
                    }
                }
                else
                {
                    soldier.Template = templates.Lexicanium;
                }
                library.AddSquadMember(soldier);
                soldier.AddEvent(new SoldierEvent(year, SoldierEventType.Promotion,
                    "Promoted to " + soldier.Template.Name + " and assigned to " + soldier.AssignedSquad.Name));
                unassignedSoldierMap.Remove(soldier.Id);
            }
        }

        private static void AssignTechMarines(Dictionary<int, PlayerSoldier> unassignedSoldierMap, 
                                              Unit chapter, Date year, ChapterGenerationTemplates templates)
        {
            IEnumerable<PlayerSoldier> techMarines = 
                unassignedSoldierMap.Values.Where(s => s.SoldierEvaluationHistory[0].TechRating > 75).OrderByDescending(s => s.SoldierEvaluationHistory[0].TechRating).Take(50);
            // assume for now that there's a single unit to hold all of the Techmarines
            Squad armory = chapter.Squads.First(s => s.SquadTemplate == templates.Armory);
            foreach (PlayerSoldier soldier in techMarines)
            {
                if (armory.SquadLeader == null && soldier.SoldierEvaluationHistory[0].TechRating > 100 && soldier.SoldierEvaluationHistory[0].LeadershipRating > 60)
                {
                    soldier.Template = templates.MasterOfTheForge;
                }
                else
                {
                    soldier.Template = templates.Techmarine;
                }
                armory.AddSquadMember(soldier);
                soldier.AddEvent(new SoldierEvent(year, SoldierEventType.Promotion,
                    "Returned from Mars, promoted to "
                    + soldier.Template.Name + " and assigned to " + soldier.AssignedSquad.Name));
                unassignedSoldierMap.Remove(soldier.Id);
            }
        }

        private static void AssignApothecaries(Dictionary<int, PlayerSoldier> unassignedSoldierMap, 
                                               Unit chapter, Date year, ChapterGenerationTemplates templates)
        {
            IEnumerable<PlayerSoldier> apothecaries = unassignedSoldierMap.Values
                                                   .Where(s => s.SoldierEvaluationHistory[0].MedicalRating > 115)
                                                   .OrderByDescending(s => s.SoldierEvaluationHistory[0].MedicalRating)
                                                   .Take(20);
            // assume for now that there's a single unit to hold all of the Techmarines
            Squad apo = chapter.Squads.First(s => s.SquadTemplate == templates.Apothecarion);
            foreach (PlayerSoldier soldier in apothecaries)
            {
                if (apo.SquadLeader == null && soldier.SoldierEvaluationHistory[0].MedicalRating > 135 && soldier.SoldierEvaluationHistory[0].LeadershipRating > 60)
                {
                    soldier.Template = templates.MasterOfTheApothecarion;
                }
                else
                {
                    soldier.Template = templates.Apothecary;
                }
                apo.AddSquadMember(soldier);
                soldier.AddEvent(new SoldierEvent(year, SoldierEventType.Promotion,
                    "finished medical and genetic training, promoted to "
                    + soldier.Template.Name + " and assigned to " + soldier.AssignedSquad.Name));
                unassignedSoldierMap.Remove(soldier.Id);
            }
        }

        private static void AssignChaplains(Dictionary<int, PlayerSoldier> unassignedSoldierMap, 
                                            Unit chapter, Date year, ChapterGenerationTemplates templates)
        {
            IEnumerable<PlayerSoldier> chaplains = unassignedSoldierMap.Values.Where(s => s.SoldierEvaluationHistory[0].PietyRating > 50)
                                                    .OrderByDescending(s => s.SoldierEvaluationHistory[0].PietyRating)
                                                    .Take(20);
            // assume for now that there's a single unit to hold all of the Techmarines
            Squad reclusium = chapter.Squads.First(s => s.SquadTemplate == templates.Reclusium);
            foreach (PlayerSoldier soldier in chaplains)
            {
                if (reclusium.SquadLeader == null && soldier.SoldierEvaluationHistory[0].PietyRating > 65 && soldier.SoldierEvaluationHistory[0].LeadershipRating > 60)
                {
                    soldier.Template = templates.MasterOfSanctity;
                }
                else
                {
                    soldier.Template = templates.Chaplain;
                }
                reclusium.AddSquadMember(soldier);
                soldier.AddEvent(new SoldierEvent(year, SoldierEventType.Promotion,
                    "promoted to " + soldier.Template.Name
                    + " and assigned to " + soldier.AssignedSquad.Name));
                unassignedSoldierMap.Remove(soldier.Id);
            }
        }

        private static void AssignCaptains(Dictionary<int, PlayerSoldier> unassignedSoldierMap, 
                                           Unit chapter, Date year, ChapterGenerationTemplates templates)
        {
            // see if there is an impressive enough leader to be the Veteran Captain
            List<PlayerSoldier> veteranLeaders = 
                unassignedSoldierMap.Values
                                    .Where(s => s.SoldierEvaluationHistory[0].LeadershipRating > 75 && s.SoldierEvaluationHistory[0].MeleeRating > 105 && s.SoldierEvaluationHistory[0].RangedRating > 110)
                                    .OrderByDescending(s => s.SoldierEvaluationHistory[0].LeadershipRating)
                                    .ToList();
            if (veteranLeaders.Count > 0)
            {
                Unit firstCompany = chapter.ChildUnits.First(u => u.UnitTemplate == templates.VeteranCompany);
                AssignSoldier(unassignedSoldierMap, veteranLeaders, firstCompany.HQSquad,
                    templates.Captain, year);
            }
            List<PlayerSoldier> leaders = unassignedSoldierMap.Values.OrderByDescending(s => s.SoldierEvaluationHistory[0].LeadershipRating).Take(20).ToList();
            // assign the Scout Company's Captain next (assuming Tenth Company for now)
            Unit tenthCompany = chapter.ChildUnits.First(u => u.UnitTemplate == templates.ScoutCompany);
            AssignSoldier(unassignedSoldierMap, leaders, tenthCompany.HQSquad,
                templates.Captain, year);

            foreach (Unit company in chapter.ChildUnits)
            {
                if (company.HQSquad.SquadLeader == null && company.UnitTemplate != templates.VeteranCompany)
                {
                    // is a true company, needs a captain
                    AssignSoldier(unassignedSoldierMap, leaders, company.HQSquad,
                        templates.Captain, year);
                }
            }
        }
    
        private static void AssignVeterans(Dictionary<int, PlayerSoldier> unassignedSoldierMap, 
                                           Unit chapter, Date year, ChapterGenerationTemplates templates)
        {
            IEnumerable<PlayerSoldier> veterans = unassignedSoldierMap.Values.Where(IsVeteranCandidate);
            List<PlayerSoldier> veteranLeaders = veterans.Where(IsVeteranSergeantCandidate).OrderByDescending(s => s.SoldierEvaluationHistory[0].LeadershipRating).ToList();
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

            // Create veteran (Elite) squads on demand in any company whose template
            // allows them, up to that company's cap.
            foreach (Unit company in chapter.ChildUnits)
            {
                foreach (SquadTemplateSlot slot in company.UnitTemplate.GetChildSquadSlots())
                {
                    if ((slot.Template.SquadType & SquadTypes.Elite) > 0)
                    {
                        FillCompanyWithSquads(unassignedSoldierMap, company, slot.Template,
                            vetList, veteranLeaders, templates.Veteran, templates.VeteranSergeant,
                            (_, _) => squadSize, year);
                    }
                }
            }
        }

        private static bool IsVeteranCandidate(PlayerSoldier soldier)
        {
            SoldierEvaluation evaluation = soldier.SoldierEvaluationHistory[0];
            bool tacticalBaseline = evaluation.MeleeRating > 90 && evaluation.RangedRating > 105;
            bool adamantiumCombatSpike = evaluation.MeleeRating > 115 || evaluation.RangedRating > 120;
            return tacticalBaseline && adamantiumCombatSpike;
        }

        private static bool IsVeteranSergeantCandidate(PlayerSoldier soldier)
        {
            return IsVeteranCandidate(soldier) && soldier.SoldierEvaluationHistory[0].LeadershipRating > 60;
        }

        // Capacity of a company for a given squad template, per its unit template's
        // SquadTemplateSlot. Companies no longer hold pre-created empty squads, so
        // generation creates squads up to this cap as soldiers are assigned.
        private static int GetSquadCap(Unit company, SquadTemplate template)
        {
            foreach (SquadTemplateSlot slot in company.UnitTemplate.GetChildSquadSlots())
            {
                if (slot.Template == template)
                {
                    return slot.MaxCount;
                }
            }
            return 0;
        }

        // Creates and fills squads of the given template in a company, up to the
        // company's cap, drawing leaders from sgtList and members from soldierList.
        // A squad that nothing is available to seed is discarded so no empty squads
        // are left behind.
        private static void FillCompanyWithSquads(
            Dictionary<int, PlayerSoldier> unassignedSoldierMap,
            Unit company,
            SquadTemplate squadTemplate,
            List<PlayerSoldier> soldierList,
            List<PlayerSoldier> sgtList,
            SoldierTemplate soldierType,
            SoldierTemplate sgtType,
            Func<List<PlayerSoldier>, List<PlayerSoldier>, int> squadSizeFunc,
            Date year)
        {
            int cap = GetSquadCap(company, squadTemplate);
            int created = 0;
            while (created < cap && (soldierList.Count > 0 || sgtList.Count > 0))
            {
                Squad squad = new Squad(squadTemplate.Name, company, squadTemplate);
                company.AddSquad(squad);
                if (sgtList.Count > 0)
                {
                    squad.Name = sgtList[0].Name.Split(' ')[1] + " Squad";
                    AssignSoldier(unassignedSoldierMap, sgtList, squad, sgtType, year);
                }
                int squadSize = squadSizeFunc(soldierList, sgtList);
                while (soldierList.Count > 0 && squad.Members.Count < squadSize)
                {
                    AssignSoldier(unassignedSoldierMap, soldierList, squad, soldierType, year);
                }
                if (squad.Members.Count == 0)
                {
                    // Nothing was available to seed this squad; undo and stop.
                    company.RemoveSquad(squad);
                    break;
                }
                created++;
            }
        }

        private static void AssignExcessToScouts(Dictionary<int, PlayerSoldier> unassignedSoldierMap, 
                                                 Unit chapter, Date year, ChapterGenerationTemplates templates)
        {
            int sgtNeed = ((unassignedSoldierMap.Count - 1) / 10) + 1;
            List<PlayerSoldier> leaderList = unassignedSoldierMap.Values.OrderByDescending(s => s.SoldierEvaluationHistory[0].LeadershipRating).Take(sgtNeed).ToList();
            List<PlayerSoldier> scoutList = unassignedSoldierMap.Values.Except(leaderList).ToList();
            SoldierTemplate scoutSgt = templates.ScoutSergeant;
            SoldierTemplate scout = templates.ScoutMarine;

            // Fill every scout-capable company up to its cap first.
            List<Unit> scoutCompanies = chapter.ChildUnits
                .Where(c => GetSquadCap(c, templates.ScoutSquad) > 0)
                .ToList();
            foreach (Unit company in scoutCompanies)
            {
                FillCompanyWithSquads(unassignedSoldierMap, company, templates.ScoutSquad,
                    scoutList, leaderList, scout, scoutSgt, (_, _) => 10, year);
            }

            // A founding chapter has far more recruits than the scout company's nominal
            // cap; the remainder overflow into extra scout squads in the scout company.
            Unit overflowCompany = scoutCompanies.LastOrDefault() ?? chapter.ChildUnits.LastOrDefault();
            while (overflowCompany != null && (scoutList.Count > 0 || leaderList.Count > 0))
            {
                Squad squad = new Squad(templates.ScoutSquad.Name, overflowCompany, templates.ScoutSquad);
                overflowCompany.AddSquad(squad);
                if (leaderList.Count > 0)
                {
                    squad.Name = leaderList[0].Name.Split(' ')[1] + " Squad";
                    AssignSoldier(unassignedSoldierMap, leaderList, squad, scoutSgt, year);
                }
                while (scoutList.Count > 0 && squad.Members.Count < 10)
                {
                    AssignSoldier(unassignedSoldierMap, scoutList, squad, scout, year);
                }
                if (squad.Members.Count == 0)
                {
                    overflowCompany.RemoveSquad(squad);
                    break;
                }
            }
            if (unassignedSoldierMap.Count > 0) Debug.WriteLine("Still did it wrong");
        }

        private static void AssignSpecialistsToUnit(Dictionary<int, PlayerSoldier> unassignedSoldierMap,
                                             Unit chapter,
                                             Date year,
                                             SoldierTemplate specialistType,
                                             List<PlayerSoldier> sortedCandidates)
        {
            // Note: Unit.Squads already includes the HQ squad (HQSquad is just a
            // computed lookup into the same list), so iterating Squads covers it.
            // Don't process HQSquad separately or specialists get assigned twice.
            foreach (Squad squad in chapter.Squads)
            {
                AssignSpecialistsToSquad(unassignedSoldierMap, squad, year,
                                         specialistType, sortedCandidates);
            }
            foreach (Unit company in chapter.ChildUnits)
            {
                foreach (Squad squad in company.Squads)
                {
                    AssignSpecialistsToSquad(unassignedSoldierMap, squad, year,
                                             specialistType, sortedCandidates);
                }
            }
        }

        private static void AssignSpecialistsToSquad(Dictionary<int, PlayerSoldier> unassignedSoldierMap,
                                                     Squad squad,
                                                     Date year,
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
                                          Unit chapter, Date year, ChapterGenerationTemplates templates)
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
            AssignTacticalMarines(unassignedSoldierMap, chapter, year, templates, tactList, tactSgtList);
            assList.AddRange(tactList);
            AssignAssaultMarines(unassignedSoldierMap, chapter, year, templates, assList, assSgtList);
            if (assList.Count > 0)
            {
                devList.AddRange(assList.Where(a => a.SoldierEvaluationHistory[0].RangedRating > 80));
                devList = devList.OrderByDescending(s => s.SoldierEvaluationHistory[0].RangedRating).ToList();
            }
            AssignDevastatorMarines(unassignedSoldierMap, chapter, year, templates, devList, devSgtList);
        }

        private static void AssignDevastatorMarines(Dictionary<int, PlayerSoldier> unassignedSoldierMap, 
                                                    Unit chapter, Date year, ChapterGenerationTemplates templates,
                                                    List<PlayerSoldier> devList, 
                                                    List<PlayerSoldier> devSgtList)
        {
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
                FillCompanyWithSquads(unassignedSoldierMap, company, templates.DevastatorSquad,
                    devList, devSgtList, templates.DevastatorMarine, templates.Sergeant,
                    CalculateSquadSize, year);
            }
        }

        private static void AssignAssaultMarines(Dictionary<int, PlayerSoldier> unassignedSoldierMap, 
                                                 Unit chapter, Date year, ChapterGenerationTemplates templates,
                                                 List<PlayerSoldier> assList, 
                                                 List<PlayerSoldier> assSgtList)
        {
            foreach (Unit company in chapter.ChildUnits)
            {
                FillCompanyWithSquads(unassignedSoldierMap, company, templates.AssaultSquad,
                    assList, assSgtList, templates.AssaultMarine, templates.Sergeant,
                    CalculateSquadSize, year);
            }
        }

        private static void AssignTacticalMarines(Dictionary<int, PlayerSoldier> unassignedSoldierMap, 
                                                  Unit chapter, Date year, ChapterGenerationTemplates templates,
                                                  List<PlayerSoldier> tactList, 
                                                  List<PlayerSoldier> tactSgtList)
        {
            foreach (Unit company in chapter.ChildUnits)
            {
                FillCompanyWithSquads(unassignedSoldierMap, company, templates.TacticalSquad,
                    tactList, tactSgtList, templates.TacticalMarine, templates.Sergeant,
                    CalculateSquadSize, year);
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
                                                   Date year)
        {
            PlayerSoldier soldier = soldierList[0];
            soldier.Template = type;
            squad.AddSquadMember(soldier);
            soldier.AddEvent(new SoldierEvent(year, SoldierEventType.Promotion,
                "promoted to " + soldier.Template.Name
                + " and assigned to " + soldier.AssignedSquad.Name));
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
