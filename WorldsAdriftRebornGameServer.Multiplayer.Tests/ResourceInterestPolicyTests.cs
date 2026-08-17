using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

public class ResourceInterestPolicyTests
{
    [Fact]
    public void Client_settle_delay_defaults_and_is_bounded()
    {
        Assert.Equal(TimeSpan.FromSeconds(5), ResourceInterestPolicy.SettleDelayFrom(null));
        Assert.Equal(TimeSpan.FromSeconds(1), ResourceInterestPolicy.SettleDelayFrom("0"));
        Assert.Equal(TimeSpan.FromSeconds(12), ResourceInterestPolicy.SettleDelayFrom("12000"));
        Assert.Equal(TimeSpan.FromSeconds(30), ResourceInterestPolicy.SettleDelayFrom("999999"));
    }

    [Theory]
    [InlineData("tree-7")]
    [InlineData("deposit-iron-2")]
    [InlineData("handshake-deposit-4-2")]
    [InlineData("atlas-shard-deposit-2")]
    [InlineData("fuel-pod-1")]
    [InlineData("databank-0")]
    [InlineData("metal-4")]
    public void Resource_families_are_streamed(string key) => Assert.True(ResourceInterestPolicy.IsStreamedResourceKey(key));

    [Theory]
    [InlineData("global")]
    [InlineData("placed-shipyard:0")]
    [InlineData("loose-part:0:helm")]
    [InlineData("built-ship:0:hull")]
    public void Essential_and_player_made_entities_are_exempt(string key) => Assert.False(ResourceInterestPolicy.IsStreamedResourceKey(key));

    [Fact]
    public void Runtime_resources_stream_only_when_interest_is_enabled_while_global_still_broadcasts()
    {
        Assert.True(ResourceInterestPolicy.StreamsRuntimeRegistration("handshake-deposit-4-3", interestEnabled: true));
        Assert.True(ResourceInterestPolicy.StreamsRuntimeRegistration("atlas-shard-runtime-3", interestEnabled: true));
        Assert.False(ResourceInterestPolicy.StreamsRuntimeRegistration("deposit-runtime-3", interestEnabled: false));
        Assert.False(ResourceInterestPolicy.StreamsRuntimeRegistration("global", interestEnabled: true));
    }

    [Fact]
    public void Component_seeds_require_a_live_checkout_only_for_streamed_resources()
    {
        Assert.False(ResourceInterestPolicy.MayServeComponents(true, true, loadedForPeer: false));
        Assert.True(ResourceInterestPolicy.MayServeComponents(true, true, loadedForPeer: true));
        Assert.True(ResourceInterestPolicy.MayServeComponents(false, true, loadedForPeer: false));
        Assert.True(ResourceInterestPolicy.MayServeComponents(true, false, loadedForPeer: false));
    }

    [Fact]
    public void Spawn_plan_winning_while_dynamic_add_is_queued_suppresses_the_duplicate()
    {
        const long entityId = 82;
        ResourceStreamAction queued = new(ResourceStreamActionKind.Add, entityId);
        HashSet<long> loaded = new();

        // Continuous interest cannot race the connect plan at all.
        Assert.False(ResourceInterestPolicy.ShouldExecute(
            connectPlanComplete: false, queued, loaded));
        Assert.True(ResourceInterestPolicy.ShouldExecute(
            connectPlanComplete: true, queued, loaded));

        // The connect-time spawn plan sends AddEntity while the interest service's
        // asset request is in flight, then NoteLoaded updates the shared peer state.
        loaded.Add(entityId);

        Assert.False(ResourceInterestPolicy.ShouldExecute(
            connectPlanComplete: true, queued, loaded));
    }

    [Fact]
    public void Stale_remove_is_also_suppressed_after_another_path_unloads_the_entity()
    {
        const long entityId = 82;
        ResourceStreamAction queued = new(ResourceStreamActionKind.Remove, entityId);
        HashSet<long> loaded = new() { entityId };

        Assert.True(ResourceInterestPolicy.ShouldExecute(
            connectPlanComplete: true, queued, loaded));
        loaded.Remove(entityId);
        Assert.False(ResourceInterestPolicy.ShouldExecute(
            connectPlanComplete: true, queued, loaded));
    }

    [Fact]
    public void Hysteresis_does_not_churn_between_load_and_unload_radii()
    {
        FixedPointPosition c = FixedPointPosition.FromMetres(0, 0, 0);
        var resources = new[] { (1L, FixedPointPosition.FromMetres(60, 0, 0)) };
        var loaded = new HashSet<long> { 1 };
        Assert.Empty(ResourceInterestPolicy.Reconcile(c, resources, loaded, 50, 80));
    }

    [Fact]
    public void Infinite_unload_radius_keeps_visited_resources_for_legacy_clients()
    {
        FixedPointPosition c = FixedPointPosition.FromMetres(0, 0, 0);
        var resources = new[] { (1L, FixedPointPosition.FromMetres(10000, 0, 0)) };
        var loaded = new HashSet<long> { 1 };

        Assert.Empty(ResourceInterestPolicy.Reconcile(
            c, resources, loaded, 50, double.PositiveInfinity));
    }

    [Fact]
    public void Reconcile_removes_far_loaded_then_adds_nearest_unloaded()
    {
        FixedPointPosition c = FixedPointPosition.FromMetres(0, 0, 0);
        var resources = new[] {
            (1L, FixedPointPosition.FromMetres(100, 0, 0)),
            (2L, FixedPointPosition.FromMetres(20, 0, 0)),
            (3L, FixedPointPosition.FromMetres(10, 0, 0)) };
        var actions = ResourceInterestPolicy.Reconcile(c, resources, new HashSet<long> { 1 }, 30, 70);
        Assert.Equal(new[] {
            new ResourceStreamAction(ResourceStreamActionKind.Remove, 1),
            new ResourceStreamAction(ResourceStreamActionKind.Add, 3),
            new ResourceStreamAction(ResourceStreamActionKind.Add, 2)}, actions);
    }

    [Fact]
    public void Invalid_unload_radius_gets_a_larger_safe_default()
    {
        Assert.Equal(85, ResourceInterestPolicy.UnloadRadiusFrom("junk", 50));
        Assert.Equal(85, ResourceInterestPolicy.UnloadRadiusFrom("40", 50));
        Assert.Equal(120, ResourceInterestPolicy.UnloadRadiusFrom("120", 50));
    }

    [Fact]
    public void A_new_reconcile_replaces_stale_work_from_the_old_position()
    {
        Queue<ResourceStreamAction> pending = new();
        pending.Enqueue(new ResourceStreamAction(ResourceStreamActionKind.Add, 1));
        pending.Enqueue(new ResourceStreamAction(ResourceStreamActionKind.Add, 2));

        ResourceInterestPolicy.ReplacePending(pending, new[]
        {
            new ResourceStreamAction(ResourceStreamActionKind.Remove, 9),
            new ResourceStreamAction(ResourceStreamActionKind.Add, 20),
        }, maximum: 10);

        Assert.Equal(new[]
        {
            new ResourceStreamAction(ResourceStreamActionKind.Remove, 9),
            new ResourceStreamAction(ResourceStreamActionKind.Add, 20),
        }, pending);
    }
}
