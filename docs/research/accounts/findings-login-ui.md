# FINDINGS — THE NATIVE LOGIN UI

## VERDICT: **IT EXISTS — fully built, fully wired, and running right now.**
An **email + password** form, instantiated on every launch today, and **the server already
controls whether it appears.** Reviving it costs **~30 lines in `WorldsAdriftServer` and ZERO
client patches.** Do not build a launcher, an IMGUI overlay, or a config credential scheme.

## EXTRACTED ASSET EVIDENCE — not archaeology
Prefab ships in `resources.assets`, container `ui/prefabs/screens/landingscreen`.
GameObject `LandingScreen` PathID 72777, MonoBehaviour 273691:
```
Creat Account Section > Holder
    "You need to have a Worlds Adrift account to play."
    Create Account  Button -> LandingScreen.CreateAccount     "CREATE ACCOUNT"
    "OR"  /  "Already registered? Log in to your account."
Email     InputField  ContentType.Standard   placeholder "Email Address"
Password  InputField  ContentType.Password (7), InputType 2, asterisk '*'
Login     Button -> LandingScreen.LoginFromForm              "LOGIN"
Forgot Password Button -> LandingScreen.ForgotPassword
"The email address or password that you have entered is incorrect."
"Please fill in both your email address and password."
```
**All 17 serialized object references on the MonoBehaviour are non-null — nothing was
stripped.** UnityEvents confirmed bound in the binary: `LoginFromForm`, `CreateAccount`,
`ForgotPassword` each sit immediately before `Button+ButtonClickedEvent`, all resolving to
MB 273691.

**Runtime proof** — our own session log, 2026-08-08T07:36:
```
[LandingScreen] Trying to checking if it is pts...
  at LandingScreen.SetLoginFormActive(Boolean enabled)
  at LandingScreen.ProtectedInit()      <- built on launch
...2 seconds later...
  at LandingScreen.SetLoginFormActive(Boolean enabled)
  at LandingScreen.HasLinkedAccount(String token)   <- hidden because we said success
```
**The form is built and shown for ~2 seconds on EVERY launch today**, then hidden the instant
`/authenticate` answers `success: true`.

## THE MOD DOES NOT TOUCH ANY OF IT
`BossaNetBootstrap` is **not patched anywhere**. `ProtectedInit`, `CheckLinkedAccount`,
`LoginFromForm`, `SetLoginFormActive` are **not patched**. There is **no Steamworks or
SteamManager patch at all**. `WAConfig_Patch` overrides only `RestServerUrl`,
`DeploymentStatusUrl`, `NtpServer` and `VOIP.Enabled` — so `UseBossaNet` and `UseSteam` keep
their defaults of **true**, confirmed in the log (`not touching BossaNet.UseBossaNet`).

**`UseBossaNet` must be LEFT ALONE.** Setting it false takes `SetAsLocalDeployment()`, which
destroys `CharacterSelectionHandler`, reads characters from a local file, and calls
`HasLinkedAccount("fake token")` — **hiding the form.** The wrong direction.

## REGISTRATION IS A WEB PAGE BY ORIGINAL DESIGN
"CREATE ACCOUNT" and "FORGOT PASSWORD" open a **configurable URL** in the player's browser
(`LandingScreen.cs:210-218`). The shipped defaults are dead S3 links. **Four lines in the
existing `WAConfig_Patch` string switch point them at our sign-up page** — exactly the flow
Bossa shipped. A perfect socket for the web page.

## ON FAILURE THE PLAYER JUST TYPES AGAIN
`LoginFailed(code)` → **`SetLoginFormActive(true)`** — the form returns with the text still
typed and the "incorrect" warning lit. **No retry limit, no lockout, no quit.** Only a
transport failure or a parse exception hard-fails to the QUIT dialog.

## A SPARE-PARTS DONOR
A second, older prefab **`ReadyLogin`** (GO 80053) also ships — its own email/password form
**plus a "Remember email" toggle** and a dev panel (name field, `localhost` host field, server
dropdown). Nothing instantiates it. Worth knowing for the remember-me toggle.

## TRAPS
- **`screenName` must be present on the bossa path** — read unconditionally at `:409`, unlike
  the Steam path which null-checks. Missing ⇒ throw ⇒ QUIT dialog.
- **`desc` must be present on every failure** — at `:415` a null `desc` fires **no callback at
  all**, and `LoginFromForm` has already called `HideAllInput()`. **A soft-lock: empty screen,
  no form, no error. The single most dangerous failure mode.**
- **The 28-minute refresh re-auths via Steam, not BossaNet.** If the server ever answers the
  Steam-only refresh with success it **silently overwrites the logged-in player's token
  mid-session.** So server tokens must never expire.
- **PTS trap:** `SetLoginFormActive` hides the login controls when
  `SteamChecker.IsSteamBranchPTS()`. Today the branch is `""` vs default `"beta"` → false.
  **Never set `PublicTestServerSteamBranchName` to match, or the controls disappear.**
- **Cosmetic:** the labels say "Email Address". If accounts are keyed on a username, either
  accept the wording or retarget the TMP strings.

## IDENTITY WITHOUT STEAM — 3 of 4 fields are already server-supplied
`LoginMetadata`: `PlayerName` ← our `screenName` · `BossaId` ← our `bossaId` ·
`BossaNetGameClientToken` ← our `token` · `CharacterUid` ← the selection.
**Only `UserId` is Steam-derived** — and the game server never reads it.
