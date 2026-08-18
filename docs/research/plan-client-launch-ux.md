# Plan: cut Steam out of the client, and stop the launch path lying

Worlds Adrift was delisted in 2019. The client still treats Steam as a hard
dependency: it will not reach the character screen without a live Steam client,
and on the night this was written a Steam outage stopped the game booting at
all. A preserved game that cannot start because a shop is down is the exact
problem this project exists to remove.

Everything below is ordered so that the thing blocking play lands first.

## What the evidence says

Measured from the player's own logs, `~/Games/WorldsAdrift/BepInEx/LogOutput.log`
and `UnityClient@Windows_Data/output_log.txt`, session 2026-08-18 22:13, and from
the decompile at `~/Games/WAReborn-decompiled/acs`.

### The Steam requirement is one config flag with a long tail

`SteamChecker.IsUsingSteam` is nothing but

```csharp
public static bool IsUsingSteam => WAConfig.Get<bool>(ConfigKeys.UseSteam);
```
(`acs/Bossa.Travellers.Utils/SteamChecker.cs:8`, key `"Bootstrap.UseSteam"`,
`acs/ConfigKeys.cs:48`, defaulted to **true** at `acs/ConfigDefaults.cs:36`.)

Because it is true, `ConnectToNeededServersState.ConnectToSteam()` calls
`_steamManager.Authenticate(15f)` (`acs/GameStateMachine/ConnectToNeededServersState.cs:154-161`),
which reaches `SteamManager.Inject()`:

```csharp
if (SteamAPI.RestartAppIfNecessary(AppId_t.Invalid)) { Application.Quit(); return; }
...
m_bInitialized = SteamAPI.Init();
if (!m_bInitialized) { throw new SteamWorksInitFailedException(); }
```
(`acs/SteamManager.cs:65-81`.)

With Steam closed, `Init()` returns false and the boot dies there. With Steam
**open** it still dies, one step later: the log shows Steam initialising fine —

```
[SteamManager] Steam Username: Doubtful Hermit
[SteamManager] Steam User Id: 76561198002472036
[SteamManager] Steam App Id: 322780
```

— and then, fifteen seconds later,

```
[EXCEPTION] [UnrecoverableErrorState] Exception of type 'SteamAuthTimeoutException' was thrown.
```

then the whole sequence again at 22:13:32. The auth-ticket callback
(`GetAuthSessionTicketResponse_t`) never arrives for a delisted appid, so
`RequestAuthSessionTicket`'s timeout fires, `HexAuthTicket` stays empty and
`Authenticate` rejects (`acs/SteamManager.cs:102-126, 168-182`). The retry is
not rate-limited because `_previousException is SteamAuthTimeoutException`
bypasses the 30-second guard, so it loops forever. **This is the boot hang.**

### There is a second, unrelated bug that has to be fixed first

`Harmony.CreateAndPatchAll(assembly)` is called once for the whole mod
(`WorldsAdriftReborn/WorldsAdriftReborn.cs:150`). One patch class throws while
resolving its target:

```
AccessTools.Method: Could not find method for type WAConfig and name GetOrDefault and parameters (string, string)
Unhandled exception occurred while patching the game: HarmonyLib.HarmonyException:
  ... WAConfig_Patch+GetOrDefault_String_WithFallback::GetTargetMethod()
```

and `"Patching completed successfully"` **never appears in the log**. Harmony
propagates the exception out of `PatchAll`, so every patch class it had not yet
processed was silently skipped. The cause is that `WAConfig.GetOrDefault<T>(string, T)`
declares its second parameter as the open generic `T`, not `string`, so matching
on `typeof(string)` finds nothing and `MakeGenericMethod` runs on null.

Any new patch is at the mercy of type ordering until this is fixed, so it is
Phase 0.

### Login does not touch Steam, and must not start to

Wareborn authenticates with the username and password typed into the client's
own form. `LandingScreen.LoginFromForm` calls
`BossaNetBootstrap.AuthenticateWithBossaNet`, which posts a body carrying two
credential objects; our server reads only `bossaCredential`
(`WorldsAdriftServer/Objects/SteamObjects/SteamAuthRequestToken.cs`,
`WorldsAdriftServer/Handlers/Authentication/SteamAuthenticationHandler.cs`) and
answers with a session token that comes back in the `Security` header.

The Steam id and ticket that ride along in `steamCredential` are ignored by us,
and the client already substitutes the literals `"steamUserId"` and
`"steamAuthToken"` whenever `SteamManager` gave it nothing
(`acs/Bossa.Travellers.BossaNet/BossaNetBootstrap.cs:138-151, 358-370`). The
in-game identity is a fixed server-side stub, `LocalPlayerIdentity.PlayerId`
= `"id"`, served in component 1086 — not a Steam id.

So turning Steam off cannot break login. The one thing that changes shape is
`LobbySystem.ConnectToGameServer`, which switches from `LoginMetadata.SteamMetadata()`
to `LoginMetadata.TestingMetadata(userName)` — different `UserId`, `Credentials`
and `Platform` in the SpatialOS connect metadata. Nothing in
`WorldsAdriftRebornCoreSdk` or `WorldsAdriftRebornGameServer` reads those three
keys, and the fields that matter (`playerName`, `bossaId`,
`bossaNetGameClientToken`, `characterUid`) are filled from BossaNetBootstrap
afterwards in `CompleteConnect`, identically on both branches.

## Phases

Each phase is a separate commit so they can be gated independently.

### Phase 0 — make patching survive one bad patch class
`WorldsAdriftReborn.cs`, `WAConfig_Patch.cs`.

Patch class by class and report failures per class instead of letting the first
one abort the assembly. Fix the two `GetOrDefault` target lookups so they stop
throwing at all.

*Risk:* patch classes that have been dormant behind the abort will start
applying again. That is the intended state and is what makes the rest of this
plan reliable, but it is the single largest behavioural change here, so it goes
in its own commit.

### Phase 1 — kill Steam
`WAConfig_Patch.cs`, new `Patching/BypassSteam/`.

1. Force `Bootstrap.UseSteam` false through the existing `ForcedBool` seam. This
   is the client's own no-Steam branch; every consumer already handles it.
2. Neuter `SteamManager.Inject` and `SteamManager.Authenticate` directly, as a
   backstop, because `SteamManagerInit.Start()` and `IntroScreen.Start()` call
   `Authenticate` with no `IsUsingSteam` check at all. Seed the identity statics
   from the existing (and until now unused) `Steam_UserId` config entry so
   nothing null-derefs. Leave `SteamManager.Initialized` false, which is what
   keeps `UGCManager`, `SteamWorkshopFileList` and `CreateAuthRequestPayload`
   from making Steam calls of their own.
3. `DebugInfoScreen.SetVersionAndBranchDisplay` waits on `while (!SteamManager.Initialized)`
   with no timeout, so leaving `Initialized` false would hang the build-number
   display forever. Replace that coroutine.

*Risk:* low. The failure mode to watch for is an unguarded `SteamManager` static
read somewhere the audit missed; the patches log rather than fail silently.

### Phase 2 — the "link your Steam account" modal
`Patching/LandingScreen/`.

`LandingScreen.LoginFromForm` shows a confirmation dialog reading *"You need to
link your Steam account to your Worlds Adrift account to play. Would you like to
do that now?"* — and YES is simply the login call
(`acs/Travellers.UI.Login/LandingScreen.cs:220-236`). It is a consent gate for
something that no longer exists, sitting in front of every sign-in. Skip the
dialog and authenticate directly, keeping the empty-field check.

*Risk:* low, but it is on the login path, so it gets its own commit and its own
test pass.

### Phase 3 — server select
Depends on what the deployment list actually carries; researched separately.
Make the server we do not run unmistakably unavailable rather than a card that
looks fine and does nothing, or collapse the screen to the one real choice.

### Phase 4 — landing screen copy and links
`CreateAccount()` and `ForgotPassword()` open `WAConfig.Get<string>` URLs
(`BossaNet.CreateAccountUrl`, `BossaNet.ResetPasswordUrl`), so they can be
redirected through the same `ForcedString` seam the alliances host already uses —
no new machinery. CREATE ACCOUNT goes to `https://wareborn.ratlabs.cc/`.

FORUMS becomes MAP (`https://wareborn.ratlabs.cc/map`); its label and URL come
from the GameDB `LandingScreenLiteralsAndLinks` table, key `FORUM_LINK`, read by
`UI.Components/DevelopmentArea.cs`. PATCH NOTES stays.

The "DEVELOPMENT BUILD" blurb and any other Bossa-era copy that is now false get
rewritten to describe this project, plainly.

### Phase 5 — the remaining Steam-era messages
`LandingScreen_Patch.cs` and `Shop_Patch.cs` still tell the player to open their
Steam library. In a build with no Steam at all that is no longer true.

### Phase 6 — gates and delivery
`dotnet build` clean for every C# project including the net35 mod; the three
test suites at or above their measured baselines
(`WorldsAdriftServer.Tests` 571 passed / 26 skipped, `Multiplayer.Tests` 3604 / 0,
`Storage.Tests` 57 passed / 146 skipped). Stage a patcher release without
publishing it.

## Outcome

All six phases landed. The client was launched afterwards, with Steam running
and available, and the new log says:

```
Patching completed successfully: 91 method(s) patched across 84 class(es).
```

against the old log's `Unhandled exception occurred while patching the game`
and no success line at all. Zero failed classes, zero errors, zero exceptions
in the whole boot.

The word "steam" appears three times in the entire log. One is our own
`ConfigKeys.UseSteam resolves to 'Bootstrap.UseSteam'`, one is the game logging
the *name* of the `CheckSteamBranchAndConfig` boot step, and one is a config read.
There is no `[SteamManager] Steam Username`, no `Steam User Id`, no
`Steam App Id`, no `[SteamChecker] Trying to fetching steam branch name`, and no
`SteamAuthTimeoutException` - all of which the previous log had. Steam was
running and the client never once spoke to it.

The boot then ran straight through
`CheckSteamBranchAndConfig -> ConnectToAnalytics -> InitializeEACClient ->
ValidateClientVersion -> ConnectToGameDB -> SplashScreenState` in about nine
seconds, where before it hung fifteen and looped.

`[WAReborn] PvE card 'PvE Server' greyed out; it is not a server we run.` also
appears, so the card-root walk found the right object and Phase 3 is confirmed
live rather than only by reading.

## What cannot be verified here

Steam could not be closed for the test - it belongs to the player and was left
alone. The launch above is arguably the stronger evidence anyway: Steam was up
and reachable and the client still never called it, which is a claim "Steam was
absent so nothing could call it" cannot make.

The landing screen itself was not reached. Getting past the splash screen needs
a CONTINUE press, and this project does not drive game UI with synthetic input.
So Phase 2 (the login dialog), Phase 4 (the links and copy) and Phase 5 (the
Island Creator, shop and connection-error messages) are verified by reading the
decompile and by their patch classes applying cleanly - 84 of 84, none failed -
but not by eye. Each of those patches logs which replacement landed and warns by
name about any that did not, so the next real launch will say so in the log
without anyone having to go looking.
