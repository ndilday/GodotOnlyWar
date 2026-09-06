using System;
using OnlyWar.Models;

namespace OnlyWar.Contracts.Application;

/// <summary>
/// Transitional compatibility defaults for legacy host-facing APIs. New application and campaign
/// workflows pass an <see cref="ICampaignSession"/> explicitly; SB-12 removes these defaults after
/// the remaining UI and compatibility callers migrate.
/// </summary>
public static class CampaignRuntimeDefaults
{
    private static Func<GameRulesData> _rules;
    private static Func<Sector> _sector;

    public static GameRulesData Rules => _rules?.Invoke();
    public static Sector Sector => _sector?.Invoke();
    public static PlayerForce PlayerForce => Sector?.PlayerForce;

    public static void Configure(Func<GameRulesData> rules, Func<Sector> sector)
    {
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        _sector = sector ?? throw new ArgumentNullException(nameof(sector));
    }
}
