using System;
using System.Collections.Concurrent;

namespace OnlyWar.Helpers.Battles.Resolutions
{
    public class MoveResolver : IResolver
    {
        public delegate void RetreatHandler(BattleSoldier battleSoldier);
        public event RetreatHandler OnRetreat;

        public ConcurrentBag<MoveResolution> MoveQueue { get; private set; }

        public MoveResolver(bool isVerbose)
        {
            MoveQueue = [];
        }

        public void Resolve()
        {
            while(!MoveQueue.IsEmpty)
            {
                MoveQueue.TryTake(out MoveResolution resolution);
                resolution.Grid.MoveSoldier(resolution.Soldier, resolution.TopLeft, resolution.Orientation);
                resolution.Soldier.TopLeft = resolution.TopLeft;
            }
        }
    }
}
