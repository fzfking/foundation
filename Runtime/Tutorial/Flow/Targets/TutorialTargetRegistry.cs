using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Foundation.Tutorial.Flow
{
    public interface ITutorialTarget
    {
        string Id { get; }
        RectTransform Rect { get; }

        bool IsAvailable { get; }

        event Action Clicked;
    }

    public interface ITutorialTargetRegistry
    {
        /// <summary>The most recently registered available target with <paramref name="id"/>.</summary>
        bool TryGet(string id, out ITutorialTarget target);

        UniTask<ITutorialTarget> WaitForAsync(string id, CancellationToken cancellationToken = default);
    }

    public sealed class TutorialTargetRegistry : ITutorialTargetRegistry
    {
        private readonly Dictionary<string, List<ITutorialTarget>> _targets = new(StringComparer.Ordinal);

        public void Register(ITutorialTarget target)
        {
            if (target == null || string.IsNullOrEmpty(target.Id))
                return;

            if (!_targets.TryGetValue(target.Id, out var list))
            {
                list = new List<ITutorialTarget>(1);
                _targets.Add(target.Id, list);
            }

            if (!list.Contains(target))
                list.Add(target);
        }

        public void Unregister(ITutorialTarget target, string id)
        {
            if (target == null || string.IsNullOrEmpty(id) || !_targets.TryGetValue(id, out var list))
                return;

            list.Remove(target);
            if (list.Count == 0)
                _targets.Remove(id);
        }

        public bool TryGet(string id, out ITutorialTarget target)
        {
            target = null;
            if (string.IsNullOrEmpty(id) || !_targets.TryGetValue(id, out var list))
                return false;

            for (var index = list.Count - 1; index >= 0; index--)
            {
                if (!list[index].IsAvailable)
                    continue;

                target = list[index];
                return true;
            }

            return false;
        }

        public async UniTask<ITutorialTarget> WaitForAsync(string id, CancellationToken cancellationToken = default)
        {
            // Availability also depends on activeInHierarchy, which raises no event, so poll once per
            // frame; the lookup is a single dictionary hit.
            while (true)
            {
                if (TryGet(id, out var target))
                    return target;

                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }
        }
    }
}
