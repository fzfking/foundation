namespace Foundation.Simulation
{
    public enum SimulationState
    {
        Stopped,
        Running,
        Paused
    }

    public sealed class SimulationContext
    {
        public SimulationState State { get; internal set; } = SimulationState.Stopped;

        public long TickIndex { get; internal set; }

        public double ElapsedTime { get; internal set; }

        /// <summary>
        /// Simulated time skipped because a frame needed more fixed steps than the budget and more catch-up
        /// than allowed. Above zero means the simulation ran slower than its clock.
        /// </summary>
        public double DroppedTime { get; internal set; }

        public bool IsRunning => State == SimulationState.Running;

        internal void Reset()
        {
            State = SimulationState.Stopped;
            TickIndex = 0;
            ElapsedTime = 0d;
            DroppedTime = 0d;
        }
    }
}
