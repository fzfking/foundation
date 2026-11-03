using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Foundation.UI.UGui
{
    [CreateAssetMenu(menuName = "Foundation/UI/Button State Strategies/Sprite")]
    public sealed class FoundationButtonStateSpriteStrategy : FoundationButtonStateStrategy
    {
        [SerializeField] private string spriteId;

        [SerializeField] private string targetImageName;

        public override async UniTask ApplyAsync(
            FoundationButtonStrategyContext context,
            string stateId,
            CancellationToken cancellationToken)
        {
            if (context.Assets == null || string.IsNullOrWhiteSpace(spriteId))
                return;

            var handle = await context.Assets.LoadAssetAsync<Sprite>(spriteId, cancellationToken);
            if (!context.IsCurrentState(stateId))
            {
                handle.Dispose();
                return;
            }

            context.ReplaceAssetHandle(this, handle);
            var matchedImage = false;
            var images = context.Images;
            var names = context.ImageNames;
            for (var index = 0; index < images.Length; index++)
            {
                if (images[index] != null && (string.IsNullOrWhiteSpace(targetImageName) || names[index] == targetImageName))
                {
                    images[index].sprite = handle.Asset;
                    matchedImage = true;
                }
            }

            if (!matchedImage)
                Debug.LogError(
                    $"Foundation button '{context.Button.name}' has no Image target for sprite strategy '{name}'. " +
                    $"Expected child '{targetImageName}'.",
                    context.Button);
        }
    }
}
