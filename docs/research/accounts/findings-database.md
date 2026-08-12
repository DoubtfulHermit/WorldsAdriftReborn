# FINDINGS — DATABASE

## DECISION: SQLite, one file, WAL, opened directly by both processes
**Our own 2024-era objection in `JsonFileStore.cs:9-11` — "native e_sqlite3 under Wine is an
extra thing to go wrong" — was a reasonable guess and is EMPIRICALLY FALSE.** Tested on both
machines in throwaway prefixes; the running services were never touched and `roster.json` is
byte- and mtime-identical afterwards.

| test | local (Wine 11.14) | VPS (Wine 9.0, ext4) |
|---|---|---|
| `e_sqlite3.dll` loads | OK | OK |
| WAL mode takes effect | OK | OK |
| **two separate Wine processes writing concurrently** | **1000/1000 rows, busy/err=0, `integrity_check=ok`** | **1000/1000, 0 errors, ok** |
| **`FailFast` mid-transaction** (5000 uncommitted) | rolled back cleanly, `ok` | — |
| **`SIGKILL` during commit, dirty WAL on disk** | 17 MB WAL recovered, **every committed batch survived** | 4 MB recovered, 240,200 rows, **all survived** |
| **native Linux `sqlite3` + Wine writing the SAME file** | 298k + 3.7M rows, 0 errors, `ok` | 8k + 358k rows, 0 errors, `ok` |
| `File.Move(overwrite)` + nested `accounts/<key>/` create | **OK** | — |

**Two open questions closed as side effects:** Wine's `File.Move` overwrite path and
per-account directory creation both work (`findings-accounts.md` listed these as
undetermined). And **VPS durable-commit latency is ~4–8 ms** — local numbers are tmpfs and
meaningless. That single measurement produces the one hard design rule below.

## ⚠ THE THING THAT WILL ACTUALLY BITE — deployment, not Wine
`dotnet build` puts the native library at `runtimes/win-x64/native/e_sqlite3.dll`, **not** flat
beside the DLLs. `docs/hosting.md` deploys with a **flat glob**. Reproduced:
```
System.DllNotFoundException: Unable to load DLL 'e_sqlite3' ... (0x8007007E)
```
On the login server that is a healthy-looking process that **dies the moment someone presses
Play**. Fix `hosting.md` in the same commit as the first DB change.

## WHY A SCHEMA — it is about refusing bad data, not speed
At 8 players SQLite's performance is irrelevant. Its ability to make a documented client crash
**unrepresentable on disk** is not:
| constraint | crash it forecloses |
|---|---|
| `CHECK slot_type IN (…)` | unguarded `Enum.Parse` **blanks the entire inventory** |
| partial unique on worn slot | equipping a second wearable **replaces** the first (1280 Bug 1) |
| partial unique on hotbar | two items claiming one slot |
| `CHECK hotbar_slot <= 7` | `>= 8` silently drops the item |
| `CHECK time_to_build = 0` | `>0` greys the item out permanently |
| `CHECK meta_json <> ''` | unguarded `TryGetValue` on every icon update |
| composite FK on `wearable_health` | *"an unregistered id throws EVERY FRAME"* |
| `CHECK belt_row < height` | throws in the client's constructor |
| `CHECK length(trim(display_name)) > 0` | missing `screenName` ⇒ **QUIT dialog** |
| **partial unique on `steam_user_key`** | two accounts claiming one SteamID — the mid-session token swap that *"would look like corruption"* |
⚠ **The FKs require `PRAGMA foreign_keys=ON` per connection — SQLite defaults it OFF.** That
pragma is half the value of the design.

**Discipline: relational where we query or constrain; JSON column where we only round-trip.**
Cosmetics are never queried and must survive byte-faithfully → blob. `slot_type` decides
whether an inventory renders → column with a CHECK.

## PROCESS OWNERSHIP — exactly one writer per table
Login owns `accounts`/`sessions`/`pairing_codes`/`characters`. Game owns inventory,
progression, world. **`characters` is the only shared set and the game server only SELECTs
it** — to resolve the `characterUid` from the 1088 publish. **So no cross-process transaction
is ever needed, and there is no reverse write path to get wrong.** If both write anyway, WAL +
`busy_timeout` serialise correctly — a mistake degrades to a lost update, never corruption.

**The hard rule, from the 4–8 ms measurement:** saving a 60-item inventory as 60 autocommits
would stall the ENet loop ~0.3 s. **Every save is exactly one transaction**, and the game
server writes behind — dirty flag, flush ≤30 s, plus disconnect and shutdown.

## WHY NOT THE ALTERNATIVES
**JSON:** the "a human can fix it" case is true for a 9 KB roster and false the moment
inventory lands. **LiteDB:** works under Wine, rejected for opaque format, no CLI, no
`integrity_check`, and a single-writer design. **Postgres/MariaDB outside Wine:** the
strongest alternative, and **its entire premise was "Wine is the risk" — now dead.** Its costs
are real: a third service, a network dependency in the login path (Postgres down ⇒ frozen
menu), and a VPS already at **84% disk** running Docker, another Postgres, SQL Server, Godot
servers, frps, Matrix and Forgejo. The sign-up page being an **EmbeddedResource in the login
server** means there is no third process and no third writer.

## LAYER — a NEW shared library `WorldsAdriftReborn.Storage`
**Not `...Multiplayer`** — its csproj states *"Deliberately has NO references… unit-tested
natively on Linux without Wine"*, and one of its files is **source-linked into the net35 mod**.
Adding a NuGet package there breaks the contract and drags SQLite toward the mod build.
Pure `Policy/` (no `SqliteConnection` may appear there) + thin `Repositories/` + `Records/`
that **name no game type** — conversion happens in a thin adapter inside each server.
**`RosterPolicy.cs` and its tests are not touched at all.**
The package ships `linux-x64/native/libe_sqlite3.so`, so **repository tests still run natively
on Linux** — `docs/testing.md`'s "no Wine, no game install" promise survives.
`SchemaScripts.cs` is **append-only**; `SchemaMigrator` is pure (int → list of scripts).

## MIGRATION — read-only against the JSON, additive to the DB
Live state: 3 entries, all with valid GUID uids, one with `"Cosmetics": null` (the empty slot
— exactly `RosterPolicy.IsEmptySlot`'s semantics). Import once at startup, in one transaction,
guarded by a `schema_meta` stamp, owned by `WAREBORN_LEGACY_ROSTER_OWNER` (held against a
reserved unclaimed row if that account doesn't exist yet — **deterministic, no
whoever-logs-in-first race**). **`roster.json` is never moved, renamed, deleted or rewritten.**
Rollback is therefore free: `WAREBORN_STORE=json`, delete the db, restart. **Keep the flag for
one release then delete it — a permanent dual path rots.**

**Deploy the login half ALONE and FIRST** — restarting `wareborn-login` is safe; restarting
`wareborn-game` orphans every connected client with no recovery.

## OPERATIONS — the 2am story
⚠ **`sqlite3` is NOT installed on the VPS.** Install it in the same change, or you have built a
2am story you cannot execute at 2am.
Reading is safe while the servers run (proven). **Editing requires stopping the owning
service** — not for corruption, but because the game server holds inventory in memory and will
overwrite you at the next autosave.
Backups: `.backup` (online, correct against a live writer) **and** `.dump` (plain-text SQL —
**the honest replacement for "you can open the file"**: greppable, diffable, restorable).
⚠ **`cp wareborn.db` alone is WRONG** — it silently omits `-wal` and loses every recent commit.
⚠ On restore, **`rm -f *-wal *-shm`** or you replay the OLD wal onto the NEW db.

## NOT IN THE DATABASE, ON PURPOSE
`defaultSchematics` (server policy) · the knowledge graph (deployment config) · lore *text*
(authored content) · **position** (client-authoritative; re-seeding is the sky-teleport bug).
**All belong in version-controlled config where they get reviewed in a PR, not in a database
where they get hand-edited at 2am.**
And `world_nodes` keys on a **stable world key, never a runtime entityId** — ids are
reallocated every boot, so an entityId key silently reattaches state to the wrong rock.

## COULD NOT DETERMINE
Whether Wine↔native concurrent writing is safe on **NFS or any network filesystem** (SQLite
locking is unreliable there regardless of Wine; `/opt` is ext4, so fine today — remember it if
the data dir ever moves). Npgsql under Wine was **not tested at all** — close that gap first if
SQLite is rejected. No 8-player session was measured; all load was synthetic.
