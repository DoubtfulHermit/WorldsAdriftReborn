using System.Text;

namespace WorldsAdriftServer.Web
{
    /// <summary>
    /// The operator dashboard's HTML, served verbatim like <see cref="SignupPage"/>
    /// as a restrained simulation operations console. Its typography, neutral
    /// controls, telemetry surfaces and danger treatment are all self-contained:
    /// no external CSS, fonts, scripts or images.
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

        /// <summary>Self-contained, high-density simulation-console design system.</summary>
        private const string Style = @"<style>
:root{
  color-scheme:dark;
  --bg:#091016;--bg-raised:#0e171f;--surface:#111c25;--surface-2:#16232d;
  --line:#263641;--line-strong:#38505d;--text:#edf3f5;--text-soft:#aab9c0;--text-faint:#71838d;
  --accent:#74c9cf;--accent-soft:rgba(116,201,207,.12);--good:#71d0a5;--warn:#d9b36b;--danger:#f08080;
  --danger-soft:rgba(240,128,128,.09);--shadow:0 22px 60px rgba(0,0,0,.24);
}
*{box-sizing:border-box;}
html{scroll-behavior:smooth;}
body{margin:0;min-height:100vh;padding:0 1.5rem 4rem;color:var(--text);background:
  radial-gradient(70rem 32rem at 15% -12%,rgba(54,111,126,.24),transparent 60%),
  radial-gradient(45rem 28rem at 100% 8%,rgba(47,76,97,.18),transparent 65%),var(--bg);
  font-family:Inter,ui-sans-serif,-apple-system,BlinkMacSystemFont,'Segoe UI',sans-serif;font-size:15px;line-height:1.5;
  font-variant-numeric:tabular-nums;-webkit-font-smoothing:antialiased;}
a{color:inherit;}
.wrap{max-width:74rem;margin:0 auto;}
.mark{font-size:.67rem;font-weight:650;letter-spacing:.26em;text-transform:uppercase;color:var(--accent);margin:0 0 .55rem;}
h1{font-size:clamp(1.6rem,4vw,2.25rem);font-weight:540;letter-spacing:-.025em;margin:0 0 .25rem;}
h2{font-size:.72rem;font-weight:700;letter-spacing:.16em;text-transform:uppercase;color:var(--text-soft);margin:0 0 1rem;}
.lede{max-width:52rem;margin:.15rem 0 1.5rem;color:var(--text-soft);font-size:.87rem;}
.card{position:relative;background:linear-gradient(145deg,rgba(19,31,40,.96),rgba(13,23,31,.96));border:1px solid var(--line);
  border-radius:12px;padding:1.55rem 1.65rem;margin:0 0 1.25rem;box-shadow:var(--shadow);}
.grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(8.5rem,1fr));gap:1px;background:var(--line);border:1px solid var(--line);border-radius:8px;overflow:hidden;}
.stat{padding:1rem 1.05rem;background:var(--surface);min-height:5.35rem;}
.stat .n{font-size:1.5rem;font-weight:590;letter-spacing:-.025em;color:var(--text);}
.stat .l{margin-top:.18rem;font-size:.61rem;font-weight:650;letter-spacing:.13em;text-transform:uppercase;color:var(--text-faint);}
label{display:block;font-size:.62rem;font-weight:700;letter-spacing:.12em;text-transform:uppercase;color:var(--text-soft);margin:0 0 .45rem;}
input,select{width:100%;min-height:2.65rem;font:inherit;color:var(--text);background:#0b141b;border:1px solid var(--line-strong);border-radius:7px;padding:.58rem .72rem;}
input::placeholder{color:var(--text-faint);}select option{color:var(--text);background:var(--surface);}
input:hover,select:hover{border-color:#4c6876;}input:focus-visible,select:focus-visible,button:focus-visible,a:focus-visible{outline:2px solid var(--accent);outline-offset:2px;}
.field{margin-bottom:1rem;text-align:left;}
button,.btn{display:inline-flex;align-items:center;justify-content:center;gap:.45rem;width:auto;min-height:2.55rem;margin:0;padding:.6rem .95rem;
  font:inherit;font-size:.72rem;font-weight:680;letter-spacing:.045em;color:var(--text);border:1px solid var(--line-strong);border-radius:7px;
  cursor:pointer;text-align:center;text-decoration:none;background:linear-gradient(180deg,#20313d,#172630);box-shadow:0 1px 0 rgba(255,255,255,.04);transition:border-color .14s,background .14s,transform .14s;}
button:hover:not(:disabled),.btn:hover{border-color:#567583;background:linear-gradient(180deg,#293d49,#1c2e38);}
button:active:not(:disabled){transform:translateY(1px);}button:disabled{opacity:.45;cursor:not-allowed;}
.btn.ghost{background:transparent;color:var(--text-soft);border-color:var(--line);padding:.48rem .8rem;}
.btn.ghost:hover{color:var(--text);background:var(--surface-2);}
.danger-button{color:#ffd9d9;border-color:rgba(240,128,128,.5);background:rgba(115,37,42,.34);}
.danger-button:hover:not(:disabled){border-color:var(--danger);background:rgba(139,43,49,.48);}
.err{display:block;margin:.65rem 0;color:var(--danger);font-weight:550;font-size:.82rem;}
.row{display:flex;flex-wrap:wrap;gap:.75rem;align-items:flex-end;}
.row .field{flex:1 1 18rem;margin-bottom:0;}
.row .grow{flex:1 1 18rem;}
.row .fit{flex:0 0 auto;}
table{width:100%;border-collapse:collapse;font-size:.79rem;}
th,td{text-align:left;padding:.68rem .6rem;border-bottom:1px solid var(--line);}
th{font-size:.58rem;font-weight:700;letter-spacing:.12em;text-transform:uppercase;color:var(--text-faint);}
tbody tr:hover{background:rgba(116,201,207,.035);}
td.num,th.num{text-align:right;font-variant-numeric:tabular-nums;}
.muted{color:var(--text-faint);}
.pill{display:inline-flex;align-items:center;gap:.3rem;padding:.18rem .52rem;border-radius:999px;font-size:.59rem;font-weight:720;letter-spacing:.075em;text-transform:uppercase;border:1px solid var(--line-strong);background:rgba(255,255,255,.025);}
.pill.ok{color:var(--good);border-color:rgba(113,208,165,.42);background:rgba(113,208,165,.06);}
.pill.bad{color:var(--danger);border-color:rgba(240,128,128,.5);background:var(--danger-soft);}
.pill.warn{color:var(--warn);border-color:rgba(217,179,107,.4);background:rgba(217,179,107,.06);}
.banner{display:none;margin:0 0 1.2rem;padding:.9rem 1rem;border:1px solid rgba(217,179,107,.32);background:rgba(93,71,29,.14);border-radius:8px;font-size:.84rem;}
.banner.show{display:block;}
.banner.spiral{border-color:rgba(240,128,128,.38);background:var(--danger-soft);}
.banner strong{display:block;font-weight:680;margin-bottom:.15rem;}
.topbar{display:flex;justify-content:space-between;align-items:center;gap:1rem;flex-wrap:wrap;padding:2.2rem 0 1.25rem;}
.asof{font-size:.72rem;color:var(--text-faint);}
.nav{position:sticky;z-index:10;top:0;display:flex;gap:.25rem;margin:0 0 2.2rem;padding:.55rem;background:rgba(9,16,22,.88);border:1px solid var(--line);border-radius:10px;backdrop-filter:blur(16px);}
.nav a{flex:0 1 9rem;padding:.58rem .85rem;border-radius:6px;text-decoration:none;text-align:center;font-size:.62rem;font-weight:680;letter-spacing:.1em;text-transform:uppercase;color:var(--text-soft);}
.nav a:hover{color:var(--text);background:var(--surface-2);}
.section-head{display:flex;align-items:center;gap:.7rem;font-size:.64rem;font-weight:750;letter-spacing:.22em;text-transform:uppercase;color:var(--accent);margin:2.6rem 0 .75rem;scroll-margin-top:5rem;}
.section-head:after{content:'';height:1px;flex:1;background:linear-gradient(90deg,var(--line-strong),transparent);}
.selectors{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:1rem;padding:1rem;background:#0c151c;border:1px solid var(--line);border-radius:8px;margin-bottom:1rem;}
.selectors .field{margin:0;}
.tool-grid{display:grid;grid-template-columns:repeat(12,minmax(0,1fr));gap:1px;background:var(--line);border:1px solid var(--line);border-radius:9px;overflow:hidden;margin-top:1rem;}
.tool{grid-column:span 6;background:var(--surface);padding:1.2rem 1.25rem;min-width:0;}
.tool h3{font-size:.8rem;font-weight:680;letter-spacing:.02em;margin:0 0 .3rem;}
.tool p{font-size:.76rem;color:var(--text-faint);margin:.2rem 0 .85rem;max-width:42rem;}
.tool .field{margin:.8rem 0;}.tool.danger-zone{grid-column:1/-1;background:linear-gradient(90deg,var(--danger-soft),var(--surface) 55%);border-top:1px solid rgba(240,128,128,.25);}
.button-row{display:flex;flex-wrap:wrap;gap:.5rem;}.button-row button{font-size:.68rem;}
.nudge-pad{display:grid;grid-template:repeat(3,2.5rem)/repeat(3,2.5rem);gap:.3rem;width:max-content;margin-top:.7rem;}
.nudge-pad button{width:2.5rem;min-height:2.5rem;padding:0;font-size:0;}
.nudge-pad button:after{font-size:1rem;line-height:1;}.nudge-pad [data-argument=north]{grid-area:1/2}.nudge-pad [data-argument=south]{grid-area:3/2}.nudge-pad [data-argument=west]{grid-area:2/1}.nudge-pad [data-argument=east]{grid-area:2/3}
.nudge-pad [data-argument=north]:after{content:'\2191'}.nudge-pad [data-argument=south]:after{content:'\2193'}.nudge-pad [data-argument=west]:after{content:'\2190'}.nudge-pad [data-argument=east]:after{content:'\2192'}
.nudge-origin{grid-area:2/2;border:1px solid var(--line);border-radius:50%;margin:.72rem;background:var(--accent);box-shadow:0 0 12px rgba(116,201,207,.5);}
.feedback{display:none;margin-top:1rem;padding:.8rem .9rem;border:1px solid rgba(113,208,165,.35);border-radius:7px;background:rgba(113,208,165,.07);font-size:.8rem;}
.feedback.show{display:block}.feedback.bad{border-color:rgba(240,128,128,.4);background:var(--danger-soft);}
.receipt{margin-top:1rem;padding:1rem 1.1rem;border:1px solid var(--line);border-radius:8px;background:#0c151c;}
.receipt h3{font-size:.68rem;text-transform:uppercase;letter-spacing:.1em;color:var(--text-faint);margin:0 0 .55rem;}
.receipt p{font-size:.78rem;margin:.35rem 0;}
.domain-grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(20rem,1fr));gap:.7rem;}
.domain{position:relative;border:1px solid var(--line);border-radius:9px;padding:1rem 1.05rem;min-width:0;background:#0c151c;overflow:hidden;}
.domain:before{content:'';position:absolute;inset:0 auto 0 0;width:2px;background:var(--accent);opacity:.6;}
.domain.has-warning:before{background:var(--danger);opacity:1}.domain.is-resting:before{background:var(--text-faint);opacity:.45;}
.domain-head{display:flex;justify-content:space-between;gap:.7rem;align-items:center;margin-bottom:.85rem;}
.domain h3{font-size:.86rem;font-weight:640;letter-spacing:.01em;margin:0;overflow-wrap:anywhere;}
.kv{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:.72rem;font-size:.74rem;color:var(--text-soft);}
.kv div{min-width:0;overflow-wrap:anywhere}.kv b{display:block;color:var(--text-faint);font-size:.54rem;font-weight:680;letter-spacing:.09em;text-transform:uppercase;margin-bottom:.12rem;}
footer{margin-top:2.5rem;padding-top:1.25rem;border-top:1px solid var(--line);font-size:.68rem;color:var(--text-faint);}
@media(max-width:760px){body{padding:0 .8rem 3rem}.card{padding:1.15rem}.topbar{padding-top:1.35rem}.nav{overflow-x:auto;justify-content:flex-start}.nav a{flex:0 0 auto}.tool{grid-column:1/-1}.selectors{grid-template-columns:1fr}.kv{grid-template-columns:repeat(2,minmax(0,1fr))}.domain-grid{grid-template-columns:1fr}.row .fit{width:100%}.row .fit button{width:100%}}
@media(max-width:430px){.stat{min-height:4.7rem;padding:.8rem}.kv{grid-template-columns:1fr 1fr}th,td{padding:.58rem .45rem}.button-row button{width:100%}}
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
        internal static string Dashboard(string bootstrapJson, string csrfToken)
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
  <form method=""post"" action=""/admin/logout""><input type=""hidden"" name=""csrf"" value=""" + HtmlEncode(csrfToken) + @"""><button class=""btn ghost"" type=""submit"">Sign out</button></form>
</div>
<nav class=""nav"" aria-label=""Control panel sections""><a href=""#world"">World</a><a href=""#simulation"">Simulation</a><a href=""#operations"">Operations</a><a href=""#system"">System</a></nav>

<div class=""banner"" id=""spiralBanner""><strong>Wire-health warning</strong><span id=""spiralText""></span></div>
<div class=""banner"" id=""downBanner""><strong>Game server not reporting</strong><span id=""downText""></span></div>

<div class=""section-head"" id=""world"">World</div>
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

<div class=""section-head"" id=""simulation"">Simulation</div>
<div class=""card"">
  <div class=""row""><div class=""grow""><h2>World inspector</h2></div><div class=""fit""><span class=""pill ok"" id=""hostMode"">local single-process</span></div></div>
  <p class=""lede"" style=""margin-top:0"">Read-only ship-domain state from the authoritative game loop. There are no remote workers or migrations in this runtime.</p>
  <div class=""grid"" style=""margin-bottom:1rem""><div class=""stat""><div class=""n"" id=""domainCount"">&mdash;</div><div class=""l"">Ship domains</div></div><div class=""stat""><div class=""n"" id=""activeDomains"">&mdash;</div><div class=""l"">Active simulation</div></div><div class=""stat""><div class=""n"" id=""aboardCount"">&mdash;</div><div class=""l"">Players aboard</div></div></div>
  <div class=""banner"" id=""domainWarning""><strong>Domain delivery warning</strong><span id=""domainWarningText""></span></div>
  <div class=""domain-grid"" id=""shipDomains""></div>
  <p class=""muted"" id=""noDomains"" style=""display:none;font-size:.85rem"">No ship domains are registered.</p>
  <p class=""muted"" style=""font-size:.72rem;margin-bottom:0"">Frame alignment: hull and members are emitted root-first under one generation/sequence. Client-rendered player-to-ship offset is not yet measured by this bridge, so the panel does not claim a player presentation tick.</p>
</div>

<div class=""section-head"" id=""operations"">Operations</div>
<div class=""card"">
  <div class=""row"">
    <div class=""grow""><h2>Recovery and control</h2></div>
    <div class=""fit""><button class=""btn ghost"" type=""button"" id=""refreshNow"">Refresh now</button></div>
  </div>
  <p class=""lede"" style=""margin-top:0"">Targeted, allowlisted recovery actions. Dispatch and game completion are recorded independently.</p>
  <div class=""selectors"">
  <div class=""field"">
    <label for=""targetPlayer"">Selected live player</label>
    <select id=""targetPlayer""><option value="""">No connected player</option></select>
  </div>
  <div class=""field"">
    <label for=""targetShip"">Selected exact ship domain</label>
    <select id=""targetShip""><option value="""">No registered ship domain</option></select>
  </div>
  </div>
  <div class=""tool-grid"">
    <div class=""tool"">
      <h3>Player travel</h3>
      <p>Move the selected player to an authored safe destination.</p>
      <div class=""button-row"">
        <button type=""button"" data-command=""teleport"" data-argument=""haven"">Return to Haven</button>
        <button type=""button"" id=""tradesTravel"" data-command=""teleport"" data-argument=""trades-challenge"">Trades Challenge</button>
      </div>
      <p id=""islandRequirement"">Trades Challenge requires <code>WAREBORN_SPAWN_SECOND_ISLAND=1</code>.</p>
    </div>
    <div class=""tool"">
      <h3>Placement recovery</h3>
      <p>Reopen native placement for the first available deployable. Inventory is consumed only on in-game confirmation.</p>
      <button type=""button"" data-command=""placement"" data-argument=""first"">Start deployable preview</button>
    </div>
    <div class=""tool"">
      <h3>Ship position trim</h3>
      <p>Nudge the active diagnostic ship one metre on the world plane.</p>
      <div class=""nudge-pad"" role=""group"" aria-label=""Ship position trim, one metre""><span class=""nudge-origin"" aria-hidden=""true""></span>
        <button type=""button"" title=""North, one metre"" aria-label=""Nudge ship north one metre"" data-command=""ship-nudge"" data-argument=""north"">North +1 m</button>
        <button type=""button"" title=""South, one metre"" aria-label=""Nudge ship south one metre"" data-command=""ship-nudge"" data-argument=""south"">South -1 m</button>
        <button type=""button"" title=""West, one metre"" aria-label=""Nudge ship west one metre"" data-command=""ship-nudge"" data-argument=""west"">West -1 m</button>
        <button type=""button"" title=""East, one metre"" aria-label=""Nudge ship east one metre"" data-command=""ship-nudge"" data-argument=""east"">East +1 m</button>
      </div>
    </div>
    <div class=""tool"">
      <h3>World resources</h3>
      <p>Restore all gatherable nodes to their authored state across the shared world.</p>
      <button type=""button"" data-command=""resources-reset"" data-argument=""all"">Reset all resource nodes</button>
    </div>
    <div class=""tool"">
      <h3>Exact ship recovery</h3>
      <p>Place the selected uncrewed hull at a safe clearance beside the selected player.</p>
      <button type=""button"" data-command=""ship-recall"" data-target=""ship"">Recall selected ship</button>
    </div>
    <div class=""tool danger-zone"">
      <h3>Permanent ship deletion</h3>
      <p>Remove the selected hull and its persistent structure. Irreversible.</p>
      <div class=""field""><label for=""deleteConfirmation"">Type DELETE</label><input id=""deleteConfirmation"" autocomplete=""off"" spellcheck=""false"" placeholder=""DELETE""></div>
      <button class=""danger-button"" type=""button"" data-command=""ship-delete"" data-target=""ship"">Delete selected ship permanently</button>
    </div>
  </div>
  <div class=""feedback"" id=""commandFeedback"" role=""status"" aria-live=""polite""></div>
  <div class=""receipt""><h3>Latest game-server completion</h3><p id=""completionEmpty"">No completed world operation has been reported yet.</p><div id=""completionReceipt"" style=""display:none""><span class=""pill"" id=""completionStatus""></span> <strong id=""completionAction""></strong><p id=""completionMessage""></p><p class=""muted"" id=""completionWhen""></p></div></div>
  <div style=""overflow-x:auto;margin-top:1rem"">
    <table><thead><tr><th>When</th><th>Action</th><th>Target</th><th>Detail</th><th>Result</th></tr></thead><tbody id=""commandLog""></tbody></table>
  </div>
  <p class=""muted"" id=""noCommands"" style=""font-size:.85rem"">No operator actions since this login-server boot.</p>
</div>

<div class=""section-head"" id=""system"">System</div>
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
  <form method=""post"" action=""/admin/server-name"" class=""row""><input type=""hidden"" name=""csrf"" value=""" + HtmlEncode(csrfToken) + @""">
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
  var CSRF = '" + csrfToken + @"';
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

    var runtime=g.runtime||{};
    text('hostMode',runtime.hostMode==='local-single-process'?'local single-process':(runtime.hostMode||'unknown host'));
    var domains=runtime.shipDomains||[];
    text('domainCount',reporting?String(domains.length):'—');
    text('activeDomains',reporting?String(domains.filter(function(d){return d.active;}).length):'—');
    text('aboardCount',reporting?String(domains.reduce(function(n,d){return n+(d.aboardPlayerEntityIds||[]).length;},0)):'—');
    var domainGrid=$('shipDomains');clear(domainGrid);
    $('noDomains').style.display=domains.length?'none':'block';
    var warnings=[];
    domains.forEach(function(d){
      var box=document.createElement('div');
      var head=document.createElement('div');head.className='domain-head';
      var title=document.createElement('h3');title.textContent=d.domainId||('ship:'+d.hullEntityId);head.appendChild(title);
      var status=document.createElement('span');
      var bad=d.staleDelivery||d.aboardCheckoutWarning;
      box.className='domain '+(bad?'has-warning':(d.active?'is-active':'is-resting'));
      status.className='pill '+(bad?'bad':(d.active?'ok':'warn'));
      status.textContent=bad?'warning':(d.piloted?'piloted':(d.active?'active':'resting'));
      head.appendChild(status);box.appendChild(head);
      var kv=document.createElement('div');kv.className='kv';
      function item(label,value){var e=document.createElement('div');var b=document.createElement('b');b.textContent=label;e.appendChild(b);e.appendChild(document.createTextNode(value));kv.appendChild(e);}
      item('Hull',String(d.hullEntityId));
      item('Authority','local · gen '+d.authorityGeneration);
      item('Replication','seq '+d.replicationSequence+' · '+d.cadenceMs+'ms target');
      item('Last delivery',d.deliveryAgeMs<0?'never':d.deliveryAgeMs+'ms ago');
      item('Pose',Number(d.x).toFixed(1)+', '+Number(d.y).toFixed(1)+', '+Number(d.z).toFixed(1));
      item('Pilot',d.pilotPlayerEntityId==null?'none':'entity '+d.pilotPlayerEntityId);
      item('Aboard',(d.aboardPlayerEntityIds||[]).length?(d.aboardPlayerEntityIds||[]).join(', '):'none');
      item('Members',d.deckCount+' decks · '+d.mountedPartCount+' mounted');
      item('Subscribers',String(d.subscriberCount));
      item('Cadence expected',d.liveCadenceExpected?'live':'rest keepalive');
      box.appendChild(kv);domainGrid.appendChild(box);
      if(d.staleDelivery)warnings.push((d.domainId||d.hullEntityId)+' has stale/no replication while live cadence is expected');
      if(d.aboardCheckoutWarning)warnings.push((d.domainId||d.hullEntityId)+' has more aboard players than checked-out subscribers');
    });
    var domainWarning=$('domainWarning');
    if(warnings.length){domainWarning.classList.add('show','spiral');text('domainWarningText',warnings.join('; ')+'.');}
    else{domainWarning.classList.remove('show','spiral');}

    var shipSelect=$('targetShip');
    var selectedShip=shipSelect.value;clear(shipSelect);
    var noShip=document.createElement('option');noShip.value='';noShip.textContent=domains.length?'Select an exact ship':'No registered ship domain';shipSelect.appendChild(noShip);
    domains.forEach(function(d){var o=document.createElement('option');o.value=String(d.hullEntityId);o.textContent=(d.domainId||'ship')+' · hull '+d.hullEntityId+' · '+(d.piloted?'piloted':'unpiloted');shipSelect.appendChild(o);});
    if(domains.some(function(d){return String(d.hullEntityId)===selectedShip;}))shipSelect.value=selectedShip;
    var confirmation=$('deleteConfirmation');
    if(confirmation && confirmation.dataset.ship!==shipSelect.value){confirmation.value='';confirmation.dataset.ship=shipSelect.value;confirmation.placeholder=shipSelect.value?'DELETE':'Select a ship first';}

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
    var completion=(data.commands||{}).latestCompletion;
    $('completionEmpty').style.display=completion?'none':'block';
    $('completionReceipt').style.display=completion?'block':'none';
    if(completion){
      var completionStatus=$('completionStatus');completionStatus.className='pill '+(completion.success?'ok':'bad');completionStatus.textContent=completion.success?'completed':'failed';
      text('completionAction',completion.action+(completion.targetEntityId?' · entity '+completion.targetEntityId:''));
      text('completionMessage',completion.message||'The game server supplied no detail.');
      text('completionWhen','Completed '+fmtWhen(completion.completedAtUnixMs)+'. This is gameplay completion, not merely queue acceptance.');
    } else if((data.commands||{}).completionState==='unreadable') {
      text('completionEmpty','The game-server result file is malformed or unreadable; check the server log.');
    } else {
      text('completionEmpty','No completed world operation has been reported yet.');
    }
    var log=$('commandLog');clear(log);
    $('noCommands').style.display=recent.length?'none':'block';
    recent.forEach(function(c){
      var tr=document.createElement('tr');
      cell(tr,fmtWhen(c.atUnixMs),'muted');cell(tr,c.action);cell(tr,c.targetEntityId||'—','num');cell(tr,c.detail||'—');
      var td=cell(tr,c.message||'',c.accepted?'':'muted');
      var pill=document.createElement('span');pill.className='pill '+(c.accepted?'ok':'bad');pill.textContent=c.accepted?'accepted':'rejected';
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
    var target=button.dataset.target==='ship'?$('targetShip').value:$('targetPlayer').value;
    if((action==='teleport'||action==='placement')&&!target){showFeedback(false,'Select a connected player first.');return;}
    if((action==='ship-recall'||action==='ship-delete')&&!target){showFeedback(false,'Select an exact ship domain first.');return;}
    if(action==='ship-recall'&&!$('targetPlayer').value){showFeedback(false,'Select the connected player who should receive the ship.');return;}
    if(!gameReporting&&action!=='ship-nudge'){showFeedback(false,'The game server is not reporting fresh status.');return;}
    if(action==='ship-nudge'&&!window.confirm('Move the active shared ship exactly one metre '+argument+'?'))return;
    if(action==='resources-reset'&&!window.confirm('Reset every shared-world resource node?'))return;
    if(action==='ship-recall'){
      argument=$('targetPlayer').value;
      if(!window.confirm('Recall exact hull '+target+' to player entity '+argument+'?'))return;
    }
    var confirmation='';
    if(action==='ship-delete'){
      confirmation=$('deleteConfirmation').value;
      if(confirmation!=='DELETE'){showFeedback(false,'Type DELETE exactly before deleting hull '+target+'.');return;}
      if(!window.confirm('Permanently delete exact hull '+target+' and its persisted structure? This cannot be undone.'))return;
    }
    var body='action='+encodeURIComponent(action)+'&target='+encodeURIComponent(target)+'&argument='+encodeURIComponent(argument||'')+'&confirmation='+encodeURIComponent(confirmation);
    button.disabled=true;
    fetch('/admin/api/command',{method:'POST',credentials:'same-origin',headers:{'Accept':'application/json','Content-Type':'application/x-www-form-urlencoded','X-Wareborn-Admin':'1','X-Wareborn-CSRF':CSRF},body:body})
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
