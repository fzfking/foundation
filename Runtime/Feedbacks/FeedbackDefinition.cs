using System;
using System.Collections.Generic;
using UnityEngine;

namespace Foundation.Feedbacks
{
    public enum FeedbackRepeatPolicy { Restart, IgnoreWhilePlaying }

    [Serializable]
    public sealed class FeedbackStep
    {
        [SerializeField, Min(0f)] private float delay;
        [SerializeField] private FeedbackAction action;

        public float Delay => delay;

        public FeedbackAction Action => action;

        public FeedbackStep(float delay, FeedbackAction action)
        {
            this.delay = delay;
            this.action = action;
        }
    }

    [CreateAssetMenu(menuName = "Foundation/Feedbacks/Definition", fileName = "Feedback")]
    public sealed class FeedbackDefinition : ScriptableObject
    {
        [SerializeField] private FeedbackId id;
        [SerializeField] private FeedbackRepeatPolicy repeatPolicy = FeedbackRepeatPolicy.Restart;
        [SerializeField] private List<FeedbackStep> steps = new();

        public FeedbackId Id => id;
        public FeedbackRepeatPolicy RepeatPolicy => repeatPolicy;
        public IReadOnlyList<FeedbackStep> Steps => steps;

        public static FeedbackDefinition Create(FeedbackId feedbackId, IEnumerable<FeedbackStep> actions,
            FeedbackRepeatPolicy policy = FeedbackRepeatPolicy.Restart)
        {
            var definition = CreateInstance<FeedbackDefinition>();
            definition.id = feedbackId;
            definition.repeatPolicy = policy;
            definition.steps.AddRange(actions);
            return definition;
        }
    }

}
