using System;

namespace Foundation.GameContexts
{
    /// <summary>
    /// Identifies one self-contained game inside the application (main game, seasonal event, separate mode).
    /// Also the key of its <see cref="GameContextSaveProfile"/> and of the persisted "last active context",
    /// so renaming an id orphans that context's saves.
    /// </summary>
    public readonly struct GameContextId : IEquatable<GameContextId>
    {
        public string Value { get; }

        public bool IsEmpty => string.IsNullOrEmpty(Value);

        public GameContextId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Game context id must not be empty.", nameof(value));

            Value = value;
        }

        public bool Equals(GameContextId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is GameContextId other && Equals(other);

        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value ?? string.Empty);

        public override string ToString() => Value ?? string.Empty;

        public static bool operator ==(GameContextId left, GameContextId right) => left.Equals(right);

        public static bool operator !=(GameContextId left, GameContextId right) => !left.Equals(right);
    }
}
