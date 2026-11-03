using System;
using System.Collections.Generic;
using System.Linq;

namespace Foundation.Tutorial
{
    public sealed class TutorialRegistry
    {
        private readonly Dictionary<TutorialStepId, ITutorialStep> _steps = new();

        public TutorialRegistry(IEnumerable<ITutorialStep> steps = null)
        {
            foreach (var step in steps ?? Enumerable.Empty<ITutorialStep>())
                Register(step);
        }

        public void Register(ITutorialStep step)
        {
            if (step == null)
                throw new ArgumentNullException(nameof(step));

            if (!_steps.TryAdd(step.Id, step))
                throw new InvalidOperationException($"Tutorial step '{step.Id}' is already registered.");
        }

        public ITutorialStep Resolve(TutorialStepId id)
        {
            if (!_steps.TryGetValue(id, out var step))
                throw new InvalidOperationException($"Tutorial step '{id}' is not registered.");

            return step;
        }
    }
}
