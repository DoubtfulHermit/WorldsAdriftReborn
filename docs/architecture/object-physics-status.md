# Object physics and authority status

Status audited 2026-08-21 against the current single-process server. “Physics” below
means authoritative state, not merely a Unity rigidbody visible on one client.

| Area | Status | What exists now |
|---|---|---|
| Mounted-part membership and pose | **Working** | The server owns `8066`, `1120` and `190602`, persists hull-local offset/rotation, transfers the entity into the whole-ship domain, and wakes `"~"` followers below the client sleep interval. Selected bar parts use a real Unity parent. |
| Mounted-part ownership | **Working, owner-only** | Pickup and placement now re-check the durable hull owner before any carry, detach, transform, persistence or domain mutation. Unknown entities and a part already carried by another player are rejected. Crew/alliance build permission is not implemented. |
| Mounted-part collision | **Client presentation only** | Prefab colliders and Unity hierarchy determine what a local client can stand against or raycast. The server has no contact manifold, overlap test, exclusion-radius test, hinge simulation or part damage. It therefore cannot authoritatively resolve part/part or part/terrain penetration. |
| Loose-part spawn and persistence | **Working for a static pose** | Crafting chooses a deterministic free spawn slot, registers one world entity, persists its absolute Q52.12 pose/packed rotation and reproduces it on restart. Materialisation is a one-shot `1013` transition. |
| Loose/dropped object motion | **Approximate and non-authoritative** | After materialisation the prefab can become a local non-kinematic Unity object, but the server publishes no loose-part motion stream and accepts no loose-part `190602` writer. Different clients may therefore settle a dropped object differently. The server continues to own its last registered pose. |
| Carry/drop lifecycle | **Partial** | `1239 PickedUpEvent` identifies the part and scan coordinate; `DroppedEvent` is an empty struct. The server tracks one carrier, clears it on drop/disconnect, and deterministically detaches a mounted part at its last composed ship pose. It cannot recover the final hand-dropped pose from the retail event. |
| Whole-ship flight | **Approximate, deterministic server authority** | One ship domain owns pilot authority, control state and a rate/force integrator. Root control points and mounted-member wakes are generation/sequence checked and replicated. This is a kinematic reconstruction, not the original Unity physics worker. |
| Ship collisions and terrain | **Missing** | Flight does not query island meshes, terrain envelopes, ships, structures or loose objects. A hull can pass through terrain and other hulls. Clients replay the authoritative path and cannot correct it with local collisions. |
| Altitude/map bounds | **Missing but recoverable in shape** | `1250 WorldBoundsDataState` is not served and no server clamp/pushback runs. The recovered retail onset/hard-ceiling model is documented in `feature-roadmap.md`, but is deliberately not enabled by this audit. |
| Replication/interest | **Working for authored state** | Mounted parts follow their ship domain; detach moves ownership back to the nearest island; connect-time and runtime interest distinguish mounted members from loose world entities. Authority generation prevents stale pilot writes. There is no replication channel for client-simulated loose rigidbody motion or collision contacts. |

## Corrections made by this audit

- A forged player-owned `1239` could previously name any entity, detach another
  character's mounted part, rewrite persistence and transfer domain ownership. Pickup
  now validates known-part identity, exclusive carry and hull ownership before mutation.
- `1070 PlacePart` previously checked that the event belonged to its player but not that
  the target ship belonged to that character. It now mirrors the recovered client ship-
  owner gate; legacy/unowned hulls retain their prior permissive behavior.
- Non-finite or Q52.12-unrepresentable local offsets are rejected before conversion.
- Disconnect now clears the carry ledger, preventing an invisible stale carrier from
  reserving a part for the lifetime of the process.

## Known limits and evidence boundaries

- `DroppedEvent` carries no entity id, pose, velocity or rotation. Accurate authoritative
  hand-drop placement needs an explicit, versioned client/server extension; deriving it
  from time, the player's camera or one client's Unity body would be invented behavior.
- The original per-part mass/power coefficients are absent. `1121 = 50 kg` and current
  engine/sail powers are WAReborn calibration, not recovered Bossa data.
- The server has extracted island surfaces for placement, not a continuous collision
  query suitable for swept hull motion. A nearest sample must not be presented as collision.
- Loose parts remain common world objects when unmounted. The recovered client ownership
  gate applies to ships; no recovered rule proves permanent creator-only ownership of a
  loose object, so this audit does not invent one.

## Roadmap

1. Define a versioned drop-pose command carrying part id, absolute pose, velocity and a
   monotonic carry generation. Validate sender/carrier, finite values, travel envelope and
   interest before persisting and broadcasting it.
2. Add crew/alliance permission as an explicit hull policy once that authority model is
   settled; do not weaken the current durable-owner gate implicitly.
3. Build a conservative swept-hull collision proxy from recovered hull geometry and island
   collision data, then test high-speed tunnelling before enabling terrain response.
4. Serve recovered `1250` bounds and implement the documented quadratic edge/altitude
   response as a separately switchable, soak-tested phase.
5. Only after authoritative collision exists, add loose-body simulation or a dedicated
   physics worker. Client-local rigidbodies must never be promoted to shared truth by relay.
