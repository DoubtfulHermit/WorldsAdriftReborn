using Improbable.Collections;
using Improbable.Corelibrary.Transforms;
using Improbable.Corelibrary.Transforms.Teleport;
using Improbable.Math;
using WorldsAdriftRebornGameServer.Multiplayer;

namespace WorldsAdriftRebornGameServer.Game.Components
{
    /// <summary>
    /// Builds the two 190607 TeleportRequestState payloads: the seed that arrives
    /// with the player entity, and the update that actually moves someone.
    ///
    /// It lives in its own file so the branch this adds to
    /// <see cref="ComponentsSerializer"/> is four lines. That file is a single
    /// 600-line else-if chain that three workstreams are editing at once; every
    /// line of teleport logic left out of it is a merge conflict that does not
    /// happen.
    ///
    /// WHY 190607 AT ALL, and why it is cheap: the shipped player prefab carries
    /// three readers of it, and only <c>TeleportTransformVisualizer</c> can
    /// enable on this server - it requires 190602 Reader, 1073 WRITER and 190607
    /// Reader, and we already grant 1073. It sets <c>transform.position</c>
    /// directly, calls <c>PlayerMove.Respawn</c> (zeroing velocity) and acks by
    /// writing 1073 <c>lastExecutedRequest</c>. No new authority grant, no client
    /// patch. The rules that make it work - seed request 0, send parent absent -
    /// are stated and tested in <see cref="TeleportPolicy"/>.
    /// </summary>
    internal static class TeleportComponent
    {
        /// <summary>
        /// The 190607 seed. Every optional field ABSENT and
        /// <c>request</c> = <see cref="TeleportPolicy.SeedRequest"/>.
        ///
        /// Both halves are load-bearing:
        ///
        /// * <b>request must be 0.</b> The generated <c>RequestUpdated</c> event
        ///   replays the current value the instant a subscriber attaches, and
        ///   <c>TeleportTransformVisualizer</c> subscribes in OnEnable. Any
        ///   non-zero seed teleports the player the moment they finish loading.
        ///   It also makes a RE-serve harmless: this method fabricates the seed
        ///   fresh each time the client asks for the component, and 0 can never
        ///   beat a <c>lastExecutedRequest</c> that is already >= 0.
        /// * <b>parent must be absent</b>, and stays absent because no update
        ///   ever sets it. The visualizer's live branch is literally
        ///   <c>if (!Parent.HasValue)</c>; with a parent present it computes a
        ///   GameObject name, throws it away, moves nothing - and still acks.
        ///
        /// localPosition and localRotation are absent too: an absent position in
        /// the seed is what guarantees the seed itself can never displace
        /// anybody, whatever else changes around it.
        /// </summary>
        public static object Seed()
        {
            return new TeleportRequestState.Data(new TeleportRequestStateData(
                new Option<Vector3d>(),                             // localPosition: ABSENT
                new Option<Improbable.Corelib.Math.Quaternion>(),   // localRotation: ABSENT
                new Option<Parent>(),                               // parent: ABSENT - see remarks
                TeleportPolicy.SeedRequest));
        }

        /// <summary>
        /// The update that moves a player: <c>localPosition</c> set,
        /// <c>request</c> bumped, everything else untouched.
        ///
        /// "Untouched" is not the same as "cleared". The generated writer emits a
        /// FieldsToClear entry for an Option that is present-in-update but empty,
        /// and nothing at all for a field the update never mentions. Leaving
        /// <c>parent</c> unmentioned means the client keeps whatever it has -
        /// which, because the seed above never set one, is nothing. That is
        /// exactly the state the working branch needs, and it is why this method
        /// must not "helpfully" clear the parent.
        ///
        /// Rotation is left alone deliberately: the visualizer only writes
        /// <c>transform.rotation</c> when localRotation is present, so omitting
        /// it means the player keeps facing wherever they were facing. Teleport
        /// should not also spin you.
        ///
        /// The position crosses the wire as <c>Vector3d</c> - three doubles, not
        /// the FixedPointVector3 that 190602 uses. Both are fed through the same
        /// <c>RemapGlobalToUnityVector()</c> on the client, so the NUMBERS are the
        /// same global-metre space; <see cref="FixedPointPosition"/> is the one
        /// representation this server keeps them in, converted here at the wire
        /// edge.
        /// </summary>
        public static object Request(FixedPointPosition destination, int request)
        {
            TeleportRequestState.Update update = new TeleportRequestState.Update();
            update.SetLocalPosition(new Option<Vector3d>(
                new Vector3d(destination.MetresX, destination.MetresY, destination.MetresZ)));
            update.SetRequest(request);
            return update;
        }
    }
}
