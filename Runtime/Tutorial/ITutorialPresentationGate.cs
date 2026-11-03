namespace Foundation.Tutorial
{
    /// <summary>
    /// Lets a full-screen presentation elsewhere (a transition effect, a takeover popup) hold
    /// <see cref="TutorialDirector"/> back from starting the next chapter until it is done. Checked only when a
    /// chapter is about to start; <see cref="TutorialDirector.Raise"/> still queues at once.
    /// </summary>
    public interface ITutorialPresentationGate
    {
        bool IsSuppressed { get; }
    }
}
