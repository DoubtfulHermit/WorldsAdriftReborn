# FINDINGS — PER-PLAYER ACCOUNTS

## LEAD: it already works. The server is throwing the answer away.
**Each client already sends a distinct, globally-unique, real account id today, with zero
code changes.** Steam is live under the mod — the BepInEx log shows `SteamAPI.Init()`
succeeding, a real 64-bit SteamID, and a live HTTP auth round trip.

That id is on the wire right now (`BossaNetBootstrap.cs:229`, `:319-326`):
```csharp
jObject.Add("steamCredential", CreateCredentialObject("steam", steamUserId, steamAuthToken));
// -> { "platformId": "steam", "secret": <ticket>, "userKey": <the SteamID> }
```
**CORRECTION TO THE BRIEF: the account id is in `userKey`. `platformId` is the constant
string `"steam"`.**

And the login server already deserializes it and discards it
(`SteamAuthenticationHandler.cs:14-19`) — every player gets `"superCoolToken"`, playerId
`"777"`, bossaId `"999"`.

**Cheapest path: SERVER-ONLY, ~1 day, no client change, no redistribution.** Read
`steamCredential.userKey` at `/authenticate`, mint a per-account token, and resolve the
account from the **`Security:` header the client already sends on every roster request**
(`CharacterSelectionHandler.cs:321-326`). The loop closes entirely inside the login server.

Today **no handler reads any header** — `grep -rn "Header"` over `WorldsAdriftServer` returns
**zero hits**. The only trace they exist is a comment at `CharacterAuthHandler.cs:9-13`
saying "in the future we should check all of those, for now allow all".

## THE ONE THING THAT BREAKS IT — and you will hit it immediately
**Two clients on ONE machine share one Steam account**, so they get one `userKey` and land in
one roster. **All local two-client testing does this.** Hence a per-launch override is not
optional — which is exactly what the dead `ModSettings.steamUserId` should become.
**Ship both: Steam id as the default, config override for testing and Steam-less friends.**

**Likely implication:** every two-client test to date **shared one identity** — consistent
with the "two peers, same uid" collision recorded in `verify/identity-claim.md`.

## `1234` IS TRULY HARDCODED — verified
An inline string literal inside `string.Format`, at **four** sites
(`CharacterSelectionHandler.cs:93, :143, :218, :293`). Not a const, not a field, not config.
Changing it needs an IL/transpiler patch.

## `ModSettings.steamUserId` — VERIFIED DEAD, and its description is a lie
Bound to `Steam_UserId`, default `"steamId"`. Exhaustive grep: **`steamUserId`, `steamAppId`
and `steamBranchName` are read by nothing.** The shipped config even tells the player
*"Its not important for the functionality to set this to a specific value"* — which becomes
false the moment we wire it. **Rewrite the description in the same commit.**

## THE STEAM-LESS FALLBACK — the failure mode to absorb
`BossaNetBootstrap.cs:46-47,140-149`: if Steam is dead, **every such client authenticates as
the literal string `"steamUserId"`** — one shared bucket. Treat it as *unknown*, name it in
the log, and refuse to mint a roster for it.

## OPTIONS, ADJUDICATED
- **(c) Real Steam auth — ALREADY LIVE, cost 0 client-side. ★ PRIMARY.** The only option that
  survives redistribution with **zero friend configuration**. Caveat: unverified whether
  `SteamAPI.Init()` succeeds on a machine that doesn't own the delisted appid 322780.
- **(a) Mod config override — ~5 lines. ★ SHIP ALONGSIDE.** One Harmony prefix on
  `AuthenticateWithSteam()`. The friend already hand-edits `GameServer_Host`, so the
  ergonomics are proven.
- **(e) URL prefix — 0 client code, config only. ★ STRONG DARK HORSE, verified end-to-end.**
  The base URL is **string-concatenated at every consumer**, never `new Uri(base, relative)`,
  so `REST_ServerUrl = .../u/alice` prefixes every call. Empirically confirmed against real
  NetCoreServer: `characterList` and `character` routes **survive unchanged** (they use
  `Contains`), but `/authenticate` and `/authorizeCharacter` **break** because they use `==`.
  **One-word fix: make those two suffix matches — worth doing even if prefixes are never
  used, because it removes a trap.**
- **(b) Machine-derived — REJECTED.** Unstable, invisible, unfixable on collision.
- **(d) Launcher token — DEFERRED.** No launcher exists; it delivers nothing (c)+(a) don't.

## STORAGE — one file per account, mirroring the shipped pattern
```
<data>/characters/roster.json        # LEGACY - never moved, never deleted
<data>/accounts/<key>/roster.json
```
New pure `AccountPolicy` (`IsUsableUserKey`, **`ToStorageKey`**, `MintToken`) + thin
in-memory `SessionRegistry`. `CharacterRepository` gains `accountKey` as its first parameter.
**`RosterPolicy` needs NO change at all** — it already takes everything as arguments. That is
the pattern working.

**`ToStorageKey` is the security-relevant one:** the only thing between a client-supplied
string and a filesystem path. Must be total, must reject traversal, and must be unit-tested
against `../`, absolute paths, reserved names and empty input.

## THE GAME SERVER — the uid is enough; do NOT plumb accounts into it
Today peers are keyed by the raw `ENetPeer*` pointer and **nothing parses `characterUid`** —
it appears nowhere in the game server.
**Once per-account rosters ship, an account can only be shown its own characters, so two
honest clients cannot select the same uid.** Keep a `CharacterClaims` guard as defence in
depth against a stale/duplicated/modified client — but there is **no channel** between the
two servers (no project reference, no HTTP, no shared file), so plumbing the account id
across would cost a new wire field to buy almost nothing.
Release the claim inside `ForgetPeer` **above line 98**, since anything below reads null.
Parse with **`System.Text.Json`** — **Newtonsoft is not available to the game server.**
*While in there:* `GameState.ComponentMap` is keyed by peer and **never removed anywhere**,
contradicting `ForgetPeer`'s own doc comment. Fix in the same commit.

## WHAT THE CLIENT DOES WHEN REJECTED — nothing. It would not notice.
Two independent stubs: `RegisterDisconnectCallback` is an empty TODO, and `IsConnected()`
returns `peer != NULL` forever. Plus the mod disables the client's own liveness check.
**Net effect of an ENet kick today: a frozen, silent world, no error, no way back.**
Also `ENet_Disconnect` is synchronous and re-enters `enet_host_service` for up to **3 s on
the caller's thread** — calling it from the main loop stalls every other player.
**So: Phase A — do NOT disconnect.** Log loudly, refuse to bind the duplicate uid, let the
second peer play as an unidentified entity whose state is never persisted. **Nobody's save is
corrupted, which is the entire point.** Phase B (the reconnect work) makes rejection visible
for free — `UnrecoverableErrorState` shows a RETRY/QUIT modal whose body is **the server's
reason string verbatim**.

## SECURITY POSTURE — write this into the code, not just the docs
**Accounts here mean "the server can tell two players apart and keep their stuff separate".
They do NOT mean "a player cannot impersonate another".**

**Steam ticket validation is IMPOSSIBLE — not "not yet".** It requires the Steam Web API
`AuthenticateUserTicket` endpoint **and a publisher key for appid 322780**, a delisted game we
do not own. So `userKey` is an unauthenticated claim: a username with no password. A modified
client can claim any `userKey`, any `characterUid`, any URL prefix.

**Defend (accidents, not adversaries):** path traversal in `ToStorageKey` · two peers holding
one uid · the `"steamUserId"` fallback silently becoming a shared account · garbage keys
creating files.
**Deliberately do NOT build:** ticket validation (impossible) · passwords, expiry, refresh,
rate limiting, TLS, signed tokens. **The token is a routing key, not a credential — do not
name it `AuthToken` or add HMAC, that is theatre that would mislead the next reader.**

## MIGRATION — nothing destructive
`characters/roster.json` is **never moved, never deleted, never rewritten**. One-time
adoption: when account A first requests a roster and its file doesn't exist, copy the legacy
file **iff** A matches `WAREBORN_LEGACY_ROSTER_OWNER`. Deterministic — no
"whoever-logs-in-first-wins" race. Everyone else starts fresh (the seeder already handles it).
**The friend's installed client is untouched** — the strongest argument for (c): their
existing install produces a distinct account on first launch with no download and no edit.
**Rollback:** an env flag forcing every request to the legacy key restores today's behaviour
exactly, because the legacy file was never modified.
**One-way door to flag:** the first login after this ships gives a friend a *fresh* roster.
Hand-copy their entry, or make them the legacy owner, **before** they connect.

## ORDERED PLAN
**Part I — "the server can tell two players apart" (login-server only, testable with curl:
no game server, no client, no Wine):**
1. **PROBE FIRST** — log `userKey` and the `Security`/`characterUid` headers, change no
   behaviour, connect two clients from **two machines**. An afternoon, and it de-risks
   everything below. *(Two of my six open unknowns are settled by this one step.)*
2. `AccountPolicy` + xUnit, no wiring.
3. Suffix-match the two `==` routes; add the account-key resolver.
4. `SessionRegistry` + mint a real token.
5. Per-account rosters. **After this the headline is delivered.**
6. Migration + legacy-owner env var + `docs/hosting.md`.
7. Wire `ModSettings.steamUserId` as override + **rewrite its description**.

**Part II — "a player cannot impersonate another":**
8. `CharacterClaims` pure module + tests. 9. Wire it (Phase A: refuse to bind, don't
disconnect) + fix the `ComponentMap` leak. 10. *(later)* `RegisterDisconnectCallback` +
a reason channel. **Step 10 is the only item that moves the needle, and even then only against
accidents.**

## COULD NOT DETERMINE
**Whether a friend's `SteamAPI.Init()` succeeds** — needs their machine; `steam_appid.txt`
usually suffices with Steam merely running, but ownership of a delisted app is untested.
**This is why the config override is not optional.**
**Whether the `Security` header actually arrives** — the client provably sets it and
NetCoreServer provably can read it, but the server has never read a header, so it has never
been observed end to end. **Step 1 resolves it; do not build step 4 on it first.**
Whether two *different* Steam accounts really yield different `userKey`s (certain by
construction; the observed log is one machine, one account).
Whether Wine's `File.Move` overwrite path works for `accounts/<key>/` — a **per-account
directory create** that the current single-file layout never exercises.
