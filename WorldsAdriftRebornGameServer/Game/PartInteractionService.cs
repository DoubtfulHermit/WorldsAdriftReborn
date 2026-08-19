using Bossa.Travellers.Ship;
using WorldsAdriftRebornGameServer.Multiplayer;

namespace WorldsAdriftRebornGameServer.Game
{
    /// <summary>
    /// Handles the ACTIVATE interact (verb 1) on MOUNTED ship parts - the sail's
    /// furl/unfurl, the lamp's switch, the power generator's REFUEL and the horn's
    /// honk. The 1211 handler dispatches every completed Activate here (the same
    /// always-on shape as the helm's Man dispatch to the flight service); the single
    /// gate per part is its own ledger, so a target that is not one of those costs
    /// four dictionary misses and returns false.
    ///
    /// WIRE DISCIPLINE: one interaction = one reliable state-update broadcast
    /// (<see cref="ShipPublisher.Broadcast"/>), nothing per-frame. The client needs
    /// no echo beyond the state itself:
    ///   * sail - SailControlVisuals.LateUpdate POLLS 1303 unfurled and fires the
    ///     FurlSail/UnfurlSail animator triggers; there is no client-side
    ///     prediction and no OnInteract receiver on the sail (decompile-verified).
    ///   * lamp - LampVisualizer reacts to 1108 EnabledUpdated (light + emissive +
    ///     Play_LightSwitch).
    ///   * horn - HornVisualizer reacts to the 1107 SoundHorn EVENT
    ///     (Play_Ship_Horn01) and animates its own 30 s needle recharge; the horn
    ///     additionally gets ONE deferred charge re-anchor push when the window
    ///     ends, so the authoritative float converges (keyed, so honk spam never
    ///     stacks timers).
    ///
    ///   * power generator - no echo at all. The refuel moves inventory (1081,
    ///     pushed once) and the hull's fuel level, and the only visible consequence
    ///     is the 1105 the fuel gauge reads. The generator itself shows nothing,
    ///     because the shipped client renders neither InteractionEntry.description
    ///     nor any fuel text on a part - see docs/plans/feature-roadmap.md 13.1.
    ///
    /// AUTHORITY: 1303/1108/1107 have no client writers (decompile: zero
    /// *StateWriter requires outside gencode) - flipping the property server-side
    /// IS the retail mechanism; there is no command to answer.
    ///
    /// PERSISTENCE: sail furl and lamp switch are flags on the part's
    /// MountedPartRecord (upserted by PartUid, no-op for session-only mounts on the
    /// static hull); a honk is transient and persists nothing.
    /// </summary>
    internal sealed class PartInteractionService
    {
        private readonly IClock _clock;

        internal PartInteractionService(IClock clock)
        {
            _clock = clock;
        }

        /// <summary>
        /// A completed Activate interaction from <paramref name="playerEntityId"/> on
        /// <paramref name="targetEntityId"/>. Returns true when a mounted part
        /// consumed it. <paramref name="ownsPlayer"/> is the 1211 ownership fact -
        /// handed in, not re-derived, exactly like the flight service's Man path.
        /// </summary>
        internal bool OnActivateInteraction(long playerEntityId, long targetEntityId, bool ownsPlayer)
        {
            if (!ownsPlayer)
            {
                // A modified client driving someone else's 1211: refuse quietly.
                Console.WriteLine("[warning] part-interact: Activate on " + targetEntityId
                    + " from entity " + playerEntityId + " whose peer does not own it; ignored.");
                return false;
            }

            // SAIL: toggle the furl ledger, persist, push 1303. Power rides the same
            // bit (1 rigged / 0 furled): the shipped client's SailBehaviour multiplies
            // wind force by it on the physics worker, so any future physics reader
            // sees a sane multiplier; the pure client only animates off `unfurled`.
            bool? unfurled = WorldsAdriftRebornGameServer.Sails.Toggle(targetEntityId);
            if (unfurled.HasValue)
            {
                Persistence.WorldStatePersistence.UpdateMountedSailState(
                    Crafting.LooseParts.PartUidFor(targetEntityId), unfurled.Value);

                SailState.Update update = new SailState.Update()
                    .SetUnfurled(unfurled.Value)
                    .SetPower(unfurled.Value ? 1f : 0f);
                int sent = ShipPublisher.Broadcast(targetEntityId, 1303u, update);

                Console.WriteLine("[info] part-interact: sail " + targetEntityId + " is now "
                    + (unfurled.Value ? "UNFURLED" : "FURLED") + " (by entity " + playerEntityId
                    + "; 1303 pushed to " + sent + " peer(s)).");
                return true;
            }

            // LAMP: toggle the switch ledger, persist, push 1108.
            bool? lampOn = WorldsAdriftRebornGameServer.Lamps.Toggle(targetEntityId);
            if (lampOn.HasValue)
            {
                Persistence.WorldStatePersistence.UpdateMountedLampState(
                    Crafting.LooseParts.PartUidFor(targetEntityId), lampOn.Value);

                LampState.Update update = new LampState.Update().SetEnabled(lampOn.Value);
                int sent = ShipPublisher.Broadcast(targetEntityId, 1108u, update);

                Console.WriteLine("[info] part-interact: lamp " + targetEntityId + " switched "
                    + (lampOn.Value ? "ON" : "OFF") + " (by entity " + playerEntityId
                    + "; 1108 pushed to " + sent + " peer(s)).");
                return true;
            }

            // POWER GENERATOR: REFUEL. The generator IS the ship's fuel tank, and
            // this is the one door in the catalogue whose LABEL we did not have to
            // invent: PowerGenerator01 bakes InteractiveObjectVisualizer(Activate)
            // plus a TutorialHelper pointing at MOUSE_OVER_GENERATOR, and that
            // overlay asset's single control reads { Name: "Refuel", Hold: true }.
            // The player is told "Refuel", holds E, and gets refuelled.
            //
            // The sky core is deliberately NOT handled here. It briefly was, on the
            // argument that its Activate was baked and unclaimed - the verb is, but
            // GetTutorialStep maps Activate + a ShipCoreVisualizer to
            // MOUSE_OVER_CORE, whose asset reads "Activate Atlas Pulse" and names a
            // real retail action (1306, the anti-boarding pulse). A control that
            // lies about what it does is what PartInteractionPolicy exists to
            // forbid, and the generator's prompt does not lie.
            int? refuelled = WorldsAdriftRebornGameServer.ShipFuel.TryRefuel(playerEntityId, targetEntityId);
            if (refuelled.HasValue)
            {
                // No echo: the refuel moves inventory (1081, pushed once) and the
                // hull's fuel level, and the only visible consequence is the 1105 the
                // fuel gauge reads. The generator itself shows nothing, because the
                // shipped client renders neither InteractionEntry.description nor any
                // fuel text on the part.
                Console.WriteLine("[info] part-interact: generator " + targetEntityId + " refuelled by entity "
                    + playerEntityId + " (+" + refuelled.Value + " fuel).");
                return true;
            }

            // HORN: honk if recharged. The event plays the sound on every client;
            // charge=0 re-anchors the needle at "just honked", and ONE keyed deferred
            // push re-anchors it at full when the window ends (a re-honk before that
            // replaces the timer via the key rather than stacking).
            double now = _clock.Elapsed.TotalSeconds;
            bool? honked = WorldsAdriftRebornGameServer.Horns.TryHonk(targetEntityId, now);
            if (honked.HasValue)
            {
                if (!honked.Value)
                {
                    Console.WriteLine("[info] part-interact: horn " + targetEntityId
                        + " still recharging; honk by entity " + playerEntityId + " ignored.");
                    return true;
                }

                HornState.Update update = new HornState.Update()
                    .AddSoundHorn(default(SoundHorn))
                    .SetCharge(0f);
                int sent = ShipPublisher.Broadcast(targetEntityId, 1107u, update);

                DeferredActions.AfterKeyed("horn-recharge-" + targetEntityId, Horns.RechargeSeconds, () =>
                {
                    HornState.Update recharged = new HornState.Update().SetCharge(1f);
                    ShipPublisher.Broadcast(targetEntityId, 1107u, recharged);
                });

                Console.WriteLine("[info] part-interact: horn " + targetEntityId + " HONKED (by entity "
                    + playerEntityId + "; 1107 event pushed to " + sent + " peer(s)).");
                return true;
            }

            return false;
        }
    }
}
