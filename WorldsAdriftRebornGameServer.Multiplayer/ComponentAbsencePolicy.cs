namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// What happened when the server was asked to produce one component for one
    /// entity. Replaces "length came back 0, good luck" as the answer
    /// <c>ComponentsSerializer.InitAndSerialize</c> gives its caller.
    ///
    /// The distinction that matters is <see cref="KnownAbsent"/> versus
    /// <see cref="UnhandledId"/>. They are the same event to a length-only
    /// caller - no bytes - and opposite events to a human:
    ///
    /// * <see cref="KnownAbsent"/> = we DECIDED this entity does not have that
    ///   component. In real SpatialOS an entity simply lacks a component and the
    ///   interest answer omits it. Nothing is wrong. Do not fail the batch, do
    ///   not print an error.
    /// * <see cref="UnhandledId"/> = the client asked for something nobody has
    ///   ever thought about. That is a gap, it is how every new entity type has
    ///   announced itself so far, and it must stay LOUD.
    ///
    /// Collapsing those two is the one way this mechanism can do harm, which is
    /// why they are separate enum members, separate log lines and separate
    /// tests rather than a bool.
    /// </summary>
    public enum ComponentSeedOutcome
    {
        /// <summary>Bytes were produced. Put it in the batch.</summary>
        Serialized,

        /// <summary>
        /// Deliberately not served: this entity does not have this component.
        /// Omit it and carry on - see <see cref="ComponentAbsencePolicy"/>.
        /// </summary>
        KnownAbsent,

        /// <summary>
        /// The id exists in this client build but has no seed branch in
        /// ComponentsSerializer. A GAP. Loud, and fatal to the batch when the
        /// caller asked for all-or-nothing.
        /// </summary>
        UnhandledId,

        /// <summary>
        /// The id has no vtable in the shipped client at all, so no branch could
        /// ever satisfy it. Loud.
        /// </summary>
        NoClientVtable,

        /// <summary>
        /// A seed was built and the client's own serializer still produced no
        /// bytes. Rare, and never benign.
        /// </summary>
        SerializeFailed,
    }

    /// <summary>
    /// The components this server deliberately does NOT put on entities, and the
    /// rule for how that is reported.
    ///
    /// THE PROBLEM THIS EXISTS TO SOLVE. Our interest handler answers whatever
    /// the client asks for. A real SpatialOS deployment answers a
    /// <c>ComponentInterest</c> with the subset the entity actually HAS; ours had
    /// no way to express "it does not have that", so it invented data instead.
    /// For 1139 that invention was actively destructive - see
    /// docs/research/diag/findings-weather-storm.md:
    ///
    /// The client turns 1139 <c>WeatherCellState</c> into a grid cell by flooring
    /// the entity's position onto a 500 m lattice and keying a dictionary on the
    /// Cantor pair of that cell. Cantor pairing is a bijection, so equal ids mean
    /// equal cells - never a hash accident. Every entity this server spawns
    /// stands on one 60 m island, so all of them landed in cell (34,-3), id 2857.
    /// One won the dictionary; the rest hit the third branch of
    /// <c>AddToIdComponentToEntityMapS</c>, which logs an error but - unlike the
    /// other two branches - does not mark the entity, so it never leaves the
    /// filter and loses again on the next tick. Measured on a live two-player
    /// session: 31,144 errors in 158 s, ~197/s, one per frame per loser, each
    /// with a 14-frame stack trace built on the client's main thread. Every
    /// entity ever added to the world cost another ~49.5 lines/s, permanently.
    ///
    /// The real game only ever put 1139 on dedicated weather-cell entities, laid
    /// out one cell apart by <c>WeatherCellGenesisS</c> - injective by
    /// construction. It was an "I am a weather cell" marker. A player is not a
    /// weather cell.
    ///
    /// WHY THIS IS A SET AND NOT A DELETED BRANCH. Every interest call site
    /// passes <c>failOnComponentInitError: true</c>, and one id with no seed
    /// drops the ENTIRE batch - which would take 190602 TransformState with it
    /// and leave a rendered, inert entity with nothing in the client log.
    /// "Absent on purpose" therefore has to be a THIRD answer, distinct from
    /// both "serialized" and "I have no idea what that is".
    ///
    /// WHY EVERY ENTITY AND NOT ONE CARRIER. Keeping 1139 on exactly one entity
    /// would also give zero collisions, and it was rejected for three reasons:
    ///
    /// 1. <b>It looks worse.</b> <c>GlobalWeather.GetWeatherAt</c> samples FOUR
    ///    cells and interpolates. With no cells at all every sample is the same
    ///    documented default - wind (1,0,-2), pressure 0.5
    ///    (<c>GlobalWeather.GetCellSampleAt</c>, acs/Assets.Visualizers.Weather/GlobalWeather.cs:55-69)
    ///    - so the wind field is uniform and continuous. With one carrier, one
    ///    corner of the interpolation reads our fabricated pressure 1.0 and zero
    ///    wind while its three neighbours read the default: a weather seam
    ///    centred exactly on the island the players are standing on.
    /// 2. <b>It keeps the fiction alive.</b> The carrier would BE a weather cell
    ///    as far as the client is concerned, including to
    ///    <c>WeatherCellGenesisS.RemoveExistingWeatherCellEntities()</c>, which
    ///    drops anything that <c>Contains&lt;WeatherCellState&gt;()</c>. Pointing
    ///    that at our island is a hazard with no upside.
    /// 3. <b>It needs state.</b> "Which entity carries it" has to survive that
    ///    entity despawning - a player logging out - or the map entry goes with
    ///    them. Absence needs no bookkeeping at all.
    ///
    /// This is why the question here is <see cref="IsKnownAbsent(uint)"/> and
    /// takes no entity id: absence is a property of the component, and staying
    /// that way is the point.
    /// </summary>
    public static class ComponentAbsencePolicy
    {
        /// <summary>
        /// WeatherCellState. The one that caused the storm; see the type remarks.
        /// </summary>
        public const uint WeatherCellStateComponentId = 1139;

        /// <summary>
        /// RadialStormState - the blight/storm sphere's weight.
        ///
        /// Fabricated by exactly the same reflex as 1139 (weight 0f on every
        /// entity that asked), and absent for the same reason: our entities are
        /// not storms. Unlike 1139 it was costing nothing, so it is included on
        /// evidence rather than on principle:
        ///
        /// * It is in no <c>IdComponentToEntityMap</c>, so it cannot collide. The
        ///   only weather map is <c>WeatherCellCoordsC</c>.
        /// * Every consumer in the shipped client is a Blight system, and every
        ///   one of their filters ALSO requires <c>BlightLocalComponent</c> -
        ///   Activate, Deactivate, View, ModelComposite, UpdateRadius,
        ///   DrawGizmos, Retarget, RefineTargetPositions, UpdateWithin. Nothing
        ///   in the client adds that flag to a Traveller, an island, a hull or a
        ///   tree. Two of them additionally require authority over 1269, which
        ///   this server grants to nobody (<see cref="MirrorSendPolicy.AuthoritativeComponents"/>).
        /// * Every store access sits inside one of those filters, so absence
        ///   cannot produce a null read.
        ///
        /// So a weight-0 radial storm on a player was unreachable code either
        /// way, and omitting it makes the mechanism general instead of a
        /// special case for one id.
        /// </summary>
        public const uint RadialStormStateComponentId = 1269;

        // ------------------------------------------------------------------
        // LOOSE-SHIP-PART physics/cosmetic states this server never authors.
        //
        // A crafted ship part is a world entity carrying ShipPartVisualizer (which
        // makes it render and LIFT). The ShipFrame/ship-part prefabs also bake a
        // handful of OTHER visualizers whose readers the client dutifully requests
        // over interest on every checkout of the part (and of a hull):
        //   ParentingMassAdderVisualizer         -> 1257 ParentingMassAdderState
        //   ShipPartShipyardInformationVisualizer -> 1121 OriginalMassState
        //   LightningStrikableVisualizer          -> 1225 LightningStrikableState
        //   Joint/DetachFromParentWhenUnderHealthThresholdVisualizer -> 1235
        //     DetachFromParentWhenUnderHealthThresholdState
        // (ids VERIFIED off the decompiled gencode; the requesters are baked by
        // ShipPartPreprocessor.cs:18-38 and ShipPreprocessor.cs, ShipRecognition.cs:35
        // already lists 1257/1121 among "components we do not seed").
        //
        // Some loose parts additionally bake a PART-SPECIFIC control visualizer. The HELM
        // (a pilot seat) carries HelmVisualizer, which [Require]s ShipControlInputReader ->
        // 1111 ShipControlInput (HelmVisualizer.cs:10-11, confirmed on the wire as
        // "1111 => Bossa.Travellers.Ship.ShipControlInput"). This server simulates no ship
        // piloting input, so it authors no ShipControlInput for any entity; left unhandled it
        // fails the helm's all-or-nothing interest batch ("failed to initialize component 1111
        // of entity NN") so the helm never renders/lifts. It is off the lift path (not in
        // ShipPartVisualizer's [Require] set) - the helm renders and lifts as a loose part with
        // HelmVisualizer simply disabled - so it is KNOWN-ABSENT like the four above.
        //
        // This server simulates none of that physics (mass aggregation, lightning,
        // damage-detach), so it authors NO such state for ANY entity - there is not a
        // single serve branch or seed for these four ids. Left unhandled, each logs a
        // loud "[ToDo] unhandled component id ... (entity NN)" on every loose-part and
        // hull checkout, and any that rides a client all-or-nothing interest batch
        // risks dropping it. Declaring them KNOWN-ABSENT is the honest statement - our
        // entities do not HAVE these functional states - and makes a part's checkout
        // serialize cleanly while the corresponding visualizers simply stay disabled
        // (none of them is on the lift path: ShipPartVisualizer's own [Require] set -
        // 8066/1120/190602/190601/1016/1013 - is fully seeded).
        // ------------------------------------------------------------------

        /// <summary>1257 ParentingMassAdderState - ship-part mass aggregation this server does not simulate.</summary>
        public const uint ParentingMassAdderStateComponentId = 1257;

        /// <summary>1121 OriginalMassState - the shipyard-info visualizer's mass readout; no server mass model.</summary>
        public const uint OriginalMassStateComponentId = 1121;

        /// <summary>1225 LightningStrikableState - no weather/lightning simulation, so our parts are not strikable.</summary>
        public const uint LightningStrikableStateComponentId = 1225;

        /// <summary>1235 DetachFromParentWhenUnderHealthThresholdState - no damage/detach model on our parts.</summary>
        public const uint DetachFromParentWhenUnderHealthThresholdStateComponentId = 1235;

        /// <summary>1111 ShipControlInput - the helm/pilot-seat control-input reader (HelmVisualizer); this server simulates no ship piloting input.</summary>
        public const uint ShipControlInputComponentId = 1111;

        // ------------------------------------------------------------------
        // SHIP-ENTITY cosmetic/identity states this server never authors.
        //
        // A ship-shaped world entity (a built/atlas ship hull) bakes two further
        // visualizers whose readers the client requests over interest, but which this
        // server produces no state for. Left unhandled, each is an UnhandledId that
        // fails the ship's all-or-nothing interest batch - dropping the WHOLE 19-id
        // batch (1114/1130/1111/1222/1225/1232/4333/1294/1306/4400/190604...) so the
        // ship entity never loads on the client (VERIFIED live: "[ToDo] unhandled
        // component id needs investigation: 1294 (entity 8). NOT known-absent" + 1306).
        // Both readers are OFF any render/lift path and are safe DISABLED:
        //   1294 UidState -> UidVisualizer (UidVisualizer.cs): a bare `long Uid`
        //     accessor, no OnEnable behaviour; disabled = the uid is unavailable, an
        //     information-only visualizer that touches nothing.
        //   1306 ShipAtlasPulseState -> ShipAtlasPulseVisualizer (Assets.Visualizers):
        //     the atlas-core PULSE cosmetic; OnEnable only subscribes to an event,
        //     disabled = no pulse animation (grapple/climb simply stay permitted).
        // So declaring them absent lets the ship's batch serialize and the hull load,
        // with only a uid readout and a core-pulse effect dormant.
        // ------------------------------------------------------------------

        /// <summary>1294 UidState - the uid readout (UidVisualizer); this server authors no per-entity uid state, and the visualizer is information-only.</summary>
        public const uint UidStateComponentId = 1294;

        /// <summary>1306 ShipAtlasPulseState - the atlas-core pulse cosmetic (ShipAtlasPulseVisualizer); this server drives no atlas pulse.</summary>
        public const uint ShipAtlasPulseStateComponentId = 1306;

        /// <summary>
        /// The whole set. Deliberately tiny, and every entry has to earn its
        /// place with a client-side reason why absence is SAFE - not merely
        /// harmless-looking. Every entry here has one:
        ///
        /// * 1139 - <c>GlobalWeather.GetCellSampleAt</c> already returns a
        ///   default on a map miss, so a world with no weather cells is a
        ///   supported state of the shipped client.
        /// * 1269 - every consumer is gated behind a local flag component our
        ///   entities never receive.
        /// * 1257 / 1121 / 1225 / 1235 - loose-ship-part physics/cosmetic states
        ///   this server authors for no entity; the visualizers that read them are
        ///   off the lift path and safe disabled (see the block above).
        /// * 1111 - the helm's ShipControlInput (pilot-seat control input); this
        ///   server simulates no piloting, HelmVisualizer is off the lift path and
        ///   safe disabled, so the helm still renders and lifts as a loose part.
        /// * 1294 / 1306 - a ship entity's UidState (information-only UidVisualizer)
        ///   and ShipAtlasPulseState (cosmetic core-pulse ShipAtlasPulseVisualizer);
        ///   both readers are off any render/lift path and safe disabled, so the ship
        ///   hull loads instead of its whole 19-id batch dropping on these two.
        ///
        /// An id belongs here only when the entity genuinely does not have the
        /// thing. It is NOT a place to park a component that is merely hard to
        /// seed: that is <see cref="ComponentSeedOutcome.UnhandledId"/>, and it
        /// is supposed to hurt.
        /// </summary>
        public static readonly IReadOnlyList<uint> KnownAbsentComponentIds = new uint[]
        {
            WeatherCellStateComponentId,
            RadialStormStateComponentId,
            ParentingMassAdderStateComponentId,
            OriginalMassStateComponentId,
            LightningStrikableStateComponentId,
            DetachFromParentWhenUnderHealthThresholdStateComponentId,
            ShipControlInputComponentId,
            UidStateComponentId,
            ShipAtlasPulseStateComponentId,
        };

        /// <summary>
        /// Whether this server has decided that no entity of ours has this
        /// component. Entity-independent on purpose - see the type remarks.
        /// </summary>
        public static bool IsKnownAbsent(uint componentId)
        {
            for (int i = 0; i < KnownAbsentComponentIds.Count; i++)
            {
                if (KnownAbsentComponentIds[i] == componentId)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// A human name for the ids in the set, so the log line says what was
        /// omitted rather than only which number. Unknown ids come back as the
        /// number, because this is a log line and not a lookup table.
        /// </summary>
        public static string NameOf(uint componentId)
        {
            if (componentId == WeatherCellStateComponentId)
            {
                return "WeatherCellState";
            }
            if (componentId == RadialStormStateComponentId)
            {
                return "RadialStormState";
            }
            if (componentId == ParentingMassAdderStateComponentId)
            {
                return "ParentingMassAdderState";
            }
            if (componentId == OriginalMassStateComponentId)
            {
                return "OriginalMassState";
            }
            if (componentId == LightningStrikableStateComponentId)
            {
                return "LightningStrikableState";
            }
            if (componentId == DetachFromParentWhenUnderHealthThresholdStateComponentId)
            {
                return "DetachFromParentWhenUnderHealthThresholdState";
            }
            if (componentId == ShipControlInputComponentId)
            {
                return "ShipControlInput";
            }
            if (componentId == UidStateComponentId)
            {
                return "UidState";
            }
            if (componentId == ShipAtlasPulseStateComponentId)
            {
                return "ShipAtlasPulseState";
            }
            return componentId.ToString();
        }

        /// <summary>
        /// Whether the outcome produced bytes that belong in the AddComponent
        /// batch. Only <see cref="ComponentSeedOutcome.Serialized"/> does.
        /// </summary>
        public static bool BelongsInBatch(ComponentSeedOutcome outcome)
        {
            return outcome == ComponentSeedOutcome.Serialized;
        }

        /// <summary>
        /// Whether this outcome must destroy the whole batch.
        ///
        /// THE ENTIRE POINT: <see cref="ComponentSeedOutcome.KnownAbsent"/> never
        /// does, whatever the caller asked for, because an entity that lacks a
        /// component is not a failure. Every other non-success outcome still
        /// obeys <paramref name="failOnComponentInitError"/> exactly as before,
        /// so a genuinely unhandled id keeps costing the caller its batch and
        /// keeps being impossible to ignore.
        /// </summary>
        public static bool DropsBatch(ComponentSeedOutcome outcome, bool failOnComponentInitError)
        {
            if (outcome == ComponentSeedOutcome.Serialized || outcome == ComponentSeedOutcome.KnownAbsent)
            {
                return false;
            }
            return failOnComponentInitError;
        }

        /// <summary>
        /// The log line for a deliberate omission. Quiet by construction: no
        /// <c>[error]</c>, no <c>[ToDo]</c>, no "failed", nothing that reads as a
        /// fault to a human skimming a server log or to a grep looking for one.
        /// Its own prefix, so it can still be counted.
        /// </summary>
        public static string DescribeKnownAbsent(long entityId, uint componentId)
        {
            return "[known-absent] entity " + entityId + " has no component " + componentId
                + " (" + NameOf(componentId) + "); omitted from the batch by ComponentAbsencePolicy."
                + " This is a decision, not a fault - the batch continues.";
        }

        /// <summary>
        /// The log line for an id nobody predicted. Keeps the historic
        /// <c>[ToDo] unhandled component id</c> wording - it is what the
        /// diagnostic notes and previous findings grep for - and adds the entity
        /// and an explicit denial that this is the quiet case, so the two can
        /// never be read as the same event.
        /// </summary>
        public static string DescribeUnhandled(long entityId, uint componentId)
        {
            return "[ToDo] unhandled component id needs investigation: " + componentId
                + " (entity " + entityId + "). NOT known-absent: nobody has decided this entity"
                + " lacks it, so this is a missing seed in ComponentsSerializer.";
        }

        /// <summary>
        /// One line per batch recording how many components were deliberately
        /// left out, so "the client asked for four and got three" is visible
        /// without turning per-component lines into a firehose.
        /// </summary>
        public static string DescribeBatchOmissions(long entityId, uint requested, int sent, int knownAbsent)
        {
            return "[interest] entity " + entityId + ": serialized " + sent + " of " + requested
                + " requested component(s); " + knownAbsent + " omitted as known-absent"
                + " (the entity does not have them).";
        }
    }
}
