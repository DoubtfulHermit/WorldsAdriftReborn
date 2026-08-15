using System;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight
{
    /// <summary>
    /// Client-side steering feel policy. Retail models yaw as a latched wheel:
    /// opposite input first unwinds the old lock at the normal 3 units/second,
    /// making a direction change take up to 667 ms before network/render time.
    ///
    /// Only the opposing journey back through centre is accelerated. Once the
    /// new direction is reached, retail's ordinary 3/s accumulation resumes, so
    /// small corrections remain precise. This changes values, never cadence:
    /// the client still writes 1111 at at most 20 Hz and the server retains its
    /// existing domain replication schedule.
    /// </summary>
    public static class HelmYawResponsePolicy
    {
        public const float InputThreshold = 0.1f;
        public const float LatchedThreshold = 0.15f;
        public const float ReversalSpeedPerSecond = 12f;

        public static float ApplyReversal(
            float beforeRetailUpdate,
            float afterRetailUpdate,
            float rawInput,
            float deltaSeconds)
        {
            if (deltaSeconds <= 0f
                || Math.Abs(rawInput) <= InputThreshold
                || Math.Abs(beforeRetailUpdate) <= LatchedThreshold
                || Math.Sign(rawInput) == Math.Sign(beforeRetailUpdate))
            {
                return Clamp(afterRetailUpdate);
            }

            return Clamp(beforeRetailUpdate
                + rawInput * ReversalSpeedPerSecond * deltaSeconds);
        }

        private static float Clamp(float value)
        {
            if (value < -1f) return -1f;
            if (value > 1f) return 1f;
            return value;
        }
    }
}
