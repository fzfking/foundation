using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Foundation.UI.UGui
{
    public abstract class UITabSelectionVisual : MonoBehaviour
    {
        public abstract UniTask PlayAsync(bool selected, CancellationToken cancellationToken = default);
    }
}
