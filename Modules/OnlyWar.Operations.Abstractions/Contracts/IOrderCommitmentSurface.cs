using OnlyWar.Models.Soldiers;
using OnlyWar.Models.Squads;

namespace OnlyWar.Operations.Abstractions;

/// <summary>
/// The operational commitments Campaign personnel is allowed to release (SB-05b-1).
///
/// Physical posting and order commitment are two different facts about the same soldier, owned by
/// two different subsystems: Campaign owns where a man physically is, Operations owns what he is
/// committed to. Moving or standing down a man has to retire his commitment as well, and before
/// this surface existed the posting service reached straight into the order services to do it --
/// the circular half of the personnel dependency that blocked the Operations extraction, since
/// Campaign may not reference Operations.
///
/// Release is idempotent and safe on a soldier or formation holding no commitment; the result says
/// whether anything was actually released.
/// </summary>
public interface IOrderCommitmentSurface
{
    /// <summary>Retires an individual's order membership, leaving his physical posting alone.</summary>
    bool ReleaseCharacter(PlayerSoldier character);

    /// <summary>Retires a formation's order assignment when it ceases to exist physically.</summary>
    bool ReleaseSquad(Squad squad);
}
