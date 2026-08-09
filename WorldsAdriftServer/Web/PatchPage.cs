namespace WorldsAdriftServer.Web
{
    /// <summary>
    /// The /patch index: a human-readable view of the latest client patch. It
    /// shows the version and every file with its size and a direct download
    /// link, so a player (or we) can grab files by hand or just see what the
    /// current build is - the WAPatch app is the automated path, this is the
    /// shop window.
    ///
    /// Self-contained and themed to match <see cref="SignupPage"/> (same airship
    /// palette, light/dark aware). The page is static; it fetches the live
    /// manifest client-side from /patch/manifest.json, which Caddy serves from
    /// the static patch dir on the VPS - so this never has to read the manifest
    /// off disk and always reflects whatever was last rsynced.
    /// </summary>
    internal static class PatchPage
    {
        internal const string ContentType = "text/html; charset=utf-8";

        internal const string Html = @"<!DOCTYPE html>
<html lang=""en"">
<head>
<meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1"">
<meta name=""color-scheme"" content=""light dark"">
<title>Client Patch - Worlds Adrift Reborn</title>
<style>
:root {
  --ink:        #26313d;
  --ink-soft:   #43525f;
  --ink-faint:  #5d6b76;
  --field:      rgba(74, 80, 96, .60);
  --field-edge: rgba(30, 36, 48, .30);
  --field-ink:  #f0ece2;
  --timber-mid: #d9a074;
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
  position: relative; width: 100%; max-width: 52rem; margin: 0 auto;
  padding: 2rem 1.75rem; border-radius: 14px;
  background: var(--veil);
  box-shadow: 0 10px 40px rgba(0,0,0,.16);
  backdrop-filter: blur(2px);
}
.mark {
  font-size: .68rem; letter-spacing: .38em; text-transform: uppercase;
  color: var(--ink-faint); text-align: center; margin: 0 0 .4rem;
}
h1 { font-size: 1.7rem; text-align: center; margin: 0 0 .25rem; }
.sub { text-align: center; color: var(--ink-soft); margin: 0 0 1.5rem; }
.meta {
  display: flex; flex-wrap: wrap; gap: .5rem 1.5rem; justify-content: center;
  margin: 0 0 1.25rem; color: var(--ink-soft); font-size: .92rem;
}
.meta b { color: var(--ink); }
.tablewrap { overflow-x: auto; border-radius: 10px; border: 1px solid var(--field-edge); }
table { border-collapse: collapse; width: 100%; font-size: .9rem; }
th, td { text-align: left; padding: .5rem .7rem; border-bottom: 1px solid var(--field-edge); white-space: nowrap; }
th { color: var(--ink-faint); font-weight: 600; text-transform: uppercase; letter-spacing: .05em; font-size: .72rem; }
td.size { text-align: right; font-variant-numeric: tabular-nums; color: var(--ink-soft); }
td a { color: var(--rust); text-decoration: none; }
td a:hover { text-decoration: underline; }
tr:last-child td { border-bottom: 0; }
.note { margin: 1.25rem 0 0; color: var(--ink-soft); font-size: .88rem; }
.err { color: var(--rust); }
code { background: var(--field); color: var(--field-ink); padding: .1rem .35rem; border-radius: 5px; font-size: .85em; }
</style>
</head>
<body>
<main>
  <p class=""mark"">Worlds Adrift Reborn</p>
  <h1>Client Patch</h1>
  <p class=""sub"">The self-updating <b>WAPatch</b> app installs these for you. This page is for grabbing files by hand or checking the latest build.</p>
  <div class=""meta"" id=""meta""><span>Loading the latest manifest&hellip;</span></div>
  <div class=""tablewrap""><table id=""tbl"" hidden><thead><tr><th>File</th><th>Installs to</th><th style=""text-align:right"">Size</th></tr></thead><tbody></tbody></table></div>
  <p class=""note"" id=""note""></p>
</main>
<script>
(function () {
  var fmt = function (n) {
    if (n < 1024) return n + ' B';
    if (n < 1048576) return (n / 1024).toFixed(1) + ' KB';
    return (n / 1048576).toFixed(2) + ' MB';
  };
  var meta = document.getElementById('meta');
  var tbl = document.getElementById('tbl');
  var note = document.getElementById('note');

  fetch('/patch/manifest.json', { cache: 'no-store' })
    .then(function (r) { if (!r.ok) throw new Error('manifest HTTP ' + r.status); return r.json(); })
    .then(function (m) {
      var total = 0;
      (m.files || []).forEach(function (f) { total += f.sizeBytes || 0; });
      meta.innerHTML =
        '<span>Version <b>' + (m.version || '?') + '</b></span>' +
        '<span>Build <b>' + (m.build || '?') + '</b></span>' +
        '<span>Cut <b>' + (m.generatedUtc || '?') + '</b></span>' +
        '<span><b>' + (m.files ? m.files.length : 0) + '</b> files, <b>' + fmt(total) + '</b></span>';

      var body = tbl.querySelector('tbody');
      (m.files || []).forEach(function (f) {
        var tr = document.createElement('tr');
        var name = (f.name || f.destPath || '').split('/').pop();
        var a = document.createElement('a');
        a.href = f.url || ('/patch/files/' + f.name);
        a.textContent = name;
        var td1 = document.createElement('td'); td1.appendChild(a);
        var td2 = document.createElement('td'); td2.textContent = f.destPath || '';
        var td3 = document.createElement('td'); td3.className = 'size'; td3.textContent = fmt(f.sizeBytes || 0);
        tr.appendChild(td1); tr.appendChild(td2); tr.appendChild(td3);
        body.appendChild(tr);
      });
      tbl.hidden = false;
      note.innerHTML = 'Manual install order does not matter, but the plugin files go into ' +
        '<code>BepInEx/plugins/WorldsAdriftReborn/</code> and the rest into the game root. ' +
        'Never replace <code>steam_api64.dll</code> or <code>winhttp.dll</code>. The WAPatch app handles all of this and verifies every file.';
    })
    .catch(function (e) {
      meta.innerHTML = '<span class=""err"">Could not load the manifest (' + e.message + '). ' +
        'It may not be published yet - try <a href=""/patch/manifest.json"">/patch/manifest.json</a> directly.</span>';
    });
})();
</script>
</body>
</html>
";
    }
}
