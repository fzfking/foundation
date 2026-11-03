using System;

namespace Foundation.Badges
{
    public enum BadgeKind { None, Dot, Count, New }
    public enum BadgeAggregation { None, Any, Sum, Max }

    public readonly struct BadgeValue : IEquatable<BadgeValue>
    {
        public static BadgeValue None => default;

        public static BadgeValue Dot => new(BadgeKind.Dot);

        public static BadgeValue New => new(BadgeKind.New);

        public BadgeKind Kind { get; }
        public int Count { get; }

        public bool IsActive => Kind != BadgeKind.None;

        public BadgeValue(BadgeKind kind, int count = 0)
        {
            Kind = kind == BadgeKind.Count && count <= 0 ? BadgeKind.None : kind;
            Count = Kind == BadgeKind.Count ? count : 0;
        }

        public static BadgeValue FromCount(int count) => new(BadgeKind.Count, count);

        public bool Equals(BadgeValue other) => Kind == other.Kind && Count == other.Count;

        public override bool Equals(object obj) => obj is BadgeValue other && Equals(other);

        public override int GetHashCode() => HashCode.Combine((int)Kind, Count);

        public static bool operator ==(BadgeValue left, BadgeValue right) => left.Equals(right);

        public static bool operator !=(BadgeValue left, BadgeValue right) => !left.Equals(right);

        public override string ToString() => Kind == BadgeKind.Count ? Count.ToString() : Kind.ToString();
    }
}
