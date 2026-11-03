using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Events;

namespace Foundation.UI.UGui
{
    [Serializable]
    public sealed class FoundationButtonStateDefinition
    {
        [SerializeField] private string id = "normal";

        [SerializeField] private bool interactable = true;

        [SerializeField] private string localizationKey;

        [SerializeField] private LocalizedTextArgument[] localizationArguments = Array.Empty<LocalizedTextArgument>();

        [SerializeField] private UnityEvent entered = new();

        [SerializeField] private UnityEvent exited = new();

        [SerializeField]
        private InterfaceReference<IFoundationButtonStateStrategy>[] strategies =
            Array.Empty<InterfaceReference<IFoundationButtonStateStrategy>>();

        public string Id => id;
        public bool Interactable => interactable;
        public string LocalizationKey => localizationKey;
        public LocalizedTextArgument[] LocalizationArguments => localizationArguments;
        public UnityEvent Entered => entered;
        public UnityEvent Exited => exited;
        public InterfaceReference<IFoundationButtonStateStrategy>[] Strategies => strategies;
    }

    public interface IFoundationButtonStateStrategy
    {
        UniTask ApplyAsync(
            FoundationButtonStrategyContext context,
            string stateId,
            CancellationToken cancellationToken);
    }
}
