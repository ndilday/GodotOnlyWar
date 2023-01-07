﻿using OnlyWar.Builders;
using OnlyWar.Helpers;
using System;

namespace OnlyWar.Models
{
    internal sealed class GameDataSingleton
    {
        private static readonly Lazy<GameDataSingleton> lazy =
        new Lazy<GameDataSingleton>(() => new GameDataSingleton());

        public static GameDataSingleton Instance { get { return lazy.Value; } }

        public GameRulesData GameRulesData { get; private set; }
        public Sector Sector { get; private set; }
        public Date Date { get; set; }
        private GameDataSingleton()
        {
            Date = new(39, 500, 1);
            GameRulesData = new();
            Sector = SectorBuilder.GenerateSector(Random.Shared.Next(), GameRulesData);
            // Load Sector Data
        }

        private void LoadSectorData(string fileName)
        {

        }
    }
}
