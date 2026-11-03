using System.Threading;
using Cysharp.Threading.Tasks;
using System;

namespace Foundation.Localization
{
    public interface ILocalizationResolver
    {
        UniTask<string> ResolveAsync(
            string key,
            CancellationToken cancellationToken = default);

        UniTask<string> ResolveAsync(
            string key,
            LocalizationArguments arguments,
            CancellationToken cancellationToken = default);
    }

    public sealed class LocalizationResolver : ILocalizationResolver
    {
        private readonly ILocalizationSource _source;
        private readonly ILocalizationFormatter _formatter;

        public LocalizationResolver(
            ILocalizationSource source,
            ILocalizationFormatter formatter)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _formatter = formatter ?? throw new ArgumentNullException(nameof(formatter));
        }

        public async UniTask<string> ResolveAsync(
            string key,
            CancellationToken cancellationToken = default)
        {
            return await _source.LoadTemplateAsync(key, cancellationToken);
        }

        public async UniTask<string> ResolveAsync(
            string key,
            LocalizationArguments arguments,
            CancellationToken cancellationToken = default)
        {
            var template = await _source.LoadTemplateAsync(key, cancellationToken);
            return arguments.IsEmpty ? template : _formatter.Format(template, arguments.Values);
        }
    }
}
