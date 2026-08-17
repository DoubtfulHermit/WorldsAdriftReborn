# Resource visual-variant audit

Status: the three shipped full-deposit shapes are now selected deterministically
per placement. Adjacent resource families were audited but are not conflated with
the deposit contract.

## Full anchored metal deposits

The release client contains three `MetalDepositVisuals` ids in every biome's
`MetalDeposits_Biome01..04` table:

- `metal_deposit_composite_light_01`
- `metal_deposit_composite_light_02`
- `metal_deposit_composite_light_03`

These are not aliases. The LOD0 meshes extracted from `sharedassets0.assets` have
different vertex counts and bounds:

| Variant | Mesh | Vertices | Local Y bounds |
| --- | --- | ---: | ---: |
| 01 | `metal_deposit_1_LOD0` | 3,013 | -0.80 .. 1.01 m |
| 02 | `metal_deposit_2_LOD0` | 2,081 | -0.66 .. 0.82 m |
| 03 | `metal_deposit_03_LOD0` | 2,348 | -1.73 .. 3.38 m |

Variant 03 is therefore the tall roughly 5.1 m formation visible in historical
footage. `MetalDepositState.variantId` chooses shape. Biome chooses the material;
metal type and quality do not choose the shell mesh.

Wareborn formerly seeded variant 01 for every full deposit. It now cycles
01/02/03 by stable island-local placement index. The selected string is carried
by the `MetalNode`, so every peer sees the same collider/geometry and the result
is stable across restart. `WAREBORN_DEPOSIT_VARIANT` remains a global diagnostic
override and the client fallback patch still logs/replaces an invalid id.

## Similar-looking systems that are not safe drop-in variants

### `MetalDepositBoulder`

This is a separate entity prefab. `MetalRockBoulderVisualiser` requires component
12280 `MetalRockBoulderState`; its integer `variant` is forwarded to a
`PropImporter`. It is not a fourth value accepted by the full deposit's 1255
string field and does not provide the crust/core lifecycle. Do not mix it into
the full-deposit rotation until its spawn, yield and depletion contract is
recovered and accepted separately.

### Surface `MetalNugget`

The nugget has one baked visual and no `ComponentMaterialColors`. It renders as
aluminium regardless of the server's metal type. Cycling a nonexistent variant
cannot fix that mismatch; a client asset/material patch or replacement resource
contract would be required.

### Deposit scrap chunks

Retail full deposits held separate lodged `MetalRockScrapState` entities with
scrap-type indices. Wareborn still omits this entire visible yield layer and
grants its invented bulk yield when the core depletes. This is a larger fidelity
gap than shell variety, but it requires separate entity lifecycle, lodging,
pickup and persistence rather than a cosmetic variant switch.

### Trees

The client ships 65 tree prefabs with recovered species and topology, and the
server already has eight fully verified representatives. Haven deliberately
remains birch-only because its original resource table did not survive. Future
B3 population must select only the tree species recorded in
`IslandSurveyCatalog`; it must not globally cycle all woods merely for variety.

### Databanks

`DataBank_001`, `DataBank_002` and `DataBank_003` all ship as client/worker
prefabs, while Wareborn currently uses 001. The authored server-side placement
list and the rule relating those three looks to culture/island did not survive.
Their existence alone is insufficient evidence for random cycling, so the
selection remains blocked pending prefab-contract comparison or authoritative
historical evidence.

### Fuel pods

The current `Egg` visual is an explicitly documented fallback, not a proven fuel
canister variant. It should be replaced only when a shipped prefab with the
required pickup/interaction contract is identified; varying placeholder eggs
would hide rather than solve the gap.
