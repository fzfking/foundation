using System;
using System.Collections.Generic;
using System.Threading;
using Foundation.Config;
using Cysharp.Threading.Tasks;

namespace Foundation.Tutorial.Flow
{
    public sealed class TutorialScriptStep : ITutorialStep
    {
        private readonly string _chapterId;
        private readonly TutorialStepScript _script;
        private readonly int _index;
        private readonly TutorialScriptRunner _runner;

        public TutorialStepId Id { get; }

        public TutorialScriptStep(string chapterId, TutorialStepScript script, int index, TutorialScriptRunner runner)
        {
            _chapterId = chapterId;
            _script = script ?? throw new ArgumentNullException(nameof(script));
            _index = index;
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));
            Id = new(script.Id);
        }

        public UniTask ExecuteAsync(CancellationToken cancellationToken = default)
            => _runner.RunAsync(_chapterId, _script, _index, cancellationToken);
    }

    /// <summary>
    /// Supplies chapters from compiled <see cref="TutorialScriptConfig"/> and registers their steps with
    /// <see cref="TutorialRegistry"/>, so saves, checkpoints, triggers and priorities keep using the Foundation
    /// tutorial orchestration.
    /// </summary>
    public sealed class ScriptTutorialChapterSource : ITutorialChapterSource
    {
        private readonly IConfigResolver _configResolver;
        private readonly TutorialRegistry _registry;
        private readonly TutorialScriptRunner _runner;
        private readonly ITutorialChapterRequirements _requirements;
        private readonly Dictionary<string, string> _registeredSteps = new(StringComparer.Ordinal);

        public ScriptTutorialChapterSource(
            IConfigResolver configResolver,
            TutorialRegistry registry,
            TutorialScriptRunner runner,
            ITutorialChapterRequirements requirements = null)
        {
            _configResolver = configResolver ?? throw new ArgumentNullException(nameof(configResolver));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));
            _requirements = requirements;
        }

        public async UniTask<IReadOnlyList<TutorialChapterDefinition>> GetChaptersAsync(CancellationToken cancellationToken = default)
        {
            var scripts = await _configResolver.ResolveAllAsync<TutorialScriptConfig>(cancellationToken);
            var chapters = new List<TutorialChapterDefinition>(scripts?.Count ?? 0);
            var incomingSteps = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var script in scripts ?? Array.Empty<TutorialScriptConfig>())
            {
                if (script == null)
                    continue;

                foreach (var step in script.Steps ?? Array.Empty<TutorialStepScript>())
                {
                    if (!incomingSteps.TryAdd(step.Id, script.Id))
                        throw new InvalidOperationException(
                            $"Tutorial step id '{step.Id}' is declared more than once (chapters '{incomingSteps[step.Id]}' and '{script.Id}').");

                    if (_registeredSteps.TryGetValue(step.Id, out var registeredChapter)
                        && !string.Equals(registeredChapter, script.Id, StringComparison.Ordinal))
                        throw new InvalidOperationException(
                            $"Tutorial step id '{step.Id}' is already registered to chapter '{registeredChapter}' and cannot be registered to '{script.Id}'.");
                }
            }

            foreach (var script in scripts ?? Array.Empty<TutorialScriptConfig>())
            {
                if (script == null)
                    continue;

                chapters.Add(CreateChapter(script));
            }

            return chapters;
        }

        private TutorialChapterDefinition CreateChapter(TutorialScriptConfig script)
        {
            if (string.IsNullOrWhiteSpace(script.TriggerId))
                throw new InvalidOperationException($"Tutorial chapter '{script.Id}' has no trigger; nothing could start it.");

            var steps = script.Steps ?? Array.Empty<TutorialStepScript>();
            var stepIds = new TutorialStepId[steps.Length];
            var checkpoints = new List<TutorialStepId>();
            for (var index = 0; index < steps.Length; index++)
            {
                var step = steps[index];
                stepIds[index] = new TutorialStepId(step.Id);
                if (step.IsCheckpoint)
                    checkpoints.Add(stepIds[index]);

                if (_registeredSteps.ContainsKey(step.Id)) 
                    continue;
                _registry.Register(new TutorialScriptStep(script.Id, step, index, _runner));
                _registeredSteps.Add(step.Id, script.Id);
            }

            var chapterId = new TutorialChapterId(script.Id);
            return new TutorialChapterDefinition(
                chapterId,
                new TutorialTriggerId(script.TriggerId),
                stepIds,
                checkpoints,
                script.Priority,
                _requirements?.GetRequirement(chapterId));
        }
    }
}
