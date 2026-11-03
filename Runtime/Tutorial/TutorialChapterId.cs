using System;

namespace Foundation.Tutorial
{
    public readonly struct TutorialChapterId : IEquatable<TutorialChapterId>
    {
        public string Value { get; }

        public TutorialChapterId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Tutorial chapter id must not be empty.", nameof(value));

            Value = value;
        }

        public bool Equals(TutorialChapterId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is TutorialChapterId other && Equals(other);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value ?? string.Empty);
        public override string ToString() => Value;
        public static bool operator ==(TutorialChapterId left, TutorialChapterId right) => left.Equals(right);
        public static bool operator !=(TutorialChapterId left, TutorialChapterId right) => !left.Equals(right);
    }
}
