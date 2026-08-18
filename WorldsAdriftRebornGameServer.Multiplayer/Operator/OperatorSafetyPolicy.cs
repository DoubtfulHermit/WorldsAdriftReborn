namespace WorldsAdriftRebornGameServer.Multiplayer.Operator
{
    /// <summary>
    /// The ODD CASES, named out loud.
    ///
    /// These commands move a human being without asking them, and several of the
    /// ways that goes wrong are invisible from the operator's side: a player who is
    /// standing on a ship's deck, a coordinate with nothing under it, an arrival
    /// exactly on top of somebody else, a hull dropped over a crowd. None of those
    /// is a reason to refuse - an operator who wants to pull someone off a moving
    /// ship is doing that on purpose - but every one of them is a reason to SAY SO,
    /// in the accepted result and in the log, so the answer to "why did that go
    /// strangely" is already written down before anyone asks.
    ///
    /// Refusals and warnings are therefore different things here and are kept
    /// apart: a refusal means nothing happened, a warning means it happened and
    /// here is what to expect. Pure - positions and flags in, sentences out.
    /// </summary>
    public static class OperatorSafetyPolicy
    {
        /// <summary>
        /// How far to one side a player is put when the destination is ANOTHER
        /// PLAYER, in metres.
        ///
        /// Landing exactly on somebody's coordinate puts two character capsules in
        /// the same cubic metre; the client resolves that by shoving one of them
        /// out, in a direction nobody chose and occasionally through the floor.
        /// Three metres is outside a capsule's radius and still inside conversation
        /// distance - the arrival reads as "next to them", which is what the
        /// operator asked for.
        ///
        /// Deliberately applied on X and NOT as a random bearing: a reproducible
        /// offset means two people teleported to the same target land in the same
        /// place every time, which is debuggable, and it is the same reasoning
        /// <c>AdminShipRecallPolicy</c> gives for refusing a lateral offset on the
        /// ship recall.
        /// </summary>
        public const double BesidePlayerMetres = 3.0;

        /// <summary>
        /// How close another player has to be to a summoned ship's drop point to be
        /// worth mentioning, in metres. A built hull is tens of metres across, so
        /// this is generous on purpose: the point is to name everyone who is about
        /// to have a ship appear over their head.
        /// </summary>
        public const double SummonBystanderRadiusMetres = 40.0;

        /// <summary>
        /// The arrival point for "teleport A to B": beside B, not inside B.
        /// </summary>
        public static FixedPointPosition BesidePlayer(FixedPointPosition target)
        {
            return new FixedPointPosition(
                target.X + (long)(BesidePlayerMetres * FixedPointPosition.UnitsPerMetre),
                target.Y,
                target.Z);
        }

        /// <summary>
        /// The players other than the summoning target who are within
        /// <see cref="SummonBystanderRadiusMetres"/> of where a hull is about to
        /// appear. Entity ids only; the caller turns them into a sentence.
        /// </summary>
        public static IReadOnlyList<long> BystandersNear(
            FixedPointPosition dropPoint,
            long targetEntityId,
            IReadOnlyList<OperatorPlayer> roster)
        {
            if (roster == null) throw new ArgumentNullException(nameof(roster));

            double radiusSquared = SummonBystanderRadiusMetres * SummonBystanderRadiusMetres;
            List<long> near = new List<long>();
            foreach (OperatorPlayer player in roster)
            {
                if (player.EntityId == targetEntityId || !player.HasPosition) continue;
                double dx = player.X - dropPoint.MetresX;
                double dy = player.Y - dropPoint.MetresY;
                double dz = player.Z - dropPoint.MetresZ;
                if (dx * dx + dy * dy + dz * dz <= radiusSquared) near.Add(player.EntityId);
            }
            near.Sort();
            return near;
        }

        /// <summary>
        /// The warnings a teleport earns, in the order an operator should read
        /// them. Empty means nothing unusual.
        /// </summary>
        public static IReadOnlyList<string> TeleportWarnings(
            OperatorDestinationKind destinationKind,
            bool landsOnLoadedGround,
            bool namesRequiredTerrain,
            bool targetIsAboardAShip)
        {
            List<string> warnings = new List<string>();

            if (targetIsAboardAShip)
            {
                warnings.Add("That player is aboard a ship. Teleporting takes them off it; "
                    + "the ship keeps flying without them.");
            }

            if (destinationKind == OperatorDestinationKind.Coordinate)
            {
                warnings.Add("A coordinate names no island, so the terrain-readiness gate "
                    + "cannot run and nothing guarantees there is ground there. Expect a fall "
                    + "unless you surveyed the point yourself.");
            }
            else if (destinationKind == OperatorDestinationKind.Player)
            {
                // Deliberately NOT the generic no-terrain sentence below. The
                // destination is wherever a live player is standing, which is
                // usually solid ground - the server simply cannot ATTRIBUTE that
                // point to an island without a terrain query, so it cannot gate the
                // arrival. Saying "expect a fall" here would be wrong far more
                // often than it was right, and a warning that cries wolf is a
                // warning an operator stops reading.
                warnings.Add("Arriving beside the destination player, "
                    + BesidePlayerMetres.ToString("0.#") + " m to one side, so the two "
                    + "characters do not occupy the same point. The server cannot tell "
                    + "which island they are on, so the arrival is ungated: if they are "
                    + "aboard a ship or over open sky, this is a fall.");
            }
            else if (!landsOnLoadedGround && !namesRequiredTerrain)
            {
                warnings.Add("This destination has no registered terrain behind it; "
                    + "the arrival is a fall rather than a landing.");
            }

            return warnings;
        }
    }
}
