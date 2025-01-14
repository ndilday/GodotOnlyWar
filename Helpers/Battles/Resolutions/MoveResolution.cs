﻿using System;
using System.Collections.Generic;


namespace OnlyWar.Helpers.Battles.Resolutions
{
    public class MoveResolution
    {
        public BattleSoldier Soldier { get; private set; }
        public BattleGridManager Grid { get; private set; }
        public Tuple<int, int> TopLeft { get; private set; }
        public ushort Orientation { get; private set; }

        public MoveResolution(BattleSoldier soldier, BattleGridManager grid, Tuple<int, int> topLeft, ushort orientation)
        {
            Soldier = soldier;
            Grid = grid;
            TopLeft = topLeft;
            Orientation = orientation;
        }
    }
}
