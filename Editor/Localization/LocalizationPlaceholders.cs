using System.Collections.Generic;

namespace Foundation.Content.Editor.Localization
{
    public static class LocalizationPlaceholders
    {
        public static bool TryParse(string template, out IReadOnlyList<string> names, out string error)
        {
            var found = new List<string>();
            names = found;
            error = null;
            if (string.IsNullOrEmpty(template))
                return true;

            for (var index = 0; index < template.Length; index++)
            {
                var character = template[index];
                if (character == '}')
                {
                    if (index + 1 < template.Length && template[index + 1] == '}')
                    {
                        index++;
                        continue;
                    }

                    error = $"unmatched '}}' at position {index}";
                    return false;
                }

                if (character != '{')
                    continue;

                if (index + 1 < template.Length && template[index + 1] == '{')
                {
                    index++;
                    continue;
                }

                var close = FindClosingBrace(template, index);
                if (close < 0)
                {
                    error = $"unmatched '{{' at position {index}";
                    return false;
                }

                var body = template.Substring(index + 1, close - index - 1);
                var name = ReadName(body);
                if (string.IsNullOrWhiteSpace(name))
                {
                    error = $"empty placeholder at position {index}";
                    return false;
                }

                if (!found.Contains(name))
                    found.Add(name);

                index = close;
            }

            return true;
        }

        private static int FindClosingBrace(string template, int openIndex)
        {
            var depth = 0;
            for (var index = openIndex; index < template.Length; index++)
            {
                if (template[index] == '{')
                    depth++;
                else if (template[index] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return index;
                }
            }

            return -1;
        }

        private static string ReadName(string body)
        {
            var end = body.IndexOfAny(new[] { ':', '(' });
            var name = end < 0 ? body : body.Substring(0, end);
            return name.Trim();
        }
    }
}
