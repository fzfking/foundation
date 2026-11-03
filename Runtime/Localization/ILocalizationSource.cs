using System.Threading;
using Cysharp.Threading.Tasks;

namespace Foundation.Localization
{
    public interface ILocalizationSource
    {
        UniTask<string> LoadTemplateAsync(
            string key,
            CancellationToken cancellationToken = default);
    }
}
