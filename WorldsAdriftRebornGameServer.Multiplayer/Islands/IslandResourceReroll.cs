using System;
using System.Collections.Generic;

namespace WorldsAdriftRebornGameServer.Multiplayer.Islands
{
    /// <summary>
    /// WHERE ONE ISLAND'S DEPOSITS STAND AFTER AN UNDERSTORM (S3).
    ///
    /// S1 and S2 RESTORE resources in place. Retail RE-ROLLED them:
    ///
    ///   "All chests will respawn during the understorm, though not always in the
    ///    same place" (WIKI, Chests.wikitext)
    ///   "Each island reset (caused by Understorms), the locations of the fuel
    ///    pods..." (WIKI, Islands.wikitext)
    ///
    /// and roadmap §14.6.3 PROVES the retail mechanism: the server raised
    /// <c>SpawnResources</c> on 1010 and the CLIENT re-sampled its own island mesh
    /// for the new positions. This server cannot use that path today - see the
    /// class remarks below - so it re-rolls the same way it PLACES: deterministically
    /// and offline, by choosing which seats of a pre-generated pool are occupied.
    ///
    /// ⚠ WHY NOT THE 1010/1011 HANDSHAKE, WHICH THIS REPO ALREADY IMPLEMENTS.
    /// It is switched off in production and would be inert if it were not:
    ///   - <c>WAREBORN_METAL_HANDSHAKE=0</c> and <c>WAREBORN_SPAWN_METAL=0</c> (read
    ///     live off the service 2026-08-20), so the client-placement path does not
    ///     run at all; and
    ///   - its coordinate guard is <c>IslandBounds.Haven()</c>, HARDCODED at
    ///     <c>Game/Gathering/IslandResourceService.cs:111</c>, so on the release world
    ///     every reply from a non-Haven island is refused.
    /// The two paths are also mutually exclusive by construction: turning the
    /// handshake on makes <c>WAREBORN_SPAWN_DEPOSIT=1</c> a no-op
    /// (<c>WorldsAdriftRebornGameServer.cs:3312-3314</c>) and the static field
    /// disappears. Flipping those flags is a DEPLOY DECISION for the maintainer, and
    /// the reason they were turned off is unrecorded. So S3 re-rolls the path
    /// production actually runs, and the handshake route stays available unchanged.
    ///
    /// THE MODEL. A deposit keeps its IDENTITY - key, metal type, quality, 1255
    /// variant - and changes only its POSITION, which is what the wiki describes
    /// ("the LOCATIONS of the fuel pods"). Placement itself is not re-computed: the
    /// pool (<see cref="Resources.HavenSurface.DepositPool"/>) was produced by the
    /// one existing placement policy, so every seat is already upward-facing,
    /// in the reachable height band, outside every exclusion, and at least
    /// <see cref="Resources.HavenSurface.DepositMinSpacing"/> from every other seat.
    /// A re-roll therefore cannot place a rock anywhere the boot layout could not
    /// have placed one, and ANY subset of the pool is a valid layout. That is the
    /// deliberate answer to S2's lesson: there is one placement policy, not two that
    /// can disagree.
    ///
    /// Pure: no ENet, no Improbable types, no game install, no RNG, no clock.
    /// </summary>
    public static class IslandResourceReroll
    {
        /// <summary>
        /// How many of the lowest deposit indices keep their seat for ever.
        ///
        /// ONE, and it is deposit-0: the hand-measured "proven" placement 8.9 m from
        /// the player spawn, pinned to iron so the first rock a new player walks up to
        /// is the metal the first recipe wants (see
        /// <c>MetalDeposits.MetalTypeFor</c>). Re-rolling it across the island would
        /// mean a new player's first mining lesson is a search. Retail had no
        /// tutorial rock to protect; we do. <b>WAREBORN TUNING.</b>
        /// </summary>
        public const int PinnedSeats = 1;

        /// <summary>
        /// The seat index each deposit occupies at <paramref name="generation"/>.
        ///
        /// The returned list is indexed by DEPOSIT INDEX (deposit-0 first) and its
        /// values are indices into <see cref="Resources.HavenSurface.DepositPool"/>.
        /// It always has <paramref name="occupiedCount"/> entries, they are always
        /// distinct, and they are always inside <c>[0, poolCount)</c> - so a caller
        /// can index the pool with any of them without a bounds check.
        ///
        /// <b>GENERATION 0 IS THE IDENTITY LAYOUT</b> (<c>seat[i] == i</c>), which is
        /// exactly the layout this server boots with today, because the pool's first
        /// <paramref name="occupiedCount"/> seats ARE
        /// <c>HavenSurface.DepositLocals()</c>. So an unstormed world is byte-identical
        /// to before S3, and the first re-roll is the first storm.
        ///
        /// Deterministic in (<paramref name="island"/>, <paramref name="generation"/>):
        /// no clock and no <c>Random</c>, so the same storm computes the same layout on
        /// a replay, in a test, and on the server. Positions are NOT persisted anywhere
        /// (PROVED: no resource table in <c>SchemaScripts</c>, no resource record in
        /// <c>WorldStateSnapshot</c>) and neither is the generation counter, so a
        /// restart returns the world to generation 0 - the boot layout - at the same
        /// moment it returns every mined node to intact. Layout and harvest state reset
        /// together, which is coherent; nothing can survive a restart half re-rolled.
        /// </summary>
        /// <param name="island">Which island's storm. Part of the seed so two islands
        /// storming on the same generation do not re-roll into the same pattern.</param>
        /// <param name="generation">The storm cycle counter; 0 is the boot layout.</param>
        /// <param name="poolCount">Seats available, i.e. <c>DepositPool().Count</c>.</param>
        /// <param name="occupiedCount">Deposits actually placed, i.e. how many seats
        /// are filled at once.</param>
        /// <param name="pinnedSeats">Leading deposits that never move; see
        /// <see cref="PinnedSeats"/>.</param>
        public static IReadOnlyList<int> SeatsFor(
            IslandId island, uint generation, int poolCount, int occupiedCount, int pinnedSeats)
        {
            if (poolCount < 0) throw new ArgumentOutOfRangeException(nameof(poolCount));
            if (occupiedCount < 0) throw new ArgumentOutOfRangeException(nameof(occupiedCount));
            if (pinnedSeats < 0) throw new ArgumentOutOfRangeException(nameof(pinnedSeats));
            if (occupiedCount > poolCount)
            {
                throw new ArgumentOutOfRangeException(nameof(occupiedCount),
                    "cannot occupy " + occupiedCount + " of " + poolCount + " seat(s)");
            }

            int pinned = pinnedSeats > occupiedCount ? occupiedCount : pinnedSeats;

            int[] seats = new int[occupiedCount];
            for (int i = 0; i < occupiedCount; i++) seats[i] = i;

            // Generation 0 is the boot layout, unconditionally and before any hashing.
            // A world that has never stormed must be indistinguishable from a pre-S3
            // world, so this is the one case that must not depend on the seed at all.
            if (generation == 0 || occupiedCount <= pinned) return seats;

            // The movable seats are [pinned, poolCount); we need (occupiedCount -
            // pinned) distinct ones. Partial Fisher-Yates over the candidate array
            // gives that in one pass, uses each draw exactly once, and cannot repeat.
            int candidateCount = poolCount - pinned;
            int[] candidates = new int[candidateCount];
            for (int i = 0; i < candidateCount; i++) candidates[i] = pinned + i;

            ulong state = Seed(island, generation);
            int wanted = occupiedCount - pinned;
            for (int i = 0; i < wanted; i++)
            {
                int remaining = candidateCount - i;
                int pick = i + (int)(NextRandom(ref state) % (ulong)remaining);

                int chosen = candidates[pick];
                candidates[pick] = candidates[i];
                candidates[i] = chosen;

                seats[pinned + i] = chosen;
            }

            return seats;
        }

        /// <summary>
        /// The PRNG seed for one island's one storm cycle: FNV-1a (64-bit) over the
        /// island id's bytes and the generation. The same integer-only, culture-free
        /// idiom <see cref="Resources.SurfacePlacementGenerator.HashKey"/> already uses
        /// for placement order, for the same reason - it must be identical across
        /// machines and .NET versions, so no string hashing and no floating point.
        /// </summary>
        public static ulong Seed(IslandId island, uint generation)
        {
            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;

            ulong h = offset;
            string id = island.Value ?? string.Empty;
            for (int i = 0; i < id.Length; i++)
            {
                char c = id[i];
                h ^= (ulong)(c & 0xFF);
                h *= prime;
                h ^= (ulong)((c >> 8) & 0xFF);
                h *= prime;
            }
            for (int b = 0; b < 4; b++)
            {
                h ^= (generation >> (b * 8)) & 0xFF;
                h *= prime;
            }
            return h;
        }

        /// <summary>
        /// SplitMix64: a stateless-to-describe, fully deterministic 64-bit step. Chosen
        /// over <c>System.Random</c> deliberately - <c>Random</c>'s sequence is a .NET
        /// implementation detail that has already changed once between runtimes, and a
        /// layout that differs between the test host and the server is a defect nobody
        /// would find until a player reported it.
        /// </summary>
        private static ulong NextRandom(ref ulong state)
        {
            unchecked
            {
                state += 0x9E3779B97F4A7C15UL;
                ulong z = state;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }
    }
}
