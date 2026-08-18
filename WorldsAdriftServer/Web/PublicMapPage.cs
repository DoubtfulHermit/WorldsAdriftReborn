namespace WorldsAdriftServer.Web
{
    /// <summary>
    /// The /map shell as it stands in PHASE A of the public-map work: a
    /// self-contained placeholder that proves the route, states what the page
    /// will be, and shows the live anonymized headline figures from /map/data.
    ///
    /// Phase B replaces this with the real map - the admin console's renderer,
    /// extracted into shared assets and fed by the anonymized projection
    /// instead of the operator one. Nothing on this page or its data feed
    /// carries identity; see PublicMapProjection for the boundary.
    ///
    /// Fully self-contained on purpose: this page is public, so a CDN or font
    /// host reference would leak every visitor to a third party.
    /// </summary>
    internal static class PublicMapPage
    {
        internal const string ContentType = "text/html; charset=utf-8";

        internal const string Html = @"<!DOCTYPE html>
<html lang=""en"">
<head>
<meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1"">
<meta name=""color-scheme"" content=""dark"">
<title>Live World Map - Worlds Adrift Reborn</title>
<style>
:root{--bg:#0b141b;--panel:#101d26;--line:#1e3240;--text:#d7e4ec;--text-soft:#9fb4c0;--accent:#71d0a5}
*{box-sizing:border-box;margin:0}
body{background:var(--bg);color:var(--text);font:16px/1.55 system-ui,sans-serif;display:grid;place-items:center;min-height:100vh;padding:1.5rem}
main{max-width:34rem;background:var(--panel);border:1px solid var(--line);border-radius:10px;padding:2rem 2.2rem}
h1{font-size:1.25rem;letter-spacing:.03em;margin-bottom:.8rem}
p{color:var(--text-soft);margin-bottom:.8rem}
strong{color:var(--accent);font-weight:650}
#live{margin-top:1rem;padding-top:1rem;border-top:1px solid var(--line);font-size:.9rem}
</style>
</head>
<body>
<main>
<h1>Worlds Adrift Reborn &mdash; Live World Map</h1>
<p>A public, anonymized view of the living world is being prepared here:
the preserved island atlas, its wildlife moving in real time, and the
ships and travellers currently aloft &mdash; with <strong>no names and no
identities</strong>, ever.</p>
<p id=""live"">Checking the skies&hellip;</p>
<script>
fetch('/map/data').then(function(r){return r.json();}).then(function(d){
  var el=document.getElementById('live');
  if(!d.reporting){el.textContent='The world is quiet right now: the game server is not reporting.';return;}
  var f=d.fauna||{};
  el.textContent='Right now: '+(d.currentOnline||0)+' traveller(s) aloft, '
    +((d.ships||[]).length)+' ship(s) on the wind, '
    +((f.liveCount||0))+' living creatures over the islands.';
}).catch(function(){
  document.getElementById('live').textContent='The live feed could not be reached.';
});
</script>
</main>
</body>
</html>";
    }
}
