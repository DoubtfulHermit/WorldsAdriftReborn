namespace WorldsAdriftRebornGameServer.Multiplayer.Ship
{
    /// <summary>
    /// WHERE A FLIGHT INSTRUMENT MOUNTS, and why it is not the deck.
    ///
    /// THE REPORT THIS ANSWERS: *"the altimeter can only go on the floor; I put a
    /// fence down and I want to place it on that"*, then, after a client patch,
    /// *"it's red and it's not even the right direction. I feel like we are hacking
    /// this together."* Both were right, and the second one is the reason this file
    /// exists instead of a third Harmony patch.
    ///
    /// RETAIL SAYS IT IN ITS OWN CODE. <c>acs/ShipInstrument.cs</c> is four lines,
    /// and it is attached to the instrument prefabs and nothing else:
    ///
    /// <code>
    /// private void Awake() {
    ///     PlacementRules r = gameObject.GetOrAddComponent&lt;PlacementRules&gt;();
    ///     r.IgnoreOverlap(entity =&gt; ShipPartVisualizer.AttachedShip(entity));
    /// }
    /// </code>
    ///
    /// An overlap EXEMPTION for anything attached to a ship. A rule like that is only
    /// worth writing for a part that is expected to be placed intersecting other ship
    /// parts - i.e. clipped onto something already bolted down. Bossa wrote the
    /// permission; we did not have to invent it. (Same shape of proof as the
    /// <c>BlockItemPlacement</c> opt-out in 11.6: the existence of the exception is
    /// the evidence for the rule.)
    ///
    /// WIKI, corroborating and labelled as WIKI: *"Flight Instruments … can be placed
    /// on other components, with a good example being putting them on Bar Pipes in
    /// front of your helm as a makeshift HUD"*, and *"The Bar Pipe and Bent Bar Pipe
    /// are structural items that can be placed on a ship … used to attract lightning
    /// in a Stormwall or to display Instruments."* A part whose stated purpose is to
    /// hold instruments only makes sense if instruments do not go on the floor.
    ///
    /// SO THE SURFACE IS <c>shipSurfaces</c>, AND EVERY SYMPTOM FALLS OUT OF IT.
    /// <c>PlacementLocationType.ShipSurfaces</c> raycasts <c>Layers.Environment</c>
    /// with an EMPTY tag (<c>PlacementPreview.GetMask</c>/<c>GetTag</c>):
    /// <list type="bullet">
    /// <item>It HITS a railing, a bar pipe, a cupboard, a barrel, a panel - every
    ///   placed part carries colliders on layer 0 <c>Default</c>, <c>Untagged</c>,
    ///   which is inside <c>Layers.Environment</c> (asset-read, 11.6). No client
    ///   change is needed to aim at them.</item>
    /// <item>It does NOT hit the deck, whose collider is <c>ShipAttachmentSolid</c>
    ///   with tag <c>"ShipDeck"</c> - which is precisely WHY bar pipes exist.</item>
    /// <item>It takes the <c>PlacingOnSurface</c> branch, not <c>PlacingOnDeck</c>,
    ///   so the pose comes from the surface instead of being forced square to the
    ///   hull. That is the *"not even the right direction"* half of the report:
    ///   <c>PlacingOnDeck</c> builds its rotation from
    ///   <c>Quaternion.LookRotation(ship.forward, up)</c> and throws the hit normal
    ///   away, so a gauge clipped to a horizontal rail was being stood up as if it
    ///   were sitting on the floor.</item>
    /// <item><c>NeedToBeOnShip</c> is FALSE for this surface, so the client never
    ///   walks the Unity parents for a <c>DockableVisualizer</c> - and our
    ///   <c>Parent(hull,"~")</c> seeding, which is <c>SetNoParent</c> client-side and
    ///   broke that walk, stops mattering at all. That walk was the BLUE preview.</item>
    /// </list>
    ///
    /// WHY THE NORMALIZER MUST NOT EAT THIS. <c>PartMountSurfaces.NormalizeForBuiltShip</c>
    /// rewrites <c>shipSurfaces</c> to <c>deck</c>, and it was right to: it fixed the
    /// *"helm only mounts in ONE spot"* bug, because our generated hull exposes no
    /// retail Environment-layer skin, so a <c>shipSurfaces</c> HELM had nothing to
    /// land on but one incidental frame collider. That reasoning holds for the helm
    /// and for structure. It does NOT hold for instruments, because an instrument is
    /// not meant to land on the hull's skin - it is meant to land on a part someone
    /// already bolted there, and those exist and carry Environment colliders.
    ///
    /// THE TRADE, STATED OUT LOUD BECAUSE IT IS A REAL LOSS. An instrument can no
    /// longer be dropped on the bare deck, and because <c>NeedToBeOnShip</c> is false
    /// for this surface the client will also let one be placed on terrain. Both are
    /// retail's behaviour for this placement type, neither is enforced server-side
    /// (<c>PlacementPolicy</c> validates no surface, deliberately), and both are
    /// reversed by changing <see cref="MountSurface"/> back to <c>"deck"</c> - one
    /// line, no migration, because <c>attachmentType</c> is read from the catalogue
    /// at serve time and mounted parts already in the world keep their pose.
    ///
    /// WAREBORN TUNING: retail's per-item placement strings lived on the GSim and are
    /// unrecoverable (11.6). This is a reconstruction from the client's own overlap
    /// exemption, its layer/tag tables and the wiki - the best available evidence, and
    /// labelled as a reconstruction rather than a recovery.
    /// </summary>
    public static class ShipInstruments
    {
        /// <summary>
        /// The <c>1120 attachmentType</c> string a flight instrument is authored with.
        ///
        /// **STILL <c>"deck"</c>, AND THAT IS DELIBERATE.** Everything above argues that
        /// <c>shipSurfaces</c> is retail's value, and it is - but flipping it TODAY
        /// makes instruments unmountable ENTIRELY, which is worse than the complaint it
        /// answers. One line is in the way, and it is not gated on the attachment type:
        ///
        /// <code>
        /// // PlayerScannerTool.cs:502
        /// bool flag4 = ShipPartPlacement.IsAttachedToShip(Placement.Preview.TargetObject);
        /// // :524  _canDrop = ... &amp;&amp; !flag4 &amp;&amp; ...
        /// // :516  CanPlace  = ... &amp;&amp; flag4 &amp;&amp; ...
        /// </code>
        ///
        /// <c>IsAttachedToShip</c> is <c>GetComponentInParents&lt;DockableVisualizer&gt;()</c>
        /// - a Unity walk. A <c>shipSurfaces</c> instrument aimed at a bar pipe would
        /// raycast correctly, pose correctly and pass every overlap rule, and then
        /// <c>flag4</c> would be false because the pipe has no Unity ancestors on this
        /// server, so <c>CanPlace</c> is false and only <c>_canDrop</c> is true: a
        /// beautiful, correctly-oriented BLUE phantom that free-drops as a loose item
        /// instead of bolting on. Meanwhile the deck is lost, because the
        /// <c>ShipSurfaces</c> mask is <c>Layers.Environment</c> and the deck collider
        /// is <c>ShipAttachmentSolid</c>. Net effect: nowhere left to put an instrument.
        ///
        /// **THE PRECONDITION, exactly.** Flip this to <c>"shipSurfaces"</c> the moment a
        /// mounted part is seeded a REAL hierarchy key instead of
        /// <see cref="BoltedPartTransform.RelativeSlotKey"/> - i.e. when
        /// <c>GetComponentInParents</c> can find the hull from a bolted part. That is
        /// PHASE SC5 (docs/plans/feature-roadmap.md 11.11.4), it is four call sites plus
        /// one filter, and it fixes FIVE separate client walks at once. This string is
        /// the last step of that change, not a substitute for it.
        ///
        /// **STATUS: the first half of SC5 has LANDED, and this string still does not
        /// move yet.** <see cref="MountedPartHierarchy"/> now seeds the two BAR PIPES a
        /// real hierarchy key at all three mounted-part transform sites, so the Unity
        /// walk <c>flag4</c> depends on can climb from a mounted pipe to the hull. Two
        /// things still stand between that and this line, and both need a live client,
        /// not another read of the decompile:
        /// <list type="number">
        /// <item>The parenting itself rests on two PREFAB-BAKED flags that are invisible
        ///   offline - BarPipe's authored <c>GameObjectCanBeParented</c> and
        ///   <c>ShouldRemoveRigidbodyOnParented</c>. They fail SAFE (an unparentable
        ///   prefab ignores the key and behaves exactly as today), which also means the
        ///   server cannot tell whether they held. Only a player craft-and-bolt shows
        ///   it.</item>
        /// <item>Even with pipes working, <c>shipSurfaces</c> still LOSES the deck: its
        ///   mask is <c>Layers.Environment</c> and the deck collider is
        ///   <c>ShipAttachmentSolid</c>. So the flip is not purely additive - it MOVES
        ///   instruments from the deck onto pipes and railings, which is retail's
        ///   layout but is a design call for the maintainer, not a bug fix.</item>
        /// </list>
        /// What the flip buys, and why it is still worth doing after a live confirm: the
        /// <c>shipSurfaces</c> branch of <c>PlacementPreview.PositionOnShip</c> poses off
        /// <c>Quaternion.LookRotation(forward, hitNormal)</c>, whereas the <c>deck</c>
        /// branch throws the hit normal away for <c>LookRotation(ship.forward,
        /// +/-Vector3.up)</c>. That discarded normal is exactly why a gauge bolted to a
        /// horizontal rail stood upright facing the sky.
        ///
        /// AND THE CHOICE ITSELF CARRIES NO FIDELITY RISK, which is worth knowing before
        /// anyone agonises over it: <c>attachmentType</c> has nine legal values and an
        /// exhaustive search of all six shipped asset containers plus
        /// <c>StreamingAssets/GameDB</c> finds NONE of those literals anywhere. The
        /// authored per-part values lived in Improbable's server-side templates and are
        /// unrecoverable. Picking one is supplying a value retail also supplied - the
        /// only question is which one WORKS, and today the answer is "deck".
        /// </summary>
        public const string MountSurface = "deck";

        /// <summary>
        /// The catalogue <c>itemType</c> that marks a row as a flight instrument. One
        /// string, consumed by the catalogue rows and by the normalizer exemption, so
        /// the two cannot disagree about what an instrument is - the same shape as
        /// <see cref="ShipContainers.IsContainer"/>, and for the same reason: the last
        /// time a set like this was written out by hand in more than one place it
        /// drifted immediately.
        /// </summary>
        public const string ItemType = "instruments";

        /// <summary>
        /// Whether a catalogue row of this <c>itemType</c> is a flight instrument, and
        /// therefore mounts on ship SURFACES rather than on the deck.
        /// </summary>
        public static bool IsInstrument(string? itemType) => itemType == ItemType;
    }
}
