using System.Text;

namespace WorldsAdriftServer.Web
{
    /// <summary>
    /// The operator dashboard's HTML, served verbatim like <see cref="SignupPage"/>
    /// and sharing its airship aesthetic: flat slate fields, a timber-plank
    /// button, a soft veil over a sky gradient, and full light/dark support - all
    /// self-contained, no external CSS, fonts, scripts or images.
    ///
    /// Two pages live here. The login page is what an unauthenticated visitor
    /// sees at /admin; the dashboard is what a signed-in operator sees. The
    /// dashboard renders entirely from a JSON payload (embedded once for the
    /// first paint, then re-fetched from /admin/api/stats) so a single code path
    /// - the API - is the source of truth and the auto-refresh is just that path
    /// on a timer.
    /// </summary>
    internal static class AdminPage
    {
        internal const string ContentType = "text/html; charset=utf-8";

        /// <summary>Shared look, kept identical in spirit to the sign-up page.</summary>
        private const string Style = @"<style>
:root{
  --ink:#26313d;--ink-soft:#43525f;--ink-faint:#5d6b76;
  --field:rgba(74,80,96,.60);--field-edge:rgba(30,36,48,.30);--field-ink:#f0ece2;--field-hint:#c2c6cf;
  --timber-lo:#c68d60;--timber-mid:#d9a074;--timber-hi:#eebd8e;--timber-ink:#4a2c14;
  --batten:#a97244;--batten-lo:#8e5d36;--batten-edge:#7d4d2a;
  --rust:#a8321f;--good:#2c6b52;--warn:#b46b16;
  --panel:rgba(255,255,255,.40);--panel-edge:rgba(30,36,48,.16);
  --veil:rgba(255,255,255,.40);--halo:0 1px 0 rgba(255,255,255,.55);
}
@media (prefers-color-scheme:dark){:root{
  --ink:#e4e9ec;--ink-soft:#b3c0c8;--ink-faint:#8b99a3;
  --field:rgba(96,106,124,.40);--field-edge:rgba(180,200,215,.16);--field-ink:#eef1f3;--field-hint:#9aa6b2;
  --rust:#ef8a6b;--good:#7fd2b3;--warn:#e5a95c;
  --panel:rgba(12,20,28,.46);--panel-edge:rgba(180,200,215,.12);
  --veil:rgba(6,12,20,.50);--halo:0 1px 3px rgba(0,0,0,.65);
}}
*{box-sizing:border-box;}
body{margin:0;min-height:100vh;padding:2rem 1.25rem 3rem;color:var(--ink);
  background:linear-gradient(180deg,#93b7c8,#bed2d8 55%,#dde7e2 100%);background-attachment:fixed;
  font-family:'Inter','Segoe UI',Roboto,'Helvetica Neue','DejaVu Sans',Arial,sans-serif;font-size:16px;line-height:1.55;}
@media (prefers-color-scheme:dark){body{background:linear-gradient(180deg,#070d14,#101c26 55%,#1b2c35 100%);background-attachment:fixed;}}
a{color:inherit;}
.wrap{max-width:60rem;margin:0 auto;}
.mark{font-size:.66rem;letter-spacing:.38em;text-transform:uppercase;color:var(--ink-soft);text-shadow:var(--halo);margin:0 0 .5rem;}
h1{font-size:clamp(1.4rem,5vw,1.9rem);font-weight:300;letter-spacing:.04em;margin:0 0 .3rem;text-shadow:var(--halo);}
h2{font-size:.72rem;letter-spacing:.2em;text-transform:uppercase;color:var(--ink-soft);margin:0 0 .8rem;text-shadow:var(--halo);}
.lede{margin:.2rem 0 1.6rem;color:var(--ink-soft);font-size:.9rem;text-shadow:var(--halo);}
.card{position:relative;background:var(--panel);border:1px solid var(--panel-edge);border-radius:2px;
  padding:1.3rem 1.4rem;margin:0 0 1.4rem;-webkit-backdrop-filter:blur(5px);backdrop-filter:blur(5px);}
.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(8.5rem,1fr));gap:.8rem;}
.stat{padding:.5rem .2rem;}
.stat .n{font-size:1.6rem;font-weight:600;letter-spacing:.02em;}
.stat .l{font-size:.64rem;letter-spacing:.16em;text-transform:uppercase;color:var(--ink-faint);}
label{display:block;font-size:.64rem;letter-spacing:.2em;text-transform:uppercase;color:var(--ink-soft);text-shadow:var(--halo);margin:0 0 .35rem;}
input,select{width:100%;font:inherit;color:var(--field-ink);background:var(--field);border:1px solid var(--field-edge);border-radius:0;padding:.6rem .7rem;
  -webkit-backdrop-filter:blur(3px);backdrop-filter:blur(3px);}
input::placeholder{color:var(--field-hint);}
select option{color:#202936;background:#e9eef0;}
input:focus-visible,select:focus-visible,button:focus-visible{outline:2px solid var(--timber-hi);outline-offset:3px;}
.field{margin-bottom:.85rem;text-align:left;}
button,.btn{position:relative;display:inline-block;width:100%;margin:.4rem 0 .2rem;padding:.72rem 1.4rem;font:inherit;font-size:.78rem;font-weight:600;
  letter-spacing:.16em;text-transform:uppercase;color:var(--timber-ink);border:1px solid #a4744a;border-radius:1px;cursor:pointer;text-align:center;text-decoration:none;
  background-image:linear-gradient(180deg,rgba(255,255,255,.34),rgba(255,255,255,0) 44%),linear-gradient(180deg,var(--timber-hi),var(--timber-mid) 46%,var(--timber-lo));
  box-shadow:0 2px 0 rgba(112,72,40,.42),0 12px 26px -14px rgba(38,24,10,.85);transition:filter .12s ease;}
button:hover:not(:disabled),.btn:hover{filter:brightness(1.06);}
button:disabled{filter:saturate(.45) brightness(.94);cursor:progress;}
.btn.ghost{background:none;box-shadow:none;color:var(--ink-soft);border-color:var(--panel-edge);width:auto;padding:.5rem 1rem;}
.err{display:block;margin:.4rem 0 0;color:var(--rust);font-weight:500;font-size:.82rem;}
.row{display:flex;flex-wrap:wrap;gap:.6rem;align-items:flex-end;}
.row .field{flex:1 1 18rem;margin-bottom:0;}
.row .grow{flex:1 1 18rem;}
.row .fit{flex:0 0 auto;}
table{width:100%;border-collapse:collapse;font-size:.84rem;}
th,td{text-align:left;padding:.4rem .5rem;border-bottom:1px solid var(--panel-edge);}
th{font-size:.6rem;letter-spacing:.14em;text-transform:uppercase;color:var(--ink-faint);}
td.num,th.num{text-align:right;font-variant-numeric:tabular-nums;}
.muted{color:var(--ink-faint);}
.pill{display:inline-block;padding:.08rem .5rem;border-radius:1rem;font-size:.66rem;letter-spacing:.08em;text-transform:uppercase;border:1px solid transparent;}
.pill.ok{color:var(--good);border-color:var(--good);}
.pill.bad{color:var(--rust);border-color:var(--rust);}
.pill.warn{color:var(--warn);border-color:var(--warn);}
.banner{display:none;margin:0 0 1.4rem;padding:.9rem 1.1rem;border-left:4px solid var(--warn);background:var(--panel);border-radius:2px;
  -webkit-backdrop-filter:blur(5px);backdrop-filter:blur(5px);font-size:.9rem;}
.banner.show{display:block;}
.banner.spiral{border-left-color:var(--rust);}
.banner strong{display:block;letter-spacing:.06em;margin-bottom:.15rem;}
.topbar{display:flex;justify-content:space-between;align-items:flex-start;gap:1rem;flex-wrap:wrap;margin-bottom:.4rem;}
.asof{font-size:.72rem;color:var(--ink-faint);}
.tool-grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(15rem,1fr));gap:1rem;margin-top:1rem;}
.tool{border:1px solid var(--panel-edge);padding:1rem;}
.tool h3{font-size:.82rem;letter-spacing:.08em;margin:0 0 .35rem;}
.tool p{font-size:.78rem;color:var(--ink-faint);margin:.2rem 0 .7rem;}
.button-row{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:.45rem;}
.button-row button{padding:.58rem .5rem;margin:0;font-size:.67rem;}
.feedback{display:none;margin-top:1rem;padding:.7rem .8rem;border-left:3px solid var(--good);background:var(--panel);font-size:.82rem;}
.feedback.show{display:block}.feedback.bad{border-left-color:var(--rust);}
footer{margin-top:1.5rem;font-size:.7rem;color:var(--ink-faint);text-shadow:var(--halo);}
@media (prefers-reduced-motion:reduce){*{transition-duration:.01ms!important;}}
</style>";

        // ---- login ---------------------------------------------------------

        internal static string Login(string? error)
        {
            string errBlock = string.IsNullOrEmpty(error)
                ? string.Empty
                : "<span class=\"err\">" + HtmlEncode(error!) + "</span>";

            return @"<!DOCTYPE html><html lang=""en""><head>
<meta charset=""utf-8""><meta name=""viewport"" content=""width=device-width, initial-scale=1"">
<meta name=""color-scheme"" content=""light dark""><meta name=""robots"" content=""noindex"">
<title>Operator sign-in - Worlds Adrift Reborn</title>" + Style + @"</head>
<body><div class=""wrap"" style=""max-width:26rem"">
<p class=""mark"">Worlds Adrift Reborn</p>
<h1>Operator sign-in</h1>
<p class=""lede"">This console is for the server operator. Player accounts do not sign in here.</p>
<div class=""card"">
<form method=""post"" action=""/admin/login"">
  <div class=""field"">
    <label for=""username"">Operator</label>
    <input id=""username"" name=""username"" type=""text"" autocomplete=""username"" spellcheck=""false"" autocapitalize=""none"">
  </div>
  <div class=""field"">
    <label for=""password"">Passphrase</label>
    <input id=""password"" name=""password"" type=""password"" autocomplete=""current-password"">
  </div>
  " + errBlock + @"
  <button type=""submit"">Sign in</button>
</form>
</div>
<footer>An unofficial, fan-run community server. Not affiliated with Bossa Studios.</footer>
</div></body></html>";
        }

        // ---- disabled ------------------------------------------------------

        internal const string Disabled = @"<!DOCTYPE html><html lang=""en""><head>
<meta charset=""utf-8""><meta name=""viewport"" content=""width=device-width, initial-scale=1"">
<meta name=""color-scheme"" content=""light dark""><meta name=""robots"" content=""noindex"">
<title>Admin panel disabled</title>" + Style + @"</head>
<body><div class=""wrap"" style=""max-width:30rem"">
<p class=""mark"">Worlds Adrift Reborn</p>
<h1>Admin panel is off</h1>
<p class=""lede"">No operator credential is installed. Set the <code>WAREBORN_ADMIN</code> environment
variable to <code>username:hash</code> and restart the login server to enable this console.</p>
</div></body></html>";

        // ---- dashboard -----------------------------------------------------

        /// <summary>
        /// The dashboard shell. <paramref name="bootstrapJson"/> is the same
        /// payload /admin/api/stats returns, embedded for the first paint;
        /// everything below the header is rendered by the inline script from it
        /// and then re-rendered every few seconds from a fresh fetch.
        /// </summary>
        internal static string Dashboard(string bootstrapJson)
        {
            return @"<!DOCTYPE html><html lang=""en""><head>
<meta charset=""utf-8""><meta name=""viewport"" content=""width=device-width, initial-scale=1"">
<meta name=""color-scheme"" content=""light dark""><meta name=""robots"" content=""noindex"">
<title>Operator console - Worlds Adrift Reborn</title>" + Style + @"</head>
<body><div class=""wrap"">
<div class=""topbar"">
  <div>
    <p class=""mark"">Worlds Adrift Reborn</p>
    <h1 id=""serverName"">&hellip;</h1>
    <div class=""asof"" id=""asof"">Loading&hellip;</div>
  </div>
  <form method=""post"" action=""/admin/logout""><button class=""btn ghost"" type=""submit"">Sign out</button></form>
</div>

<div class=""banner"" id=""spiralBanner""><strong>Wire-health warning</strong><span id=""spiralText""></span></div>
<div class=""banner"" id=""downBanner""><strong>Game server not reporting</strong><span id=""downText""></span></div>

<div class=""card"">
  <h2>Live game</h2>
  <div class=""grid"">
    <div class=""stat""><div class=""n"" id=""online"">&mdash;</div><div class=""l"">Online now</div></div>
    <div class=""stat""><div class=""n"" id=""peak"">&mdash;</div><div class=""l"">Peak since boot</div></div>
    <div class=""stat""><div class=""n"" id=""connects"">&mdash;</div><div class=""l"">Total connects</div></div>
    <div class=""stat""><div class=""n"" id=""disconnects"">&mdash;</div><div class=""l"">Total disconnects</div></div>
    <div class=""stat""><div class=""n"" id=""uptime"">&mdash;</div><div class=""l"">Uptime</div></div>
    <div class=""stat""><div class=""n"" id=""relay"">&mdash;</div><div class=""l"">Relay mode</div></div>
    <div class=""stat""><div class=""n"" id=""testIsland"">&mdash;</div><div class=""l"">Test island</div></div>
  </div>
  <div style=""overflow-x:auto;margin-top:1rem"">
  <table id=""playersTable"">
    <thead><tr>
      <th>Entity</th><th>Peer</th><th class=""num"">Connected for</th>
      <th class=""num"">RTT</th><th class=""num"">Loss</th><th class=""num"">In-flight</th><th>Wire</th>
    </tr></thead>
    <tbody id=""players""></tbody>
  </table>
  </div>
  <p class=""muted"" id=""noPlayers"" style=""display:none;font-size:.85rem"">Nobody in world.</p>
</div>

<div class=""card"">
  <div class=""row"">
    <div class=""grow""><h2>Operator tools</h2></div>
    <div class=""fit""><button class=""btn ghost"" type=""button"" id=""refreshNow"">Refresh now</button></div>
  </div>
  <p class=""lede"" style=""margin-top:0"">Allowlisted game actions only. Every accepted or rejected request is recorded below; no shell commands or raw file paths are exposed.</p>
  <div class=""field"">
    <label for=""targetPlayer"">Selected live player</label>
    <select id=""targetPlayer""><option value="""">No connected player</option></select>
  </div>
  <div class=""tool-grid"">
    <div class=""tool"">
      <h3>Player travel</h3>
      <p>Return the selected player to safe Haven, or send them to the PR3 test island when its terrain is registered.</p>
      <div class=""button-row"">
        <button type=""button"" data-command=""teleport"" data-argument=""haven"">Return to Haven</button>
        <button type=""button"" id=""tradesTravel"" data-command=""teleport"" data-argument=""trades-challenge"">Trades Challenge</button>
      </div>
      <p id=""islandRequirement"">Trades Challenge requires <code>WAREBORN_SPAWN_SECOND_ISLAND=1</code>.</p>
    </div>
    <div class=""tool"">
      <h3>Placement recovery</h3>
      <p>Starts native placement preview for the selected player's first hotbar or bag deployable. Nothing is consumed unless the player confirms in-game.</p>
      <button type=""button"" data-command=""placement"" data-argument=""first"">Start deployable preview</button>
    </div>
    <div class=""tool"">
      <h3>Ship carry diagnostic</h3>
      <p>Moves the active test ship by exactly one metre. This is shared-world motion and asks for confirmation.</p>
      <div class=""button-row"">
        <button type=""button"" data-command=""ship-nudge"" data-argument=""north"">North +1 m</button>
        <button type=""button"" data-command=""ship-nudge"" data-argument=""south"">South -1 m</button>
        <button type=""button"" data-command=""ship-nudge"" data-argument=""west"">West -1 m</button>
        <button type=""button"" data-command=""ship-nudge"" data-argument=""east"">East +1 m</button>
      </div>
    </div>
  </div>
  <div class=""feedback"" id=""commandFeedback"" role=""status"" aria-live=""polite""></div>
  <div style=""overflow-x:auto;margin-top:1rem"">
    <table><thead><tr><th>When</th><th>Action</th><th>Target</th><th>Detail</th><th>Result</th></tr></thead><tbody id=""commandLog""></tbody></table>
  </div>
  <p class=""muted"" id=""noCommands"" style=""font-size:.85rem"">No operator actions since this login-server boot.</p>
</div>

<div class=""card"">
  <h2>Accounts</h2>
  <div class=""grid"">
    <div class=""stat""><div class=""n"" id=""acctTotal"">&mdash;</div><div class=""l"">Total signups</div></div>
    <div class=""stat""><div class=""n"" id=""acctToday"">&mdash;</div><div class=""l"">Signups today</div></div>
    <div class=""stat""><div class=""n"" id=""charTotal"">&mdash;</div><div class=""l"">Characters</div></div>
  </div>
  <div style=""overflow-x:auto;margin-top:1rem"">
  <table id=""recentTable"">
    <thead><tr><th>Recent signups</th><th>When</th><th class=""num"">Characters</th></tr></thead>
    <tbody id=""recent""></tbody>
  </table>
  </div>
  <p class=""muted"" id=""acctErr"" style=""display:none;font-size:.85rem""></p>
</div>

<div class=""card"">
  <h2>Server name</h2>
  <p class=""lede"" style=""margin-top:0"">The name the in-game server browser shows for this deployment.</p>
  <form method=""post"" action=""/admin/server-name"" class=""row"">
    <div class=""field grow"">
      <label for=""server-name-input"">Display name</label>
      <input id=""server-name-input"" name=""serverName"" type=""text"" maxlength=""64"" value="""">
    </div>
    <div class=""fit"" style=""flex:0 0 12rem""><button type=""submit"">Save name</button></div>
  </form>
</div>

<footer>Operator console. Auto-refreshes every few seconds. Not affiliated with Bossa Studios.</footer>
</div>
<script id=""bootstrap"" type=""application/json"">" + bootstrapJson + @"</script>
<script>
(function(){
  'use strict';
  var REFRESH_MS = 4000;
  var gameReporting = false;
  var secondIslandRegistered = false;
  function $(id){return document.getElementById(id);}
  function text(id,v){var e=$(id);if(e)e.textContent=v;}

  function fmtDur(sec){
    sec=Math.max(0,Math.floor(sec));
    var d=Math.floor(sec/86400);sec-=d*86400;
    var h=Math.floor(sec/3600);sec-=h*3600;
    var m=Math.floor(sec/60);var s=sec-m*60;
    if(d>0)return d+'d '+h+'h';
    if(h>0)return h+'h '+m+'m';
    if(m>0)return m+'m '+s+'s';
    return s+'s';
  }
  function fmtWhen(ms){
    try{var dt=new Date(ms);return dt.toLocaleString();}catch(e){return String(ms);}
  }
  function clear(el){while(el.firstChild)el.removeChild(el.firstChild);}
  function cell(row,val,cls){var td=document.createElement('td');if(cls)td.className=cls;td.textContent=val;row.appendChild(td);return td;}

  function render(data){
    if(!data){return;}
    text('serverName',data.serverName||'(unnamed server)');
    var input=$('server-name-input');
    if(input && document.activeElement!==input && !input.dataset.touched){input.value=data.serverName||'';}

    var g=data.game||{};
    var down=$('downBanner');
    var reporting=g.reporting===true;
    if(!reporting){
      down.classList.add('show');
      text('downText', g.state==='unreadable'
        ? 'A stats file is present but could not be read.'
        : 'No stats file yet. The game server may be down or was just started.');
    } else if(g.stale){
      down.classList.add('show');
      text('downText','Last update was '+Math.round(g.ageSeconds)+'s ago - the game server may have stalled.');
    } else {
      down.classList.remove('show');
    }

    text('asof', reporting
      ? ('Live game as of '+Math.round(g.ageSeconds||0)+'s ago'+(g.build&&g.build!=='unknown'?' · build '+g.build:''))
      : 'Game server not reporting');

    text('online', reporting?String(g.currentOnline):'—');
    text('peak', reporting?String(g.peakOnline):'—');
    text('connects', reporting?String(g.totalConnects):'—');
    text('disconnects', reporting?String(g.totalDisconnects):'—');
    text('uptime', reporting?fmtDur(g.uptimeSeconds):'—');
    text('relay', reporting?(g.relayMode||'—'):'—');
    gameReporting=reporting && !g.stale;
    secondIslandRegistered=gameReporting && g.secondIslandRegistered===true;
    text('testIsland',reporting?(secondIslandRegistered?'ready':'off'):'—');

    var spiral=$('spiralBanner');
    if(reporting && g.wireHealthWarning){
      spiral.classList.add('show','spiral');
      var bad=(g.players||[]).filter(function(p){return p.spiral;}).map(function(p){return 'entity '+p.entityId+' ('+p.rttMs+'ms)';});
      text('spiralText','Round-trip time is spiralling for '+bad.join(', ')+'. This is the shape of the reliable-relay backlog that silently drops a player - act before it times out.');
    } else {
      spiral.classList.remove('show','spiral');
    }

    var tbody=$('players');clear(tbody);
    var players=(reporting?g.players:[])||[];
    $('noPlayers').style.display=players.length?'none':'block';
    players.forEach(function(p){
      var tr=document.createElement('tr');
      cell(tr,p.entityId);
      cell(tr,p.peerId,'muted');
      cell(tr,fmtDur(p.connectedForSeconds),'num');
      if(p.hasHealth){
        cell(tr,p.rttMs+'ms','num');
        cell(tr,p.packetsLost+'/'+p.packetsSent,'num');
        cell(tr,p.inFlightBytes+'B','num');
        var w=document.createElement('td');
        var pill=document.createElement('span');
        pill.className='pill '+(p.spiral?'bad':'ok');
        pill.textContent=p.spiral?'spiral':'ok';
        w.appendChild(pill);tr.appendChild(w);
      } else {
        cell(tr,'—','num');cell(tr,'—','num');cell(tr,'—','num');
        cell(tr,'unreadable','muted');
      }
      tbody.appendChild(tr);
    });

    var playerSelect=$('targetPlayer');
    var selected=playerSelect.value;
    clear(playerSelect);
    var none=document.createElement('option');none.value='';none.textContent=players.length?'Select a player':'No connected player';playerSelect.appendChild(none);
    players.forEach(function(p){var o=document.createElement('option');o.value=String(p.entityId);o.textContent='Entity '+p.entityId+' · peer '+p.peerId;playerSelect.appendChild(o);});
    if(players.some(function(p){return String(p.entityId)===selected;})){playerSelect.value=selected;}
    else if(players.length===1){playerSelect.value=String(players[0].entityId);}

    var trades=$('tradesTravel');
    trades.disabled=!secondIslandRegistered;
    text('islandRequirement',secondIslandRegistered
      ? 'Terrain registration is confirmed by the live game server.'
      : 'Unavailable: requires WAREBORN_SPAWN_SECOND_ISLAND=1 and a fresh game-server report.');

    var recent=((data.commands||{}).recent)||[];
    var log=$('commandLog');clear(log);
    $('noCommands').style.display=recent.length?'none':'block';
    recent.forEach(function(c){
      var tr=document.createElement('tr');
      cell(tr,fmtWhen(c.atUnixMs),'muted');cell(tr,c.action);cell(tr,c.targetEntityId||'—','num');cell(tr,c.detail||'—');
      var td=cell(tr,c.message||'',c.accepted?'':'muted');
      var pill=document.createElement('span');pill.className='pill '+(c.accepted?'ok':'bad');pill.textContent=c.accepted?'queued':'rejected';
      td.insertBefore(pill,td.firstChild);td.insertBefore(document.createTextNode(' '),pill.nextSibling);log.appendChild(tr);
    });

    var a=data.accounts||{};
    if(a.available===false){
      $('acctErr').style.display='block';
      text('acctErr','The account database is not reachable right now.');
      text('acctTotal','—');text('acctToday','—');text('charTotal','—');
    } else {
      $('acctErr').style.display='none';
      text('acctTotal',String(a.total));
      text('acctToday',String(a.today));
      text('charTotal',String(a.characters));
      var rb=$('recent');clear(rb);
      (a.recent||[]).forEach(function(r){
        var tr=document.createElement('tr');
        cell(tr,r.username);
        cell(tr,fmtWhen(r.createdAtUnixMs),'muted');
        cell(tr,r.characters,'num');
        rb.appendChild(tr);
      });
    }
  }

  var input=$('server-name-input');
  if(input){input.addEventListener('input',function(){input.dataset.touched='1';});}

  function boot(){
    try{render(JSON.parse($('bootstrap').textContent));}catch(e){}
  }
  function refresh(){
    fetch('/admin/api/stats',{headers:{'Accept':'application/json'},credentials:'same-origin'})
      .then(function(r){if(r.status===401){location.href='/admin';return null;}return r.ok?r.json():null;})
      .then(function(d){if(d)render(d);})
      .catch(function(){});
  }
  function sendCommand(action,argument,button){
    var target=$('targetPlayer').value;
    if((action==='teleport'||action==='placement')&&!target){showFeedback(false,'Select a connected player first.');return;}
    if(!gameReporting&&action!=='ship-nudge'){showFeedback(false,'The game server is not reporting fresh status.');return;}
    if(action==='ship-nudge'&&!window.confirm('Move the active shared ship exactly one metre '+argument+'?'))return;
    var body='action='+encodeURIComponent(action)+'&target='+encodeURIComponent(target)+'&argument='+encodeURIComponent(argument||'');
    button.disabled=true;
    fetch('/admin/api/command',{method:'POST',credentials:'same-origin',headers:{'Accept':'application/json','Content-Type':'application/x-www-form-urlencoded','X-Wareborn-Admin':'1'},body:body})
      .then(function(r){if(r.status===401){location.href='/admin';return null;}return r.json().then(function(j){return {ok:r.ok,data:j};});})
      .then(function(result){if(result){showFeedback(result.ok,result.data.message||'Command request finished.');refresh();}})
      .catch(function(){showFeedback(false,'The admin command request could not reach the login server.');})
      .then(function(){button.disabled=false;if(button.id==='tradesTravel'&&!secondIslandRegistered)button.disabled=true;});
  }
  function showFeedback(ok,message){var e=$('commandFeedback');e.className='feedback show'+(ok?'':' bad');e.textContent=message;}
  Array.prototype.forEach.call(document.querySelectorAll('[data-command]'),function(button){button.addEventListener('click',function(){sendCommand(button.dataset.command,button.dataset.argument,button);});});
  $('refreshNow').addEventListener('click',refresh);
  boot();
  refresh();
  setInterval(refresh,REFRESH_MS);
})();
</script>
</body></html>";
        }

        /// <summary>Minimal HTML entity escaping for text interpolated into markup.</summary>
        internal static string HtmlEncode(string value)
        {
            StringBuilder b = new StringBuilder(value.Length + 16);
            foreach (char c in value)
            {
                switch (c)
                {
                    case '&': b.Append("&amp;"); break;
                    case '<': b.Append("&lt;"); break;
                    case '>': b.Append("&gt;"); break;
                    case '"': b.Append("&quot;"); break;
                    case '\'': b.Append("&#39;"); break;
                    default: b.Append(c); break;
                }
            }
            return b.ToString();
        }
    }
}
