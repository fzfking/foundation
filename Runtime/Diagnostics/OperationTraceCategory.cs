#if UNITY_EDITOR || FOUNDATION_DEVELOPMENT
using System;

namespace Foundation.Diagnostics
{
    [Flags]
    public enum OperationTraceCategory
    {
        None = 0,
        Command = 1 << 0,
        Query = 1 << 1,
        Event = 1 << 2,
        Subscriber = 1 << 3,
        Simulation = 1 << 4,

        Default = Command | Query | Event | Subscriber,

        All = Command | Query | Event | Subscriber | Simulation
    }
}
#endif
