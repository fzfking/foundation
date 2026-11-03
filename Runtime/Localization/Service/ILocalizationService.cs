using System.Globalization;
using System.Threading;
using Cysharp.Threading.Tasks;
using Foundation.Reactive;

namespace Foundation.Localization
{
    public interface ILocalizationService
    {
        LocaleId CurrentLocale { get; }

        IReadOnlyReactiveValue<LocaleId> Locale { get; }

        LocalizationSettings Settings { get; }

        UniTask InitializeAsync(LocaleId locale, CancellationToken cancellationToken = default);

        UniTask<bool> SetLocaleAsync(LocaleId locale, CancellationToken cancellationToken = default);

        string GetTemplate(LocalizationKey key);

        string Format(LocalizationKey key, LocalizationArguments arguments);
    }

    public interface ILocalizationCultureAware
    {
        void SetCulture(CultureInfo culture);
    }
}
