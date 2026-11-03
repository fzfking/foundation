using System;
using System.Collections.Generic;
using System.Globalization;
using Foundation.Localization;
using SmartFormat;
using SmartFormat.Core.Settings;

namespace Foundation.Infrastructure.SmartFormat
{
    /// <summary>
    /// <see cref="ILocalizationFormatter"/> on SmartFormat. The service tells it the active culture through
    /// <see cref="ILocalizationCultureAware"/>, so plural and conditional forms follow the current locale.
    /// </summary>
    /// <remarks>
    /// A template that does not parse or format throws SmartFormat's <c>FormattingException</c> or <c>ParsingErrors</c>;
    /// a malformed template is a content bug that the build validation is there to catch, and a visible failure beats a
    /// silently wrong sentence. Pass <c>configure</c> to change the pipeline, for example to relax the error actions or to
    /// drop an extension.
    /// </remarks>
    public sealed class SmartFormatLocalizationFormatter : ILocalizationFormatter, ILocalizationCultureAware
    {
        private readonly SmartFormatter _formatter;
        private CultureInfo _culture = CultureInfo.InvariantCulture;

        public SmartFormatLocalizationFormatter(Action<SmartFormatter> configure = null)
        {
            _formatter = Smart.CreateDefaultSmartFormat(new SmartSettings
            {
                Formatter = { ErrorAction = FormatErrorAction.ThrowError },
                Parser = { ErrorAction = ParseErrorAction.ThrowError },
            });
            configure?.Invoke(_formatter);
        }

        public void SetCulture(CultureInfo culture) => _culture = culture ?? CultureInfo.InvariantCulture;

        public string Format(string template, IReadOnlyDictionary<string, object> arguments)
        {
            if (string.IsNullOrEmpty(template) || arguments == null || arguments.Count == 0)
                return template;

            var values = new Dictionary<string, object>(arguments.Count);
            foreach (var pair in arguments)
                values[pair.Key] = pair.Value;

            return _formatter.Format(_culture, template, values);
        }
    }
}
