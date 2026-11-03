using System.Collections.Generic;

namespace Foundation.Localization
{
    public interface ILocalizationFormatter
    {
        string Format(
            string template,
            IReadOnlyDictionary<string, object> arguments);
    }
}
