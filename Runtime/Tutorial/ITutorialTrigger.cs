using System;

namespace Foundation.Tutorial
{
    public interface ITutorialTrigger : IDisposable
    {
        TutorialTriggerId Id { get; }
        void Activate(ITutorialTriggerSink sink);
    }

    public interface ITutorialTriggerSink
    {
        void Raise(TutorialTriggerId triggerId);
    }
}
