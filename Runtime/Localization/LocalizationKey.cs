using System;

namespace Foundation.Localization
{
    public readonly struct LocalizationKey : IEquatable<LocalizationKey>
    {
        public string Value { get; }

        public LocalizationKey(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Localization key must not be empty.", nameof(value));

            Value = value;
        }

        public bool Equals(LocalizationKey other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is LocalizationKey other && Equals(other);

        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);

        public override string ToString() => Value ?? string.Empty;

        public static implicit operator LocalizationKey(string value) => new(value);

        public static bool operator ==(LocalizationKey left, LocalizationKey right) => left.Equals(right);

        public static bool operator !=(LocalizationKey left, LocalizationKey right) => !left.Equals(right);
    }
}
