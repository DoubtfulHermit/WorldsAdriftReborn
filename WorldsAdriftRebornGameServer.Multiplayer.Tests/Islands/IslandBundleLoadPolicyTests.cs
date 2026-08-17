using System.Collections.Generic;
using WorldsAdriftRebornGameServer.Multiplayer.Islands;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Islands
{
    /// <summary>
    /// The decision half of the asynchronous island bundle load. The Unity half
    /// (LoadFromFileAsync / LoadAssetAsync on a coroutine) cannot be tested here;
    /// what CAN be tested is the part that made the synchronous loader safe by
    /// accident - that one bundle name is loaded once and every request for it
    /// is answered.
    /// </summary>
    public sealed class IslandBundleLoadPolicyTests
    {
        [Theory]
        [InlineData("1044497584@Island_unityclient", true)]
        [InlineData("1044497584@island_unityclient", true)]
        [InlineData("1044497584@ISLAND_unityclient", true)]
        [InlineData("1431299145@Island", true)]
        [InlineData("CoreMain_unityclient", false)]
        [InlineData("Traveller@Default", false)]
        [InlineData("Deck01_unityclient", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void Only_island_names_take_the_bundle_path(string prefabName, bool expected)
        {
            Assert.Equal(expected, IslandBundleLoadPolicy.IsIslandBundle(prefabName));
        }

        [Fact]
        public void Bundle_file_name_is_the_lower_cased_prefab_name()
        {
            // LocalAssetBundleLoader does Path.Combine(dir, prefabName.ToLower()),
            // which is why the on-disk files read "...@island_unityclient".
            Assert.Equal("1044497584@island_unityclient",
                IslandBundleLoadPolicy.BundleFileName("1044497584@Island_unityclient"));
            Assert.Null(IslandBundleLoadPolicy.BundleFileName(null));
        }

        [Fact]
        public void First_request_starts_the_load()
        {
            IslandBundleLoadLedger ledger = new IslandBundleLoadLedger();
            Assert.True(ledger.BeginOrJoin("a@Island", new object(), 0.0));
            Assert.True(ledger.IsInFlight("a@Island"));
            Assert.Equal(1, ledger.InFlightCount);
        }

        [Fact]
        public void Second_request_for_the_same_bundle_joins_instead_of_loading_again()
        {
            IslandBundleLoadLedger ledger = new IslandBundleLoadLedger();
            object first = new object();
            object second = new object();

            Assert.True(ledger.BeginOrJoin("a@Island", first, 0.0));
            Assert.False(ledger.BeginOrJoin("a@Island", second, 1.0));
            Assert.Equal(1, ledger.InFlightCount);

            IList<object> waiters = ledger.TakeWaiters("a@Island");
            Assert.Equal(new object[] { first, second }, waiters);
        }

        [Fact]
        public void Different_bundles_load_independently()
        {
            IslandBundleLoadLedger ledger = new IslandBundleLoadLedger();
            Assert.True(ledger.BeginOrJoin("a@Island", new object(), 0.0));
            Assert.True(ledger.BeginOrJoin("b@Island", new object(), 0.0));
            Assert.Equal(2, ledger.InFlightCount);
        }

        [Fact]
        public void Taking_the_waiters_ends_the_flight()
        {
            IslandBundleLoadLedger ledger = new IslandBundleLoadLedger();
            ledger.BeginOrJoin("a@Island", new object(), 0.0);
            ledger.TakeWaiters("a@Island");

            Assert.False(ledger.IsInFlight("a@Island"));
            Assert.Equal(0, ledger.InFlightCount);
            Assert.True(ledger.BeginOrJoin("a@Island", new object(), 2.0));
        }

        [Fact]
        public void Completing_twice_is_a_no_op_rather_than_a_throw()
        {
            // A double completion would happen inside a coroutine, where the
            // exception is swallowed and the callback is simply lost.
            IslandBundleLoadLedger ledger = new IslandBundleLoadLedger();
            ledger.BeginOrJoin("a@Island", new object(), 0.0);

            Assert.Single(ledger.TakeWaiters("a@Island"));
            Assert.Empty(ledger.TakeWaiters("a@Island"));
            Assert.Empty(ledger.TakeWaiters("never-requested"));
            Assert.Empty(ledger.TakeWaiters(null));
        }

        [Fact]
        public void A_load_that_never_reports_back_is_restarted_after_the_stale_window()
        {
            IslandBundleLoadLedger ledger = new IslandBundleLoadLedger();
            object first = new object();
            object second = new object();

            Assert.True(ledger.BeginOrJoin("a@Island", first, 0.0));
            // Still inside the window: join, do not start a competing load.
            Assert.False(ledger.BeginOrJoin("a@Island", second, 59.9));
            // Past it: the only way to get here is a destroyed coroutine host,
            // and the server's 30 s ack fallback has already fired.
            Assert.True(ledger.BeginOrJoin("a@Island", null, 60.0));

            // The restart owes the earlier requests their callbacks too.
            Assert.Equal(new object[] { first, second }, ledger.TakeWaiters("a@Island"));
        }

        [Fact]
        public void The_stale_window_restarts_from_the_restart_not_from_the_first_request()
        {
            IslandBundleLoadLedger ledger = new IslandBundleLoadLedger();
            ledger.BeginOrJoin("a@Island", new object(), 0.0);
            Assert.True(ledger.BeginOrJoin("a@Island", null, 60.0));
            Assert.False(ledger.BeginOrJoin("a@Island", null, 100.0));
            Assert.True(ledger.BeginOrJoin("a@Island", null, 120.0));
        }

        [Fact]
        public void A_zero_stale_window_disables_the_rescue_entirely()
        {
            IslandBundleLoadLedger ledger = new IslandBundleLoadLedger();
            ledger.BeginOrJoin("a@Island", new object(), 0.0, 0.0);
            Assert.False(ledger.BeginOrJoin("a@Island", null, 1000000.0, 0.0));
        }

        [Fact]
        public void An_unnamed_request_is_untracked_and_always_loads()
        {
            IslandBundleLoadLedger ledger = new IslandBundleLoadLedger();
            Assert.True(ledger.BeginOrJoin(null, new object(), 0.0));
            Assert.True(ledger.BeginOrJoin("", new object(), 0.0));
            Assert.Equal(0, ledger.InFlightCount);
            Assert.False(ledger.IsInFlight(null));
            Assert.False(ledger.IsInFlight(""));
        }

        [Fact]
        public void The_stale_window_outlives_the_servers_asset_ack_fallback()
        {
            // If this ever inverted, a merely-slow load would be restarted while
            // the first LoadFromFileAsync still held the file - which is a Unity
            // error, not a duplicate.
            Assert.True(IslandBundleLoadPolicy.DefaultStaleLoadSeconds * 1000.0
                > IslandTerrainInterestPolicy.DefaultAssetAckTimeoutMs);
        }
    }
}
