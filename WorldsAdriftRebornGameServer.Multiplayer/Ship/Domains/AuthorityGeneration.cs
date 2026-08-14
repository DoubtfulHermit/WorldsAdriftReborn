using System;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship.Domains
{
    /// <summary>
    /// Monotonic epoch of a domain's authority. Commands are accepted only when
    /// their captured generation still equals the domain's current generation.
    /// </summary>
    public readonly struct AuthorityGeneration : IEquatable<AuthorityGeneration>, IComparable<AuthorityGeneration>
    {
        public AuthorityGeneration(long value)
        {
            if (value <= 0) throw new ArgumentOutOfRangeException(nameof(value), "generation must be positive");
            Value = value;
        }

        public long Value { get; }
        public static AuthorityGeneration Initial => new AuthorityGeneration(1);

        public AuthorityGeneration Next()
        {
            if (Value == long.MaxValue) throw new InvalidOperationException("authority generation exhausted");
            return new AuthorityGeneration(Value + 1);
        }

        public bool Equals(AuthorityGeneration other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is AuthorityGeneration other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public int CompareTo(AuthorityGeneration other) => Value.CompareTo(other.Value);
        public static bool operator ==(AuthorityGeneration left, AuthorityGeneration right) => left.Equals(right);
        public static bool operator !=(AuthorityGeneration left, AuthorityGeneration right) => !left.Equals(right);
        public override string ToString() => Value.ToString();
    }
}
