namespace Foundation.Simulation
{
    public interface ISimulationSystem
    {
        int Order { get; }

        void Tick(in SimulationTick tick);
    }
}
