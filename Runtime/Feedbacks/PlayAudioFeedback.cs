using System.Threading;
using Foundation.Audio;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Foundation.Feedbacks
{
    [CreateAssetMenu(menuName = "Foundation/Feedbacks/Actions/Audio")]
    public sealed class PlayAudioFeedback : FeedbackAction
    {
        [SerializeField] private AudioEventId audioEvent;

        public AudioEventId AudioEvent => audioEvent;

        public static PlayAudioFeedback Create(AudioEventId id)
        {
            var action = CreateInstance<PlayAudioFeedback>();
            action.audioEvent = id;
            return action;
        }

        public override UniTask PlayAsync(FeedbackContext context, FeedbackServices services, CancellationToken cancellationToken)
        {
            if (!cancellationToken.IsCancellationRequested && !audioEvent.IsEmpty)
                services.Audio?.Play(audioEvent);
            return UniTask.CompletedTask;
        }
    }
}
