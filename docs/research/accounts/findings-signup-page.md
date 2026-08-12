# FINDINGS — SIGN-UP PAGE & PLAYER JOURNEY

Draft page committed as `signup-draft.html` — 18,544 bytes, **0 external references**, pure
ASCII, and deliberately **zero `"` and zero backticks** so it drops verbatim into a C# `@"..."`
string (the project is net6.0 with no `LangVersion`, so C# 11 raw strings are unavailable).

## ⭐ THE #1 LOSS POINT IS NOT THE PAGE — it is a Steam dialog *after* signup
Seconds after we tell the friend "you don't need Steam", the game shows its own unpatched
*"Failed to authenticate with Steam… make sure your steam client is not in Offline Mode."*
**Nothing on screen says to press RETRY** — `docs/running-locally.md:25-26` records that even a
developer has to be told. A non-technical friend reads it as *"my new account didn't work"*
and stops. **Fix it in the same commit: ~10 lines.**

## ⚠ AND THE ONE SIGN-UP CANNOT FIX
`SETUP.bat` still hands the player to **DepotDownloader for their Steam username, password and
Guard code** to pull the game depot. **Removing Steam from LOGIN is real; it does not remove
Steam from ACQUISITION.** Tell the friend up front. If they genuinely don't own the delisted
app, no sign-up page saves them.

## ⚠⚠ TWO LANDMINES UNDER THE WHOLE DESIGN
1. **`DataParser` prints EVERY received byte to the console** (`DataParser.cs:131-134`), and
   the console goes through `script` into journald. **Left alone, every sign-up POST writes the
   player's plaintext password into the system log.** Gate it before serving any web traffic.
2. **`update.ps1` overwrites `WorldsAdriftReborn.cfg` wholesale**, silently discarding
   hand-edits. **Ship any credential in that file and the next update logs everybody out** —
   a week later, with nobody connecting the two events. Fix in the same change.
   Related: **the installer scripts are not in the repo at all** — they live only inside the
   distribution zips, with the server address hardcoded in three hand-synced places. Bring
   them under version control as part of this.

## MEASURED FACTS ABOUT NetCoreServer UNDER OUR SETUP
Ran 6.5.0 locally rather than assuming:
- **`AddStaticContent` short-circuits `OnReceivedRequest` entirely for GET** — anything mounted
  silently shadows every GET beneath it.
- **`AddStaticContent` throws if the directory is missing** → a startup crash the first time
  someone scp's DLLs and forgets the folder.
- **`FileCache` runs a `FileSystemWatcher`** — Wine inotify emulation, in a process that must
  also stay attached to a pty.
- **An unmatched route sends NOTHING AT ALL** — the socket stays open until the peer times out.
  **A browser hitting `/` today spins forever on a blank tab.**
- **`request.Url` carries the query string** — measured. So the three `==` route comparisons
  break under any prefix *or query*.
- Existing JSON responses carry **no `Content-Type` at all**.

**Verdict: embed the page as an `EmbeddedResource`, do not serve files.** Verified: served
**byte-identical** to source through NetCoreServer with correct headers.

## THE API CONTRACT (what the auth/DB agents must implement)
`GET /` · `GET /api/config` · `GET /api/name-available?name=` · `POST /api/signup` ·
`POST /api/signin` · `POST /api/pair/redeem`.
One error envelope everywhere: `{error: snake_case, message: "one actionable sentence"}` —
**the page renders `message` verbatim, so the wording is part of the contract.**
`POST /api/pair/redeem` must also honour `Accept: text/plain` and return **one line, the exact
cfg line** — that is what lets `SETUP.bat` be five lines of batch instead of a JSON parser in
`cmd.exe` (`curl.exe` ships in Windows 10 1803+).
Pairing code: 6 chars from `23456789ABCDEFGHJKMNPQRSTVWXYZ` (no `0/O`, no `1/I/L`), shown
`XXX-XXX`, case/hyphen-insensitive, single use, 30 min.
**Do not add client-side password hashing — the hash becomes the password.**

## MOBILE DETAIL THAT MATTERS
**17px inputs** (below 16px iOS Safari zooms on focus and the layout jumps) · 52–54px tap
targets · `autocapitalize=off` on the name (iOS capitalises usernames otherwise) ·
`execCommand` clipboard fallback (in-app messenger browsers frequently deny the async
clipboard) · `viewport-fit=cover` + safe-area insets · no font loads.

## TLS — split the surfaces
A domain is ~€10/yr plus one A record. **Put Caddy in front for the SIGN-UP PAGE only** — one
line of config, automatic Let's Encrypt. **Leave the game on plain HTTP**: its 2019-era Unity
HTTP stack has untested certificate validation under Proton, and it carries only a routing
token, not a password.
**Encrypt the surface that carries the secret; leave the surface that carries a routing key
alone.** Not theatre — it maps exactly onto where the sensitive data is.
Until then the page says once, in plain words: *"treat this password as throwaway — never
reuse one you use anywhere else."* **No padlock emoji on an HTTP page.**

## ANTI-ABUSE, PROPORTIONATE
Build: **one shared invite code** (`WAREBORN_INVITE_CODE`, ~10 lines, stops a leaked link
becoming a faucet) · **a hard account cap** (a runaway guard, not security) · a kill switch.
Do NOT build: rate limiting (five players — it would mostly lock out a friend who mistyped) ·
email verification · password reset flow (**write the operator's manual reset into
`docs/hosting.md` instead — with no email there is no self-service**) · account lockout · 2FA.
