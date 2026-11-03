using System.Threading;
using Cysharp.Threading.Tasks;

namespace Foundation.Tutorial
{
    public interface ITutorialStep
    {
        TutorialStepId Id { get; }
        UniTask ExecuteAsync(CancellationToken cancellationToken = default);
    }
}
