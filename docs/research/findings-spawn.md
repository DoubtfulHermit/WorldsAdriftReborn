# FINDINGS — PLAYER SPAWN POSITIONING

## LEAD: the server CAN place a player anywhere today. One field. No client change.
> **190602 `TransformState.localPosition`, as `FixedPointVector3{(long)(x*4096), …}`, with
> `.parent` absent, delivered in the player's FIRST `AddComponentOp` — before the transform
> behaviours enable — and never re-sent.**

**Empirically proven from a real session log**, not static analysis:
```
1947: LOCAL PLAYER JUMPED 109868.6m: (-78980.1,-24400.3,-72373.2) -> (0.0, 0.0, 0.0)
2016: y=  0.0  v=-0.4      3806: y= -5.0  v=-12.9
3299: y= -0.7  v=-4.0      4878: y=-26.9  v=-30.4
5374: y=-31.2  v=  0.0   <- landed, at rest
```
The player is applied to **exactly (0,0,0)** — our seed `{0,100,0}` ÷ 4096 — then free-falls
31 m and **lands on the island and stops**. Gravity ≈ **−17.7 m/s² (≈1.8 g)**.
**Position, gravity and ground collision all already work. What has never worked is the
*value* we send.**

## ⭐ THE 110 KM MYSTERY IS SOLVED — and it was never a bug
`(-78980, -24400, -72373)` is the SDK's **deliberate off-world staging point**, chosen at
random per instantiation:
```csharp
// DefaultInstantiationData.cs:7-9
MinInstantiationPoint = (-9999, -9999, -9999);
MaxInstantiationPoint = (-99999, -99999, -99999);
```
The rig takes this path because `NonPooling` is on it, so `SelectivePooledPrefabFactory`
routes it to `UnityPrefabFactory` → random staging point. Corroborated: a **second** entry in
the same process staged at `(-28133.7, -26458.1, -70850.8)` — different point, same octant,
same range — and also jumped to (0,0,0).

> **Every prior conclusion that "the player's position is never initialised", built on that
> telemetry sample, was measuring the staging point.** `InstantiationData.IsStaged()` is the
> flag the SDK itself uses to exclude such entities from origin calculations.

Related: **`WorldEdgePushback` never runs** (`Ready => WorldBoundsDataVisualizer.CheckedOut`,
and we never send world bounds). **There is no world-edge safety net in either direction.**

## ⭐⭐ THE EXTRACTED SURFACE TABLES ARE WRONG — DO NOT USE THEM FOR ALTITUDE YET
`docs/research/world-data/island-surfaces/949069116.json` says the highest candidate at the
island's centre column is **y = −55.8**. The player empirically rests at **y = −31.2** —
a **24.6 m error** — and the same file has candidates at **y = +262** only ~11 m away in XZ.

**Root cause, in our own extractor:** `world-data/tools/sweep_one.py`'s `offs()` accumulates
**only `m_LocalPosition`** up the transform hierarchy and **ignores `m_LocalRotation` and
`m_LocalScale`**, so any rotated or scaled LOD0 cell lands at the wrong local coordinate.

**Consequences:**
- **The Haven spawn coordinates in `findings-haven.md` are derived from this table and are
  therefore SUSPECT.** They must be re-derived after the fix, or validated empirically.
- Fixing `offs()` to compose full TRS matrices is a **prerequisite** to trusting the table for
  any island.
- For a first move, prefer an **empirically measured** altitude plus 2–5 m clearance.

## THE THREE-BRANCH DECISION — and 1073 no longer vetoes us
`ClientAuthoritativePlayerMovement.SetPlayersInitialPosition:355-374`:
| condition | effect |
|---|---|
| `1073.relativeBias > 0.5` | **returns; server position NEVER applied** |
| `190602.parent.HasValue` | `localPosition` applied raw, **no origin remap** |
| else — **the live one** | `transform.position = localPosition ÷ 4096 − OffsetOrigin` |

Since `ecd3d76` the seed is `relativeTo = InvalidEntityId, relativeBias = 0f`, so branch 1 is
dead and **branch 3 executes**. Confirmed statically and empirically.

A **second, independent writer** of the same field exists — `LocalTransformUpdaterBehaviour.
InitalizeTransform:101-115` — also on the local rig. Both read 190602.localPosition, so the
answer is unambiguous and redundant by design.

## ⚠ DO NOT PERSIST-AND-REPLAY 1073
The client sets `relativeBias = 1f` / `relativeTo = <entity>` whenever it is grounded **on an
entity — and the island IS an entity.** Feeding a stored 1073 back as a seed on the next login
re-arms the exact bug `ecd3d76` fixed. **Seed 1073 from constants, always.**

## THE FLOATING ORIGIN
`OffsetOrigin` starts at (0,0,0) and only moves inside `ChangeActiveIsland`, which needs
`Time.time > nextCheckTime` (default **10 s**), a live LocalPlayer, a checked-out island, and
an **unbounded** `while (!ReadyToSpawn())`. **Until all four hold, the remap is the identity
and the player and island both sit at their raw global coordinates in Unity space.**
At 17 km that is a float ULP of **1.95 mm** — physics is fine, some skinned-mesh/IK jitter.
Nothing else breaks: no threshold is crossed (the origin-move trigger is 50,000).
Both entities convert with the **same** `OffsetOrigin`, so no per-entity remap error is
possible.

**COULD NOT DETERMINE which strategy is live** — scene-serialized, invisible in the decompile.
`ActiveIslandBasedRemapping` has **zero code references anywhere**. The observed session cannot
discriminate because the island sits at global (0,0,0). **One-line mod diagnostic before
moving the island:** log `GetDetermineOriginStrategy().GetType().Name` and `OffsetOrigin`.

## THE SEQUENCE — order is load-bearing
1. **Island first, ack-gated:** AssetLoad → ack → AddEntity → ack → AddComponents with
   1041, 1042, **190602 carrying the island's real position**, 190601.
2. **Only then the player:** AssetLoad → ack → AddEntity.
3. **The player's FIRST AddComponents must already carry the final position** — 190602 with
   `localPosition` = spawn point, `localRotation = Quaternion32(1023)`, `parent = null`; and
   1073 with `relativeBias = 0f`, `relativeTo = InvalidEntityId`.
4. AuthorityChangeOp, gated by `isSendersOwnEntity`.
5. **Never again** — the position is consumed once at `OnEnable`.

**The existing ack-gated SyncStep order already satisfies this, and it is why the observed
session worked. Preserve it. Do not parallelise island and player spawn.**

## ⭐ THE SKY-TELEPORT RULE GETS WORSE AFTER THE ISLAND MOVES
Re-sending AddComponents re-runs `InitAndSerialize`, re-fabricating the **default** 190602 and
handing it to a live entity. The destination was the **world origin** — not the sky. **Once
the island is at its real coordinates, the origin is 15 km away and 500 m below, so an
accidental resend stops being a nuisance and becomes an instant out-of-world drop** with no
`WorldEdgePushback` to catch it. `MirrorSendPolicy.MayResend` (AddEntity only) must stay.

**Also: never send a 190602 ComponentUpdate to a player** — the client is the authoritative
writer; a server-authored update to a client-owned component is a protocol contradiction.

## ⭐ AND A NEW ORDERING HAZARD FOR ISLANDS
`IslandLocalTransformVisualizer.UpdatePosition` does **not** teleport — it starts a
**5-second smoothstep slide**. So a 190602 *update* to an already-placed island **drags the
terrain out from under everyone standing on it over five seconds.**
**Islands must get their final position in their FIRST AddComponents, never as a follow-up.**

## RESPAWN IS COMPLETELY DEAD — five components never seeded
`RespawnVisualizer` requires 1092, **1093** (writer), **190607**, 190602, **1072** (writer),
1077. `ComponentsSerializer` handles 52 ids and **1092, 1093, 190607, 190606 and 1072 are none
of them** — so the visualizer can never enable, though it is on the rig.

The server answers a respawn by writing **`TeleportRequestState` (190607)**; the client copies
it into its own 190602 and acks on 190606. **The Reborn server has never sent 190607.**
**There is no fallback** — omit `localPosition` and the client stays exactly where it is.

**Minimum viable respawn needs no reviver and no shrine entity**: seed 1092 + 190607 + 190606,
grant 1093 + 1072 to the owning client, and on a respawn trigger write `190607.localPosition`
and bump `request`. **Caution: 1072 `CharacterControlsData` means "this is the character you
control" — it must go through the `isSendersOwnEntity` gate and stay OUT of the remote seed.**

## FALL DAMAGE DOESN'T EXIST
Nothing in the Reborn server writes `HealthState`, so **a bad spawn currently produces an
infinite fall rather than a death** — worse than dying. A server-side floor check is worth
having once positions become arbitrary.

## COULD NOT DETERMINE
Which `IDetermineOriginStrategy` is in the scene (one-line diagnostic settles it — **run it
before moving the island**). Whether the client requests 1092/1093/190607 in its interest set
(decides whether respawn needs serializer branches only, or branches **plus** injection).
The shipped `nextCheckTime` (`[SerializeField]`, code default 10 s) — it sets the length of the
raw-coordinate window. **True surface altitude for any island but 949069116**, per the
extractor bug above.
