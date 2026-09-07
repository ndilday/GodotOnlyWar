using OnlyWar.Models;
using OnlyWar.Models.Recruitment;
using OnlyWar.Models.Soldiers.Ratings;

namespace OnlyWar.Helpers.UI;

/// <summary>
/// The campaign facts a soldier dossier is read against: the Chapter's operational doctrine and
/// recruitment program decide duty readiness, and the date and sector place his service record.
/// Supplied explicitly so the builder never resolves a current campaign of its own.
/// </summary>
public sealed record SoldierDetailContext(
    ChapterOperationalDoctrine Doctrine,
    RecruitmentProgram RecruitmentProgram,
    Date CurrentDate,
    Sector Sector,
    RatingConsumerBindings RatingBindings)
{
    public static readonly SoldierDetailContext Empty =
        new(null, null, null, null, null);
}
