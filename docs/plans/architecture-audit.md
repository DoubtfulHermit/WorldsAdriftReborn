# ARCHITECTURE AUDIT — the server system, not the content

**Written:** 2026-08-20. **Branch:** `docs/architecture-audit`, cut from `main` at `febf8f1`.
**Scope:** the *shape* of the system — ownership, lifetime, boundaries, failure modes,
observability, configuration, growth. Not features. `docs/plans/reality-inventory.md`
already owns the content diff; this document deliberately does not repeat it.

**Nothing here was deployed, and one line of code was changed** (Finding 21). Everything
else is a recommendation.

---

## 0. How to read this

### 0.1 Provenance

The repo's existing labels, unchanged. **PROVED** = read off shipped bytes, this tree's
source, or the live box this session. **RECOVERED** = reconstructed from surviving data or
committed incident records. **INFERRED** = reasoned from PROVED facts; could be wrong.
**WIKI** = community sources, weakest. **WAREBORN TUNING** = ours.

Every non-obvious claim below carries one. Where a subagent asserted something I could
check cheaply, I checked it, and I say where I found them wrong.

### 0.2 What this audit is actually about

This is an unusual machine and the usual intuitions do not transfer. It is worth naming the
shape precisely, because four of the top ten findings are consequences of it rather than
mistakes anyone made:

1. **The client is unmodifiable and authoritative about its own expectations.** We do not
   get to define the protocol; we get to *satisfy* one. When we are wrong, the client's
   normal response is to render something plausible and say nothing.
2. **The server is one thread.** `LocalDomainHost` "schedules nothing"
   (`WorldsAdriftRebornGameServer.Multiplayer/Domains/LocalDomainHost.cs:11-12`); the poll
   loop *is* the simulation. Every cost is a latency cost for everyone.
3. **State lives in three stores with three different durability models** — Postgres, one
   JSON file, and process memory — and no transaction spans them.
4. **The evidence base is a decompile of a dead game**, and it is incomplete in a way that
   returns confident wrong answers rather than errors (Finding 4).

The recurring failure this project already named — *an agent searches for a thing, does not
find it, and designs around its absence* — is not a discipline problem. It is what happens
when **absence is not distinguishable from ignorance anywhere in the toolchain**. That
theme runs through Findings 4, 6, 15 and 21, and it is the single most valuable thing to
fix structurally.

### 0.3 A note on what is good

It would be dishonest to file twenty risks without saying that the guards which *do* exist
are unusually well built, and several are better than what most production systems have:

- `ComponentSeedOutcome` (`.../Multiplayer/ComponentAbsencePolicy.cs:24-62`) separates
  "we decided this entity lacks it" from "nobody has thought about this id" from "the
  client has no vtable for it" from "a branch ran and had nothing". Four states where most
  codebases have a bool. **This is the correct answer to the whole error class above**, and
  it exists in exactly one place. Findings 4, 6 and 15 are all requests to apply it again
  somewhere else.
- `InventoryLoadPolicy`, `ProgressionLoadPolicy` and `InventorySnapshot.Read`'s
  `Width <= 0 -> null` sentinel are correct, tested, and written by someone who understood
  the asymmetry between "no data" and "a wipe".
- `ForgetPeer` (`WorldsAdriftRebornGameServer.cs:77-280`) genuinely clears every per-peer
  store. I went looking for a leak and did not find one.
- `EntityIdAllocator`'s refusal to use id 0, with the reason recorded
  (`Multiplayer/EntityIdAllocator.cs:28-42`), is exactly the standard of write-up this
  project should be judged by.
- `tools/deploy-login.sh`'s patch-notes staleness gate refuses to deploy on a mismatch
  between two *independent* sources. That is a real check, and the model the others should
  copy.

---

## 1. THE TOP RISKS, RANKED

Ranked by (probability it bites) x (what it costs when it does). The first four are the
ones to worry about.

| # | Finding | Costs | Bites |
|---:|---|---|---|
| **1** | Opening the character list can permanently CASCADE-delete a character | character + inventory + knowledge + position + crew + alliance seat | needs one bad row, then fires unattended on every login |
| **2** | A too-new schema disables persistence *silently* and the game plays on | every connected player's whole session | already happened once |
| **3** | The 9th player is refused with no packet, no log, no event | indistinguishable from the outage that cost 90 minutes | the first day 9 people show up |
| **4** | Negative search results carry no information — 8 assemblies absent, 510 bundles unreadable, binaries skipped — and are treated as decisive | wrong architectural decisions, repeatedly; ~80 planning claims rest on one | has produced ≥6 already |
| **5** | `world-state.json`: no fsync, no backup, empty file = silent wipe | every ship and shipyard in the world | one power loss |
| **6** | The post-deploy check watches 1 of 46 error families | cannot detect Finding 2 | every deploy |
| **7** | A DB read blip at login overwrites inventory with the starter kit | one player's everything | one connection blip |
| **8** | Reliable inventory head-of-line blocks unreliable movement | everyone freezes for a retransmit RTT (6.8 s observed) | under loss, now |
| **9** | Emit interval == poll timeout == 50 ms | the known relay two-state defect | 40% of sessions |
| **10** | Production config exists in exactly one place on Earth | near-total silent misconfiguration | box rebuild |
| 11 | `Synchronize` scans the whole world per ship per flight frame | 300k ops/s at 20 hulls | with ships + players |
| 12 | Avatar relay is global and O(n²) in serialisations | 16,000 packets/s at n=20 | above ~10 players |
| 13 | The state-lifetime map is chronological, not principled | ship cargo lost every restart | every restart |
| 14 | No SIGTERM handler; nothing saves on stop | ≤20 s of movement (small — see finding) | every deploy |
| 15 | Read-once / fail-silent client contracts have no register | dead props, ignored updates | continuously |
| 16 | Crews have two writers; the game server caches at boot | crew state stale until restart | any Social Sheet use |
| 17 | The safety gate covers ~5% of services and 0% of ship code | false confidence | every merge |
| 18 | CI runs no tests at all | green badge means "it compiles" | every push |
| 19 | Per-peer cleanup is a manual checklist keyed on a reused pointer | a new player inherits a ghost's state | next new per-peer store |
| 20 | Config polarity traps: three flags whose default is the unsafe side | ships fall out of the sky | if a drop-in is lost |
| 21 | `WAREBORN_LOG_VERBOSE=0` turned the firehose **on** | measured multi-second main-loop stall | **fixed in this branch** |
| 22 | No mod↔server version negotiation; retail's own gate is patched out | stale clients misbehave undiagnosably | as soon as anyone skips a patch |
| 23 | Client patches that compensate for server data | permanent divergence, asynchronous rollout | one open instance |
| 24 | DB credential in the systemd environment | the whole account/character database | known, open; needs a foothold |
| 25 | One unthrottled per-packet log line from unregistered peers | cheapest DoS against the poll loop | trivially reachable |

Findings 22–25 are set out in §4 (the mod/server seam) and §5 (security). The security
posture is otherwise **better than expected**, and §5 says exactly where and why.

---

## 2. THE FOUR TO WORRY ABOUT

### Finding 1 — Reading the character list is a destructive write. PROVED.

**What.** `AccountRosters.Load` (`WorldsAdriftServer/Persistence/AccountRosters.cs:47-68`)
unconditionally calls `Write` at `:65`. `Write` (`:112-116`) calls
`CharacterRepository.ReplaceRoster`, which computes `keepUids` from the roster it was
handed (`WorldsAdriftReborn.Storage/Repositories/CharacterRepository.cs:156`) and then:

```sql
DELETE FROM characters WHERE account_id = @account_id AND character_uid <> ALL(@keep);
```
(`CharacterRepository.cs:161-163`)

Six tables `ON DELETE CASCADE` off that row: `character_inventories`
(`SchemaScripts.cs:237`), `character_progression` (`:316`), `character_positions` (`:357`),
`crew_members` (`:414`), `alliance_members` (`:642`), `social_invites` (`:482,490`).

The roster handed to `Write` is not the roster in the database. It is
`RosterPolicy.Normalize(Read(...))`, and **both stages drop rows**:

- `Read` (`AccountRosters.cs:101-109`) does `.Select(CharacterAdapter.ToGameData).Where(c => c != null)`.
  `ToGameData` returns `null` on `JsonException` (`CharacterAdapter.cs:101-106`).
- `RosterPolicy.IsEmptySlot` is `c.Cosmetics == null` (`RosterPolicy.cs:29-32`). `Normalize`
  keeps **only the first** such entry and `continue`s past the rest (`:71-77`). A real
  character whose payload deserialises with a null `Cosmetics` — a `{}` blob, schema drift,
  a partial save — is reclassified as an empty slot and, if any empty slot precedes it,
  dropped.
- Over the cap: `if (real.Count < MaxCharacters) real.Add(c);` (`:79-82`). A 6th character
  is dropped and therefore deleted.

The `data_json` CHECK does not help: `characters` uses `length(data_json) > 0`
(`SchemaScripts.cs:187-188`) while its own siblings at `:254` and `:328` use
`length(btrim(...)) > 0`. So `' '` is a legal payload, deserialises to null, and enters the
drop path.

**Why it matters.** The comment at `CharacterAdapter.cs:80-82` — *"one corrupt character
costs that character rather than the player's whole roster"* — is materially wrong. It
costs that character **and everything they have ever owned**, permanently, with no
tombstone and no audit row, on an action every player performs at every login. There is no
soft delete anywhere in the schema.

`MaxCharacters` is also stated three times that must agree and none of which reference each
other: `RosterPolicy.cs:20`, `SchemaScripts.cs:24`, and the column CHECK `slot_index <= 5`
at `SchemaScripts.cs:186`.

**Fix.** Separate "which characters exist" (a database fact) from "which characters we can
render" (an adapter opinion). `keepUids` should be every uid `ListForAccount` returned, not
every uid that survived adaptation. **A five-line interim hardening that is worth doing
today:** in `Load`, refuse to `Write` at all when `Read` returned fewer rows than
`Accounts.Characters.ListForAccount` produced, and log loudly. That converts a silent
permanent wipe into a loud no-op.

**Bites:** high. One malformed row, ever, and then unattended.

> One bounded amplifier, for completeness: if *every* character fails to adapt,
> `stored.Count == 0` sends `Load` into `InheritLegacyRoster` (`:57`), which would rewrite
> the account from the pre-Postgres `roster.json`. That path is gated on an env var
> matching one specific account (`:126-136`), so it is not general — but for that one
> account it is a full rollback to a pre-migration state. PROVED.

---

### Finding 2 — The game server cannot tell "Postgres is down" from "this binary is older than the schema", and the second one eats a session. PROVED.

**What.** `SchemaMigrator.ScriptsToApply` has a correct, deliberate downgrade guard
(`WorldsAdriftReborn.Storage/Schema/SchemaMigrator.cs:52-58`):

> *"The database is at schema version N but this build only knows up to M. Refusing to run:
> a newer build wrote this file."*

That throw then meets two callers that do **opposite** things.

**Login server — refuses, correctly.** `WorldsAdriftServer/WorldsAdriftServer.cs:22-33`
catches, prints `[fatal] could not open the account database`, and `return`s. The process
exits, `Restart=always` restarts it, it crash-loops, and nobody can log in. Loud, immediate,
unmissable.

**Game server — swallows it, and keeps serving players.** `EnsureSchema()` is called from
four independent lazy constructors, never at boot, each wrapped identically:

| file:line | |
|---|---|
| `Game/Inventory/InventoryPersistence.cs:57` | `catch (Exception e) { DisabledReason = e.Message; repository = null; }` |
| `Game/Knowledge/ProgressionPersistence.cs:42` | same |
| `Game/Persistence/PlayerPositionPersistence.cs:37` | same |
| `Game/Crew/CrewPersistence.cs:35` | same |

`repository == null` makes every `Load` return null and every `Save` a silent no-op. There
is **no retry** — the field is null for the process lifetime.

The design intent is stated at `InventoryPersistence.cs:14-19`: a database being down must
never refuse players entry. That is *right* for a contributor's laptop and catastrophically
wrong for "this binary is older than the schema", and **the two are indistinguishable at
this catch site**. The guard's entire purpose — to refuse to run — is defeated two frames up.

**This is the recorded incident's mechanism.** `tools/deploy-login.sh:21-24` (RECOVERED):
*"A SCHEMA MIGRATION MEANS BOTH BINARIES SHIP TOGETHER. A split deploy once left the game
server refusing persistence and destroyed a character's progression."*

The two deploy orders are asymmetric in the worst direction (INFERRED, mechanically):

| order | outcome |
|---|---|
| **login first** (DB → N+1, game server still at N) | all four stores disable; **players play a whole session into RAM and lose it**. Four `[warning]` lines at boot are the only tell. |
| **game first** | login `[fatal]`s and crash-loops. Total login outage — but loud. |

The dangerous order is the one the repo makes easy: `tools/deploy-login.sh` is the **only**
automated deploy script, and it deploys only the login server. Worse, the unit declares
`After=wareborn-login.service` (`deploy/wareborn-game-native.service:4`), so **a reboot is
hardcoded to the dangerous order.**

A second, independent aggravator: because `EnsureSchema` is called four times rather than
once, a transient failure between two constructor calls yields **partial** persistence —
knowledge saves, inventory does not.

**Fix, and this is the highest-value ~20 lines in the document.** Give `SchemaMigrator` a
typed `SchemaTooNewException` and let the four constructors rethrow *that one* while still
swallowing `NpgsqlException`. A game server that cannot persist because the schema moved
under it must not accept a single connection. Then: call `EnsureSchema` once per process,
not once per store.

Cheapest mechanical enforcement of the co-deploy rule, if you want belt and braces: expose
`Db.SchemaVersion()` (`Db.cs:128`, currently zero production callers) as a
`--schema-version` flag and have both deploy scripts refuse when the artefact and the
database disagree.

**Bites:** it already has, once, expensively.

---

### Finding 3 — The 9th player gets no packet, no event and no log line. PROVED to the ENet source.

**What.** `private const int MaxPlayers = 8` (`WorldsAdriftRebornGameServer.cs:2183`) is
passed as `peerCount` to `enet_host_create` (`:4172` →
`WorldsAdriftRebornCoreSdk/enetLayer.cpp:84`). There is no environment override, and the
startup banner prints the port (`:4170`) but **not the capacity**.

What happens to the 9th connect is not a guess. `enet_protocol_handle_connect` scans
`host->peers[0..peerCount]` for a slot in `ENET_PEER_STATE_DISCONNECTED`; if there is none,
`peer` stays NULL and `protocol.c:322-323` returns NULL, which the caller turns into
`goto commandError` (`protocol.c:1121-1122`). The connect packet is discarded. **ENet
raises no event for a refused connection, so no log line is possible without patching the
shim.** The client waits forever.

That symptom — PLAY hangs, no packet reaches the server, nothing in the log — is
byte-for-byte the 2026-08-19 outage (`HANDOVER.md:530-556`) that took ninety minutes to
diagnose, and it would be diagnosed the same wrong way the second time.

Worse, effective capacity is below 8: a slot is freed only when its peer reaches
DISCONNECTED, and `ENET_PEER_TIMEOUT_MAXIMUM` is 30,000 ms (`enet/include/enet/enet.h:231`).
A player whose client crashes holds a slot for up to half a minute.

**Fix, in this order.** (a) Log the capacity in the boot banner and log `players now: N/8`
on every connect — one line each, and it makes the ceiling visible before it is hit. (b)
Make it `WAREBORN_MAX_PLAYERS`. (c) **Do not raise it yet.** See Findings 11 and 12: at
present the constant is the only thing standing between this server and defects nobody has
measured. Raising it is the most dangerous one-line change available in this repository.

**Bites:** the first day nine people want to play — i.e. the day the project succeeds.

---

### Finding 4 — A negative result from our evidence base carries almost no information, and is being treated as decisive. PROVED.

**What.** `/home/ttanurhan/Games/WAReborn-decompiled/` contains four trees: `acs`
(Assembly-CSharp, 2,158 files), `ecs` (BossaECS), `gencode` (Generated.Code), `sdk-decomp`
(the two Improbable SDK assemblies). The shipped client's `Managed/` directory contains
**eight further first-party assemblies that are in no tree**:

| assembly | bytes | holds, among others |
|---|---:|---|
| `Assembly-CSharp-firstpass.dll` | 4,052,480 | unknown — nobody has looked |
| `SpatialTranslator.dll` | 313,856 | `RadialStormStateC`, `ApplyBlueprintLocalComponentsS` |
| `WASystems.dll` | 67,072 | `BlightLocalComponent`, `WeatherCellGenesisS`, `CantorPairUtils` |
| `GameDBLibrary.dll` | 54,272 | |
| `Improbable.UnityDll.dll` | 16,896 | |
| `ConfigurationManagement.dll` | 14,336 | |
| `WAUtilities.dll` | 13,824 | |
| `GameDBLibraryUnity.dll` | 8,704 | |

That is **4.54 MB of first-party managed code absent, against 14.8 MB present — roughly
23%** of the evidence base by size, and `Assembly-CSharp-firstpass.dll` alone is 79% the
size of the `acs` tree everyone treats as *the* decompile.

**The failure mode is worse than a zero result, and this is the part that matters.** Those
symbols are *referenced* by `acs` but *defined* in the missing assemblies:

| symbol | files in `acs` naming it | defined in |
|---|---:|---|
| `BlightLocalComponent` | 21 | `WASystems.dll` |
| `RadialStormStateC` | 9 | `SpatialTranslator.dll` |
| `WeatherCellGenesisS` | 2 | `WASystems.dll` |
| `CantorPairUtils` | 2 | `WASystems.dll` |
| `ApplyBlueprintLocalComponentsS` | 0 | `SpatialTranslator.dll` |

So a searcher gets **hits**, opens them, finds only call sites and no class body, and
concludes *"it is referenced but stubbed"* or *"the implementation lived server-side and is
lost"*. That is a confident, evidence-shaped wrong answer, and it is exactly the shape of
two corrections already in the handover: *"Retail's flight model is **NOT** lost"*
(`HANDOVER.md:328-329`) and *"the formula lived in the lost Scala worker"*
(`HANDOVER.md:1447-1451`). The roadmap's Phase 7 "may force a client mod" blocker rested on
one of these negatives and has since been corrected.

**Nothing records the gap where anyone would look.** `HANDOVER.md` §2 names *"the shipped
retail decompile and asset census"* as source-of-truth #3 and never says what it contains
or that it is partial. The caveat exists — exactly once, at
`docs/research/diag/findings-weather-storm.md:51-54`, along with the `ilspycmd` invocation
that recovers the missing bodies — where only somebody already researching weather will
ever find it.

**And one committed research document's entire evidence base has evaporated.**
`docs/research/findings-weather.md:5` anchors every citation to
`S = /tmp/claude-1000/-home-ttanurhan-Documents-Claude-Projects/ff15d21e-.../scratchpad`.
That directory no longer exists (verified). Its ~40 citations are currently unverifiable
and unre-derivable without re-running a decompiler. The blast radius is bounded — two files
reference a `/tmp` scratchpad, one substantively — but the practice is the finding.

**Three compounding shapes, all confirmed, all silent:**

1. **`grep` here skips binary files** unless given `-a` (`grep -c basher_body resources.assets` → 0, exit 1; `grep -ac` → 5). Already in the project's own error list as instance #4.
2. **`grep` here also skips gitignored files, and exits 0 while doing it.** I tested this directly: a tree with `obj/` ignored and the needle in `obj/hidden.txt` and `visible.txt` returns only `visible.txt`, **exit status 0**. The `-a` case at least returns exit 1; this one gives no signal whatsoever. The repo's `.gitignore` is the stock VisualStudio one, so `[Bb]in/`, `[Oo]bj/`, `[Rr]elease/`, `x64/`, `x86/` are invisible. No *tracked* file currently matches (verified: 0), so today this is latent — but any extracted-asset or build-output directory would fall into it.
3. **Content inside container files is invisible to `find`.** Blueprints are TextAssets inside `resources.assets`; a `find` for `*blueprint*` returns nothing and *means* nothing. Likewise `client-entity-prefabs.txt` (349 names) has now held the overlooked answer three separate times — fuel tank = power generator (line 219), bar pipes, `blight` (line 17).
4. **The 510 asset bundles are compressed, so `grep` cannot see inside them at all — and unlike the `-a` case there is no flag that fixes it.** `/home/ttanurhan/Games/WorldsAdrift/Assets/unity/` holds 510 bundles, 255 of them `*@island_unityclient`. They are LZ4/LZMA, and they ship **type trees**, so their contents and serialized field values read straight out with `UnityPy` — which **is not installed anywhere on this machine** (verified), and for which **no tool is checked into `tools/`** (verified). Demonstrated cost, this session: roadmap §14.11.1 recorded "is `IslandLightningTimerVisualizer` baked onto our island prefabs?" as an open question *a live client would have to settle*, explicitly noting that enumerating bundle contents "wants UnityPy, not grep". In a throwaway venv it took about two minutes: **present in 255 of 255 island bundles, zero lack it.** §14.11.2 recorded a serialized field as unreadable headless; it reads out as `_minTimeBetweenLightningSeconds = 0.0`, `_maxTimeBetweenLightningSeconds = 1.0`. The capability has in fact been built at least twice before — `HANDOVER.md:322` cites a "UnityPy-confirmed" enumeration of ship roots, and two research documents mention it — **and thrown away each time**, in exactly the same way as the vanished `/tmp` scratchpad above. The project keeps rebuilding and discarding its own best instrument.
5. **`git worktree add` does not populate submodules.** `WorldsAdriftRebornCoreSdk/enet` is a submodule; in a fresh worktree it is an empty directory, so a grep over the transport layer silently returns nothing. (This is why one subagent could only mark Finding 8 INFERRED; I resolved it from the main worktree.)

**The risk is not any individual wrong fact. It is that a negative result from this evidence
base carries almost no information, and is nonetheless being treated as decisive.** All
three of the substantive mechanisms — binary files skipped, assemblies absent, bundles
unreadable — fail in the **same direction**: they under-report, so the project
systematically concludes *"the client does not have this"* and then builds around the
absence. That is precisely the error class the handover names as its most expensive, and
these are its mechanical causes rather than four unlucky lapses of attention.

**Scale of the exposure.** `docs/plans/feature-roadmap.md` and
`docs/plans/reality-inventory.md` between them carry roughly **80 negative-shaped claims** —
20 `MISSING` status rows, ~32 "unreferenced / never referenced / unused", ~22
"absent / lost / missing from", ~9 "does not exist / nothing in the client" (counted, so
treat as an order of magnitude rather than an audit of each). `reality-inventory` §7's
headline number — 308 of 443 component ids unimplemented — is itself partly a negative
classification. **Every one of those is worth exactly as much as the search that produced
it**, and until item 3 of the fix below exists, none of them states which sources it
actually reached.

**Fix — this is cheap and it retires a class rather than an instance.**

1. **Decompile the eight missing assemblies into the same tree**, starting with
   `Assembly-CSharp-firstpass.dll`. The command is already recorded at
   `findings-weather-storm.md:52-54`.
2. **Check in a bundle-inspection tool** — `tools/evidence/bundle-query.py` plus a pinned
   `requirements.txt` for UnityPy. It has been written twice and lost twice; the third time
   should be the last. Working scripts exist right now in a session scratchpad
   (`scan_island.py`, `read_fields.py`) and should be adopted before that directory goes
   the way of the last one.
3. **Commit a `docs/research/EVIDENCE.md`** that states, for each source, *what it can and
   cannot see*: which assemblies are in the decompile and which are not; that `grep` cannot
   read bundles or `resources.assets` without `-a`; that `find` cannot see inside
   containers. Then a negative result can be *qualified* — "absent from a tree that covers
   100% of first-party assemblies and from all 510 bundles" is a fact; "absent from `acs`"
   is not.
4. **Write one search script** (`tools/evidence/search.sh`) that sweeps the decompile, the
   asset census, `resources.assets` with `-a`, the 510 bundles, the wiki archive and the
   prefab list in one pass, and — critically — **prints which sources it searched and which
   it could not reach**. A search that cannot state its own coverage cannot support a
   negative. This is the single highest-leverage tool this project could build.
5. **Ban `/tmp` paths in committed documents.** Anything cited must live under the repo or
   a stable path under `~/Games/`.
6. Re-run, with `-a` and with item 4, the negative claims that are load-bearing — the 20
   `MISSING` rows first, since those are the ones the roadmap schedules work against.

**Bites:** continuously. This is the only finding in the document that has already produced
five wrong decisions, and the only one whose fix makes the other nineteen easier to find.

---

## 3. THE REST, IN RANK ORDER

### Finding 5 — `world-state.json`: no fsync, no backup, and an empty file is a silent wipe. PROVED.

`AtomicJsonFile.Write` (`.../Multiplayer/Persistence/AtomicJsonFile.cs:72-113`) does
temp-then-rename — correct against a *process* crash. Three gaps:

**(a) No fsync, anywhere.** `File.WriteAllText(tmp, json)` at `:86`, `File.Move(tmp, path, true)`
at `:90`, and no `Flush(true)` on the temp file nor an fsync of the directory after the
rename. On ext4 the `auto_da_alloc` heuristic usually forces allocation on rename-over
(WIKI), but it is a heuristic and does not cover the directory entry. The classic outcome
of rename-without-fsync under power loss is a **zero-length file**.

**(b) A zero-length file is the one input that skips quarantine.** `AtomicJsonFile.cs:49-52`:

```csharp
if (string.IsNullOrWhiteSpace(json))
{
    return null;          // early return: NOT quarantined, NOT logged
}
```

A *malformed* file is moved aside to `.broken` with an `[info]` line (`:59`, `:115-133`). An
*empty* one returns null silently. `WorldStatePersistence.Snapshot()` (`:70-78`) turns that
null into `new WorldStateSnapshot()` — an empty world — and caches it in a static for the
process lifetime. The first subsequent `Save()` (`:544-547`) writes
`{"PlacedDeployables":[],"BuiltShips":[],"MountedParts":[],"LooseParts":[]}` over the file.
**Every ship, shipyard, mounted part and loose part is now permanently gone, with no
`.broken` to recover from.** The existing test asserts exactly this behaviour and checks
only the null (`Tests/Persistence/AtomicJsonFileTests.cs:97-104`).

**(c) The window is wide, because a flying ship rewrites the whole document every 2 s.**
`PoseSaveInterval = TimeSpan.FromSeconds(2)` (`Game/ShipFlightService.cs:127`), and each
pose write goes through `Snapshot()` → full serialise of every ship's base64 hull blob →
`AtomicJsonFile.Write` (`WorldStatePersistence.cs:194-204`). With any ship in the air the
server is inside a whole-file rewrite a meaningful fraction of the time. That is also a
scale problem in its own right: at 20 hulls it is 10 full-document writes/second,
synchronously, on the poll loop.

**(d) No backup or rotation.** The only sidecars are `.tmp` and `.broken`, and
`TryQuarantine` (`:121-124`) deletes the previous `.broken` first — one recovery slot,
overwritten by the next incident.

**(e) The Wine fallback is a delete-then-move on the live world file.** `:92-104`: if the
atomic move throws, `File.Delete(path)` then `File.Move(tmp, path)`. Between those two calls
the world does not exist. The reasoning ("only runs when the atomic one already failed") is
sound for an occasional failure and wrong for a prefix where `MoveFileEx` *consistently*
refuses the flag. Production is native Linux and Wine is rollback-only; this branch should
be deleted rather than kept warm.

**Fix.** Four small changes: write via `FileStream` + `Flush(true)` then fsync the
directory; treat empty exactly like corrupt (delete the early return, fall through to
quarantine); keep one previous generation as `world-state.json.1`; and refuse to `Save()` an
empty world when the file existed on disk — the same "when in doubt, do not wipe" rule
`InventoryLoadPolicy` already encodes, applied to the world.

Add a `Version` field to the snapshot whose *absence* means refuse, not empty. Every guard
in this codebase that works is a structural sentinel in the deserialiser, not a database
CHECK (see Finding 7). `world-state.json` has no sentinel at all.

**Bites:** needs one hard power loss or OOM-kill. On a hosted VPS that is a matter of when,
and the blast radius is the entire built world.

---

### Finding 6 — The one automated post-deploy check watches 1 of 46 error families, and structurally cannot see Finding 2. PROVED.

`tools/check-game-server.sh` is a real improvement and I want to be precise about what it
fixed before saying what it misses. It has a denominator (`:71-74`); a window with no
component-interest batches exits **2 INCONCLUSIVE**, never 0 (`:85-91`); an unknown
component id fails at **count 1** (`:107-108`); and the baseline is a committed ledger whose
stated rule is that a fixed id is *deleted*, not commented out
(`tools/game-server-error-baseline.txt:9-17`). The INCONCLUSIVE path genuinely triggers: the
denominator line is an unconditional `Console.WriteLine` at the top of `SendAddComponentOp`
(`Networking/Wrapper/SendOPHelper.cs:154`), not routed through `ServerLog.Trace`, so verbose
mode cannot suppress it. And if the `sed` at `:79` ever stops matching, the unparsed line
survives into `known_ids` lookup, misses, and **fails**. Format drift fails closed. That was
not an accident.

Now the gaps.

**(a) It greps one string.** `\[error\] failed to initialize component `. The game server
has **46 distinct `[error]` prefixes and 99 `[warning]` prefixes** (counted). Invisible to
the check, among others: `[error] could not write inventories to the database`,
`[error] could not write progression to the database`, `[error] failed to write <path>`
(that is `AtomicJsonFile` — Finding 5's disk-full case), `[error] packet processing threw`,
`[error] stored inventory ... is unreadable`.

**(b) It cannot see persistence being off**, which is Finding 2's only symptom.
`InventoryService.ReportPersistenceState()` (`Game/Inventory/InventoryService.cs:38-43`)
prints `[warning] inventory persistence is OFF (...)` — a `[warning]`, at boot, not the
counted string, with three identical siblings. **So the split-deploy failure that already
destroyed a character's progression produces four warning lines and then a green check.**
The post-deploy verification cannot detect the incident the deploy rule exists to prevent.
Three lines fix this: grep `--since-boot` for `persistence is OFF` and fail.

**(c) The baseline claims coverage it does not have.** `game-server-error-baseline.txt:21-25`
asserts that if a `[error] DROPPING the whole AddComponent batch` line ever appears *"the
rate check will show it"*. It will not: a dropped batch emits exactly **one**
`failed to initialize component` line and then `return false` (`SendOPHelper.cs:186,199`),
so the remaining ids are never attempted and produce no further errors. Five dropped batches
in five hundred is 1 per 100 against a ceiling of 25 — **exit 0, "OK"** — while five
entities "render and do nothing". Early-dropping batches *lower* the measured error rate.
One more awk counter and a hard gate at ≥1 closes it.

**(d) journald rate-limiting is at defaults and drops are never checked.** The live
`journald.conf` has every rate-limit line commented (`RateLimitIntervalSec=30s`,
`RateLimitBurst=10000`) and the unit sets no `LogRateLimitBurst`. Meanwhile
`SendOPHelper.cs:204` prints `[success] initialized and serialized componentId N`
**unconditionally, once per component per batch**, and `ServerLog.cs:13-18` records 1,207
lines in a single second with two players. A few simultaneous logins can plausibly cross the
limit; journald then discards silently and both numerator and denominator are truncated.
Fix: `LogRateLimitBurst=0` on the unit, and move that `[success]` line behind
`ServerLog.Trace` where its per-packet-class volume belongs.

**Everything else that reports on this system, assessed the same way:**

| check | verdict |
|---|---|
| `systemctl is-active` (`HANDOVER.md:46`) | **can print green while broken.** A game server with all four stores disabled is `active`. |
| `deploy-login.sh` HTTP probes (`:102-110`) | **status-code only.** 200 means a page was served, not the right one. Add one `grep -q` body assertion each. |
| `deploy-login.sh` patch-notes count (`:112-121`) | **honest** — two genuinely independent sources, a curl failure yields a mismatch. The model to copy. |
| `deploy-login.sh` staleness gate (`:41-55`) | **excellent.** Refuses to deploy on a mismatch. |
| `/admin` stats freshness (`Admin/GameStats.cs:29-46`) | **well built.** 12 s staleness, missing/unreadable/ok distinguished, fail-safe on a missing timestamp. |
| `/admin` stats **schema** version | **can print green while broken.** `StatsSnapshot.cs:424` writes `SchemaVersion = 13`; `GameStats.cs:229` reads `(int?)o["schemaVersion"] ?? 0` and every unknown field becomes `?? 0` / `?? false`. This is a **second cross-binary version coupling on the same pair of binaries**, and unlike the database one it is never enforced. A newer game server read by an older login server gives a dashboard full of confident zeros. |
| `WAREBORN_BUILD` | **already lied once** (`HANDOVER.md:452-454`). A hand-maintained build stamp with no relationship to the artefact. Stamp it at publish time from `git rev-parse`. |
| soak `FLAT` verdict | **was** green-while-broken; the `REGRESSED` gate fixed it. Note `SOAK_MISSED_TICK_CEILING_PCT=100` is one env var away from being a mute button. |
| CI (`.github/workflows/msbuild.yml`) | **runs no tests at all.** A green badge means "it compiles". Every gate in this project is a human typing `dotnet test`. |

**And the eleven-day blindness: the instance is fixed, the class is not.** The bug
(RECOVERED from commit `9fd93e5`) was not log suppression — it was a *default-initialised
diagnostic* that only some paths overwrote, so a gated branch's implicit `else` returned
`NoClientVtable`, whose own message tells the reader the gap is unfixable. Three
observations:

1. **The initialiser is still there.** `ComponentsSerializer.cs:141` still starts `outcome`
   at `NoClientVtable`, assigned only at `:3822/:3857/:3869` at the bottom of a ~3,700-line
   chain. The repair was to track `hasClientVtable` separately (`:147/:3918`) and to add
   source-reading mutation tests. The structural hazard — a new branch's implicit `else`
   inheriting a wrong default — is unchanged. **Two lines make it impossible instead of
   tested-against: initialise to a `NotDecided` member and assert on it at `:3918`.**
2. **The `logged once per kind` flags are the same shape, on the persistence path.**
   `InventoryPersistence.cs:150-157` and its three siblings: `if (!saveFailureLogged) { ... }`.
   **A database that fails every write for eleven days logs one line, on day one, and is
   silent forever** — and that line is not the string the check counts. Keep the once-per-kind
   first line, but add a count, a periodic re-emit, and expose the counter in `StatsSnapshot`.
3. **Nothing anywhere counts save successes or failures.** `Save` returns `bool` and every
   caller discards it — `InventoryPush.cs:83` calls `InventoryService.Save(entityId)` and
   ignores the result. **You cannot answer "is this server saving?" from anything but the
   boot banner.** Similarly `SendOPHelper.CountSend` (`:22-25`) counts only successes, so a
   peer whose sends are all failing looks identical to an idle one.

---

### Finding 7 — A transient database read at login overwrites inventory and knowledge with the starter kit. PROVED.

`InventoryLoadPolicy` (`Multiplayer/Inventory/InventoryLoadPolicy.cs:41-54`) exists precisely
so a transient error never looks like a wipe. It is defeated by the ordering of its caller.

`Game/Inventory/InventoryService.cs:244-310`, `BindIdentity`:

- `:269` — `Store.Rebind(entityId, key, InventoryWire.DefaultModel)` runs **first**. On a
  fresh process `byKey` never contains the character key, so `Rebind`
  (`Multiplayer/Inventory/InventoryStore.cs:87-95`) carries the session's freshly-seeded
  starter kit **onto the durable character key**.
- `:273` — `Persistence.Load(key)`. That method (`InventoryPersistence.cs:111-121`) catches
  `Exception` and returns null for **three collapsed cases**: no row, unparseable payload,
  and *database read failure*.
- `:279-281` — `currentCount` is now the starter kit's count, not zero, and
  `ShouldApplyStored` returns false. The log says the reassuring
  *"no stored inventory ... keeping this session's contents"* (`:288-292`).

The next item move calls `InventoryPush.cs:83` → `Save` → upsert of the starter kit over the
player's real row. `Forget` (`:339-343`) saves unconditionally on disconnect, guaranteeing
the flush.

`InventoryStore.Rebind`'s own docblock (`:70-73`) describes exactly this hazard and guards it
with `!byKey.ContainsKey(key)` — a guard that is **structurally inert on a fresh process**,
because the only thing that populates `byKey` for a character key is the load that has not
happened yet.

**Progression has the identical shape.** `Game/Knowledge/ProgressionService.cs:81` sets
`EntityUid[entityId] = uid` before `Persistence.Load` at `:83`, and `:133-136` then calls
`Save` on the load path itself. A read blip at login can write a seed-only progression over a
full knowledge tree. Npgsql has no retry configured and no command timeout (`Db.cs:78-83`).

**The CHECK constraints cannot catch it**, and this generalises. `character_inventories`
has `CHECK (length(btrim(data_json)) > 0)` (`SchemaScripts.cs:253-254`), but
`InventorySnapshot.Write` (`Multiplayer/Inventory/InventorySnapshot.cs:42-55`) *always*
emits `{"Version":1,"Width":...,"Items":[...]}` — never blank. **The constraint can only
reject a string no code path can produce.** Same for `character_progression`. The
guarantees claimed in `Records/InventoryRecord.cs:25-27` and
`Records/ProgressionRecord.cs:21-23` are not enforced by the database.

**Fix.** Make the collapsed cases distinguishable where it matters: return
`Found(model)` / `NoRow` / `Unavailable`, and have `BindIdentity` **refuse to bind to the
durable key at all** on `Unavailable`. `InventoryPersistence.cs:138` already refuses
non-durable keys, so the session simply does not save — which is the failure mode the file's
docblock at `:23-25` already claims to have.

**The general lesson, and it is the most transferable thing in this document:** every guard
in this codebase that actually works is a **structural sentinel in the deserialiser**
(`Width <= 0 → null`; `does not decode as a ShipPlan → fallback + log`), not a constraint in
the database. The empty-payload hazard recurs in twelve places; five are guarded that way and
seven are not. The unguarded ones worth naming:

| field | empty value becomes | guard |
|---|---|---|
| `characters.data_json = ' '` | null → **CASCADE DELETE** (Finding 1) | none — CHECK lacks the `btrim` its siblings have (`:187` vs `:254`) |
| `characters.data_json = '{}'` | `Cosmetics == null` → empty slot → **CASCADE DELETE** | none |
| `character_progression.data_json = '{"Version":1}'` | a valid-looking **full knowledge reset** | no structural sentinel; only the load policy, and only if the session already has progress |
| `character_positions.x/y/z = 0,0,0` | world origin — inside the ±20 km box and above the deep floor, so `PlayerPositionPolicy.Decide` (`:52-63`) accepts it | none: no CHECK (`:361-363`), no load policy. The only member of the trio without one. Mitigated by fall-rescue. |
| `world-state.json = ''` | **empty world**, made permanent by the next save | none (Finding 5) |
| `alliance_ranks.permissions = ''` | a rank with **zero permissions** | none — `AllianceEndpoints.cs:840-848` writes it with no fallback to `stored.Permissions`, while the adjacent line falls back for `Name`. **A PUT that renames a rank silently strips every permission from it.** |
| `alliance_members.rank_id` dangling | `TryGetRank` throws → the whole Social Sheet dies | `UUID NOT NULL`, no FK, no CHECK; of four write sites only `AccountHandler.cs:411-416` validates |
| `LoosePartRecord.PartUid = ''` | dedupe silently off; `RemoveLoosePart('')` returns **true** while doing nothing (`WorldStatePersistence.cs:239-242`) → `MountedPartSalvageService.cs:58-59` reports a salvage that never happened → **the part respawns on restart = duplication** | none; latent only because the spawner always mints a fresh Guid |

---

### Finding 8 — Reliable inventory head-of-line blocks unreliable movement, because all component traffic shares channel 4. PROVED to the ENet source.

Six channels are created (`WorldsAdriftRebornGameServer.cs:4172`), enumerated at
`DLLCommunication/EnetLayer.cs:8-16`, and channel 5 is `REMOVE_ENTITY_OP` as documented. But
**every** component update, reliable and unreliable alike, goes on channel 4 — those are the
only two `COMPONENT_UPDATE_OP` sends (`Networking/Wrapper/SendOPHelper.cs:346`, `:489`).

Reliability is per-component (`MirrorSendPolicy.RelayReliabilityFor`, `:680-693`):
unreliable for `190602`, `1073`, `1051`, `1130`; **reliable for everything else, including
`1081` InventoryState**.

In ENet, an unreliable command carries the channel's current `reliableSequenceNumber`, and
`enet_peer_dispatch_incoming_unreliable_commands` only dispatches commands whose
`reliableSequenceNumber == channel->incomingReliableSequenceNumber` (`enet/peer.c:724`). So
**one lost reliable inventory packet stalls that peer's movement stream until it is
retransmitted.** The recorded incident's 49 KB in flight and 6.8 s RTT
(`HANDOVER.md:843`) is exactly the shape this amplifies.

This is the clearest example in the audit of a constraint imposed by the client rather than
chosen. Mitigations, cheapest first:

1. **Keep reliable bulk off channel 4 during movement.** A full `1081` push while people are
   flying is the worst case; it can be deferred or split.
2. **Add a seventh channel for unreliable component updates.** This follows the *exact*
   pattern already proven for RemoveEntity: the shim exposes
   `ENet_EXP_PeerChannelCount` and the server already gates behaviour on `>= 6`
   (`SendOPHelper.cs:93`). Both ends link *our* shim, so this is achievable — but it is a
   coordinated client-and-server release with a version-skew window, i.e. Finding 22's
   problem.

Related and cheap: **`enet_peer_send` failure is invisible to C#.** `enetLayer.cpp:229-233`
correctly checks the return and destroys the packet, but `ENet_Send` returns `void`, so
`SendRawComponentUpdateOp` returns true whenever the serializer produced bytes
(`SendOPHelper.cs:349`). `[relay-stats] emitted(...)` therefore counts *attempts*, and a
peer with a full queue reads as healthy. Returning the result through the shim makes the
existing diagnostics honest.

Also: **backpressure reacts to RTT, a lagging indicator, while the direct signal is read and
discarded.** `EnetPeerProbe` reads `reliableDataInTransit` (`DLLCommunication/EnetPeerProbe.cs:46,67`)
— literally the "49 KB in flight" number — surfaces it in the `[rates]` line and the stats
JSON, and uses it as a control input **nowhere**; `RelayBackpressurePolicy.Next` switches on
`rttMs` only (`Multiplayer/RelayCadence.cs:90-109`). Adding an in-flight term is cheap,
testable, and the data is already plumbed. (Note the probe's offsets are commented as
*"enet 1.3.17, x64 Windows ABI"* while the server is native Linux — the scalar layout should
be identical and there is a sanity check, but the comment is stale.)

---

### Finding 9 — The relay's emit interval and the loop's wake interval are the same number, in two files that do not know about each other. PROVED (the coincidence); INFERRED (that it explains the two-state defect).

The known defect: at join, a session settles into forwarding a position in <1 ms or holding
every position for a full 50 ms, and stays there. Not re-derived here — but two facts about
the machine around it are worth putting in front of whoever fixes it, because they suggest a
**zero-code experiment** that is much cheaper than the per-sender emit gate currently planned.

1. **`PollDrainPolicy.FirstWaitMs = 50`** (`Multiplayer/PollDrainPolicy.cs:43`) — only the
   first poll of an iteration blocks; the rest are zero-wait. So an idle loop wakes on a
   50 ms metronome.
2. **`RelayCadencePolicy.DefaultHz = 20.0`** (`Multiplayer/RelayCadence.cs:28`), i.e.
   `IntervalFor(20) = 50 ms`. One global `CadenceTimer` (`Networking/RelayEmitter.cs:138,153`)
   for all senders, anchored on its first `Due()` call — which is the first loop turn, at
   boot — and scheduled on the **ideal** grid (`_nextDue += _interval`, `RelayCadence.cs:170`)
   so it never re-phases.
3. `Relay.Tick` is called once per loop turn (`WorldsAdriftRebornGameServer.cs:4709`), and
   `Due()` is therefore **sampled only at loop wake-ups, which are themselves ~50 ms apart**.

A pending position can only be emitted at a wake; wakes are one emit interval apart;
therefore the age at emit is quantised to approximately {0, 50} ms. **That is bimodal by
construction**, and which mode a session lands in is decided by the phase between the
boot-anchored emit grid and that peer's arrival train — set at join, exactly as observed.
This is also consistent with the recorded negative result that moving `Relay.Tick` below the
drain did not fix it: moving the call within the turn does not change how far apart the
turns are.

**The experiment, and it needs no code change at all:** `WAREBORN_RELAY_HZ` already exists
(`RelayEmitter.cs:151`). Run the fifteen-soak sweep at `WAREBORN_RELAY_HZ=25` (40 ms). Three
outcomes, all informative: bimodality vanishes → the resonance is confirmed and the fix is
to make the two intervals coprime (drop `FirstWaitMs` to ~10 ms — more idle wakeups, still
negligible); bimodality persists at {0, 40} → the emit interval alone is responsible and the
per-sender gate is the right fix; unchanged at {0, 50} → my model is wrong and the phase is
set somewhere else. **An afternoon, no branch, and it either kills the defect or eliminates
a hypothesis before anyone rewrites the synthetic timeline.**

I am labelling the causal half INFERRED deliberately. The coincidence of the two constants
is PROVED and is worth fixing regardless: two files independently choosing 50 ms, with the
relay's entire timing resolution equal to the loop's wake period, is a coupling nobody
designed.

---

### Finding 10 — The production configuration exists in exactly one place on Earth. PROVED.

`deploy/wareborn-game-native.service:9` contains **one** environment line,
`WAREBORN_GAME_PORT=7779`. Production runs **twelve drop-ins** under
`/etc/systemd/system/wareborn-game.service.d/` carrying ~32 further variables. None of those
files is in version control; there is no `deploy/dropins/`, no export, no check comparing
live environment against an expected set. (`fauna.conf.bak-204457` sitting beside
`fauna.conf` is the level of rigour: a manual `cp`.)

*Correction to a plausible worry:* all twelve are in `/etc`, not `/run`, so **nothing
disappears on reboot.** The exposure is a rebuild, a restore from an older snapshot, or a
lost `/etc` — and then the game server comes up:

- **with no database** (`WAREBORN_DB` gone → `Db.IsConfigured` false → all four stores
  disabled) — plays fine, saves nothing, four `[warning]` lines, and the post-deploy check
  says OK (Finding 6b);
- with **one deposit** in Haven (Finding 20);
- with **no Wilderness** — 46 islands, 328 deposits, 215 databanks, 328 shards gone;
- with **interest disabled** — `WAREBORN_INTEREST_RADIUS_M` unset means radius 0, which
  means *send every entity to every client* (`Multiplayer/InterestPolicy.cs:72-82`), and
  *also* silently fail-closes the release world, which requires it (`:2816-2824`);
- with **no shipyard placement**, **no load barrier**, **no helm flight**;
- with **thrust gated on fuel** — `WAREBORN_FUEL_GATES_THRUST` defaults **ON**, so a ship
  that runs dry in flight drops;
- with the **metal handshake back on**, which silently voids `WAREBORN_SPAWN_DEPOSIT=1`
  (`:3161`).

Every one of those is silent. Not one produces an `[error]`.

**Fix, in value order.** (1) Copy the twelve drop-ins into `deploy/dropins/` with the
credential replaced by a `db.conf.example`, and point `hosting.md` at an rsync. Thirty
minutes, and it retires most of the above. (2) Make an unset `WAREBORN_DB` **fatal** in the
game server when a `WAREBORN_PRODUCTION=1` marker is set — the friendly local-contributor
default is right for a laptop and wrong for the VPS. (3) One `ConfigReport` at boot printing
every effective value with `(set)` / `(default)` and failing on a short must-be-set list;
the pieces already exist as `ReportPersistenceState`, `ReportConfiguration`,
`ServerLog.AnnounceMode`, they are just per-subsystem and advisory.

**Also undocumented and load-bearing:** `docs/hosting.md:120-146` lists 26 variables;
production sets 33. Missing from the runbook and materially behaviour-changing:
`WAREBORN_DB`, `WAREBORN_PLACEMENT`, `WAREBORN_LOAD_BARRIER`, `WAREBORN_HELM_FLIGHT`,
`WAREBORN_FLIGHT_FORCES`, `WAREBORN_FUEL_GATES_THRUST`, the five `WAREBORN_ISLAND_FAUNA*`,
`WAREBORN_SKY_WHALE`, `WAREBORN_SPAWN_*` (5), `WAREBORN_METAL_HANDSHAKE`,
`WAREBORN_DEPOSIT_COUNT`, `WAREBORN_ATLAS_RATE`, and more.

---

### Finding 11 — `LocalDomainHost.Synchronize` linear-scans the whole world, per ship, per flight frame. PROVED.

`Multiplayer/Domains/LocalDomainHost.cs:51-53`:

```csharp
_ownerByEntity.Where(x => x.Value == domain.Id).Select(x => x.Key).ToArray()
```

`_ownerByEntity` holds the **entire world** — `LocalDomainOwnership.Bootstrap` assigns every
region-owned entity, every tree, deposit, databank and atlas shard
(`Game/LocalDomainOwnership.cs:80-88`). At `tier1` that is ~3,600 entries.

It is on the hot path: `ShipFlightService.Tick` → `foreach (hullEntityId in _activeHullIds)`
→ `RefreshDomainMembership(domain)` (`Game/ShipFlightService.cs:635-640`) →
`_domainHost.Synchronize(domain)` (`:916`), at the 240 ms flight cadence.

Cost is **O(ships × world) every 240 ms on the poll loop**: 5 hulls × 3,602 = 18,010 entry
visits per frame (~75,000/s); 20 hulls ≈ 300,000/s plus 20 LINQ-chain array allocations per
frame.

**Fix:** maintain a reverse index `Dictionary<SimulationDomainId, HashSet<long>>` beside
`_ownerByEntity`. Local to one class.

**Bites:** high once there are both ships and players — and it is **100% invisible to the
soak gate, which has neither ships nor flight** (Finding 17). This is the exact intersection
of the two blind spots.

---

### Finding 12 — The avatar relay is globally scoped and O(n²) in serialisations. PROVED (structure), arithmetic (numbers).

`RelayEmitter.EmitAll` (`Networking/RelayEmitter.cs:453-593`) is a sender loop containing a
recipient loop, with **no distance test** — `_players.Others(senderId)` (`:483`) is everyone.
The only gates are seed-ordering (`:548`) and RTT backpressure (`:568`). The 190602 payload
is serialised once per sender (`:533`); the **1073 payload cannot be**, because each
recipient's stamp is its own synthetic timeline (`:562`, `:574`, and the comment at
`:530-532` says so).

| players | serialise 190602 | serialise 1073 | sends/tick | packets/s @20 Hz |
|---:|---:|---:|---:|---:|
| 2 | 2 | 2 | 4 | 80 |
| 3 | 3 | 6 | 18 | 360 |
| 8 (the cap) | 8 | 56 | 128 | 2,560 |
| 20 | 20 | 380 | 800 | **16,000** |

Each `SerializeComponentUpdatePayload` (`SendOPHelper.cs:387-419`) does an `AllocHGlobal`,
a `CreateReference`, a native serialise, a `new byte[len]`, a `Marshal.Copy` and two frees.
At n=20 that is ~8,000 managed arrays/s and ~16,000 native alloc/free pairs/s from the relay
alone, on the one loop thread. Plus a fresh `List` per sender per tick from `Others`
(`Multiplayer/PlayerRegistry.cs:106-117`) and ~22,800 dictionary lookups/s.

**This is a fix the project already made once, for ships and not for avatars.**
`HANDOVER.md:838` records the 2026-08-14 incident — *"distant ships and mounted parts
broadcast motion globally"* — resolved with distance/checkout-gated ship motion. Two players
20 km apart on different islands still exchange 40 position packets/s each.

**Fix, cheapest first:** (a) distance-gate the recipient loop —
`ResourceInterest.TryCenterFor` already gives every peer a world centre and `InterestPolicy`
already has the radius, so this is ~5 lines and kills the dominant term for spread-out
players. (b) The 1073 payloads differ only in one float; serialise once per *distinct stamp*
and patch the field, collapsing O(n²) serialisations to O(n) in the common case.

---

### Finding 13 — The state-lifetime map is chronological, not principled. PROVED.

There are exactly four sinks: Postgres, `world-state.json`, process memory, and recomputed.

**The stated boundary rule is real and it holds** — and it is a *deployment* argument, not a
data-modelling one. `SchemaScripts.cs:206-224` states it outright: *"the key is a character
uid, the thing that says a character uid is real is the characters table, and a file has no
way to enforce that."* So: **keyed on a character uid → Postgres; keyed on a transient
entity id → JSON.** `WorldStateSnapshot.cs:16-19` confirms the other half (*"the entity id
is deliberately NOT stored"*).

What the rule does not explain is what is *missing*, and chronology explains that perfectly.
`docs/research/persistence-map.md:85-88` (2026-08-11) defers "ship designs, ship blueprints,
knowledge/progression, and logoff position" as a clean follow-on. Two of the four have since
shipped; two have not. **The frontier is a date.**

Session-scoped, where a player loses work:

| state | where | what is lost |
|---|---|---|
| **Ship container contents** | `Game/ShipContainerService.cs:33-42,85-91` binds `InventoryKey.ForSession` | **everything stowed on their ship, every restart** |
| **Ship frame designs** | `Multiplayer/Ship/ShipDesignStore.cs:247` | every design they drew |
| **Ship blueprints** | `Multiplayer/Crafting/ShipBlueprintCatalog.cs:55` | every saved blueprint |
| **Open craft sessions** | `Game/Crafting/CraftSessions.cs:76`, `ShipBuildTimerService.cs:32` | **materials committed to an open slot, with no output** |
| Mounts on a non-persisted hull | `Game/PartMountService.cs:505-508` | parts bolted to the static test hull |

Three incoherences worth naming, because each is a half-landed change rather than a boundary:

- **Ship containers are session-keyed while the ship they hang off is fully persisted.** Half
  the object survives. And the project has already reasoned about container item loss: the
  salvage policy refuses to salvage a *non-empty* container because that is "the one loss
  this server CAN prevent today" — the small case is guarded, the every-restart case is not.
- **Fuel has a restore hook with no caller.** `ShipFuelLedger.RegisterAt` (`:173`) exists;
  nothing calls it, so a restored ship comes back with a full tank
  (`Game/Crafting/LoosePartSpawner.cs:297-299`). Not a loss — a free refuel, i.e. an economy
  leak.
- **Loot contents and loot depletion have opposite polarity.** Contents are a deterministic
  FNV-1a hash of the container key (`Multiplayer/Loot/LootTable.cs:83,114-125`), *deliberately*
  so that "a server restart would silently redistribute the world" cannot happen (`:19`).
  That is good design. But emptied-state is memory-only (`Game/Loot/LootStock.cs:28-31`), so
  **every chest in the world refills on every restart** — an unbounded item faucet gated only
  on restart cadence. The pair is incoherent by construction.

Also memory-only and therefore reset on every restart: all resource depletion (trees, metal,
atlas shards, fuel canisters, databanks). Nothing is *lost*, but the whole world re-harvests.

**Fix for the one that costs a player work:** containers hang off a `MountedPartRecord` which
already carries a durable `PartUid` (`WorldStateSnapshot.cs:265`). Key container inventories
on `container:{PartUid}`. That needs a v10 migration, because `character_inventories` is
`NOT NULL PRIMARY KEY REFERENCES characters` (`SchemaScripts.cs:236-237`) — so it is a
planned change, not a patch.

**Stale documentation that will mislead the next reader (PROVED):**
`docs/research/persistence-map.md:45-51` says deployables, ships, progression and position
are all "lost on restart" — all four now persist; `Game/Knowledge/PlayerProgression.cs:14`
says "In-session only" — it persists; `Game/Placement/PlacedShipyards.cs:15-17` says "until
the server restarts" — it is restored.

---

### Finding 14 — There is no SIGTERM handler. PROVED. But the loss window is small, and the framing matters more than the fix.

The only thing that clears `keepRunning` is Ctrl+C (`WorldsAdriftRebornGameServer.cs:4149-4152`);
there is no `PosixSignalRegistration`, no `ProcessExit` handler (the *login* server has one,
`WorldsAdriftServer.cs:90`). And even if the loop exited, the post-loop path saves nothing
(`:4924-4927`). `systemctl restart` sends SIGTERM, the runtime finds no handlers, the process
dies mid-iteration. **`[info] shutting down.` has almost certainly never been printed in
production.** The code knows: `PlayerPositionService.cs:109-111` — *"a server that is killed
never runs the disconnect path"*.

**But the durable loss is bounded and small**, because almost everything is write-through:

| state | write policy | lost on SIGTERM |
|---|---|---|
| Inventory | after every mutation (`InventoryPush.cs:83`) | ~0 |
| Progression | on knowledge/scanner events | ~0 |
| Crew | write-through | ~0 |
| World state | write-through at the spawn seams | ~0 |
| Ship pose | 2 s (`ShipFlightService.cs:127`) | ~2 s |
| **Player position** | **20 s** (`WorldsAdriftRebornGameServer.cs:2299`) plus a movement threshold; `SaveOnLeave` ignores the threshold but never runs on SIGTERM | **up to 20 s of travel** |

So the honest statement is: **a restart costs ≤20 s of movement, not a session.** The
brief's "save-or-lose" framing overstates this one — and understates Finding 2, which is the
restart hazard that actually costs sessions.

Two forward-looking notes. `TimeoutStopSec=15` is *irrelevant today* because nothing is
attempted during stop; it becomes load-bearing the moment somebody adds a save-on-shutdown,
and 15 s is optimistic for four table writes per online player. Fix the handler and the
timeout together, or the fix is worse than the gap. And OOM-kill is currently
indistinguishable from SIGTERM — same caveat.

**Fix:** ~30 lines — register SIGTERM, set `keepRunning = false`, flush every player's
position and inventory after the loop, then raise `TimeoutStopSec` to 60 and add
`TimeoutStopFailureMode=abort` so a hung save leaves evidence instead of vanishing.

---

### Finding 15 — Read-once and fail-silent client contracts are known one at a time and recorded nowhere collectively. PROVED.

This was the brief's highest-value question, and the honest answer is **50 silently-dead
visualisers and at least 14 read-once contracts we do not have a register for**.

**The four mechanisms** (all PROVED against the decompile), because they generalise:

- **M1** — a visualiser enables only when *every* `[Require]` is non-null, with no log, no
  counter, no else (`acs/Improbable.Unity.Internal/EntityVisualizers.cs:356-390`).
- **M2** — **reader** fields are injected even when the visualiser never activates (`:342-350`
  injects then checks); **writer** fields only on authority (`:235-258`). So a dead
  visualiser's getters still read live data — which is why Finding 15's databank bug actually
  throws.
- **M3** — `[Require]` is **inherited**. The effective set is the closure over the base chain.
  `LoomVisualizer` is documented in-tree as needing 1264; its real set is {1210, 1264, 1005,
  1004} via `CraftingStationBehaviour.cs:25,28`.
- **M4** — subscription style decides whether the **seeded** value is ever seen.
  `+= handler` and `.AddAndInvoke` fire with the current value; **`.Add(handler)` does not.**
  There are exactly **23 `.Add(` sites** in `acs` — that is the complete "the seed is
  silently skipped" set.

**Numbers.** 394 classes with a closed `[Require]` set, 660 `[Require]` fields, 295 distinct
ids required, 120 served. **205 fully served; 50 partially served and therefore silently
dead; 139 with nothing served.**

Status of the four known instances: **loom 1264 still dead** (and its set is bigger than
documented); **ship containers FIXED** (`InWorldInventoryVisualiser` needs exactly {1210,
1081}); **fuel gauge FIXED** (`FuelGaugeVisualizer` needs only 1105); **`FuelVisualizer`
1106 still dead**, on every hull.

The highest-yield single ids, if anyone wants the cheapest wins: **4334 DeteriorateFsimState
revives four visualisers** (`DeteriorateVisualiser` on ship, part *and* tree, plus
`ShipPhysicalityVisualizer`); then 1106, 1113, 1122, 1124, 1009, 1021, 5129, 1222, 2101,
one visualiser each.

**Two axes the current `ComponentAbsencePolicy` framing does not express, and should:**

1. **A served WRITER with no authority grant is exactly as dead as a missing reader.**
   Authority is granted only on a player's own entity (`WorldsAdriftRebornGameServer.cs:2147-2180`,
   `:3924`) plus 1011 on resources. Served but never authoritative anywhere, and blocking a
   client visualiser: **1130** (`SSPDeadReckoningBehaviour`), **1232**
   (`RigidbodyCollisionBehaviour`), **1118** (`ShipPanelBehaviour`), **4444**
   (`MountedGunShooterBehaviour`), **190300**. Most are probably intentional — **none is
   recorded as a decision**, unlike absence.
2. **"Served" ≠ "this entity has it."** Many branches are gated on
   `Game.Crafting.LooseParts.Is(entityId)` — 1105, 1236, 1303, 1118, 12281 — and
   `LooseParts.Register` is called **only** from `LoosePartSpawner.cs:94,176,267`.
   `BuiltShipSpawner` never registers. **So a built ship's own panels, lamps, sails and
   gauges are dead scenery even though those ids are "served."**

**Read-once contracts we are subject to.** The two known ones are confirmed and sharper than
documented: the inventory grid is read at **checkout**, not at panel open
(`InventoryVisualiser.cs:86`, `InWorldInventoryVisualiser.cs:108`, and no `Setup` on any
update path); and the interaction verb miss is worse than "no verb" — `InteractionEntry` is
a **struct**, so `FirstOrDefault` on a miss yields `radius = 0f` and every string null,
*overwriting* the sane field initialiser (`InteractiveObjectVisualizer.cs:28,67`), and
**nothing in the entire client subscribes to `InteractionsUpdated`** (grep count 0). We are
well defended on both. Fourteen more, all PROVED, of which the ones on components we
actually serve:

| site | frozen at checkout | id |
|---|---|---|
| `ShipyardVisualizer.cs:39` | `Deployed` — no `DeployedUpdated` subscription exists | 1205 |
| `TreeClientVisualizer.cs:26` | `Dynamic` — a tree's fall flag | 1036 |
| `ShipPanelVisualizer.cs:42` | `EnableBending` — we seed `false`, so panels are **permanently flat** on a curved hull | 1118 |
| `GlobalBiomeDataVisualizer.cs:159` | biome centres | 1253 |
| `IslandProxyVisualizer.cs:60,82-84` | resource-spawn tuning | 1010/1011 |
| `HornVisualizer.cs:27` | initial charge | 1107 |
| `PlayerVisualizer.cs:61-62` | first timestamp anchors interpolation | 1073/190602 |
| `StaticLocalTransformBehaviour.cs:30-36` | position/rotation applied **once** — any entity on the Static transform nature ignores every later 190602 | 190602 |
| `IslandLightningTimerVisualizer.cs:246-247` | lightning countdowns | 1254 |
| `SalvageableItemVisualiser.cs:48,54` | initial health drives the damage material | 1016 |

And one live `.Add(` seed-skip that matters today: `ClientAuthoritativePlayerMovement.cs:215`
subscribes `RelativeToUpdated` **without invoking**, and there is no imperative read — so
relogging aboard a ship leaves the local player unparented until the client writes its own
`relativeTo`.

**One live exception we are causing.** `PlayerPropertiesVisualiser.cs:27-37` reads
`playerProps.Properties["alreadyScannedDatabanks"]` with a **bare Map indexer**, while every
sibling in the same file uses `ContainsKey` first. We seed 1088 `properties` as an empty map
(`ComponentsSerializer.cs:737-740`) and that key appears nowhere in the server. Called from
`HasAlreadyScannedDatabank` → `DatabankHighlightableObject.cs:37`, **inside a coroutine, so
the throw kills it silently.** By M2 the visualiser being otherwise dead does not save us:
`playerProps` is a *reader*, injected regardless. **One line: seed the key as `""`.**

**Refuted, so nobody spends a session on it:** a sweep flagged that proxy transforms can
never register listeners because `SendOPHelper.cs:515` hardcodes `authority = true`. Both
halves are true and the conclusion does not follow — the field is the *old-style* reader and
`OnEnable` uses `+=`, whose generated `add` block immediately invokes with the current
`HasAuthority` (false), so `RegisterListeners()` runs anyway
(`ExactLocalTransformBehaviour.cs:12-13,36`). **No action needed.**

**Fix — and this is the structural answer to the brief's question.** Static analysis can
enumerate this table once; it cannot keep it true. The mod already patches
`Improbable.Unity.Logging.Debug.LogWarningFormat`
(`WorldsAdriftReborn/Patching/Performance/VisualizerEnableWarning_Patch.cs`). **A postfix on
`EntityVisualizers.UpdateActivation` that logs which `[Require]` field is still null converts
this entire class from static analysis into a runtime log line**, and makes the 50-row table
self-maintaining. It is the highest-leverage instrumentation available anywhere in this
system. Second: extend `ComponentAbsencePolicy` with a third category, *served but never
authoritative*, so the five deliberate denials in axis (1) become expressible and greppable
the way absence already is.

---

### Finding 16 — Crews have two writers and the game server caches at boot. PROVED.

`WorldsAdriftServer/Persistence/Accounts.cs:53-62` says it plainly: *"The login server is a
SECOND writer here, which v6 did not anticipate: the retail Social Sheet drives crews over
HTTP, so create, invite, boot, leave and disband all arrive on this process."*

The game server loads crews **once, at boot**, into an in-memory ledger —
`Game/Crew/CrewPersistence.cs:51-83`, `LoadInto`, which has exactly one caller. **No
invalidation, no re-read, no notification channel.** A player creates or joins a crew through
the Social Sheet; the game server serves the crew state it read at boot until the next
restart. The reverse direction is fine (the login server reads per request).

The same file names the contrast: alliances are clean because they have *"exactly ONE writer
and one reader, both this process"* (`:67-74`).

**Fix:** have the game server read crews on demand rather than caching. At eight players the
query is trivial, and it removes the class rather than a symptom.

**Related, and general: no cross-store transaction exists, and none can.** No repository
accepts an ambient transaction — every method opens its own connection (`Db.cs:78-83`) — so
a caller cannot make two writes atomic even in principle. Six multi-write sequences are
therefore non-atomic, the sharpest being **alliance founding**
(`Social/AllianceEndpoints.cs:228,239,244,249` — four autocommits; a failure after `:228`
leaves an alliance with no ranks, which that file's own comment at `:232-237` calls *"an
alliance that exists and cannot be opened"*). And `Handlers/Social/SocialHandler.cs:65-75`
catches bare `Exception` around the whole dispatch and answers `StoreUnavailable`, so a
CHECK violation, an FK violation and a dropped connection are indistinguishable to both the
client and the operator.

Credit where due: `WorldStatePersistence.SalvageBuiltShip` (`:356-389`) commits tombstone,
mount removals and loose-part upserts in **one** JSON write and **rolls back the in-memory
snapshot if the write fails** (`:380-387`). That is the best failure handling in the
repository. It is one-sided only because the materials it refunds go to Postgres by a
separate path that nothing sequences against it.

**Fix:** add an optional `NpgsqlTransaction?` to the repository methods and a
`Db.InTransaction(...)` helper — `ReplaceRoster` already demonstrates the pattern working.
Postgres↔JSON atomicity is genuinely hard and probably not worth solving; the honest answer
there is to order the writes so the recoverable one goes last, and log a reconciliation
warning when they disagree at boot.

---

### Finding 17 — The safety gate covers about 5% of the services it is trusted to gate, and 0% of ship code. PROVED.

The soak is a **movement-relay** gate. Calling it a ship gate is not honest.

- **Two bots, hardcoded** — `tools/relaybot/RelayBot/Program.cs:115-119` builds a fixed
  2-element array; no flag controls peer count.
- **No ship at all.** `run-soak.sh:226-232` starts the server with exactly two env vars;
  `WAREBORN_STATIC_SHIP` is unset so the hull/helm/deck block never registers (`:3268-3269`),
  and `$STAGE/data` is wiped at `:216` so nothing is restored. The bot has **zero**
  ship-construction code — its `ManHelm`/`SetShipInput`/`SetAboard` verbs are reachable only
  under `--ship-acceptance`.
- *Correction to the brief:* the soak world is **not** empty. It contains Haven, trees, metal
  nuggets, fuel pods and the shrine (all default-ON). It contains no ship, no loot, no
  databank, no deposits, no atlas, no fauna.
- **`run-ship-acceptance.sh` is not part of the gate** — the only mention in `run-soak.sh` is
  a comment at `:213`. It is a separate, manual, opt-in run.
- **Neither is in CI**, and **CI runs no tests at all** (`.github/workflows/msbuild.yml` is
  MSBuild + artifact upload). There are no git hooks.

**The default soak runs interest fail-open**, which is the sharpest of these:
`WAREBORN_INTEREST_RADIUS_M` is unset, so radius 0 = "send everything"
(`Multiplayer/InterestPolicy.cs:72-82`). `docs/testing.md:85-86` praises the ship-acceptance
runner for explicitly *avoiding* this. **The soak therefore cannot catch an interest
regression at all** — fail-open is indistinguishable from a pass. And the fauna mode's
step-gate is inert: `run-soak.sh:130` selects baseline world `tier1-island`, which does not
exist in `baselines/soak-levels.json`, so the comparison prints "NOT COMPARED" while
`docs/testing.md:153-154` names both keys as if both exist.

Coverage: ~1–2 of 29 services under `Game/` by default (~5%), ~6–7 with `SOAK_FAUNA=1`. The
**14 ship-domain files are at 0% under every soak invocation**. The three things it
structurally cannot see are **ships, n>2 fan-out, and interest scoping** — precisely the set
of defects in Findings 11, 12 and this one.

It is still valuable: it covers the hot path every packet takes. The fix is to stop claiming
more. Concretely: set an interest radius in `run-soak.sh`, add the `tier1-island` baseline,
and say in `docs/testing.md` what the gate does not cover.

---

### Finding 18 — CI runs no tests. PROVED.

`.github/workflows/msbuild.yml` builds Release and Debug and uploads artifacts. There is no
`dotnet test` anywhere in it, and no git hooks (`core.hooksPath` unset). **A green badge
means "it compiles."** Every gate in this project — 4,132 Multiplayer tests, 1,192 server
tests, the storage suite, the soak, the ship acceptance — is a human choosing to type a
command.

The project's own history argues this matters more than usual: `HANDOVER.md:385-391` records
that *"this repo has twice shipped a green suite over an unplugged feature"*, which is why
the scrap-salvage work was mutation-tested by hand. Mutation testing by hand is a heroic
answer to a problem CI would make routine.

Constraint worth stating: the `WorldsAdriftRebornGameServer` and `WorldsAdriftReborn` builds
need `DevEnv.targets` pointing at a real game install, so they cannot run in a hosted runner.
**But `WorldsAdriftRebornGameServer.Multiplayer.Tests` and `WorldsAdriftReborn.Storage.Tests`
deliberately reference nothing** (`docs/testing.md:14-17`) — they run natively on Linux in
under a second, with no game and no database. Those two are free to add and cover the large
majority of the pure logic. The brief forbids GitHub Actions here; a `tools/pre-push.sh` that
runs them, plus one line in `CONTRIBUTING.md`, achieves most of it locally.

---

### Finding 19 — Per-peer cleanup is a manual checklist, keyed on a pointer ENet reuses. PROVED, no live bug.

There are **nine** per-peer stores keyed on `ENetPeerHandle`: `GameState.ComponentMap`
(`Game/GameState.cs:44`), `SpawnPacers` (`:423`), `PeerManager.playerState`
(`Networking/Singleton/PeerManager.cs:57,74`), and the `_peers` dictionaries of
`ResourceInterestService` (`:88`), `ShipDomainInterestService` (`:39`),
`IslandTerrainInterestService` (`:121`), `IslandFaunaService` (`:170`), `SkyWhaleService`
(`:132`) — plus `EntitySendLedger` and `ServedComponentLedger`.

**`ForgetPeer` clears all of them today.** I checked each one; there is no leak. That is
genuinely good work and the file's comments show why it is: two of them record *previous
misses*, one of which "cost a debugging round" and one of which left native `ClientObjects`
references live for the process lifetime.

The finding is the *mechanism*, not the current state. The key is a raw `ENetPeer*` that ENet
**reuses** for the next connection on that slot, so a store that is added and not registered
in `ForgetPeer` does not leak quietly — **it hands the next player the previous player's
state.** The comments say so explicitly ("a reused handle would otherwise inherit a stale
'already delivered' set and wrongly skip seeding the next joiner's entities"). Correctness
currently depends on every future contributor remembering to add a line to a 200-line
function.

**Fix:** an `IPeerScoped { void Forget(ENetPeerHandle peer); }` and a registry, so
registration is the only step and `ForgetPeer` iterates. Half a day, and it converts a
convention into a type.

Related, and the same shape: `EntityIdAllocator` is monotonic and never reuses *within a
process*, but `_next` resets to 1 on restart (`Multiplayer/EntityIdAllocator.cs:47`). That is
safe **only** because clients reconnect fresh and nothing persists a raw entity id
(`WorldStateSnapshot.cs:16-19` deliberately stores keys, not ids). It is worth keeping that
invariant written down, because the day something persists an entity id, restart ordering
silently re-points it.

---

### Finding 20 — Three configuration flags default to the unsafe side, and one variable means two different things. PROVED.

**Inverted polarity — losing the drop-in turns the behaviour ON:**

| flag | live | default if lost | consequence |
|---|---|---|---|
| `WAREBORN_FUEL_GATES_THRUST` | `0` | **ON** (`Game/ShipFuelService.cs:136`, `ShipFuelPolicy.cs:223`) | empty fuel cuts thrust — **a ship in flight drops.** This flag is holding back a live behaviour and its default is the unsafe direction. |
| `WAREBORN_METAL_HANDSHAKE` | `0` | **ON** (`Multiplayer/IslandResourceHandshake.cs:143`) | the handshake turns on and **silently voids `WAREBORN_SPAWN_DEPOSIT=1`** (`:3161`); ore placement flips wholesale to client-driven |
| `WAREBORN_SPAWN_METAL` | `0` | **one name, two booleans, opposite defaults** — `:3127` is `!= "0"` (default ON) and `:3137` is `== "proven"` (default OFF) | legacy metal nodes reappear alongside deposits |

**`WAREBORN_DEPOSIT_COUNT` means two different things to two consumers.**
`Multiplayer/WorldEntities.cs:1188-1190` special-cases unset to **1**:

```csharp
int depositCount = depositCountEnv == null ? 1 : SpawnCountPolicy.CountFrom(...);
```

while `Game/Gathering/DepositFallbackSpawner.cs:45-49` calls `CountFrom` **directly**, whose
own default is the *full* table — **40**. And `CountFrom` is reached when the variable is set
but *empty*, so `WAREBORN_DEPOSIT_COUNT=` gives 40 on both paths while unset gives 1 on one
and 40 on the other. `DepositFallbackSpawner.cs:40-43` asserts the opposite in a comment:
*"Reuses the SAME knob ... so an operator who had tuned that knob keeps its meaning."* It
does not. **Fix: hoist the `null ? 1` into one `DepositCountPolicy` and call it from both.**

**Safe to remove** (redundant with the code default, so they carry no information and invite
the reader to think they do): `WAREBORN_ISLAND_FAUNA_MAX=4000`, `WAREBORN_CARRY_ECHO=1`,
`WAREBORN_SHIP_RECOGNISE=1`, `WAREBORN_SHIP_FERRY=0`,
`WAREBORN_DISTANT_ISLAND_SHELLS_ENABLED=0`.

**Vestigial while their feature is off:** `WAREBORN_METAL_COUNT`,
`WAREBORN_METAL_FALLBACK_SECONDS` (both gated behind `METAL_HANDSHAKE=0`), and the three
`WAREBORN_SHIP_FERRY_*` tuning values. Note that `METAL_COUNT` and `METAL_FALLBACK_SECONDS`
**stop being vestigial the instant `METAL_HANDSHAKE` is lost** — which is Finding 10's
scenario, and an example of why "vestigial" is not the same as "safe to delete".

One live documentation contradiction: `fauna.conf`'s own comment says ECOLOGY was
*"TEMPORARILY OFF 2026-08-18"* while the line reads `=1`.

---

### Finding 21 — `WAREBORN_LOG_VERBOSE=0` turned the firehose ON. PROVED. **Fixed in this branch.**

`ServerLog.cs:34-35` read:

```csharp
internal static readonly bool Verbose =
    !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAREBORN_LOG_VERBOSE"));
```

Any non-empty value enabled it, **including `0`** — the one value an operator reaches for to
be certain it is off. Its own docblock two lines above says `=1`, and every other opt-in flag
in the server parses its value.

This is not cosmetic. The same file's docblock (`:6-27`) measures this path at **1,207 lines
in a single second with two players**, sustained at 500–800/s, all synchronous writes on the
ENet thread, and states the observed symptom: *"Position relays died for seconds at a time
while animation kept flowing, which is exactly what 'we stopped seeing each other move' looks
like."* So the footgun's payload is a multi-second main-loop stall — and, per Finding 6d, it
would simultaneously push journald past its rate limit and blind the health check.

**Changed here**, because leaving it would be perverse: it now uses the same shared
`EnabledFrom` tokeniser (`"1"`/`"true"`/`"yes"`) as the terrain and fauna switches. The
variable is unset in production, so this cannot change current behaviour. It is the only code
change on this branch.

---

## 4. THE MOD / SERVER SEAM

The asymmetry that makes this a seam at all: **a server fix is instant and universal; a
client fix needs a patcher release and reaches players asynchronously**, if ever. A player
who never runs WAPatch again keeps the old behaviour indefinitely. There are **73 patch
files** under `WorldsAdriftReborn/Patching/` in 21 directories.

### Finding 22 — There is no version negotiation between the mod and the server, and retail's own gate has been deliberately deleted. PROVED.

`WorldsAdriftReborn/Patching/ContinueBootstrap/ConnectToNeededServersState_Patch.cs:9-15`
prefixes `ValidateClientVersion` with `__result = Resolved(null); return false;` — the
original never runs. That was necessary (it validated against Bossa's dead endpoint), but
**nothing replaced it.** The mod does check the *game* assembly's `ModuleVersionId`
(`WorldsAdriftReborn.cs:24-31`), which catches "wrong build of Worlds Adrift" — it says
nothing about the mod's own age relative to the server.

So: an arbitrarily old client mod connects to an arbitrarily new server, is served, and
neither side can tell. **This is not hypothetical** — manifests `2026.08.18-6` and
`2026.08.19-1` both shipped the connect defect (`HANDOVER.md:548-549`), so out-of-date
installs demonstrably exist in the wild, and the only reason they are not a live problem is
that the defect prevented them connecting at all.

Which patches, if absent, fail *hard* versus *subtly*, is the question to answer before this
matters. Two that are load-bearing and would fail subtly:

- **`Patching/Flight/EndOfTheWorld_Patch.cs`** pins `AtlasMultiplier` at `1f`. Without it
  every ship is permanently overloaded and cannot climb, `UpdateVertical` returns early and
  the OSD spams `"Ship weighs more than its atlas sky core can lift."` **Do not remove it**
  — it is one of the two mechanisms that make vertical flight work at all (the other being
  the flat 1,000,000 kg we seed for 1258). Recorded here only so nobody reads a future
  `[Require]`-completion exercise as "fixing a gap".
- **`Patching/SpatialOS/AssetLoadAck_Patch.cs`** — the correlated asset acknowledgement the
  terrain-checkout lifecycle depends on. An old client without it retains terrain rather than
  unloading it.

**Fix:** the manifest already carries a version string and the server already has a per-peer
handshake. A minimum-mod-version field, refused with a message that names the patcher, is a
small change and it converts "a stale client behaves strangely for reasons nobody can
diagnose" into "a stale client is told to update". It also gives the channel-count work in
Finding 8 a way to roll out safely.

### Finding 23 — Which side of the line things sit on: mostly right, with one visible class of exception.

The bulk of the 73 patches are genuinely client-only — rendering, input, UI, Unity
lifecycle, the EAC/Steam/analytics bypasses, the landing screen. Those are correct where they
are and there is nothing to say about them.

The class worth watching is **patches that override a value the server could simply serve
correctly**. The project has already had one and dealt with it well: a placement patch was
written client-side and then *deleted* once the real cause turned out to be a single
server-side parenting decision. The open instance flagged in the handover is the same shape —
the `GetTag`/`GetCurrentMask` patch that forces instruments onto railings, where the honest
fix was implementing bar pipes (now done) and **deleting the patch**. That deletion should be
completed; a patch left in place "just in case" after its cause is fixed is a permanent
divergence between what the server thinks it is doing and what the player sees.

The general rule worth writing into `CONTRIBUTING.md`, because it is the seam's whole
economics: **if a client patch exists to compensate for data the server sends, it is a bug
report against the server, not a feature of the mod.** Every such patch has to be shipped,
versioned, and eventually removed; a server change has none of those costs.

One structural note in the other direction: `ClientRigPolicy` lives in the server-side
Multiplayer library and is **`<Compile Include=... Link=...>`-linked** into the net35 mod
(`docs/testing.md:279-284`), so the mod and the tests compile the same source. That is a good
answer to a real problem (the mod cannot reference a net6 assembly) and the constraint —
keep that one file C# 7.3-clean — is written down. Worth knowing before anyone "modernises"
it.

---

## 5. SECURITY

Threat model: an untrusted game client on a player's machine, plus a public web portal.

**The posture is better than I expected, and it is worth saying so precisely rather than
filing a generic warning.**

- **CSRF on the portal is correct.** `Admin/AdminAuthPolicy.cs:48-65`: the token is a
  session-derived SHA-256 double-submit, the bearer token stays HttpOnly and only the
  one-way derivative reaches the page, and the comparison is
  `CryptographicOperations.FixedTimeEquals` with a length check. That is the textbook
  construction.
- **The admin gate fails closed.** `Admin/AdminConfig.cs:84-98` prints `[info] admin panel is
  off` and leaves `/admin` disabled when unconfigured. `TrySplitConfig` refuses a
  half-parsed credential rather than guessing.
- **Client-supplied identifiers are gated almost everywhere.** Of 21 component update
  handlers under `Game/Components/Update/Handlers/`, **20 reference an ownership check**; the
  one that does not is `ReferenceDataRequestState_Handler`, which pushes a read-only
  catalogue and has no per-entity subject. `PlayerRegistry.Owns` is unit-tested including the
  "an unregistered peer does not own entity 0" case (`docs/testing.md:304`).
- **The unauthenticated emblem render is bounded.** Output dimensions are a constant
  (`EmblemImages.cs:112` → `EmblemPainter.Size`), not attacker-supplied; the query code is
  capped at 96 characters (`EmblemUrlPolicy.cs:220-226`); an ETag is issued (`:178`). There
  is no attacker-controlled size, no path component reaching the filesystem, and no outbound
  fetch, so the three obvious risks — resource exhaustion, path traversal, SSRF — do not
  apply as written.
- The `"id"` stub that caused the locked-container bug is **fixed and confirmed in a live
  client** (`HANDOVER.md:195-204`).

### Finding 24 — The database credential is in the systemd environment. PROVED. Known, open, and the last real one.

`HANDOVER.md §10` already tracks it. Stated precisely: the value is in a drop-in under
`/etc/systemd/system/wareborn-game.service.d/`, so it is readable by anything that can run
`systemctl cat` or read `/proc/<pid>/environ` — i.e. **any root-equivalent process, and it is
printed by ordinary diagnostic commands an operator or an agent would run without thinking**.
This audit tripped over it accidentally while enumerating configuration, which is the
practical demonstration of the problem: a secret that leaks into routine output will
eventually leak into a paste.

Blast radius: full read/write on the Postgres holding every account, character, inventory and
progression row. It is loopback-only on port 5434, so exploitation requires a foothold on the
box — but "the credential is only as good as the box" is exactly the assumption a rotation
exists to remove.

**Fix:** the login server was already done correctly — `/etc/wareborn/login.env` via
`EnvironmentFile=` (`docs/hosting.md:114`). Do the same for the game server, root-only mode
`0600`, and **rotate**, because the current value has been in a readable location for an
unknown period. Never reproduce the old value anywhere.

### Finding 25 — Flood and rate limiting: one unthrottled log line is the reachable amplifier. PROVED.

`WorldsAdriftRebornGameServer.cs:3673` emits a bare `Console.WriteLine` for **every packet
from an unregistered peer**. Every other hot-path log in the server is gated behind
`ServerLog.Verbose` or `PacketFaults.ShouldLog`; this one is not. A peer that connects and
then sends without registering costs one synchronous stdout write per packet, up to 32 per
loop turn (the drain budget), from the simulation thread into journald.

That is the cheapest denial-of-service available against this server and the fix is to wrap
it in the throttle that already exists next to it. Minutes of work.

Beyond that, the poll loop's structure is sound: the drain is **bounded** at 32 events per
turn (`Multiplayer/PollDrainPolicy.cs:40`) precisely so a flooding client cannot starve the
timers, and each packet is processed inside a per-packet `try/catch` that frees the packet
and keeps draining (`:4757-4790`), so a malformed inventory delta from a modified client
costs that packet and not the process. Both of those are deliberate, commented, and correct.

**Scope note, stated honestly:** §5 is a review of the surfaces the brief named plus the
handler-ownership sweep. It is not a full application-security review of the portal's 27,183
lines. The alliance and social endpoints in particular carry the non-atomic multi-write
sequences described in Finding 16 and a bare `catch (Exception)` that collapses distinct
failures into one client-visible answer (`Handlers/Social/SocialHandler.cs:65-75`); those are
correctness findings that also blunt security diagnostics, and a dedicated pass over
`WorldsAdriftServer/Social/` would be worth doing separately.

---

## 6. WHAT TO DO, IN ORDER

Not a backlog — a reading of the risk table. Each line is small enough to do in one sitting.

**This week, because they are cheap and they stop data loss:**

1. **Refuse to `Write` a shorter roster than was read** (Finding 1). Five lines. Turns a
   silent permanent CASCADE delete into a loud no-op while the real fix is designed.
2. **`SchemaTooNewException`, rethrown past the four persistence catches** (Finding 2).
   ~20 lines. This is the one that already cost a character's progression.
3. **Three more greps in `check-game-server.sh`** (Finding 6): `persistence is OFF` → fail,
   `DROPPING the whole AddComponent batch` → fail at ≥1, `Suppressed .* rate-limiting` →
   inconclusive. ~10 lines, and #2 becomes detectable.
4. **fsync `world-state.json`, quarantine the empty case, keep one generation** (Finding 5).
5. **Seed `properties["alreadyScannedDatabanks"] = ""`** (Finding 15). One line, removes a
   live exception in a coroutine.
6. **Log the player cap in the boot banner and on every connect** (Finding 3). Two lines.
   Does not raise the cap — makes it visible before it is hit.
7. **Throttle the unregistered-peer log line** (Finding 25).

**This month, because they retire classes rather than instances:**

8. **`tools/evidence/` — a checked-in bundle reader, a coverage-declaring search script, and
   `EVIDENCE.md`** (Finding 4). Highest leverage in the document: it makes the other
   nineteen easier to find and stops the sixth wrong "the client does not have this".
9. **Commit the twelve systemd drop-ins, credential-free** (Finding 10).
10. **A Harmony postfix on `EntityVisualizers.UpdateActivation` logging the null `[Require]`
    field** (Finding 15). Converts 50 rows of static analysis into a self-maintaining runtime
    signal.
11. **SIGTERM handler + flush + a `TimeoutStopSec` that reflects it** (Finding 14).
12. **`tools/pre-push.sh` running the two dependency-free suites** (Finding 18).
13. **Move `WAREBORN_DB` to a root-only `EnvironmentFile` and rotate** (Finding 24).

**Before the player count grows — and note that (14) is what makes (17) safe:**

14. **Distance-gate the avatar relay** (Finding 12). ~5 lines, kills the dominant n² term.
15. **Reverse index in `LocalDomainHost`** (Finding 11).
16. **Run the fifteen-soak sweep at `WAREBORN_RELAY_HZ=25`** (Finding 9). No code change, and
    it either kills the two-state defect or eliminates a hypothesis before anyone rewrites
    the synthetic timeline.
17. **Only then, `WAREBORN_MAX_PLAYERS`** (Finding 3).

**Deliberately not proposed:** removing `EndOfTheWorld_Patch.cs` or the 1258 seed; changing
the overload string; "completing" the sky core's `[Require]` set; raising the player cap
before 14 and 15; or re-channelling the transport (Finding 8) before Finding 22 gives it a
safe rollout.
