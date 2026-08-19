using System;
using System.IO;
using WorldsAdriftRebornGameServer.Multiplayer.Ship;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// DOES THE SERVER ACTUALLY REPORT THE OUTCOME IT COMPUTES?
    ///
    /// The pure tests next door prove what each <see cref="ComponentSeedOutcome"/>
    /// MEANS. They cannot prove that <c>ComponentsSerializer</c> ever produces
    /// <see cref="ComponentSeedOutcome.NoSeedForEntity"/>, and that gap is exactly
    /// how the defect this fixes survived: the outcome enum, the batch rule and the
    /// log helpers were all correct and unit-tested, while the one line that
    /// assigns the outcome did not exist, so every branch that ran and declined
    /// reported <see cref="ComponentSeedOutcome.NoClientVtable"/> - "the shipped
    /// client has never heard of this id" - about ids the client had just asked for
    /// by number. Measured live on 2026-08-19: every 1013 / 1120 / 8066 failure on
    /// a built hull or a built deck was reported that way, for eleven days.
    ///
    /// The game-server assembly has no test project of its own (it needs a Windows
    /// game install to compile against), so the wire is asserted the way
    /// <see cref="Ship.ShipContainerWiringTests"/> does it: by reading the
    /// production source off disk. Coarse on purpose. It cannot prove the
    /// diagnosis is right; it proves the diagnosis is CONNECTED, and it goes red
    /// the moment somebody deletes it.
    /// </summary>
    public class ComponentSeedOutcomeWiringTests
    {
        private static string RepoRoot()
        {
            DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string probe = Path.Combine(dir.FullName,
                    "WorldsAdriftRebornGameServer", "Game", "Items", "Config", "itemData.json");
                if (File.Exists(probe)) return dir.FullName;
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException(
                "Could not locate the repo root from " + AppContext.BaseDirectory);
        }

        private static string Source(params string[] parts) =>
            File.ReadAllText(Path.Combine(RepoRoot(), Path.Combine(parts)));

        private static string Serializer() => Source(
            "WorldsAdriftRebornGameServer", "Game", "Components", "ComponentsSerializer.cs");

        private static string SendOpHelper() => Source(
            "WorldsAdriftRebornGameServer", "Networking", "Wrapper", "SendOPHelper.cs");

        private static void Contains(string haystack, string needle, string why)
        {
            Assert.True(haystack.Contains(needle, StringComparison.Ordinal),
                "Expected to find `" + needle + "`. " + why);
        }

        [Fact]
        public void The_serializer_marks_a_branch_that_ran_and_declined()
        {
            // Without this assignment `outcome` keeps its initial value,
            // NoClientVtable, and the server misdiagnoses its own gaps in the one
            // direction that stops anyone investigating.
            Contains(Serializer(),
                "outcome = Multiplayer.ComponentSeedOutcome.NoSeedForEntity",
                "ComponentsSerializer must set NoSeedForEntity when a seed branch matched "
                + "and left obj null. Delete it and every gated branch - 1013, 1120, 8066 "
                + "and the dozens of other ledger-gated ones - starts claiming again that "
                + "the shipped client has no vtable for a component it just requested.");
        }

        [Fact]
        public void The_decline_check_is_not_chained_onto_the_unhandled_else()
        {
            // THE SUBTLE WAY TO GET THIS WRONG, and it compiles and passes a naive
            // grep. Written as `else if (obj == null)` on the end of the big
            // `else if (componentId == ...)` chain, the block is reached ONLY when
            // no branch matched at all - which is the UnhandledId case and the exact
            // case this is not about. It must be a fresh statement after the chain,
            // and it must not steal UnhandledId's answer.
            string serializer = Serializer();

            Contains(serializer,
                "if (obj == null && outcome != Multiplayer.ComponentSeedOutcome.UnhandledId)",
                "The decline check must be a standalone `if` guarded against the "
                + "unhandled case, not another `else if` on the branch chain.");

            Assert.False(
                serializer.Contains("else if (obj == null)", StringComparison.Ordinal),
                "`else if (obj == null)` chained onto the branch list can only fire when NO "
                + "branch matched, so it would relabel every genuinely unhandled id and "
                + "catch none of the declined ones.");
        }

        [Fact]
        public void A_branch_can_decide_absence_for_one_entity_and_it_is_honoured()
        {
            // 1120 ShipPartState is present on a loose part and absent on a built
            // deck, which the component-wide set cannot express. The branch says
            // it; this is the tail that turns saying it into the KnownAbsent
            // contract - no bytes, no [error], never a dropped batch. Without the
            // return, a branch that "decided" would fall straight through into
            // NoSeedForEntity and log as a gap anyway.
            string serializer = Serializer();

            Contains(serializer, "bool decidedAbsentForThisEntity = false;",
                "InitAndSerialize needs the per-entity absence flag.");
            Contains(serializer,
                "if (decidedAbsentForThisEntity)",
                "The flag must be checked, and checked BEFORE the NoSeedForEntity gap check, "
                + "or a decision gets reported as a gap.");
            Contains(serializer,
                "return Multiplayer.ComponentSeedOutcome.KnownAbsent;",
                "A per-entity decision must return KnownAbsent, which is the only outcome "
                + "that never drops an all-or-nothing batch.");

            Assert.True(
                serializer.IndexOf("if (decidedAbsentForThisEntity)", StringComparison.Ordinal)
                    < serializer.IndexOf(
                        "outcome = Multiplayer.ComponentSeedOutcome.NoSeedForEntity", StringComparison.Ordinal),
                "The decision check must come before the gap check, or every deliberate "
                + "per-entity omission is relabelled as a missing seed.");
        }

        [Fact]
        public void The_built_ship_structure_decisions_are_present_for_both_ids()
        {
            // 1120 and 8066 have to go together. Serving one and not the other
            // leaves ShipPartVisualizer disabled anyway (so the served one is
            // read by nobody), and serving BOTH plus 1013 is what arms the
            // client's lift whitelist on a structural deck.
            string serializer = Serializer();

            Contains(serializer,
                "DescribeKnownAbsentForEntity(\n                                entityId, 1120,",
                "The 1120 branch must declare a built hull/deck absent rather than "
                + "silently falling through.");
            Contains(serializer,
                "DescribeKnownAbsentForEntity(\n                                entityId, 8066,",
                "The 8066 branch must make the matching decision. Absent 1120 with a "
                + "served 8066 is a statement nothing reads.");
        }

        [Fact]
        public void Craftable_spawning_is_seeded_for_every_entity_that_asks()
        {
            // THE ONE GENUINELY MISSING SEED of the six. 1013 was served only to
            // entities in the LooseParts ledger, and the built hull and every one
            // of a built ship's decks ask for it too - it was the most frequent
            // component-init failure on the live server. The ternary is the fix:
            // ledger value if we have one, settled "finished spawning" otherwise.
            string serializer = Serializer();

            Contains(serializer,
                ": Multiplayer.Ship.CraftableSpawnPolicy.Done;",
                "1013 must fall back to the settled (false, 0, 0) state for entities "
                + "outside the LooseParts ledger, or every built hull and deck fails to "
                + "initialize it.");

            // The trap next door: `Done` and a spawning=true with zero timers look
            // interchangeable and are not - the latter leaves the crafting SFX
            // looping forever. Pin that the fallback is the finished one.
            Assert.True(CraftableSpawnPolicy.Done.Spawning == false,
                "CraftableSpawnPolicy.Done must mean FINISHED spawning; the 1013 fallback "
                + "relies on it being inert in CraftableSpawningVisualizer.");
        }

        [Fact]
        public void The_error_line_carries_its_own_diagnosis()
        {
            // One line is all a reader gets, and for eleven days that line said
            // "NoClientVtable" and nothing else. The clause that says what to do
            // about it has to be ON that line, not in a doc.
            Contains(SendOpHelper(),
                "Multiplayer.ComponentAbsencePolicy.ExplainOutcome(outcome)",
                "SendOPHelper's `[error] failed to initialize component` line must print "
                + "the explanation next to the outcome name. The name alone was what "
                + "misled every reader of this log.");
        }
    }
}
