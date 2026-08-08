namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// A world position in the game's own wire encoding: Q52.12 fixed point,
    /// 4096 units to the metre, exactly what
    /// <c>Improbable.Corelibrary.Math.FixedPointVector3</c> carries.
    ///
    /// It exists as a value type in the pure-policy assembly so spawn
    /// coordinates can be written, converted and asserted on WITHOUT
    /// referencing the game's assemblies - which on Linux means without Wine
    /// and without a game install.
    ///
    /// TRUNCATION, NOT ROUNDING. The client's own encoder is
    /// <c>(long)(d * 4096)</c> (FixedPointVector3Util.cs:9-11,32-39), a C cast,
    /// which truncates toward zero. <see cref="FromMetres"/> reproduces that
    /// exactly so a coordinate derived here and a coordinate derived by the
    /// client agree to the last unit; rounding would disagree by 1 unit
    /// (0.24 mm) on roughly half of all inputs, which is harmless in space but
    /// would make any byte-for-byte comparison against captured traffic fail
    /// for reasons that look like a protocol bug.
    /// </summary>
    public readonly struct FixedPointPosition : IEquatable<FixedPointPosition>
    {
        /// <summary>Fixed-point units per metre. Q52.12, so 2^12.</summary>
        public const long UnitsPerMetre = 4096;

        public FixedPointPosition(long x, long y, long z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public long X { get; }
        public long Y { get; }
        public long Z { get; }

        /// <summary>
        /// Encodes metres the way the client does: <c>(long)(d * 4096)</c>,
        /// truncating toward zero. See the type remarks for why not rounding.
        /// </summary>
        public static FixedPointPosition FromMetres(double x, double y, double z)
        {
            return new FixedPointPosition(
                (long)(x * UnitsPerMetre),
                (long)(y * UnitsPerMetre),
                (long)(z * UnitsPerMetre));
        }

        /// <summary>The position back in metres. Lossy by up to one unit per axis; for logs and tests.</summary>
        public double MetresX => (double)X / UnitsPerMetre;

        /// <summary>The position back in metres. Lossy by up to one unit per axis; for logs and tests.</summary>
        public double MetresY => (double)Y / UnitsPerMetre;

        /// <summary>The position back in metres. Lossy by up to one unit per axis; for logs and tests.</summary>
        public double MetresZ => (double)Z / UnitsPerMetre;

        public bool Equals(FixedPointPosition other) => X == other.X && Y == other.Y && Z == other.Z;

        public override bool Equals(object? obj) => obj is FixedPointPosition other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(X, Y, Z);

        public static bool operator ==(FixedPointPosition a, FixedPointPosition b) => a.Equals(b);

        public static bool operator !=(FixedPointPosition a, FixedPointPosition b) => !a.Equals(b);

        public override string ToString()
        {
            return "{" + X + ", " + Y + ", " + Z + "} = ("
                + MetresX.ToString("0.###") + ", "
                + MetresY.ToString("0.###") + ", "
                + MetresZ.ToString("0.###") + ") m";
        }
    }
}
