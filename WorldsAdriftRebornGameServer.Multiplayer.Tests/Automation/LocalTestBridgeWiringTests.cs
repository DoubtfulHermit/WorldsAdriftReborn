using System;
using System.IO;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Automation;

public sealed class LocalTestBridgeWiringTests
{
    private static string RepoRoot()
    {
        DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "WorldsAdriftReborn.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate repo root.");
    }

    private static string Source(params string[] parts) =>
        File.ReadAllText(Path.Combine(RepoRoot(), Path.Combine(parts)));

    [Fact]
    public void Plugin_does_not_create_bridge_without_explicit_opt_in()
    {
        string plugin = Source("WorldsAdriftReborn", "WorldsAdriftReborn.cs");
        int gate = plugin.IndexOf("LocalTestBridge.ShouldStart()", StringComparison.Ordinal);
        int create = plugin.IndexOf("AddComponent<Patching.Automation.LocalTestBridge>()",
            StringComparison.Ordinal);

        Assert.True(gate >= 0 && create > gate,
            "the ordinary client must not create the automation listener");
    }

    [Fact]
    public void Bridge_is_loopback_token_bounded_and_cancels_timed_out_work()
    {
        string bridge = Source("WorldsAdriftReborn", "Patching", "Automation",
            "LocalTestBridge.cs");

        Assert.Contains("WAREBORN_TEST_BRIDGE_TOKEN", bridge, StringComparison.Ordinal);
        Assert.Contains(".wareborn-test-bridge-token", bridge, StringComparison.Ordinal);
        Assert.Contains("File.Delete(path)", bridge, StringComparison.Ordinal);
        Assert.Contains("file.Length <= MaxLineLength", bridge, StringComparison.Ordinal);
        Assert.Contains("IsValidToken", bridge, StringComparison.Ordinal);
        Assert.Contains("new TcpListener(IPAddress.Loopback, port)", bridge,
            StringComparison.Ordinal);
        Assert.Contains("private const int MaxLineLength = 512", bridge,
            StringComparison.Ordinal);
        Assert.Contains("private const int MaxCommandsPerFrame = 8", bridge,
            StringComparison.Ordinal);
        Assert.Contains("FixedTimeEquals", bridge, StringComparison.Ordinal);
        Assert.Contains("ReadBoundedLine(reader, MaxLineLength", bridge,
            StringComparison.Ordinal);
        Assert.DoesNotContain("reader.ReadLine()", bridge, StringComparison.Ordinal);
        Assert.Contains("command.Cancelled = true", bridge, StringComparison.Ordinal);
        Assert.Contains("if (command.Cancelled)", bridge, StringComparison.Ordinal);
    }

    [Fact]
    public void World_and_helm_state_are_read_from_initialized_client_state()
    {
        string bridge = Source("WorldsAdriftReborn", "Patching", "Automation",
            "LocalTestBridge.cs");

        Assert.Contains("bool localPlayer = LocalPlayer.Exists", bridge,
            StringComparison.Ordinal);
        Assert.Contains("SpatialOS.IsConnected", bridge, StringComparison.Ordinal);
        Assert.Contains("pilot.DrivingEntityId", bridge, StringComparison.Ordinal);
        Assert.Contains("ShipControlsBehaviour.Throttle", bridge, StringComparison.Ordinal);
        Assert.Contains("InteractionTargetJson()", bridge, StringComparison.Ordinal);
        Assert.Contains("timedInteractionController", bridge, StringComparison.Ordinal);
        Assert.Contains("renderedHullPose", bridge, StringComparison.Ordinal);
        Assert.Contains("controlAxes", bridge, StringComparison.Ordinal);
        Assert.DoesNotContain("LocalPlayer.Instance != null ? \"world\"", bridge,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Synthetic_overlay_is_inert_until_bridge_enables_it()
    {
        string input = Source("WorldsAdriftReborn", "Patching", "Automation",
            "SyntheticInput.cs");
        string bridge = Source("WorldsAdriftReborn", "Patching", "Automation",
            "LocalTestBridge.cs");

        Assert.Contains("private static bool _enabled", input, StringComparison.Ordinal);
        Assert.Contains("if (!_enabled || !sink.CanReceive(axis)", input,
            StringComparison.Ordinal);
        Assert.Contains("SyntheticInput.Enable()", bridge, StringComparison.Ordinal);
        Assert.Contains("SyntheticInput.Disable()", bridge, StringComparison.Ordinal);
    }

    [Fact]
    public void Commands_cover_menu_interaction_and_ship_axes()
    {
        string bridge = Source("WorldsAdriftReborn", "Patching", "Automation",
            "LocalTestBridge.cs");

        foreach (string command in new[]
                 {
                     "menu.continue", "menu.play", "menu.enter-world",
                     "input.tap", "input.hold", "input.pulse", "axis.set", "axis.pulse",
                     "axis.clear", "input.clear", "interact.list", "interact.use",
                     "interact.release"
                 })
        {
            Assert.Contains(command, bridge, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Unattended_controls_have_bounded_auto_release()
    {
        string bridge = Source("WorldsAdriftReborn", "Patching", "Automation",
            "LocalTestBridge.cs");
        string input = Source("WorldsAdriftReborn", "Patching", "Automation",
            "SyntheticInput.cs");

        Assert.Contains("private const float MaxPulseSeconds = 10f", bridge,
            StringComparison.Ordinal);
        Assert.Contains("seconds >= 0.02f && seconds <= MaxPulseSeconds", bridge,
            StringComparison.Ordinal);
        Assert.Contains("AutoReleaseRealtime", input, StringComparison.Ordinal);
        Assert.Contains("PulseAxis", input, StringComparison.Ordinal);
        Assert.Contains("Axes.Remove(expiredAxes[i])", input, StringComparison.Ordinal);
    }

    [Fact]
    public void Aim_independent_ship_interaction_keeps_native_proximity_and_lifecycle()
    {
        string bridge = Source("WorldsAdriftReborn", "Patching", "Automation",
            "LocalTestBridge.cs");

        Assert.Contains("distance + 0.5f >= visualizer.InteractRange", bridge,
            StringComparison.Ordinal);
        Assert.Contains("visualizer.InteractionEnabled", bridge, StringComparison.Ordinal);
        Assert.Contains("EntityId.IsValidEntityId(candidateEntity)", bridge,
            StringComparison.Ordinal);
        Assert.Contains("FindInteractionCollider(visualizer, playerPosition)", bridge,
            StringComparison.Ordinal);
        Assert.Contains("IsInLayerMask(Layers.Interactables)", bridge,
            StringComparison.Ordinal);
        Assert.Contains("if (collider == null)", bridge, StringComparison.Ordinal);
        Assert.Contains("CheckInteractionMethod.Invoke(observer", bridge,
            StringComparison.Ordinal);
        Assert.Contains("InteractionTotalTimeField.GetValue(timer)", bridge,
            StringComparison.Ordinal);
        Assert.Contains("ReleaseAfter(InputButtons.Interact, releaseAfter)", bridge,
            StringComparison.Ordinal);
        Assert.Contains("interactAgentObserver.ReleaseInteractiveObject()", bridge,
            StringComparison.Ordinal);
        Assert.Contains("kind == \"helm\" || kind == \"sail\"", bridge,
            StringComparison.Ordinal);
        Assert.DoesNotContain("TriggerInteractWithObject", bridge, StringComparison.Ordinal);
    }
}
