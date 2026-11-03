namespace Foundation.TypedIds
{
    /// <summary>The syntax rules of stable id keys and a deterministic hash of them.</summary>
    public static class StableIdKey
    {
        public const int MaxLength = 128;

        /// <summary>
        /// A key is non-empty, at most <see cref="MaxLength"/> characters and made of ASCII letters, digits
        /// and <c>. _ - / :</c>, so it is safe in file names, addresses and CSV cells.
        /// </summary>
        public static bool IsValid(string key, out string reason)
        {
            if (string.IsNullOrEmpty(key))
            {
                reason = "The id is empty.";
                return false;
            }

            if (key.Length > MaxLength)
            {
                reason = $"The id is longer than {MaxLength} characters.";
                return false;
            }

            foreach (var symbol in key)
            {
                if (IsAllowed(symbol)) 
                    continue;
                reason = $"The id contains the character '{symbol}'. Use letters, digits and . _ - / :";
                return false;
            }

            reason = null;
            return true;
        }

        public static bool IsValid(string key) => IsValid(key, out _);

        /// <summary>
        /// FNV-1a over the UTF-16 code units. Unlike <see cref="string.GetHashCode()"/> it is the same in
        /// every process and on every platform, so it can be stored and compared across sessions.
        /// </summary>
        public static int StableHash(string key)
        {
            unchecked
            {
                var hash = (uint)2166136261;
                if (key == null) 
                    return (int)hash;
                
                foreach (var symbol in key)
                    hash = (hash ^ symbol) * 16777619;

                return (int)hash;
            }
        }

        private static bool IsAllowed(char c)
            => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')
                || c == '.' || c == '_' || c == '-' || c == '/' || c == ':';
    }
}
