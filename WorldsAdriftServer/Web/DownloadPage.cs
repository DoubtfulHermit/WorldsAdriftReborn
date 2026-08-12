namespace WorldsAdriftServer.Web
{
    /// <summary>
    /// The login-gated download page: the shop window for the WAPatch installer.
    /// Only a signed-in player ever sees it (the gate is in the download handler),
    /// so it can greet them by name and point at one prominent Download button that
    /// pulls the exe from <c>/download/WAPatch.exe</c>.
    ///
    /// Unlike the static <see cref="SignupPage"/> and <see cref="LoginPage"/>, this
    /// one is rendered per request - the greeting name and the current patch
    /// version/build are stamped in server-side. Every stamped value is
    /// HTML-encoded through <see cref="AdminPage.HtmlEncode"/>, the same escaper the
    /// admin dashboard uses, so a username or a manifest field can never break out
    /// of the markup. Themed to match <see cref="PatchPage"/> (same airship
    /// palette, light/dark aware, one centred card).
    /// </summary>
    internal static class DownloadPage
    {
        internal const string ContentType = "text/html; charset=utf-8";

        /// <summary>The link the Download button and hero point at.</summary>
        private const string PatcherHref = "/download/WAPatch.exe";

        /// <summary>
        /// Renders the page for a signed-in player. <paramref name="username"/> is
        /// the display name to greet; <paramref name="version"/> and
        /// <paramref name="build"/> come from the patch manifest and each fall back
        /// to a dash upstream when the manifest cannot be read - this method just
        /// renders whatever it is handed, encoded.
        /// </summary>
        internal static string Render(string username, string version, string build)
        {
            string name = AdminPage.HtmlEncode(username);
            string ver = AdminPage.HtmlEncode(version);
            string bld = AdminPage.HtmlEncode(build);

            return @"<!DOCTYPE html>
<html lang=""en"">
<head>
<meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1"">
<meta name=""color-scheme"" content=""light dark"">
<title>WAReborn Patcher - Worlds Adrift Reborn</title>
<style>
:root {
  --ink:        #26313d;
  --ink-soft:   #43525f;
  --ink-faint:  #5d6b76;
  --field:      rgba(74, 80, 96, .60);
  --field-edge: rgba(30, 36, 48, .30);
  --field-ink:  #f0ece2;
  --timber-lo:  #c68d60;
  --timber-mid: #d9a074;
  --timber-hi:  #eebd8e;
  --timber-ink: #4a2c14;
  --batten:     #a97244;
  --batten-lo:  #8e5d36;
  --batten-edge:#7d4d2a;
  --rust:       #a8321f;
  --good:       #2c6b52;
  --veil:       rgba(255, 255, 255, .40);
}
@media (prefers-color-scheme: dark) {
  :root {
    --ink:       #e4e9ec;
    --ink-soft:  #b3c0c8;
    --ink-faint: #8b99a3;
    --field:     rgba(96, 106, 124, .40);
    --field-edge:rgba(180, 200, 215, .16);
    --rust:      #ef8a6b;
    --good:      #7fd2b3;
    --veil:      rgba(6, 12, 20, .52);
  }
}
* { box-sizing: border-box; }
body {
  margin: 0; min-height: 100vh; padding: 2.5rem 1.25rem 3rem;
  color: var(--ink);
  background: linear-gradient(180deg, #93b7c8, #bed2d8 55%, #dde7e2);
  font-family: 'Inter', 'Segoe UI', Roboto, 'Helvetica Neue', 'DejaVu Sans', Arial, sans-serif;
  font-size: 16px; line-height: 1.55;
}
@media (prefers-color-scheme: dark) {
  body { background: linear-gradient(180deg, #1b2530, #223038 55%, #2b3a3e); }
}
main {
  position: relative; width: 100%; max-width: 40rem; margin: 0 auto;
  padding: 2rem 1.75rem; border-radius: 14px;
  background: var(--veil);
  box-shadow: 0 10px 40px rgba(0,0,0,.16);
  backdrop-filter: blur(2px);
  text-align: center;
}
.mark {
  font-size: .68rem; letter-spacing: .38em; text-transform: uppercase;
  color: var(--ink-faint); margin: 0 0 .4rem;
}
h1 { font-size: 1.9rem; margin: 0 0 .25rem; font-weight: 300; letter-spacing: .03em; }
.greet { color: var(--ink-soft); margin: 0 0 1.6rem; }
.greet b { color: var(--ink); }

.meta {
  display: flex; flex-wrap: wrap; gap: .4rem 1.5rem; justify-content: center;
  margin: 0 0 1.6rem; color: var(--ink-soft); font-size: .92rem;
}
.meta b { color: var(--ink); }

/* The plank button, borrowed from the sign-up page, as an anchor this time. */
.plank {
  position: relative;
  display: inline-block;
  margin: .4rem 0 1.9rem;
  padding: .95rem 2.6rem;
  font-size: .85rem;
  font-weight: 600;
  letter-spacing: .17em;
  text-transform: uppercase;
  text-decoration: none;
  color: var(--timber-ink);
  border: 1px solid #a4744a;
  border-radius: 1px;
  background-image:
    linear-gradient(180deg, rgba(255,255,255,.34), rgba(255,255,255,0) 44%),
    repeating-linear-gradient(90deg, rgba(120,78,44,.07) 0 3px, transparent 3px 9px),
    linear-gradient(180deg, var(--timber-hi), var(--timber-mid) 46%, var(--timber-lo));
  box-shadow: 0 2px 0 rgba(112,72,40,.42), 0 12px 26px -14px rgba(38,24,10,.85);
  transition: filter .12s ease, transform .06s ease;
}
.plank::before, .plank::after {
  content: ''; position: absolute; top: -9px; bottom: -9px; width: 12px;
  border: 1px solid var(--batten-edge); border-radius: 1px;
  background-image: linear-gradient(90deg, var(--batten), var(--batten-lo));
  box-shadow: 0 2px 0 rgba(90,58,30,.35);
}
.plank::before { left: -7px; }
.plank::after  { right: -7px; }
.plank:hover { filter: brightness(1.06); }
.plank:active { transform: translateY(1px); }
.plank:focus-visible { outline: 2px solid var(--timber-hi); outline-offset: 3px; }

.steps {
  list-style: none; counter-reset: step; padding: 0;
  margin: 1.5rem auto 0; max-width: 30rem; text-align: left;
}
.steps li {
  counter-increment: step;
  position: relative; padding: .55rem 0 .55rem 2.6rem;
  color: var(--ink-soft); border-top: 1px solid var(--field-edge);
}
.steps li:first-child { border-top: 0; }
.steps li::before {
  content: counter(step);
  position: absolute; left: 0; top: .5rem;
  width: 1.7rem; height: 1.7rem; line-height: 1.7rem; text-align: center;
  border-radius: 50%; background: var(--field); color: var(--field-ink);
  font-size: .82rem; font-weight: 700;
}
.steps b { color: var(--ink); }
code { background: var(--field); color: var(--field-ink); padding: .1rem .35rem; border-radius: 5px; font-size: .85em; }

footer {
  margin-top: 2rem; font-size: .72rem; line-height: 1.5;
  color: var(--ink-faint);
}
</style>
</head>
<body>
<main>
  <p class=""mark"">Worlds Adrift Reborn</p>
  <h1>WAReborn Patcher</h1>
  <p class=""greet"">Signed in as <b>" + name + @"</b>.</p>

  <div class=""meta"">
    <span>Version <b>" + ver + @"</b></span>
    <span>Build <b>" + bld + @"</b></span>
  </div>

  <a class=""plank"" href=""" + PatcherHref + @""">Download WAPatch.exe</a>

  <ol class=""steps"">
    <li>Download &amp; run <b>WAPatch.exe</b>.</li>
    <li>Point it at your <b>Worlds Adrift</b> install folder.</li>
    <li>Click <b>Patch</b>.</li>
  </ol>

  <footer>
    An unofficial, fan-run community server. Not affiliated with, endorsed by, or supported by Bossa Studios.
  </footer>
</main>
</body>
</html>
";
        }
    }
}
