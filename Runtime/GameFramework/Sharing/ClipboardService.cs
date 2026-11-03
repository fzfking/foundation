using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Foundation.GameFramework.Sharing
{
    public interface IClipboardService
    {
        UniTask SetTextAsync(string text, CancellationToken cancellationToken = default);
        UniTask<string> GetTextAsync(CancellationToken cancellationToken = default);
    }

    public sealed class ClipboardService : IClipboardService
    {
        public UniTask SetTextAsync(string text, CancellationToken cancellationToken = default)
        {
            GUIUtility.systemCopyBuffer = text ?? string.Empty;
            return UniTask.CompletedTask;
        }

        public UniTask<string> GetTextAsync(CancellationToken cancellationToken = default)
            => UniTask.FromResult(GUIUtility.systemCopyBuffer);
    }
}
