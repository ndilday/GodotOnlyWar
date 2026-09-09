using OnlyWar.Domain;
using OnlyWar.Domain.Recruitment;
using OnlyWar.Domain.Soldiers.Ratings;

namespace OnlyWar.Application;

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
