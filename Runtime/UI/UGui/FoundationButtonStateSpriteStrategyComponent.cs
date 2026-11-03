using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace Foundation.UI.UGui
{
    [AddComponentMenu("UI (Canvas)/Foundation Button State Sprite Strategy")]
    public sealed class FoundationButtonStateSpriteStrategyComponent : MonoBehaviour, IFoundationButtonStateStrategy
    {
        [SerializeField] private string spriteId;

        [SerializeField] private Image targetImage;

        public async UniTask ApplyAsync(
            FoundationButtonStrategyContext context,
            string stateId,
            CancellationToken cancellationToken)
        {
            if (context.Assets == null || string.IsNullOrWhiteSpace(spriteId) || targetImage == null)
                return;

            var handle = await context.Assets.LoadAssetAsync<Sprite>(spriteId, cancellationToken);
            if (!context.IsCurrentState(stateId))
            {
                handle.Dispose();
                return;
            }

            context.ReplaceAssetHandle(this, handle);
            targetImage.sprite = handle.Asset;
        }
    }
}
