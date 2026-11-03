using System;
using System.Globalization;

namespace Foundation.Release
{
    /// <summary>
    /// A dotted client version such as "0.9.21". A missing trailing part counts as zero, so "1.2" is "1.2.0".
    /// </summary>
    public readonly struct ClientVersion : IComparable<ClientVersion>
    {
        private readonly int _major;
        private readonly int _minor;
        private readonly int _patch;

        public ClientVersion(int major, int minor, int patch)
        {
            _major = major;
            _minor = minor;
            _patch = patch;
        }

        public static bool TryParse(string text, out ClientVersion version)
        {
            version = default;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            var parts = text.Trim().Split('.');
            if (parts.Length > 3)
                return false;

            var numbers = new int[3];
            for (var i = 0; i < parts.Length; i++)
            {
                if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
                    return false;
            }

            version = new ClientVersion(numbers[0], numbers[1], numbers[2]);
            return true;
        }

        public static bool operator <(ClientVersion left, ClientVersion right) => left.CompareTo(right) < 0;

        public static bool operator >(ClientVersion left, ClientVersion right) => left.CompareTo(right) > 0;

        public static bool operator <=(ClientVersion left, ClientVersion right) => left.CompareTo(right) <= 0;

        public static bool operator >=(ClientVersion left, ClientVersion right) => left.CompareTo(right) >= 0;

        public int CompareTo(ClientVersion other)
        {
            var major = _major.CompareTo(other._major);
            if (major != 0)
                return major;

            var minor = _minor.CompareTo(other._minor);
            return minor != 0 
                ? minor 
                : _patch.CompareTo(other._patch);
        }

        public override string ToString() => _major + "." + _minor + "." + _patch;
    }
}
