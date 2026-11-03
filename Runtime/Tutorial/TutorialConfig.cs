using System;

namespace Foundation.Tutorial
{
    [Serializable]
    public sealed class TutorialConfig
    {
        public string Id;
        public string TriggerId;
        public int Priority;
        public string[] StepIds;
        public string[] CheckpointStepIds;
    }
}
