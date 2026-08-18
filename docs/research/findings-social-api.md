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

All PROVED from `acs/Bossa.Travellers.Alliances/AllianceServerImpl.cs` unless
noted. `data` column is what the client's parser demands.

| # | Method | Path | Body | `data` |
| --- | --- | --- | --- | --- |
| 1 | GET | `alliance/find/{region}/{characterUid}` | — | `AllianceDataModel` |
| 2 | GET | `alliance/{region}/{allianceUid}` | — | `AllianceDataModel` |
| 3 | POST | `alliance` | create | `AllianceDataModel` |
| 4 | GET | `alliances/{region}` | — | `data.items[]`, null-checked |
| 5 | GET | `alliance/search/{region}?term=` | — | **bare array of uid STRINGS** |
| 6 | POST | `alliance/{region}/batch` | `{"batch":[ids]}` | **bare array** |
| 7 | GET | `memberships/alliance/{allianceUid}` | — | `data.items[]`, null-checked |
| 8 | POST | `memberships/join` | apply | `MembershipChangeRequestDataModel` |
| 9 | DELETE | `alliance/{region}/{allianceUid}` | — | ignored (`dataFieldExpected:false`) |
| 10 | PATCH | `alliance/{region}/{allianceUid}` | update | `AllianceDataModel` |
| 11 | POST | `rank` | rank | `RankDataModel` |
| 12 | PUT | `rank/{rankUid}` | rank | `RankDataModel` |
| 13 | DELETE | `rank/{rankUid}` | — | **required** — `dataFieldExpected` left at its `true` default (`:132`), unlike 9 and 17 |
| 14 | GET | `ranks/{allianceUid}` | — | **bare array** |
| 15 | GET | `memberships/invites/alliance/{allianceUid}` | — | `data.items[]`, **NOT null-checked** (`:158`) |
| 16 | PATCH | `memberships/character/{characterUid}/{allianceUid}` | one of three | `AllianceMembershipDataModel` |
| 17 | DELETE | `memberships/alliance/{allianceUid}/{characterUid}` | — | ignored (`dataFieldExpected:false`) |

**Alliance and rank uids must be real GUIDs.** `SanitizeGuid`/`ValidateGuid`
(`SocialHelper.cs:30-59`) run over them on 1, 2, 7, 9, 12, 13, 14, 15 and 17;
they require a hyphen and then construct a `System.Guid`. A crew-style
`alliance:{guid}` throws a `FormatException` **inside the client**, so the
request never leaves the machine and nothing appears in our log. Crews are
exempt only because no crew id is ever sanitised.

Three path shapes are ambiguous and matching the wrong one swaps two ids
silently rather than failing:

- `alliance/find/{region}/{uid}` takes a **character**; `alliance/{region}/{uid}`
  takes an **alliance**;
- `memberships/character/{characterUid}/{allianceUid}` is character-first while
  `memberships/alliance/{allianceUid}/{characterUid}` is group-first — eight
  lines apart in the same file (`:169` and `:177`);
- ranks are a top-level `rank`/`ranks` resource with no region and no alliance in
  the path — singular is the collection you POST to and plural is the one you
  list, the exact inverse of `crew`/`crews` in the same service.

#### Request bodies

```
POST alliance      {leaderCharacterUid, name, description?, messageOfTheDay?, region}
PATCH alliance     {messageOfTheDay, description}          <- ONLY these two
POST memberships/join
POST memberships/invite
                   {targetId, character, targetType:"alliance_member",
                    message?, inviter?, region}
POST rank / PUT rank
                   {target, name, editable, rankType:"member",
                    membershipType:"alliance_member", permissions:[...]}
PATCH memberships/character/{c}/{a}
                   {"rankUid":...} | {"publicOfficerNote":...} | {"privateOfficerNote":...}
```

`description` and `messageOfTheDay` are **omitted entirely**, not sent empty,
when the player left the box blank (`CreateAllianceFillOptionalFields`).
`inviter` is likewise omitted for an application — that absence is the client's
structural discriminator, not a convention.

`POST rank` always claims `editable:true`, `rankType:"member"` and
`membershipType:"alliance_member"`; the client hardcodes all three
(`SocialGroupParsers.cs:198-199`).

#### Permission vocabulary

Five the client WRITES (`ServerRankPermissionsFromAllianceRank`, `:225-249`) plus
two it only READS (`:131-132`):

`edit_group`, `edit_message_of_the_day`, `leader_chat`, `edit_ranks`,
`edit_members`, `edit_officer_note`, `read_officer_note`

The list is CLOSED in the weaker sense that an unknown string is not an error —
`permissions.Contains(...)` simply answers false — so an invented permission
produces a button nobody can ever see rather than a warning.

Two derivations are load-bearing:

```csharp
// SocialGroupParsers.cs:126-127, 134
isDefaultLeaderRank = rankType == "leader" && !editable;
isDefaultMemberRank = rankType == "member" && !editable;
editMembers         = permissions.Contains("edit_members") || isDefaultLeaderRank;
```

`AllianceRankInformation.CreateLookup` fills its `Leader` and `BasicMember`
fields from those two booleans and callers dereference them, so an alliance
missing either default rank has a null where the panel expects a rank.

#### Field-name asymmetries, all retail's

| written as | read back as | mapped onto |
| --- | --- | --- |
| `rankUid` | `rankId` | `AllianceMember.RankId` |
| `publicOfficerNote` | `officerNote` | `AllianceMember.PublicNote` |
| `privateOfficerNote` | `privateOfficerNote` | `AllianceMember.OfficerNote` |

#### What could NOT be recovered, and was chosen instead

- **`MAX_MEMBERS`, `MAX_APPS`, `MAX_INVITES`.** `SocialConstantsSchema` proves
  they existed under the key `ALLIANCE`, but the row data lived in Bossa's remote
  GameDB and no value survives in the shipped install. `AlliancePolicy` uses 100
  and 50, both labelled WAREBORN TUNING. Unlike crews these are NOT rendering
  limits: `AllianceMembersList.CreateListObjects` instantiates one widget per
  member through `UIObjectFactory` behind a `ScrollPaginator`, so there is no
  fixed widget budget to overrun and no `CrewRosterLimits`-style clamp is needed.
- **`ALLIANCE_NAME` min/max length.** Same GameDB provenance. The CHARACTER rules
  in `StringFormatHelper.CheckRules` (`:138-176`) *are* recoverable and are
  reproduced exactly in `AllianceNamePolicy`; the length bounds are 1..64,
  deliberately looser than any plausible retail value so the server never refuses
  a name the client told the player was fine.
- **Name-uniqueness comparison.** `duplicate_alliance_name` proves uniqueness was
  a rule; nothing says how it compared. Case-insensitive invariant was CHOSEN,
  because an alliance list shows the name and nothing else.
- **Who inherits a leaving founder's alliance.** Nothing in the client decides
  it. Seniority (join order) was chosen, matching crews; rank could not be used
  because no ordering over ranks is defined anywhere.

## The alliance crest: there is no picker

**PROVED, and it is the whole answer to "I could fill in every field but could
not change the logo".** The retail client cannot change an alliance crest,
because nothing in it ever sends one.

`YourAllianceCreateAlliancePanel` has exactly three `TMP_InputField`s — name,
description, message of the day — and `OnCreateAllianceButtonPressed` calls
`CreateAlliance(name, description, motd)`. There is no fourth control. Across the
whole decompile there is no emblem picker, no uploader, no colour/pattern
composer and no crest endpoint; grepping `emblem|crest|insignia|banner|heraldr`
finds only the read path below and one unrelated lore string.

The entire feature is one field:

```csharp
// AllianceDataModel.cs:26
public string emblemUrl;

// AllianceClient.cs:79-93
if (string.IsNullOrEmpty(allianceInfo.EmblemWebLink)) return Promise<Sprite>.Resolved(null);
return SpriteDownloader.GetSpriteFromUrl(allianceInfo.EmblemWebLink).Then(...);
```

`SpriteDownloader` does a plain GET on that URL with **no `Security` or
`CharacterUid` header** — it bypasses `SocialRequest.DecorateRequest` entirely —
decodes the bytes with `response.DataAsTexture2D`, and **resolves null on any
exception** rather than rejecting. Every consumer
(`SocialInfoPanelAllianceInfo`, `YourAllianceTitleSegment`, `AllianceInfoBar`)
falls back to a local placeholder sprite. Neither `POST alliance` nor
`PATCH alliance` carries the field.

So the crest the player saw on the form is a **static placeholder**, and it was
never interactive. Retail set `emblemUrl` out of band. This server stores the
column and serves it back verbatim; it is empty unless an operator fills it in,
which is exactly what retail did for the alliances that had no crest. Inventing
an upload endpoint would have been inventing contract, so none was added.

## The crew-creation spinner is client-side and cannot be fixed from here

The report — *"the spinning icon kinda speeds super fast and jumps to crew
created"* — is not caused by our responses. Two independent client facts explain
it and neither is reachable from a server.

**The spinner advances per FRAME, with no `Time.deltaTime`.**

```csharp
// Travellers.UI.Framework/ForwardSpin.cs
public override void Spin(Image spinImage) {
    spinImage.fillClockwise = true;
    spinImage.fillAmount += 0.02f;          // per Update(), not per second
}
```

`LoadingInputBlocker.Update` calls it every frame, so a full 0→1 sweep is exactly
50 frames: 0.83 s at the 60 FPS it was authored for, 0.25 s at 200. The same
codebase contains a correct time-based version of the identical effect,
`SpinningSprite` (`float num = Time.deltaTime * speed * dir;`), which this
component does not use.

**It is also reset once per round trip.** `LoadingInputBlocker.Activate` sets
`fillAmount = 0.3333f` and restarts the phase, and `Activate` runs on every
off→on transition of the busy overlay. Busy is a boolean edge from
`SocialRequestMonitor`, raised when the in-flight dictionary becomes non-empty
and dropped when it empties — and the post-create calls are strictly sequential
(`POST crews`, then `GET memberships/character`, `GET crew/{region}/{uid}`,
`GET memberships/crew/{uid}`, `GET memberships/invites/crew/{uid}`, each
`.Then`-chained on the last), so the dictionary empties between every one. Four
resets, then `YouAsLeaderState.EnterScreen` flips the panel with bare
`SetActive` calls and no transition — the "jumps to crew created".

Note the count is a boolean edge, not a rate: `SocialRequestMonitor` only ever
compares its dictionary count to `1` and `0`, and the number never reaches the
spinner. **Server timing and response shape can change only how LONG the spinner
is visible and how many times it is snapped back — never how fast it sweeps.**
Answering more slowly would make it spin fast for longer, which is worse.

The fix therefore had to be a client one, and it is:
`WorldsAdriftReborn/Patching/Social/LoadingSpinner_Patch.cs` replaces both
`Spin` bodies with `0.02f * 60f * Time.deltaTime` — the authored speed of 0.02
per frame at 60 FPS, restated per second rather than re-tuned — capped at one
frame's worth of 0.05 so a hitch cannot make the wheel teleport, and skips the
`fillAmount` rewind in `Activate`.

`Activate`'s OTHER line cannot simply be dropped with it. It is also the only
thing guaranteeing `_spinPhase` is non-null before `Update` reads it, so the
patch still ensures the phase and removes only the part the player sees; dropping
both would turn a cosmetic bug into an NRE every frame on any blocker enabled
before its init ran.

The panel swap itself is left alone. `YouAsLeaderState.EnterScreen` is bare
`SetActive` calls with no transition, but so is every state in that screen, and
animating one of them would be a change to the game's look rather than a repair.

**This is a client-mod change and the patcher has not been updated for it** — a
player on the current patch will not get it until the mod ships.

One latent client bug found while looking, recorded rather than acted on:
`ResponseCache` is applied to the **POST `/crews`** too
(`CrewServerImpl.cs:27`), keyed on `(uri, method, rawData)`, so an identical
repeat create inside a warm-cache window is served from cache instead of being
re-sent.

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
- ~~**`POST alliance` / `PATCH alliance` full payload**, and `emblemUrl`
  semantics.~~ Both recovered — see the alliance endpoint table and the crest
  section below.

## A retail client bug, recorded so nobody "fixes" it into a mismatch

The client **writes** `edit_message_of_the_day` but **reads** the MOTD
permission off `leader_chat`. Reproduce the bug, not the intent.

## Implementation status

`WorldsAdriftServer/Social/` implements all 7 social and all 6 crew endpoints,
reusing `CrewPolicy`/`CrewLedger` from the engine-free multiplayer project rather
than reimplementing crew rules. `SocialIdentityPolicy` resolves the account from
`Security` and then requires the claimed `CharacterUid` to belong to that
account, so a valid uid belonging to someone else is refused.

Alliances are implemented in full: all 17 endpoints, in
`WorldsAdriftServer/Social/AllianceEndpoints.cs` and `AllianceWire.cs`, over
`AlliancePolicy`/`AllianceLedger`/`AlliancePermissions`/`AllianceNamePolicy` in
the engine-free multiplayer project. `alliances/{region}` and `alliance/search`
no longer answer an honestly empty list, because the list is no longer empty.

The alliance endpoints take PORTS (`IAllianceStore`, `ISocialInviteStore`, a name
lookup) rather than the concrete repositories, so
`WorldsAdriftServer.Tests/AllianceEndpointsTests.cs` drives the whole contract
through the real `SocialRoute` parser as plain `[Fact]`s with no database. That
is deliberate and is the correction to the gap that let two crew defects ship:
`SocialServiceTests` take the concrete repositories, so they are `[PostgresFact]`
and skipped on most machines.

Schema **v7** adds `social_invites` with both closed vocabularies as CHECKs, a
no-self-invite CHECK, and a partial unique index on
`(character_uid, target_id) WHERE status='new'` so a race cannot produce two live
invites while a rejection still permits a later one.

Schema **v8** is purely additive and adds `alliances`, `alliance_ranks` and
`alliance_members`. `alliance_id` and `rank_id` are UUID columns, because the
client parses them. Three constraints encode things the client cannot survive
being wrong: one alliance per name folded to lower case
(`alliances_one_name`), one alliance per character (`alliance_members`' primary
key), and exactly one default rank of each kind per alliance
(`alliance_ranks_one_default_leader` / `_member`).

`alliance_members.rank_id` is deliberately NOT a foreign key. Both it and
`alliance_ranks` cascade from `alliances`, Postgres does not order sibling
cascades, and a RESTRICT would make disbanding fail whenever the ranks were
deleted first while a CASCADE would turn a rank deletion into a mass boot. The
invariant is kept in two agreeing places instead: `AllianceLedger.RemoveRank`
moves holders to the default member rank, and the wire layer resolves an
unresolvable rank id to that rank before emitting it. The second is not belt and
braces — `AllianceClient.TryGetRank` THROWS on a rank id absent from
`ranks/{allianceUid}`, and the throw lands in
`TriggerAllianceExceptionHandler`, so one stale row destroys the whole sheet,
crew tab included.

### The accept endpoint has two different actors

`PUT memberships/invite/accept/{inviteUid}/{characterUid}/{region}` carries the
SUBJECT of the row, which is **not** always the caller:

- answering an INVITE, the invitee accepts their own offer and the two coincide
  (`PlayerAcceptAllianceInvitation` passes `SocialHelper.MyCharacterUid`);
- answering an APPLICATION, an officer accepts somebody else's request and the
  client passes the APPLICANT's uid
  (`AllianceAcceptPlayerApplication` passes `applicationInfo.CharacterUiD`).

`SocialService.ResolveInvite` used to require subject == caller. That reads as a
sound identity check and is in fact a refusal of every application ever accepted;
it was invisible because applications had no store. Which of the two a row is
comes from `inviter` being null, not from the URL, which is identical either way.
The group side is now asked as a PERMISSION question (`edit_members`), because
`YourAllianceManagementButtons.SetForPermissions` shows the APPLICATIONS tab on
exactly that permission — a server that only let the founder admit would render a
button that always failed.

### Alliance ids are collision-proof against the ledger, not a counter

`AllianceEndpoints` mints a fresh GUID and re-rolls if the hydrated ledger
already holds it. The loop is a formality for a GUID; it is the SHAPE the crew
side had to be corrected into after a bare counter restarting at 1 every boot
handed a new crew the id of a RESTORED one and the `ON CONFLICT` write gave
somebody else's crew away. The check is against the ledger — which is what a
create would actually collide with, restored rows included — rather than against
a high-water mark this process happens to remember.

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

## The sheet must not be destructible by data

Two more defects sat behind the URL bug. Neither stopped the sheet loading, so
both survived the first fix; one of them destroys the sheet outright once a crew
grows.

### Outstanding invites shared the members' widgets, and nothing counted them

`CrewClient.GetCrewMembers` builds ONE list: the crew's members, then every
invite whose `status` is `"new"`, appended to the same list. `CrewScreen`
pre-builds exactly `MaxCrewSlots` = **5** non-leader widgets and draws the leader
into its own, so the list may hold at most **six** entries. The sixth non-leader
entry indexes past the end, and the throw lands in
`SocialCharacterSheet.TriggerAllianceExceptionHandler` — shared with alliances.
One over-invited crew therefore kills the WHOLE sheet, both tabs, with the same
*"Can't retrieve alliance or crew data"* the localhost URL produced.

`CrewPolicy.MayInvite` counted seated members and stopped there, and `Hydrate()`
built the ledger from `crews` + `crew_members` without ever loading invites — so
nothing anywhere could count a crew's outstanding offers. `invite_limit_met` was
defined in `SocialErrorCodes` and never once referenced. A leader alone in a
four-seat crew could send offers without limit.

**Retail's own number is not recoverable, and was not invented.** The client's
GameDB schema does carry `SocialConstants.MAX_INVITES` — proof a limit existed —
but it has **zero consumers** anywhere in the decompile, its only defined row key
is `ALLIANCE` rather than crew, and the row data lived in Bossa's GameDB: the
string `MAX_INVITES` appears in the shipped install only inside
`Assembly-CSharp.dll`, as the schema's own field name, with no value anywhere.
There is no crew constants table at all.

What IS recoverable is the client's arithmetic, and that is what
`CrewRosterLimits` encodes:

    live invites <= min(numSlots, 6) - members

Both ceilings are load-bearing. `numSlots - members` is the game rule — never
offer a seat that does not exist. `6` is the client rule, and it is not
redundant: `CrewPolicy.MaxSlots` is 8, so a crew configured above six seats would
pass the game rule and still crash the panel. Refusals return the client's own
`invite_limit_met`, so the player reads the sentence retail wrote.

The emitters clamp as well as the policy. A cap stops a crew growing, but it
cannot heal rows written before it existed, so `CrewMembers` and `CrewInvites`
truncate to what the sheet can draw — invites yielding first, because a truncated
pending list is a nuisance and a member missing from their own crew panel is a
bug report.

### A refusal has to be written in the dialect its reader parses

This client has **two** response checkers and they disagree about where a
failure's text lives. The general one reads `errorCode` and looks it up in the
closed `ServerErrorCodesTable`. The character-search one,
`SocialRequest.CheckSearchResponseModelForErrors` (`:114-124`), ignores
`errorCode` entirely and throws `SocialServerResponseErrorException(model.desc)`.

`SocialHandler` refused route-blind — auth failures, unimplemented routes and the
catch-all around a database fault all emitted `errorCode` only, because they ran
*before* the route was parsed. On the search path that reached the player as a
dialog whose text was the .NET default for a null message: the name of an
exception class.

There were **two** independent triggers, not one:

1. any refusal on `screenname/find/{term}` — auth, store failure, or the catch-all;
2. a **whitespace-only** search term. `CrewScreen` guards the invite field with
   `IsNullOrEmpty` and trims only afterwards (`:308-310`), so a field of spaces
   sends an empty term and the client builds `screenname/find/`. The route
   required three path segments, so that fell through to "unknown route" — which
   also meant `SocialService`'s own `"No name was given to search for."` branch,
   the one place a refusal correctly carried `desc`, **could never be reached**.

The fix is `SocialRefusal`: the refusal is built for the reader, and which routes
read `desc` is enumerated rather than defaulted, so a route added later has to be
a deliberate decision instead of silently inheriting the wrong shape.

### Two latent hazards closed while in there

- **`"data": null` is not null to the client.** `data` is typed `JToken`, and
  Json.NET turns a JSON null into a `JValue` of type Null, not a C# `null` — so
  an explicit null walks straight past the `dataFieldExpected && model.data ==
  null` guard that exists to catch it and NREs deeper in. `SocialEnvelope.Ok` now
  **omits** the key instead; an absent key does deserialize to C# null and trips
  the guard correctly.
- **`message` / `messages` are still not emitted, deliberately.** They are read
  only by `ParseTechnicalError`, which runs only on a non-200 — which we never
  send. Worth knowing that its fallback is `"Unknown error : " +
  originalResponseData`, i.e. the whole response body shown to the player, if a
  success envelope's data shape is ever wrong.

### The test gap that let all of this through

Nothing tested `SocialHandler`. The social tests called `SocialService.Handle`
with the actor already resolved — *behind* the boundary — and were
`[PostgresFact]`, skipped on any machine without a database. The boundary
decision now lives in `SocialGate`: strings and flags in, an envelope or a route
out, no session and no repositories, so `SocialGateTests` and
`SocialRefusalTests` assert it exhaustively as plain facts. Writing them
immediately caught a third bug that had never run — `SocialRoute` is a class, so
the refusal path's `default` route was null and the first `Kind` read on any
refusal would have thrown.

## Known gap

**HTTP-driven crew changes do not reach the game server's in-memory ledger
mid-session.** `CrewService` loads crews at boot and holds them; the login server
reads Postgres fresh per request. So game server → login server propagates
immediately, but login server → game server is stale until restart. That affects
the crew chat channel and the beacon, not the retail panel (which is HTTP).

The clean fix is making `CrewService` re-read from Postgres rather than trusting
its boot snapshot. It touches the path every Multiplayer test covers and deserves
its own change.

**Alliances do not have this problem**, and not by care: nothing in the game
server knows what an alliance is, so there is no second process holding a stale
copy. Every alliance row is written and read by the login server answering HTTP.
If alliance chat is ever wired to SpatialOS, this gap arrives with it.

## What remains unproven without a live client

Everything above was recovered by reading the shipped client and verified against
this server — over HTTP, end to end, on a spare port: create, the whole read
chain, applications, invitations, rank permissions, succession, disband, and
survival across a restart. What that cannot establish:

- **That the retail UI renders it.** The shapes match what the client's parsers
  read, field by field, but no Worlds Adrift client has been pointed at this yet.
  The things that would fail visibly rather than loudly are a rank the panel
  draws with the wrong permissions, and a `created` timestamp displayed as 1970
  if the epoch unit guess (milliseconds, still INFERRED) is wrong.
- **`SocialConstants` limits.** 100 members and 50 live requests are WAREBORN
  TUNING. Retail's numbers are not in the shipped install and never will be.
- **Whether an alliance larger than a page renders.** `ScrollPaginator` and
  `_numberOfItemsPerPage` are set in the Unity prefab, not in code, so the page
  size cannot be read from the decompile. The list is built dynamically per
  member, so there is no fixed-widget crash of the kind crews have — but "does
  paging work at 40 members" is a question only a client can answer.
- **The alliance PubSub push.** `Bossa.Travellers.Alliance.PubSub` shows the
  original service pushed change notifications through a SpatialOS command
  (`CrewOrAllianceNotification`), which made the panel refresh itself when
  somebody else acted. We do not send those, so a player sees another member's
  change on their next refresh rather than immediately. The client's consumer
  dereferences `changeList.*.uid` without null checks per event type, so sending
  a partial one would be worse than sending none.
- **The transport quirk found while testing, which is NOT ours and predates
  this work**: a `PUT` carrying no `Content-Length` at all leaves the connection
  waiting for a body. `PUT` with `Content-Length: 0` answers correctly, and
  BestHTTP sets it, so the retail client is unaffected — but a hand-rolled
  `curl -X PUT` with no `-d` will appear to hang. Worth knowing before it is
  mistaken for an alliance bug.
