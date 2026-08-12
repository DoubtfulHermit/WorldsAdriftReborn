namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// What the fall watch thinks should happen to one player, given the last
    /// position they published.
    /// </summary>
    public enum FallVerdict
    {
        /// <summary>
        /// At or above <see cref="FallPolicy.RearmY"/> - level with the island or
        /// higher, i.e. somewhere a player can plausibly be. Nothing to do, and
        /// seeing this is what re-arms the watch for the NEXT fall.
        /// </summary>
        InTheWorld,

        /// <summary>
        /// Below the island but not yet below the floor. Almost always a real
        /// fall in progress; deliberately NOT acted on, because the whole point
        /// of the floor is to sit below anything anyone could be standing on.
        /// </summary>
        Descending,

        /// <summary>Below the floor. Send them home, now.</summary>
        Rescue,

        /// <summary>
        /// Below the floor, but a rescue was sent less than
        /// <see cref="FallWatch.RetryInterval"/> ago. The client is authoritative
        /// over its own transform and keeps publishing its falling position for
        /// the whole round trip, so this is the ordinary case for every packet
        /// after the first - and suppressing it is what makes this one rescue per
        /// fall instead of one per packet.
        /// </summary>
        RescueInFlight,

        /// <summary>
        /// Below the floor after <see cref="FallWatch.MaxAttemptsPerFall"/>
        /// rescues that all failed to move the player. The server has done
        /// everything it can from here. Returned EXACTLY ONCE per fall, so the
        /// glue can log it without a flag of its own; every later packet of the
        /// same fall comes back as <see cref="Abandoned"/>.
        /// </summary>
        GaveUp,

        /// <summary>
        /// Below the floor, past the attempt cap, and already reported. Say
        /// nothing: this arrives several times a second for as long as the
        /// player's client keeps publishing, and one unreadable log is not a
        /// better outcome than one lost player.
        /// </summary>
        Abandoned,

        /// <summary>
        /// This entity's transform is PARENTED, so its position is expressed in
        /// its parent's local space and comparing it to a world floor is
        /// meaningless. Not judged, in either direction.
        ///
        /// It is sticky, and that is the point: the client only puts
        /// <c>parent</c> on the wire when it CHANGES
        /// (<c>LocalTransformUpdaterBehaviour</c> publishes it once and then
        /// sends bare positions), so a watch that only looked at the current
        /// packet would see one parented update and then happily measure a few
        /// hundred island-local metres against a world floor - and teleport
        /// somebody who was standing on a deck.
        /// </summary>
        Parented,
    }

    /// <summary>
    /// WHERE the bottom of the world is, in the same Q52.12 space every other
    /// coordinate in this assembly uses. Pure: no ENet, no Improbable types, no
    /// game install.
    ///
    /// WHY THIS EXISTS. Walking off Haven kills nobody and stops nowhere. There
    /// is no fall damage on this server, and the client's own
    /// <c>WorldEdgePushback</c> cannot help even when it runs: read it and it
    /// enforces X and Z in both directions but only the POSITIVE Y bound
    /// (push at +800 m, hard clamp at +1000 m). There is no lower bound in the
    /// shipped client at all - <c>WorldBoundsDataState.minHeight</c> exists in
    /// the schema and its single consumer is the lightning VFX spawner. So a
    /// player who steps off an edge falls forever, and since the server never
    /// sees them again except as opaque relayed bytes, their session simply
    /// ends. The floor has to be ours.
    ///
    /// WHERE THE NUMBER COMES FROM - all of it measured, none of it guessed:
    ///
    /// * Haven instance #5 sits at world y = <c>-318.669</c> m
    ///   (<see cref="SpawnPolicy.IslandPosition"/>, from Bossa's own map file).
    /// * Its collider mesh bottoms out at island-local y = <c>-86.0</c> m -
    ///   <c>meta.localAABB.min[1]</c> of
    ///   <c>docs/research/world-data/island-surfaces/1431299145.json</c>, the
    ///   TRS-composed surface table over 28,616 LOD0 vertices in 90 cells. That
    ///   is the deepest point of the whole island; under the starter camp the
    ///   underside is only about 34 m down (findings-haven.md).
    /// * So the lowest thing on Haven anyone could conceivably be standing on,
    ///   in world space, is <c>-318.669 + -86.0 = -404.669</c> m. That is
    ///   <see cref="RearmY"/>.
    /// * The floor is <see cref="SafetyMarginMetres"/> = 100 m below that:
    ///   <c>-504.669</c> m.
    ///
    /// WHY 100 m OF MARGIN, and not 10 or 500. It has to beat the largest error
    /// this dataset has ever actually had: the pre-TRS extractor bug displaced
    /// Haven's vertices by up to <b>51 m</b> (findings-haven.md, "mean |ΔY|
    /// 24.84, median 24.00, max 51"). 100 m is nearly twice that, so even a
    /// surface table as wrong as the worst one we have ever shipped still cannot
    /// put ground below the floor. It is also far more than the 2 m stand-off
    /// convention and than any collider-vertex versus raycast-hit discrepancy,
    /// which findings-spawn.md puts in the "ordinary risk" bracket after the
    /// systematic 25 m error was fixed.
    ///
    /// AND WHY NOT DEEPER. The margin is also a fall duration: at Unity's
    /// gravity from a standing step off the camp rim (world y about -352.7 m,
    /// the underside beneath the camp) the floor is 152 m down, about 5.6 s of
    /// falling; from the spawn point itself it is 193 m, about 6.3 s. A floor
    /// below the DEEPEST geometry in the entire authored world - Shattered
    /// Mausoleum's underside at -828.3 m - would be honest for a world we do not
    /// spawn and would cost the falling player 11 s of nothing happening.
    ///
    /// WHEN A SECOND ISLAND IS SPAWNED, THIS NUMBER MUST BE REVISITED. It is
    /// derived from the one island this server actually places, because that is
    /// the only ground that exists (every other
    /// <see cref="TeleportDestination"/> is flagged
    /// <c>LandsOnLoadedGround: false</c> for exactly this reason). The world's
    /// islands span y = -527.0 to +356.8 before their own geometry is added, so
    /// a second island low enough to sit under this floor is entirely possible -
    /// and <see cref="TeleportPolicy.MausoleumName"/> already is: its destination
    /// is at world y -707.1, BELOW the floor, so teleporting someone there now
    /// bounces them straight home. That is asserted in the tests so it is a
    /// documented consequence rather than a surprise.
    /// </summary>
    public static class FallPolicy
    {
        /// <summary>
        /// Haven's deepest collider vertex in island-local space, in fixed point:
        /// -86.0 m, from <c>island-surfaces/1431299145.json</c>'s local AABB.
        /// </summary>
        public const long IslandLocalMinimumY = -86L * FixedPointPosition.UnitsPerMetre;

        /// <summary>
        /// How far below the island's deepest point the floor sits. See the type
        /// remarks: it is chosen to beat the 51 m worst-case error this surface
        /// dataset has historically carried, with room to spare.
        /// </summary>
        public const long SafetyMarginMetres = 100L;

        /// <summary>
        /// The lowest world y anything on Haven reaches: -404.669 m. A player at
        /// or above this is level with the island or higher, so they are back in
        /// the world - which is what re-arms the watch for the next fall, and
        /// gives <see cref="FloorY"/> a 100 m hysteresis band beneath it that a
        /// player cannot oscillate across at a packet's notice.
        /// </summary>
        public static readonly long RearmY = SpawnPolicy.IslandPosition.Y + IslandLocalMinimumY;

        /// <summary>
        /// The fall floor: world y = -504.669 m, in Q52.12. Below this, a player
        /// is under everything and falling away from a world with no bottom.
        ///
        /// Kept in fixed point, not metres, because the comparison runs on every
        /// 190602 update from every player: the wire already carries fixed point,
        /// so this way the hot path is one long comparison and no arithmetic at
        /// all.
        /// </summary>
        public static readonly long FloorY = RearmY - SafetyMarginMetres * FixedPointPosition.UnitsPerMetre;

        /// <summary>The floor in metres. For log lines and for reading a test failure.</summary>
        public static double FloorMetres => (double)FloorY / FixedPointPosition.UnitsPerMetre;

        /// <summary>The re-arm altitude in metres. For log lines and tests.</summary>
        public static double RearmMetres => (double)RearmY / FixedPointPosition.UnitsPerMetre;

        // --------------------------------------------------------------------
        // THE DEEP SAFETY NET. The floor above catches a player who fell off the
        // island; this one catches a player who fell through the WORLD, and it
        // exists so the automatic rescue can be turned OFF (see
        // AutoFallRescuePolicy) without leaving a player in a genuinely endless
        // fall completely unrecoverable.
        //
        // WHY A SECOND, DEEPER FLOOR AND NOT JUST THE FIRST ONE. Once ships fly,
        // being below the island is normal - a player flying, boarding, or
        // descending on a ship sits below FloorY on purpose, and the ordinary
        // floor would snatch them home mid-flight. Recovery is now a manual F10
        // (client side), so the automatic yank is off by default. But "off"
        // must not mean "a fall through the bottom of the world is permanent":
        // the world still writes no fall damage and no lower world bound, so a
        // true fall accelerates without limit forever. The deep net is the
        // last-ditch catch for exactly that, placed far enough down that nothing
        // a ship does near any island reaches it.
        //
        // WHERE THE NUMBER COMES FROM. The deepest authored geometry in the
        // whole world is Shattered Mausoleum's underside at world y = -828.3 m
        // (see the type remarks). -2000 m is ~1.17 km below that - below every
        // island and its underside by a wide margin, so a ship flown anywhere
        // near real ground cannot trip it, while a player who has fallen a full
        // kilometre past the lowest thing in the world has unambiguously fallen
        // OUT of it. Like FloorY this MUST be revisited if a second island is
        // ever spawned lower than this; today nothing authored reaches it.
        // </summary>
        public const long DeepSafetyNetMetresValue = -2000L;

        /// <summary>
        /// The deep net in fixed point: world y = -2000 m, in Q52.12. Kept in the
        /// same encoding as <see cref="FloorY"/> so the hot-path comparison is a
        /// single long compare with no arithmetic.
        /// </summary>
        public static readonly long DeepFloorY = DeepSafetyNetMetresValue * FixedPointPosition.UnitsPerMetre;

        /// <summary>The deep net in metres. For log lines and tests.</summary>
        public static double DeepFloorMetres => (double)DeepFloorY / FixedPointPosition.UnitsPerMetre;

        /// <summary>Whether this position is under the floor.</summary>
        public static bool IsBelowFloor(FixedPointPosition position) => position.Y < FloorY;

        /// <summary>Whether this position is under the deep safety net - i.e. it fell through the world.</summary>
        public static bool IsBelowDeepFloor(FixedPointPosition position) => position.Y < DeepFloorY;

        /// <summary>
        /// Whether this position is level with the island or higher. Only the y
        /// matters: the world is 36 km across and there is nothing to stand on
        /// between islands, so a horizontal test would only ever add a way to be
        /// wrong.
        /// </summary>
        public static bool IsInTheWorld(FixedPointPosition position) => position.Y >= RearmY;
    }

    /// <summary>
    /// The record of who is falling and who has already been caught. Pure and
    /// clock-injected, so "one rescue per fall, not one per packet" is asserted
    /// natively instead of by watching somebody fall off an island.
    ///
    /// THE PROBLEM IT SOLVES IS AUTHORITY, NOT ARITHMETIC. The client owns
    /// 190602 for its own entity (it is in
    /// <see cref="MirrorSendPolicy.AuthoritativeComponents"/>) and publishes it
    /// continuously while falling. A naive "y &lt; floor -&gt; teleport" would
    /// therefore fire on every packet for the entire round trip, and each of
    /// those teleports would land on a client that has since moved further down
    /// and is still publishing - a server and a client shoving the same player in
    /// opposite directions several times a second. Two brakes stop that:
    ///
    /// 1. <b><see cref="RetryInterval"/></b>. After a rescue is sent, nothing
    ///    else fires for that entity for five seconds. That is far longer than
    ///    any plausible round trip, so the client has finished applying the
    ///    teleport (and acked it on 1073) long before another can be sent.
    /// 2. <b><see cref="FallPolicy.RearmY"/></b>, 100 m above the floor. The
    ///    attempt counter only resets when the player is seen level with the
    ///    island again, so a fall is one episode however many packets it spans.
    ///
    /// The same interval doubles as the RETRY, and that is deliberate rather
    /// than a convenience: a teleport can be dropped, or ignored because the
    /// client never got 190607, and the only evidence the server has either way
    /// is the 1073 ack (see <see cref="TeleportRequestCounter"/>). If the player
    /// is still under the floor five seconds later, the rescue did not take, and
    /// trying again is the only remedy that exists. After
    /// <see cref="MaxAttemptsPerFall"/> attempts it stops - a client that has
    /// ignored three teleports will ignore the fourth, and a rescue every five
    /// seconds forever is a log nobody can read.
    /// </summary>
    public sealed class FallWatch
    {
        /// <summary>
        /// Minimum gap between two rescues of the same entity, and equally the
        /// delay before a rescue that produced no ack is retried. Five seconds:
        /// comfortably beyond a round trip plus the client's own apply, and short
        /// enough that a genuinely dropped teleport is not a lost session.
        /// </summary>
        public static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(5);

        /// <summary>
        /// How many rescues one fall gets before the server stops trying. Three
        /// is fifteen seconds of a client refusing to move.
        /// </summary>
        public const int MaxAttemptsPerFall = 3;

        private sealed class Fall
        {
            public int Attempts;
            public TimeSpan LastRescueAt;
            public bool GiveUpAnnounced;
        }

        private readonly Dictionary<long, Fall> _falling = new();

        /// <summary>
        /// Entities whose last word on the subject was "I have a parent". See
        /// <see cref="FallVerdict.Parented"/> for why this has to be remembered
        /// rather than read off each packet.
        /// </summary>
        private readonly HashSet<long> _parented = new();

        private readonly IClock _clock;

        /// <summary>
        /// The world-y, in Q52.12, below which a fall is acted on. Defaults to the
        /// ordinary island floor (<see cref="FallPolicy.FloorY"/>); the glue passes
        /// <see cref="FallPolicy.DeepFloorY"/> instead when the automatic rescue is
        /// off (see AutoFallRescuePolicy), so that "below the island" - now an
        /// ordinary thing for a ship to be - is NOT rescued, but "fell through the
        /// world" still is. Everything else about the watch - one rescue per fall,
        /// the retry interval, the attempt cap, the parented handling - is
        /// unchanged: only the trigger altitude moves.
        /// </summary>
        private readonly long _floorY;

        public FallWatch(IClock clock, long? floorY = null)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _floorY = floorY ?? FallPolicy.FloorY;
        }

        /// <summary>The trigger floor this watch is using, in Q52.12. For tests and the banner.</summary>
        public long FloorY => _floorY;

        /// <summary>
        /// Takes one position a player published and says what to do about it.
        /// Called for every 190602 update from every player, so it allocates
        /// nothing on the overwhelmingly common
        /// <see cref="FallVerdict.InTheWorld"/> path.
        /// </summary>
        /// <param name="parentPresent">
        /// Whether this update said the entity HAS a parent, or null if the
        /// update did not mention <c>parent</c> at all - which is what almost
        /// every update does, because the generated writer only puts a field on
        /// the wire when it changes. The last non-null answer is remembered.
        /// </param>
        public FallVerdict Observe(long entityId, FixedPointPosition position, bool? parentPresent = null)
        {
            if (parentPresent.HasValue)
            {
                if (parentPresent.Value)
                {
                    _parented.Add(entityId);
                }
                else
                {
                    _parented.Remove(entityId);
                }
            }

            if (_parented.Contains(entityId))
            {
                // Island-local or ship-local metres. There is no world floor to
                // compare them to, and guessing would teleport somebody off a
                // deck. A parented entity is somebody else's problem.
                return FallVerdict.Parented;
            }

            if (FallPolicy.IsInTheWorld(position))
            {
                // Back on (or above) the island: this fall, if there was one, is
                // over. Dropping the record is what re-arms the watch, and it is
                // why a second genuine fall is rescued immediately rather than
                // waiting out the retry interval of the first.
                _falling.Remove(entityId);
                return FallVerdict.InTheWorld;
            }

            if (position.Y >= _floorY)
            {
                // Above this watch's trigger floor. With the default floor this is
                // "under the island, above the margin" - a normal fall in
                // progress. With the deep floor (auto-rescue off) it is also every
                // ship that is legitimately flying below the island: not rescued
                // either way, because acting here is how a rescue ends up fighting
                // somebody who is standing on - or flying - something.
                return FallVerdict.Descending;
            }

            TimeSpan now = _clock.Elapsed;
            if (!_falling.TryGetValue(entityId, out Fall? fall))
            {
                _falling[entityId] = new Fall { Attempts = 1, LastRescueAt = now };
                return FallVerdict.Rescue;
            }

            // The interval is checked BEFORE the cap on purpose: the last attempt
            // is owed its five seconds to work before the server declares it
            // failed, or "gave up" would be logged in the same breath as the
            // third try and would be wrong every time the third one landed.
            if (now - fall.LastRescueAt < RetryInterval)
            {
                return FallVerdict.RescueInFlight;
            }

            if (fall.Attempts >= MaxAttemptsPerFall)
            {
                if (fall.GiveUpAnnounced)
                {
                    return FallVerdict.Abandoned;
                }

                fall.GiveUpAnnounced = true;
                return FallVerdict.GaveUp;
            }

            fall.Attempts++;
            fall.LastRescueAt = now;
            return FallVerdict.Rescue;
        }

        /// <summary>
        /// How many rescues this entity's current fall has already had, or 0 if
        /// it is not falling. For the log line and for the tests.
        /// </summary>
        public int AttemptsFor(long entityId)
        {
            return _falling.TryGetValue(entityId, out Fall? fall) ? fall.Attempts : 0;
        }

        /// <summary>Whether this entity is mid-rescue. For tests and diagnostics.</summary>
        public bool IsFalling(long entityId) => _falling.ContainsKey(entityId);

        /// <summary>Whether this entity last said it was parented. For tests.</summary>
        public bool IsParented(long entityId) => _parented.Contains(entityId);

        /// <summary>Drops an entity's record. Called when its peer disconnects.</summary>
        public void Forget(long entityId)
        {
            _falling.Remove(entityId);
            _parented.Remove(entityId);
        }
    }
}
