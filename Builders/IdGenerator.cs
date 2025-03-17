using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OnlyWar.Builders
{
    public static class IdGenerator
    {
        private static int _nextOrderId = 0; // Start from 1 or 0, as you prefer
        private static int _nextMissionId = 0;

        public static int GetNextOrderId()
        {
            return _nextOrderId++; // Increment *after* returning the current value
        }

        public static int GetNextMissionId()
        {
            return _nextMissionId++; // Increment *after* returning the current value
        }

        // TODO: if I need thread-safety later)
        // private static int _nextOrderId = 1;
        // private static readonly object _lock = new object();
        // public static int GetNextOrderId()
        // {
        //     lock (_lock)
        //     {
        //         return _nextOrderId++;
        //     }
        // }

        public static void SetNextOrderId(int nextOrderId)
        {
            _nextOrderId = nextOrderId;
        }

        public static void SetNextMissionId(int nextMissionId)
        {
            _nextMissionId = nextMissionId;
        }
    }
}
