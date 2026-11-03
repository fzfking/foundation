using System;
using System.Collections.Generic;
using Foundation.DI;
using Foundation.Localization;
using Foundation.Logging;
using UnityEngine;

namespace Foundation.UI.UGui
{
    [DisallowMultipleComponent]
    public sealed class LocalizedText : MonoBehaviour, IServiceResolverClient
    {
        [SerializeField] private MonoBehaviour textTarget;

        [SerializeField] private string localizationKey;

        [SerializeField] private LocalizedTextArgument[] arguments = Array.Empty<LocalizedTextArgument>();

        private readonly Dictionary<string, object> _runtimeArguments = new(StringComparer.Ordinal);
        // Merged authored and runtime arguments, rebuilt in place on every refresh: the localization service
        // reads it during Format and does not keep it.
        private readonly Dictionary<string, object> _formatArguments = new(StringComparer.Ordinal);
        private IReadOnlyList<LocalizedTextArgument> _appliedArguments;
        private ILocalizedTextTarget _target;
        private ILocalizationService _localization;
        private IAppLogger _logger;
        private IDisposable _localeSubscription;
        private bool _refreshed;

        public string LocalizationKey => localizationKey;

        private void Awake()
        {
            _target = textTarget as ILocalizedTextTarget;
            if (_target == null)
                Debug.LogWarning($"LocalizedText '{name}' requires a serialized ILocalizedTextTarget.", this);

            // Bind runs at injection, which can precede Awake for an inactive object; that Refresh found no
            // target and did nothing, so catch up on first activation. Not on OnEnable: code may have
            // replaced the text since, and re-enabling must keep it.
            if (_localization != null)
                Refresh();
        }

        private void OnDestroy()
        {
            _localeSubscription?.Dispose();
            _localeSubscription = null;
        }

        public void SetTarget(ILocalizedTextTarget target)
        {
            _target = target ?? throw new ArgumentNullException(nameof(target));
            Refresh();
        }

        void IServiceResolverClient.Bind(IServiceResolver resolver)
        {
            _logger = resolver.TryResolve(out IAppLogger logger) ? logger : null;
            Bind(resolver.Resolve<ILocalizationService>());
        }

        public void Bind(ILocalizationService localization)
        {
            _localization = localization ?? throw new ArgumentNullException(nameof(localization));
            _localeSubscription?.Dispose();
            _localeSubscription = _localization.Locale.Subscribe(_ => Refresh());
            Refresh();
        }

        public void SetKey(string key)
        {
            key ??= string.Empty;
            if (string.Equals(localizationKey, key, StringComparison.Ordinal) && _refreshed)
                return;

            localizationKey = key;
            Refresh();
        }

        public void SetArguments(IReadOnlyDictionary<string, object> values)
        {
            _appliedArguments = null;
            _runtimeArguments.Clear();
            if (values != null)
            {
                foreach (var pair in values)
                    _runtimeArguments[pair.Key] = pair.Value;
            }

            Refresh();
        }

        public void SetArguments(IReadOnlyList<LocalizedTextArgument> values)
        {
            // A button applies its state's authored list on every click; the same list with no runtime change
            // in between would format the same text again.
            if (values != null && ReferenceEquals(values, _appliedArguments) && _refreshed)
                return;

            _appliedArguments = values;
            _runtimeArguments.Clear();
            if (values != null)
            {
                foreach (var argument in values)
                {
                    if (argument != null && !string.IsNullOrWhiteSpace(argument.Key))
                        _runtimeArguments[argument.Key] = argument.Value;
                }
            }

            Refresh();
        }

        public void SetArgument(string key, object value)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Localization argument key is required.", nameof(key));

            _appliedArguments = null;
            _runtimeArguments[key] = value;
            Refresh();
        }

        public void Refresh()
        {
            if (_target == null || _localization == null)
                return;

            _refreshed = true;

            if (string.IsNullOrWhiteSpace(localizationKey))
            {
                _target.SetText(string.Empty);
                return;
            }

            try
            {
                var values = BuildArguments();
                var value = values.Count == 0
                    ? _localization.GetTemplate(new LocalizationKey(localizationKey))
                    : _localization.Format(
                        new LocalizationKey(localizationKey),
                        new LocalizationArguments(values));
                _target.SetText(value);
            }
            catch (Exception exception)
            {
                if (_logger != null)
                    _logger.Log(LogLevel.Error, "Localization", $"'{name}' could not format '{localizationKey}': {exception}");
                else
                    Debug.LogException(exception, this);
                _target.SetText(_localization.GetTemplate(new LocalizationKey(localizationKey)));
            }
        }

        private Dictionary<string, object> BuildArguments()
        {
            var values = _formatArguments;
            values.Clear();
            if (arguments != null)
            {
                foreach (var argument in arguments)
                {
                    if (argument != null && !string.IsNullOrWhiteSpace(argument.Key))
                        values[argument.Key] = argument.Value;
                }
            }

            foreach (var pair in _runtimeArguments)
                values[pair.Key] = pair.Value;
            return values;
        }
    }

    public interface ILocalizedTextTarget
    {
        void SetText(string value);
    }
}
