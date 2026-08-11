using System;

namespace WorldsAdriftRebornGameServer.Multiplayer.Placement
{
    /// <summary>
    /// Packs a full (w,x,y,z) quaternion into the game's 32-bit
    /// <c>Quaternion32</c> wire form, byte-for-byte the way the client's own
    /// <c>Improbable.Corelibrary.Math.Quaternion32Util.EncodeToQuaternion32</c>
    /// does (smallest-three: drop the largest component, store the two-bit index
    /// of which was dropped plus the other three at ten bits each).
    ///
    /// It lives in the pure Multiplayer assembly - a faithful re-implementation
    /// rather than a call into the client's util - for the reason everything else
    /// here does: so a placed structure's yaw can be encoded and unit-tested with
    /// no Unity, no Wine and no game install. The client decodes what this
    /// produces, so the encoding must match its decoder exactly; the round-trip is
    /// asserted in the tests.
    ///
    /// The transform seed carries a <c>Quaternion32(uint)</c>; this is the uint.
    /// <see cref="Identity"/> is the client's identity SENTINEL (1023), the value
    /// every existing world-entity seed already uses.
    /// </summary>
    public static class Quaternion32Packing
    {
        /// <summary>
        /// The identity-rotation sentinel: low ten bits all set. The client's
        /// decoder special-cases <c>(q &amp; 0x3FF) == 1023</c> as identity, and 1
        /// (a naive "unrotated") decodes to NaN and is rejected. Matches
        /// <c>ShipPartTransform.IdentityRotation</c>.
        /// </summary>
        public const uint Identity = 1023u;

        private const float OneOverRootTwo = 0.70710677f;
        private const float RootTwo = 1.4142135f;
        private const float NormalizationTolerance = 0.001f;

        /// <summary>
        /// Encodes a quaternion to the 32-bit wire form. Returns
        /// <see cref="Identity"/> for a near-identity or unusable (zero-magnitude,
        /// non-finite) rotation, so a caller can pass a client-supplied rotation
        /// straight through without a separate guard: a bad rotation becomes
        /// "unrotated", never a throw or a NaN on the wire.
        /// </summary>
        public static uint Encode(float w, float x, float y, float z)
        {
            if (!IsFinite(w) || !IsFinite(x) || !IsFinite(y) || !IsFinite(z))
            {
                return Identity;
            }

            float squareMagnitude = (w * w) + (x * x) + (y * y) + (z * z);
            if (squareMagnitude == 0f)
            {
                return Identity;
            }

            if (Math.Abs(squareMagnitude - 1f) > NormalizationTolerance)
            {
                float magnitude = (float)Math.Sqrt(squareMagnitude);
                w /= magnitude;
                x /= magnitude;
                y /= magnitude;
                z /= magnitude;
            }

            // The client treats |w| == 1 as identity; do the same so we emit its
            // sentinel rather than an all-but-degenerate smallest-three encoding.
            if (w == 1f || w == -1f)
            {
                return Identity;
            }

            float[] components = { w, x, y, z };
            int largest = LargestComponentIndex(components);

            // Canonicalise sign on the largest component so its reconstructed
            // (positive) square root is correct - exactly the client's step.
            if (components[largest] < 0f)
            {
                for (int i = 0; i < 4; i++)
                {
                    components[i] = -components[i];
                }
            }

            uint packed = (uint)(largest << 30);
            int shift = 20;
            for (int i = 0; i < 4; i++)
            {
                if (i != largest)
                {
                    packed |= To10Bits(components[i]) << shift;
                    shift -= 10;
                }
            }

            return packed;
        }

        /// <summary>
        /// Decodes a 32-bit wire quaternion back to (w,x,y,z). Present for the
        /// round-trip test and for logs; the game never needs the server to
        /// decode.
        /// </summary>
        public static (float W, float X, float Y, float Z) Decode(uint packed)
        {
            if ((packed & 0x3FFu) == 1023u)
            {
                return (1f, 0f, 0f, 0f);
            }

            float[] components = new float[4];
            uint largest = (packed >> 30) & 3u;
            float sumOfSquares = 0f;
            int shift = 20;
            for (uint i = 0; i < 4; i++)
            {
                if (i != largest)
                {
                    uint component = (packed >> shift) & 0x3FEu;
                    components[i] = ToFloatComponent(component);
                    sumOfSquares += components[i] * components[i];
                    shift -= 10;
                }
            }

            components[largest] = (float)Math.Sqrt(1f - sumOfSquares);
            return (components[0], components[1], components[2], components[3]);
        }

        private static int LargestComponentIndex(float[] components)
        {
            int result = 0;
            float max = Math.Abs(components[0]);
            for (int i = 1; i < 4; i++)
            {
                float magnitude = Math.Abs(components[i]);
                if (magnitude > max)
                {
                    result = i;
                    max = magnitude;
                }
            }

            return result;
        }

        private static uint To10Bits(float component)
        {
            float scaled = Clamp01(0.5f + (OneOverRootTwo * component));
            // Unity's Mathf.RoundToInt is round-half-to-even.
            return (uint)Math.Round(scaled * 1022f, MidpointRounding.ToEven);
        }

        private static float ToFloatComponent(uint component)
        {
            float scaled = component / 1022f;
            return (scaled - 0.5f) * RootTwo;
        }

        private static float Clamp01(float value)
        {
            if (value < 0f)
            {
                return 0f;
            }

            return value > 1f ? 1f : value;
        }

        private static bool IsFinite(float f)
        {
            return !float.IsInfinity(f) && !float.IsNaN(f);
        }
    }
}
