namespace WorldsAdriftRebornGameServer.Multiplayer.Placement
{
    /// <summary>
    /// The VALUES that shape the "Craft" interaction prompt on a placed shipyard's
    /// centre console, kept out of the component serializer so each can be asserted
    /// natively - exactly like <see cref="WorldsAdriftRebornGameServer.Multiplayer.Helm"/>
    /// does for the helm's "Man" prompt.
    ///
    /// The console is made interactive by seeding 1210 InteractiveState with a single
    /// InteractionEntry naming the verb the Shipyard prefab bakes: InteractVerb.Craft
    /// (VERIFIED enum Bossa.Travellers.Interact.InteractVerb
    /// { Default=0, Activate=1, PickUp=2, Man=3, Inventory=4, Craft=5, ... }).
    /// InteractiveObjectVisualizer.OnEnable does
    /// <c>Interactions.FirstOrDefault(i =&gt; i.verb == Verb)</c>; with no entry naming
    /// Craft the radius and timeToUse fall to 0 and the prompt never appears - the same
    /// trap <see cref="WorldsAdriftRebornGameServer.Multiplayer.MetalNodes.PickUpRadius"/>
    /// and <see cref="WorldsAdriftRebornGameServer.Multiplayer.Helm.ManRadius"/> document.
    /// </summary>
    public static class ShipyardInteraction
    {
        /// <summary>
        /// 1210 InteractionEntry.radius, metres. Non-zero or no prompt appears.
        /// Matched to the helm's and nugget's 3 m so "how close do I have to be" is
        /// one number across every interaction seed this server sends. The tutorial
        /// asks the player to "approach the center console", so a modest radius keys
        /// the prompt off standing on the platform rather than across the deck.
        /// </summary>
        public const float CraftRadius = 3.0f;

        /// <summary>
        /// 1210 InteractionEntry.timeToUse, seconds. A short hold that shapes the
        /// prompt's fill animation before the client fires
        /// <c>TriggerInteractWithObject(shipyard, Craft)</c> on its own 1211.
        /// </summary>
        public const float CraftTimeToUse = 0.5f;
    }
}
