using System.Threading;
using Cysharp.Threading.Tasks;

namespace Foundation.Localization
{
    public interface ILocalizationTableSource
    {
        UniTask<LocalizationTable> LoadAsync(LocaleId locale, CancellationToken cancellationToken = default);
    }
}
