using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Foundation.Lifecycle
{
    /// <summary>Loads the root-scope scene; throws if Unity cannot start the load operation.</summary>
    public sealed class SceneBootStep : IBootStep
    {
        private readonly int _sceneBuildIndex;

        public string Name => "RootScene";

        public int Weight { get; }

        public SceneBootStep(int sceneBuildIndex, int weight = 1)
        {
            if (sceneBuildIndex < 0)
                throw new ArgumentOutOfRangeException(nameof(sceneBuildIndex), sceneBuildIndex, "A scene build index cannot be negative.");
            if (weight < 0)
                throw new ArgumentOutOfRangeException(nameof(weight), weight, "A boot step weight cannot be negative.");

            _sceneBuildIndex = sceneBuildIndex;
            Weight = weight;
        }

        public async UniTask<BootStepOutcome> RunAsync(IProgress<float> progress, CancellationToken cancellationToken)
        {
            var operation = SceneManager.LoadSceneAsync(_sceneBuildIndex);
            if (operation == null)
                throw new InvalidOperationException($"Could not start loading the root scene at build index {_sceneBuildIndex}.");

            operation.allowSceneActivation = true;
            while (!operation.isDone)
            {
                // Unity reports 0.9 when a scene is loaded and only waits to be activated.
                progress?.Report(Mathf.Clamp01(operation.progress / 0.9f));
                await UniTask.Yield(cancellationToken);
            }

            return BootStepOutcome.Continue;
        }
    }
}
