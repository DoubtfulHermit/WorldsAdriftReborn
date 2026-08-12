namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// What one salvage shot on a FUEL CANISTER did: how much fuel it freed, whether
    /// it was the shot that emptied the canister, and which shot number it was.
    ///
    /// UNLIKE the metal path, EVERY shot pays out. A metal node grants only on the
    /// single deplete transition (<see cref="MetalHitOutcome"/>); a fuel canister
    /// yields fuel on each of its three shots - 8, 8, then 9 - so
    /// <see cref="FuelGranted"/> is non-zero on all three, and
    /// <see cref="Depleted"/> is true only on the last.
    /// </summary>
    public readonly struct FuelHitOutcome : IEquatable<FuelHitOutcome>
    {
        public FuelHitOutcome(int fuelGranted, bool depleted, int shotNumber)
        {
            FuelGranted = fuelGranted;
            Depleted = depleted;
            ShotNumber = shotNumber;
        }

        /// <summary>Fuel units this shot freed. 8, 8 or 9 on a live canister; 0 otherwise.</summary>
        public int FuelGranted { get; }

        /// <summary>True on the single shot that emptied the canister (the third).</summary>
        public bool Depleted { get; }

        /// <summary>Which shot this was, 1-based. 0 when the shot did nothing.</summary>
        public int ShotNumber { get; }

        /// <summary>Whether this shot is worth granting for.</summary>
        public bool Granted => FuelGranted > 0;

        /// <summary>The shot changed nothing (not a canister, or already empty).</summary>
        public static FuelHitOutcome Nothing => new FuelHitOutcome(0, false, 0);

        public bool Equals(FuelHitOutcome other) =>
            FuelGranted == other.FuelGranted && Depleted == other.Depleted && ShotNumber == other.ShotNumber;

        public override bool Equals(object? obj) => obj is FuelHitOutcome other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(FuelGranted, Depleted, ShotNumber);

        public override string ToString() =>
            ShotNumber == 0 ? "no-op" : "shot " + ShotNumber + ": " + FuelGranted + " fuel"
                + (Depleted ? " (depleted)" : "");
    }

    /// <summary>
    /// The RETAIL per-shot fuel yield schedule of one fuel canister.
    ///
    /// RECOVERED, not invented: the official wiki / community guides record that a
    /// fuel canister takes THREE gauntlet salvage shots yielding <b>8, then 8, then
    /// 9 fuel - 25 total</b> (worldsadrift.fandom.com/wiki/Fuel, /wiki/Resources,
    /// /wiki/Mining). That is a real preserved number, so it is encoded here verbatim
    /// rather than approximated, and the uneven last shot (9, not 8) is preserved
    /// deliberately - it is the distinctive part of the real curve.
    ///
    /// This is the fuel analogue of <see cref="MetalHarvest"/>'s yield sizing, but a
    /// SCHEDULE rather than a single lump: metal pays out once, on the shot that
    /// destroys the node; fuel pays out on every shot.
    ///
    /// Pure: no ENet, no Improbable types, no game install.
    /// </summary>
    public static class FuelCanisterYield
    {
        /// <summary>
        /// Fuel granted by shot 1, 2 and 3 of a canister, in order. RECOVERED retail
        /// values (8/8/9). The array's length IS <see cref="ShotsToDeplete"/>.
        /// </summary>
        public static readonly IReadOnlyList<int> Schedule = new[] { 8, 8, 9 };

        /// <summary>Salvage shots to empty one canister. Three, per the recovered retail figure.</summary>
        public static int ShotsToDeplete => Schedule.Count;

        /// <summary>Total fuel one whole canister is worth: 25.</summary>
        public static int TotalFuel
        {
            get
            {
                int total = 0;
                foreach (int units in Schedule)
                {
                    total += units;
                }
                return total;
            }
        }

        /// <summary>
        /// Fuel freed by shot <paramref name="shotNumber"/> (1-based). 0 for a shot
        /// number outside the schedule - a fourth shot on an emptied canister frees
        /// nothing rather than throwing, because this is driven by client input, which
        /// is never trusted and never fatal.
        /// </summary>
        public static int FuelForShot(int shotNumber)
        {
            if (shotNumber < 1 || shotNumber > Schedule.Count)
            {
                return 0;
            }
            return Schedule[shotNumber - 1];
        }

        /// <summary>
        /// Total fuel granted through shot <paramref name="shotNumber"/> inclusive -
        /// the running total a partially-salvaged canister has paid out. Clamped to
        /// [0, <see cref="TotalFuel"/>].
        /// </summary>
        public static int TotalThrough(int shotNumber)
        {
            int total = 0;
            for (int shot = 1; shot <= shotNumber && shot <= Schedule.Count; shot++)
            {
                total += Schedule[shot - 1];
            }
            return total;
        }
    }

    /// <summary>
    /// The server's ledger of every FUEL CANISTER placed in the world and how far each
    /// has been salvaged. The fuel analogue of <see cref="MetalHarvest"/>: it COUNTS
    /// salvage shots and reports what each one freed, using the recovered
    /// <see cref="FuelCanisterYield"/> schedule.
    ///
    /// WHY NOT THE LODGEABLE-PICKUP CORE. An earlier pass modelled fuel pods as
    /// lodgeable PICKUPS (a 1211 InteractWithObject/PickUp, generalized from the atlas
    /// shard). That is WRONG for fuel: retail fuel is obtained by SALVAGING canisters
    /// with the gauntlet salvage tool - the same tool and flow as metal and wood - not
    /// by an interact/E pickup. So a canister is a SALVAGE TARGET (1099
    /// SalvageAndRepairState with isSalvageable=true, which is the gate
    /// <c>PlayerMultitool.TryDeploySalvager</c> reads via the client's
    /// <c>Salvageable</c> base class) and its shots arrive on 2106 exactly like a
    /// metal node's. The lodgeable-pickup core remains, correctly, for the ATLAS
    /// SHARD, which really is a pickup.
    ///
    /// Pure: no ENet, no Improbable types, no game install.
    ///
    /// NOT THREAD-SAFE, deliberately, like the rest of this assembly: the server is a
    /// single poll loop.
    /// </summary>
    public sealed class FuelCanisterRegistry
    {
        private sealed class Canister
        {
            public int Shots { get; set; }
            public bool Depleted { get; set; }
        }

        private readonly Dictionary<long, Canister> _byEntityId = new Dictionary<long, Canister>();

        /// <summary>
        /// Declares a spawned canister as shootable. Called when the canister's
        /// AddEntityOp goes out and its entity id is known - the same seam as
        /// <see cref="MetalHarvest.Place"/>.
        ///
        /// Idempotent by design: every joining client walks the same spawn plan and
        /// reaches this canister's step, but there is one canister, and a second
        /// player arriving must not refill one someone has already emptied.
        /// </summary>
        /// <returns>True on the first registration of this id; false thereafter.</returns>
        public bool Register(long canisterEntityId)
        {
            if (_byEntityId.ContainsKey(canisterEntityId))
            {
                return false;
            }
            _byEntityId[canisterEntityId] = new Canister();
            return true;
        }

        /// <summary>Whether an entity id is a fuel canister this module is tracking.</summary>
        public bool IsCanister(long canisterEntityId) => _byEntityId.ContainsKey(canisterEntityId);

        /// <summary>Whether a canister has been emptied. False for an intact or unknown id.</summary>
        public bool IsDepleted(long canisterEntityId) =>
            _byEntityId.TryGetValue(canisterEntityId, out Canister? c) && c.Depleted;

        /// <summary>How many shots have landed on a canister. 0 for an untouched or unknown id.</summary>
        public int ShotsOn(long canisterEntityId) =>
            _byEntityId.TryGetValue(canisterEntityId, out Canister? c) ? c.Shots : 0;

        /// <summary>
        /// Fuel a canister has already paid out - the running total, so a log or a
        /// late-join seed can report how far along it is.
        /// </summary>
        public int FuelPaidOut(long canisterEntityId) =>
            FuelCanisterYield.TotalThrough(ShotsOn(canisterEntityId));

        /// <summary>
        /// Records one salvage shot on a canister and reports what it freed.
        ///
        /// A shot on an unknown id or an already-empty canister is
        /// <see cref="FuelHitOutcome.Nothing"/>, not a throw: the beam legitimately
        /// rests on trees, hulls, players and emptied canisters, and this is driven by
        /// client input. EVERY live shot grants (8/8/9); the third also sets
        /// <see cref="FuelHitOutcome.Depleted"/>, exactly once, so the caller sinks the
        /// world entity there and nowhere else.
        /// </summary>
        public FuelHitOutcome Hit(long canisterEntityId)
        {
            if (!_byEntityId.TryGetValue(canisterEntityId, out Canister? c) || c.Depleted)
            {
                return FuelHitOutcome.Nothing;
            }

            c.Shots++;
            int fuel = FuelCanisterYield.FuelForShot(c.Shots);
            bool depleted = c.Shots >= FuelCanisterYield.ShotsToDeplete;
            if (depleted)
            {
                c.Depleted = true;
            }
            return new FuelHitOutcome(fuel, depleted, c.Shots);
        }

        /// <summary>Every placed canister's entity id. For fan-out and logs.</summary>
        public IReadOnlyList<long> EntityIds => _byEntityId.Keys.ToArray();

        /// <summary>How many canisters are placed. For logs and tests.</summary>
        public int Count => _byEntityId.Count;
    }
}
