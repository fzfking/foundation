using Cysharp.Threading.Tasks;

namespace Foundation.Lifecycle
{
    /// <summary>
    /// The loading curtain as features see it: what to show while a world loads and how to lift it. Features
    /// inject this; only the bootstrap touches the scene object behind it.
    /// </summary>
    public interface ILoadingCurtain
    {
        /// <summary>Covers the screen again and restarts the progress, for a reload or a world change.</summary>
        void BeginReload();

        /// <summary>Hides the curtain after a transition that was abandoned; a fatal error stays on screen.</summary>
        void CancelReload();

        /// <summary>Fills the progress, then fades the curtain out.</summary>
        UniTask CompleteAsync();

        /// <summary>One equally-weighted step of the boot sequence is done.</summary>
        void AdvanceStep(string status);

        /// <summary>Sets the progress of a world change, 0 to 1 over the whole transition. For work that is not split
        /// into boot steps.</summary>
        void SetTransitionProgress(float progress);

        /// <summary>One boot step that counts as <paramref name="weight"/> steps: it takes that much of the bar.</summary>
        void AdvanceWeightedStep(int weight, string status);

        /// <summary>Progress inside a weighted boot step, 0 to 1 of that step's share of the bar.</summary>
        void SetWeightedStepFraction(float fraction, int weight, string status);

        void ShowFatalError(string title, string description);

        void ShowWaitingForConnection(string title, string description);

        void ClearWaitingForConnection();
    }
}
