# FINDINGS — USERNAME/PASSWORD AUTH

## ⭐ LEAD: THE LOGIN FORM ALREADY EXISTS AND IS ONE SERVER LINE AWAY
`Travellers.UI.Login.LandingScreen` has **`_userField`, `_passwordField`, `_loginButton`** and
a `LoginFromForm()` that calls `BossaNetBootstrap.AuthenticateWithBossaNet(user, pwd, …)`
(`LandingScreen.cs:35-41, :220-237`). That path POSTs a **second credential object,
`bossaCredential`, in the SAME `/authenticate` body the client already sends**
(`BossaNetBootstrap.cs:443-445`):
```csharp
jObject.Add("steamCredential", CreateCredentialObject("steam", steamUserId, steamAuthToken));
jObject.Add("bossaCredential", CreateCredentialObject("bossa", bossaNetId, bossaNetPassword));
// CreateCredentialObject -> { platformId, secret, userKey }
// so bossaCredential.userKey = USERNAME, .secret = PASSWORD
```

**The form is hidden today for exactly one reason: our server answers every `/authenticate`
with success** (`SteamAuthenticationHandler.cs:18`), so `CheckLinkedAccount()` →
`HasLinkedAccount()` → `SetLoginFormActive(false)` (`LandingScreen.cs:166-182`).

> **Reply `{"success":false,"desc":"no_bossa_registration"}` and the login form appears —
> zero client changes, zero config lines for the friend.** Tab-order, Enter-to-submit and the
> failure label are already wired (`:145-164, :279-285, :239-251`).

Our DTO silently discards the password today — `SteamAuthRequestToken` has only
`{appId, steamCredential}` (`SteamAuthRequestToken.cs:9-13`). **Adding one property is the
whole client-facing change.**

**The in-game CREATE ACCOUNT and FORGOT PASSWORD buttons already open a URL we control via
config** (`LandingScreen.cs:210-213`) — 4 lines in `WAConfig_Patch` points them at our page.

## THE FOUR-RULE RESOLUTION
1. `bossaCredential` present + password verifies → mint token; **opportunistically link the
   request's real SteamID**; success.
2. `bossaCredential` present but wrong → failure, `desc: "invalid_credentials"`.
3. No `bossaCredential`; `steamCredential.userKey` is a real SteamID already linked → success
   (**a Steam player never sees the form again**).
4. Otherwise → failure, `desc: "no_bossa_registration"` → **the form appears.**

"Real SteamID" = 17 digits and not the literal `"steamUserId"`, which is what a Steam-less
client sends and which would otherwise become one shared account.

**This is best-of-both:** friend *with* Steam types a password once and skips the form
forever; friend *without* Steam types it each launch (or pastes one pairing code, once).
And it **solves the two-clients-on-one-machine problem outright** — two usernames, two
accounts, one shared SteamID — which a Steam-only design structurally could not.

**`steam/1234` never needs touching.** With the account resolved from the `Security` header,
the URL segment becomes decoration — removing the IL-transpiler work the Steam approach needed.

## ⚠ FOUR TRAPS THAT EACH PRODUCE A DEAD MENU
1. **`screenName` is mandatory and NOT null-guarded on the password path** (`:406-409`).
   Omit it and it throws → caught → **"Connection Error … QUIT" dialog**. It must also be
   **non-empty**, or every character creation POSTs `/player/reserveName`, which we do not
   route — so creation hangs forever with the save button already disabled.
2. **`desc` must always be present on failure.** The `if (jToken["desc"] != null)` has **no
   `else`** — a failure without `desc` fires **no callback at all**, leaving a frozen menu
   with no form and no error. **Prefer HTTP 200 + `success:false` + `desc` over 401.**
3. **Our `SteamAuthResponseToken` sets `desc = string.Empty`** (`:22`). Empty is *not* null,
   so it reaches `default:` → the QUIT dialog. Set `desc` explicitly on every failure.
4. **Never emit `bossa_account_not_validated`** — it dereferences `jToken["token"]` (`:419`).

## THE 28-MINUTE REFRESH DISCARDS THE PASSWORD
`RefreshGameClientToken` (`:615-629`) re-authenticates **Steam-only** every 1680 s. Two
consequences:
- **A Steam-only refresh must never mint a token for a different account**, or the player's
  roster identity flips mid-session — a bug that would look like corruption. The 4-rule order
  prevents it.
- **A failing refresh is silent and terminal** — the no-linked-account callback is
  `delegate {}`, and no further refresh is scheduled. The session keeps its original token.
  **Therefore server tokens must not expire inside a session → 30-day sliding expiry.**

## TOKEN
32 bytes from `RandomNumberGenerator.GetBytes(32)`, base64url, server-side table, 30-day
sliding. **Not a JWT, not HMAC-signed** — the server holds the table anyway, so a signature
buys nothing and would misrepresent the posture. But unlike today's routing key **this one IS
a credential** — name it `SessionToken` and keep it out of logs.
**Persist it in a mod-owned file beside the `.cfg`**, not `PlayerPrefs` (whose Windows backing
store is the Wine prefix registry and dies with a prefix rebuild).
**On expiry:** return **401 with a plain-text body** — `LobbySystem.cs:472-476` shows our
response text **verbatim** in an OK/QUIT dialog. Blunt but honest. **Never** return 200 with
an empty roster; that reads as "my characters are gone".

## PASSWORD HANDLING — verified in-box, no new dependency
Compiled against net6.0 reference assemblies, 0 errors:
`Rfc2898DeriveBytes.Pbkdf2(...)`, `RandomNumberGenerator.GetBytes(int)`,
`CryptographicOperations.FixedTimeEquals`.
**PBKDF2-HMAC-SHA256, 210,000 iterations, 16-byte salt, 32-byte output**, stored as one
algorithm-agile string `pbkdf2$sha256$210000$<salt>$<hash>` so swapping to Argon2 later is a
migration, not a rewrite. Not because PBKDF2 is best — Argon2id is — but pulling a NuGet
package into a Wine-hosted server is the worse trade.
**Stored:** account id, usernames, display name, one hash, timestamps.
**Not stored:** the password, an email, IPs.

### WHAT A HOSTILE ACTOR CAN DO — do not let this get softened
- **Everything travels in cleartext.** HTTP on a bare IP. **Anyone on the path — ISP, café
  wifi, the datacentre — reads the password and the token in plain sight.** Server-side
  hashing protects against **one** thing: someone obtaining the stored file. Nothing else.
- **The token is a bearer credential** — whoever sees it *is* that account for 30 days, and
  it is repeated on every roster call rather than once per login.
- **No rate limiting.** PBKDF2 at ~50–100 ms is the only brake. **Add a fixed 1-second delay
  after any failed auth** — 5 lines, no state, no lockout that could lock a friend out.
- **The game server cannot be lied to any less than today** — it keys peers by raw pointer and
  nothing parses `characterUid`. Accounts fix *"whose roster do I see"*, **not** *"who can I
  pretend to be in the world"*.

**Worth more than everything above combined: TLS in front of 8085.** A hostname plus Caddy,
**zero lines of C#** — `restServerUrl` is already a free-form string the friend edits.
Until then the sign-up page must say, in plain language and not small print:
**do not reuse a password you use anywhere else.**

## DELIBERATELY NOT BUILT
TLS termination in-process · email verification · password reset by email · 2FA · CAPTCHA ·
account lockout · signed/JWT tokens · Steam ticket validation (**impossible** — needs a
publisher key for a delisted app) · audit logging.

## STEAM-LESS SAFETY — one line
`SetLoginFormActive` calls `SteamChecker.IsSteamBranchPTS()` → `SteamApps.GetCurrentBetaName`
(`LandingScreen.cs:111-116`), guarded only by `UseSteam` (default **true**). The shipped
Steamworks.NET contains `TestIfAvailableClient`, the guard that throws when uninitialised, and
this is on the landing screen's first-frame path. **Fix: return `false` for
`"Bootstrap.UseSteam"` in the existing `WAConfig_Patch.Get_Bool`.** `UseSteam` has exactly one
consumer, so the flip is contained. *(Not empirically verified — no Steam-less machine here.)*
The auth path itself is already Steam-safe.

## CREDENTIAL CHANNELS, RANKED FOR A NON-TECHNICAL FRIEND
| # | channel | friend's effort | our cost |
|---|---|---|---|
| **1** | **native login form** | type user+pass in the game | **server-only** + 1-line Steam safety |
| **2** | **pairing code** | paste one code into one line, once ever | server + web + ~60 mod lines |
| 3 | config credentials | edit two lines, **password on disk forever** | ~20 mod lines — also the answer for local two-client testing |
| 4 | IMGUI overlay | type into an ugly box | ~150 lines — only if the form is unwired |
| 5 | launcher | download a new thing | large — defer |

## ROUTES THAT CAN HANG THE CLIENT FOREVER — fix while in here
`/player/reserveName` is **not routed** but the client calls it. `DELETE /character` is not
routed. `reserveCharacterSlot` returns nothing. An unmatched path sends **nothing at all**, so
the client waits for its own timeout. **Every route must answer, and 404 must have a body.**
Also add `OPTIONS` + CORS (the sign-up page is a different origin), and suffix-match the two
`==` routes.

## ORDERED PLAN — steps 1-6 are SERVER-ONLY, testable with curl
1. **PROBE FIRST** — log the `Security` header and both credential objects, change no
   behaviour. **The header round trip has never been observed; do not build step 5 before
   step 1 has run.**
2. `AccountPolicy` pure + xUnit (mirrors `RosterPolicyTests`).
3. Account store + `/account/signup|signin|session|password` — **unblocks the web-page agent**.
4. `bossaCredential` on the DTO + the 4-rule resolution + real tokens + Steam linking.
   ⭐ **The in-game login form goes live here, with no client change.**
5. Resolve the account from the `Security` header on every roster route; ownership checks;
   add the missing routes; guarantee non-empty `screenName`.
6. Migration (`WAREBORN_LEGACY_ROSTER_OWNER`, `WAREBORN_ACCOUNTS=off`) + the 1-second delay.
7. Point `CreateAccountUrl`/`ResetPasswordUrl` at our page (4 lines) — **the in-game buttons
   then work**. 8. Steam-less safety. 9. Config-credential override + **rewrite the three dead
   Steam config descriptions, which currently say the value does not matter**. 10. Remember-me
   + pairing. 11. *Only if unwired:* the overlay. 12. TLS.

## COULD NOT DETERMINE
**Whether the form's UI objects are actually wired in the shipped scene** — the C# is
complete and unambiguous, but they are `[SerializeField]` scene references not in the
decompile. **This is the single fork in the design.** If unwired: promote the pairing code to
primary and build the overlay; **the protocol, storage and password design are unchanged.**
Whether `GetCurrentBetaName` really throws without Steam. Whether
`CharacterAuthResponse.tokenExpiryTime` is parsed (change `"12.12.12"` in an isolated commit).
Whether the `Security` header arrives (step 1 settles it). Whether `PlayerPrefs` survives a
prefix rebuild.
