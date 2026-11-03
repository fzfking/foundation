using System;
using System.Collections.Generic;
using Foundation.DI;
using Foundation.Lifecycle;

namespace Foundation.Simulation
{
    public static class SimulationServiceRegistryExtensions
    {
        /// <summary>
        /// Binary-compatible overload: automatic ticks with the bounded backlog policy. Kept so a caller
        /// compiled against the earlier four-argument signature still binds to it.
        /// </summary>
        public static IServiceRegistry UseSimulation(
            this IServiceRegistry registry,
            float fixedDeltaTime,
            int maxStepsPerTick,
            float maxCatchUpTime)
        {
            return UseSimulation(
                registry,
                fixedDeltaTime,
                maxStepsPerTick,
                maxCatchUpTime,
                tickMode: SimulationTickMode.Automatic,
                backlogPolicy: SimulationBacklogPolicy.Bounded);
        }

        /// <summary>
        /// A <see cref="SimulationRunner"/> over every <see cref="ISimulationSystem"/> the scope sees, ticked each frame by an
        /// entry point. Needs <see cref="Foundation.Timing.ITimeProvider"/> and <see cref="Foundation.Timing.ITimeController"/>
        /// (<c>UseTime</c>) and a container that resolves a collection of a service. Register systems with
        /// <see cref="UseSimulationSystem{TSystem}"/>. The scope also gets a <see cref="SimulationTimers"/> service,
        /// the same instance the runner clears on start and stop and ticks with every step.
        /// </summary>
        public static IServiceRegistry UseSimulation(
            this IServiceRegistry registry,
            float fixedDeltaTime = 0f,
            int maxStepsPerTick = SimulationRunner.MaxFixedStepsPerTick,
            float maxCatchUpTime = 0f,
            SimulationTickMode tickMode = SimulationTickMode.Automatic,
            SimulationBacklogPolicy backlogPolicy = SimulationBacklogPolicy.Bounded)
        {
            if (registry == null)
                throw new ArgumentNullException(nameof(registry));

            registry.Register<TimeProviderSimulationClock>(ServiceLifetime.Singleton).As<ISimulationClock>();
            registry.Register<SimulationTimers>(ServiceLifetime.Singleton);
            registry.Register(
                resolver => new SimulationRunner(
                    resolver.Resolve<IReadOnlyList<ISimulationSystem>>(),
                    resolver.Resolve<ISimulationClock>(),
                    context: null,
                    fixedDeltaTime,
                    maxStepsPerTick,
                    maxCatchUpTime,
                    tickMode,
                    backlogPolicy,
                    resolver.Resolve<SimulationTimers>()),
                ServiceLifetime.Singleton);
            registry.RegisterEntryPoint<SimulationTicker>();
            return registry;
        }

        /// <summary>Adds <typeparamref name="TSystem"/> to the simulation, and registers it as itself.</summary>
        public static IServiceRegistry UseSimulationSystem<TSystem>(this IServiceRegistry registry)
            where TSystem : class, ISimulationSystem
        {
            if (registry == null)
                throw new ArgumentNullException(nameof(registry));

            registry.Register<TSystem>(ServiceLifetime.Singleton).AsSelf().As<ISimulationSystem>();
            return registry;
        }
    }

    /// <summary>Starts the scope's <see cref="SimulationRunner"/> and ticks it every frame; a manual runner ignores the frame tick.</summary>
    public sealed class SimulationTicker : IStartableEntryPoint, ITickableEntryPoint
    {
        private readonly SimulationRunner _runner;

        public SimulationTicker(SimulationRunner runner) => _runner = runner ?? throw new ArgumentNullException(nameof(runner));

        public void Start() => _runner.Start();
        public void Tick() => _runner.Tick();
    }
}
