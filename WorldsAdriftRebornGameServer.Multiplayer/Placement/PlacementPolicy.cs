namespace WorldsAdriftRebornGameServer.Multiplayer.Placement
{
    /// <summary>
    /// Why a client-authored 1017 PlaceItemEvent was accepted or rejected.
    ///
    /// Placement is the ONE inventory-mutating path where the CLIENT chooses the
    /// transform (it raycasts, previews and rotates locally, then submits), so the
    /// server cannot trust any field. This enum is the exhaustive set of reasons
    /// the pure check can refuse; the handler maps a non-Ok to "drop the event,
    /// spawn nothing, consume nothing".
    /// </summary>
    public enum PlacementOutcome
    {
        /// <summary>Everything checked out; consume the item and spawn the structure.</summary>
        Ok,

        /// <summary>No item with that id is in the player's server-side inventory (or it was already consumed - the duplicate-event guard).</summary>
        ItemNotInInventory,

        /// <summary>The item exists but is not the deployable type this handler places (e.g. not a shipyard).</summary>
        WrongItemType,

        /// <summary>The event's sourceEntity is not the player entity the 1017 update rode in on.</summary>
        SourceMismatch,

        /// <summary>A real parent entity was named by a deployable that must go down parentless (the shipyard). A crafting station may carry a parent (it is placed on a ship/surface) and is NOT rejected for it.</summary>
        UnexpectedParent,

        /// <summary>A coordinate was NaN or infinite - a malformed or malicious transform.</summary>
        NonFinitePosition,

        /// <summary>The placement point is further from the player than any legitimate preview could reach.</summary>
        TooFar,
    }

    /// <summary>The pure result of evaluating one placement request; the handler applies it.</summary>
    public readonly struct PlacementDecision
    {
        public PlacementDecision(PlacementOutcome outcome)
        {
            Outcome = outcome;
        }

        public PlacementOutcome Outcome { get; }

        /// <summary>True only when the placement may proceed to consume + spawn.</summary>
        public bool Ok => Outcome == PlacementOutcome.Ok;
    }

    /// <summary>
    /// Validates a client-authored deployable placement (1017 PlaceItemEvent) with
    /// no game types and no ENet, so it is unit-tested on Linux with no install -
    /// the same trick <see cref="Inventory.InventoryModel"/> and
    /// <see cref="Knowledge.KnowledgeScanPolicy"/> use.
    ///
    /// The client already runs the DETAILED preview checks (slope, overlap,
    /// grapple surfaces, special rules); the server cannot reproduce those without
    /// mirroring Unity physics, so this deliberately does the cheap authoritative
    /// checks the client CANNOT be trusted to have done: is the item real and
    /// mine, is it the right type, is the source me, is the transform sane. A
    /// modified client can lie about all of them, and every one of them, wrong,
    /// either duplicates an item or spawns a structure out of nothing.
    /// </summary>
    public static class PlacementPolicy
    {
        /// <summary>
        /// The ceiling on how far a placed structure may sit from the placing
        /// player, in metres. Only enforced when the server actually knows the
        /// player's position (it usually does not cache one); a generous bound,
        /// because the point is to reject a transform on the far side of the map,
        /// not to reproduce the client's exact max-distance rule.
        /// </summary>
        public const double MaxPlacementDistanceMetres = 60.0;

        /// <summary>
        /// Evaluates one placement.
        ///
        /// <paramref name="placeableItemTypeId"/> is the item type the server
        /// found in its own inventory for the requested id, or null if no such
        /// item is there (which is ALSO how the duplicate-event guard reads: once
        /// the first event consumes the item, a retry finds nothing).
        ///
        /// Distance is checked only when <paramref name="playerX"/>/Y/Z are all
        /// supplied. The server has no live player-position store today, so the
        /// handler passes null and distance is skipped - documented, not silently
        /// dropped.
        ///
        /// <paramref name="parentAllowed"/> relaxes the parentless rule for the ONE
        /// deployable family that is legitimately placed onto another entity: a
        /// generic crafting station (the Assembly Station) placed on a ship deck.
        /// Its 1017 carries the deck/hull entity as the parent (the client's preview
        /// raycast snapped to that surface), yet the server still spawns it parentless
        /// at the event's GLOBAL position, so the parent is accepted and then ignored.
        /// The shipyard leaves this false: it is a ground structure and its parentless
        /// invariant is what keeps a client from spawning one welded to a ship.
        /// </summary>
        public static PlacementDecision Evaluate(
            string? placeableItemTypeId,
            string expectedItemTypeId,
            bool sourceMatchesPlayer,
            bool hasParent,
            double posX,
            double posY,
            double posZ,
            double? playerX = null,
            double? playerY = null,
            double? playerZ = null,
            double maxDistanceMetres = MaxPlacementDistanceMetres,
            bool parentAllowed = false)
        {
            if (placeableItemTypeId == null)
            {
                return new PlacementDecision(PlacementOutcome.ItemNotInInventory);
            }

            if (!string.Equals(placeableItemTypeId, expectedItemTypeId, System.StringComparison.Ordinal))
            {
                return new PlacementDecision(PlacementOutcome.WrongItemType);
            }

            if (!sourceMatchesPlayer)
            {
                return new PlacementDecision(PlacementOutcome.SourceMismatch);
            }

            if (hasParent && !parentAllowed)
            {
                return new PlacementDecision(PlacementOutcome.UnexpectedParent);
            }

            if (!IsFinite(posX) || !IsFinite(posY) || !IsFinite(posZ))
            {
                return new PlacementDecision(PlacementOutcome.NonFinitePosition);
            }

            if (playerX.HasValue && playerY.HasValue && playerZ.HasValue)
            {
                double dx = posX - playerX.Value;
                double dy = posY - playerY.Value;
                double dz = posZ - playerZ.Value;
                double distanceSquared = (dx * dx) + (dy * dy) + (dz * dz);

                if (distanceSquared > maxDistanceMetres * maxDistanceMetres)
                {
                    return new PlacementDecision(PlacementOutcome.TooFar);
                }
            }

            return new PlacementDecision(PlacementOutcome.Ok);
        }

        private static bool IsFinite(double d)
        {
            return !double.IsNaN(d) && !double.IsInfinity(d);
        }

        /// <summary>
        /// After a 1017 has been evaluated to this <paramref name="outcome"/>, must the
        /// server tell the CLIENT to leave placement mode (push the 1019 StopPlacing
        /// clear), not merely drop its own server-side session?
        ///
        /// TRUE for EVERY outcome - accept AND every reject. The client enters placement
        /// mode the instant it sends the 1017 and it does NOT self-exit on a server
        /// reject: it waits for the 1019 Placing=false + StopPlacingItemEvent. So a
        /// reject path that only clears the server session (EndSession) leaves the client
        /// stuck in preview with the green ghost attached and EVERY tool (scanner, etc.)
        /// locked, exactly the assembly-station bug. Both the accept and the reject paths
        /// must therefore call StopPlacing. This is the pure, test-guarded statement of
        /// that invariant: any future outcome that fails to clear the client is a
        /// regression the handler must not introduce.
        /// </summary>
        public static bool ClientMustLeavePlacing(PlacementOutcome outcome)
        {
            // Placement is a modal client state entered on send and only ever left on
            // an explicit server clear; no terminal outcome may skip that clear.
            return true;
        }
    }
}
