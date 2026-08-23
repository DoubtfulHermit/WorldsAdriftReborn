# Local client acceptance bridge

The `WorldsAdriftReborn` BepInEx plugin contains an inert-by-default,
loopback-only semantic control bridge for repeatable visual acceptance runs.
It avoids unreliable Wayland/Wine desktop injection and executes commands on
Unity's main thread through the same client code used by the real UI.

The listener exists only when both variables are present before client launch:

```sh
export WAREBORN_TEST_BRIDGE=1
export WAREBORN_TEST_BRIDGE_TOKEN='<per-run random token>'
~/Games/WAReborn-servers/run-client.sh
```

If Wine strips those variables during a reparent/re-exec launch, place the same
random token in `.wareborn-test-bridge-token` beside `UnityClient@Windows.exe`
with mode `0600` before launching. The plugin consumes and deletes this bounded
file before opening the listener, so a later ordinary launch remains inert.

Send one bounded command with:

```sh
WAREBORN_TEST_BRIDGE_TOKEN='<same token>' \
  tools/client-automation/bridge-command.sh state
```

Initial commands:

- `ping`
- `state`
- `menu.continue`
- `menu.play`
- `menu.enter-world`
- `input.tap Interact`
- `input.hold Interact true|false`
- `input.pulse Interact 2.0`
- `axis.set ShipThrottle 1`
- `axis.pulse ShipYaw -1 0.5`
- `axis.clear ShipThrottle`
- `input.clear`

`state` reports the active menu/world phase, SpatialOS connection state,
whether the fully initialized `LocalPlayer` exists, global player position,
the current stable in-range interaction target (including helm/sail kind, verb
and required hold time), timed-interaction activity, helm/control/hull ids,
integrated throttle/vertical/pitch/yaw/roll values, and the rendered hull pose.
A half-created `LocalPlayer.Instance` is never reported as `world`.

Prefer the bounded `input.pulse` and `axis.pulse` forms for unattended runs.
They clear themselves after 0.02-10 seconds even if the caller is interrupted;
`input.clear` remains the explicit emergency release for all synthetic state.

The bridge deliberately has no username/password commands. Authenticate once
through the normal landing screen after each fresh client process; subsequent
Play, character selection, movement, interaction and helm acceptance steps can
then be driven semantically without desktop mouse-coordinate injection.

The bridge binds only `127.0.0.1`, accepts one allocation-bounded line per
connection, caps line length and commands per frame, requires a constant-time token match, and times
out if Unity's main thread does not answer. Timed-out work is cancelled before
execution. The Harmony input overlay is explicitly disabled unless the bridge
successfully starts, and it falls through to the original physical-input path
for every value that has not been synthetically supplied. Ordinary clients do
not create the component or open the port.
