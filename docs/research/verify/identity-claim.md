# VERIFY — "identity is already solved" (round 2, adversarial)

**VERDICT: PARTIALLY REFUTED.**

The four-link wire chain is statically sound — the agent could not break it. But the
claim's two load-bearing words, **"solved"** and **"TODAY"**, are false.

## The chain HOLDS (all four links)
- **Link 1** `CharacterSelectionScreen_Patch.cs:23-27` — builds a fresh one-element list,
  so `ToArray()[0]` downstream is deterministically the selected character.
- **Link 2** `CharacterCustomisationVisualizer_Patch.cs:54-58` — holds, but see the
  config gate below. **Why it fires at all** (never explained before): `_properties` is
  typed as the legacy `PlayerPropertiesStateReader`, so `+=` binds the
  explicit-interface event whose `add` accessor invokes the handler **immediately**
  (`gencode/.../PlayerPropertiesState.cs:255-268` — `value(Data.customisation);`).
  The publish rides the *subscription*, not a server update.
- **Link 3** publish at `:81-83` — **1088's absence from `authoritativeComponents` is
  NOT a blocker.** Nothing on the send path checks authority:
  `PlayerPropertiesState.cs:335-338` → `sdk-decomp:487-497` →
  `Connection.cpp:166-197`, all unconditional.
- **Link 4** `PlayerPropertiesState_Handler.cs:45-51` — relay is unconditional
  (`WorldsAdriftRebornGameServer.cs:704-712`, every id, no filter), and registration
  order is safe: `Mirror.OnJoin` runs at `:539`, before the client requests interest.

## WHAT REFUTES "SOLVED" AND "TODAY"

### 1. It has never run. Not once.
- `findings-persistence.md:114-116` says so itself — *"not observed"*. Its headline at
  `:6-9` contradicts its own limitations section.
- Implementing commit `b4d15da`: *"Verified against **decompiled sources**."*
- Hardening commit `70c9df6`: *"**Not deployed on its own** - rides the next test round."*
- No later commit mentions an appearance test.

### 2. Behind an UNPINNED config flag — and our own comments disagree about its polarity
`CharacterCustomisationVisualizer.cs:332-344`: the `OnCustomisationUpdated` subscription
only happens when `WAConfig.Get<bool>(ConfigKeys.UseBossaNet)` is **true**. If false,
the Harmony prefix never runs and nothing is ever published.
The mod does **not** pin it — `WAConfig_Patch.cs:68-80` intercepts only `"VOIP.Enabled"`
and logs `not touching BossaNet.UseBossaNet`. Default is `true`
(`acs/ConfigDefaults.cs:30`) and the REST roster flow is gated on it, so it is almost
certainly true — but `ConfigurationManagement.ConfigManager` is **not in the decompile
set**, so it is unproven statically.
**Our own comment at `CharacterSelectionScreen_Patch.cs:21` asserts the OPPOSITE
polarity** ("the game only tries to read data from here if UseBossaNet is *false*") —
the two halves of the chain were written against different assumptions.

### 3. THE UID IDENTIFIES A CHARACTER, NOT A PLAYER — AND CAN COLLIDE
Commit `3a1860c` made uids real GUIDs, so the key is *stable*. But the same commit
records that one roster serves the whole deployment, because the client hardcodes
`steam/1234` and Steam auth is stubbed. **Two clients can therefore select the SAME
character and publish the SAME `characterUid` on two different entity ids.**
`Appearances` will hold both. Persistence keyed on that uid gets **two concurrent
writers for one profile — the exact corruption mode persistence exists to prevent.**
`findings-persistence.md:106-107` lists this under RISKS while `:6` calls identity
"solved". Both cannot be true.

### 4. "Zero new work" is false — three concrete gaps
- **(a) Ordering makes seed-time restore impossible.** The ordering claim is correct
  (identity lands strictly after 1081) but "harmless" hides a build.
  `ComponentsSerializer.cs:85-92` seeds 1081 at interest-request time, before the client
  can instantiate the publisher. A restore needs a **second** 1081 push after identity
  arrives, and **no such path exists** — the only 1081 push is
  `InventoryModificationState_Handler.cs:51,71`, reachable only in reaction to an
  inbound 1082 event.
- **(b) Identity is DESTROYED on disconnect.** `WorldsAdriftRebornGameServer.cs:67-70`
  calls `Appearances.Forget(ownEntity.Value)`. **Any save-on-disconnect hook must be
  inserted ABOVE line 69 or it reads null.**
- **(c) Nothing parses it.** `Appearances.Get()` returns
  `IReadOnlyDictionary<string,string>` whose value is `JObject.ToString()` —
  Newtonsoft's default `Formatting.Indented`, a multi-line blob. And the game-server
  csproj has **no Newtonsoft reference**, so parsing it there needs a new dependency.

### 5. The publish is one-shot with a re-arm on an unmentioned path
`appearancePublished` is static, reset only by `ResetAppearancePublished()` from
`CharacterSelectionScreen_Patch.cs:32`. Any world re-entry bypassing `EnterWorld` leaves
a new entity with **no identity, permanently, for the life of the process**.

## Corrections to the original findings
- **The `[0]` outside the try/catch is worse than "silent".**
  `CharacterCustomisationVisualizer_Patch.cs:56` sits above the `try` at `:71`.
  `CharacterDataLoader.Load()` returns an empty list on an empty/corrupt pref, and the
  `IndexOutOfRangeException` escapes into the SDK `add` accessor — called from
  `OnEnable` **after** `ComponentUpdated.Add(...)` already ran. So `:342-343` (the
  `_inventory` subscriptions) never bind and **the rig gets no cosmetics at all.**
- **The "fails silently on a bad cast" claim is wrong on both counts.** It is not silent
  (`:77` logs a warning and returns *without* setting `appearancePublished`, so it can
  retry) and the cast succeeds (`EntityVisualizers.cs:354` injects the Impl from
  `gencode/.../PlayerPropertiesState.cs:669-678`; `Impl` declares `Writer` directly).
  The genuinely silent path is `PublishOwnAppearance` early-returning at `:66-69` with
  **no log at all**, compounded by BepInEx's `WriteUnityLog = false`.
- **The second boot path is `DebugLobbyState`**, which never calls `EnterWorld` — it
  does `SelectCharacter(0); Login(); AuthenticateCurrentCharacterChoice();`. Its
  editor gate is dead in a shipped client, but the second disjunct at
  `ConnectToNeededServersState.cs:104` — `IsSpectatorMode()` — is **not editor-gated**.
  Not live today (`SpectateMode` defaults false) but it is a non-editor door into a
  boot path that breaks link 1 and then crashes link 2.
- **Side effect nobody documented:** `obj` is the Impl's **live** `Data.customisation`
  map (passed by reference), so `obj.Add(...)` at `:57` mutates the component's stored
  data in place.
- **`Appearances` is global, keyed by entityId** (`AppearanceStore.cs:14`), not per-peer
  — but cross-peer mis-attribution of the *record* is impossible: ids come from a
  monotonic counter and the handler validates ownership. The collision is in the uid
  *inside* the record, not the record itself.

## THE PROBE — Pass 0 needs ZERO code changes
The server already prints enough. Start it with stdout captured, join with one client:

| log line | file:line | meaning |
|---|---|---|
| `trying to handle a ComponentUpdateOp for 1088` | `ComponentUpdateManager.cs:123` | client published; links 1–3 fired |
| `recorded appearance for entity N (5 keys)` | `PlayerPropertiesState_Handler.cs:52` | **PASS** — seed is 4 keys, so 5 = 4 + identity |
| `… (4 keys)` | same | **FAIL** — published, identity key absent |
| `1088 update for entity X from a peer that owns Y` | `:41` | ownership gate rejected it |
| `could not match requested ComponentUpdate` | `ComponentUpdateManager.cs:181` | `ComponentMap[peer][entity][1088]` missing |

```bash
grep -nE "ComponentUpdateOp for 1088|recorded appearance|1088 update for entity|could not match requested" server.log
```

**Two-client collision test:** join twice, select the *same* character in both, look for
two probe lines with different `entity=` but an **identical `characterUid`**. If you see
that, uid-keyed persistence is unsafe as designed regardless of the wire chain.

Pass 1 (one probe line in the handler) and Pass 2 (client-side, needs
`WriteUnityLog = true`) are in the agent transcript; Pass 0 discriminates most cases.
