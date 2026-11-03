using System;

namespace Foundation.Tutorial
{
    public readonly struct TutorialTriggerId : IEquatable<TutorialTriggerId>
    {
        public string Value { get; }

        public TutorialTriggerId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Tutorial trigger id must not be empty.", nameof(value));

            Value = value;
        }

        public bool Equals(TutorialTriggerId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is TutorialTriggerId other && Equals(other);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value ?? string.Empty);
        public override string ToString() => Value;
        public static bool operator ==(TutorialTriggerId left, TutorialTriggerId right) => left.Equals(right);
        public static bool operator !=(TutorialTriggerId left, TutorialTriggerId right) => !left.Equals(right);
    }
}
