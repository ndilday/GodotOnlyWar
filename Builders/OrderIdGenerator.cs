using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OnlyWar.Builders
{
    public static class OrderIdGenerator
    {
        private static int _nextOrderId = 0; // Start from 1 or 0, as you prefer

        public static int GetNextOrderId()
        {
            return _nextOrderId++; // Increment *after* returning the current value
        }

        // (Optional - if you need thread-safety later)
        // private static int _nextOrderId = 1;
        // private static readonly object _lock = new object();
        // public static int GetNextOrderId()
        // {
        //     lock (_lock)
        //     {
        //         return _nextOrderId++;
        //     }
        // }
    }
}
