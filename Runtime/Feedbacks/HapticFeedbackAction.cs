using System.Threading;
using Foundation.Haptics;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Foundation.Feedbacks
{
    [CreateAssetMenu(menuName = "Foundation/Feedbacks/Actions/Haptic")]
    public sealed class HapticFeedbackAction : FeedbackAction
    {
        [SerializeField] private HapticFeedbackKind kind;

        public HapticFeedbackKind Kind => kind;

        public static HapticFeedbackAction Create(HapticFeedbackKind value)
        {
            var action = CreateInstance<HapticFeedbackAction>();
            action.kind = value;
            return action;
        }

        public override UniTask PlayAsync(FeedbackContext context, FeedbackServices services, CancellationToken cancellationToken)
        {
            if (!cancellationToken.IsCancellationRequested)
                services.Haptics?.Play(kind);
            return UniTask.CompletedTask;
        }
    }
}
