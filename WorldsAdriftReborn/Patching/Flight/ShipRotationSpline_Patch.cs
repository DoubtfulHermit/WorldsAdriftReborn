using System;
using System.Collections.Generic;
using System.Reflection;
using Bossa.DeadReckoning;
using HarmonyLib;
using UnityEngine;
using WorldsAdriftReborn.Config;
using WorldsAdriftRebornGameServer.Multiplayer.Ship.Flight;

namespace WorldsAdriftReborn.Patching.Flight
{
    /// <summary>
    /// Gives replayed ship attitude the derivative the 1130 wire does not carry.
    ///
    /// THE DEFECT (docs/research/findings-turn-vibration.md, production section).
    /// A ShipControlPoint is {timestamp, position, rotation, velocity,
    /// fsim_id_hash} and `velocity` is LINEAR only - there is no angular-velocity
    /// field in the component. So retail's
    /// SplineInterpolator.CubicHermiteInterpolation builds POSITION from a cubic
    /// with real endpoint tangents (C1) and ATTITUDE from a bare
    /// Quaternion.SlerpUnclamped (C0). Angular velocity is piecewise constant
    /// with a kink at every 240 ms point. The attitude-rate error it produces is
    /// ~0 metres at the hull origin and is multiplied by each mounted part's
    /// lever arm before it reaches the eye - which is why the helm, wings, sails
    /// and outboard engines shake while the deck barely does, why it is invisible
    /// in straight flight, and why the 24 August millimetre-accurate POSITION
    /// probe correctly saw nothing.
    ///
    /// Neither remedy is available server-side. There is no field to put angular
    /// velocity in, and publishing faster is defeated by
    /// ControlPoint.ValidateControlPoints, which DROPS any point arriving less
    /// than SendInterval * 0.95 = 228 ms after the last one, without advancing
    /// SSPDeadReckoningVisualizer.PreviousControlPoint - so a 120 ms cadence would
    /// lose half its points and still play back at 240 ms for double the
    /// bandwidth. This is the client-side half, and it needs no schema change, no
    /// server change and no extra bytes: the missing derivative is DERIVED from
    /// control points the client has already buffered.
    ///
    /// WHY THIS TARGET. SplineInterpolator.Interpolate, not
    /// CubicHermiteInterpolation. Two reasons, both load-bearing:
    ///   * Interpolate is the only overload handed the whole control-point LIST,
    ///     which is where the neighbouring attitudes needed for a tangent live.
    ///   * CubicHermiteInterpolation is ALSO the engine of
    ///     PathFollower.ApplySplineCorrection, which drives it with rotation
    ///     DELTAS from identity rather than with world attitudes. Smoothing there
    ///     would silently reshape the recovery ramp after a buffer underrun.
    ///     Patching Interpolate leaves that path untouched by construction.
    ///
    /// WHY THIS IS NOT THE REJECTED 2026.08.24-1 TRIAL. That trial bypassed the
    /// receive-side apply thresholds by REPEATING the hull MovePosition /
    /// MoveRotation target every fixed update and forcing every "~" follower to
    /// re-apply. It compiled, it tested green, and it failed live acceptance:
    /// repeating the pose left PathFollower's PreviousSample and the Rigidbody
    /// velocity on an older sample, and the local player's contact/carry path
    /// reads exactly those, so player and ship drifted smoothly apart and then
    /// hard-corrected two or three times
    /// (docs/architecture/client-ship-motion-continuity-2026-08-24.md:33-61).
    ///
    /// This patch does none of that. It changes ONE FIELD of the ONE sample
    /// retail was already about to use, before retail uses it. The sample count,
    /// the schedule, PathFollower.Move, the apply thresholds, PreviousSample, the
    /// Rigidbody velocity and the spline-correction machinery are all bit-for-bit
    /// the stock code paths, and the value handed to them is still a real
    /// attitude on the arc between two poses the server actually sent. Nothing
    /// downstream can observe a state it would not otherwise have seen.
    ///
    /// It is also the identity on a steady turn: under a constant angular rate
    /// the squad tangents cancel and it reproduces retail's slerp exactly (see
    /// ShipRotationSplinePolicy and its tests). It acts only where the turn RATE
    /// changes, which is where the C0 kink is.
    /// </summary>
    [HarmonyPatch(typeof(SplineInterpolator), "Interpolate")]
    internal static class ShipRotationSpline_Patch
    {
        /// <summary>
        /// Fallback for ShipConfiguration.SendInterval if the asset cannot be
        /// read. Retail ships 0.24; see ShipConfiguration.SendInterval.
        /// </summary>
        private const double RetailSendInterval = 0.24;

        /// <summary>
        /// Segments shorter than half a publication interval are not ship
        /// playback. DeadReckoningSender drives the same static Interpolate over
        /// its OWN 50 ms pre-smoothed buffer
        /// (ShipConfiguration.SmoothingControlPointInterval), and that is a SEND
        /// path, not a render path. Scoping by segment length keeps this patch off
        /// it without needing to know who the caller is.
        /// </summary>
        private const double MinimumSegmentIntervalFraction = 0.5;

        /// <summary>
        /// Per-buffer attitude history slots. One live PathFollower buffer per
        /// visible ship, plus headroom; ships beyond this simply keep stock slerp.
        /// </summary>
        private const int MemorySlots = 16;

        private static readonly WeakReference[] MemoryKeys = new WeakReference[MemorySlots];
        private static readonly NeighbourMemory[] MemoryValues = new NeighbourMemory[MemorySlots];
        private static int _nextMemorySlot;

        private static double _minimumSegmentSeconds;
        private static bool _resolvedSegmentSeconds;
        private static bool _loggedFailure;
        private static bool _loggedState;
        private static bool _lastLoggedEnabled;

        /// <summary>
        /// The attitude of the control point that FELL OFF the front of a buffer.
        ///
        /// This exists because PathFollower.FixedUpdate trims the buffer with
        /// RemoveRange(0, fromIndex) after every successful interpolation, so the
        /// current segment's from-point sits at index 0 for the rest of its life
        /// and its predecessor - the point a start tangent needs - is gone. It IS
        /// present on the one frame the segment advances (at fromIndex - 1, just
        /// before the trim), which is exactly when this is captured. Nothing is
        /// invented: this only remembers a point the client really received.
        /// </summary>
        private sealed class NeighbourMemory
        {
            public bool HasSegment;
            public double SegmentTime;
            public Quaternion SegmentRotation;
            public bool SegmentReceived;

            public bool HasPrevious;
            public double PreviousTime;
            public Quaternion PreviousRotation;
        }

        private static bool Prepare()
        {
            MethodInfo target = AccessTools.Method(
                typeof(SplineInterpolator), "Interpolate");
            if (target == null)
            {
                Debug.LogWarning("[WAR][flight] SplineInterpolator.Interpolate was not"
                    + " resolvable; ship rotation smoothing skipped.");
                return false;
            }

            // The postfix binds controlPoints / t / interpolated / fromIndex by
            // name. If a future assembly renames them Harmony would bind the
            // wrong slot silently, so refuse rather than smooth the wrong value.
            ParameterInfo[] parameters = target.GetParameters();
            bool named = parameters.Length == 4
                && parameters[0].Name == "controlPoints"
                && parameters[1].Name == "t"
                && parameters[2].Name == "interpolated"
                && parameters[3].Name == "fromIndex";
            if (!named)
            {
                Debug.LogWarning("[WAR][flight] SplineInterpolator.Interpolate has an"
                    + " unexpected signature; ship rotation smoothing skipped.");
            }
            return named;
        }

        private static void Postfix(
            bool __result,
            List<ControlPoint> controlPoints,
            double t,
            ref ControlPoint interpolated,
            ref int fromIndex)
        {
            try
            {
                if (!__result || controlPoints == null || fromIndex < 0)
                {
                    return;
                }

                int toIndex = fromIndex + 1;
                if (toIndex >= controlPoints.Count)
                {
                    return;
                }

                ControlPoint from = controlPoints[fromIndex];
                ControlPoint to = controlPoints[toIndex];

                // Kept warm even while the toggle is off, so flipping it live
                // (the config file is reloaded every 5 s) starts smoothing on the
                // next segment instead of waiting for a history to rebuild.
                NeighbourMemory memory = RememberFront(controlPoints, fromIndex, from);

                if (!SmoothingEnabled())
                {
                    return;
                }

                double span = to.Timestamp - from.Timestamp;
                if (!from.Received || !to.Received || span < MinimumSegmentSeconds())
                {
                    return;
                }

                bool hasNext = false;
                SplineRotationSample next = default(SplineRotationSample);
                int nextIndex = toIndex + 1;
                if (nextIndex < controlPoints.Count)
                {
                    ControlPoint candidate = controlPoints[nextIndex];
                    if (candidate.Received)
                    {
                        hasNext = true;
                        next = Sample(candidate.Timestamp, candidate.Rotation);
                    }
                }

                bool hasPrevious = memory != null && memory.HasPrevious;
                SplineRotationSample previous = hasPrevious
                    ? Sample(memory.PreviousTime, memory.PreviousRotation)
                    : default(SplineRotationSample);

                if (!hasPrevious && !hasNext)
                {
                    return;
                }

                SplineRotationSample smoothed;
                if (!ShipRotationSplinePolicy.TrySmooth(
                        Sample(from.Timestamp, from.Rotation),
                        Sample(to.Timestamp, to.Rotation),
                        hasPrevious, previous,
                        hasNext, next,
                        (t - from.Timestamp) / span,
                        out smoothed))
                {
                    return;
                }

                interpolated.Rotation = new Quaternion(
                    (float)smoothed.X, (float)smoothed.Y,
                    (float)smoothed.Z, (float)smoothed.W);
            }
            catch (Exception e)
            {
                // Fail closed to stock slerp, once, loudly enough to find.
                if (!_loggedFailure)
                {
                    _loggedFailure = true;
                    Debug.LogWarning("[WAR][flight] ship rotation smoothing failed closed"
                        + " (once): " + e.Message);
                }
            }
        }

        /// <summary>
        /// Records this buffer's current from-point and, when the segment advances,
        /// promotes the point it replaced to the start-tangent neighbour.
        /// </summary>
        private static NeighbourMemory RememberFront(
            List<ControlPoint> controlPoints, int fromIndex, ControlPoint from)
        {
            NeighbourMemory memory = MemoryFor(controlPoints);
            if (memory == null)
            {
                return null;
            }

            if (memory.HasSegment && memory.SegmentTime == from.Timestamp)
            {
                return memory;
            }

            if (fromIndex > 0)
            {
                // The real predecessor is still in the buffer; PathFollower is
                // about to trim it away in this same FixedUpdate.
                ControlPoint predecessor = controlPoints[fromIndex - 1];
                memory.HasPrevious = predecessor.Received
                    && predecessor.Timestamp < from.Timestamp;
                memory.PreviousTime = predecessor.Timestamp;
                memory.PreviousRotation = predecessor.Rotation;
            }
            else if (memory.HasSegment
                && memory.SegmentReceived
                && memory.SegmentTime < from.Timestamp)
            {
                // Already trimmed: the point we were interpolating FROM last time
                // is the point before this one.
                memory.HasPrevious = true;
                memory.PreviousTime = memory.SegmentTime;
                memory.PreviousRotation = memory.SegmentRotation;
            }
            else
            {
                // First point on this buffer, or the timeline went backwards
                // (a clear, a re-seed, a halt recovery). No trustworthy neighbour.
                memory.HasPrevious = false;
            }

            memory.HasSegment = true;
            memory.SegmentTime = from.Timestamp;
            memory.SegmentRotation = from.Rotation;
            memory.SegmentReceived = from.Received;
            return memory;
        }

        /// <summary>
        /// The history for one buffer, keyed on the LIST INSTANCE.
        ///
        /// A PathFollower allocates _processedControlPoints once in its field
        /// initialiser and only ever Clear()s it, so the reference is a stable
        /// per-follower identity that needs no reflection into private fields and
        /// cannot keep a dead follower alive - the keys are weak.
        /// </summary>
        private static NeighbourMemory MemoryFor(List<ControlPoint> controlPoints)
        {
            int free = -1;
            for (int i = 0; i < MemorySlots; i++)
            {
                WeakReference key = MemoryKeys[i];
                if (key == null)
                {
                    if (free < 0) free = i;
                    continue;
                }
                object target = key.Target;
                if (ReferenceEquals(target, controlPoints))
                {
                    return MemoryValues[i];
                }
                if (target == null && free < 0)
                {
                    free = i;
                }
            }

            int slot = free >= 0 ? free : _nextMemorySlot;
            _nextMemorySlot = (_nextMemorySlot + 1) % MemorySlots;
            MemoryKeys[slot] = new WeakReference(controlPoints);
            MemoryValues[slot] = new NeighbourMemory();
            return MemoryValues[slot];
        }

        private static SplineRotationSample Sample(double time, Quaternion rotation)
        {
            return new SplineRotationSample(
                time, rotation.x, rotation.y, rotation.z, rotation.w);
        }

        /// <summary>
        /// Half of the live ShipConfiguration.SendInterval. Read from the asset
        /// rather than hardcoded so that lowering SendInterval - the other client
        /// option in the findings document - does not silently switch this off.
        /// </summary>
        private static double MinimumSegmentSeconds()
        {
            if (_resolvedSegmentSeconds)
            {
                return _minimumSegmentSeconds;
            }

            try
            {
                ShipConfiguration config = ShipConfiguration.Instance;
                if (config != null && config.SendInterval > 0.0)
                {
                    _minimumSegmentSeconds =
                        config.SendInterval * MinimumSegmentIntervalFraction;
                    _resolvedSegmentSeconds = true;
                    return _minimumSegmentSeconds;
                }
            }
            catch (Exception)
            {
                // Resources.Load can be unavailable this early. Fall through to
                // the retail value WITHOUT caching, so the asset is read properly
                // once it exists.
            }

            return RetailSendInterval * MinimumSegmentIntervalFraction;
        }

        /// <summary>
        /// The [Flight] Flight_SmoothShipRotation toggle, re-read every call so a
        /// live edit takes effect on the next segment - WAConfig_Patch reloads the
        /// mod config from disk every 5 s, which makes this A/B-able mid-flight
        /// rather than per-launch. Missing config fails to stock behaviour.
        /// </summary>
        private static bool SmoothingEnabled()
        {
            bool enabled = ModSettings.smoothShipRotation != null
                && ModSettings.smoothShipRotation.Value;

            if (!_loggedState || enabled != _lastLoggedEnabled)
            {
                _loggedState = true;
                _lastLoggedEnabled = enabled;
                Debug.Log("[WAR][flight] ship attitude spline smoothing is "
                    + (enabled ? "ON" : "OFF")
                    + " ([Flight] Flight_SmoothShipRotation in WorldsAdriftReborn.cfg;"
                    + " re-read live every 5 s).");
            }

            return enabled;
        }
    }
}
