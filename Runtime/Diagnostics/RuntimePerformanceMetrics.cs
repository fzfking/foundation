#if UNITY_EDITOR || DEVELOPMENT_BUILD || FOUNDATION_DEVELOPMENT
using System;
using System.Collections.Generic;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine.Profiling;

namespace Foundation.Diagnostics
{
    public readonly struct FramePerformance
    {
        public FramePerformance(int count, double seconds, float shortest, float longest, float latest)
        {
            Frames = count;
            FramesPerSecond = latest > 0 ? 1f / latest : 0;
            FrameMilliseconds = latest * 1000f;
            AverageFramesPerSecond = seconds > 0 ? count / seconds : 0;
            MinimumFramesPerSecond = longest > 0 ? 1f / longest : 0;
            MaximumFramesPerSecond = shortest > 0 ? 1f / shortest : 0;
        }

        public int Frames { get; }
        public double FramesPerSecond { get; }
        public double FrameMilliseconds { get; }
        public double AverageFramesPerSecond { get; }
        public double MinimumFramesPerSecond { get; }
        public double MaximumFramesPerSecond { get; }
    }

    /// <summary>A bounded time window; average FPS is frames/time rather than a mean of reciprocals.</summary>
    public sealed class FramePerformanceWindow
    {
        private readonly float[] _durations;
        private readonly float _windowSeconds;
        private int _start;
        private int _count;
        private double _seconds;
        private float _latest;

        public FramePerformanceWindow(float windowSeconds = 3f, int capacity = 4096)
        {
            if (windowSeconds <= 0 || float.IsNaN(windowSeconds) || float.IsInfinity(windowSeconds))
                throw new ArgumentOutOfRangeException(nameof(windowSeconds));
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            _windowSeconds = windowSeconds;
            _durations = new float[capacity];
        }

        public void Clear()
        {
            _start = 0;
            _count = 0;
            _seconds = 0;
            _latest = 0;
        }

        public void Add(float seconds)
        {
            if (seconds <= 0 || float.IsNaN(seconds) || float.IsInfinity(seconds))
                return;
            if (_count == _durations.Length)
                RemoveOldest();
            _durations[(_start + _count) % _durations.Length] = seconds;
            _count++;
            _seconds += seconds;
            _latest = seconds;
            while (_count > 1 && _seconds - _durations[_start] >= _windowSeconds)
                RemoveOldest();
        }

        public FramePerformance Read()
        {
            var shortest = float.MaxValue;
            var longest = 0f;
            for (var index = 0; index < _count; index++)
            {
                var duration = _durations[(_start + index) % _durations.Length];
                shortest = Math.Min(shortest, duration);
                longest = Math.Max(longest, duration);
            }
            return new FramePerformance(_count, _seconds, _count > 0 ? shortest : 0, longest, _latest);
        }

        private void RemoveOldest()
        {
            _seconds -= _durations[_start];
            _start = (_start + 1) % _durations.Length;
            _count--;
        }
    }

    public enum PerformanceMetric
    {
        ManagedUsed,
        ManagedReserved,
        TotalAllocated,
        TotalReserved,
        SystemUsed,
        GcAllocatedInFrame,
        MainThread,
        RenderThread,
        DrawCalls,
        Batches,
        SetPass,
        Triangles,
        StandardDrawCalls,
        IndirectDrawCalls,
        InstancedDrawCalls,
        SrpDrawCalls,
        BrgDrawCalls,
        BrgIndirectDrawCalls,
        ProceduralDrawCalls,
        ProceduralIndirectDrawCalls,
        DynamicBatches,
        StaticBatches,
        InstancedBatches,
        Count,
    }

    /// <summary>UI-independent diagnostics. Start/Stop belong to the caller's lifetime; Tick allocates nothing.</summary>
    public sealed class RuntimePerformanceMetrics : IDisposable
    {
        private readonly FramePerformanceWindow _frames = new();
        private readonly ProfilerRecorder[] _recorders = new ProfilerRecorder[(int)PerformanceMetric.Count];

        public bool IsRunning { get; private set; }
        public int ActiveRecorderCount { get; private set; }

        public void Start()
        {
            if (IsRunning)
                return;
            _frames.Clear();
            var available = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(available);
            foreach (var handle in available)
            {
                var description = ProfilerRecorderHandle.GetDescription(handle);
                var metric = FindMetric(description.Name);
                if (metric == PerformanceMetric.Count || _recorders[(int)metric].Valid)
                    continue;
                var expected = metric <= PerformanceMetric.GcAllocatedInFrame
                    ? ProfilerMarkerDataUnit.Bytes
                    : metric <= PerformanceMetric.RenderThread ? ProfilerMarkerDataUnit.TimeNanoseconds : ProfilerMarkerDataUnit.Count;
                if (description.UnitType != expected && !(metric >= PerformanceMetric.DrawCalls && description.UnitType == ProfilerMarkerDataUnit.Undefined))
                    continue;
                try
                {
                    _recorders[(int)metric] = ProfilerRecorder.StartNew(description.Category, description.Name, 1);
                    if (_recorders[(int)metric].Valid)
                        ActiveRecorderCount++;
                }
                catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or NotSupportedException)
                {
                    // Some platform counters are enumerated but cannot be recorded.
                }
            }
            IsRunning = true;
        }

        public void Tick(float unscaledDeltaTime)
        {
            if (IsRunning)
                _frames.Add(unscaledDeltaTime);
        }

        public FramePerformance ReadFrames() => _frames.Read();

        public long? Read(PerformanceMetric metric)
        {
            if (!IsRunning)
                return null;
            var recorder = _recorders[(int)metric];
            if (recorder is { Valid: true, Count: > 0 })
                return recorder.LastValue;
            if (metric == PerformanceMetric.DrawCalls)
            {
                // Unity 6.5 exposes mutually exclusive draw paths instead of an aggregate counter.
                long total = 0;
                for (var index = (int)PerformanceMetric.StandardDrawCalls; index <= (int)PerformanceMetric.ProceduralIndirectDrawCalls; index++)
                {
                    var value = Read((PerformanceMetric)index);
                    if (!value.HasValue)
                        return null;
                    total += value.Value;
                }
                return total;
            }
            // Memory APIs do not require a Profiler connection and provide device fallbacks.
            var fallback = metric switch
            {
                PerformanceMetric.ManagedUsed => Profiler.GetMonoUsedSizeLong(),
                PerformanceMetric.ManagedReserved => Profiler.GetMonoHeapSizeLong(),
                PerformanceMetric.TotalAllocated => Profiler.GetTotalAllocatedMemoryLong(),
                PerformanceMetric.TotalReserved => Profiler.GetTotalReservedMemoryLong(),
                _ => -1,
            };
            return fallback > 0 ? fallback : null;
        }

        public void Stop()
        {
            for (var index = 0; index < _recorders.Length; index++)
            {
                if (_recorders[index].Valid)
                    _recorders[index].Dispose();
                _recorders[index] = default;
            }
            IsRunning = false;
            ActiveRecorderCount = 0;
            _frames.Clear();
        }

        public void Dispose() => Stop();

        private static PerformanceMetric FindMetric(string name) => name switch
        {
            "GC Used Memory" => PerformanceMetric.ManagedUsed,
            "GC Reserved Memory" => PerformanceMetric.ManagedReserved,
            "Total Used Memory" => PerformanceMetric.TotalAllocated,
            "Total Reserved Memory" => PerformanceMetric.TotalReserved,
            "System Used Memory" => PerformanceMetric.SystemUsed,
            "GC Allocated In Frame" => PerformanceMetric.GcAllocatedInFrame,
            "Main Thread" => PerformanceMetric.MainThread,
            "Render Thread" => PerformanceMetric.RenderThread,
            "Draw Calls Count" => PerformanceMetric.DrawCalls,
            "Batches Count" => PerformanceMetric.Batches,
            "SetPass Calls Count" => PerformanceMetric.SetPass,
            "Triangles Count" => PerformanceMetric.Triangles,
            "Standard Draw Calls Count" => PerformanceMetric.StandardDrawCalls,
            "Standard Indirect Draw Calls Count" => PerformanceMetric.IndirectDrawCalls,
            "Standard Instanced Draw Calls Count" => PerformanceMetric.InstancedDrawCalls,
            "SRP Batcher Draw Calls Count" => PerformanceMetric.SrpDrawCalls,
            "BRG Draw Calls Count" => PerformanceMetric.BrgDrawCalls,
            "BRG Indirect Draw Calls Count" => PerformanceMetric.BrgIndirectDrawCalls,
            "Null Geometry Draw Calls Count" => PerformanceMetric.ProceduralDrawCalls,
            "Null Geometry Indirect Draw Calls Count" => PerformanceMetric.ProceduralIndirectDrawCalls,
            "Dynamic Batches Count" => PerformanceMetric.DynamicBatches,
            "Static Batches Count" => PerformanceMetric.StaticBatches,
            "Instanced Batches Count" => PerformanceMetric.InstancedBatches,
            _ => PerformanceMetric.Count,
        };
    }
}
#endif
