using System;
using System.Globalization;
using System.Threading;
using Cysharp.Threading.Tasks;
using Foundation.Logging;
using Foundation.Reactive;

namespace Foundation.Localization
{
    public sealed class LocalizationService : ILocalizationService, ILocalizationSource, IDisposable
    {
        public const string MissingKeyPrefix = "[MISSING:";

        private readonly ILocalizationTableSource _tableSource;
        private readonly ILocalizationFormatter _formatter;
        private readonly LocalizationSettings _settings;
        private readonly IAppLogger _logger;
        private readonly ReactiveValue<LocaleId> _locale;

        // Overlapping switches (a player tapping through languages) apply in the order they were asked, so a slow table
        // for an earlier request cannot land after, and replace, the locale that was asked for last.
        private readonly SemaphoreSlim _switchGate = new(1, 1);

        private LocalizationTable _current;
        private LocalizationTable _fallback;

        public LocaleId CurrentLocale => _locale.Value;

        public IReadOnlyReactiveValue<LocaleId> Locale => _locale;

        public LocalizationSettings Settings => _settings;

        public bool IsInitialized => _current != null;

        public LocalizationService(
            ILocalizationTableSource tableSource,
            ILocalizationFormatter formatter,
            LocalizationSettings settings,
            IAppLogger logger = null)
        {
            _tableSource = tableSource ?? throw new ArgumentNullException(nameof(tableSource));
            _formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _logger = logger ?? new AppLogger(new ILogOutput[] { new UnityLogOutput() });
            _locale = new(settings.FallbackLocale);
        }

        public void Dispose() => _locale.Dispose();

        public async UniTask InitializeAsync(LocaleId locale, CancellationToken cancellationToken = default)
        {
            if (!await SetLocaleAsync(locale, cancellationToken)
                && !await SetLocaleAsync(_settings.FallbackLocale, cancellationToken))
            {
                throw new InvalidOperationException(
                    $"Localization could not load the fallback locale '{_settings.FallbackLocale}'.");
            }
        }

        public async UniTask<bool> SetLocaleAsync(LocaleId locale, CancellationToken cancellationToken = default)
        {
            if (!_settings.TryResolveSupported(locale, out var resolved))
                return false;

            await _switchGate.WaitAsync(cancellationToken);
            try
            {
                if (_current != null && _current.Locale == resolved)
                    return true;

                LocalizationTable table;
                LocalizationTable fallback;
                try
                {
                    // Everything that can fail happens before anything is swapped, so a failed
                    // switch leaves the running locale untouched.
                    table = await _tableSource.LoadAsync(resolved, cancellationToken);
                    if (table == null)
                        return false;

                    fallback = resolved == _settings.FallbackLocale
                        ? null
                        : _fallback ?? await _tableSource.LoadAsync(_settings.FallbackLocale, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    // The switch is refused, not lost: the running locale stays, and the cause must not vanish with it.
                    _logger.Log(LogLevel.Warning, "Localization", $"locale '{resolved}' could not be loaded: {exception.Message}");
                    return false;
                }

                _current = table;
                _fallback = fallback;
                (_formatter as ILocalizationCultureAware)?.SetCulture(CreateCulture(resolved));
                _locale.Value = resolved;
                return true;
            }
            finally
            {
                _switchGate.Release();
            }
        }

        public string GetTemplate(LocalizationKey key)
        {
            if (_current != null && _current.TryGet(key, out var template))
                return template;

            if (_fallback != null && _fallback.TryGet(key, out var fallbackTemplate))
                return fallbackTemplate;

            return MissingKeyPrefix + key.Value + "]";
        }

        public string Format(LocalizationKey key, LocalizationArguments arguments)
        {
            var template = GetTemplate(key);
            return arguments.IsEmpty ? template : _formatter.Format(template, arguments.Values);
        }

        UniTask<string> ILocalizationSource.LoadTemplateAsync(string key, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return UniTask.FromResult(GetTemplate(new LocalizationKey(key)));
        }

        private static CultureInfo CreateCulture(LocaleId locale)
        {
            try
            {
                return CultureInfo.GetCultureInfo(locale.Value);
            }
            catch (CultureNotFoundException)
            {
                return CultureInfo.InvariantCulture;
            }
        }
    }
}
