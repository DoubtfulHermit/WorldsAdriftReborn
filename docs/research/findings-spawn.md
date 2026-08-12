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

## ⭐⭐ THE SURFACE TABLES WERE WRONG — FIXED AND VALIDATED AGAINST THE SESSION LOG (2026-08-08)
**Status: MEASURED. All 255 tables re-extracted. The altitude column is now trustworthy.**

The old table said the highest candidate at 949069116's centre column was **y = −55.8**, while
the player empirically rests at **y = −31.2**, and it had candidates at **y = +262** only ~11 m
away in XZ.

**Root cause — and the earlier diagnosis was only half right.** `sweep_one.py`'s `offs()` summed
**only `m_LocalPosition`**. The missing term turned out to be **scale, not rotation**: every LOD0
grid-cell GameObject carries **`m_LocalScale = (4, 4, 4)`**, one level *above* the MeshFilter, so
every terrain vertex was placed at a quarter of its true offset inside its own 64 m cell.
Audited across **all 255 bundles / 36,091 LOD0 cells**: chain depth is always 4, **rotation is
identity everywhere, without exception**, and the *only* non-identity component anywhere is that
uniform `(4,4,4)`. The extractor now composes full TRS anyway — rotation is implemented,
self-tested and unexercised, which is the honest description of it.

**Why the old `verify_extract.py` check 1 said "0/497 rotated, 0/497 non-unit-scale" and was
still lying:** it only inspected the MeshFilter's own GameObject. The scale is on its parent.
Check 1 now walks the whole chain to the root.

### The validation — it ran, and it passed
`world-data/tools/validate_949069116.py` re-extracts the island **both ways** in one process and
compares against the only empirically known altitude in this research (the session log above,
line 5374, "landed, at rest", y = −31.2):

| centre column | OLD sum-of-localPosition | NEW full TRS |
|---|---|---|
| radius 4 m | −55.07 → **error −23.87 m** | −31.07 → **error +0.13 m** |
| radius 8 m | −54.82 → error −23.62 m | −30.42 → error +0.78 m |
| radius 16 m | +262.26 → error +293.5 m | −27.91 → error +3.29 m |

The **+262 anomaly is fully explained**: it is a real vertex of a cell 5 rows up, dropped into
the centre column by the missing ×4. Two independent cross-checks agree:
- **Grid closure.** Cell pitch is 64.00 m; cell meshes are authored spanning ~17 m. Old walk:
  **45 of 144** 4 m X-slices occupied, in **10 disconnected runs** — the "surface" was islands of
  16 m terrain with 47 m holes between them. New walk: **141/141 slices, one contiguous run.**
  Only a ×4 makes 17 m tile a 64 m grid, and only if scale is applied *before* the cell
  translation — which is exactly `Matrix4x4.TRS` = `T * R * S`.
- **Quaternion convention.** `unity_transform.quat_to_mat3` is checked at import against a
  verbatim transcription of `UnityEngine.Quaternion.operator*(Quaternion, Vector3)` over 200
  random quaternions. Two independent derivations agreeing to 1e-9 rules out the transpose.
  No handedness conversion exists or should: we read Unity's own quaternion and emit Unity-space
  coordinates, so a mirror step would itself be the bug.

**Magnitude of what was wrong:** on Haven the per-vertex |ΔY| between old and new is
**mean 24.84 m, median 24.00 m, p90 45 m, max 51 m** (mean 3D displacement 47.7 m). The "~25 m"
figure was not specific to 949069116; it is the typical error of the old walk everywhere.

**Consequences, now discharged:** `findings-haven.md` has been re-derived from the corrected
table. The one caveat that survives is below.

### ⚠ WHAT IS STILL INFERRED
Ground truth exists for **exactly one island and one column** — 949069116 at (0, ·, 0). The fix
is validated *there* to +0.13 m and validated *structurally* (grid closure) on all 255. What has
**never** been checked in-game is any altitude on any other island, Haven included. The residual
risk is no longer a systematic 25 m; it is now the ordinary risk of picking a vertex rather than
a collider hit, which the 2 m stand-off covers.

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
raw-coordinate window. **Empirical in-game surface altitude for any island but 949069116** — the
extractor bug is fixed and the tables are now geometrically self-consistent, but only that one
column has ever been confirmed by a real session.
