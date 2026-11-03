using System;

namespace Foundation.Localization
{
    public readonly struct LocaleId : IEquatable<LocaleId>
    {
        public string Value { get; }

        public bool IsEmpty => string.IsNullOrEmpty(Value);

        public LocaleId Language
        {
            get
            {
                var separator = Value.IndexOf('-');
                return separator <= 0 ? this : new LocaleId(Value[..separator]);
            }
        }

        public bool HasRegion => Value.IndexOf('-') > 0;

        public LocaleId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Locale id must not be empty.", nameof(value));

            Value = value.Trim();
        }

        public bool Equals(LocaleId other) => string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);

        public override bool Equals(object obj) => obj is LocaleId other && Equals(other);

        public override int GetHashCode() =>
            Value == null ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(Value);

        public override string ToString() => Value ?? string.Empty;

        public static bool operator ==(LocaleId left, LocaleId right) => left.Equals(right);

        public static bool operator !=(LocaleId left, LocaleId right) => !left.Equals(right);
    }
}
