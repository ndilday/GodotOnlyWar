using System;
using System.Collections.Concurrent;

namespace OnlyWar.Helpers.Battles.Resolutions
{
    public class MoveResolver : IResolver
    {
        public delegate void RetreatHandler(BattleSoldier battleSoldier);
        event RetreatHandler OnRetreat;

        public ConcurrentBag<MoveResolution> MoveQueue { get; private set; }

        public MoveResolver()
        {
            MoveQueue = new ConcurrentBag<MoveResolution>();
        }

        public void Resolve()
        {
            while(!MoveQueue.IsEmpty)
            {
                MoveQueue.TryTake(out MoveResolution resolution);
                if(resolution.Grid.IsEmpty(resolution.TopLeft))
                {
                    resolution.Grid.MoveSoldier(resolution.Soldier, resolution.TopLeft, resolution.Orientation);
                    resolution.Soldier.TopLeft = resolution.TopLeft;
                    // TODO: need new retreat logic
                }
                else
                {
                    throw new InvalidOperationException("Soldier " + resolution.Soldier.Soldier.Name + " could not move to targeted position");
                }
            }
        }
    }
}
