using System;
using System.Collections.Generic;

namespace Foundation.Simulation
{
    /// <summary>How frames advance the simulation.</summary>
    public enum SimulationTickMode
    {
        /// <summary>Each <see cref="SimulationRunner.Tick"/> advances by the clock, in whole fixed steps when configured.</summary>
        Automatic,

        /// <summary>Only <see cref="SimulationRunner.Step"/> advances the simulation, by whole fixed steps.</summary>
        Manual
    }

    /// <summary>What a fixed-step frame does with time it could not run within its step budget.</summary>
    public enum SimulationBacklogPolicy
    {
        /// <summary>Carry at most <see cref="SimulationRunner.MaxCatchUpTime"/>; report the rest as dropped time.</summary>
        Bounded,

        /// <summary>Keep every unprocessed whole step and run it over later frames, without dropping any.</summary>
        Preserve
    }

    /// <summary>
    /// Ticks the simulation systems in order, by the frame's time or in whole fixed steps. A fixed-step frame
    /// runs at most <see cref="MaxStepsPerTick"/> steps, so one long frame cannot stall the next ones. Whole
    /// steps beyond that budget follow <see cref="BacklogPolicy"/>: by default they carry into following frames
    /// up to <see cref="MaxCatchUpTime"/> and the rest is skipped and added to
    /// <see cref="SimulationContext.DroppedTime"/>.
    /// A game that fast-forwards (x3, x5) raises the step budget to what its top speed needs per frame; one
    /// that must not lose time (a colony sim) allows catch-up or preserves the backlog; an action game keeps
    /// the default, which skips a hitch rather than replaying it in a burst.
    /// A manual runner ignores <see cref="Tick"/> and advances only through <see cref="Step"/>, so an external
    /// lockstep controller owns when each whole step happens.
    /// <para>
    /// One step runs the ordered systems first and due timers last. A system or timer that calls
    /// <see cref="Pause"/> finishes the current step, commits its TickIndex once, and stops the rest of the
    /// batch; a system or timer that calls <see cref="Stop"/> aborts the current step immediately, resets the
    /// context to TickIndex zero and discards timers, so no later system or timer of that step runs and no
    /// TickIndex increment follows the reset. Calling <see cref="Tick"/> or <see cref="Step"/> from a simulation
    /// callback throws instead of advancing the simulation from inside itself.
    /// </para>
    /// </summary>
    public sealed class SimulationRunner
    {
        public const int MaxFixedStepsPerTick = 8;

        private readonly ISimulationSystem[] _systems;
        private readonly ISimulationClock _clock;
        private readonly SimulationContext _context;
        private readonly SimulationTimers _timers;
        private readonly float _fixedDeltaTime;
        private float _accumulator;
        private bool _isAdvancing;
        private bool _stepStopped;
        private bool _stepPaused;

        public int MaxStepsPerTick { get; }

        /// <summary>The most simulated time carried into later frames once a frame ran out of steps.</summary>
        public float MaxCatchUpTime { get; }

        public SimulationContext Context => _context;

        public IReadOnlyList<ISimulationSystem> Systems => _systems;

        /// <summary>Timers counted in whole simulation ticks; cleared whenever the runner starts or stops.</summary>
        public SimulationTimers Timers => _timers;

        public bool IsFixedStep => _fixedDeltaTime > 0f;

        public SimulationTickMode TickMode { get; }

        public SimulationBacklogPolicy BacklogPolicy { get; }

        /// <summary>
        /// Creates an automatic runner with the bounded backlog policy. Kept as a non-optional six-argument
        /// overload so assemblies compiled against the earlier signature still bind to it.
        /// </summary>
        public SimulationRunner(
            IEnumerable<ISimulationSystem> systems,
            ISimulationClock clock,
            SimulationContext context,
            float fixedDeltaTime,
            int maxStepsPerTick,
            float maxCatchUpTime)
            : this(
                systems,
                clock,
                context,
                fixedDeltaTime,
                maxStepsPerTick,
                maxCatchUpTime,
                SimulationTickMode.Automatic,
                SimulationBacklogPolicy.Bounded,
                null)
        {
        }

        public SimulationRunner(
            IEnumerable<ISimulationSystem> systems,
            ISimulationClock clock,
            SimulationContext context = null,
            float fixedDeltaTime = 0f,
            int maxStepsPerTick = MaxFixedStepsPerTick,
            float maxCatchUpTime = 0f,
            SimulationTickMode tickMode = SimulationTickMode.Automatic,
            SimulationBacklogPolicy backlogPolicy = SimulationBacklogPolicy.Bounded,
            SimulationTimers timers = null)
        {
            if (systems == null)
                throw new ArgumentNullException(nameof(systems));
            if (!float.IsFinite(fixedDeltaTime) || fixedDeltaTime < 0f)
                throw new ArgumentOutOfRangeException(nameof(fixedDeltaTime), "Fixed delta time must be finite and non-negative.");
            if (maxStepsPerTick < 1)
                throw new ArgumentOutOfRangeException(nameof(maxStepsPerTick));
            if (!float.IsFinite(maxCatchUpTime) || maxCatchUpTime < 0f)
                throw new ArgumentOutOfRangeException(nameof(maxCatchUpTime), "Maximum catch-up time must be finite and non-negative.");
            if (tickMode == SimulationTickMode.Manual && fixedDeltaTime <= 0f)
                throw new ArgumentException("Manual tick mode needs a fixed delta time.", nameof(fixedDeltaTime));
            if (tickMode != SimulationTickMode.Automatic && tickMode != SimulationTickMode.Manual)
                throw new ArgumentOutOfRangeException(nameof(tickMode));
            if (backlogPolicy != SimulationBacklogPolicy.Bounded && backlogPolicy != SimulationBacklogPolicy.Preserve)
                throw new ArgumentOutOfRangeException(nameof(backlogPolicy));

            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _context = context ?? new SimulationContext();
            _timers = timers ?? new SimulationTimers();
            _fixedDeltaTime = fixedDeltaTime;
            MaxStepsPerTick = maxStepsPerTick;
            MaxCatchUpTime = maxCatchUpTime;
            TickMode = tickMode;
            BacklogPolicy = backlogPolicy;
            _systems = Order(systems);
        }

        /// <summary>
        /// Resets the context and timers, discarding timers scheduled before this call, then runs. A game that
        /// restores saved events schedules them again after this start.
        /// </summary>
        public void Start()
        {
            Reset();
            if (_isAdvancing)
                _stepStopped = true;
            _context.State = SimulationState.Running;
        }

        /// <summary>
        /// Resets the context to TickIndex zero and clears timers. Called from a simulation callback, it also
        /// aborts the current step and the rest of that batch without committing an index increment.
        /// </summary>
        public void Stop()
        {
            Reset();
            if (_isAdvancing)
                _stepStopped = true;
        }

        public void Pause()
        {
            if (_context.State != SimulationState.Running)
                return;

            _context.State = SimulationState.Paused;
            if (_isAdvancing)
                _stepPaused = true;
        }

        public void Resume()
        {
            if (_context.State == SimulationState.Paused)
                _context.State = SimulationState.Running;
        }

        /// <summary>
        /// Advances a running automatic simulation by the clock. Does nothing in manual mode. A Pause from a
        /// callback finishes the current step and stops the batch; a Stop aborts it; a reentrant call throws.
        /// </summary>
        public void Tick()
        {
            if (_isAdvancing)
                throw new InvalidOperationException("Tick cannot be called from a simulation callback.");
            if (_context.State != SimulationState.Running || TickMode != SimulationTickMode.Automatic)
                return;

            _isAdvancing = true;
            _stepStopped = false;
            _stepPaused = false;
            try
            {
                var delta = _clock.GetDeltaTime();
                if (!float.IsFinite(delta))
                    throw new InvalidOperationException("The simulation clock returned a non-finite delta time.");
                if (delta <= 0f)
                    return;

                if (!IsFixedStep)
                {
                    Advance(delta);
                    return;
                }

                var accumulated = _accumulator + delta;
                if (!float.IsFinite(accumulated))
                    throw new InvalidOperationException("The simulation time accumulator became non-finite.");
                _accumulator = accumulated;
                var steps = 0;
                while (_accumulator >= _fixedDeltaTime
                       && steps < MaxStepsPerTick
                       && !_stepStopped
                       && !_stepPaused
                       && _context.State == SimulationState.Running)
                {
                    _accumulator -= _fixedDeltaTime;
                    Advance(_fixedDeltaTime);
                    steps++;
                }

                if (steps == MaxStepsPerTick && BacklogPolicy == SimulationBacklogPolicy.Bounded && _accumulator > MaxCatchUpTime)
                {
                    _context.DroppedTime += _accumulator - MaxCatchUpTime;
                    _accumulator = MaxCatchUpTime;
                }
            }
            finally
            {
                _isAdvancing = false;
            }
        }

        /// <summary>
        /// Advances a running manual simulation by <paramref name="steps"/> whole fixed steps; a stopped or
        /// paused runner does nothing. An automatic runner rejects this call so time-driven and externally
        /// driven ticking never mix. A Pause from a callback finishes the current step and stops the batch; a
        /// Stop aborts it; a reentrant call throws.
        /// </summary>
        public void Step(int steps = 1)
        {
            if (_isAdvancing)
                throw new InvalidOperationException("Step cannot be called from a simulation callback.");
            if (TickMode != SimulationTickMode.Manual)
                throw new InvalidOperationException("Step is available only in manual tick mode.");
            if (steps < 0)
                throw new ArgumentOutOfRangeException(nameof(steps));
            if (_context.State != SimulationState.Running)
                return;

            _isAdvancing = true;
            _stepStopped = false;
            _stepPaused = false;
            try
            {
                for (var index = 0; index < steps && !_stepStopped && !_stepPaused && _context.State == SimulationState.Running; index++)
                    Advance(_fixedDeltaTime);
            }
            finally
            {
                _isAdvancing = false;
            }
        }

        private void Reset()
        {
            _context.Reset();
            _accumulator = 0f;
            _timers.Reset();
        }

        private void Advance(float delta)
        {
            var previousElapsedTime = _context.ElapsedTime;
            _context.ElapsedTime = previousElapsedTime + delta;
            var tick = new SimulationTick(_context.TickIndex, delta, _context.ElapsedTime);
            try
            {
                _timers.BeginTick(tick.Index);
                foreach (var simulationSystem in _systems)
                {
                    simulationSystem.Tick(in tick);
                    if (_stepStopped)
                        return;
                }

                _timers.Tick(in tick);
                if (_stepStopped)
                    return;
            }
            catch
            {
                // shortcut: systems may mutate game state before throwing; the owning game must recover it before restarting.
                // A Stop or Start inside the step already reset the context; restoring the pre-step time here
                // would resurrect a run that was deliberately discarded.
                if (!_stepStopped)
                    _context.ElapsedTime = previousElapsedTime;
                _context.State = SimulationState.Stopped;
                _timers.Reset();
                throw;
            }

            _context.TickIndex++;
        }

        private static ISimulationSystem[] Order(IEnumerable<ISimulationSystem> systems)
        {
            var ordered = new List<SystemEntry>();
            foreach (var system in systems)
                ordered.Add(new SystemEntry(system, ordered.Count));
            ordered.Sort(CompareSystems);

            var result = new ISimulationSystem[ordered.Count];
            for (var index = 0; index < ordered.Count; index++)
                result[index] = ordered[index].System;
            return result;
        }

        private static int CompareSystems(SystemEntry left, SystemEntry right)
        {
            var byOrder = left.System.Order.CompareTo(right.System.Order);
            if (byOrder != 0)
                return byOrder;

            var byType = string.CompareOrdinal(left.System.GetType().FullName, right.System.GetType().FullName);
            return byType != 0
                ? byType
                : left.RegistrationIndex.CompareTo(right.RegistrationIndex);
        }

        private readonly struct SystemEntry
        {
            public readonly ISimulationSystem System;
            public readonly int RegistrationIndex;

            public SystemEntry(ISimulationSystem system, int registrationIndex)
            {
                System = system;
                RegistrationIndex = registrationIndex;
            }
        }
    }
}
