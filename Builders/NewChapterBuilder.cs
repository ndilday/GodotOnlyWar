
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
        // Judiciars (chaplains in training) kept in the Reclusium beyond the one seconded to
        // each captained company, forming the chaplaincy's pool of aspirants.
        private const int RECLUSIUM_JUDICIAR_RESERVE = 2;
        private const int DEFAULT_FOUNDING_SOLDIER_COUNT = 1000;
        private const int MAX_TECHMARINES = 50;
        // A company only staffs its HQ if at least one line squad can be seeded from the
        // remaining pool: this many sergeants and members of a matching squad type.
        // Tuning knobs for how top-heavy a small company is allowed to found.
        private const int MIN_SEED_SERGEANTS = 1;
        private const int MIN_SEED_MEMBERS = 4;
        private delegate void TrainingFunction(PlayerSoldier playerSoldier);

        internal static PlayerForce CreateChapter(GameRulesData data,
                                                  ISoldierTrainingService trainingService,
                                                  Date trainingStartDate,
                                                  Date date,
                                                  string chapterName = null,
                                                  int foundingSoldierCount = DEFAULT_FOUNDING_SOLDIER_COUNT)
        {
            if (foundingSoldierCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(foundingSoldierCount),
                    "A chapter must start with at least one soldier.");
            }

            chapterName = string.IsNullOrWhiteSpace(chapterName) ? "Heart of the Emperor" : chapterName.Trim();
            Date trainingEndDate =
                new Date(trainingStartDate.GetTotalWeeks() + INITIAL_TRAINING_DURATION_WEEKS);
            List<PlayerSoldier> soldiers = GenerateInitialSoldiers(
                data, trainingService, trainingStartDate, date, trainingEndDate, foundingSoldierCount);

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
                $"The {chapterName} officially forms with its first {foundingSoldierCount:N0} battle brothers."
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

        private static List<PlayerSoldier> GenerateInitialSoldiers(
            GameRulesData data,
            ISoldierTrainingService trainingService,
            Date trainingStartDate,
            Date date,
            Date trainingEndDate,
            int foundingSoldierCount)
        {
            SoldierTemplate soldierTemplate = data.PlayerFaction.SoldierTemplates[0];
            List<PlayerSoldier> soldiers =
                SoldierFactory.Instance.GenerateNewSoldiers(
                    foundingSoldierCount,
                    soldierTemplate.Species,
                    data.SkillTemplateList,
                    StaticRNG.Instance)
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
            // Rank every non-psyker for every founding role up front (score → derive
            // demand → consume; Design/FoundingRoleAssignment.md). Each list is consumed
            // best-first; a soldier taken for one role is skipped by every other list.
            RoleSuitabilityService suitability = new(unassignedSoldierMap.Values.ToList());
            Dictionary<FoundingRole, List<PlayerSoldier>> roleLists =
                Enum.GetValues<FoundingRole>()
                    .ToDictionary(role => role, suitability.CreateCandidateList);

            // Psykers are categorically Librarius material and appear in no role list.
            AssignLibrarians(unassignedSoldierMap, oob, year, templates);
            AssignTechMarines(unassignedSoldierMap, oob, year, templates, roleLists);
            AssignChapterHQ(unassignedSoldierMap, oob, year, templates, roleLists);
            AssignApothecarionLeader(unassignedSoldierMap, oob, year, templates, roleLists);
            AssignReclusiumLeaders(unassignedSoldierMap, oob, year, templates, roleLists);

            // The Scout Company's captain is chosen before the battle companies' so the
            // chapter's next generation trains under the best remaining leader.
            Unit scoutCompany = oob.ChildUnits
                .FirstOrDefault(u => u.UnitTemplate == templates.ScoutCompany);
            if (scoutCompany != null
                && unassignedSoldierMap.Count >= MIN_SEED_SERGEANTS + MIN_SEED_MEMBERS)
            {
                StaffCompanyHQ(unassignedSoldierMap, scoutCompany, year, templates, roleLists,
                    isVeteranCompany: false);
            }

            PopulateCompanies(year, unassignedSoldierMap, oob, templates, roleLists);

            // Remainder sweep: every unconsumed medical candidate staffs the Apothecarion...
            Squad apothecarion = oob.Squads.First(s => s.SquadTemplate == templates.Apothecarion);
            List<PlayerSoldier> apothecaries = roleLists[FoundingRole.Apothecary];
            while (CountAvailable(unassignedSoldierMap, apothecaries) > 0)
            {
                AssignSoldier(unassignedSoldierMap, apothecaries, apothecarion,
                    templates.Apothecary, year);
            }
            // ...and the Reclusium holds a small Judiciar reserve of chaplain aspirants.
            Squad reclusium = oob.Squads.First(s => s.SquadTemplate == templates.Reclusium);
            List<PlayerSoldier> chaplains = roleLists[FoundingRole.Chaplain];
            for (int i = 0; i < RECLUSIUM_JUDICIAR_RESERVE
                && CountAvailable(unassignedSoldierMap, chaplains) > 0; i++)
            {
                AssignSoldier(unassignedSoldierMap, chaplains, reclusium, templates.Judiciar, year);
            }

            // Everyone left becomes a scout.
            AssignExcessToScouts(unassignedSoldierMap, oob, year, templates);
        }

        // Companies are populated in order-of-battle order, so earlier companies draw
        // better soldiers — a deliberate quality gradient. A company's HQ is staffed
        // only when at least one line squad can be seeded; otherwise the company is
        // skipped entirely and its (always-present) HQ squad founds empty, to be
        // staffed later through the promotion/transfer flow like the First Company.
        private static void PopulateCompanies(Date year,
                                              Dictionary<int, PlayerSoldier> unassignedSoldierMap,
                                              Unit chapter, ChapterGenerationTemplates templates,
                                              Dictionary<FoundingRole, List<PlayerSoldier>> roleLists)
        {
            int veteranSquadSize = CalculateVeteranSquadSize(roleLists);
            bool lineListsBalanced = false;

            foreach (Unit company in chapter.ChildUnits)
            {
                // The scout company's HQ is staffed up front and its squads are filled
                // by the excess-to-scouts sweep at the end.
                if (company.UnitTemplate == templates.ScoutCompany)
                {
                    continue;
                }

                List<SlotAssignment> seedable = new();
                foreach (SquadTemplateSlot slot in company.UnitTemplate.GetChildSquadSlots())
                {
                    if (slot.Template == templates.ScoutSquad)
                    {
                        continue;
                    }
                    bool isElite = (slot.Template.SquadType & SquadTypes.Elite) > 0;
                    if (!isElite && !lineListsBalanced)
                    {
                        // Line lists are balanced only once the veteran company has
                        // consumed its soldiers, mirroring the old veterans-before-
                        // marines ordering.
                        BalanceLineLists(unassignedSoldierMap, roleLists);
                        lineListsBalanced = true;
                    }
                    SlotAssignment assignment =
                        ResolveSlotAssignment(slot, templates, roleLists, veteranSquadSize);
                    if (assignment == null)
                    {
                        continue;
                    }
                    if (CountAvailable(unassignedSoldierMap, assignment.SergeantList) >= MIN_SEED_SERGEANTS
                        && CountAvailable(unassignedSoldierMap, assignment.MemberList) >= MIN_SEED_MEMBERS)
                    {
                        seedable.Add(assignment);
                    }
                }

                if (seedable.Count == 0)
                {
                    continue;
                }

                StaffCompanyHQ(unassignedSoldierMap, company, year, templates, roleLists,
                    isVeteranCompany: company.UnitTemplate == templates.VeteranCompany);
                foreach (SlotAssignment assignment in seedable)
                {
                    FillCompanyWithSquads(unassignedSoldierMap, company, assignment.SquadTemplate,
                        assignment.MemberList, assignment.SergeantList,
                        assignment.MemberTemplate, assignment.SergeantTemplate,
                        assignment.SquadSizeFunc, year);
                }
            }

            SpillIntoVacantSeats(year, unassignedSoldierMap, chapter, templates, roleLists);
        }

        // Ports the old AssignMarines overflow cascades: surplus tactical candidates
        // backfill vacant assault seats, then surplus assault (and tactical) candidates
        // with serviceable ranged ratings backfill vacant devastator seats. Vacancies
        // only, leftovers only — nobody already assigned is displaced.
        private static void SpillIntoVacantSeats(Date year,
                                                 Dictionary<int, PlayerSoldier> unassignedSoldierMap,
                                                 Unit chapter, ChapterGenerationTemplates templates,
                                                 Dictionary<FoundingRole, List<PlayerSoldier>> roleLists)
        {
            FillVacancies(year, unassignedSoldierMap, chapter, templates, roleLists,
                templates.AssaultSquad, roleLists[FoundingRole.TacticalMarine],
                roleLists[FoundingRole.AssaultSergeant], templates.AssaultMarine);

            // The devastator spill pool is whoever is left from the assault and tactical
            // pools with enough ranged skill to man a heavy weapon (the old ranged > 80
            // gate), best shots first.
            List<PlayerSoldier> devastatorSpill = roleLists[FoundingRole.AssaultMarine]
                .Concat(roleLists[FoundingRole.TacticalMarine])
                .Where(s => unassignedSoldierMap.ContainsKey(s.Id)
                    && s.SoldierEvaluationHistory[0].RangedRating > 80)
                .OrderByDescending(s => s.SoldierEvaluationHistory[0].RangedRating)
                .ToList();
            FillVacancies(year, unassignedSoldierMap, chapter, templates, roleLists,
                templates.DevastatorSquad, devastatorSpill,
                roleLists[FoundingRole.DevastatorSergeant], templates.DevastatorMarine);
        }

        private static void FillVacancies(Date year,
                                          Dictionary<int, PlayerSoldier> unassignedSoldierMap,
                                          Unit chapter, ChapterGenerationTemplates templates,
                                          Dictionary<FoundingRole, List<PlayerSoldier>> roleLists,
                                          SquadTemplate squadTemplate,
                                          List<PlayerSoldier> memberList,
                                          List<PlayerSoldier> sergeantList,
                                          SoldierTemplate memberTemplate)
        {
            foreach (Unit company in chapter.ChildUnits)
            {
                if (company.UnitTemplate == templates.ScoutCompany)
                {
                    continue;
                }
                if (CountAvailable(unassignedSoldierMap, memberList) == 0)
                {
                    return;
                }
                int vacancies = GetSquadCap(company, squadTemplate)
                    - company.Squads.Count(s => s.SquadTemplate == squadTemplate);
                if (vacancies <= 0)
                {
                    continue;
                }
                // A company that gains its first line squad through spill deserves a
                // staffed HQ. As in the main pass, the HQ is staffed before the squads
                // fill so the captain is not consumed as a spill member first.
                bool hasLineSquad = company.Squads.Any(s =>
                    (s.SquadTemplate.SquadType & SquadTypes.HQ) == 0 && s.Members.Count > 0);
                if (company.HQSquad != null && company.HQSquad.SquadLeader == null
                    && (hasLineSquad
                        || CountAvailable(unassignedSoldierMap, memberList) >= MIN_SEED_MEMBERS))
                {
                    StaffCompanyHQ(unassignedSoldierMap, company, year, templates, roleLists,
                        isVeteranCompany: company.UnitTemplate == templates.VeteranCompany);
                }
                FillCompanyWithSquads(unassignedSoldierMap, company, squadTemplate,
                    memberList, sergeantList, memberTemplate, templates.Sergeant,
                    CalculateSquadSize, year);
            }
        }

        // The lists and templates that fill one company squad slot.
        private sealed class SlotAssignment
        {
            public SquadTemplate SquadTemplate;
            public List<PlayerSoldier> MemberList;
            public List<PlayerSoldier> SergeantList;
            public SoldierTemplate MemberTemplate;
            public SoldierTemplate SergeantTemplate;
            public Func<List<PlayerSoldier>, List<PlayerSoldier>, int> SquadSizeFunc;
        }

        private static SlotAssignment ResolveSlotAssignment(SquadTemplateSlot slot,
                                                            ChapterGenerationTemplates templates,
                                                            Dictionary<FoundingRole, List<PlayerSoldier>> roleLists,
                                                            int veteranSquadSize)
        {
            if ((slot.Template.SquadType & SquadTypes.Elite) > 0)
            {
                return new SlotAssignment
                {
                    SquadTemplate = slot.Template,
                    MemberList = roleLists[FoundingRole.Veteran],
                    SergeantList = roleLists[FoundingRole.VeteranSergeant],
                    MemberTemplate = templates.Veteran,
                    SergeantTemplate = templates.VeteranSergeant,
                    SquadSizeFunc = (_, _) => veteranSquadSize
                };
            }
            if (slot.Template == templates.TacticalSquad)
            {
                return LineSlot(slot, roleLists, FoundingRole.TacticalMarine,
                    FoundingRole.TacticalSergeant, templates.TacticalMarine, templates.Sergeant);
            }
            if (slot.Template == templates.AssaultSquad)
            {
                return LineSlot(slot, roleLists, FoundingRole.AssaultMarine,
                    FoundingRole.AssaultSergeant, templates.AssaultMarine, templates.Sergeant);
            }
            if (slot.Template == templates.DevastatorSquad)
            {
                return LineSlot(slot, roleLists, FoundingRole.DevastatorMarine,
                    FoundingRole.DevastatorSergeant, templates.DevastatorMarine, templates.Sergeant);
            }
            return null;
        }

        private static SlotAssignment LineSlot(SquadTemplateSlot slot,
                                               Dictionary<FoundingRole, List<PlayerSoldier>> roleLists,
                                               FoundingRole memberRole, FoundingRole sergeantRole,
                                               SoldierTemplate memberTemplate, SoldierTemplate sergeantTemplate)
        {
            return new SlotAssignment
            {
                SquadTemplate = slot.Template,
                MemberList = roleLists[memberRole],
                SergeantList = roleLists[sergeantRole],
                MemberTemplate = memberTemplate,
                SergeantTemplate = sergeantTemplate,
                SquadSizeFunc = CalculateSquadSize
            };
        }

        // Veteran squad size is derived from the founding cohort's veteran-to-sergeant
        // ratio, clamped to 5..10, as before the role-list rework.
        private static int CalculateVeteranSquadSize(Dictionary<FoundingRole, List<PlayerSoldier>> roleLists)
        {
            int veteranCount = roleLists[FoundingRole.Veteran].Count;
            int sergeantCount = roleLists[FoundingRole.VeteranSergeant].Count;
            if (sergeantCount == 0)
            {
                return 5;
            }
            int squadSize = (veteranCount / sergeantCount) + 1;
            return Math.Clamp(squadSize, 5, 10);
        }

        private static void BalanceLineLists(Dictionary<int, PlayerSoldier> unassignedSoldierMap,
                                             Dictionary<FoundingRole, List<PlayerSoldier>> roleLists)
        {
            // Prune already-assigned soldiers so the cohort ratios BalanceLists reasons
            // about reflect who is actually still available.
            CountAvailable(unassignedSoldierMap, roleLists[FoundingRole.TacticalMarine]);
            CountAvailable(unassignedSoldierMap, roleLists[FoundingRole.TacticalSergeant]);
            CountAvailable(unassignedSoldierMap, roleLists[FoundingRole.AssaultMarine]);
            CountAvailable(unassignedSoldierMap, roleLists[FoundingRole.AssaultSergeant]);
            CountAvailable(unassignedSoldierMap, roleLists[FoundingRole.DevastatorMarine]);
            CountAvailable(unassignedSoldierMap, roleLists[FoundingRole.DevastatorSergeant]);
            BalanceLists(roleLists[FoundingRole.AssaultMarine], roleLists[FoundingRole.DevastatorMarine],
                roleLists[FoundingRole.TacticalMarine], roleLists[FoundingRole.AssaultSergeant],
                roleLists[FoundingRole.DevastatorSergeant], roleLists[FoundingRole.TacticalSergeant]);
        }

        // Staffs a company HQ squad: the captain first (so he outranks his sergeants),
        // then the support elements the HQ squad template defines. If no qualified
        // captain is available the HQ is left entirely empty.
        private static void StaffCompanyHQ(Dictionary<int, PlayerSoldier> unassignedSoldierMap,
                                           Unit company, Date year,
                                           ChapterGenerationTemplates templates,
                                           Dictionary<FoundingRole, List<PlayerSoldier>> roleLists,
                                           bool isVeteranCompany)
        {
            Squad hq = company.HQSquad;
            if (hq == null || hq.SquadLeader != null)
            {
                return;
            }
            SquadTemplateElement leaderElement = hq.SquadTemplate.Elements
                .FirstOrDefault(e => e.SoldierTemplate.IsSquadLeader);
            if (leaderElement == null)
            {
                return;
            }
            List<PlayerSoldier> captains =
                roleLists[isVeteranCompany ? FoundingRole.VeteranCaptain : FoundingRole.Captain];
            if (!AssignSoldier(unassignedSoldierMap, captains, hq, leaderElement.SoldierTemplate, year))
            {
                return;
            }
            StaffHQSupport(unassignedSoldierMap, hq, year, templates, roleLists);
        }

        // Fills the non-leader elements of an HQ squad template (Chaplain, Judiciar,
        // Apothecary, Champion, Ancient) from the matching role lists, up to each
        // element's maximum. A Judiciar is the next-most-pious after the Chaplain, so
        // both draw from the Chaplain list in element order.
        private static void StaffHQSupport(Dictionary<int, PlayerSoldier> unassignedSoldierMap,
                                           Squad hq, Date year,
                                           ChapterGenerationTemplates templates,
                                           Dictionary<FoundingRole, List<PlayerSoldier>> roleLists)
        {
            // minor hack to avoid assigning non-veteran champions/ancients to veteran HQ squads
            bool isElite = (hq.SquadTemplate.SquadType & SquadTypes.Elite) > 0;
            foreach (SquadTemplateElement element in hq.SquadTemplate.Elements
                .Where(e => !e.SoldierTemplate.IsSquadLeader))
            {
                FoundingRole? role = null;
                if (element.SoldierTemplate == templates.Chaplain) role = FoundingRole.Chaplain;
                else if (element.SoldierTemplate == templates.Judiciar) role = FoundingRole.Chaplain;
                else if (element.SoldierTemplate == templates.Apothecary) role = FoundingRole.Apothecary;
                else if (element.SoldierTemplate == templates.Champion && !isElite) role = FoundingRole.Champion;
                else if (element.SoldierTemplate == templates.Ancient && !isElite) role = FoundingRole.Ancient;
                if (role == null)
                {
                    continue;
                }
                for (int i = 0; i < element.MaximumNumber; i++)
                {
                    if (!AssignSoldier(unassignedSoldierMap, roleLists[role.Value], hq,
                        element.SoldierTemplate, year))
                    {
                        break;
                    }
                }
            }
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

        private static void AssignChapterHQ(Dictionary<int, PlayerSoldier> unassignedSoldierMap,
                                            Unit chapter, Date year,
                                            ChapterGenerationTemplates templates,
                                            Dictionary<FoundingRole, List<PlayerSoldier>> roleLists)
        {
            PlayerSoldier master =
                TakeTop(unassignedSoldierMap, roleLists[FoundingRole.ChapterMaster]);
            master.Template = templates.ChapterMaster;
            chapter.HQSquad.AddSquadMember(master);
            master.AddEvent(new SoldierEvent(year, SoldierEventType.Founding,
                "voted by the chapter to become the first Chapter Master"));
            unassignedSoldierMap.Remove(master.Id);
            // The Chapter Champion and Chapter Ancient, per the HQ squad's elements.
            StaffHQSupport(unassignedSoldierMap, chapter.HQSquad, year, templates, roleLists);
        }

        private static void AssignLibrarians(Dictionary<int, PlayerSoldier> unassignedSoldierMap, 
                                             Unit chapter, Date year, ChapterGenerationTemplates templates)
        {
            // assume for now that there's a single unit to hold all of the Librarians as a squad on the chapter
            Squad library = chapter.Squads.First(s => s.SquadTemplate == templates.Librarius);
            List<PlayerSoldier> psychers =
                unassignedSoldierMap.Values.Where(s => s.PsychicPower > 0)
                                           .OrderByDescending(s => s.Ego)
                                           .ToList();
            // TODO: add 24 points

            // Librarius rank is relative to the psykers the founding actually produced, never an
            // absolute Ego score. Marine Ego is base 14 / SD 1.4, so any gate high enough to feel
            // exceptional is one a chapter's handful of psykers essentially never clears, and the
            // Librarius founds leaderless and all-Lexicanium. The best psyker leads it whoever he
            // is - the squad template requires a Master of the Librarium - and the seniority split
            // below him follows the template's own Codicier:Lexicanium capacity ratio.
            int codicierCount = GetFoundingCodicierCount(psychers.Count - 1, templates);
            for (int i = 0; i < psychers.Count; i++)
            {
                PlayerSoldier soldier = psychers[i];
                if (i == 0)
                {
                    soldier.Template = templates.MasterOfTheLibrarium;
                }
                else if (i <= codicierCount)
                {
                    soldier.Template = templates.Codicier;
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

        // Splits the non-leader psykers between Codicier and Lexicanium in proportion to the
        // seats the Librarius template allots each rank, so the rank pyramid's shape lives in
        // the rules DB rather than in a threshold here.
        private static int GetFoundingCodicierCount(int nonLeaderPsykerCount,
                                                    ChapterGenerationTemplates templates)
        {
            if (nonLeaderPsykerCount <= 0) return 0;
            int codicierSeats = GetTemplateSeats(templates.Librarius, templates.Codicier);
            int lexicaniumSeats = GetTemplateSeats(templates.Librarius, templates.Lexicanium);
            if (codicierSeats + lexicaniumSeats == 0) return 0;
            int codicierCount = (int)Math.Round(
                nonLeaderPsykerCount * codicierSeats / (double)(codicierSeats + lexicaniumSeats),
                MidpointRounding.AwayFromZero);
            return Math.Min(codicierCount, codicierSeats);
        }

        private static int GetTemplateSeats(SquadTemplate squadTemplate, SoldierTemplate soldierTemplate)
        {
            return squadTemplate.Elements
                .Where(e => e.SoldierTemplate == soldierTemplate)
                .Sum(e => (int)e.MaximumNumber);
        }

        private static void AssignTechMarines(Dictionary<int, PlayerSoldier> unassignedSoldierMap,
                                              Unit chapter, Date year,
                                              ChapterGenerationTemplates templates,
                                              Dictionary<FoundingRole, List<PlayerSoldier>> roleLists)
        {
            // assume for now that there's a single unit to hold all of the Techmarines
            Squad armory = chapter.Squads.First(s => s.SquadTemplate == templates.Armory);
            int assigned = 0;
            PlayerSoldier master =
                TakeTop(unassignedSoldierMap, roleLists[FoundingRole.MasterOfTheForge]);
            if (master != null)
            {
                AssignTechMarine(unassignedSoldierMap, armory, master, templates.MasterOfTheForge, year);
                assigned++;
            }
            List<PlayerSoldier> techmarines = roleLists[FoundingRole.Techmarine];
            while (assigned < MAX_TECHMARINES)
            {
                PlayerSoldier techmarine = TakeTop(unassignedSoldierMap, techmarines);
                if (techmarine == null)
                {
                    break;
                }
                AssignTechMarine(unassignedSoldierMap, armory, techmarine, templates.Techmarine, year);
                assigned++;
            }
        }

        private static void AssignTechMarine(Dictionary<int, PlayerSoldier> unassignedSoldierMap,
                                             Squad armory, PlayerSoldier soldier,
                                             SoldierTemplate template, Date year)
        {
            soldier.Template = template;
            armory.AddSquadMember(soldier);
            soldier.AddEvent(new SoldierEvent(year, SoldierEventType.Promotion,
                "Returned from Mars, promoted to "
                + soldier.Template.Name + " and assigned to " + soldier.AssignedSquad.Name));
            unassignedSoldierMap.Remove(soldier.Id);
        }

        // The most skilled initiate leads the Apothecarion as Master of the
        // Apothecarion, if he is also a capable leader; otherwise the chapter founds
        // without one for now. Company apothecaries are seconded when each company's
        // HQ is staffed; the remaining medical candidates return to the Apothecarion
        // in the remainder sweep.
        private static void AssignApothecarionLeader(Dictionary<int, PlayerSoldier> unassignedSoldierMap,
                                                     Unit chapter, Date year,
                                                     ChapterGenerationTemplates templates,
                                                     Dictionary<FoundingRole, List<PlayerSoldier>> roleLists)
        {
            Squad apo = chapter.Squads.First(s => s.SquadTemplate == templates.Apothecarion);
            AssignSoldier(unassignedSoldierMap, roleLists[FoundingRole.MasterOfTheApothecarion],
                apo, templates.MasterOfTheApothecarion, year);
        }

        // The Reclusium is the chaplaincy's home: it holds the Master of Sanctity, the
        // Reclusiarch, and a reserve of Judiciars (chaplains in training) beyond the one
        // seconded to each company. The most pious qualified initiate leads the
        // chaplaincy as Master of Sanctity; if no one qualifies, the chapter founds
        // without one for now. The next-most pious becomes the Reclusiarch. Company
        // chaplains and judiciars are seconded when each company's HQ is staffed.
        private static void AssignReclusiumLeaders(Dictionary<int, PlayerSoldier> unassignedSoldierMap,
                                                   Unit chapter, Date year,
                                                   ChapterGenerationTemplates templates,
                                                   Dictionary<FoundingRole, List<PlayerSoldier>> roleLists)
        {
            Squad reclusium = chapter.Squads.First(s => s.SquadTemplate == templates.Reclusium);
            AssignSoldier(unassignedSoldierMap, roleLists[FoundingRole.MasterOfSanctity],
                reclusium, templates.MasterOfSanctity, year);
            AssignSoldier(unassignedSoldierMap, roleLists[FoundingRole.Chaplain],
                reclusium, templates.Reclusiarch, year);
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
            // Count squads of this template the company already holds (e.g. from the
            // main pass, when this is a spill pass) so the cap is never exceeded.
            int created = company.Squads.Count(s => s.SquadTemplate == squadTemplate);
            while (created < cap
                && (CountAvailable(unassignedSoldierMap, soldierList) > 0
                    || CountAvailable(unassignedSoldierMap, sgtList) > 0))
            {
                Squad squad = new Squad(squadTemplate.Name, company, squadTemplate);
                company.AddSquad(squad);
                if (sgtList.Count > 0)
                {
                    squad.Name = sgtList[0].Name.Split(' ')[1] + " Squad";
                    AssignSoldier(unassignedSoldierMap, sgtList, squad, sgtType, year);
                }
                int squadSize = squadSizeFunc(soldierList, sgtList);
                while (squad.Members.Count < squadSize
                    && AssignSoldier(unassignedSoldierMap, soldierList, squad, soldierType, year))
                {
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

        // Takes the best still-unassigned soldier from a candidate list and assigns
        // him. Candidate lists overlap (one soldier ranks for many roles), so soldiers
        // consumed elsewhere are skipped and dropped as they are encountered. Returns
        // false when the list has no available soldier left.
        private static bool AssignSoldier(Dictionary<int, PlayerSoldier> unassignedSoldierMap,
                                                   List<PlayerSoldier> soldierList,
                                                   Squad squad,
                                                   SoldierTemplate type,
                                                   Date year)
        {
            PlayerSoldier soldier = TakeTop(unassignedSoldierMap, soldierList);
            if (soldier == null)
            {
                return false;
            }
            soldier.Template = type;
            squad.AddSquadMember(soldier);
            soldier.AddEvent(new SoldierEvent(year, SoldierEventType.Promotion,
                "promoted to " + soldier.Template.Name
                + " and assigned to " + soldier.AssignedSquad.Name
                + ", " + soldier.AssignedSquad.ParentUnit.Name));
            unassignedSoldierMap.Remove(soldier.Id);
            return true;
        }

        // Pops the best available (still-unassigned) soldier from a candidate list,
        // or null if none remain. Does not remove him from the unassigned map; the
        // caller assigns him and removes him.
        private static PlayerSoldier TakeTop(Dictionary<int, PlayerSoldier> unassignedSoldierMap,
                                             List<PlayerSoldier> soldierList)
        {
            while (soldierList.Count > 0 && !unassignedSoldierMap.ContainsKey(soldierList[0].Id))
            {
                soldierList.RemoveAt(0);
            }
            if (soldierList.Count == 0)
            {
                return null;
            }
            PlayerSoldier soldier = soldierList[0];
            soldierList.RemoveAt(0);
            return soldier;
        }

        // Prunes soldiers assigned through other role lists, then reports how many
        // candidates remain available.
        private static int CountAvailable(Dictionary<int, PlayerSoldier> unassignedSoldierMap,
                                          List<PlayerSoldier> soldierList)
        {
            soldierList.RemoveAll(s => !unassignedSoldierMap.ContainsKey(s.Id));
            return soldierList.Count;
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
