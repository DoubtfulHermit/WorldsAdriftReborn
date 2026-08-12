using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// Serialises the test classes that mutate the static <c>DatabankLedger</c>.
    ///
    /// xUnit runs test CLASSES in parallel by default, and a static ledger is one
    /// object shared by all of them - so <c>FidelityCheapWinsTests</c> and
    /// <c>Knowledge.DatabanksTests</c>, which both <c>Clear()</c> it and then
    /// assert on what they registered, could interleave and fail each other. Both
    /// declare this collection, which puts them in one sequential group.
    ///
    /// The race was latent from the moment the second class was written; it only
    /// became visible when an unrelated new test class changed the scheduling.
    /// That is the usual way this kind of bug is found, and the fix belongs here
    /// rather than in the tests that happened to expose it.
    /// </summary>
    [CollectionDefinition(Name)]
    public sealed class DatabankLedgerCollection
    {
        public const string Name = "databank-ledger";
    }
}
