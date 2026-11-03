using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Foundation.UI.UGui
{
    [CreateAssetMenu(menuName = "Foundation/UI/Button State Strategies/Object Switch")]
    public sealed class FoundationButtonStateObjectSwitchStrategy : FoundationButtonStateStrategy
    {
        [SerializeField] private string activeObjectName;

        public override UniTask ApplyAsync(
            FoundationButtonStrategyContext context,
            string stateId,
            CancellationToken cancellationToken)
        {
            var children = context.DirectChildren;
            var names = context.DirectChildNames;
            for (var index = 0; index < children.Length; index++)
            {
                if (children[index] != null)
                    children[index].gameObject.SetActive(names[index] == activeObjectName);
            }

            return UniTask.CompletedTask;
        }
    }
}
