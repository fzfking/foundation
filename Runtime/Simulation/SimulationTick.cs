namespace Foundation.Simulation
{
    public readonly struct SimulationTick
    {
        public long Index { get; }
        public float DeltaTime { get; }
        public double ElapsedTime { get; }

        public SimulationTick(long index, float deltaTime, double elapsedTime)
        {
            Index = index;
            DeltaTime = deltaTime;
            ElapsedTime = elapsedTime;
        }
    }
}
