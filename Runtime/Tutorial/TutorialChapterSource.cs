using System;
using System.Collections.Generic;
using System.Threading;
using Foundation.Config;
using Cysharp.Threading.Tasks;

namespace Foundation.Tutorial
{
    public interface ITutorialChapterSource
    {
        UniTask<IReadOnlyList<TutorialChapterDefinition>> GetChaptersAsync(
            CancellationToken cancellationToken = default);
    }

    public sealed class ConfigTutorialChapterSource : ITutorialChapterSource
    {
        private readonly IConfigResolver _configResolver;
        private readonly ITutorialChapterRequirements _requirements;

        public ConfigTutorialChapterSource(
            IConfigResolver configResolver,
            ITutorialChapterRequirements requirements = null)
        {
            _configResolver = configResolver ?? throw new ArgumentNullException(nameof(configResolver));
            _requirements = requirements;
        }

        public async UniTask<IReadOnlyList<TutorialChapterDefinition>> GetChaptersAsync(
            CancellationToken cancellationToken = default)
        {
            var configs = await _configResolver.ResolveAllAsync<TutorialConfig>(cancellationToken);
            var chapters = new List<TutorialChapterDefinition>(configs?.Count ?? 0);
            foreach (var config in configs ?? Array.Empty<TutorialConfig>())
            {
                if (config == null)
                    continue;

                var chapterId = new TutorialChapterId(config.Id);
                if (string.IsNullOrWhiteSpace(config.TriggerId))
                    throw new InvalidOperationException(
                        $"Tutorial chapter '{config.Id}' has no trigger id; a chapter nothing can trigger would never run.");

                chapters.Add(new TutorialChapterDefinition(
                    chapterId,
                    new TutorialTriggerId(config.TriggerId),
                    ToStepIds(config.StepIds, config.Id, nameof(TutorialConfig.StepIds)),
                    ToStepIds(config.CheckpointStepIds, config.Id, nameof(TutorialConfig.CheckpointStepIds)),
                    config.Priority,
                    _requirements?.GetRequirement(chapterId)));
            }

            return chapters;
        }

        private static TutorialStepId[] ToStepIds(string[] values, string chapterId, string field)
        {
            if (values == null)
                return Array.Empty<TutorialStepId>();

            var steps = new TutorialStepId[values.Length];
            for (var index = 0; index < values.Length; index++)
            {
                if (string.IsNullOrWhiteSpace(values[index]))
                    throw new InvalidOperationException(
                        $"Tutorial chapter '{chapterId}' has an empty entry in {field}.");

                steps[index] = new TutorialStepId(values[index]);
            }

            return steps;
        }
    }
}
