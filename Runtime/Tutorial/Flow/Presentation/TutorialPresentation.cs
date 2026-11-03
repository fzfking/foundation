using System.Threading;
using Cysharp.Threading.Tasks;
using Foundation.UI;

namespace Foundation.Tutorial.Flow
{
    public sealed class TutorialDialogueRequest
    {
        public string TextKey;

        /// <summary>Null keeps the presenter's default speaker.</summary>
        public string SpeakerKey;
        public bool WaitForContinue = true;
        public ITutorialTarget Anchor;

        /// <summary>
        /// With no explicit <see cref="Anchor"/>: sit next to the focused element and follow it; with nothing
        /// focused the popup is docked.
        /// </summary>
        public bool FollowFocus = true;
        public TooltipSide Side = TooltipSide.Auto;
        public bool ShowArrow = true;
    }

    public sealed class TutorialFocusRequest
    {
        public ITutorialTarget Target;
        /// <summary>Extra space around the target inside the spotlight; null uses the presenter default.</summary>
        public float? Padding;
        public bool ShowHand = true;
    }

    public interface ITutorialOverlay
    {
        /// <summary>Opens the overlay if needed. False when it could not be shown.</summary>
        UniTask<bool> OpenAsync(CancellationToken cancellationToken = default);
        void SetVisible(bool visible);
        void Close();
    }

    public interface ITutorialDialoguePresenter
    {
        UniTask ShowAsync(TutorialDialogueRequest request, CancellationToken cancellationToken = default);
    }

    public interface ITutorialFocusPresenter
    {
        ITutorialTarget Focused { get; }
        void Focus(TutorialFocusRequest request);
        void ClearFocus();
    }
}
