namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    /// <summary>
    /// The three retail numbers that together decide how far a distant island's
    /// impostor quad may turn away from the direction its texture was rendered
    /// from. Field names map 1:1 onto the retail components so the diagnostic
    /// log and this policy can be read against each other.
    /// </summary>
    public struct ImpostorBillboardSettings
    {
        /// <summary>
        /// <c>ImposterSystem.ImposterController.errorCameraAngle</c>, retail 2.5.
        /// The camera may move this many degrees around the object before
        /// <c>needUpdate()</c> asks for a fresh bake.
        /// </summary>
        public float RebakeAngleDegrees;

        /// <summary>
        /// <c>ImposterSystem.ImpostersHandler.minAngleToStopLookAtCamera</c>,
        /// retail 30, declared <c>[Range(0,45)]</c>. Pushed to the impostor
        /// shader every LateUpdate as <c>_ImposterSystem_MinAngleToStopLookAtCamera</c>.
        /// The quad keeps turning to face the viewer until it is this far from
        /// the baked direction, then holds.
        /// </summary>
        public float FollowAngleDegrees;

        /// <summary>
        /// <c>ImposterSystem.ImposterController.timeInterval</c>, 10 for islands
        /// (<c>IslandVisualiser.SetupIslandImpostors</c>) and 5 for ships.
        /// </summary>
        public float RebakeSeconds;

        /// <summary>
        /// <c>ImposterSystem.ImposterController.useUpdateByTime</c>. NOTE this is
        /// ADDITIVE in retail, not exclusive: <c>needUpdate()</c> checks the timer
        /// first and returns early, then falls through to the angle, light,
        /// resolution and distance checks regardless. Turning it on does not turn
        /// the angle path off.
        /// </summary>
        public bool RebakeOnTime;
    }

    /// <summary>
    /// Bounds how far a runtime-baked island impostor may swing.
    ///
    /// The retail impostor is a single camera-facing quad drawn out of an atlas
    /// (<c>ImposterSystem.AtlasHandler.DrawAll</c> issues one <c>Graphics.DrawMesh</c>
    /// at the world origin with identity rotation), so every quad's world
    /// placement is reconstructed in the vertex shader: position from the vertex
    /// COLOUR, baked view direction from the vertex NORMAL
    /// (<c>CombinedImpostersMesh.UpdateNormals(place, lastUpdateConfig.cameraDirection)</c>),
    /// and a per-vertex flag in <c>uv.z</c> carrying <c>alwaysLookAtCamera</c>.
    ///
    /// That leaves TWO independent angles, and retail sets them twelve-fold
    /// apart:
    ///
    ///   * the quad re-orients toward the viewer continuously, and only stops
    ///     once it is <see cref="ImpostorBillboardSettings.FollowAngleDegrees"/>
    ///     (30) from the baked direction;
    ///   * the TEXTURE is only re-rendered once the viewer has moved
    ///     <see cref="ImpostorBillboardSettings.RebakeAngleDegrees"/> (2.5)
    ///     around the object.
    ///
    /// While the re-bake keeps up, the 30 is never reached and nobody sees it:
    /// the quad is realigned every 2.5 degrees. The 30 is the behaviour when a
    /// re-bake is LATE, and it is late for ordinary reasons - the request goes
    /// through <c>ImpostersHandler.queueOfImposters</c>, which is drained in
    /// LateUpdate at <c>maxUpdatesPerFrame</c> (20) bakes per frame shared across
    /// every impostor and camera in the scene, and each bake is two full camera
    /// renders of the island. Behind a backlog the island keeps rotating to face
    /// the viewer while wearing a silhouette photographed from somewhere else,
    /// which is exactly the "islands turn towards me as I move" artefact.
    ///
    /// The rule: the quad must never turn further from its baked direction than
    /// the angle that triggers a re-bake. Clamping the follow angle to the
    /// re-bake angle costs nothing - no extra bakes, no extra draws, and no
    /// visible change at all while the queue is keeping up, because in that
    /// regime the quad never turns that far anyway. It only removes the
    /// unbounded swing that appears when the queue is not.
    /// </summary>
    public static class ImpostorBillboardPolicy
    {
        /// <summary><c>ImpostersHandler.minAngleToStopLookAtCamera</c> as retail ships it.</summary>
        public const float RetailFollowAngleDegrees = 30f;

        /// <summary><c>ImposterController.errorCameraAngle</c> as retail ships it.</summary>
        public const float RetailRebakeAngleDegrees = 2.5f;

        /// <summary><c>IslandVisualiser.SetupIslandImpostors</c> sets 10 seconds.</summary>
        public const float RetailIslandRebakeSeconds = 10f;

        /// <summary>Retail declares the follow angle <c>[Range(0f, 45f)]</c>.</summary>
        public const float MaxFollowAngleDegrees = 45f;

        /// <summary>
        /// A floor, not a preference. At zero the quad would hold the baked
        /// orientation exactly and every re-bake would land as a visible snap
        /// instead of a correction, trading one artefact for another.
        /// </summary>
        public const float MinFollowAngleDegrees = 0.5f;

        /// <summary>A re-bake threshold below this would thrash the bake queue.</summary>
        public const float MinRebakeAngleDegrees = 0.25f;

        /// <summary>Retail's own settings, for tests and for the diagnostic log.</summary>
        public static ImpostorBillboardSettings RetailIslandSettings()
        {
            ImpostorBillboardSettings settings = new ImpostorBillboardSettings();
            settings.RebakeAngleDegrees = RetailRebakeAngleDegrees;
            settings.FollowAngleDegrees = RetailFollowAngleDegrees;
            settings.RebakeSeconds = RetailIslandRebakeSeconds;
            settings.RebakeOnTime = true;
            return settings;
        }

        /// <summary>
        /// How far the quad turns away from its texture while re-bakes are
        /// arriving on time: the re-bake realigns it, so whichever of the two
        /// angles is smaller wins.
        /// </summary>
        public static float SteadyStateSwingDegrees(ImpostorBillboardSettings settings)
        {
            float rebake = settings.RebakeAngleDegrees;
            float follow = settings.FollowAngleDegrees;
            return rebake < follow ? rebake : follow;
        }

        /// <summary>
        /// How far the quad turns away from its texture when a re-bake is late -
        /// queued behind other impostors, or not requested at all because the
        /// island is outside the frustum and <c>WillRendered()</c> is not being
        /// called. The re-bake angle does not bound this; only the follow angle
        /// does.
        /// </summary>
        public static float StaleSwingDegrees(ImpostorBillboardSettings settings)
        {
            return settings.FollowAngleDegrees;
        }

        /// <summary>
        /// True when a late re-bake cannot make the island swing further than a
        /// timely one already does. False for retail's shipped numbers.
        /// </summary>
        public static bool IsSwingBounded(ImpostorBillboardSettings settings)
        {
            return StaleSwingDegrees(settings) <= SteadyStateSwingDegrees(settings);
        }

        /// <summary>
        /// How many times looser the follow tolerance is than the re-bake
        /// trigger. Retail islands: 12.
        /// </summary>
        public static float SwingToleranceRatio(ImpostorBillboardSettings settings)
        {
            if (settings.RebakeAngleDegrees <= 0f) return 0f;
            return settings.FollowAngleDegrees / settings.RebakeAngleDegrees;
        }

        /// <summary>
        /// The follow angle to write onto <c>ImpostersHandler</c>. A positive
        /// <paramref name="requestedOverride"/> is an explicit operator choice
        /// (mod config); zero or negative means "track the re-bake angle", which
        /// is the rule this policy exists for.
        /// </summary>
        public static float FollowAngleFor(float rebakeAngleDegrees, float requestedOverride)
        {
            float wanted = requestedOverride > 0f ? requestedOverride : rebakeAngleDegrees;
            if (wanted < MinFollowAngleDegrees) return MinFollowAngleDegrees;
            if (wanted > MaxFollowAngleDegrees) return MaxFollowAngleDegrees;
            return wanted;
        }

        /// <summary>
        /// The re-bake angle to write onto an island's <c>ImposterController</c>.
        /// Retail's value is kept unless the operator asks for another.
        /// </summary>
        public static float RebakeAngleFor(float retailValue, float requestedOverride)
        {
            if (requestedOverride <= 0f) return retailValue;
            if (requestedOverride < MinRebakeAngleDegrees) return MinRebakeAngleDegrees;
            if (requestedOverride > MaxFollowAngleDegrees) return MaxFollowAngleDegrees;
            return requestedOverride;
        }

        /// <summary>
        /// The re-bake timer to write onto an island's <c>ImposterController</c>.
        /// Retail's 10 seconds is kept unless the operator asks for another; the
        /// timer is a backstop for changes the camera-angle test cannot see (an
        /// island whose static props finished spawning, a viewer moving straight
        /// at it), so shortening it buys staleness cover at the price of bakes
        /// nothing asked for.
        /// </summary>
        public static float RebakeSecondsFor(float retailValue, float requestedOverride)
        {
            if (requestedOverride <= 0f) return retailValue;
            return requestedOverride < 0.25f ? 0.25f : requestedOverride;
        }

        /// <summary>
        /// The settings the client should end up running, given retail's and the
        /// operator's overrides. Applying this makes <see cref="IsSwingBounded"/>
        /// true unless the operator explicitly asked for a looser follow angle.
        /// </summary>
        public static ImpostorBillboardSettings Correct(
            ImpostorBillboardSettings retail,
            float followOverride,
            float rebakeAngleOverride,
            float rebakeSecondsOverride)
        {
            ImpostorBillboardSettings corrected = new ImpostorBillboardSettings();
            corrected.RebakeAngleDegrees =
                RebakeAngleFor(retail.RebakeAngleDegrees, rebakeAngleOverride);
            corrected.FollowAngleDegrees =
                FollowAngleFor(corrected.RebakeAngleDegrees, followOverride);
            corrected.RebakeSeconds =
                RebakeSecondsFor(retail.RebakeSeconds, rebakeSecondsOverride);
            corrected.RebakeOnTime = retail.RebakeOnTime;
            return corrected;
        }
    }
}
