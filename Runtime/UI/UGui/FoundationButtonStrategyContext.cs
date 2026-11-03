using System;
using System.Collections.Generic;
using Foundation.Content;
using Foundation.Motion;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Foundation.UI.UGui
{
    public sealed class FoundationButtonStrategyContext : IDisposable
    {
        private readonly Dictionary<object, IDisposable> _assetHandles = new();
        private readonly Dictionary<object, object> _runtimes = new();
        private readonly FoundationButton _button;
        private Graphic[] _graphics;
        private Image[] _images;
        private Transform[] _directChildren;
        private string[] _imageNames;
        private string[] _directChildNames;
        private bool _disposed;

        public FoundationButton Button => _button;

        public IAssetProvider Assets { get; private set; }

        /// <summary>The scope's motion service, or null when none is registered. A strategy that tweens (the scale and color ones, when their duration is above zero) calls <see cref="RequireMotion"/>.</summary>
        public IMotionService Motion { get; private set; }

        // Arrays, not IReadOnlyList: a foreach over the interface boxes an enumerator on every state change.
        public Graphic[] Graphics => _graphics ??= _button.GetComponentsInChildren<Graphic>(true);

        public Image[] Images => _images ??= _button.GetComponentsInChildren<Image>(true);

        public Transform[] DirectChildren => _directChildren ??= GetDirectChildren();

        /// <summary>The names of <see cref="Images"/>, read once: <c>Object.name</c> allocates a string on every access.</summary>
        public string[] ImageNames => _imageNames ??= GetNames(Images);

        /// <summary>The names of <see cref="DirectChildren"/>, read once for the same reason.</summary>
        public string[] DirectChildNames => _directChildNames ??= GetNames(DirectChildren);

        public FoundationButtonStrategyContext(FoundationButton button)
        {
            _button = button ?? throw new ArgumentNullException(nameof(button));
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            ClearStateAssets();
            foreach (var runtime in _runtimes.Values)
            {
                if (runtime is IDisposable disposable)
                    disposable.Dispose();
            }

            _runtimes.Clear();
        }

        public void SetAssets(IAssetProvider assets)
        {
            Assets = assets;
        }

        /// <summary>
        /// True once the button's scope has been bound (<see cref="SetMotion"/>). A button applies its first state in
        /// <c>Awake</c>, before that, and applies it again when the scope arrives; a strategy that tweens sets its end
        /// state at once while this is false, so a freshly instantiated button neither fails nor animates on spawn.
        /// </summary>
        public bool ScopeBound { get; private set; }

        public void SetMotion(IMotionService motion)
        {
            Motion = motion;
            ScopeBound = true;
        }

        /// <summary>The motion service for a strategy that tweens; fails with the missing registration named, rather than animating on a hidden default.</summary>
        public IMotionService RequireMotion()
            => Motion ?? throw new InvalidOperationException("This button strategy tweens, so an IMotionService has to be registered in the scope (for example with LitMotionInstaller).");

        public bool IsCurrentState(string stateId)
            => string.Equals(_button.CurrentStateId, stateId, StringComparison.Ordinal);

        public T GetRuntime<T>(object owner, Func<T> factory)
            where T : class
        {
            if (!_runtimes.TryGetValue(owner, out var value))
            {
                value = factory();
                _runtimes.Add(owner, value);
            }

            return (T)value;
        }

        /// <summary><see cref="GetRuntime{T}"/> whose factory gets <paramref name="argument"/>, so it can be a static lambda that allocates no closure on the calls after the first.</summary>
        public T GetRuntime<T, TArgument>(object owner, TArgument argument, Func<TArgument, T> factory)
            where T : class
        {
            if (!_runtimes.TryGetValue(owner, out var value))
            {
                value = factory(argument);
                _runtimes.Add(owner, value);
            }

            return (T)value;
        }

        public void ReplaceAssetHandle(object owner, IDisposable handle)
        {
            if (_assetHandles.TryGetValue(owner, out var previous))
                previous.Dispose();

            _assetHandles[owner] = handle ?? throw new ArgumentNullException(nameof(handle));
        }

        public void ClearStateAssets()
        {
            foreach (var handle in _assetHandles.Values)
                handle.Dispose();
            _assetHandles.Clear();
        }

        private static string[] GetNames(Object[] objects)
        {
            var names = new string[objects.Length];
            for (var index = 0; index < names.Length; index++)
                names[index] = objects[index] != null ? objects[index].name : null;
            return names;
        }

        private Transform[] GetDirectChildren()
        {
            var result = new Transform[_button.transform.childCount];
            for (var index = 0; index < result.Length; index++)
                result[index] = _button.transform.GetChild(index);
            return result;
        }
    }
}
