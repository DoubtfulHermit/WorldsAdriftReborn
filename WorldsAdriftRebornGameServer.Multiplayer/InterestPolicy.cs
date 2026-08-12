using System;
using System.Collections.Generic;
using System.Globalization;

namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// WHETHER connect-time spatial interest is armed, HOW BIG the interest radius
    /// is, and WHICH world entities fall inside a given player's radius - the pure
    /// policy half of the P0-8 streaming fix (findings-open-systems-audit).
    ///
    /// WHY THIS EXISTS. The server currently seeds EVERY world entity into EVERY
    /// joining peer's spawn plan, so the whole world is dumped on each client at
    /// connect. The decompiled client instantiates each checked-out entity
    /// SYNCHRONOUSLY on the main thread (UnityPrefabFactory.Instantiate), throttled
    /// to a ~100 ms/frame budget, and every entity NOT named in the shipped
    /// 190000 EntityLoadingControl set streams in AFTER the loading screen has
    /// already faded. A big connect-time burst therefore hitches the main thread
    /// for the host and, for a JOINER who checks out the whole accumulated world at
    /// once, drives native allocation until the OS kills the process (the observed
    /// second-player crash). Real SpatialOS streamed each client only the entities
    /// near it. This restores that: a peer is only told about world entities within
    /// <see cref="RadiusMetresFrom"/> metres of its position, so the world can hold
    /// an unlimited number of resource nodes while any one client only ever loads
    /// the nearby set.
    ///
    /// FAIL SAFE + OPT-IN. Radius 0 (unset, empty, unparsable or non-positive) means
    /// "no gating" - byte-for-byte the old all-entities behaviour - so an operator
    /// who never sets the flag is unaffected and a nonsense value can neither shrink
    /// the world to nothing nor throw the server down at boot.
    ///
    /// PURE. No ENet, no Improbable types, no wall clock. Distance is computed in
    /// METRES as doubles: an interest radius is a coarse gameplay gate, so sub-metre
    /// exactness is irrelevant, and metre magnitudes (tens of km at most) keep the
    /// squared-distance math far inside double precision and free of the overflow a
    /// raw fixed-point (4096 units/m) product could hit.
    /// </summary>
    public static class InterestPolicy
    {
        /// <summary>
        /// The environment variable that sets the interest radius, in METRES. Unset
        /// / empty / unparsable / non-positive keeps the current all-entities
        /// behaviour, so an operator who has never heard of this flag gets exactly
        /// what they had before.
        /// </summary>
        public const string RadiusEnvVar = "WAREBORN_INTEREST_RADIUS_M";

        /// <summary>
        /// Upper clamp on the radius. A radius this large already covers any WA
        /// island cluster, so a typo of a colossal value is pinned here rather than
        /// risking a squared-distance product that leaves double's exact range. A
        /// radius at or above the world size simply means "gate nothing", which is
        /// the safe direction.
        /// </summary>
        public const double MaxRadiusMetres = 1_000_000.0;

        /// <summary>
        /// The interest radius in metres for an env value. Unset, empty, unparsable,
        /// NaN, zero or negative all fall back to 0 (disabled = send everything); a
        /// value above <see cref="MaxRadiusMetres"/> is clamped down. A perf/safety
        /// knob must never take the server down or silently empty the world.
        /// </summary>
        public static double RadiusMetresFrom(string? env)
        {
            if (!double.TryParse(env, NumberStyles.Float, CultureInfo.InvariantCulture, out double r)
                || double.IsNaN(r)
                || r <= 0.0)
            {
                return 0.0;
            }

            return r > MaxRadiusMetres ? MaxRadiusMetres : r;
        }

        /// <summary>
        /// Whether gating is armed for a radius. Any positive radius arms it; 0
        /// (the disabled sentinel from <see cref="RadiusMetresFrom"/>) does not.
        /// </summary>
        public static bool IsEnabled(double radiusMetres) => radiusMetres > 0.0;

        /// <summary>
        /// Whether <paramref name="entity"/> is within <paramref name="radiusMetres"/>
        /// of <paramref name="center"/>. A non-positive radius means gating is off, so
        /// EVERYTHING is in range (fail open - never hide the world by accident).
        /// Full 3D distance: WA islands float, so the vertical offset is real, but at
        /// connect every entity sits at its surface position so this is simply the
        /// straight-line distance from the player's spawn point.
        /// </summary>
        public static bool InRange(FixedPointPosition center, FixedPointPosition entity, double radiusMetres)
        {
            if (radiusMetres <= 0.0)
            {
                return true;
            }

            double dx = center.MetresX - entity.MetresX;
            double dy = center.MetresY - entity.MetresY;
            double dz = center.MetresZ - entity.MetresZ;

            return (dx * dx) + (dy * dy) + (dz * dz) <= radiusMetres * radiusMetres;
        }

        /// <summary>
        /// Partitions <paramref name="items"/> into the ones inside the radius and the
        /// ones outside it, preserving order. The counterpart used by the connect-time
        /// glue and by tests; together the two lists cover every item exactly once. A
        /// non-positive radius puts everything in the in-range list.
        /// </summary>
        public static (IReadOnlyList<T> InRange, IReadOnlyList<T> OutOfRange) Partition<T>(
            FixedPointPosition center,
            IEnumerable<T> items,
            Func<T, FixedPointPosition> position,
            double radiusMetres)
        {
            if (items == null)
            {
                throw new ArgumentNullException(nameof(items));
            }
            if (position == null)
            {
                throw new ArgumentNullException(nameof(position));
            }

            List<T> inside = new List<T>();
            List<T> outside = new List<T>();
            foreach (T item in items)
            {
                if (InRange(center, position(item), radiusMetres))
                {
                    inside.Add(item);
                }
                else
                {
                    outside.Add(item);
                }
            }

            return (inside, outside);
        }
    }
}
