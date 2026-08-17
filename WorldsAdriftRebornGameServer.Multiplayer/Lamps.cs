using System.Collections.Generic;

namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// The server's ledger of every LAMP currently MOUNTED on a built ship and each
    /// lamp's on/off switch. The ONE place "is this lamp switched on" lives, so the
    /// 1108 LampState serve branch and the 1211 Activate toggle agree - the sail's
    /// <see cref="Sails"/> pattern, applied to the lamp's single bool.
    ///
    /// RETAIL: a lamp is interactable with verb Activate (the client's
    /// InteractiveObjectVisualizer.GetTutorialStep maps Activate on a lamp to
    /// MOUSE_OVER_SWITCH_ON / _OFF via LampVisualizer.IsSwitchedOn). The toggle is a
    /// pure server-side property write: flipping 1108 <c>enabled</c> makes
    /// LampVisualizer.OnUpdated re-evaluate <c>Enabled &amp;&amp; IsFunctional</c>,
    /// switch the light + emissive materials and play <c>Play_LightSwitch</c>. There
    /// is no command and no client-side prediction.
    ///
    /// A freshly mounted lamp starts ON - the proven-working serve the lamp has
    /// always had (1108 enabled=true), so mounting keeps looking exactly like it did
    /// before this ledger existed. A LOOSE lamp is not tracked here and stays served
    /// enabled=true (unchanged); only a mounted lamp is switchable, matching the
    /// sail's "operable only rigged on a ship" rule.
    ///
    /// Pure: no ENet, no Improbable types. NOT thread-safe, deliberately - single
    /// poll loop, like every ledger here.
    /// </summary>
    public sealed class Lamps
    {
        private sealed class Lamp
        {
            public Lamp(long hullEntityId, bool on)
            {
                HullEntityId = hullEntityId;
                On = on;
            }

            public long HullEntityId { get; }
            public bool On { get; set; }
        }

        private readonly Dictionary<long, Lamp> _byEntityId = new Dictionary<long, Lamp>();

        /// <summary>
        /// Records that a lamp part entity is now mounted on
        /// <paramref name="hullEntityId"/>, starting <paramref name="on"/> (true for
        /// a fresh mount; the persisted value for a boot restore). Idempotent per
        /// entity id - a re-registration never resets a player-set switch. Returns
        /// true on first registration.
        /// </summary>
        public bool Register(long lampEntityId, long hullEntityId, bool on = true)
        {
            if (_byEntityId.ContainsKey(lampEntityId))
            {
                return false;
            }

            _byEntityId[lampEntityId] = new Lamp(hullEntityId, on);
            return true;
        }

        /// <summary>
        /// Removes a lamp - LIFTED off its ship. State is forgotten; a re-mount
        /// starts ON like a fresh lamp. Returns true if it was registered.
        /// </summary>
        public bool Unregister(long lampEntityId)
        {
            return _byEntityId.Remove(lampEntityId);
        }

        /// <summary>Whether this entity id is a mounted lamp this ledger tracks.</summary>
        public bool IsLamp(long lampEntityId)
        {
            return _byEntityId.ContainsKey(lampEntityId);
        }

        /// <summary>
        /// The switch state served on the lamp's 1108. TRUE for an unknown id: an
        /// untracked (loose) lamp keeps the proven always-on serve.
        /// </summary>
        public bool IsOn(long lampEntityId)
        {
            return !_byEntityId.TryGetValue(lampEntityId, out Lamp? lamp) || lamp.On;
        }

        /// <summary>
        /// Flips a mounted lamp's switch - THE interaction. Returns the NEW state,
        /// or null when the id is not a mounted lamp.
        /// </summary>
        public bool? Toggle(long lampEntityId)
        {
            if (!_byEntityId.TryGetValue(lampEntityId, out Lamp? lamp))
            {
                return null;
            }

            lamp.On = !lamp.On;
            return lamp.On;
        }

        /// <summary>How many lamps are mounted across all ships (diagnostics).</summary>
        public int Count => _byEntityId.Count;
    }
}
