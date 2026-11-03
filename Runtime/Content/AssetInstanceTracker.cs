using System;
using UnityEngine;

namespace Foundation.Content
{
    internal sealed class AssetInstanceTracker : MonoBehaviour
    {
        private Action _onDestroy;
        private bool _invoked;

        private void Awake()
        {
            hideFlags = HideFlags.HideInInspector;
        }

        private void OnDestroy()
        {
            if (_invoked)
                return;

            _invoked = true;
            _onDestroy?.Invoke();
            _onDestroy = null;
        }

        public void Initialize(Action onDestroy)
        {
            _onDestroy = onDestroy;
        }

        public void ClearCallback()
        {
            _onDestroy = null;
        }
    }
}
