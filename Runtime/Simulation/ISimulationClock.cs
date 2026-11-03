using System;
using Foundation.Timing;

namespace Foundation.Simulation
{
    public interface ISimulationClock
    {
        float GetDeltaTime();
    }

    public sealed class TimeProviderSimulationClock : ISimulationClock
    {
        private readonly ITimeProvider _timeProvider;
        private readonly ITimeController _timeController;

        public TimeProviderSimulationClock(ITimeProvider timeProvider, ITimeController timeController)
        {
            _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
            _timeController = timeController ?? throw new ArgumentNullException(nameof(timeController));
        }

        public float GetDeltaTime() => _timeProvider.UnscaledDeltaTime * _timeController.SimulationScale;
    }
}
