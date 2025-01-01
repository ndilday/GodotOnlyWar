using OnlyWar.Builders;
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
            Date date = new(39, 500, 1);
            Date = date;
            GameRulesData = new();
            Sector = SectorBuilder.GenerateSector(1, GameRulesData, date);
        }

        private void LoadSectorData(string fileName)
        {

        }
    }
}
