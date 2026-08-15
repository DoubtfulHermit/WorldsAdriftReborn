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
.wrap{max-width:92rem;margin:0 auto;}
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
.recovery-actions{display:flex;flex-wrap:wrap;gap:.5rem;margin-top:.8rem;}
.incident{margin-top:.85rem;padding:.72rem .8rem;border:1px solid var(--line);border-radius:7px;background:#0b141b;color:var(--text-soft);font:500 .68rem/1.55 ui-monospace,SFMono-Regular,Consolas,monospace;overflow-wrap:anywhere;}
.feedback{display:none;margin-top:1rem;padding:.8rem .9rem;border:1px solid rgba(113,208,165,.35);border-radius:7px;background:rgba(113,208,165,.07);font-size:.8rem;}
.feedback.show{display:block}.feedback.bad{border-color:rgba(240,128,128,.4);background:var(--danger-soft);}
.receipt{margin-top:1rem;padding:1rem 1.1rem;border:1px solid var(--line);border-radius:8px;background:#0c151c;}
.receipt h3{font-size:.68rem;text-transform:uppercase;letter-spacing:.1em;color:var(--text-faint);margin:0 0 .55rem;}
.receipt p{font-size:.78rem;margin:.35rem 0;}
.runtime-overview{display:grid;grid-template-columns:minmax(15rem,1.25fr) repeat(6,minmax(6.5rem,.62fr));gap:1px;background:var(--line);border:1px solid var(--line);border-radius:10px;overflow:hidden;margin-bottom:1rem;}
.host-summary{position:relative;padding:1.05rem 1.15rem;background:linear-gradient(135deg,#142631,#101b23);min-height:6rem;overflow:hidden;}
.host-summary:after{content:'';position:absolute;width:9rem;height:9rem;border:1px solid rgba(116,201,207,.12);border-radius:50%;right:-3.5rem;top:-4.5rem;box-shadow:0 0 0 1.6rem rgba(116,201,207,.025),0 0 0 3.2rem rgba(116,201,207,.018);}
.host-kicker{font-size:.56rem;font-weight:720;letter-spacing:.13em;text-transform:uppercase;color:var(--accent);}.host-name{font-size:1rem;font-weight:630;margin:.24rem 0 .12rem;}.host-meta{font-size:.68rem;color:var(--text-faint);}
.runtime-metric{padding:1rem;background:var(--surface);min-height:6rem;}.runtime-metric .n{font-size:1.35rem;font-weight:610;}.runtime-metric .l{font-size:.56rem;font-weight:700;letter-spacing:.11em;text-transform:uppercase;color:var(--text-faint);margin-top:.18rem;}
.topology{border:1px solid var(--line);border-radius:10px;background:#0a131a;overflow:hidden;margin-bottom:1rem;}
.topology-bar{display:flex;justify-content:space-between;align-items:center;gap:1rem;padding:.72rem .9rem;border-bottom:1px solid var(--line);background:rgba(22,35,45,.55);}.topology-bar strong{font-size:.68rem;letter-spacing:.08em;text-transform:uppercase;}.topology-legend{display:flex;flex-wrap:wrap;gap:.7rem;color:var(--text-faint);font-size:.61rem;}.legend-dot{display:inline-block;width:.45rem;height:.45rem;border-radius:50%;margin-right:.3rem;background:var(--accent)}.legend-dot.ship{background:#8aa6ff}.legend-dot.warn{background:var(--danger)}
.topology-canvas{position:relative;display:flex;flex-direction:column;gap:1rem;padding:2rem 1.25rem 1.25rem;min-height:13rem;background-image:linear-gradient(rgba(70,96,111,.075) 1px,transparent 1px),linear-gradient(90deg,rgba(70,96,111,.075) 1px,transparent 1px);background-size:24px 24px;}
.topology-canvas:before{content:'AUTHORITY DIRECTORY';position:absolute;top:.55rem;left:1.25rem;right:1.25rem;height:1px;background:linear-gradient(90deg,var(--accent),rgba(116,201,207,.08));color:var(--accent);font-size:.5rem;font-weight:750;letter-spacing:.16em;padding-top:.18rem;}
.host-cluster{position:relative;border:1px solid rgba(116,201,207,.18);border-radius:12px;padding:.85rem;background:rgba(7,15,20,.6)}.host-cluster-head{display:flex;justify-content:space-between;gap:1rem;align-items:center;padding:.15rem .25rem .75rem}.host-cluster-name{font:650 .66rem/1.4 ui-monospace,SFMono-Regular,Consolas,monospace;color:var(--accent)}.host-cluster-meta{font-size:.58rem;color:var(--text-faint)}.host-domain-grid{display:grid;grid-template-columns:repeat(auto-fit,minmax(17rem,1fr));gap:.75rem}
.island-node{position:relative;min-width:0;border:1px solid var(--line-strong);border-radius:10px;background:linear-gradient(145deg,rgba(18,34,43,.98),rgba(11,21,28,.98));padding:1rem;box-shadow:0 14px 34px rgba(0,0,0,.18);}.island-node:before{content:'';position:absolute;left:1.2rem;top:-1.45rem;width:1px;height:1.45rem;background:var(--accent);}.island-head{display:flex;align-items:flex-start;justify-content:space-between;gap:.75rem;padding-bottom:.75rem;border-bottom:1px solid var(--line);}.island-title{font-size:.88rem;font-weight:660;}.island-id{font:500 .6rem/1.4 ui-monospace,SFMono-Regular,Consolas,monospace;color:var(--text-faint);}.island-counts{font-size:.62rem;color:var(--text-soft);text-align:right;white-space:nowrap;}
.ship-lane{display:flex;flex-wrap:wrap;gap:.42rem;padding-top:.8rem;min-height:2.2rem;}.ship-node{position:relative;display:inline-flex;align-items:center;gap:.38rem;max-width:100%;padding:.38rem .52rem;border:1px solid #33495a;border-radius:6px;background:#101d27;color:var(--text-soft);font-size:.62rem;cursor:pointer;}.ship-node:hover{border-color:#698897;color:var(--text)}.ship-node:before{content:'';width:.38rem;height:.38rem;border-radius:50%;background:#8aa6ff;box-shadow:0 0 0 3px rgba(138,166,255,.08)}.ship-node.active:before{background:var(--good)}.ship-node.warning{border-color:rgba(240,128,128,.5);color:#ffd2d2}.ship-node.warning:before{background:var(--danger)}.ship-more{padding:.4rem .25rem;color:var(--text-faint);font-size:.6rem;}
.domain-workbench{display:grid;grid-template-columns:minmax(0,1fr) minmax(18rem,25rem);gap:1rem;align-items:start;}.domain-browser{min-width:0;border:1px solid var(--line);border-radius:10px;background:#0c151c;overflow:hidden;}.domain-toolbar{display:grid;grid-template-columns:minmax(12rem,1fr) auto;gap:.75rem;padding:.8rem;border-bottom:1px solid var(--line);}.domain-toolbar input{min-height:2.3rem}.segmented{display:flex;gap:2px;padding:3px;border:1px solid var(--line);border-radius:7px;background:#091219}.segmented button{min-height:1.9rem;padding:.35rem .55rem;border:0;background:transparent;box-shadow:none;color:var(--text-faint);font-size:.6rem}.segmented button.active{color:var(--text);background:var(--surface-2)}
.domain-table-wrap{overflow:auto;max-height:32rem}.domain-table{font-size:.72rem}.domain-table tbody tr{cursor:pointer}.domain-table tbody tr:focus-visible,.island-node:focus-visible{outline:2px solid var(--accent);outline-offset:-2px}.domain-table tbody tr.selected{background:var(--accent-soft)}.domain-table .domain-name{font-weight:620;color:var(--text)}.kind-mark{display:inline-block;width:.42rem;height:.42rem;border-radius:2px;margin-right:.42rem;background:var(--accent)}.kind-mark.ship{border-radius:50%;background:#8aa6ff}.domain-footer{display:flex;justify-content:space-between;gap:1rem;padding:.62rem .8rem;border-top:1px solid var(--line);color:var(--text-faint);font-size:.61rem;}
.domain-detail{position:sticky;top:5rem;border:1px solid var(--line-strong);border-radius:10px;background:linear-gradient(155deg,#14242e,#0d171f);overflow:hidden;min-height:18rem;}.detail-empty{display:grid;place-items:center;min-height:18rem;padding:2rem;text-align:center;color:var(--text-faint);font-size:.73rem}.detail-content{display:none}.detail-content.show{display:block}.detail-head{padding:1rem 1.05rem;border-bottom:1px solid var(--line);background:rgba(116,201,207,.035)}.detail-head-top{display:flex;justify-content:space-between;gap:.6rem}.detail-head h3{font-size:1rem;margin:0 0 .18rem}.detail-grid{display:grid;grid-template-columns:1fr 1fr;gap:1px;background:var(--line)}.detail-item{padding:.78rem .9rem;background:#0e1921;min-width:0;overflow-wrap:anywhere}.detail-item b{display:block;font-size:.52rem;text-transform:uppercase;letter-spacing:.1em;color:var(--text-faint);margin-bottom:.16rem}.detail-item span{font-size:.72rem;color:var(--text-soft)}.detail-note{padding:.85rem .95rem;color:var(--text-faint);font-size:.65rem;line-height:1.55;border-top:1px solid var(--line)}
footer{margin-top:2.5rem;padding-top:1.25rem;border-top:1px solid var(--line);font-size:.68rem;color:var(--text-faint);}
@media(max-width:980px){.runtime-overview{grid-template-columns:repeat(3,1fr)}.host-summary{grid-column:1/-1}.domain-workbench{grid-template-columns:1fr}.domain-detail{position:relative;top:auto}.host-domain-grid{grid-template-columns:repeat(auto-fit,minmax(15rem,1fr))}}
@media(max-width:760px){body{padding:0 .8rem 3rem}.card{padding:1.15rem}.topbar{padding-top:1.35rem}.nav{overflow-x:auto;justify-content:flex-start}.nav a{flex:0 0 auto}.tool{grid-column:1/-1}.selectors{grid-template-columns:1fr}.row .fit{width:100%}.row .fit button{width:100%}.domain-toolbar{grid-template-columns:1fr}.segmented{overflow-x:auto}.host-domain-grid{grid-template-columns:1fr}.runtime-overview{grid-template-columns:1fr 1fr}}
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
  <div class=""row""><div class=""grow""><h2>Simulation fabric</h2></div><div class=""fit""><span class=""pill ok"" id=""hostMode"">local single-process</span></div></div>
  <p class=""lede"" style=""margin-top:0"">Live ownership topology from the authoritative game loop. Start with the shape of the world, filter the inventory, then inspect one domain. Future workers fit this model without turning the page into one card per process.</p>
  <div class=""runtime-overview"">
    <div class=""host-summary""><div class=""host-kicker"">Authority host</div><div class=""host-name"" id=""hostIdentity"">Local primary</div><div class=""host-meta"" id=""hostSummary"">Waiting for runtime topology&hellip;</div></div>
    <div class=""runtime-metric""><div class=""n"" id=""runtimeDomainTotal"">&mdash;</div><div class=""l"">Domains</div></div>
    <div class=""runtime-metric""><div class=""n"" id=""runtimeHostTotal"">&mdash;</div><div class=""l"">Authority hosts</div></div>
    <div class=""runtime-metric""><div class=""n"" id=""runtimeIslandTotal"">&mdash;</div><div class=""l"">Islands</div></div>
    <div class=""runtime-metric""><div class=""n"" id=""runtimeShipTotal"">&mdash;</div><div class=""l"">Ships</div></div>
    <div class=""runtime-metric""><div class=""n"" id=""runtimeEntityTotal"">&mdash;</div><div class=""l"">Owned entities</div></div>
    <div class=""runtime-metric""><div class=""n"" id=""runtimeWarningTotal"">&mdash;</div><div class=""l"">Warnings</div></div>
  </div>
  <div class=""banner"" id=""domainWarning""><strong>Domain delivery warning</strong><span id=""domainWarningText""></span></div>
  <div class=""topology"">
    <div class=""topology-bar""><strong>Authority topology</strong><div class=""topology-legend""><span><i class=""legend-dot""></i>Island</span><span><i class=""legend-dot ship""></i>Ship affinity</span><span><i class=""legend-dot warn""></i>Warning</span></div></div>
    <div class=""topology-canvas"" id=""topologyCanvas""></div>
  </div>
  <div class=""domain-workbench"">
    <div class=""domain-browser"">
      <div class=""domain-toolbar""><input id=""domainSearch"" type=""search"" placeholder=""Search domain, entity or host&hellip;"" aria-label=""Search simulation domains""><div class=""segmented"" role=""group"" aria-label=""Filter simulation domains""><button type=""button"" class=""active"" data-domain-filter=""all"">All</button><button type=""button"" data-domain-filter=""island"">Islands</button><button type=""button"" data-domain-filter=""ship"">Ships</button><button type=""button"" data-domain-filter=""issues"">Issues</button></div></div>
      <div class=""domain-table-wrap""><table class=""domain-table""><thead><tr><th>Domain</th><th>Kind</th><th>Host</th><th class=""num"">Entities</th><th>State</th></tr></thead><tbody id=""domainInventory""></tbody></table></div>
      <div class=""domain-footer""><span id=""domainResultCount"">0 domains</span><span>Click a row to inspect</span></div>
    </div>
    <aside class=""domain-detail"" aria-live=""polite""><div class=""detail-empty"" id=""domainDetailEmpty"">Select a domain from the topology or inventory to inspect authority, affinity, replication and membership.</div><div class=""detail-content"" id=""domainDetail""><div class=""detail-head""><div class=""detail-head-top""><div><h3 id=""detailTitle"">Domain</h3><div class=""island-id"" id=""detailId""></div></div><span class=""pill"" id=""detailStatus""></span></div></div><div class=""detail-grid"" id=""detailGrid""></div><div class=""detail-note"" id=""detailNote""></div></div></aside>
  </div>
  <p class=""muted"" id=""noDomains"" style=""display:none;font-size:.78rem"">No runtime topology is available. An older game server may still be writing schema-v2 ship telemetry.</p>
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
      <h3>Selected ship recovery</h3>
      <p>Stop a runaway hull, clear a stuck helm owner, or bring an uncrewed ship beside the selected player.</p>
      <div class=""recovery-actions"">
        <button type=""button"" id=""stopShip"" data-command=""ship-stop"" data-target=""ship"">Stop ship</button>
        <button type=""button"" id=""releaseHelm"" data-command=""helm-release"" data-target=""ship"">Release stuck helm</button>
        <button type=""button"" id=""recallShip"" data-command=""ship-recall"" data-target=""ship"">Recall beside player</button>
        <button class=""btn ghost"" type=""button"" id=""copyShipDiagnostics"">Copy incident bundle</button>
      </div>
      <div class=""incident"" id=""selectedShipSummary"">Select a ship domain to inspect its live recovery state.</div>
    </div>
    <div class=""tool"">
      <h3>World resources</h3>
      <p>Restore all gatherable nodes to their authored state across the shared world.</p>
      <button type=""button"" data-command=""resources-reset"" data-argument=""all"">Reset all resource nodes</button>
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
  var latestDomains = [];
  var latestRuntimeDomains = [];
  var domainFilter = 'all';
  var selectedRuntimeDomainId = '';
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
  function selectedDomain(){var id=$('targetShip').value;for(var i=0;i<latestDomains.length;i++){if(String(latestDomains[i].hullEntityId)===id)return latestDomains[i];}return null;}
  function incidentText(d){
    if(!d)return 'Select a ship domain to inspect its live recovery state.';
    return 'hull '+d.hullEntityId+' | '+(d.piloted?'PILOT '+d.pilotPlayerEntityId:'unpiloted')
      +' | '+((d.aboardPlayerEntityIds||[]).length)+' aboard | '+d.subscriberCount+' subscribers'
      +' | gen '+d.authorityGeneration+' seq '+d.replicationSequence
      +' | frame '+(d.deliveryAgeMs<0?'never':d.deliveryAgeMs+'ms')
      +' | pose '+Number(d.x).toFixed(1)+', '+Number(d.y).toFixed(1)+', '+Number(d.z).toFixed(1);
  }
  function updateShipSummary(){text('selectedShipSummary',incidentText(selectedDomain()));}
  function updateRecoveryActions(){
    var d=selectedDomain();var hasPlayer=$('targetPlayer').value!=='';var occupied=d&&((d.aboardPlayerEntityIds||[]).length>0);
    $('stopShip').disabled=!gameReporting||!d||d.piloted;
    $('releaseHelm').disabled=!gameReporting||!d||!d.piloted;
    $('recallShip').disabled=!gameReporting||!d||d.piloted||occupied||!hasPlayer;
    $('copyShipDiagnostics').disabled=!d;
  }

  function shipTelemetryFor(domainId){
    for(var i=0;i<latestDomains.length;i++)if(latestDomains[i].domainId===domainId)return latestDomains[i];
    return null;
  }
  function domainState(d){
    if((d.warningCount||0)>0)return 'warning';
    if(d.kind==='island')return 'resident';
    return d.active?'active':'resting';
  }
  function addDetailItem(grid,label,value){
    var item=document.createElement('div');item.className='detail-item';
    var b=document.createElement('b');b.textContent=label;item.appendChild(b);
    var span=document.createElement('span');span.textContent=value;item.appendChild(span);grid.appendChild(item);
  }
  function selectRuntimeDomain(domainId){
    selectedRuntimeDomainId=domainId||'';
    var d=null;for(var i=0;i<latestRuntimeDomains.length;i++)if(latestRuntimeDomains[i].domainId===selectedRuntimeDomainId)d=latestRuntimeDomains[i];
    $('domainDetailEmpty').style.display=d?'none':'grid';
    $('domainDetail').className='detail-content'+(d?' show':'');
    if(!d){renderDomainInventory();return;}
    text('detailTitle',d.label||d.domainId);text('detailId',d.domainId);
    var status=$('detailStatus');var state=domainState(d);status.className='pill '+(state==='warning'?'bad':(state==='active'||state==='resident'?'ok':'warn'));status.textContent=state;
    var grid=$('detailGrid');clear(grid);
    addDetailItem(grid,'Host',d.hostId||'unknown');addDetailItem(grid,'Kind',d.kind||'unknown');
    addDetailItem(grid,'Owned entities',String(d.entityCount||0));addDetailItem(grid,'Island affinity',d.affinityDomainId||'none');
    addDetailItem(grid,'World position',Number(d.x).toFixed(1)+', '+Number(d.y).toFixed(1)+', '+Number(d.z).toFixed(1));
    addDetailItem(grid,'Warnings',String(d.warningCount||0));
    var ship=shipTelemetryFor(d.domainId);
    if(ship){
      addDetailItem(grid,'Authority generation',String(ship.authorityGeneration));
      addDetailItem(grid,'Replication','seq '+ship.replicationSequence+' · '+ship.cadenceMs+'ms');
      addDetailItem(grid,'Last delivery',ship.deliveryAgeMs<0?'never':ship.deliveryAgeMs+'ms ago');
      addDetailItem(grid,'Pilot',ship.pilotPlayerEntityId==null?'none':'entity '+ship.pilotPlayerEntityId);
      addDetailItem(grid,'Crew',(ship.aboardPlayerEntityIds||[]).length?(ship.aboardPlayerEntityIds||[]).join(', '):'none');
      addDetailItem(grid,'Checkout subscribers',String(ship.subscriberCount));
      addDetailItem(grid,'Structure',ship.deckCount+' decks · '+ship.mountedPartCount+' mounted');
    }
    text('detailNote',ship
      ? 'Ship motion is emitted hull-first under one authority generation and replication sequence. Affinity is spatial context, not authority ownership.'
      : (d.kind==='island'?'Island ownership is resident on this host. Scheduling and remote migration are not enabled yet.':'Ownership-only static structure; excluded from live ship flight and checkout scheduling.'));
    renderDomainInventory();
  }
  function renderTopology(){
    var canvas=$('topologyCanvas');clear(canvas);
    var islands=latestRuntimeDomains.filter(function(d){return d.kind==='island';});
    var ships=latestRuntimeDomains.filter(function(d){return d.kind==='ship'||d.kind==='static-ship';});
    var runtimeHosts=[];latestRuntimeDomains.forEach(function(d){var host=d.hostId||'unknown';if(runtimeHosts.indexOf(host)<0)runtimeHosts.push(host);});
    function islandCard(island,relatedOverride){
      var card=document.createElement('section');card.className='island-node';
      var inspectable=latestRuntimeDomains.some(function(d){return d.domainId===island.domainId;});
      if(inspectable){card.tabIndex=0;card.setAttribute('role','button');card.setAttribute('aria-label','Inspect '+island.label);card.addEventListener('click',function(){selectRuntimeDomain(island.domainId);});card.addEventListener('keydown',function(e){if(e.key==='Enter'||e.key===' '){e.preventDefault();selectRuntimeDomain(island.domainId);}});}
      var head=document.createElement('div');head.className='island-head';
      var left=document.createElement('div');var title=document.createElement('div');title.className='island-title';title.textContent=island.label;left.appendChild(title);var id=document.createElement('div');id.className='island-id';id.textContent=island.domainId;left.appendChild(id);head.appendChild(left);
      var related=relatedOverride||ships.filter(function(s){return s.affinityDomainId===island.domainId;});
      var counts=document.createElement('div');counts.className='island-counts';counts.textContent=island.entityCount+' entities\n'+related.length+' nearby ships';head.appendChild(counts);card.appendChild(head);
      var lane=document.createElement('div');lane.className='ship-lane';
      related.slice(0,8).forEach(function(ship){var node=document.createElement('button');node.type='button';node.className='ship-node '+(ship.warningCount?'warning':(ship.active?'active':''));node.textContent=ship.label;node.addEventListener('click',function(e){e.stopPropagation();selectRuntimeDomain(ship.domainId);});lane.appendChild(node);});
      if(related.length>8){var more=document.createElement('span');more.className='ship-more';more.textContent='+'+(related.length-8)+' more in inventory';lane.appendChild(more);}
      if(!related.length){var empty=document.createElement('span');empty.className='ship-more';empty.textContent='No ship affinity';lane.appendChild(empty);}
      card.appendChild(lane);return card;
    }
    var hostIds=[];latestRuntimeDomains.forEach(function(d){var id=d.hostId||'unknown';if(hostIds.indexOf(id)<0)hostIds.push(id);});hostIds.sort();
    hostIds.forEach(function(hostId){
      var hosted=latestRuntimeDomains.filter(function(d){return (d.hostId||'unknown')===hostId;});
      var hostIslands=hosted.filter(function(d){return d.kind==='island';});
      var hostShips=hosted.filter(function(d){return d.kind==='ship'||d.kind==='static-ship';});
      var cluster=document.createElement('section');cluster.className='host-cluster';
      var clusterHead=document.createElement('div');clusterHead.className='host-cluster-head';var clusterName=document.createElement('div');clusterName.className='host-cluster-name';clusterName.textContent=hostId;clusterHead.appendChild(clusterName);var clusterMeta=document.createElement('div');clusterMeta.className='host-cluster-meta';clusterMeta.textContent=hosted.length+' domains · '+hostShips.length+' ships';clusterHead.appendChild(clusterMeta);cluster.appendChild(clusterHead);
      var grid=document.createElement('div');grid.className='host-domain-grid';
      hostIslands.forEach(function(island){grid.appendChild(islandCard(island,hostShips.filter(function(ship){return ship.affinityDomainId===island.domainId;})));});
      var remote=hostShips.filter(function(ship){return !hostIslands.some(function(island){return island.domainId===ship.affinityDomainId;});});
      if(remote.length)grid.appendChild(islandCard({domainId:'host-context:'+hostId,label:'Transit / remote affinity',entityCount:0,kind:'island'},remote));
      cluster.appendChild(grid);canvas.appendChild(cluster);
    });
    if(!latestRuntimeDomains.length){var empty=document.createElement('div');empty.className='detail-empty';empty.textContent='No schema-v3 topology received.';canvas.appendChild(empty);}
  }
  function renderDomainInventory(){
    var query=($('domainSearch').value||'').toLowerCase().trim();
    var rows=latestRuntimeDomains.filter(function(d){
      var filterOk=domainFilter==='all'||(domainFilter==='issues'?(d.warningCount||0)>0:(domainFilter==='ship'?(d.kind==='ship'||d.kind==='static-ship'):d.kind===domainFilter));
      var hay=(d.domainId+' '+d.label+' '+d.kind+' '+d.hostId+' '+(d.affinityDomainId||'')).toLowerCase();return filterOk&&(!query||hay.indexOf(query)>=0);
    });
    var body=$('domainInventory');clear(body);
    rows.slice(0,250).forEach(function(d){
      var tr=document.createElement('tr');if(d.domainId===selectedRuntimeDomainId)tr.className='selected';tr.tabIndex=0;tr.addEventListener('click',function(){selectRuntimeDomain(d.domainId);});tr.addEventListener('keydown',function(e){if(e.key==='Enter'||e.key===' '){e.preventDefault();selectRuntimeDomain(d.domainId);}});
      var name=cell(tr,'','');name.className='domain-name';var mark=document.createElement('i');mark.className='kind-mark '+((d.kind==='ship'||d.kind==='static-ship')?'ship':'');name.appendChild(mark);name.appendChild(document.createTextNode(d.label||d.domainId));
      cell(tr,d.kind);cell(tr,d.hostId||'unknown','muted');cell(tr,String(d.entityCount||0),'num');
      var state=domainState(d);var stateCell=cell(tr,'');var pill=document.createElement('span');pill.className='pill '+(state==='warning'?'bad':(state==='active'||state==='resident'?'ok':'warn'));pill.textContent=state;stateCell.appendChild(pill);body.appendChild(tr);
    });
    text('domainResultCount',rows.length+' domain'+(rows.length===1?'':'s')+(rows.length>250?' · first 250 shown':''));
  }

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
    latestDomains=domains;
    latestRuntimeDomains=runtime.domains||[];
    if(!latestRuntimeDomains.length&&domains.length){
      latestRuntimeDomains=domains.map(function(d){return {domainId:d.domainId||('ship:'+d.hullEntityId),kind:'ship',label:'Ship '+d.hullEntityId,hostId:runtime.hostId||'local:primary',affinityDomainId:null,entityCount:1+(d.deckCount||0)+(d.mountedPartCount||0),active:d.active===true,warningCount:(d.staleDelivery?1:0)+(d.aboardCheckoutWarning?1:0),x:d.x,y:d.y,z:d.z};});
    }
    text('hostIdentity',runtime.hostId&&runtime.hostId!=='unknown'?runtime.hostId:'Local primary');
    var islands=latestRuntimeDomains.filter(function(d){return d.kind==='island';});
    var ships=latestRuntimeDomains.filter(function(d){return d.kind==='ship'||d.kind==='static-ship';});
    var runtimeHosts=[];latestRuntimeDomains.forEach(function(d){var host=d.hostId||'unknown';if(runtimeHosts.indexOf(host)<0)runtimeHosts.push(host);});
    var warningTotal=latestRuntimeDomains.reduce(function(n,d){return n+(d.warningCount||0);},0)+(runtime.unownedEntityCount||0)+(runtime.ownershipIssueCount||0);
    text('runtimeDomainTotal',reporting?String(latestRuntimeDomains.length):'—');
    text('runtimeHostTotal',reporting?String(runtimeHosts.length):'—');
    text('runtimeIslandTotal',reporting?String(islands.length):'—');
    text('runtimeShipTotal',reporting?String(ships.length):'—');
    text('runtimeEntityTotal',reporting?String(runtime.ownedEntityCount||0):'—');
    text('runtimeWarningTotal',reporting?String(warningTotal):'—');
    text('hostSummary',reporting
      ? ((runtime.hostMode||'unknown')+' · '+(runtime.globalEntityCount||0)+' global · '+(runtime.unownedEntityCount||0)+' unowned · '+(runtime.ownershipIssueCount||0)+' ownership issues')
      : 'Waiting for runtime topology…');
    $('noDomains').style.display=latestRuntimeDomains.length?'none':'block';
    var warnings=[];
    domains.forEach(function(d){
      if(d.staleDelivery)warnings.push((d.domainId||d.hullEntityId)+' has stale/no replication while live cadence is expected');
      if(d.aboardCheckoutWarning)warnings.push((d.domainId||d.hullEntityId)+' has more aboard players than checked-out subscribers');
    });
    if((runtime.unownedEntityCount||0)>0)warnings.push(runtime.unownedEntityCount+' world entities have no domain owner');
    if((runtime.ownershipIssueCount||0)>0)warnings.push(runtime.ownershipIssueCount+' domain ownership invariants are inconsistent');
    var domainWarning=$('domainWarning');
    if(warnings.length){domainWarning.classList.add('show','spiral');text('domainWarningText',warnings.join('; ')+'.');}
    else{domainWarning.classList.remove('show','spiral');}
    renderTopology();
    if(selectedRuntimeDomainId&&latestRuntimeDomains.some(function(d){return d.domainId===selectedRuntimeDomainId;}))selectRuntimeDomain(selectedRuntimeDomainId);
    else{if(selectedRuntimeDomainId)selectedRuntimeDomainId='';renderDomainInventory();}

    var shipSelect=$('targetShip');
    var selectedShip=shipSelect.value;clear(shipSelect);
    var noShip=document.createElement('option');noShip.value='';noShip.textContent=domains.length?'Select an exact ship':'No registered ship domain';shipSelect.appendChild(noShip);
    domains.forEach(function(d){var o=document.createElement('option');o.value=String(d.hullEntityId);o.textContent=(d.domainId||'ship')+' · hull '+d.hullEntityId+' · '+(d.piloted?'piloted':'unpiloted');shipSelect.appendChild(o);});
    if(domains.some(function(d){return String(d.hullEntityId)===selectedShip;}))shipSelect.value=selectedShip;
    updateShipSummary();
    var confirmation=$('deleteConfirmation');
    if(confirmation && confirmation.dataset.ship!==shipSelect.value){confirmation.value='';confirmation.dataset.ship=shipSelect.value;confirmation.placeholder=shipSelect.value?'DELETE':'Select a ship first';}

    var playerSelect=$('targetPlayer');
    var selected=playerSelect.value;
    clear(playerSelect);
    var none=document.createElement('option');none.value='';none.textContent=players.length?'Select a player':'No connected player';playerSelect.appendChild(none);
    players.forEach(function(p){var o=document.createElement('option');o.value=String(p.entityId);o.textContent='Entity '+p.entityId+' · peer '+p.peerId;playerSelect.appendChild(o);});
    if(players.some(function(p){return String(p.entityId)===selected;})){playerSelect.value=selected;}
    else if(players.length===1){playerSelect.value=String(players[0].entityId);}
    updateRecoveryActions();

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
    if((action==='ship-recall'||action==='ship-stop'||action==='helm-release'||action==='ship-delete')&&!target){showFeedback(false,'Select an exact ship domain first.');return;}
    if(action==='ship-recall'&&!$('targetPlayer').value){showFeedback(false,'Select the connected player who should receive the ship.');return;}
    if(!gameReporting){showFeedback(false,'The game server is not reporting fresh status.');return;}
    if(action==='resources-reset'&&!window.confirm('Reset every shared-world resource node?'))return;
    if(action==='ship-stop'&&!window.confirm('Immediately stop exact hull '+target+' at its current authoritative pose?'))return;
    if(action==='helm-release'&&!window.confirm('Release the current helm owner of exact hull '+target+' and neutralize its controls?'))return;
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
  function copyIncident(){
    var d=selectedDomain();
    if(!d){showFeedback(false,'Select an exact ship domain before copying diagnostics.');return;}
    var bundle=JSON.stringify({capturedAt:new Date().toISOString(),domain:d},null,2);
    function done(){showFeedback(true,'Copied the selected ship incident bundle.');}
    if(navigator.clipboard&&navigator.clipboard.writeText){navigator.clipboard.writeText(bundle).then(done,function(){fallbackCopy(bundle);});}
    else fallbackCopy(bundle);
  }
  function fallbackCopy(value){var area=document.createElement('textarea');area.value=value;area.setAttribute('readonly','');area.style.position='fixed';area.style.opacity='0';document.body.appendChild(area);area.select();var ok=false;try{ok=document.execCommand('copy');}catch(e){}document.body.removeChild(area);showFeedback(ok,ok?'Copied the selected ship incident bundle.':'Copy failed; use the World Inspector values instead.');}
  Array.prototype.forEach.call(document.querySelectorAll('[data-command]'),function(button){button.addEventListener('click',function(){sendCommand(button.dataset.command,button.dataset.argument,button);});});
  $('targetShip').addEventListener('change',function(){updateShipSummary();updateRecoveryActions();});
  $('targetPlayer').addEventListener('change',updateRecoveryActions);
  $('copyShipDiagnostics').addEventListener('click',copyIncident);
  $('refreshNow').addEventListener('click',refresh);
  $('domainSearch').addEventListener('input',renderDomainInventory);
  Array.prototype.forEach.call(document.querySelectorAll('[data-domain-filter]'),function(button){button.addEventListener('click',function(){domainFilter=button.dataset.domainFilter;Array.prototype.forEach.call(document.querySelectorAll('[data-domain-filter]'),function(other){other.classList.toggle('active',other===button);});renderDomainInventory();});});
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
