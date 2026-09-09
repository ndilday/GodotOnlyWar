using System;
using System.Collections.Generic;
using System.Linq;
using OnlyWar.Abstractions;
using OnlyWar.Campaign.Recruitment;
using OnlyWar.Domain;
using OnlyWar.Domain.Planets;
using OnlyWar.Domain.Recruitment;
using OnlyWar.Domain.Soldiers.Ratings;
using OnlyWar.Domain.Squads;
using OnlyWar.Domain.Units;

namespace OnlyWar.Application;

/// <summary>
/// The recruitment and Scout Company context used by the 10th Company screen.
///
/// It provides recruitment facts, authored training options and feature commands while keeping
/// the full sector and rules objects private to the application composition root.
/// </summary>
internal sealed class TrainingContext
{
    private readonly Sector _sector;
    private readonly GameRulesData _rules;
    private readonly IPersistentIdAllocator _identity;
    private readonly RecruitmentStaffService _staffService = new();

    internal Date CurrentDate { get; }
    internal RecruitmentProgram Program => _sector.PlayerForce?.RecruitmentProgram;
    internal Unit Chapter => _sector.PlayerForce?.Army?.OrderOfBattle;
    internal ChapterOperationalDoctrine OperationalDoctrine =>
        _sector.PlayerForce?.Army?.OperationalDoctrine;
    internal int FactionId => _sector.PlayerForce?.Faction?.Id ?? -1;
    internal int Requisition => _sector.PlayerForce?.Army?.Requisition ?? 0;
    internal ushort GeneseedStockpile => _sector.PlayerForce?.GeneseedStockpile ?? 0;
    internal ScoutTrainingOptionCatalog ScoutTrainingOptions => _rules.ScoutTrainingOptions;
    internal RatingConsumerBindings RatingConsumers =>
        _rules.RatingConsumers ?? RatingConsumerBindings.CreateDefault();
    internal IEnumerable<Squad> ChapterSquads => Chapter?.GetAllSquads() ?? [];

    internal TrainingContext(
        Sector sector,
        GameRulesData rules,
        Date currentDate,
        IPersistentIdAllocator identity)
    {
        _sector = sector ?? throw new ArgumentNullException(nameof(sector));
        _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        CurrentDate = currentDate ?? throw new ArgumentNullException(nameof(currentDate));
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
    }

    internal void SynchronizeStaff() =>
        _staffService.Synchronize(
            _sector.PlayerForce, _rules, _sector, identity: _identity);

    internal void ValidateTrainingOption(string optionKey) =>
        _rules.ScoutTrainingOptions.GetRequired(optionKey);

    internal Planet FindHomeWorld()
    {
        int planetId = _sector.PlayerForce?.HomeWorldPlanetId ?? Program?.HomeWorldPlanetId ?? -1;
        return _sector.Planets.TryGetValue(planetId, out Planet planet) ? planet : null;
    }
}
