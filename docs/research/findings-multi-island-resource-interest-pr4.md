# PR4 — Multi-island resource interest

## Coordinate contract

The client does not publish a globally self-describing player position in 1073.
`positionRelative` is expressed in the frame named by the sparse `relativeTo` field.
When the ground object is an island terrain entity, its world registration key is
resolved through `IslandRegistry.ByWorldEntityKey` and becomes that peer's active
`IslandId`. Later position deltas reuse the remembered frame until another terrain is
reported. This removes the former unconditional Haven-origin addition.

Players aboard ships are a separate, already-authoritative case: the flight session or
hull seed supplies a global base pose, so the relative player offset is added directly
and no island-local conversion occurs. Successful teleport acknowledgements likewise
seed the exact global destination and select the nearest registered island immediately,
covering the packet where the client's new terrain `relativeTo` has not arrived yet.

## Resource ownership and transition

Each resource registration is assigned to the nearest registered island origin. This is
unambiguous for the evidenced world: resource fields are hundreds of metres wide while
Haven and The Trades Challenge are about four kilometres apart. Reconciliation considers
the active island's resources plus already-loaded resources from the previous island.
The latter are retained in the candidate set only so the existing hysteresis policy can
emit their removals; distant, never-visited island resources are never checked out.

`RemoveEntity` is enabled from a protocol capability, not an assumption. The native shim
exposes the connected ENet peer's negotiated channel count. Channel 5 removal is used only
when the peer negotiated at least six channels; older peers remain in retain-visited mode.

## The Trades Challenge population

The production profile is recovered from the preserved Cardinal Guild row and collision
surface, not copied from Haven:

- metal: Aluminium, quality 4;
- deposits: 5 (`ceil(98 LOD0 cells * retail density 0.05)`);
- databanks: 5;
- trees: none;
- no invented fuel or mixed-metal assortment.

Positions are deterministic outputs of `SurfacePlacementGenerator` over the embedded,
TRS-composed 1206286558 LOD0 surface table. Deposits and databanks use upward-facing top
surface samples, spacing and a keep-out around the proven teleport landing. The global
biome entity is registered before either island's deposits. Databank 8073 now points to
the owning island entity instead of always naming Haven.

## Acceptance

Automated coverage proves island classification, old-island removal/new-island addition,
the exact recovered resource profile, surface constraints and production registration.
The remaining manual gate is one real client transition Haven -> Trades Challenge ->
Haven, confirming visible add/remove and harvesting/scanning after re-checkout.
