using System.Text;
using System;

namespace Foundation.GameFramework.Analytics.Firebase
{
    /// <summary>
    /// Firebase silently drops an event or parameter with an invalid name, so names are made valid here:
    /// letters, digits and underscores, starting with a letter, at most 40 characters, no reserved prefix.
    /// </summary>
    public static class FirebaseEventFormat
    {
        public const int MaxNameLength = 40;
        public const int MaxStringValueLength = 100;
        public const int MaxParameters = 25;

        public static string SanitizeName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "event";

            var builder = new StringBuilder(Math.Min(name.Length, MaxNameLength));
            foreach (var character in name)
            {
                if (builder.Length == MaxNameLength)
                    break;
                builder.Append(IsNameCharacter(character) ? character : '_');
            }

            var result = builder.ToString();
            if (!char.IsLetter(result[0]))
                result = Truncate("e_" + result);

            return HasReservedPrefix(result) ? Truncate("x_" + result) : result;
        }

        public static string TrimValue(string value)
            => value == null ? string.Empty : value.Length <= MaxStringValueLength ? value : value.Substring(0, MaxStringValueLength);

        private static string Truncate(string name) => name.Length <= MaxNameLength ? name : name.Substring(0, MaxNameLength);

        private static bool IsNameCharacter(char character)
            => character < 128 && (char.IsLetterOrDigit(character) || character == '_');

        private static bool HasReservedPrefix(string name)
            => name.StartsWith("firebase_", StringComparison.Ordinal)
               || name.StartsWith("google_", StringComparison.Ordinal)
               || name.StartsWith("ga_", StringComparison.Ordinal);
    }
}
