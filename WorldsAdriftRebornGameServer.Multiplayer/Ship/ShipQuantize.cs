using System;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship
{
    /// <summary>
    /// The float &lt;-&gt; sbyte quantization the client uses for every geometry
    /// value inside a ShipPlan blob. A verbatim port of the two methods the
    /// decompiled client serialises hull geometry with:
    ///
    ///   MathUtils.SerializeFloat(v, range)   = (sbyte)Mathf.RoundToInt(Mathf.Clamp(v / range, -1, 1) * 127)
    ///   MathUtils.DeserializeFloat(v, range) = (float)v / 127f * range
    ///
    /// (acs/Assets.Scripts.Utils/MathUtils.cs:513-521 in the decompile.)
    ///
    /// The pair is byte-stable: DeserializeFloat then SerializeFloat returns the
    /// original sbyte for every value SerializeFloat can emit, because Serialize
    /// clamps to [-1,1]*127 and so never produces the one value (-128) that would
    /// not survive the round trip. That property is what lets ShipPlanModel store
    /// decoded geometry as plain floats and still re-encode byte-for-byte.
    ///
    /// Ranges baked into the client: position x = 16, y = 1.7, z = 2; curve offset = 1.
    /// </summary>
    internal static class ShipQuantize
    {
        internal const float RangeX = 16f;
        internal const float RangeY = 1.7f;
        internal const float RangeZ = 2f;
        internal const float RangeCurve = 1f;

        internal static sbyte SerializeFloat(float v, float range)
        {
            float clamped = Clamp(v / range, -1f, 1f) * 127f;
            return (sbyte)RoundToInt(clamped);
        }

        internal static float DeserializeFloat(sbyte v, float range)
        {
            return (float)v / 127f * range;
        }

        private static float Clamp(float value, float min, float max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        /// <summary>
        /// UnityEngine.Mathf.RoundToInt: round-half-to-even (banker's rounding),
        /// matching (int)Math.Round(f) with MidpointRounding.ToEven. None of the
        /// stock geometry values are exact half-way cases, but ports of this
        /// function have been bitten before by assuming round-half-away, so it is
        /// spelled out.
        /// </summary>
        private static int RoundToInt(float f)
        {
            return (int)Math.Round(f, MidpointRounding.ToEven);
        }
    }
}
