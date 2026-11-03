using System;
using System.Collections.Generic;

namespace Foundation.Simulation
{
    /// <summary>A scheduled simulation timer; disposing it cancels the callback.</summary>
    public interface ISimulationTimerHandle : IDisposable
    {
        bool IsActive { get; }
    }

    /// <summary>
    /// Game events counted in whole simulation ticks, never in wall-clock time. The owning
    /// <see cref="SimulationRunner"/> ticks it after every system of a step, so a timer fires at a tick boundary
    /// and does not depend on the frame delta. Timers are cleared by <see cref="SimulationRunner.Start"/> and
    /// <see cref="SimulationRunner.Stop"/>, so they never outlive a run. Callbacks are not serialized: a game
    /// that saves events restores them by scheduling again after it starts the simulation.
    /// Scheduling before <see cref="SimulationRunner.Start"/> is discarded by that start; schedule after it, or
    /// from a system's first tick, and replay saved events there after every start.
    /// </summary>
    public sealed class SimulationTimers
    {
        private readonly LinkedList<Entry> _entries = new();
        private readonly List<Entry> _due = new();
        private long _currentTick = -1;
        private long _sequence;

        /// <summary>
        /// Schedules <paramref name="callback"/> after skipping <paramref name="delayTicks"/> whole ticks; zero
        /// runs on the next tick. A timer scheduled during a tick never runs in that same tick. A due tick that
        /// would overflow the tick counter is rejected. A timer scheduled before Start is discarded by Start.
        /// </summary>
        public ISimulationTimerHandle Schedule(long delayTicks, Action callback)
        {
            if (delayTicks < 0)
                throw new ArgumentOutOfRangeException(nameof(delayTicks), delayTicks, "A timer delay is a whole number of ticks and cannot be negative.");
            if (callback == null)
                throw new ArgumentNullException(nameof(callback));

            long dueTick;
            try
            {
                var nextTick = checked(_currentTick + 1);
                if (delayTicks > long.MaxValue - 1L - nextTick)
                    throw new OverflowException();
                dueTick = checked(nextTick + delayTicks);
            }
            catch (OverflowException)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(delayTicks), delayTicks, "The timer's due tick overflows the simulation tick counter.");
            }

            var entry = new Entry(this, dueTick, _sequence++, callback);
            entry.Node = _entries.AddLast(entry);
            return entry;
        }

        /// <summary>
        /// Cancels every scheduled timer, including timers already due in the current step, and releases their
        /// callbacks. Start and Stop clear this way too.
        /// </summary>
        public void Clear()
        {
            foreach (var entry in _entries)
                entry.Deactivate();
            _entries.Clear();

            for (var index = 0; index < _due.Count; index++)
                _due[index].Deactivate();
            _due.Clear();
        }

        internal void Reset()
        {
            Clear();
            _currentTick = -1;
            _sequence = 0;
        }

        internal void BeginTick(long tickIndex) => _currentTick = tickIndex;

        internal void Tick(in SimulationTick tick)
        {
            _due.Clear();
            try
            {
                for (var node = _entries.First; node != null;)
                {
                    var next = node.Next;
                    var entry = node.Value;
                    if (!entry.IsActive)
                        _entries.Remove(node);
                    else if (entry.DueTick <= tick.Index)
                        _due.Add(entry);
                    node = next;
                }

                if (_due.Count == 0)
                    return;

                _due.Sort(CompareEntries);
                for (var index = 0; index < _due.Count; index++)
                {
                    var entry = _due[index];
                    if (!entry.IsActive)
                        continue;

                    if (entry.Node != null)
                    {
                        _entries.Remove(entry.Node);
                        entry.Node = null;
                    }

                    var callback = entry.Callback;
                    entry.Deactivate();
                    callback?.Invoke();
                }
            }
            finally
            {
                // Due entries must not keep callbacks alive after this step, especially when a callback threw.
                _due.Clear();
            }
        }

        private void Cancel(Entry entry)
        {
            if (entry.Node != null)
            {
                _entries.Remove(entry.Node);
                entry.Node = null;
            }

            entry.Deactivate();
        }

        private static int CompareEntries(Entry left, Entry right)
        {
            var byDue = left.DueTick.CompareTo(right.DueTick);
            return byDue != 0
                ? byDue
                : left.Sequence.CompareTo(right.Sequence);
        }

        private sealed class Entry : ISimulationTimerHandle
        {
            private readonly SimulationTimers _owner;

            public readonly long DueTick;
            public readonly long Sequence;
            public LinkedListNode<Entry> Node;
            public Action Callback;

            private bool _active = true;

            public Entry(SimulationTimers owner, long dueTick, long sequence, Action callback)
            {
                _owner = owner;
                DueTick = dueTick;
                Sequence = sequence;
                Callback = callback;
            }

            public bool IsActive => _active;

            public void Dispose() => _owner.Cancel(this);

            public void Deactivate()
            {
                _active = false;
                Callback = null;
                Node = null;
            }
        }
    }
}
