using System;

namespace Foundation.GameModules
{
    /// <summary>
    /// A game module's logical identity, what a context asks an <see cref="IGameModuleLoader"/> for. Not tied
    /// to an assembly name or Addressables address: <see cref="BuiltInGameModuleLoader"/> maps it to a
    /// factory, a HybridCLR loader to a downloaded assembly.
    /// </summary>
    public readonly struct GameModuleId : IEquatable<GameModuleId>
    {
        public string Value { get; }

        public GameModuleId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Game module id must not be empty.", nameof(value));

            Value = value;
        }

        public bool Equals(GameModuleId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is GameModuleId other && Equals(other);

        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value ?? string.Empty);

        public override string ToString() => Value ?? string.Empty;

        public static bool operator ==(GameModuleId left, GameModuleId right) => left.Equals(right);

        public static bool operator !=(GameModuleId left, GameModuleId right) => !left.Equals(right);
    }
}
