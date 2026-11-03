using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Foundation.Logging;
using Unity.Profiling;

namespace Foundation.Lifecycle
{
    /// <summary>
    /// Runs the application's <see cref="IBootStep"/>s in order. Each step fills its weighted share of the loading
    /// curtain's bar, is measured by a profiler marker of its own, and may end the boot on purpose. An exception from a
    /// step is not caught here: the host (<see cref="BootstrapEntryPoint"/>) decides how a failed boot looks.
    /// </summary>
    public sealed class BootPipeline
    {
        private const string LogCategory = "Boot";

        private readonly IReadOnlyList<IBootStep> _steps;
        private readonly ILoadingCurtain _curtain;
        private readonly IAppLogger _logger;

        public BootPipeline(IReadOnlyList<IBootStep> steps, ILoadingCurtain curtain = null, IAppLogger logger = null)
        {
            _steps = steps ?? throw new ArgumentNullException(nameof(steps));
            _curtain = curtain;
            _logger = logger;
        }

        /// <summary>True when every step ran; false when a step stopped the boot.</summary>
        public async UniTask<bool> RunAsync(CancellationToken cancellationToken = default)
        {
            foreach (var step in _steps)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var progress = _curtain != null && step.Weight > 0
                    ? new CurtainProgress(_curtain, step.Weight)
                    : null;
                BootStepOutcome outcome;
                using (new ProfilerMarker("Foundation.Boot." + step.Name).Auto())
                    outcome = await step.RunAsync(progress, cancellationToken);

                if (outcome == BootStepOutcome.Stop)
                {
                    _logger?.Log(LogLevel.Info, LogCategory, $"Boot stopped by '{step.Name}'.");
                    return false;
                }

                if (step.Weight > 0)
                    _curtain?.AdvanceWeightedStep(step.Weight, null);
            }

            return true;
        }

        // Applied at once rather than posted, so a late report cannot land after the step's own advance.
        private sealed class CurtainProgress : IProgress<float>
        {
            private readonly ILoadingCurtain _curtain;
            private readonly int _weight;

            public CurtainProgress(ILoadingCurtain curtain, int weight)
            {
                _curtain = curtain;
                _weight = weight;
            }

            public void Report(float value) => _curtain.SetWeightedStepFraction(value, _weight, null);
        }
    }
}
