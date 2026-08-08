using WorldsAdriftRebornGameServer.Multiplayer;

namespace WorldsAdriftRebornGameServer.Game
{
    /// <summary>
    /// Catches players who have walked off an island, and puts them back.
    ///
    /// THE FAILURE IT EXISTS FOR. Nothing on this server ends a fall. There is no
    /// fall damage; the client's own <c>WorldEdgePushback</c> never runs because
    /// it gates on world bounds we do not send, and reading it shows it would not
    /// help anyway - it enforces X and Z both ways but only the POSITIVE Y bound.
    /// So stepping off the edge of Haven used to end a player's session
    /// permanently, silently, with no feedback and no way back except an operator
    /// writing to a file on the VPS. It is the worst thing a new player can hit
    /// and it costs them everything.
    ///
    /// WHAT IS GLUE AND WHAT IS NOT. Everything decidable lives in
    /// <see cref="FallPolicy"/> (where the floor is, derived from measured island
    /// geometry) and <see cref="FallWatch"/> (one rescue per fall, retried, then
    /// abandoned). Both are pure and unit-tested. This class owns exactly two
    /// things neither of those can have: a real clock, and the ability to send.
    ///
    /// WHY IT LOGS EVERY VERDICT IT ACTS ON. A fall is invisible from the server
    /// unless it is written down. The two lines that matter are the
    /// <c>fall-rescue</c> send and the <c>fall-rescue</c> 1073 ack; between them
    /// they say "we saw it, and it worked".
    /// </summary>
    internal sealed class FallRescueService
    {
        private readonly FallWatch _watch;
        private readonly TeleportService _teleports;

        public FallRescueService(IClock clock, TeleportService teleports)
        {
            _watch = new FallWatch(clock);
            _teleports = teleports;
        }

        /// <summary>
        /// One position a player published about themselves, straight off 190602.
        ///
        /// HOT PATH. Called for every transform update from every player - the
        /// client republishes whenever it moves past a threshold, so this runs
        /// many times a second per player while anybody is walking. The common
        /// case is a single long comparison inside
        /// <see cref="FallPolicy.IsInTheWorld"/> and a dictionary miss.
        /// </summary>
        public void OnPlayerTransform(long entityId, FixedPointPosition position, bool? parentPresent)
        {
            switch (_watch.Observe(entityId, position, parentPresent))
            {
                case FallVerdict.Rescue:
                    _teleports.RescueFromFall(entityId, position, _watch.AttemptsFor(entityId));
                    break;

                case FallVerdict.GaveUp:
                    // Said once per fall by construction - FallWatch returns
                    // Abandoned for every later packet - so this cannot become a
                    // per-packet scream.
                    Console.WriteLine("[error] " + TeleportService.FallRescueReason + ": entity " + entityId
                        + " is still at y " + position.MetresY.ToString("0.#") + " m after "
                        + FallWatch.MaxAttemptsPerFall + " rescues. Its client is not applying 190607; "
                        + "no more will be sent for this fall. The trigger file is the next thing to try.");
                    break;

                case FallVerdict.InTheWorld:
                case FallVerdict.Descending:
                case FallVerdict.RescueInFlight:
                case FallVerdict.Abandoned:
                case FallVerdict.Parented:
                    // Nothing to say. Descending in particular is every ordinary
                    // jump off a ledge that ends on the ground 40 m below, and
                    // Parented is announced once by TransformState_Handler at the
                    // edge, which is where the news actually is.
                    break;
            }
        }

        /// <summary>Drops an entity's fall record when its peer disconnects.</summary>
        public void Forget(long entityId)
        {
            _watch.Forget(entityId);
        }

        // --------------------------------------------------------------------
        // TELL-THE-PLAYER SEAM - deliberately empty, and researched rather than
        // shrugged at.
        //
        // A silent rescue is confusing: from inside the game you are falling,
        // and then you are standing at spawn with no explanation. What the
        // player DOES perceive today is real but wordless - TeleportTransformVisualizer
        // calls PlayerMove.Respawn, which zeroes velocity, revives the ragdoll
        // and drops anything being carried, so the stop is unmistakable. Nothing
        // says why.
        //
        // THE ONE CHANNEL THAT WOULD SAY WHY, and exactly what it costs:
        //
        //   1001 ChatListener carries an EVENT, ReceiveFeedback
        //   {feedbackTitle, feedbackDescription, timeToLive}, appended with
        //   ChatListener.Update.AddReceiveFeedback. ChatVisualizer forwards it to
        //   WAUIFeedbackReportingEvents.FeedbackItemToDisplay - an on-screen card
        //   with a title, a body and a lifetime. That is precisely the "you fell
        //   off the world, we brought you home" banner this wants, it is
        //   server-authored, and it needs NO client patch.
        //
        //   What blocks it: ChatVisualizer also [Require]s NewChatListener.Writer
        //   (9002), so nothing on 1001 is read until 9002 is seeded AND granted
        //   to the client in MirrorSendPolicy.AuthoritativeComponents. Neither
        //   1001 nor 9002 is seeded by ComponentsSerializer today. So the price
        //   is two new component seeds plus a new authority grant - which also
        //   enables a visualizer that registers a command responder - on a build
        //   a player is already using tonight and which cannot be verified
        //   without a client.
        //
        // That is a chat/notification workstream, not a fall-floor one, and it
        // should be built once for every message this server will ever want to
        // send rather than smuggled in behind this feature. Getting the player
        // back onto solid ground is worth having without it.
        // --------------------------------------------------------------------
    }
}
