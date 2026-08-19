using System;
using System.Collections.Generic;

namespace WorldsAdriftRebornGameServer.Multiplayer.Ship
{
    /// <summary>
    /// Which CRAFTED, MOUNTED ship parts are seeded as REAL Unity children of the hull
    /// rather than <c>"~"</c> position-followers - the mounted-part counterpart of
    /// <see cref="BoltedPartTransform.HierarchyKeyFor"/>, which answers the same question
    /// for the four STATIC parts bolted to the demo hull.
    ///
    /// WHY THIS EXISTS AT ALL. A mounted part's 190602 has always carried
    /// <c>Parent(hullId, "~")</c>, and the client's
    /// <c>RelativeParentTransformChildHierarchyBehaviour.TrySetNewParent</c> treats
    /// <c>"~"</c> as <c>SetNoParent()</c> (VERIFIED, decompile
    /// RelativeParentTransformChildHierarchyBehaviour.cs:34-42): the part TRACKS the hull's
    /// position every FixedUpdate but is never re-parented in the Unity scene graph. That
    /// one fact breaks FIVE separate client walks, every one of which climbs
    /// <c>transform.parent</c> and therefore cannot see a ship that is not an ancestor
    /// (roadmap 11.11.2, read line by line off the decompile):
    /// <list type="number">
    /// <item><c>DockableVisualizer</c> -&gt; <c>NeedToBeOnShip</c> (PlacementPreview.cs:664,
    ///   ShipPartPlacement.cs:132) - the BLUE "this is not on a ship" preview;</item>
    /// <item><c>flag4 = ShipPartPlacement.IsAttachedToShip(target)</c>
    ///   (PlayerScannerTool.cs:502) - <c>CanPlace</c> REQUIRES it and <c>_canDrop</c>
    ///   requires its negation, so nothing can be bolted onto the part at ANY
    ///   attachmentType. This is the blocker; it is not gated on the attachment string,
    ///   so no catalogue edit can move it;</item>
    /// <item>the ownership check (ShipPartPlacement.cs:98) - silently unsatisfiable;</item>
    /// <item><c>ShipPartVisualizer.AttachedShip</c>, which is what retail's OWN instrument
    ///   overlap exemption is written against (ShipInstrument.cs:10 -
    ///   ShipPartVisualizer.cs:131) - the RED preview;</item>
    /// <item><c>HasParentEntity</c> at commit (ShipPartPlacement.cs:213) - a green preview
    ///   whose click does nothing.</item>
    /// </list>
    /// Plus a sixth nobody had noticed: the exclusion-radius test
    /// (ShipPartPlacement.cs:153) is <c>ship.GetComponentsInChildren&lt;&gt;()</c>, so
    /// exclusion radii are UNENFORCED for every mounted part on this server today.
    ///
    /// THE MECHANISM IS ALREADY PROVEN HERE. <see cref="Deck.HierarchyKey"/> does exactly
    /// this for the walkable deck and is the live-confirmed carry fix; <c>Deck.cs:55-110</c>
    /// documents the client chain end to end. A non-<c>"~"</c> key sends the part down
    /// <c>TransformChildHierarchyBehaviour.TrySetNewParent</c> -&gt; a REAL
    /// <c>CachedTransform.parent = hull</c> (VERIFIED, TransformChildHierarchyBehaviour
    /// .cs:195-201 and NotifyParentTransformOffsetUpdated:194-199), and the part then rides
    /// the hull through the Unity hierarchy instead of being composed against it.
    ///
    /// WHY THE LIST IS TWO ROWS AND NOT "every mounted part". Two reasons, both hard:
    /// <list type="bullet">
    /// <item><b>A real parent DESTROYS the part's client-side rigidbody</b> (VERIFIED,
    ///   TransformManageRigidbodyBehaviour.SaveAndRemoveRigidbody, :224-243, reached from
    ///   ApplyParentUpdate :178-197 - which explicitly coerces a <c>"~"</c> key to "no
    ///   parent" first, :180-183). That is right and wanted for inert structure; it is NOT
    ///   right for the helm, engine and sail, which <see cref="BoltedPartTransform"/> says
    ///   in as many words must keep their own rigidbody.</item>
    /// <item><b>Bar pipes cannot regress anything.</b> The two pipe recipes shipped hours
    ///   before this change, so no bar pipe exists in any player's world. Every OTHER part
    ///   type is already bolted to live ships; changing their transform model is a second,
    ///   riskier step that wants its own live confirmation first.</item>
    /// </list>
    ///
    /// TWO PREFAB-BAKED ASSUMPTIONS ARE INVISIBLE OFFLINE and only a live client settles
    /// them, exactly as for Deck01: the BarPipe / BarPipeBent prefabs' authored
    /// <c>TransformNature</c> must have <c>GameObjectCanBeParented = true</c> (so the part
    /// carries a child-hierarchy behaviour at all) and <c>ShouldRemoveRigidbodyOnParented
    /// = true</c> (so the rigidbody is destroyed on parent, letting a raycast climb to the
    /// hull). Both are baked into the shipped prefab and are not in the decompiled C#.
    /// They FAIL SAFE: if <c>GameObjectCanBeParented</c> is false the key is simply
    /// ignored and the pipe behaves exactly as it does today.
    /// </summary>
    public static class MountedPartHierarchy
    {
        /// <summary>
        /// The 190602 <c>TransformState.parent</c> hierarchy key for a mounted part that
        /// is a REAL Unity child of the hull.
        ///
        /// A PLAIN word, with no leading <c>#</c>, on purpose: the client looks the key up
        /// in the hull's <c>TransformOffsetsRegistry</c> and, finding nothing, returns the
        /// hull ROOT transform (VERIFIED, TransformParentHierarchyBehaviour
        /// .GetTransformOffset:59-66 - <c>if (!Registry.TaggedOffsets.TryGetValue(key, out
        /// var value)) return base.transform;</c>). So the part parents directly under the
        /// hull at its own <c>localPosition</c>, which is exactly the hull-local offset the
        /// mount ledger already stores. Nothing has to be registered client-side for this
        /// key to resolve.
        ///
        /// DELIBERATELY NOT <see cref="Deck.HierarchyKey"/>, though the two resolve to the
        /// same transform: a distinct word keeps "this is a deck" and "this is a mounted
        /// part" apart in the server's own log line and in the client's per-key child
        /// registration, at zero behavioural cost.
        /// </summary>
        public const string HierarchyKey = "shippart";

        /// <summary>
        /// The mounted part item types (the <c>LoosePartCatalogue</c> schematic ids, which
        /// are what <c>MountedParts.Mount.ItemType</c> carries) that become real Unity
        /// children.
        ///
        /// KEYED ON ITEM TYPE, NOT PREFAB NAME, on purpose: the prefab is overridable at
        /// spawn time by a per-schematic environment variable (see <c>LoosePartSpawner</c>),
        /// so a prefab-keyed decision could be silently switched off by an operator config
        /// change, whereas the schematic id is the part's identity in the recipe table and
        /// in every persisted mount record. Ordinal comparison, because these are literal
        /// catalogue keys and never user text.
        /// </summary>
        public static readonly IReadOnlyList<string> UnityChildItemTypes = new[]
        {
            "barPipe",
            "barPipeBent",
        };

        /// <summary>
        /// The 190602 hierarchy key to seed and to wake a mounted part with: this module's
        /// <see cref="HierarchyKey"/> for a real Unity child, otherwise
        /// <see cref="BoltedPartTransform.RelativeSlotKey"/> - the unchanged <c>"~"</c>
        /// position-follow every other mounted part has always had.
        ///
        /// This is the ONE place the decision lives, so the checkout seed
        /// (<c>ComponentsSerializer</c>), the mount commit (<c>PartMountService</c>) and
        /// the in-flight wake (<c>ShipFlightService</c>) can never disagree about which
        /// parts are real children - the same single-source discipline
        /// <see cref="BoltedPartTransform.HierarchyKeyFor"/> keeps for the static hull. A
        /// seed with one key and a wake with another would re-parent and un-parent the part
        /// on alternate frames.
        /// </summary>
        public static string HierarchyKeyFor(string? itemType)
        {
            return IsUnityChild(itemType) ? HierarchyKey : BoltedPartTransform.RelativeSlotKey;
        }

        /// <summary>
        /// True when a mounted part is seeded as a REAL Unity child of the hull.
        ///
        /// THE WAKE HEARTBEAT MUST CONSULT THIS AND SKIP. A real-key part is dragged along
        /// by the hull's transform through the Unity hierarchy and needs no 190602 wake -
        /// and re-sending its <c>parent</c> field on a heartbeat is actively harmful:
        /// <c>TransformStateReader.ParentUpdated</c> fires on every update that carries the
        /// field, and <c>TransformChildHierarchyBehaviour.OnParentUpdated</c> begins with
        /// <c>ResetCurrentParent()</c>, which sets <c>CachedTransform.parent =
        /// OriginalParentTransform</c> - a full un-parent - before re-parenting (VERIFIED,
        /// TransformChildHierarchyBehaviour.cs:254-292). At the flight wake cadence that is
        /// an un-parent/re-parent of the part's Unity transform several times a second,
        /// which is what "it jitters" would look like. A <c>"~"</c> follower, by contrast,
        /// NEEDS the wake: its follow-visualizer sleeps a second after its last transform
        /// change (see <c>ShipPartMotionPolicy</c>) and would park while the hull flies.
        /// </summary>
        public static bool IsUnityChild(string? itemType)
        {
            if (itemType == null)
            {
                return false;
            }
            for (int i = 0; i < UnityChildItemTypes.Count; i++)
            {
                if (string.Equals(UnityChildItemTypes[i], itemType, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }
    }
}
