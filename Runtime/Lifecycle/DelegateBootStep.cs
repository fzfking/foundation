using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace Foundation.Lifecycle
{
    /// <summary>A boot step written inline: the project's own phases that need no class of their own.</summary>
    public sealed class DelegateBootStep : IBootStep
    {
        private readonly Func<IProgress<float>, CancellationToken, UniTask<BootStepOutcome>> _run;

        public string Name { get; }

        public int Weight { get; }

        public DelegateBootStep(string name, int weight, Func<IProgress<float>, CancellationToken, UniTask<BootStepOutcome>> run)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("A boot step needs a name.", nameof(name));
            if (weight < 0)
                throw new ArgumentOutOfRangeException(nameof(weight), weight, "A boot step weight cannot be negative.");

            Name = name;
            Weight = weight;
            _run = run ?? throw new ArgumentNullException(nameof(run));
        }

        /// <summary>A step that always lets the boot continue.</summary>
        public DelegateBootStep(string name, int weight, Func<IProgress<float>, CancellationToken, UniTask> run)
            : this(name, weight, Continue(run ?? throw new ArgumentNullException(nameof(run))))
        {
        }

        public UniTask<BootStepOutcome> RunAsync(IProgress<float> progress, CancellationToken cancellationToken)
            => _run(progress, cancellationToken);

        private static Func<IProgress<float>, CancellationToken, UniTask<BootStepOutcome>> Continue(
            Func<IProgress<float>, CancellationToken, UniTask> run)
            => async (progress, cancellationToken) =>
            {
                await run(progress, cancellationToken);
                return BootStepOutcome.Continue;
            };
    }
}
