using System;
using System.Collections.Generic;

namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// One entry of a client's 1011 SpawnResourcesReply, flattened to plain values so
    /// the reply-to-spawn decision can be taken and asserted on WITHOUT the game's
    /// Improbable types - which on Linux means without Wine and without a game install.
    ///
    /// The glue (the 1011 update handler) reads each
    /// <c>SpawnResourceRequest.resource</c> FabricTransform and turns it into one of
    /// these: <see cref="Metadata"/> from <c>resource.metadata</c> (the client sends the
    /// string "MetalDeposit" or "Egg", acs/IslandProxyVisualizer.cs:216/175),
    /// <see cref="X"/>/<see cref="Y"/>/<see cref="Z"/> from <c>resource.position</c>
    /// (a SpatialOS global <c>Coordinates</c>, metres - the client already remapped it
    /// to global with <c>RemapUnityVectorToGlobalCoordinates</c>, so it is the same
    /// world frame the 190602 seed uses), and <see cref="Variant"/> from the request's
    /// <c>variant</c> (the metal-deposit visuals asset id the client chose by biome,
    /// acs/IslandProxyVisualizer.cs:217).
    /// </summary>
    public readonly struct ResourceReplyItem
    {
        public ResourceReplyItem(double x, double y, double z, string? metadata, string? variant)
        {
            X = x;
            Y = y;
            Z = z;
            Metadata = metadata;
            Variant = variant;
        }

        /// <summary>World X in metres (SpatialOS global coordinates).</summary>
        public double X { get; }

        /// <summary>World Y in metres.</summary>
        public double Y { get; }

        /// <summary>World Z in metres.</summary>
        public double Z { get; }

        /// <summary>The resource kind string the client stamped on the transform ("MetalDeposit"/"Egg").</summary>
        public string? Metadata { get; }

        /// <summary>The metal-deposit visuals variant id the client selected, or null/empty.</summary>
        public string? Variant { get; }
    }

    /// <summary>
    /// What one reply batch amounted to: the placements to spawn, plus a count of each
    /// reason an item was dropped and a sample of the first position REFUSED by the
    /// island bounds. The counts exist so the server can log a reply that admitted
    /// nothing with the reason rather than a shrug - a batch rejected wholesale for being
    /// out of bounds is a coordinate-frame bug and must not read the same as a batch of
    /// duplicates.
    /// </summary>
    public readonly struct SpawnReplyOutcome
    {
        public SpawnReplyOutcome(
            IReadOnlyList<HandshakeDeposit> accepted,
            int nonMetal,
            int duplicate,
            int outOfBounds,
            ResourceReplyItem? firstOutOfBounds)
        {
            Accepted = accepted;
            NonMetal = nonMetal;
            Duplicate = duplicate;
            OutOfBounds = outOfBounds;
            FirstOutOfBounds = firstOutOfBounds;
        }

        /// <summary>The placements the caller should spawn now, in reply order.</summary>
        public IReadOnlyList<HandshakeDeposit> Accepted { get; }

        /// <summary>Items dropped for not being a MetalDeposit (eggs, mostly).</summary>
        public int NonMetal { get; }

        /// <summary>Items dropped because that exact position already carries a deposit.</summary>
        public int Duplicate { get; }

        /// <summary>Items REFUSED by <see cref="IslandBounds"/> - the coordinate-frame guard.</summary>
        public int OutOfBounds { get; }

        /// <summary>The first refused item, verbatim, so its raw metres can be logged.</summary>
        public ResourceReplyItem? FirstOutOfBounds { get; }
    }

    /// <summary>One accepted metal-deposit placement: where (world fixed point) and which visuals variant.</summary>
    public readonly struct HandshakeDeposit
    {
        public HandshakeDeposit(FixedPointPosition position, string variant)
        {
            Position = position;
            Variant = variant;
        }

        /// <summary>The 190602 TransformState.localPosition, from the client's own on-ground sample.</summary>
        public FixedPointPosition Position { get; }

        /// <summary>The 1255 MetalDepositState.variantId (never null/empty; defaulted if the client sent none).</summary>
        public string Variant { get; }
    }

    /// <summary>
    /// The PURE decision that turns a client's 1011 reply into the deposits to spawn:
    /// filter to metal, convert client world metres to the server's Q52.12 fixed point
    /// EXACTLY as the client encodes it, dedup, and clamp to the remaining budget.
    ///
    /// No ENet, no Improbable types, no mutable state - so the trust rules (only metal,
    /// never a duplicate position, never past the requested count) are unit-tested
    /// directly. The mutable per-island bookkeeping that calls this lives in
    /// <see cref="IslandResourceLedger"/>.
    /// </summary>
    public static class SpawnReplyPlan
    {
        /// <summary>
        /// The <c>resource.metadata</c> string the client stamps on a metal placement.
        /// VERIFIED: <c>IslandProxyVisualizer.ResourceNames.MetalDeposit = "MetalDeposit"</c>
        /// (acs/IslandProxyVisualizer.cs:16), sent as the FabricTransform metadata at :216.
        /// </summary>
        public const string MetalMetadata = "MetalDeposit";

        /// <summary>The first variant used when the client's reply carried none.</summary>
        public const string DefaultVariant = MetalDeposits.DefaultVariantId;

        /// <summary>Whether a reply item is a metal deposit (case-insensitive, like the client's own lookup).</summary>
        public static bool IsMetal(string? metadata)
        {
            return string.Equals(metadata, MetalMetadata, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// The deposits to spawn from a reply batch, given how many this island has
        /// ALREADY spawned, how many were requested, and the positions already used.
        ///
        /// Rules, in order:
        ///  - budget = clamp(requestedCount) - alreadySpawned; nothing if it is =&lt; 0.
        ///  - only items whose metadata is <see cref="MetalMetadata"/> (eggs and anything
        ///    else are dropped - this server spawns deposits).
        ///  - position = <see cref="FixedPointPosition.FromMetres"/>, the client's own
        ///    <c>(long)(m*4096)</c> truncation, so a deposit lands on the exact vertex the
        ///    client physics-checked.
        ///  - dedup: a position already in <paramref name="existing"/>, or repeated within
        ///    this batch, is skipped once - idempotency against a client that re-sends the
        ///    same reply and against two clients that sample the same vertex.
        ///  - stop at the budget.
        ///
        /// <paramref name="existing"/> is not mutated.
        /// </summary>
        public static IReadOnlyList<HandshakeDeposit> Accept(
            IEnumerable<ResourceReplyItem>? items,
            int alreadySpawned,
            int requestedCount,
            ISet<FixedPointPosition>? existing)
        {
            return Evaluate(items, alreadySpawned, requestedCount, existing, bounds: null).Accepted;
        }

        /// <summary>
        /// <see cref="Accept"/> plus the COORDINATE-FRAME GUARD and the drop reasons.
        ///
        /// The extra rule, applied BEFORE the fixed-point conversion, is
        /// <paramref name="bounds"/>: a placement whose global metres fall outside the
        /// island's (generously widened) AABB is refused outright and counted, never
        /// spawned. That is the guard that makes a floating-origin or scale error
        /// impossible to turn into deposits scattered across the sky - see
        /// <see cref="IslandBounds"/> for the failure modes it is aimed at. Passing null
        /// disables it (the unit tests that predate the guard, and any caller with no
        /// island to bound against).
        ///
        /// Order matters: metal-filter, then bounds, then dedup, then budget. Bounds runs
        /// before dedup so a wall of identical out-of-frame points is reported as
        /// out-of-bounds - the actionable reason - rather than as duplicates.
        /// </summary>
        public static SpawnReplyOutcome Evaluate(
            IEnumerable<ResourceReplyItem>? items,
            int alreadySpawned,
            int requestedCount,
            ISet<FixedPointPosition>? existing,
            IslandBounds? bounds)
        {
            List<HandshakeDeposit> accepted = new List<HandshakeDeposit>();
            int nonMetal = 0;
            int duplicate = 0;
            int outOfBounds = 0;
            ResourceReplyItem? firstOutOfBounds = null;

            if (items == null)
            {
                return new SpawnReplyOutcome(accepted, 0, 0, 0, null);
            }

            int budget = IslandResourceHandshake.ClampCount(requestedCount) - alreadySpawned;

            HashSet<FixedPointPosition> seen = existing == null
                ? new HashSet<FixedPointPosition>()
                : new HashSet<FixedPointPosition>(existing);

            foreach (ResourceReplyItem item in items)
            {
                if (!IsMetal(item.Metadata))
                {
                    nonMetal++;
                    continue;
                }
                if (bounds.HasValue && !bounds.Value.Contains(item.X, item.Y, item.Z))
                {
                    outOfBounds++;
                    firstOutOfBounds ??= item;
                    continue;
                }
                if (accepted.Count >= budget)
                {
                    // Over budget: keep counting the reasons above (they are diagnostic)
                    // but admit nothing more. Not a "duplicate" - just full.
                    continue;
                }
                FixedPointPosition pos = FixedPointPosition.FromMetres(item.X, item.Y, item.Z);
                if (!seen.Add(pos))
                {
                    duplicate++;
                    continue;
                }
                int placementIndex = alreadySpawned + accepted.Count;
                string variant = string.IsNullOrWhiteSpace(item.Variant)
                    ? MetalDeposits.VariantIdFor(placementIndex, configuredOverride: null)
                    : item.Variant.Trim();
                accepted.Add(new HandshakeDeposit(pos, variant));
            }

            return new SpawnReplyOutcome(accepted, nonMetal, duplicate, outOfBounds, firstOutOfBounds);
        }
    }
}
