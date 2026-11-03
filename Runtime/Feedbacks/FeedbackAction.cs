using System;
using System.Threading;
using Foundation.Audio;
using Foundation.Haptics;
using Foundation.Motion;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Foundation.Feedbacks
{
    public readonly struct FeedbackServices
    {
        public IAudioService Audio { get; }
        public IHapticsService Haptics { get; }

        /// <summary>The scope's motion service, or null when none is registered. The package's own actions never tween; an action that does calls <see cref="RequireMotion"/>.</summary>
        public IMotionService Motion { get; }

        public FeedbackServices(IAudioService audio, IHapticsService haptics, IMotionService motion = null)
        {
            Audio = audio;
            Haptics = haptics;
            Motion = motion;
        }

        /// <summary>The motion service for an action that tweens; fails with the missing registration named, rather than animating on a hidden default.</summary>
        public IMotionService RequireMotion()
            => Motion ?? throw new InvalidOperationException("This feedback action tweens, so an IMotionService has to be registered in the scope (for example with LitMotionInstaller).");
    }

    public abstract class FeedbackAction : ScriptableObject
    {
        public virtual bool RequiresTargetLifetime => false;
        public abstract UniTask PlayAsync(FeedbackContext context, FeedbackServices services, CancellationToken cancellationToken);
    }

}
