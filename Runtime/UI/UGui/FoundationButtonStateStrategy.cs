using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Foundation.UI.UGui
{
    public abstract class FoundationButtonStateStrategy : ScriptableObject, IFoundationButtonStateStrategy
    {
        public abstract UniTask ApplyAsync(
            FoundationButtonStrategyContext context,
            string stateId,
            CancellationToken cancellationToken);
    }
}
