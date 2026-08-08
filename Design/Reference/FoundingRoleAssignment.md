# Founding Role Assignment

**Status:** Implemented 2026-07-17.

TDD §6.8 is the architectural source of truth. This reference retains the detailed eligibility rules and behavior decisions that are too granular for the TDD.

## Assignment model

Founding assignment follows `score → derive demand → consume`:

1. Psykers are removed into the Librarius path, ranked by Ego, and assigned relative seniority from the Librarius template's Codicier-to-Lexicanium seat ratio.
2. `RoleSuitabilityService` creates best-first candidate lists for every non-Librarius `FoundingRole`.
3. Eligibility is applied before sorting. Ineligible soldiers are absent rather than represented by sentinel scores.
4. `NewChapterBuilder` derives which company line squads can be seeded before staffing their HQs.
5. All lists consume through the shared `unassignedSoldierMap`, so a soldier who qualifies for several roles can only be assigned once.

`RoleSuitabilityService` excludes psykers entirely; non-psykers never enter the Librarius path.

## Eligibility and ordering

| Role | Eligibility from the founding evaluation | Primary order |
|---|---|---|
| Chapter Master | Any non-psyker | Leadership |
| Librarius ranks | Psyker; handled outside `RoleSuitabilityService` | Ego, then template seat ratio |
| Techmarine | Tech > 75 | Tech |
| Master of the Forge | Tech > 100 and Leadership > 60 | Tech |
| Apothecary | Medical > 95 | Medical |
| Master of the Apothecarion | Medical > 115 and Leadership > 60 | Medical |
| Chaplain / Judiciar | Piety > 90 | Piety |
| Master of Sanctity | Piety > 100 and Leadership > 60 | Piety |
| Reclusiarch | Piety > 90 | Piety |
| Veteran Captain | Leadership > 75, Melee > 105, Ranged > 110 | Leadership |
| Captain | Any non-psyker | Leadership |
| Veteran Sergeant | Veteran criteria and Leadership > 60 | Leadership |
| Veteran | Melee > 90, Ranged > 105, and either Melee > 115 or Ranged > 120 | Melee |
| Champion | Any non-psyker | Melee |
| Ancient | Any non-psyker | Ancient |
| Tactical Marine | Melee > 90, Ranged > 105, Leadership < 50 | Ranged |
| Tactical Sergeant | Tactical bands, Leadership > 50 | Leadership |
| Assault Marine | Melee > 90, 95 < Ranged < 105, Leadership < 50 | Melee |
| Assault Sergeant | Assault bands, Leadership > 50 | Leadership |
| Devastator Marine | 80 < Melee < 90, Ranged > 95, Leadership < 50 | Ranged |
| Devastator Sergeant | Devastator bands, Leadership > 50 | Leadership |
| Scout Sergeant | Any remaining non-psyker | Leadership |
| Scout Marine | Any remaining non-psyker | Pool remainder |

Threshold boundaries are deliberate and covered by `RoleSuitabilityServiceTests`.

## Structural decisions

- A line squad is seedable with at least one eligible sergeant and four eligible members.
- A company with no seedable line squad keeps an empty HQ rather than founding a “ghost company” staffed only by officers and specialists.
- Within a seedable company, the HQ is staffed before line squads. Earlier companies therefore receive the stronger eligible candidates by design.
- The Veteran Company may seed line squads while leaving its HQ empty if no qualified Veteran Captain exists.
- Surplus tactical candidates may fill vacant assault seats. Remaining tactical/assault candidates with sufficient ranged aptitude may fill vacant devastator seats. This spill pass fills vacancies only and never displaces an assignment.
- Surplus Apothecaries return to the Apothecarion; surplus Chaplains and Judiciars return to the Reclusium; all other remaining soldiers enter the Tenth Company.
- Empty HQ squads are persistent and remain available to the normal promotion and transfer flow.

## Future direction

Role thresholds and sort keys remain code-defined. They are candidates for rules-data migration if chapter-generation tuning or in-campaign promotion suggestions need the same policy to become moddable.
