using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Foundation.Lifecycle
{
    public enum BootStepOutcome
    {
        Continue,

        /// <summary>The boot ends here on purpose (a client that must update): nothing after this step runs and
        /// the step has put whatever explains it on screen.</summary>
        Stop,
    }

    /// <summary>
    /// One phase of the application's startup, before the root scope exists: whatever must happen outside the DI
    /// container (content initialization, a release check, loading the root scene). A project lists its steps in a
    /// <see cref="BootstrapEntryPoint"/>, in order; <see cref="BootPipeline"/> runs them and drives the loading curtain.
    /// </summary>
    public interface IBootStep
    {
        /// <summary>Names the step in logs and in the profiler marker <c>Foundation.Boot.&lt;Name&gt;</c>.</summary>
        string Name { get; }

        /// <summary>How many loading-curtain steps this step covers; 0 for work that should not move the bar.</summary>
        int Weight { get; }

        /// <summary>Runs the step; <paramref name="progress"/> reports 0 to 1 over its share of the bar.</summary>
        UniTask<BootStepOutcome> RunAsync(IProgress<float> progress, CancellationToken cancellationToken);
    }
}
