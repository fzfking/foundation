using System;

namespace Foundation.Tutorial
{
    public readonly struct TutorialStepId : IEquatable<TutorialStepId>
    {
        public string Value { get; }

        public TutorialStepId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Tutorial step id must not be empty.", nameof(value));

            Value = value;
        }

        public bool Equals(TutorialStepId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is TutorialStepId other && Equals(other);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value;
        public static bool operator ==(TutorialStepId left, TutorialStepId right) => left.Equals(right);
        public static bool operator !=(TutorialStepId left, TutorialStepId right) => !left.Equals(right);
    }
}
