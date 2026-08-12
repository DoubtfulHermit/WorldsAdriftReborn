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

        /// <summary>The variant used when the client's reply carried none - the verified default 1255 asset.</summary>
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
            List<HandshakeDeposit> accepted = new List<HandshakeDeposit>();
            if (items == null)
            {
                return accepted;
            }

            int budget = IslandResourceHandshake.ClampCount(requestedCount) - alreadySpawned;
            if (budget <= 0)
            {
                return accepted;
            }

            HashSet<FixedPointPosition> seen = existing == null
                ? new HashSet<FixedPointPosition>()
                : new HashSet<FixedPointPosition>(existing);

            foreach (ResourceReplyItem item in items)
            {
                if (accepted.Count >= budget)
                {
                    break;
                }
                if (!IsMetal(item.Metadata))
                {
                    continue;
                }
                FixedPointPosition pos = FixedPointPosition.FromMetres(item.X, item.Y, item.Z);
                if (!seen.Add(pos))
                {
                    continue;
                }
                string variant = string.IsNullOrWhiteSpace(item.Variant) ? DefaultVariant : item.Variant.Trim();
                accepted.Add(new HandshakeDeposit(pos, variant));
            }

            return accepted;
        }
    }
}
