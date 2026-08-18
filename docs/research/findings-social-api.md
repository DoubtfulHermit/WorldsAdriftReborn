# The Bossa social API, reconstructed

Static analysis of the shipped player client, 2026-08-18. This is the contract
`WorldsAdriftServer/Social/` implements. Everything below is PROVED from the
decompile at `/home/ttanurhan/Games/WAReborn-decompiled` unless marked otherwise.

The implementing code cites this file by path in ~8 comments.

## Why this exists

Alliances — and the Social Sheet that hosts the CREW panel — were backed by a
Bossa REST service, not by SpatialOS:

```csharp
// acs/StagingConfig.cs
WAConfig.OverrideDefault(ConfigKeys.AlliancesUrl,
                         "https://alliances-staging.api.bossagames.com");
```

That host is dead. Opening the sheet fetches from it, the request throws, and
`SocialCharacterSheet.TriggerAllianceExceptionHandler` — **shared between
alliances and crews** — covers the whole sheet with *"Can't retrieve alliance or
crew data"*, including the CREW tab.

## The finding that set the size of the job

**The crew panel is HTTP-driven, not component-driven.**

```csharp
// acs/Travellers.UI.Social.Crew/CrewScreen.cs:80-84
public void InjectDependencies(ICrewClient crewClient) { _crewClient = crewClient; }

// acs/Bossa.Travellers.Crew/CrewClient.cs:26-30
public CrewClient(ISocialClient socialClient) {
    _socialClient = socialClient;
    _crewServer = new CrewServerImpl();          // HTTP
}
```

`CrewServerImpl` builds only `SocialRequest`s, and `SocialRequest.cs:69` is
`SocialHelper.AlliancesServerUrl + "/" + endpoint`. Create crew, list members,
list invites, boot, leave and disband all went to the dead host.

The only SpatialOS parts of the retail crew feature are `CrewClientBehaviour`'s
beacon clock and a notification that calls
`SocialCharacterSheet.TriggerCrewDataRefresh()` — i.e. it triggers an HTTP
re-fetch and carries no membership data at all.

Consequence: answering only *"this player has no alliance"* would have made the
sheet **open** and then shown a permanently empty crew with a Create button that
fails. The crew endpoints are not optional.

### The pre-alliance path is a dead end

`OldCrewScreen`, reachable by forcing `ConfigKeys.AlliancesEnabled` false, is not
a working fallback:

```csharp
// acs/Travellers.UI.Social.Crew/OldCrewScreen.cs:169-174
public void CreateCrew() {
    _noCrewObject.SetActive(false);
    _hasCrewLeftPane.SetActive(true);
    _hasCrewRightPane.SetActive(true);
}
```

Three `SetActive` calls. No event, no request. Its `ICrewClient` field is
declared, never used and never injected — the class has no `[InjectableClass]`.
`CrewManagementVisualiser`, which does feed it, is marked
`[Obsolete("Remove once alliances are switched on permanently")]`. In that UI a
crew only ever came into existence via a successful invite.

## Transport rules — these decide success or a modal

| Rule | Evidence |
| --- | --- |
| **HTTP 200 for everything**, including creates and failures. 201/204 are errors to this client. | `HttpHelper.HandleResponseStatusCode` returns early only on literal `200`; the body of a non-200 is discarded |
| Errors ride **in band on a 200** as `{"success":false,"errorCode":"..."}` | `SocialRequest.cs:102-112` |
| The body must **always be a JSON object**, even when no data is expected. Empty body or array root throws. | `HttpHelper.cs:85-86` parses then assigns `jToken["statusCode"]` |
| Envelope is `{success, message, messages[], errorCode, data}`. `statusCode` and `originalResponseData` are **client-injected** — do not emit them. | `ResponseSchema.cs`, `HttpHelper.cs:86`, `SocialRequest.cs:98` |
| Collections nest at **`data.items`** — except two endpoints returning a **bare array at `data`** | `SocialServerImpl.cs:55` vs `CrewServerImpl.cs:60` |
| Base URL joins as `{base}/{endpoint}` — **no trailing slash** | `SocialRequest.cs:69` |
| Content type exactly `application/json`, UTF-8, unindented | `HttpHelper.AddJsonPayload` |
| **A failed GET is cached as a rejection for the whole session.** `CachedData<T>` attaches `Then` but never `Catch`, and has no TTL. Only `ResetCache()` escapes, fired on every sheet open. | `ResponseCache.cs`, `CachedData.cs:17-31` |

### Auth

`SocialRequest.DecorateRequest` (`:79-89`) adds:

- `Security: <CharacterClientAuthToken>` — omitted entirely when null, never sent empty
- `CharacterUid: <SelectedCharacterUid>`

`CharacterClientAuthToken` comes from the `token` field of `/authorizeCharacter`
and has exactly one consumer in the whole decompile: `SocialHelper.WebToken`.

Casing differs between endpoints and matters: social sends `CharacterUid`,
`/authorizeCharacter` sends `characterUid`.

### Region

`SocialHelper.Region` = `BossaNetBootstrap.CharacterRegion` =
`CurrentCreationData.serverIdentifier`, set once at character select
(`LobbySystem.cs:135`). Against this server that is `community_server`. It is a
path segment to **accept**, not a filter to apply.

`AlliancesServerUrl` is read with `GetOrDefault<string>`, **not** `Get`
(`SocialHelper.cs:12`), and is a `static readonly` resolved by the type
initializer. Anything redirecting it must patch the right accessor.

## The error vocabulary is closed

`ParseErrorCode` looks `errorCode` up in the client's shipped
`ServerErrorCodesTable`; an unknown code renders to the player as the literal
`"Unknown error code: X"`. The complete list
(`GameDBClient/ServerErrorCodesSchema.cs:9-45`):

`alliance_at_capacity`, `already_a_member`, `already_in_alliance`, `auth_failed`,
`crew_at_capacity`, `duplicate_alliance_name`, `dynamo_read`,
`empty_update_payload`, `existing_invite`, `invalid_entity_id`,
`invalid_entity_pair`, `invalid_name`, `invite_limit_met`, `invite_not_found`,
`json_deserialization`, `no_auth_token`, `no_ranks_found_in_alliance`,
`self_invite`, `uneditable_rank`

## Endpoints

### Social (`SocialServerImpl.cs`)

| Method | Path | `data` |
| --- | --- | --- |
| GET | `memberships/character/{uid}` | `PlayerMembershipModel` |
| GET | `screenname/find/{term}` | not enveloped — see below |
| GET | `memberships/invites/character/{uid}` | `data.items[]` |
| POST | `memberships/invite` | `MembershipChangeRequestDataModel` |
| PUT | `memberships/invite/accept/{inviteUid}/{charUid}/{region}` | ignored |
| PUT | `memberships/invite/reject/{inviteUid}/{charUid}` | ignored — **no region segment** |
| PUT | `memberships/invite/cancel/{inviteUid}/{charUid}/{region}` | ignored |

POST body: `{targetId, character, targetType, inviter?, message?, region}`.
`character`/`inviter` are **uid strings in requests** and `{uid,name}` **objects
in responses**.

### Crew (`CrewServerImpl.cs`)

| Method | Path | `data` |
| --- | --- | --- |
| POST | `crews` | `CrewDataModel` |
| DELETE | `crew/{region}/{crewUid}` | ignored |
| GET | `crew/{region}/{crewUid}` | `CrewDataModel` |
| GET | `memberships/crew/{crewUid}` | **bare array** (`:60`) |
| GET | `memberships/invites/crew/{crewUid}` | `data.items[]` (`:76`) |
| DELETE | `memberships/crew/{crewUid}/{charUid}` | **`data` required** (`:89`), unlike its alliance twin |

`POST crews` body: `{name: <my uid>, description: "Crew of <my uid>",
leaderCharacterUid: <my uid>, region}` — the client really does put the uid in
`name`.

### Alliance (`AllianceServerImpl.cs`, 17 endpoints)

`GET alliance/find/{region}/{uid}`, `GET alliance/{region}/{uid}`,
`POST alliance`, `GET alliances/{region}` (items),
`GET alliance/search/{region}?term=` (bare array of uid strings),
`POST alliance/{region}/batch` body `{"batch":[ids]}` (bare array),
`GET memberships/alliance/{uid}` (items), `POST memberships/join`,
`DELETE alliance/{region}/{uid}`, `PATCH alliance/{region}/{uid}`, `POST rank`,
`PUT rank/{uid}`, `DELETE rank/{uid}` (**`dataFieldExpected:true` —
inconsistent with the other DELETEs**), `GET ranks/{uid}` (bare array),
`GET memberships/invites/alliance/{uid}` (items),
`PATCH memberships/character/{charUid}/{allianceUid}`,
`DELETE memberships/alliance/{allianceUid}/{charUid}`.

## What the sheet does on open

A fresh open lands on the **Crew** tab (`SocialScreenUIState.OnEnterState` →
`SetAsCrew()`). `ProtectedInit` fires both cache invalidations then
`CheckAllianceState()` → `GetYourBasicAllianceInfo()`. Module constructors and
`PrepareForContext` fetch nothing; only `OnSelected()` does.

`GetYourBasicAllianceInfo` (`AllianceClient.cs:124-135`) is a two-step chain with
**no `.Catch` of its own**:

1. `GET memberships/character/{me}`
2. **only if** `data.alliance != null`: `GET alliance/find/{region}/{me}`

So the "no alliance" signal lives entirely in step 1's body. A 404 is fatal.
`data: null` is fatal (`dataFieldExpected:true`). `success:false` is fatal. The
**correct** answer is:

```json
200 {"success":true,"data":{"character":"…","member":{…}}}
```

with **no `alliance` key**.

## DTO field names

There is not one `[JsonProperty]` anywhere, so these *are* the wire names.

```
NameServerDataModel          uid, name
PlayerMembershipModel        character, member, alliance, crew
CrewDataModel                uid, region, name, description, leaderCharacterUid,
                             leaderCharacter, created, lastUpdated
CrewMembershipDataModel      memberId, targetId, lastUpdated, created, member
AllianceMembershipDataModel  memberId, targetId, rankId, lastUpdated, created,
                             member, officerNote, privateOfficerNote
AllianceDataModel            uid, region, name, description, messageOfTheDay,
                             leaderCharacterUid, leaderCharacter, created,
                             lastUpdated, emblemUrl, memberCount
MembershipChangeRequestDataModel  id, targetId, targetName, character,
                             targetType, inviter, message, status, created,
                             lastUpdated
RankDataModel                target, uid, name, editable, rankType,
                             membershipType, permissions[]
CharacterDataModel           name, displayName, bossaId, characterSlot,
                             lastUpdated, characterUid, validated
```

Closed enums that **throw** on an unknown value:
`targetType` ∈ {`alliance_member`, `crew_member`};
`status` ∈ {`new`, `rejected`, `accepted`, `cancelled`}.

`inviter == null` is the **structural discriminator** between an Application and
an Invite (`CheckMembershipRequestType`) — not a field.

`IsLeader` is derived: `CrewMember`'s 7-arg constructor does
`IsLeader = characterId == leaderUid`, an ordinal string compare across **three
separate responses**, so every uid must be byte-identical to what the character
list sent.

## What could NOT be recovered

Stated rather than guessed.

- **`screenname/find` URL encoding.** The client escapes the *whole* endpoint
  including separators (`Uri.EscapeDataString($"screenname/find/{term}")`,
  `SocialServerImpl.cs:42`). Whether `%2F` survives Unity's Mono `System.Uri`
  cannot be read statically. **The server accepts both forms**, with a test.
- **`screenname/find` response envelope.** `CharacterSearchResponseModel`
  extends `ResponseSchema`, so `screenname` is a **sibling** of `success`, not
  under `data`; failures use `desc` verbatim, not `errorCode`. Only `success`,
  `screenname.characterUid` and `desc` are consumed — the meaning of `status`
  and `error` is unknown.
- **HTTP status codes for failures.** Non-200 bodies are discarded, so the
  original service's 4xx choices left no trace.
- **Timestamp unit.** DTOs type `created`/`lastUpdated` as `long`; no surviving
  consumer displays them. Epoch milliseconds was CHOSEN and is marked INFERRED
  in the code.
- **Pagination.** `data.items` implies a wrapper with siblings, but the client
  reads only `items` and never sends a page parameter. `AllianceMembersSlice` is
  built with empty strings and `0`, discarding whatever else the wrapper carried.
- **`POST alliance` / `PATCH alliance` full payload**, and `emblemUrl` semantics.

## A retail client bug, recorded so nobody "fixes" it into a mismatch

The client **writes** `edit_message_of_the_day` but **reads** the MOTD
permission off `leader_chat`. Reproduce the bug, not the intent.

## Implementation status

`WorldsAdriftServer/Social/` implements all 7 social and all 6 crew endpoints,
reusing `CrewPolicy`/`CrewLedger` from the engine-free multiplayer project rather
than reimplementing crew rules. `SocialIdentityPolicy` resolves the account from
`Security` and then requires the claimed `CharacterUid` to belong to that
account, so a valid uid belonging to someone else is refused.

Alliances: `alliances/{region}` and `alliance/search` answer an **honestly empty
list** — we host none, which is true rather than a stand-in. Every other alliance
endpoint is refused in band with `dynamo_read` and logged. Nothing is faked.

Schema **v7** adds `social_invites` with both closed vocabularies as CHECKs, a
no-self-invite CHECK, and a partial unique index on
`(character_uid, target_id) WHERE status='new'` so a race cannot produce two live
invites while a rejection still permits a later one.

## The default that broke every install

Shipping the API was not enough: the first build that carried it broke the Social
Sheet for **every** player, and the cause was one line of config.

`REST_AlliancesUrl` was introduced as its own key — correctly, because retail
really did split `ConfigKeys.AlliancesUrl` from `ConfigKeys.RestServerUrl`, and an
operator who splits them should not have to patch code. Its comment said the
default was "the same origin because ours does not split them". The default was
in fact the literal `http://127.0.0.1:8080`, a copy of the *development* REST
default, not a reference to REST_ServerUrl. A copy cannot track what it copies.

That is only a latent bug on a fresh install, but this was a **new key**, and
BepInEx materialises a new key into every **existing** config file using the
shipped default. So every player who had long since pointed `REST_ServerUrl` at
production silently got a localhost social host written into their config on
update. Both tabs fetch through it, the failure lands in the shared
`SocialCharacterSheet.TriggerAllianceExceptionHandler`, and the whole sheet dies
with *"An error occurred — Can't retrieve alliance or crew data"* — including the
CREW tab, which renders and offers CREATE but never loads.

The symptom is indistinguishable from a broken server. The tell that it is not:
the production login server's journal shows **zero** social traffic. The requests
never left the player's machine.

### The rule now

"Same origin" is expressed as a **sentinel, not a copy**:

| `REST_AlliancesUrl` | meaning |
| --- | --- |
| blank (**the shipped default**) | same origin as `REST_ServerUrl`, re-resolved every read, so it follows if that moves |
| anything else | an explicit operator override, used verbatim — the retail split, still available |

Resolution lives in `RestUrlPolicy` (engine-free, in the multiplayer project,
linked into the net35 mod and unit tested), which also enforces the
no-trailing-slash rule on the *resolved* value rather than trusting the operator
to — the client joins with `"/" + endpoint` (`SocialRequest.cs:69`).

`REST_ServerDeploymentUrl` had the same shape (a hardcoded localhost copy of the
REST origin with a path glued on) and got the same treatment. It never hurt
anyone only because it is an **old** key that installs already had set. That is
the whole lesson: a derived URL with a literal default is a landmine that goes
off the day it becomes a new key.

### Healing the installs that already took it

Fixing the default does nothing for a config that already has the bad value —
BepInEx never rewrites an existing key. A one-time migration
(`alliances-url-follows-rest`, recorded in `Internal_AppliedMigrations`) resets it
to blank, but only when all three hold:

1. the stored value is the shipped legacy literal **exactly**, not merely some
   loopback URL — a developer running a local social server points at the port it
   actually listens on, not the stale `8080` in that literal;
2. `REST_ServerUrl` is a real **remote** host — a local dev has REST on loopback
   too and is left completely alone. "Production REST + loopback social" is not a
   deployment anyone runs, because one server answers both;
3. it has never run before, so a developer who deliberately re-enters that exact
   literal afterwards keeps it.

A deliberate `http://127.0.0.1:8080` is byte-identical to the accident, so these
cannot be told apart with certainty. The conservative option was taken and the
residual risk is stated rather than hidden: a developer whose first launch after
the fix has production REST plus a hand-typed `http://127.0.0.1:8080` loses that
one setting, once, with a warning logged.

The patcher deliberately does **not** force `REST_AlliancesUrl`
(`WarebornConnectionConfig`). Forcing a literal there would re-create the same
hardcoded duplicate one layer down, and would clobber an operator's split on every
patch run. A test asserts the key stays absent.

## Known gap

**HTTP-driven crew changes do not reach the game server's in-memory ledger
mid-session.** `CrewService` loads crews at boot and holds them; the login server
reads Postgres fresh per request. So game server → login server propagates
immediately, but login server → game server is stale until restart. That affects
the crew chat channel and the beacon, not the retail panel (which is HTTP).

The clean fix is making `CrewService` re-read from Postgres rather than trusting
its boot snapshot. It touches the path every Multiplayer test covers and deserves
its own change.
