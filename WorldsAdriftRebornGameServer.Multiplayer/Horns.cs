using System.Collections.Generic;

namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// The server's ledger of every HORN currently MOUNTED on a built ship and each
    /// horn's honk cooldown. The ONE place "may this horn sound now" lives, so the
    /// 1211 Activate handler and the 1107 charge serve agree.
    ///
    /// RETAIL: a horn is interactable with verb Activate (the client's
    /// InteractiveObjectVisualizer.GetTutorialStep maps Activate on a horn to
    /// MOUSE_OVER_HORN). The honk is the 1107 <c>SoundHorn</c> EVENT: HornVisualizer
    /// subscribes <c>_state.SoundHorn += OnSoundHorn</c> and plays
    /// <c>Play_Ship_Horn01</c> for everyone in earshot. The needle joint then shows a
    /// 30-second recharge the client animates LOCALLY (<c>_charge = 1 - _time/30</c>,
    /// HornVisualizer.Update), which is where <see cref="RechargeSeconds"/> comes
    /// from - the served <c>charge</c> float only re-anchors the needle. So the
    /// server's job is: gate honks to one per recharge window, fire the event, and
    /// keep the charge field consistent with the window.
    ///
    /// Pure: no ENet, no Improbable types, and NO CLOCK of its own - the caller
    /// hands in elapsed seconds (the flight service's IClock pattern), so the
    /// cooldown is unit-testable without sleeping. NOT thread-safe, deliberately -
    /// single poll loop, like every ledger here.
    /// </summary>
    public sealed class Horns
    {
        /// <summary>
        /// The honk cooldown, matching the 30-second needle recharge the client
        /// animates after every honk (HornVisualizer.Update).
        /// </summary>
        public const double RechargeSeconds = 30.0;

        private sealed class Horn
        {
            public Horn(long hullEntityId)
            {
                HullEntityId = hullEntityId;
            }

            public long HullEntityId { get; }

            /// <summary>When the last honk fired, in caller seconds; null = never.</summary>
            public double? LastHonkAt { get; set; }
        }

        private readonly Dictionary<long, Horn> _byEntityId = new Dictionary<long, Horn>();

        /// <summary>
        /// Records that a horn part entity is now mounted on
        /// <paramref name="hullEntityId"/>, fully charged. Idempotent per entity id.
        /// Returns true on first registration.
        /// </summary>
        public bool Register(long hornEntityId, long hullEntityId)
        {
            if (_byEntityId.ContainsKey(hornEntityId))
            {
                return false;
            }

            _byEntityId[hornEntityId] = new Horn(hullEntityId);
            return true;
        }

        /// <summary>Removes a horn - LIFTED off its ship. Returns true if it was registered.</summary>
        public bool Unregister(long hornEntityId)
        {
            return _byEntityId.Remove(hornEntityId);
        }

        /// <summary>Whether this entity id is a mounted horn this ledger tracks.</summary>
        public bool IsHorn(long hornEntityId)
        {
            return _byEntityId.ContainsKey(hornEntityId);
        }

        /// <summary>
        /// Attempts a honk at <paramref name="nowSeconds"/> - THE interaction.
        /// Returns true (and starts the cooldown) when the horn exists and its
        /// recharge window has elapsed; false while still recharging; null when the
        /// id is not a mounted horn.
        /// </summary>
        public bool? TryHonk(long hornEntityId, double nowSeconds)
        {
            if (!_byEntityId.TryGetValue(hornEntityId, out Horn? horn))
            {
                return null;
            }

            if (horn.LastHonkAt.HasValue && nowSeconds - horn.LastHonkAt.Value < RechargeSeconds)
            {
                return false;
            }

            horn.LastHonkAt = nowSeconds;
            return true;
        }

        /// <summary>
        /// The 1107 charge for this horn at <paramref name="nowSeconds"/>: 1 when
        /// ready, ramping 0..1 across the recharge window after a honk. 1 for an
        /// unknown id is deliberately NOT returned - an untracked (loose) horn keeps
        /// the serializer's idle charge=0 - so this returns null for unknown ids and
        /// the caller keeps its own default.
        /// </summary>
        public float? ChargeFor(long hornEntityId, double nowSeconds)
        {
            if (!_byEntityId.TryGetValue(hornEntityId, out Horn? horn))
            {
                return null;
            }

            if (!horn.LastHonkAt.HasValue)
            {
                return 1f;
            }

            double elapsed = nowSeconds - horn.LastHonkAt.Value;
            if (elapsed >= RechargeSeconds)
            {
                return 1f;
            }

            return (float)(elapsed / RechargeSeconds);
        }

        /// <summary>How many horns are mounted across all ships (diagnostics).</summary>
        public int Count => _byEntityId.Count;
    }
}
