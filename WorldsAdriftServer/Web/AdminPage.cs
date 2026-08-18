using System.Text;
using WorldsAdriftServer.Admin;

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

        /// <summary>
        /// Self-contained, high-density simulation-console design system.
        ///
        /// Neither the tier colours nor the weather-wall colours are written here.
        /// They are appended from <see cref="MapTierPalette"/> and
        /// <see cref="MapWallPalette"/>, so the drawn surface and the legend key
        /// beside it are always the same value - including the ocean the
        /// translucent tier cells are composited over, which those modules emit
        /// too rather than assume.
        /// </summary>
        private static readonly string Style =
            StyleHead + MapTierPalette.Css() + MapWallPalette.Css() + "</style>";

        private const string StyleHead = @"<style>
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
.world-map{border:1px solid var(--line);border-radius:12px;background:#071017;overflow:hidden;margin-bottom:1rem}.world-map-bar{display:flex;justify-content:space-between;gap:.9rem;align-items:center;flex-wrap:wrap;padding:.72rem .95rem;border-bottom:1px solid var(--line);background:linear-gradient(180deg,rgba(30,47,60,.9),rgba(18,30,39,.9))}.world-map-title strong{display:block;font-size:.68rem;letter-spacing:.09em;text-transform:uppercase}.world-map-title span{display:block;margin-top:.12rem;font-size:.59rem;color:var(--text-faint)}.map-controls{display:flex;align-items:center;flex-wrap:wrap;gap:.4rem}.map-controls button{min-height:2rem;padding:.32rem .6rem;font-size:.62rem}.map-toggle{display:inline-flex;align-items:center;gap:.3rem;padding:.3rem .5rem;border:1px solid var(--line);border-radius:999px;color:var(--text-soft);font-size:.58rem;text-transform:none;letter-spacing:0;margin:0;transition:border-color .12s,color .12s,background .12s}.map-toggle:hover{border-color:var(--accent);color:var(--text)}.map-toggle input{width:auto;min-height:0;margin:0;accent-color:var(--accent)}
.map-search{position:relative;flex:0 1 20rem;min-width:11rem}.map-search input{width:100%;min-height:2.1rem;padding:.34rem .62rem .34rem 1.85rem;border:1px solid var(--line-strong);border-radius:999px;background:rgba(7,16,23,.9);color:var(--text);font-size:.66rem;transition:border-color .14s,box-shadow .14s}.map-search input:focus{outline:none;border-color:var(--accent);box-shadow:0 0 0 3px rgba(116,201,207,.16)}.map-search:before{content:'';position:absolute;left:.72rem;top:50%;width:.62rem;height:.62rem;margin-top:-.42rem;border:1.5px solid var(--text-faint);border-radius:50%;pointer-events:none}.map-search:after{content:'';position:absolute;left:1.24rem;top:50%;width:.3rem;height:1.5px;margin-top:.16rem;background:var(--text-faint);transform:rotate(45deg);pointer-events:none}.map-search-results{position:absolute;z-index:30;top:calc(100% + .35rem);left:0;width:24rem;max-width:78vw;max-height:19rem;overflow:auto;border:1px solid var(--line-strong);border-radius:9px;background:#0c1721;box-shadow:0 22px 50px rgba(0,0,0,.55);display:none}.map-search-results.show{display:block}.map-search-results .res{display:flex;width:100%;align-items:baseline;justify-content:space-between;gap:.7rem;padding:.46rem .68rem;border:0;border-bottom:1px solid rgba(38,54,65,.55);border-radius:0;background:none;color:var(--text);text-align:left;text-transform:none;letter-spacing:0;font-size:.68rem;font-weight:560;min-height:0;cursor:pointer}.map-search-results .res:last-child{border-bottom:0}.map-search-results .res:hover,.map-search-results .res:focus-visible{background:var(--accent-soft);outline:none}.map-search-results .res em{font-style:normal;font-weight:500;color:var(--text-faint);font-size:.58rem;white-space:nowrap}.map-search-results .res-none{padding:.6rem .68rem;color:var(--text-faint);font-size:.62rem}
.world-map-body{display:grid;grid-template-columns:minmax(0,1fr) 26rem;align-items:stretch;height:clamp(32rem,74vh,58rem);border-bottom:1px solid var(--line)}.world-map-stage{position:relative;height:100%;min-height:0;overflow:hidden;background:#071017;border-right:1px solid var(--line)}.world-map-stage svg{display:block;width:100%;height:100%;touch-action:none;cursor:grab}.world-map-stage svg.dragging{cursor:grabbing}.world-map-stage svg *{shape-rendering:geometricPrecision}
.map-world-boundary{fill:none;stroke:#5b7684;stroke-width:1.5;vector-effect:non-scaling-stroke}.map-haven-zone{fill:#17322f;opacity:.72}.map-grid line{stroke:#39515d;stroke-width:1;opacity:.28;vector-effect:non-scaling-stroke}
.map-biome{stroke:#233a45;stroke-width:1;vector-effect:non-scaling-stroke;cursor:pointer;transition:stroke .14s,stroke-width .14s}.map-biome.is-active{stroke-width:3.5}.map-biome:focus{outline:none}.map-biome.unassigned{stroke-dasharray:6 4}
.map-cell-label,.map-zone-label{fill:#dce8ed;font:680 12px/1 ui-sans-serif,system-ui,sans-serif;letter-spacing:.11em;text-anchor:middle;pointer-events:none;paint-order:stroke;stroke:#071017;stroke-width:3.2;stroke-linejoin:round;transition:opacity .18s}.map-cell-label .tier{font-size:8px;letter-spacing:.06em;font-weight:560;opacity:.86}.map-cell-label.unassigned{letter-spacing:.06em}.map-zone-label{fill:#8dc8b1;font-size:11px;letter-spacing:.32em}
svg.zoom-near .map-cell-label{opacity:.34}svg.zoom-near .map-cell-label .tier{opacity:0}
.map-shell-layer{opacity:0;pointer-events:none;transition:opacity .25s ease}svg.zoom-near .map-shell-layer{opacity:1;pointer-events:auto}.map-shell{fill:#74898f;fill-opacity:.92;stroke:#c3d6dc;stroke-width:1.1;stroke-linejoin:round;vector-effect:non-scaling-stroke;cursor:pointer;transition:fill .14s,stroke .14s,stroke-width .14s}.map-shell:hover,.map-shell.hot{fill:#a8c6cd;stroke:#f2fbff;stroke-width:2.2}.map-shell.selected{fill:#c6e6ec;stroke:#ffffff;stroke-width:3}
.map-marker{cursor:pointer}.map-marker:focus{outline:none}.map-marker .mk{transform-box:view-box;transform-origin:0 0;transition:transform .16s cubic-bezier(.3,.85,.35,1)}.map-marker.hot .mk,.map-marker:focus .mk{transform:scale(1.45)}.map-marker .mk-hit{fill:#000;fill-opacity:0;pointer-events:all;r:11px}svg.zoom-far .map-marker .mk-hit{r:6.5px}svg.zoom-mid .map-marker .mk-hit{r:9px}.map-marker .mk-ring{fill:none;stroke:#a6e9ee;stroke-width:1.4;opacity:0;transition:opacity .16s,r .16s}.map-marker.hot .mk-ring,.map-marker:focus .mk-ring{opacity:.95}.map-marker.selected .mk-ring{opacity:1;stroke:#fff;stroke-width:1.8}
.map-island{fill:#8b9ea7;stroke:#ccd8dd;stroke-width:.6;transition:fill .14s,stroke .14s,opacity .18s}.map-marker.hot .map-island{fill:#bfe1e7;stroke:#f4fdff}.map-marker.selected .map-island{fill:#dff4f8;stroke:#fff}.map-island.haven{fill:#71d0a5;stroke:#d6fff0}
.map-island-name{fill:#e6f1f4;font:600 8px/1 ui-sans-serif,system-ui,sans-serif;paint-order:stroke;stroke:#050d13;stroke-width:2.4;stroke-linejoin:round;pointer-events:none;opacity:0;transition:opacity .2s}svg.zoom-near .map-marker .map-island-name{opacity:1}.map-marker.hot .map-island-name,.map-marker.selected .map-island-name{opacity:1}
svg.zoom-near .map-marker .map-island{opacity:.28}svg.zoom-near .map-marker.hot .map-island,svg.zoom-near .map-marker.selected .map-island{opacity:1}
#mapIslandLayer.filtering .map-marker{opacity:.13;transition:opacity .18s}#mapIslandLayer.filtering .map-marker.match{opacity:1}#mapShellLayer.filtering .map-shell{opacity:.13}#mapShellLayer.filtering .map-shell.match{opacity:1}
.map-wall-halo{fill:none;stroke:#071017;stroke-width:5;opacity:.8;stroke-linecap:round;stroke-linejoin:round;vector-effect:non-scaling-stroke}.map-wall{fill:none;stroke-width:2.5;opacity:.98;stroke-linecap:round;stroke-linejoin:round;vector-effect:non-scaling-stroke}.map-runtime-island{fill:none;stroke:#71d0a5;stroke-width:2.5;vector-effect:non-scaling-stroke}.map-ship{fill:#8aa6ff;stroke:#f3f7ff;stroke-width:.7}.map-ship.resting{fill:#50647d}.map-player{fill:#71d0a5;stroke:#edfff7;stroke-width:.7}
/* Live wildlife. Warm hues on purpose: every other live mark on this map is
   cool (ship blue, player and island-domain green) and the tier fills are the
   Nightfall greens, blues, violets and yellows, so coral and pink are the two
   families nothing else here occupies. Colour is never the only channel - a
   manta is a swept dart that points along its travel and a jelly is a fringed
   bell that does not - and the thin dark stroke keeps both readable over a
   pale Tier 4 cell as well as over open ocean. */
.map-fauna-layer{pointer-events:none}.fauna{pointer-events:none;stroke:#08141b;stroke-width:.85;vector-effect:non-scaling-stroke}.fauna.manta{fill:#ff9e7a}.fauna.jelly{fill:#f0a8e2}.fauna.school{opacity:.96}.fauna.member{opacity:.9}svg.zoom-far .fauna.school{opacity:.8}
.map-hover{position:absolute;z-index:12;pointer-events:none;max-width:17rem;padding:.5rem .62rem;border:1px solid rgba(116,201,207,.4);border-radius:8px;background:rgba(8,17,24,.96);box-shadow:0 12px 30px rgba(0,0,0,.45);opacity:0;transform:translateY(3px);transition:opacity .12s,transform .12s}.map-hover.show{opacity:1;transform:none}.map-hover b{display:block;font-size:.72rem;font-weight:620;letter-spacing:-.01em}.map-hover .hv-meta{display:block;margin-top:.16rem;font-size:.56rem;letter-spacing:.06em;text-transform:uppercase;color:var(--text-faint)}.map-hover .hv-facts{display:block;margin-top:.3rem;font-size:.62rem;color:var(--text-soft);line-height:1.5}.map-hover .hv-cta{display:block;margin-top:.34rem;padding-top:.3rem;border-top:1px solid rgba(56,76,88,.7);font-size:.55rem;letter-spacing:.08em;text-transform:uppercase;color:var(--accent)}
.map-compass{position:absolute;right:.85rem;top:.85rem;width:2.1rem;height:2.1rem;border:1px solid var(--line-strong);border-radius:50%;display:grid;place-items:center;background:rgba(7,15,21,.82);font-size:.58rem;font-weight:750;color:var(--text-soft);pointer-events:none}.map-scalebar{position:absolute;right:.85rem;bottom:.85rem;display:flex;flex-direction:column;align-items:flex-end;gap:.18rem;pointer-events:none}.map-scalebar .bar{height:.34rem;min-width:1.5rem;border:1px solid rgba(214,232,238,.8);border-top:0;background:linear-gradient(90deg,rgba(214,232,238,.22),rgba(214,232,238,.05))}.map-scalebar .lbl{color:var(--text-soft);font-size:.55rem;font-variant-numeric:tabular-nums}.map-zoomlevel{position:absolute;left:.85rem;top:.85rem;padding:.22rem .5rem;border:1px solid var(--line);border-radius:999px;background:rgba(7,15,21,.82);color:var(--text-faint);font-size:.54rem;letter-spacing:.09em;text-transform:uppercase;pointer-events:none}.map-hint{position:absolute;left:.85rem;bottom:.85rem;padding:.3rem .6rem;border:1px solid var(--line);border-radius:999px;background:rgba(7,15,21,.82);color:var(--text-faint);font-size:.56rem;pointer-events:none;transition:opacity .4s}.map-hint.faded{opacity:.28}.world-map-stage .map-empty{position:absolute;left:50%;bottom:3.2rem;transform:translateX(-50%);padding:.3rem .6rem;border-radius:999px;background:rgba(7,15,21,.86);color:var(--text-faint);font-size:.58rem;pointer-events:none}
.map-detail{display:flex;flex-direction:column;min-width:0;height:100%;min-height:0;background:linear-gradient(180deg,#0c1721,#0a131a);overflow:hidden}.md-scroll{flex:1;overflow-y:auto;overscroll-behavior:contain}.md-head{position:sticky;top:0;z-index:2;padding:.95rem 1.05rem .8rem;border-bottom:1px solid var(--line);background:linear-gradient(180deg,#101d28,#0c1721)}.md-back{display:inline-flex;align-items:center;gap:.3rem;margin:0 0 .5rem;padding:.2rem .5rem .2rem .38rem;border:1px solid var(--line);border-radius:999px;background:none;color:var(--text-soft);font-size:.55rem;letter-spacing:.08em;text-transform:uppercase;min-height:0;cursor:pointer;transition:border-color .12s,color .12s}.md-back:hover{border-color:var(--accent);color:var(--text)}.md-kicker{font-size:.53rem;letter-spacing:.16em;text-transform:uppercase;color:var(--text-faint)}.md-title{margin:.24rem 0 0;font-size:1.18rem;font-weight:620;letter-spacing:-.015em;line-height:1.15}.md-sub{margin-top:.34rem;display:flex;flex-wrap:wrap;align-items:center;gap:.34rem .5rem;font-size:.63rem;color:var(--text-soft)}.md-sub .dot{width:2px;height:2px;border-radius:50%;background:var(--text-faint)}.md-id{margin-top:.42rem;font:500 .57rem/1.5 ui-monospace,SFMono-Regular,Consolas,monospace;color:var(--text-faint);word-break:break-all}
.md-stats{display:grid;grid-template-columns:repeat(3,minmax(0,1fr));gap:1px;background:var(--line);border-bottom:1px solid var(--line)}.md-stat{padding:.72rem .5rem .68rem 1.05rem;background:#0b1620}.md-stat:nth-child(3n+2),.md-stat:nth-child(3n){padding-left:.75rem}.md-stat b{display:block;font-size:1.42rem;font-weight:600;line-height:1.05;font-variant-numeric:tabular-nums;letter-spacing:-.02em}.md-stat b.zero{color:var(--text-faint)}.md-stat span{display:block;margin-top:.22rem;font-size:.53rem;letter-spacing:.11em;text-transform:uppercase;color:var(--text-faint)}
.md-block{padding:.85rem 1.05rem;border-bottom:1px solid rgba(38,54,65,.55)}.md-block:last-child{border-bottom:0}.md-block h4{margin:0 0 .55rem;font-size:.56rem;font-weight:640;letter-spacing:.14em;text-transform:uppercase;color:var(--text-faint)}.md-p{margin:0;font-size:.68rem;line-height:1.62;color:var(--text-soft)}.md-p+.md-p{margin-top:.5rem}.md-chips+.md-p,table.md-table+.md-p,.md-kv+.md-p{margin-top:.7rem}.md-flag+table.md-table{margin-top:.1rem}.md-p strong{color:var(--text);font-weight:600}
table.md-table{width:100%;border-collapse:collapse;font-size:.67rem}table.md-table th{padding:.2rem .45rem .3rem 0;border-bottom:1px solid var(--line);text-align:left;font-size:.52rem;font-weight:620;letter-spacing:.12em;text-transform:uppercase;color:var(--text-faint)}table.md-table td{padding:.32rem .45rem .32rem 0;border-bottom:1px solid rgba(38,54,65,.45);vertical-align:baseline}table.md-table tr:last-child td{border-bottom:0}table.md-table th.n,table.md-table td.n{text-align:right;padding-right:0;font-variant-numeric:tabular-nums}table.md-table td.ore{font-weight:600;color:var(--text)}table.md-table td.qual{color:var(--text-soft)}table.md-table tr.is-inferred td.ore:after{content:'inferred';margin-left:.42rem;padding:.04rem .3rem;border:1px solid rgba(232,192,106,.45);border-radius:3px;color:#e8c06a;font-size:.48rem;font-weight:600;letter-spacing:.09em;text-transform:uppercase;vertical-align:.06rem}
.md-flag{margin:0 0 .6rem;padding:.52rem .65rem;border-left:2px solid #e8c06a;border-radius:0 6px 6px 0;background:rgba(232,192,106,.09);color:#f0d79c;font-size:.63rem;line-height:1.6}.md-flag.plain{border-left-color:var(--accent);background:rgba(116,201,207,.08);color:var(--text-soft)}.md-flag strong{color:#f7e6bd;font-weight:640}.md-flag.plain strong{color:var(--text)}
.md-chips{display:flex;flex-wrap:wrap;gap:.3rem}.md-chip{padding:.2rem .5rem;border:1px solid var(--line-strong);border-radius:999px;background:rgba(255,255,255,.03);font-size:.6rem;color:var(--text-soft)}.md-chip.warn{border-color:rgba(232,192,106,.45);color:#e8c06a}
.md-kv{display:grid;grid-template-columns:auto 1fr;gap:.28rem .8rem;font-size:.65rem}.md-kv dt{color:var(--text-faint);letter-spacing:.03em}.md-kv dd{margin:0;color:var(--text);font-variant-numeric:tabular-nums;word-break:break-word}
.md-list{display:flex;flex-direction:column;margin:0 -1.05rem -.85rem;border-top:1px solid rgba(38,54,65,.55)}.md-list .row{display:grid;grid-template-columns:1fr auto;align-items:baseline;gap:.5rem;width:100%;padding:.44rem 1.05rem;border:0;border-bottom:1px solid rgba(38,54,65,.4);border-radius:0;background:none;color:var(--text);text-align:left;text-transform:none;letter-spacing:0;font-size:.67rem;font-weight:560;min-height:0;cursor:pointer;transition:background .12s}.md-list .row:hover,.md-list .row:focus-visible{background:var(--accent-soft);outline:none}.md-list .row em{font-style:normal;font-weight:500;font-size:.57rem;color:var(--text-faint);white-space:nowrap;font-variant-numeric:tabular-nums}.md-list .row .mark{color:#e8c06a}
.md-empty{display:flex;flex:1;flex-direction:column;align-items:center;justify-content:center;gap:.5rem;padding:2rem 1.4rem;text-align:center}.md-empty .glyph{width:2.6rem;height:2.6rem;border:1px dashed var(--line-strong);border-radius:50%;display:grid;place-items:center;color:var(--text-faint);font-size:1rem}.md-empty p{margin:0;font-size:.66rem;line-height:1.6;color:var(--text-faint);max-width:20rem}
.tierchip{display:inline-block;min-width:1.5rem;padding:.1rem .42rem;border-radius:4px;text-align:center;font-weight:670;font-size:.56rem;letter-spacing:.06em;text-transform:uppercase}
.world-map-legend{display:flex;flex-wrap:wrap;gap:.5rem .9rem;padding:.7rem .95rem;border-top:1px solid var(--line);color:var(--text-faint);font-size:.58rem;align-items:center}.map-legend-break{flex-basis:100%;height:0}.world-map-legend .legend-lead{flex-basis:100%;color:var(--text-soft);font-weight:650;letter-spacing:.04em}.map-swatch{display:inline-block;width:1rem;height:.16rem;margin-right:.3rem;vertical-align:middle;background:var(--accent)}.map-swatch.tier{height:.62rem;width:1.05rem;border-radius:2px;border:1px solid #6f7d85}.map-swatch.haven{height:.48rem;background:#173f37;border:1px solid #71d0a5}.map-swatch.ship,.map-swatch.player{width:.48rem;height:.48rem;border-radius:2px;background:#8aa6ff}.map-swatch.player{border-radius:50%;background:#71d0a5}.map-swatch.runtime{width:.5rem;height:.5rem;border-radius:50%;background:transparent;border:1px solid #71d0a5}.map-swatch.manta{width:.46rem;height:.46rem;background:#ff9e7a;clip-path:polygon(50% 0,100% 100%,50% 74%,0 100%)}.map-swatch.jelly{width:.46rem;height:.46rem;border-radius:50% 50% 34% 34%;background:#e9a4d8}.world-map-legend .legend-fauna{flex-basis:100%;color:var(--text-soft)}.world-map-legend .legend-inferred{color:#e8c06a}.world-map-legend .legend-inferred strong{color:#e8c06a}
.map-provenance{display:flex;flex-wrap:wrap;align-items:baseline;gap:.5rem .8rem;padding:.6rem .95rem;border-bottom:1px solid var(--line);background:rgba(116,201,207,.045);color:var(--text-faint);font-size:.62rem;line-height:1.6}.map-provenance strong{color:var(--text-soft)}.map-provenance-text{flex:1 1 24rem;min-width:0}
@media(max-width:1180px){.world-map-body{grid-template-columns:1fr;height:auto}.world-map-stage{border-right:0;border-bottom:1px solid var(--line);height:clamp(24rem,58vh,40rem)}.map-detail{height:auto;max-height:38rem}}
@media(prefers-reduced-motion:reduce){.map-marker .mk,.map-shell,.map-island,.map-hover,.map-shell-layer{transition:none}}
.provenance-tag{flex:0 0 auto;padding:.14rem .45rem;border:1px solid rgba(116,201,207,.42);border-radius:999px;color:var(--accent);font-size:.53rem;font-weight:750;letter-spacing:.11em;text-transform:uppercase;white-space:nowrap}.provenance-tag.live{border-color:rgba(113,208,165,.45);color:var(--good)}
.count-reconcile{display:inline-block;padding:.16rem .48rem;border:1px solid var(--line-strong);border-radius:5px;background:#0b141b;color:var(--text-soft);font:700 .58rem/1.45 ui-monospace,SFMono-Regular,Consolas,monospace;overflow-wrap:anywhere}
.island-ledger{border-top:1px solid var(--line)}.island-ledger-bar{display:flex;justify-content:space-between;gap:.8rem;align-items:flex-end;flex-wrap:wrap;padding:.66rem .9rem;background:rgba(22,35,45,.5)}.island-ledger-bar strong{display:block;font-size:.68rem;letter-spacing:.08em;text-transform:uppercase}.island-ledger-bar span.ledger-status{display:block;margin-top:.1rem;font-size:.59rem;color:var(--text-faint)}.ledger-controls{display:flex;align-items:center;flex-wrap:wrap;gap:.4rem}.ledger-controls input[type=search]{min-height:2rem;width:16rem;max-width:52vw;padding:.28rem .5rem;border:1px solid var(--line);border-radius:6px;background:var(--bg-raised);color:var(--text);font-size:.64rem}.ledger-scroll{max-height:30rem;overflow:auto;border-top:1px solid var(--line)}table.ledger{border-collapse:collapse;width:100%;font-size:.62rem}table.ledger th,table.ledger td{text-align:left;padding:.3rem .55rem;border-bottom:1px solid rgba(38,54,65,.7);white-space:nowrap;vertical-align:top}table.ledger th{position:sticky;top:0;z-index:1;background:#101a22;font-size:.55rem;letter-spacing:.1em;text-transform:uppercase;color:var(--text-faint)}table.ledger td.n,table.ledger th.n{text-align:right;font-variant-numeric:tabular-nums}table.ledger td.zero{color:var(--text-faint)}table.ledger td.wrap{white-space:normal;min-width:12rem}table.ledger tr.inferred td.ore{color:#e8c06a}table.ledger tbody tr:hover{background:rgba(116,201,207,.06)}table.ledger .tierchip{display:inline-block;min-width:1.5rem;padding:0 .25rem;border-radius:3px;text-align:center;font-weight:700}.ledger-foot{padding:.55rem .9rem;color:var(--text-faint);font-size:.6rem;line-height:1.55}.ledger-foot strong{color:var(--text-soft)}.ledger-empty{padding:1.2rem .9rem;color:var(--text-faint);font-size:.66rem}
.map-authenticity{padding:.65rem .9rem;border-top:1px solid var(--line);background:rgba(113,208,165,.035);color:var(--text-faint);font-size:.61rem;line-height:1.55}.map-authenticity strong{color:#9ee0c2}.map-empty{position:absolute;inset:auto 1rem 1rem;padding:.55rem .7rem;border:1px solid var(--line);border-radius:7px;background:rgba(7,15,21,.88);color:var(--text-faint);font-size:.65rem;pointer-events:none}
.terrain-strip{display:grid;grid-template-columns:repeat(auto-fit,minmax(7.5rem,1fr));gap:1px;background:var(--line);border:1px solid var(--line);border-radius:10px;overflow:hidden;margin-bottom:1rem;}
.terrain-metric{padding:.85rem .95rem;background:var(--surface);min-width:0;}.terrain-metric .n{font-size:1.1rem;font-weight:610;overflow-wrap:anywhere;}.terrain-metric .l{margin-top:.16rem;font-size:.55rem;font-weight:700;letter-spacing:.11em;text-transform:uppercase;color:var(--text-faint);}
.terrain-panel{border:1px solid var(--line);border-radius:10px;background:#0c151c;overflow:hidden;margin-bottom:1rem;}
.terrain-panel-head{display:flex;justify-content:space-between;align-items:center;gap:.75rem;flex-wrap:wrap;padding:.72rem .9rem;border-bottom:1px solid var(--line);background:rgba(22,35,45,.55);}.terrain-panel-head strong{font-size:.66rem;letter-spacing:.1em;text-transform:uppercase;}
.terrain-toolbar{display:grid;grid-template-columns:minmax(11rem,1fr) auto;gap:.75rem;padding:.8rem;border-bottom:1px solid var(--line);}.terrain-toolbar input{min-height:2.3rem;}
.terrain-table-wrap{overflow:auto;max-height:30rem;}.terrain-table{font-size:.72rem;}.terrain-table th{position:sticky;top:0;z-index:1;background:#0c151c;}
.terrain-table tbody tr.player-row{cursor:pointer;}.terrain-table tbody tr:focus-visible{outline:2px solid var(--accent);outline-offset:-2px;}
.state-chip{display:inline-block;padding:.16rem .45rem;border-radius:5px;border:1px solid var(--line-strong);background:rgba(255,255,255,.025);color:var(--text-faint);white-space:nowrap;font:700 .55rem/1.35 ui-monospace,SFMono-Regular,Consolas,monospace;letter-spacing:.06em;text-transform:uppercase;}
.state-chip.ready{color:var(--good);border-color:rgba(113,208,165,.42);background:rgba(113,208,165,.07);}
.state-chip.requesting,.state-chip.waiting-ack{color:var(--accent);border-color:rgba(116,201,207,.4);background:var(--accent-soft);}
.state-chip.draining,.state-chip.unloading{color:var(--warn);border-color:rgba(217,179,107,.4);background:rgba(217,179,107,.07);}
.state-chip.retained-legacy{color:var(--warn);border-style:dashed;border-color:rgba(217,179,107,.55);}
.state-chip.error{color:var(--danger);border-color:rgba(240,128,128,.5);background:var(--danger-soft);}
.terrain-detail td{background:#0a131a;}
.terrain-kv{display:grid;grid-template-columns:repeat(auto-fit,minmax(11rem,1fr));gap:.55rem .9rem;}
.terrain-kv div{min-width:0;overflow-wrap:anywhere;}.terrain-kv b{display:block;font-size:.52rem;font-weight:700;letter-spacing:.1em;text-transform:uppercase;color:var(--text-faint);margin-bottom:.14rem;}.terrain-kv span{font-size:.71rem;color:var(--text-soft);}
.events{margin:0;padding:0;list-style:none;max-height:17rem;overflow:auto;}
.event-line{display:grid;grid-template-columns:4.5rem 8rem minmax(0,1fr) auto;gap:.7rem;align-items:center;padding:.4rem .9rem;border-bottom:1px solid var(--line);font:500 .67rem/1.5 ui-monospace,SFMono-Regular,Consolas,monospace;color:var(--text-soft);}
.event-line:last-child{border-bottom:0;}.event-line .age{color:var(--text-faint);}.event-line.bad{color:#ffd2d2;background:var(--danger-soft);}
.acceptance{border:1px solid var(--line-strong);border-radius:10px;background:linear-gradient(155deg,#14242e,#0d171f);padding:1.15rem 1.25rem;}
.acceptance ol{margin:.5rem 0 0;padding-left:1.2rem;font-size:.78rem;color:var(--text-soft);}.acceptance li{margin:.3rem 0;}
.prereq{display:flex;flex-wrap:wrap;gap:.45rem;margin:.75rem 0;}
.note{margin:.75rem 0 0;font-size:.68rem;color:var(--text-faint);line-height:1.55;}
footer{margin-top:2.5rem;padding-top:1.25rem;border-top:1px solid var(--line);font-size:.68rem;color:var(--text-faint);}
@media(max-width:980px){.runtime-overview{grid-template-columns:repeat(3,1fr)}.host-summary{grid-column:1/-1}.domain-workbench{grid-template-columns:1fr}.domain-detail{position:relative;top:auto}.host-domain-grid{grid-template-columns:repeat(auto-fit,minmax(15rem,1fr))}}
@media(max-width:980px){.terrain-strip{grid-template-columns:repeat(3,1fr)}}
@media(max-width:760px){body{padding:0 .8rem 3rem}.card{padding:1.15rem}.topbar{padding-top:1.35rem}.nav{overflow-x:auto;justify-content:flex-start}.nav a{flex:0 0 auto}.tool{grid-column:1/-1}.selectors{grid-template-columns:1fr}.row .fit{width:100%}.row .fit button{width:100%}.domain-toolbar{grid-template-columns:1fr}.segmented{overflow-x:auto}.host-domain-grid{grid-template-columns:1fr}.runtime-overview{grid-template-columns:1fr 1fr}.terrain-strip{grid-template-columns:1fr 1fr}.terrain-toolbar{grid-template-columns:1fr}.event-line{grid-template-columns:4.5rem minmax(0,1fr);row-gap:.15rem}.terrain-table th{position:static}}
@media(max-width:430px){.stat{min-height:4.7rem;padding:.8rem}.kv{grid-template-columns:1fr 1fr}th,td{padding:.58rem .45rem}.button-row button{width:100%}}
@media (prefers-reduced-motion:reduce){*{transition-duration:.01ms!important;}}
";

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

        internal static readonly string Disabled = @"<!DOCTYPE html><html lang=""en""><head>
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
        internal static string Dashboard(string bootstrapJson, string csrfToken,
            string worldMapJson = "{}")
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
<nav class=""nav"" aria-label=""Control panel sections""><a href=""#world"">World</a><a href=""#simulation"">Simulation</a><a href=""#terrain"">Terrain checkout</a><a href=""#operations"">Operations</a><a href=""#system"">System</a></nav>

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
  <div class=""world-map"">
    <div class=""world-map-bar""><div class=""world-map-title""><strong>Preserved release-world map <span class=""provenance-tag"">map evidence</span></strong><span id=""mapStatus"">Loading preserved geography&hellip;</span></div>
      <div class=""map-search"">
        <input type=""search"" id=""ledgerFilter"" placeholder=""Search islands, zones, ore, wood&hellip;"" aria-label=""Search the release world"" autocomplete=""off"" role=""combobox"" aria-expanded=""false"" aria-controls=""mapSearchResults"">
        <div class=""map-search-results"" id=""mapSearchResults"" role=""listbox""></div>
      </div>
      <div class=""map-controls"">
        <label class=""map-toggle""><input type=""checkbox"" id=""mapBiomes"" checked>zones</label>
        <label class=""map-toggle""><input type=""checkbox"" id=""mapIslands"" checked>islands</label>
        <label class=""map-toggle""><input type=""checkbox"" id=""mapWalls"" checked>walls</label>
        <label class=""map-toggle""><input type=""checkbox"" id=""mapShips"" checked>ships</label>
        <label class=""map-toggle""><input type=""checkbox"" id=""mapPlayers"" checked>players</label>
        <label class=""map-toggle""><input type=""checkbox"" id=""mapFauna"" checked>wildlife</label>
        <label class=""map-toggle""><input type=""checkbox"" id=""ledgerInferredOnly"">inferred ore only</label>
        <button type=""button"" id=""mapZoomIn"" aria-label=""Zoom map in"">+</button><button type=""button"" id=""mapZoomOut"" aria-label=""Zoom map out"">&minus;</button><button type=""button"" id=""mapReset"">Whole world</button>
      </div>
    </div>
    <div class=""map-provenance"">
      <span class=""provenance-tag"">map evidence</span>
      <span class=""map-provenance-text""><strong>Island geometry, tier/biome cells, weather walls and the world boundary are a static embedded projection of the preserved Bossa release MapFile.</strong> They are historical map evidence, not live simulation state, and they do not change when the game server does. <strong>Only the ship and player markers, and the ring drawn around each simulated island domain, are live:</strong> this browser refreshes them every 4 seconds from the game server's roughly 3-second stats snapshots. The islands this server is actually simulating are the ones listed under Terrain checkout, not the ones drawn here.</span>
      <span class=""count-reconcile"" id=""mapReconcile"">Reconciling island counts&hellip;</span>
    </div>
    <div class=""world-map-body"">
    <div class=""world-map-stage""><svg id=""liveWorldMap"" role=""img"" aria-label=""Preserved release-world map evidence - tiered biome cells, Haven corridor, weather walls and island placements - with a live overlay of authoritative ships, players and currently simulated island domains""><defs><symbol id=""releaseIslandSymbol"" viewBox=""-90 -90 180 180""><path d=""M0 -70 62 -22 48 54 -8 72 -67 30 -55 -38Z""></path></symbol><symbol id=""havenIslandSymbol"" viewBox=""-110 -110 220 220""><circle r=""80""></circle><path d=""M0 -58 51 -18 39 44 -6 59 -55 25 -45 -31Z"" fill=""#d6fff0""></path></symbol><symbol id=""shipSymbol"" viewBox=""-170 -170 340 340""><path d=""M0 -145 112 98 0 58 -112 98Z""></path></symbol><symbol id=""playerSymbol"" viewBox=""-145 -145 290 290""><circle r=""105""></circle></symbol><symbol id=""mantaSymbol"" viewBox=""-100 -100 200 200""><path d=""M0 -76 C30 -50 60 -6 74 34 C44 26 20 18 0 18 C-20 18 -44 26 -74 34 C-60 -6 -30 -50 0 -76 Z""></path><path d=""M-5 14 L5 14 L2 90 L-2 90 Z""></path></symbol><symbol id=""jellySymbol"" viewBox=""-100 -100 200 200""><path d=""M0 -58 C38 -58 60 -30 60 0 C60 10 52 18 40 20 C20 24 -20 24 -40 20 C-52 18 -60 10 -60 0 C-60 -30 -38 -58 0 -58 Z""></path><path d=""M-34 20 L-24 20 L-18 78 L-30 70 Z M-6 22 L6 22 L4 90 L-4 90 Z M24 20 L34 20 L30 70 L18 78 Z"" opacity="".78""></path></symbol><clipPath id=""worldClip""><rect id=""worldClipRect""></rect></clipPath></defs><rect id=""mapOcean"" class=""map-ocean""></rect><g clip-path=""url(#worldClip)""><g id=""mapBiomeLayer""></g><g id=""mapHavenLayer""></g><g id=""mapGrid"" class=""map-grid""></g><g id=""mapWallLayer""></g><g id=""mapShellLayer"" class=""map-shell-layer""></g><g id=""mapIslandLayer""></g><g id=""mapRuntimeIslandLayer""></g><g id=""mapFaunaLayer"" class=""map-fauna-layer""></g><g id=""mapShipLayer""></g><g id=""mapPlayerLayer""></g></g><rect id=""mapWorldBoundary"" class=""map-world-boundary""></rect></svg><div class=""map-hover"" id=""mapHover"" role=""tooltip""></div><div class=""map-compass"">N</div><div class=""map-zoomlevel"" id=""mapZoomLevel"">whole world</div><div class=""map-scalebar""><div class=""bar"" id=""mapScaleBar""></div><div class=""lbl"" id=""mapScale"">6 km</div></div><div class=""map-hint"" id=""mapHint"">Drag to pan &middot; scroll to zoom &middot; click an island for its full inventory</div><div class=""map-empty"" id=""mapLiveNote"">No live positions reported.</div></div>
    <aside class=""map-detail"" id=""mapDetail"" aria-live=""polite"" aria-label=""Selected map feature""></aside>
    </div>
    <div class=""world-map-legend""><span class=""legend-lead"">Island tier, low to high &mdash; one hue per tier, painted as a translucent zone over the world at " + MapTierPalette.FillOpacityCss + @" opacity. Each key below is the <em>composited</em> colour the cell actually shows, not the undimmed hex; every cell also prints its own tier, so colour is never the only channel:</span><span><i class=""map-swatch tier tier-1""></i>T1 Wilderness &middot; temperate</span><span><i class=""map-swatch tier tier-2""></i>T2 Expanse &middot; highlands</span><span><i class=""map-swatch tier tier-3""></i>T3 Remnants &middot; ice</span><span><i class=""map-swatch tier tier-4""></i>T4 Badlands &middot; desert</span><span class=""map-legend-break""></span><span><i class=""map-swatch haven""></i>Haven corridor</span>" + MapWallPalette.LegendHtml() + @"<span><i class=""map-swatch ship""></i>Ship (live)</span><span><i class=""map-swatch player""></i>Player (live)</span><span><i class=""map-swatch runtime""></i>Currently simulated island domain (live)</span><span><i class=""map-swatch manta""></i>Manta ray school (live)</span><span><i class=""map-swatch jelly""></i>Jellyfish shoal (live)</span><span>Every other mark is preserved map evidence</span><span class=""map-legend-break""></span><span class=""legend-fauna"" id=""mapFaunaNote"">Wildlife: waiting for the game server&rsquo;s fauna roster&hellip;</span><span class=""map-legend-break""></span><span class=""legend-lead"">Nothing is written on the terrain. Hover any island or zone for a quick read, click it for the full panel on the right, and zoom in past a few kilometres to get island names and their real preserved coastlines.</span><span class=""legend-inferred"">Ore types on 193 of the 254 islands are <strong>INFERRED</strong>, not recovered: those islands were never surveyed for metal, so their ore table is composed from the surveyed same-tier cohort. Every place the panel shows one, it says so in words.</span><span>Fuel pods and loot chests are reported as 0 because retail&rsquo;s per-island placements did not survive; none were invented.</span><span>Drag to pan &middot; wheel to zoom &middot; X east / Z north</span></div>
    <div class=""map-authenticity""><strong>Release MapFile geometry.</strong> The map contains 20 distinct tier/biome cells: 18 have authored district IDs and two Tier-4 Badlands cells are explicitly unassigned. E3 is one cell; the adjacent unnamed cells are not silently invented as E1/E2 or merged into E3. Haven is inside the 36&times;36 km boundary, east of the authored separator, with 12 preserved starter-island placements. None of this geometry is read from the running game server, and none of it is evidence that any of these islands is currently simulated.</div>
    <div class=""island-ledger"">
      <div class=""island-ledger-bar"">
        <div><strong>Island ledger &middot; every catalogued island, in full <span class=""provenance-tag"">map evidence</span></strong><span class=""ledger-status"" id=""ledgerStatus"">Loading the release catalogue&hellip;</span></div>
        <div class=""ledger-controls""><span class=""ledger-status"">Driven by the map search above &mdash; one filter for the map and this table. Click a row to open that island on the map.</span></div>
      </div>
      <div class=""ledger-scroll""><table class=""ledger""><thead><tr><th>Island</th><th>Cell</th><th>Tier</th><th>Culture</th><th class=""n"">Databanks</th><th class=""n"">Deposits</th><th class=""n"">Trees</th><th>Woods</th><th>Ore table</th><th class=""n"">Fuel pods</th><th class=""n"">Loot</th><th>Notes</th></tr></thead><tbody id=""ledgerBody""></tbody></table><div class=""ledger-empty"" id=""ledgerEmpty"" hidden>No island matches that filter.</div></div>
      <div class=""ledger-foot"" id=""ledgerFoot""></div>
    </div>
  </div>
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

<div class=""section-head"" id=""terrain"">Terrain checkout</div>
<div class=""card"">
  <div class=""row""><div class=""grow""><h2>Optional island terrain <span class=""provenance-tag live"">live simulation state</span></h2></div><div class=""fit""><span class=""pill"" id=""terrainMode"">unknown</span></div></div>
  <p class=""lede"" style=""margin-top:0"">Per-peer checkout of optional island terrain on this one authoritative poll loop (<span id=""terrainHost"">local:primary</span>). Observation only: it does not move island authority and describes no remote worker. The island inventory below is the authoritative live set of islands the running game server is actually simulating; the preserved release-world map above is static map evidence and is deliberately a much larger set.</p>
  <div class=""banner"" id=""terrainBanner""><strong>Terrain checkout attention</strong><span id=""terrainBannerText""></span></div>
  <div class=""terrain-strip"">
    <div class=""terrain-metric""><div class=""n"" id=""terrainState"">&mdash;</div><div class=""l"">Runtime state</div></div>
    <div class=""terrain-metric""><div class=""n"" id=""terrainCandidates"">&mdash;</div><div class=""l"">Managed candidates</div></div>
    <div class=""terrain-metric""><div class=""n"" id=""terrainPeers"">&mdash;</div><div class=""l"">Tracked peers</div></div>
    <div class=""terrain-metric""><div class=""n"" id=""terrainReady"">&mdash;</div><div class=""l"">Ready checkouts</div></div>
    <div class=""terrain-metric""><div class=""n"" id=""terrainWarnings"">&mdash;</div><div class=""l"">Peer warnings</div></div>
    <div class=""terrain-metric""><div class=""n"" id=""terrainErrors"">&mdash;</div><div class=""l"">Errors</div></div>
    <div class=""terrain-metric""><div class=""n"" id=""terrainRadii"">&mdash;</div><div class=""l"">Load / unload m</div></div>
    <div class=""terrain-metric""><div class=""n"" id=""terrainTimings"">&mdash;</div><div class=""l"">Ack / settle</div></div>
  </div>
  <p class=""muted"" id=""terrainUnavailable"" style=""display:none;font-size:.8rem""></p>

  <div class=""terrain-panel"">
    <div class=""terrain-toolbar""><input id=""terrainSearch"" type=""search"" placeholder=""Filter by player entity, island or state&hellip;"" aria-label=""Filter terrain checkout rows""><span class=""muted"" style=""align-self:center;font-size:.62rem"" id=""terrainMatrixCount"">0 players</span></div>
    <div class=""terrain-table-wrap"">
      <table class=""terrain-table matrix"" id=""terrainMatrix""><thead><tr id=""terrainMatrixHead""><th>Player</th><th>Confirmed ground</th><th>Destination</th><th>Pending</th><th>Asset flight</th><th>Client</th></tr></thead><tbody id=""terrainPlayers""></tbody></table>
    </div>
    <div class=""domain-footer""><span id=""terrainMatrixNote"">One row per tracked peer; select a row for its lifecycle detail.</span><span>Columns are managed islands</span></div>
  </div>
  <p class=""muted"" id=""noTerrainPlayers"" style=""display:none;font-size:.8rem"">No peer is being tracked for terrain checkout.</p>

  <div class=""terrain-panel"">
    <div class=""terrain-panel-head""><strong>Island inventory &middot; islands this game server is simulating</strong><span class=""muted"" style=""font-size:.62rem"" id=""terrainIslandCount"">0 islands</span></div>
    <p class=""muted"" style=""margin:0;padding:.6rem .9rem;border-bottom:1px solid var(--line);font-size:.66rem;line-height:1.6"">Read from this game server's own live stats snapshot. It is the authoritative live set, and its size follows this deployment's configuration rather than the preserved release map. <span class=""count-reconcile"" id=""terrainReconcile"">Reconciling island counts&hellip;</span></p>
    <div class=""terrain-table-wrap"">
      <table class=""terrain-table""><thead><tr><th>Island</th><th>Registration</th><th class=""num"">Ready</th><th class=""num"">Loading</th><th class=""num"">Draining</th><th class=""num"">Unloading</th><th class=""num"">Retained</th><th class=""num"">Errors</th><th>Resources</th><th>Extent</th><th>Last event</th></tr></thead><tbody id=""terrainIslands""></tbody></table>
    </div>
  </div>
  <p class=""muted"" id=""noTerrainIslands"" style=""display:none;font-size:.8rem"">No optional terrain is registered on this game server.</p>

  <div class=""terrain-panel"">
    <div class=""terrain-panel-head""><strong>Recent lifecycle events</strong><span class=""muted"" style=""font-size:.62rem"" id=""terrainEventNote"">bounded ring buffer</span></div>
    <ul class=""events"" id=""terrainEvents"" aria-label=""Recent terrain lifecycle events""></ul>
  </div>
  <p class=""muted"" id=""noTerrainEvents"" style=""display:none;font-size:.8rem"">No terrain lifecycle event has been recorded since boot.</p>

  <div class=""acceptance"">
    <h3 style=""margin:0 0 .2rem;font-size:.9rem"">One-island visual acceptance run</h3>
    <p class=""muted"" style=""font-size:.76rem;margin:.1rem 0 0"">Haven &rarr; Mental Facility &rarr; Haven using the existing guarded travel operations. The console reports lifecycle state only; whether the terrain LOOKS right is a human judgement and is never asserted here.</p>
    <div class=""prereq"" id=""acceptancePrereq""></div>
    <div class=""button-row"">
      <button type=""button"" id=""acceptanceTravel"">Run step 2 &middot; travel to Mental Facility</button>
      <button type=""button"" id=""acceptanceReturn"">Run step 4 &middot; return to Haven</button>
    </div>
    <ol>
      <li>Select the connected player in Operations, and confirm they are grounded on Haven below.</li>
      <li>Travel to Mental Facility. Watch this player's row go REQUESTING &rarr; WAITING ACK &rarr; READY.</li>
      <li>In game, look at the island: terrain mesh, collision underfoot, and its resources appearing only after the ground does.</li>
      <li>Return to Haven, then watch the row go DRAINING &rarr; UNLOADING &rarr; ABSENT. A RETAINED (LEGACY) row means this client cannot receive a terrain removal and will keep it for the session.</li>
      <li>Record what you saw. Nothing on this page is evidence that the visuals were correct.</li>
    </ol>
    <p class=""note"" id=""acceptanceNote"">Both steps dispatch the same allowlisted, CSRF-bound travel commands as the Operations panel.</p>
  </div>
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
        <button type=""button"" id=""havenTravel"" data-command=""teleport"" data-argument=""haven"">Return to Haven</button>
        <button type=""button"" id=""tradesTravel"" data-command=""teleport"" data-argument=""trades-challenge"">Trades Challenge</button>
        <button type=""button"" id=""mentalFacilityTravel"" data-command=""teleport"" data-argument=""mental-facility"">Mental Facility · Tier 1</button>
      </div>
      <p id=""islandRequirement"">Optional destinations unlock only after their terrain registration is confirmed by the game server.</p>
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
        <button type=""button"" id=""recallShip"" data-command=""ship-recall"" data-target=""ship"">Recall 30m above player</button>
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
<script id=""releaseWorldMap"" type=""application/json"">" + worldMapJson + @"</script>
<script>
(function(){
  'use strict';
  var REFRESH_MS = 4000;
  var CSRF = '" + csrfToken + @"';
  var gameReporting = false;
  var secondIslandRegistered = false;
  var firstRegionTerrainCount = 0;
  var latestDomains = [];
  var latestRuntimeDomains = [];
  var latestTerrain = null;
  var terrainExpandedSlot = -1;
  var domainFilter = 'all';
  var selectedRuntimeDomainId = '';
  var latestGame=null;
  var mapLoaded=false;
  var worldMap={worldEdgeLength:36000,havenSeparatorX:15943.6523,islands:[],biomes:[],walls:[]};
  var mapView={x:-18000,y:-18000,w:36000,h:36000};
  var mapDragged=false;
  var latestPlayers=[];
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

  // ---- the interactive release-world map --------------------------------
  // This is a MAP and it behaves like one: pan, zoom, hover, click. NOTHING is
  // written onto the terrain. An earlier version stamped an abbreviated
  // resource roll-up on every zone - '11 isl / 55 db / 72 dep' - which made the
  // world unreadable and told nobody anything. Detail lives in two places now:
  // the panel to the right of the map, and progressive disclosure. Zoom in past
  // a few kilometres and the islands gain their names and their real preserved
  // coastlines; zoom out and it simplifies back to zones.
  var SVG_NS='http://www.w3.org/2000/svg';
  function svgEl(name,attrs,title){
    var e=document.createElementNS(SVG_NS,name);
    Object.keys(attrs||{}).forEach(function(k){e.setAttribute(k,String(attrs[k]));});
    if(title){var t=document.createElementNS(SVG_NS,'title');t.textContent=title;e.appendChild(t);}
    return e;
  }
  function el(tag,cls,txt){var e=document.createElement(tag);if(cls)e.className=cls;if(txt!=null)e.textContent=txt;return e;}
  function fmt(n){return Number(n||0).toLocaleString('en-US');}
  function plural(n,one,many){return fmt(n)+' '+(Number(n)===1?one:many);}

  // Markers are drawn at a CONSTANT SCREEN SIZE rather than a constant world
  // size. At whole-world zoom a 180 m island is a third of a pixel, which is
  // why the previous map's islands were undiscoverable and effectively
  // unclickable. Everything inside a marker group is authored in pixels and the
  // group carries scale(mapPx), so a 12 px island stays a 12 px island.
  var mapPx=1,mapAppliedPx=0,mapZoomFactor=1;
  var mapMarkers=[];       // {node,x,y} - scale-compensated
  var mapIslandNodes=[];   // one per MapFile placement: {island,inv,marker,shell,hay}
  var mapZoneNodes=[];     // one per drawn tier cell: {biome,index,path,label}
  var mapSelection={kind:'world'};
  var mapAnim=null,mapHintFaded=false;

  // The viewBox follows the STAGE aspect ratio rather than being square. A
  // square viewBox in a wide stage is letterboxed, which wasted a quarter of the
  // map area at every zoom and made a zoomed-in view look like a postage stamp
  // in a black frame.
  function mapStageAspect(){
    var r=$('liveWorldMap').getBoundingClientRect();
    return Math.max(.2,Math.min(5,(r.height||1)/(r.width||1)));
  }
  function mapStageWidthPx(){
    var r=$('liveWorldMap').getBoundingClientRect();
    return Math.max(1,r.width||1);
  }
  function worldEdge(){return Math.max(1,Number(worldMap.worldEdgeLength)||36000);}
  // The widest view allowed: the whole world square visible, plus a little air.
  function mapMaxSpan(){
    var aspect=mapStageAspect();
    return worldEdge()*1.06/Math.min(1,aspect);
  }
  function mapMinSpan(){return worldEdge()/56;}
  // You cannot drag the world off into the void. Without this you can pan until
  // the map is a black rectangle with a corner of terrain in it, which reads as
  // the page having broken rather than as you having gone too far.
  function clampMapView(){
    var half=worldEdge()/2,pad=worldEdge()*0.06,lo=-half-pad,hi=half+pad,span=hi-lo;
    mapView.x=mapView.w>=span?(lo+hi)/2-mapView.w/2
                             :Math.min(hi-mapView.w,Math.max(lo,mapView.x));
    mapView.y=mapView.h>=span?(lo+hi)/2-mapView.h/2
                             :Math.min(hi-mapView.h,Math.max(lo,mapView.y));
  }
  function applyMapView(){
    var svg=$('liveWorldMap'),aspect=mapStageAspect();
    mapView.w=Math.max(mapMinSpan(),Math.min(mapMaxSpan(),mapView.w));
    // Keep the centre while the height follows the stage.
    var cy=mapView.y+mapView.h/2,nextH=mapView.w*aspect;
    mapView.y=cy-nextH/2;mapView.h=nextH;
    clampMapView();
    svg.setAttribute('viewBox',[mapView.x,mapView.y,mapView.w,mapView.h].join(' '));
    mapPx=mapView.w/mapStageWidthPx();
    mapZoomFactor=mapMaxSpan()/mapView.w;
    // Progressive disclosure. Panning does not change any of this, so the
    // rescale below is skipped entirely while dragging.
    svg.classList.toggle('zoom-far',mapZoomFactor<2.2);
    svg.classList.toggle('zoom-mid',mapZoomFactor>=2.2&&mapZoomFactor<7);
    svg.classList.toggle('zoom-near',mapZoomFactor>=7);
    text('mapZoomLevel',mapZoomFactor<1.15?'whole world':(mapZoomFactor<7?('x'+mapZoomFactor.toFixed(1)+' zoom'):('x'+mapZoomFactor.toFixed(1)+' zoom - coastlines')));
    if(mapPx!==mapAppliedPx){mapAppliedPx=mapPx;rescaleMapFurniture();}
    updateScaleBar();
  }
  // Everything that must stay a constant SIZE ON SCREEN - island markers, live
  // markers, zone captions - is a group carrying scale(mapPx) with its contents
  // authored in pixels. Setting a font-size presentation attribute instead was
  // tried and silently lost to the stylesheet's `font:` shorthand, which is how
  // the zone captions ended up rendering at 12 world units, i.e. invisible.
  function rescaleMapFurniture(){
    for(var i=0;i<mapMarkers.length;i++){
      var m=mapMarkers[i];
      m.node.setAttribute('transform','translate('+m.x+' '+m.y+') scale('+mapPx+')');
    }
  }
  function updateScaleBar(){
    var raw=mapView.w/5,power=Math.pow(10,Math.floor(Math.log(raw)/Math.LN10)),unit=raw/power;
    var nice=(unit>=5?5:(unit>=2?2:1))*power;
    text('mapScale',nice<1000?(nice.toFixed(0)+' m'):((nice/1000).toFixed(nice%1000?1:0)+' km'));
    var bar=$('mapScaleBar');if(bar)bar.style.width=Math.round(nice/mapPx)+'px';
  }
  function resetMapView(){
    var span=mapMaxSpan(),aspect=mapStageAspect();
    mapView={x:-span/2,y:-span*aspect/2,w:span,h:span*aspect};applyMapView();
  }
  function zoomMap(factor,cx,cy){
    var next=Math.max(mapMinSpan(),Math.min(mapMaxSpan(),mapView.w*factor));
    var ratio=next/mapView.w;cx=cx==null?mapView.x+mapView.w/2:cx;cy=cy==null?mapView.y+mapView.h/2:cy;
    mapView={x:cx-(cx-mapView.x)*ratio,y:cy-(cy-mapView.y)*ratio,w:next,h:mapView.h*ratio};
    applyMapView();
    fadeMapHint();
  }
  function fadeMapHint(){if(mapHintFaded)return;mapHintFaded=true;var h=$('mapHint');if(h)h.classList.add('faded');}
  function prefersReducedMotion(){
    return window.matchMedia&&window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  }
  // Flying to a feature rather than teleporting to it is what makes a map feel
  // like one thing you are moving around in instead of a series of pictures.
  function flyTo(cx,cy,span){
    span=Math.max(mapMinSpan(),Math.min(mapMaxSpan(),span));
    var spanH=span*mapStageAspect();
    var target={x:cx-span/2,y:cy-spanH/2,w:span,h:spanH};
    if(mapAnim){cancelAnimationFrame(mapAnim);mapAnim=null;}
    if(prefersReducedMotion()){mapView=target;applyMapView();return;}
    var from={x:mapView.x,y:mapView.y,w:mapView.w,h:mapView.h},start=null,ms=420;
    function step(now){
      if(start===null)start=now;
      var t=Math.min(1,(now-start)/ms),e=t<.5?4*t*t*t:1-Math.pow(-2*t+2,3)/2;
      mapView={x:from.x+(target.x-from.x)*e,y:from.y+(target.y-from.y)*e,
               w:from.w+(target.w-from.w)*e,h:from.h+(target.h-from.h)*e};
      applyMapView();
      if(t<1)mapAnim=requestAnimationFrame(step);else mapAnim=null;
    }
    mapAnim=requestAnimationFrame(step);
    fadeMapHint();
  }
  function mapClientPoint(event){
    var svg=$('liveWorldMap'),point=svg.createSVGPoint();point.x=event.clientX;point.y=event.clientY;
    return point.matrixTransform(svg.getScreenCTM().inverse());
  }
  function clipPolygon(poly,a,b,c){
    var out=[];
    for(var i=0;i<poly.length;i++){
      var current=poly[i],previous=poly[(i+poly.length-1)%poly.length];
      var currentValue=current.x*a+current.y*b-c,previousValue=previous.x*a+previous.y*b-c;
      if(currentValue<=0){
        if(previousValue>0){var t=previousValue/(previousValue-currentValue);out.push({x:previous.x+(current.x-previous.x)*t,y:previous.y+(current.y-previous.y)*t});}
        out.push(current);
      }else if(previousValue<=0){var cross=previousValue/(previousValue-currentValue);out.push({x:previous.x+(current.x-previous.x)*cross,y:previous.y+(current.y-previous.y)*cross});}
    }
    return out;
  }
  function biomeCell(biome,all,half,separator){
    var poly=[{x:-half,y:-half},{x:separator,y:-half},{x:separator,y:half},{x:-half,y:half}],iy=-Number(biome.z),ix=Number(biome.x);
    all.forEach(function(other){if(other===biome||!poly.length)return;var ox=Number(other.x),oy=-Number(other.z),a=ox-ix,b=oy-iy,c=(ox*ox+oy*oy-ix*ix-iy*iy)/2;poly=clipPolygon(poly,a,b,c);});
    return poly;
  }
  var BIOME_INFO={
    1:{name:'Wilderness',terrain:'temperate'},
    2:{name:'Expanse',terrain:'highlands'},
    3:{name:'Remnants',terrain:'ice'},
    4:{name:'Badlands',terrain:'desert'}
  };
  function biomeInfo(type){return BIOME_INFO[Number(type)]||{name:'Unknown',terrain:'unknown'};}
  function cultureName(inv){return String(inv.culture||'').toLowerCase()==='kioki'?'Kioki':'Saborian';}
  function zoneTitle(b){
    var authored=typeof b.district==='string'&&b.district.trim().length>0;
    return authored?('Zone '+b.district):('Unassigned Tier '+b.type+' zone');
  }

  // ---- what is actually ON an island ------------------------------------
  // Every figure below is a COUNT of seeded entities carried on the island's
  // own record - never a scaled or rounded estimate. The one thing that is not
  // an observation is WHICH ore a deposit carries on the 193 islands whose
  // metal was never surveyed; those are composed from the same-tier cohort by
  // tools/world-import/metal_inference.py, and every place they are shown says
  // so in words, so they cannot be read as recovered.
  var ORE_SOURCE_LABEL={
    'survey-pve':'RECOVERED - the island own PvE survey',
    'survey-pvp':'RECOVERED - read on the PvP shard, one ruleset removed',
    'inferred-tier':'INFERRED - composed from the surveyed same-tier cohort, not Bossa data'
  };
  function oreName(metal){
    return String(metal||'').replace(/(^|[\s-])([a-z])/g,function(all,lead,ch){return lead+ch.toUpperCase();});
  }
  function oreSummary(inv){
    if(!inv.ores||!inv.ores.length)return 'no metal deposits';
    return inv.ores.map(function(o){return oreName(o.metal)+' quality '+o.quality+' x'+o.deposits;}).join(', ');
  }
  function oreProvenanceFlag(inv){
    if(inv.oresInferred){
      var flag=el('div','md-flag');
      flag.appendChild(el('strong','','Inferred, not recovered. '));
      flag.appendChild(document.createTextNode(
        'This island was never surveyed for metal, so which ore each deposit carries is composed from the '
        +'surveyed same-tier cohort. The '+plural(inv.deposits,'deposit','deposits')
        +' below are real and counted; the ore names and qualities are plausible, not Bossa data.'));
      return flag;
    }
    var ok=el('div','md-flag plain');
    ok.appendChild(el('strong','','Recovered. '));
    ok.appendChild(document.createTextNode(inv.oreSource==='survey-pvp'
      ? 'These ore types were read from the Cardinal Guild survey of this island on the PvP shard - a real reading of this island, one ruleset removed from the PvE world.'
      : 'These ore types were read from the Cardinal Guild PvE survey of this island itself.'));
    return ok;
  }
  function oreTable(ores,markInferred){
    var table=el('table','md-table');
    var head=el('tr');
    head.appendChild(el('th','','Ore'));
    head.appendChild(el('th','','Quality'));
    var th=el('th','n','Deposits');head.appendChild(th);
    var thead=el('thead');thead.appendChild(head);table.appendChild(thead);
    var body=el('tbody');
    ores.forEach(function(o){
      var row=el('tr');
      if(markInferred&&(o.inferred===undefined?markInferred:o.inferred))row.className='is-inferred';
      row.appendChild(el('td','ore',oreName(o.metal)));
      row.appendChild(el('td','qual','Quality '+o.quality));
      row.appendChild(el('td','n',fmt(o.deposits)));
      body.appendChild(row);
    });
    table.appendChild(body);
    return table;
  }
  function statTile(value,label){
    var t=el('div','md-stat');
    var b=el('b','',fmt(value));
    if(!Number(value))b.className='zero';
    t.appendChild(b);t.appendChild(el('span','',label));
    return t;
  }
  function mdBlock(heading){
    var s=el('section','md-block');
    if(heading)s.appendChild(el('h4','',heading));
    return s;
  }
  function chipRow(items,cls){
    var wrap=el('div','md-chips');
    items.forEach(function(item){wrap.appendChild(el('span','md-chip'+(cls?' '+cls:''),item));});
    return wrap;
  }
  function kv(pairs){
    var dl=el('dl','md-kv');
    pairs.forEach(function(pair){dl.appendChild(el('dt','',pair[0]));dl.appendChild(el('dd','',pair[1]));});
    return dl;
  }
  function listRow(label,meta,marked,onPick){
    var row=el('button','row');row.type='button';
    row.appendChild(el('span','',label));
    var m=el('em',marked?'mark':'',meta);row.appendChild(m);
    row.addEventListener('click',function(e){e.stopPropagation();onPick();});
    return row;
  }
  function backButton(label,onPick){
    var b=el('button','md-back','← '+label);b.type='button';
    b.addEventListener('click',function(e){e.stopPropagation();onPick();});
    return b;
  }
  function tierChip(tier){
    var c=el('span','tierchip tier-'+tier,'Tier '+tier);
    return c;
  }
  function subLine(parts){
    var wrap=el('div','md-sub');
    parts.forEach(function(part,i){
      if(i)wrap.appendChild(el('i','dot'));
      if(typeof part==='string')wrap.appendChild(document.createTextNode(part));
      else wrap.appendChild(part);
    });
    return wrap;
  }
  var NOT_PRESENT_FUEL='Fuel pods: 0. Retail per-island fuel-pod placements did not survive; the only fuel pods this server seeds are hand-placed on Haven. Reported as zero rather than omitted, and never invented.';
  var NOT_PRESENT_LOOT='Loot containers: 0. Retail carried lootable containers in component 1244, which never shipped, so there is nothing to count anywhere in the world.';

  // ---- the detail panel --------------------------------------------------
  // The thing the map is FOR. Words are spelled out here: Databanks, Metal
  // deposits, Iron - never db, dep or an asterisk that has to be looked up.
  function renderMapDetail(){
    var panel=$('mapDetail');if(!panel)return;
    clear(panel);
    var scroll=el('div','md-scroll');
    if(mapSelection.kind==='island')detailIsland(panel,scroll,mapSelection.node);
    else if(mapSelection.kind==='zone')detailZone(panel,scroll,mapSelection.zone);
    else if(mapSelection.kind==='marker')detailLiveMarker(panel,scroll,mapSelection.marker);
    else detailWorld(panel,scroll);
    panel.appendChild(scroll);
  }
  function detailWorld(panel,scroll){
    var rt=worldMap.resourceTotals||{};
    var head=el('div','md-head');
    head.appendChild(el('div','md-kicker','Preserved release world'));
    head.appendChild(el('h3','md-title','World overview'));
    if(!rt.islands){
      head.appendChild(subLine(['Catalogue not loaded']));
      panel.appendChild(head);
      var wait=el('div','md-empty');
      wait.appendChild(el('div','glyph','◎'));
      wait.appendChild(el('p','','The preserved release catalogue has not loaded yet.'));
      scroll.appendChild(wait);
      return;
    }
    head.appendChild(subLine([fmt(rt.islands)+' catalogued islands',(worldMap.biomes||[]).length+' zones',
      ((worldMap.islands||[]).length-rt.islands)+' Haven placements']));
    panel.appendChild(head);

    var stats=el('div','md-stats');
    stats.appendChild(statTile(rt.islands,'Islands'));
    stats.appendChild(statTile(rt.deposits,'Metal deposits'));
    stats.appendChild(statTile(rt.databanks,'Databanks'));
    stats.appendChild(statTile(rt.trees,'Trees'));
    stats.appendChild(statTile(rt.woodedIslands,'Wooded islands'));
    stats.appendChild(statTile(rt.islandsWithInferredOres,'Inferred ore tables'));
    scroll.appendChild(stats);

    var intro=mdBlock(null);
    var p=el('p','md-p');
    p.appendChild(document.createTextNode('Hover any island or zone to see what it is. '));
    p.appendChild(el('strong','','Click one to open it here.'));
    p.appendChild(document.createTextNode(' Zoom past a few kilometres and the islands take on their names and their real preserved coastlines.'));
    intro.appendChild(p);
    scroll.appendChild(intro);

    var prov=mdBlock('How much of this is Bossa data');
    var flag=el('div','md-flag');
    flag.appendChild(el('strong','','Ore types are inferred on '+fmt(rt.islandsWithInferredOres)+' of '+fmt(rt.islands)+' islands. '));
    flag.appendChild(document.createTextNode(
      'Those islands were never surveyed for metal, so their ore tables - covering '+plural(rt.inferredDeposits,'deposit','deposits')
      +' - are composed from the surveyed same-tier cohort. '+fmt(rt.islandsWithRecoveredOres)
      +' islands carry a real recovered survey. Deposit, databank and tree COUNTS are recovered everywhere.'));
    prov.appendChild(flag);
    scroll.appendChild(prov);

    var none=mdBlock('Not present anywhere');
    none.appendChild(el('p','md-p',NOT_PRESENT_FUEL));
    none.appendChild(el('p','md-p',NOT_PRESENT_LOOT));
    scroll.appendChild(none);

    var wild=mdBlock('Wildlife');
    var planned=0,plannedIslands=0;
    (worldMap.islands||[]).forEach(function(x){
      if(!x.fauna)return;
      plannedIslands++;planned+=(Number(x.fauna.manta)||0)+(Number(x.fauna.jelly)||0);
    });
    wild.appendChild(el('p','md-p',
      'Manta rays patrol each island’s perimeter and jellyfish drift under it by day and rise to '
      +'walking height at night. Across the whole catalogue the seeding rule would place '
      +fmt(planned)+' creatures on '+plural(plannedIslands,'island','islands')
      +'; how many actually exist depends on the world the game server booted and its creature '
      +'budget. This is a Wareborn reconstruction: retail’s ecology lived in GSim, which is not '
      +'preserved, so every COUNT is this project’s tuning and only the shape of the paths is '
      +'recovered. Open any island for its own roster.'));
    wild.appendChild(el('p','md-p',faunaNoteText()));
    scroll.appendChild(wild);

    var zones=mdBlock('Zones');
    var list=el('div','md-list');
    mapZoneNodes.slice().sort(function(a,b){
      return String(a.biome.cellId).localeCompare(String(b.biome.cellId));
    }).forEach(function(z){
      var roll=(worldMap.cells||{})[z.biome.cellId];
      var meta=roll?(plural(roll.islands,'island','islands')+' · T'+z.biome.type):('no catalogued islands · T'+z.biome.type);
      list.appendChild(listRow(zoneTitle(z.biome),meta,false,function(){selectZone(z);}));
    });
    zones.appendChild(list);
    scroll.appendChild(zones);
  }
  function detailIsland(panel,scroll,node){
    var i=node.island,inv=node.inv;
    var head=el('div','md-head');
    if(inv){
      var zone=zoneFor(inv.cell);
      if(zone)head.appendChild(backButton(zoneTitle(zone.biome),function(){selectZone(zone);}));
    }else{
      head.appendChild(backButton('Whole world',function(){selectWorld();}));
    }
    head.appendChild(el('div','md-kicker',inv?'Release island':'Haven starter placement'));
    head.appendChild(el('h3','md-title',inv?inv.name:'Haven starter island'));
    if(inv){
      var info=biomeInfo(inv.cellTier);
      head.appendChild(subLine([tierChip(inv.cellTier),info.name,cultureName(inv),'Zone '+inv.cell]));
      head.appendChild(el('div','md-id',inv.islandId+'  ·  asset '+(i.asset||'unknown')));
    }else{
      head.appendChild(subLine(['Haven reserve corridor','Hand-tuned, not surveyed']));
      head.appendChild(el('div','md-id','asset '+(i.asset||'unknown')));
    }
    panel.appendChild(head);

    if(!inv){
      var noneBlock=mdBlock('No surveyed inventory');
      noneBlock.appendChild(el('p','md-p',
        i.haven?'Haven is hand-tuned and is deliberately not part of the preserved release catalogue, so there is no survey of what is on it. Its statics are seeded by the game server, not by the release catalogue this map draws from.'
               :'This MapFile placement has no matching record in the release catalogue, so nothing is claimed about its contents.'));
      noneBlock.appendChild(kv([['World X',Number(i.x).toFixed(1)],['World Y',Number(i.y).toFixed(1)],['World Z',Number(i.z).toFixed(1)]]));
      scroll.appendChild(noneBlock);
      return;
    }

    var stats=el('div','md-stats');
    stats.appendChild(statTile(inv.databanks,'Databanks'));
    stats.appendChild(statTile(inv.deposits,'Metal deposits'));
    stats.appendChild(statTile(inv.trees,'Trees'));
    if(i.fauna)stats.appendChild(statTile(Number(i.fauna.manta)+Number(i.fauna.jelly),'Creatures'));
    scroll.appendChild(stats);

    var ore=mdBlock('Metal deposits by ore');
    if(inv.deposits&&inv.ores&&inv.ores.length){
      ore.appendChild(oreProvenanceFlag(inv));
      ore.appendChild(oreTable(inv.ores,!!inv.oresInferred));
      ore.appendChild(el('p','md-p','Provenance: '+(ORE_SOURCE_LABEL[inv.oreSource]||inv.oreSource)+'.'));
    }else{
      ore.appendChild(el('p','md-p','No metal deposits are seeded on this island. The survey found none, so none are placed.'));
    }
    scroll.appendChild(ore);

    var trees=mdBlock('Trees');
    if(inv.trees){
      trees.appendChild(el('p','md-p',plural(inv.trees,'tree','trees')+' are seeded here.'));
      if(inv.woods&&inv.woods.length){
        trees.appendChild(chipRow(inv.woods.map(function(w){return w.charAt(0).toUpperCase()+w.slice(1);})));
        trees.appendChild(el('p','md-p','The survey records WHICH woods grow on this island but not how many of each, and the seats cycle through the species above - so no per-species split is published here rather than a made-up one.'));
      }
    }else{
      trees.appendChild(el('p','md-p','No trees. The Cardinal Guild survey records none on this island, and none are seeded.'));
    }
    scroll.appendChild(trees);

    appendIslandFauna(scroll,i,inv);

    var notes=mdBlock('Survey notes');
    var flags=[];
    if(inv.revival)flags.push('Revival chamber');
    if(inv.turrets)flags.push('Turrets');
    if(inv.dangerous)flags.push('Flagged dangerous');
    if(flags.length)notes.appendChild(chipRow(flags,'warn'));
    if(Number(inv.surveyTier)!==Number(inv.cellTier)){
      var conflict=el('div','md-flag plain');
      conflict.appendChild(el('strong','','Two preserved tiers disagree. '));
      conflict.appendChild(document.createTextNode(
        'The MapFile puts this island in a Tier '+inv.cellTier+' zone, but the Cardinal Guild survey recorded it as Tier '
        +inv.surveyTier+'. Both are preserved facts and neither is dropped to make the other consistent. The map colours by the '
        +'MapFile cell tier, which is why the chip above reads Tier '+inv.cellTier+'.'));
      notes.appendChild(conflict);
    }
    if(!flags.length&&Number(inv.surveyTier)===Number(inv.cellTier))
      notes.appendChild(el('p','md-p','Nothing further was flagged: no revival chamber, no turrets, not marked dangerous, and the survey tier agrees with the MapFile cell tier.'));
    scroll.appendChild(notes);

    var absent=mdBlock('Not present on this island');
    absent.appendChild(el('p','md-p',NOT_PRESENT_FUEL));
    absent.appendChild(el('p','md-p',NOT_PRESENT_LOOT));
    scroll.appendChild(absent);

    var place=mdBlock('Placement');
    place.appendChild(kv([
      ['World X',Number(i.x).toFixed(1)],
      ['World Y',Number(i.y).toFixed(1)+' (altitude)'],
      ['World Z',Number(i.z).toFixed(1)],
      ['MapFile zone',inv.cell],
      ['Island id',inv.islandId],
      ['Workshop asset',i.asset||'unknown']
    ]));
    var zoomBtn=el('button','md-back','Zoom to this island');zoomBtn.type='button';
    zoomBtn.style.marginTop='.65rem';zoomBtn.style.marginBottom='0';
    zoomBtn.textContent='⊕ Zoom to this island';
    zoomBtn.addEventListener('click',function(){flyTo(Number(i.x),-Number(i.z),5000);});
    place.appendChild(zoomBtn);
    scroll.appendChild(place);
  }
  // WHAT LIVES ON THIS ROCK. The roster is per-island and derived from the
  // island's own surveyed tier, so stating it costs nothing - and it closes a
  // question the panel could not answer at all before, which is whether there
  // is anything alive out there.
  function appendIslandFauna(scroll,i,inv){
    var f=i.fauna,model=worldMap.faunaModel||{};
    var block=mdBlock('Wildlife');
    if(!f){
      block.appendChild(el('p','md-p','No fauna geometry is published for this placement, '
        +'so nothing is claimed about what lives on it.'));
      scroll.appendChild(block);return;
    }
    var manta=Number(f.manta)||0,jelly=Number(f.jelly)||0,schools=Math.max(1,Number(f.schools)||1);
    block.appendChild(el('p','md-p',
      plural(manta,'manta ray','manta rays')+' in '+plural(schools,'school','schools')
      +' and '+plural(jelly,'jellyfish','jellyfish')+' in '+plural(schools,'shoal','shoals')
      +' - '+plural(manta+jelly,'creature','creatures')+' in all. The seeding rule reads this '
      +'island’s Cardinal Guild SURVEY tier, which is Tier '+inv.surveyTier
      +(Number(inv.surveyTier)===Number(inv.cellTier)
        ? '.' : ', not the Tier '+inv.cellTier+' its MapFile cell carries.')));
    block.appendChild(chipRow(['Manta ray','Jellyfish']));

    var live=faunaLiveOn(inv.islandId),state=el('p','md-p');
    if(!faunaStat){
      state.textContent='The game server is not reporting an island-fauna roster, so nothing is '
        +'claimed about what is alive here right now. The counts above are what the seeding rule '
        +'places when it runs.';
    }else if(!live){
      state.textContent='The game server is reporting island fauna and this island is NOT in its '
        +'roster, so nothing is alive here this run - normally the world-wide creature budget '
        +'running out before this island was reached.';
    }else{
      state.appendChild(el('strong','','Live now. '));
      state.appendChild(document.createTextNode(
        plural(Number(live.mantaRays)||0,'manta ray','manta rays')+' and '
        +plural(Number(live.jellyFish)||0,'jellyfish','jellyfish')
        +' are on this island on the running game server, and the map is drawing them where the '
        +'server has them: it evaluates the server’s own movement against the clock the '
        +'server reports, rather than sampling positions.'));
    }
    block.appendChild(state);

    block.appendChild(kv([
      ['Manta orbit',fmt(Math.round(f.mantaOrbitRadius))+' m out, one lap in '+fmtShort(f.mantaLapSeconds)],
      ['Shoal drift',fmt(Math.round(f.jellyLateralRadius*(Number(model.jellyNightRadiusRatio)||0)))
        +' m at night to '+fmt(Math.round(f.jellyLateralRadius*(Number(model.jellyDayRadiusRatio)||0)))+' m by day'],
      ['Day/night cycle',fmtShort(Number(model.dayNightCycleSeconds)||0)],
      ['Manta speed',(Number(model.mantaMetresPerSecond)||0)+' m/s, constant']
    ]));

    var flag=el('div','md-flag plain');
    flag.appendChild(el('strong','','Wareborn tuning, not Bossa data. '));
    flag.appendChild(document.createTextNode(
      'How many creatures a school holds was never recoverable: retail’s flock component carries '
      +'two unbounded member lists and no size, minimum, maximum or density anywhere, and the '
      +'bookkeeping that filled them lived in GSim, which is not preserved. These counts are this '
      +'project’s choice. That they RISE with the tier is taken from the fandom Biome and '
      +'Creatures pages, not from Bossa data. What IS recovered is the shape of the paths: the '
      +'orbit radius above is the island’s own horizontal half-diagonal plus a ten-metre '
      +'standoff, read exactly off the decompiled patrol visualiser, and the day/night split is '
      +'its recovered 0.2/0.8 threshold. The motion itself is an analytical reconstruction, '
      +'because retail steered these animals with physics this server does not run.'));
    block.appendChild(flag);
    scroll.appendChild(block);
  }
  function detailZone(panel,scroll,z){
    var b=z.biome,info=biomeInfo(b.type),roll=(worldMap.cells||{})[b.cellId];
    var head=el('div','md-head');
    head.appendChild(backButton('Whole world',function(){selectWorld();}));
    head.appendChild(el('div','md-kicker','Map zone'));
    head.appendChild(el('h3','md-title',zoneTitle(b)));
    head.appendChild(subLine([tierChip(b.type),info.name,info.terrain,Number(b.civilization)===1?'Kioki':'Saborian']));
    head.appendChild(el('div','md-id','cell '+b.cellId+'  ·  source cell '+(z.index+1)+' of '+(worldMap.biomes||[]).length));
    panel.appendChild(head);

    if(!roll){
      var noneBlock=mdBlock('No catalogued islands');
      noneBlock.appendChild(el('p','md-p','No island in the release catalogue sits inside this zone, so there is nothing to roll up. '
        +(b.authoredDistrict?'':'Bossa left this cell’s district null in the release MapFile; no name is inferred for it here.')));
      scroll.appendChild(noneBlock);
      appendZoneGeometry(scroll,b,z);
      return;
    }

    var stats=el('div','md-stats');
    stats.appendChild(statTile(roll.islands,'Islands'));
    stats.appendChild(statTile(roll.deposits,'Metal deposits'));
    stats.appendChild(statTile(roll.databanks,'Databanks'));
    stats.appendChild(statTile(roll.trees,'Trees'));
    stats.appendChild(statTile(roll.woodedIslands,'Wooded islands'));
    stats.appendChild(statTile(roll.islandsWithInferredOres,'Inferred ore tables'));
    scroll.appendChild(stats);

    var ore=mdBlock('Metal deposits by ore across this zone');
    if(roll.islandsWithInferredOres){
      var flag=el('div','md-flag');
      flag.appendChild(el('strong','','Partly inferred. '));
      flag.appendChild(document.createTextNode(
        fmt(roll.islandsWithInferredOres)+' of the '+fmt(roll.islands)+' islands here were never surveyed for metal ('
        +plural(roll.inferredDeposits,'deposit','deposits')+'), so their ore tables are composed from the surveyed same-tier cohort. '
        +'Any row below that any unsurveyed island contributed to is marked inferred, even where a surveyed island also feeds it.'));
      ore.appendChild(flag);
    }else{
      var ok=el('div','md-flag plain');
      ok.appendChild(el('strong','','Fully recovered. '));
      ok.appendChild(document.createTextNode('Every island in this zone carries a real Cardinal Guild metal survey.'));
      ore.appendChild(ok);
    }
    if(roll.ores&&roll.ores.length)ore.appendChild(oreTable(roll.ores,true));
    else ore.appendChild(el('p','md-p','No metal deposits are seeded in this zone.'));
    scroll.appendChild(ore);

    if(roll.woods&&roll.woods.length){
      var woods=mdBlock('Woods surveyed in this zone');
      woods.appendChild(chipRow(roll.woods.map(function(w){return w.charAt(0).toUpperCase()+w.slice(1);})));
      scroll.appendChild(woods);
    }

    appendZoneGeometry(scroll,b,z);

    var islands=mdBlock('Islands in this zone');
    var list=el('div','md-list');
    mapIslandNodes.filter(function(n){return n.inv&&n.inv.cell===b.cellId;})
      .sort(function(a,c){return String(a.inv.name).localeCompare(String(c.inv.name));})
      .forEach(function(n){
        var meta=plural(n.inv.deposits,'deposit','deposits')+' · '+plural(n.inv.databanks,'databank','databanks')
          +(n.inv.oresInferred?' · inferred ore':'');
        list.appendChild(listRow(n.inv.name,meta,!!n.inv.oresInferred,function(){focusIsland(n);}));
      });
    islands.appendChild(list);
    scroll.appendChild(islands);
  }
  function appendZoneGeometry(scroll,b,z){
    var geom=mdBlock('Cell geometry');
    geom.appendChild(kv([
      ['Centre X',Number(b.x).toFixed(1)],
      ['Centre Z',Number(b.z).toFixed(1)],
      ['Tier','Tier '+b.type+' - '+biomeInfo(b.type).name],
      ['District',b.authoredDistrict?b.district:'null in the release MapFile - no name inferred'],
      ['Source cell',(z.index+1)+' of '+(worldMap.biomes||[]).length]
    ]));
    scroll.appendChild(geom);
  }
  function detailLiveMarker(panel,scroll,m){
    var head=el('div','md-head');
    head.appendChild(backButton('Whole world',function(){selectWorld();}));
    head.appendChild(el('div','md-kicker',m.kicker));
    head.appendChild(el('h3','md-title',m.title));
    head.appendChild(subLine(['Live simulation state']));
    panel.appendChild(head);
    var block=mdBlock(m.heading);
    block.appendChild(kv(m.pairs));
    block.appendChild(el('p','md-p','Read from the game server’s own stats snapshot, refreshed every 4 seconds. Unlike everything else on this map, this is live and will change.'));
    scroll.appendChild(block);
  }

  // ---- selection ---------------------------------------------------------
  function zoneFor(cellId){
    for(var i=0;i<mapZoneNodes.length;i++)if(mapZoneNodes[i].biome.cellId===cellId)return mapZoneNodes[i];
    return null;
  }
  function clearMapHighlights(){
    mapIslandNodes.forEach(function(n){
      if(n.marker)n.marker.classList.remove('selected');
      if(n.shell)n.shell.classList.remove('selected');
    });
    mapZoneNodes.forEach(function(z){z.path.classList.remove('is-active');});
  }
  function selectWorld(){mapSelection={kind:'world'};clearMapHighlights();renderMapDetail();}
  function selectIsland(node){
    mapSelection={kind:'island',node:node};
    clearMapHighlights();
    if(node.marker)node.marker.classList.add('selected');
    if(node.shell)node.shell.classList.add('selected');
    renderMapDetail();
  }
  function focusIsland(node){
    selectIsland(node);
    flyTo(Number(node.island.x),-Number(node.island.z),5000);
  }
  function selectZone(z){
    mapSelection={kind:'zone',zone:z};
    clearMapHighlights();
    z.path.classList.add('is-active');
    renderMapDetail();
  }
  function selectLiveMarker(m){mapSelection={kind:'marker',marker:m};clearMapHighlights();renderMapDetail();}

  // ---- hover -------------------------------------------------------------
  function hoverCard(title,meta,facts,cta){
    var box=$('mapHover');clear(box);
    box.appendChild(el('b','',title));
    box.appendChild(el('span','hv-meta',meta));
    if(facts)box.appendChild(el('span','hv-facts',facts));
    box.appendChild(el('span','hv-cta',cta||'Click for full detail'));
    box.classList.add('show');
  }
  function moveHover(event){
    var box=$('mapHover'),stage=box.parentNode.getBoundingClientRect();
    var x=event.clientX-stage.left+16,y=event.clientY-stage.top+16;
    var w=box.offsetWidth||220,h=box.offsetHeight||90;
    if(x+w>stage.width-8)x=event.clientX-stage.left-w-16;
    if(y+h>stage.height-8)y=event.clientY-stage.top-h-16;
    box.style.left=Math.max(6,x)+'px';box.style.top=Math.max(6,y)+'px';
  }
  function hideHover(){var box=$('mapHover');if(box)box.classList.remove('show');}
  function islandHoverFacts(inv){
    if(!inv)return 'Hand-tuned Haven placement - not in the release catalogue.';
    var parts=[plural(inv.databanks,'databank','databanks'),plural(inv.deposits,'metal deposit','metal deposits')];
    parts.push(inv.trees?plural(inv.trees,'tree','trees'):'no trees');
    return parts.join(' · ');
  }
  function attachHover(node,build){
    node.addEventListener('pointerenter',function(e){hoverCard.apply(null,build());moveHover(e);});
    node.addEventListener('pointermove',moveHover);
    node.addEventListener('pointerleave',hideHover);
  }

  // ---- drawing -----------------------------------------------------------
  function pathFromSegments(segments){return segments.map(function(w){return 'M '+Number(w.x1)+' '+(-Number(w.z1))+' L '+Number(w.x2)+' '+(-Number(w.z2));}).join(' ');}
  function shellPath(i){
    var s=i.shell;if(!s||s.length<6)return null;
    var ox=Number(i.x),oz=-Number(i.z),d='M';
    for(var k=0;k<s.length;k+=2)d+=' '+(ox+Number(s[k]))+' '+(oz-Number(s[k+1]))+(k+2<s.length?' L':'');
    return d+' Z';
  }
  function renderStaticWorldMap(){
    var grid=$('mapGrid'),walls=$('mapWallLayer'),islands=$('mapIslandLayer'),biomes=$('mapBiomeLayer'),haven=$('mapHavenLayer'),shells=$('mapShellLayer');
    clear(grid);clear(walls);clear(islands);clear(biomes);clear(haven);clear(shells);
    mapMarkers=[];mapIslandNodes=[];mapZoneNodes=[];faunaById={};
    var edge=Math.max(1,Number(worldMap.worldEdgeLength)||36000),half=edge/2,separator=Number(worldMap.havenSeparatorX)||15943.6523;
    ['mapOcean','mapWorldBoundary','worldClipRect'].forEach(function(id){var node=$(id);node.setAttribute('x',-half);node.setAttribute('y',-half);node.setAttribute('width',edge);node.setAttribute('height',edge);});

    (worldMap.biomes||[]).forEach(function(b,index){
      var poly=biomeCell(b,worldMap.biomes,half,separator);if(!poly.length)return;
      var hasDistrict=typeof b.district==='string'&&b.district.trim().length>0,info=biomeInfo(b.type);
      var path=svgEl('path',{d:'M '+poly.map(function(p){return p.x+' '+p.y;}).join(' L ')+' Z',
        'class':'map-biome type-'+b.type+(hasDistrict?'':' unassigned'),tabindex:'0',role:'button',
        'aria-label':zoneTitle(b)+' - Tier '+b.type+' '+info.name});
      biomes.appendChild(path);

      var labelWrap=svgEl('g',{'class':'map-cell-label-wrap'});
      var label=svgEl('text',{x:0,y:0,'class':'map-cell-label type-'+b.type+(hasDistrict?'':' unassigned')});
      var districtLine=svgEl('tspan',{x:0,dy:'0'});districtLine.textContent=hasDistrict?b.district:'UNASSIGNED';label.appendChild(districtLine);
      var tierLine=svgEl('tspan',{x:0,dy:'14','class':'tier'});tierLine.textContent='T'+b.type+' · '+info.name;label.appendChild(tierLine);
      labelWrap.appendChild(label);
      biomes.appendChild(labelWrap);
      mapMarkers.push({node:labelWrap,x:Number(b.x),y:-Number(b.z)-6});

      var z={biome:b,index:index,path:path,label:label};
      mapZoneNodes.push(z);
      path.addEventListener('click',function(e){e.stopPropagation();if(!mapDragged)selectZone(z);});
      path.addEventListener('keydown',function(e){if(e.key==='Enter'||e.key===' '){e.preventDefault();selectZone(z);}});
      path.addEventListener('pointerenter',function(){path.classList.add('is-active');});
      path.addEventListener('pointerleave',function(){if(!(mapSelection.kind==='zone'&&mapSelection.zone===z))path.classList.remove('is-active');});
      attachHover(path,function(){
        var roll=(worldMap.cells||{})[b.cellId];
        return [zoneTitle(b),'Tier '+b.type+' · '+info.name+' · '+info.terrain,
          roll?(plural(roll.islands,'island','islands')+' · '+plural(roll.deposits,'metal deposit','metal deposits')
                +' · '+plural(roll.databanks,'databank','databanks'))
              :'No catalogued islands in this zone',
          'Click for the zone panel'];
      });
    });

    haven.appendChild(svgEl('rect',{x:separator,y:-half,width:half-separator,height:edge,'class':'map-haven-zone'},'Authored Haven reserve corridor'));
    var zoneWrap=svgEl('g',{});
    var zoneLabel=svgEl('text',{x:0,y:0,'class':'map-zone-label',transform:'rotate(-90)'});
    zoneLabel.textContent='HAVEN CORRIDOR';zoneWrap.appendChild(zoneLabel);haven.appendChild(zoneWrap);
    mapMarkers.push({node:zoneWrap,x:(separator+half)/2,y:0});

    for(var p=-half;p<=half;p+=6000){
      grid.appendChild(svgEl('line',{x1:p,y1:-half,x2:p,y2:half}));
      grid.appendChild(svgEl('line',{x1:-half,y1:p,x2:half,y2:p}));
    }
    var names=['Wind Rift','Storm Rift','Typhon','Sand Storm','Ice Storm','World End'];
    names.forEach(function(name,type){var segments=(worldMap.walls||[]).filter(function(w){return Number(w.type)===type;});if(!segments.length)return;var d=pathFromSegments(segments);walls.appendChild(svgEl('path',{d:d,'class':'map-wall-halo'}));walls.appendChild(svgEl('path',{d:d,'class':'map-wall type-'+type},name+' · '+segments.length+' authored segments'));});

    (worldMap.islands||[]).forEach(function(i,index){
      var inv=i.inventory||null;
      var title=inv?inv.name:(i.haven?'Haven starter island':'Release island '+(index+1));
      var node={island:i,inv:inv,marker:null,shell:null,hay:islandHaystack(inv,title)};

      // The real preserved coastline, in world metres. Hidden until the view is
      // close enough for it to mean anything - see the .map-shell-layer rules.
      var d=inv?shellPath(i):null;
      if(d){
        var shell=svgEl('path',{d:d,'class':'map-shell',role:'button',tabindex:'-1','aria-label':title});
        shells.appendChild(shell);node.shell=shell;
        shell.addEventListener('click',function(e){e.stopPropagation();if(!mapDragged)selectIsland(node);});
        attachHover(shell,function(){return islandHoverCard(node);});
      }

      // Constant-screen-size marker: a generous invisible hit disc, a hover
      // ring, the island glyph and its name, all authored in pixels.
      var group=svgEl('g',{'class':'map-marker',tabindex:'0',role:'button','aria-label':title});
      var inner=svgEl('g',{'class':'mk'});
      inner.appendChild(svgEl('circle',{r:13,'class':'mk-hit'}));
      inner.appendChild(svgEl('circle',{r:10,'class':'mk-ring'}));
      inner.appendChild(svgEl('use',{href:i.haven?'#havenIslandSymbol':'#releaseIslandSymbol',
        x:-6.5,y:-6.5,width:13,height:13,'class':'map-island'+(i.haven?' haven':'')}));
      var nameLabel=svgEl('text',{x:10,y:3.4,'class':'map-island-name'});nameLabel.textContent=title;
      inner.appendChild(nameLabel);
      var t=document.createElementNS(SVG_NS,'title');t.textContent=title+' - '+islandHoverFacts(inv);group.appendChild(t);
      group.appendChild(inner);
      islands.appendChild(group);
      node.marker=group;
      mapMarkers.push({node:group,x:Number(i.x),y:-Number(i.z)});
      group.addEventListener('click',function(e){e.stopPropagation();if(!mapDragged)selectIsland(node);});
      group.addEventListener('dblclick',function(e){e.stopPropagation();focusIsland(node);});
      group.addEventListener('keydown',function(e){if(e.key==='Enter'||e.key===' '){e.preventDefault();focusIsland(node);}});
      group.addEventListener('pointerenter',function(){group.classList.add('hot');if(node.shell)node.shell.classList.add('hot');});
      group.addEventListener('pointerleave',function(){group.classList.remove('hot');if(node.shell)node.shell.classList.remove('hot');});
      attachHover(group,function(){return islandHoverCard(node);});
      mapIslandNodes.push(node);
      // The wildlife roster arrives keyed by island id, so the join to the
      // drawn placement is built once here rather than searched 460 times a
      // frame.
      if(inv&&inv.islandId&&i.fauna)faunaById[inv.islandId]=node;
    });

    renderIslandLedger();
    // Before selectWorld, because the world panel states what the wildlife is
    // doing and a panel built while the evaluator was still null said the model
    // had failed to load.
    startFauna();
    selectWorld();
    resetMapView();
  }
  function islandHoverCard(node){
    var inv=node.inv;
    return [inv?inv.name:'Haven starter island',
      inv?('Tier '+inv.cellTier+' · '+biomeInfo(inv.cellTier).name+' · zone '+inv.cell+' · '+cultureName(inv))
         :'Haven reserve corridor · hand-tuned',
      islandHoverFacts(inv),
      'Click for the full island panel'];
  }
  function islandHaystack(inv,title){
    if(!inv)return (title+' haven').toLowerCase();
    return [inv.name,inv.cell,'t'+inv.cellTier,'tier '+inv.cellTier,biomeInfo(inv.cellTier).name,cultureName(inv),
            (inv.woods||[]).join(' '),
            (inv.ores||[]).map(function(o){return o.metal+' q'+o.quality;}).join(' '),
            inv.oresInferred?'inferred':'recovered',inv.oreSource,
            inv.revival?'revival':'',inv.turrets?'turrets':'',inv.dangerous?'dangerous':''
           ].join(' ').toLowerCase();
  }

  // ---- search: one filter for the map and the ledger ---------------------
  function mapSearchQuery(){return (($('ledgerFilter')||{}).value||'').trim().toLowerCase();}
  function mapInferredOnly(){return !!(($('ledgerInferredOnly')||{}).checked);}
  function islandMatches(node){
    var q=mapSearchQuery();
    if(mapInferredOnly()&&!(node.inv&&node.inv.oresInferred))return false;
    return !q||node.hay.indexOf(q)>=0;
  }
  function applyMapFilter(){
    var q=mapSearchQuery(),active=!!q||mapInferredOnly();
    $('mapIslandLayer').classList.toggle('filtering',active);
    $('mapShellLayer').classList.toggle('filtering',active);
    var matches=[];
    mapIslandNodes.forEach(function(n){
      var hit=islandMatches(n);
      if(n.marker)n.marker.classList.toggle('match',hit);
      if(n.shell)n.shell.classList.toggle('match',hit);
      if(hit&&n.inv)matches.push(n);
    });
    renderSearchResults(q,matches);
    renderIslandLedger();
  }
  function renderSearchResults(q,matches){
    var box=$('mapSearchResults');if(!box)return;
    clear(box);
    if(!q){box.classList.remove('show');$('ledgerFilter').setAttribute('aria-expanded','false');return;}
    var zones=mapZoneNodes.filter(function(z){
      return (zoneTitle(z.biome)+' '+z.biome.cellId+' '+biomeInfo(z.biome.type).name).toLowerCase().indexOf(q)>=0;});
    if(!zones.length&&!matches.length){
      box.appendChild(el('div','res-none','Nothing in the release catalogue matches that.'));
    }
    zones.slice(0,4).forEach(function(z){
      var roll=(worldMap.cells||{})[z.biome.cellId];
      box.appendChild(listResult(zoneTitle(z.biome),'zone · '+(roll?plural(roll.islands,'island','islands'):'no islands'),function(){
        selectZone(z);flyTo(Number(z.biome.x),-Number(z.biome.z),10000);closeSearchResults();}));
    });
    matches.slice(0,12).forEach(function(n){
      box.appendChild(listResult(n.inv.name,'zone '+n.inv.cell+' · T'+n.inv.cellTier+' · '+plural(n.inv.deposits,'deposit','deposits'),function(){
        focusIsland(n);closeSearchResults();}));
    });
    if(matches.length>12)box.appendChild(el('div','res-none',fmt(matches.length-12)+' more match; the ledger below lists them all.'));
    box.classList.add('show');
    $('ledgerFilter').setAttribute('aria-expanded','true');
  }
  function listResult(label,meta,onPick){
    var b=el('button','res');b.type='button';b.setAttribute('role','option');
    b.appendChild(el('span','',label));b.appendChild(el('em','',meta));
    b.addEventListener('mousedown',function(e){e.preventDefault();});
    b.addEventListener('click',onPick);
    return b;
  }
  function closeSearchResults(){
    var box=$('mapSearchResults');if(box)box.classList.remove('show');
    var input=$('ledgerFilter');if(input)input.setAttribute('aria-expanded','false');
  }

  // ---- the ledger: every catalogued island, in one table -----------------
  // The map answers where something is; a 254-row table answers what we have,
  // which no amount of clicking a map ever does. Provenance travels with a row:
  // an inferred ore table is marked in the row it is in, not only in a footnote.
  function ledgerNotes(inv){
    var notes=[];
    if(inv.revival)notes.push('revival chamber');
    if(inv.turrets)notes.push('turrets');
    if(inv.dangerous)notes.push('dangerous');
    if(Number(inv.surveyTier)!==Number(inv.cellTier))
      notes.push('survey T'+inv.surveyTier+' vs cell T'+inv.cellTier+', both preserved');
    return notes.join(' · ');
  }
  function renderIslandLedger(){
    var body=$('ledgerBody');if(!body)return;
    clear(body);
    var all=mapIslandNodes.filter(function(n){return n.inv;}).sort(function(a,b){
      return String(a.inv.cell).localeCompare(String(b.inv.cell))
          || String(a.inv.name).localeCompare(String(b.inv.name));});
    var frag=document.createDocumentFragment();
    var shown=0,db=0,dep=0,tr=0,inf=0;
    all.forEach(function(node){
      if(!islandMatches(node))return;
      var inv=node.inv;
      shown++;db+=Number(inv.databanks||0);dep+=Number(inv.deposits||0);tr+=Number(inv.trees||0);
      if(inv.oresInferred)inf++;
      var row=document.createElement('tr');
      if(inv.oresInferred)row.className='inferred';
      row.tabIndex=0;
      row.addEventListener('click',function(){focusIsland(node);});
      row.addEventListener('keydown',function(e){if(e.key==='Enter'||e.key===' '){e.preventDefault();focusIsland(node);}});
      cell(row,inv.name);
      cell(row,inv.cell);
      var td=cell(row,'');
      var chip=document.createElement('span');
      chip.className='tierchip tier-'+inv.cellTier;
      chip.textContent='T'+inv.cellTier;
      td.appendChild(chip);
      td.appendChild(document.createTextNode(' '+biomeInfo(inv.cellTier).name));
      cell(row,cultureName(inv));
      cell(row,inv.databanks,'n'+(Number(inv.databanks)?'':' zero'));
      cell(row,inv.deposits,'n'+(Number(inv.deposits)?'':' zero'));
      cell(row,inv.trees,'n'+(Number(inv.trees)?'':' zero'));
      cell(row,(inv.woods&&inv.woods.length)?inv.woods.join(', '):'—',
           'wrap'+((inv.woods&&inv.woods.length)?'':' zero'));
      cell(row,(inv.oresInferred?'INFERRED: ':'')+oreSummary(inv),'wrap ore');
      cell(row,inv.fuelPods,'n zero');
      cell(row,inv.lootContainers,'n zero');
      cell(row,ledgerNotes(inv)||'—','wrap'+(ledgerNotes(inv)?'':' zero'));
      frag.appendChild(row);
    });
    body.appendChild(frag);
    var empty=$('ledgerEmpty');if(empty)empty.hidden=shown>0;
    text('ledgerStatus',shown===all.length
      ? ('All '+all.length+' catalogued islands, sorted by zone then name. Haven’s 12 hand-tuned placements are not in the release catalogue and are deliberately absent.')
      : (shown+' of '+all.length+' islands match the search.'));
    var foot=$('ledgerFoot');
    if(foot){
      clear(foot);
      var totals=document.createElement('div');
      totals.appendChild(document.createTextNode('Shown: '));
      var strong=document.createElement('strong');
      strong.textContent=shown+' islands · '+db+' databanks · '+dep+' metal deposits · '+tr+' trees';
      totals.appendChild(strong);
      totals.appendChild(document.createTextNode(
        '. Counts are lengths of lists in the catalogue the game server seeds from — nothing here is scaled, rounded or estimated.'));
      foot.appendChild(totals);
      var prov=document.createElement('div');
      prov.className='legend-inferred';
      prov.textContent=inf+' of the '+shown+' rows shown carry an INFERRED ore table: which metal a deposit holds was never recovered for those islands, so the table is composed from the surveyed same-tier cohort. The deposit COUNT is real; the ore names are plausible, not Bossa data.';
      foot.appendChild(prov);
      var none=document.createElement('div');
      none.textContent='Fuel pods and loot containers are 0 for every island because retail shipped neither per-island: fuel pods exist only as hand-placed Haven statics, and the lootable-container component never shipped at all. Reported as zero rather than omitted, and never invented.';
      foot.appendChild(none);
    }
  }

  // ---- the live overlay --------------------------------------------------
  function liveMarker(layer,cls,symbol,size,x,y,title,model){
    var group=svgEl('g',{'class':'map-marker',tabindex:'0',role:'button','aria-label':title});
    var inner=svgEl('g',{'class':'mk'});
    inner.appendChild(svgEl('circle',{r:12,'class':'mk-hit'}));
    inner.appendChild(svgEl('circle',{r:9,'class':'mk-ring'}));
    inner.appendChild(svgEl('use',{href:symbol,x:-size/2,y:-size/2,width:size,height:size,'class':cls}));
    var t=document.createElementNS(SVG_NS,'title');t.textContent=title;group.appendChild(t);
    group.appendChild(inner);
    layer.appendChild(group);
    mapMarkers.push({node:group,x:x,y:y});
    group.setAttribute('transform','translate('+x+' '+y+') scale('+mapPx+')');
    group.addEventListener('click',function(e){e.stopPropagation();if(!mapDragged)selectLiveMarker(model);});
    group.addEventListener('pointerenter',function(){group.classList.add('hot');});
    group.addEventListener('pointerleave',function(){group.classList.remove('hot');});
    attachHover(group,function(){return [model.title,model.kicker,model.summary,'Click for live detail'];});
  }
  function renderLiveWorldMap(reporting,ageSeconds){
    var runtimeLayer=$('mapRuntimeIslandLayer'),shipLayer=$('mapShipLayer'),playerLayer=$('mapPlayerLayer');
    // Live markers are re-created every poll, so drop their scale registrations
    // first or the list grows without bound.
    mapMarkers=mapMarkers.filter(function(m){
      return m.node.parentNode!==shipLayer&&m.node.parentNode!==playerLayer;});
    clear(runtimeLayer);clear(shipLayer);clear(playerLayer);
    var runtimeIslands=latestRuntimeDomains.filter(function(d){return d.kind==='island';});
    runtimeIslands.forEach(function(i){
      runtimeLayer.appendChild(svgEl('circle',{cx:Number(i.x),cy:-Number(i.z),r:155,'class':'map-runtime-island'},
        (i.label||i.domainId)+' · currently simulated island domain, resident on this host · live position '+Number(i.x).toFixed(1)+', '+Number(i.z).toFixed(1)));
    });
    latestDomains.forEach(function(s){
      var x=Number(s.x),y=-Number(s.z);
      liveMarker(shipLayer,'map-ship'+(s.active?'':' resting'),'#shipSymbol',15,x,y,'Ship '+s.hullEntityId,{
        kicker:'Live ship',title:'Ship '+s.hullEntityId,heading:'Authoritative ship state',
        summary:(s.piloted?'Piloted':'Resting')+' · X '+x.toFixed(1)+' Z '+Number(s.z).toFixed(1),
        pairs:[['Hull entity',String(s.hullEntityId)],['World X',x.toFixed(1)],['World Y',Number(s.y).toFixed(1)],
               ['World Z',Number(s.z).toFixed(1)],['Helm',s.piloted?'piloted':'resting'],
               ['Affinity',s.affinityDomainId||'none']]});
    });
    var positioned=latestPlayers.filter(function(p){return p.hasPosition;});
    positioned.forEach(function(p){
      var x=Number(p.x),y=-Number(p.z);
      liveMarker(playerLayer,'map-player','#playerSymbol',13,x,y,'Player entity '+p.entityId,{
        kicker:'Live player',title:'Player entity '+p.entityId,heading:'Authoritative player position',
        summary:'X '+x.toFixed(1)+' · Y '+Number(p.y).toFixed(1)+' · Z '+Number(p.z).toFixed(1),
        pairs:[['Entity',String(p.entityId)],['World X',x.toFixed(1)],['World Y',Number(p.y).toFixed(1)],
               ['World Z',Number(p.z).toFixed(1)]]});
    });
    // The wildlife roster and its clock come from the same snapshot, and the
    // animation loop reads them; nothing is drawn on this pass.
    noteFauna(latestGame);
    text('mapFaunaNote',faunaNoteText());
    $('mapBiomeLayer').style.display=$('mapBiomes').checked?'':'none';
    $('mapIslandLayer').style.display=$('mapIslands').checked?'':'none';
    $('mapShellLayer').style.display=$('mapIslands').checked?'':'none';
    runtimeLayer.style.display=$('mapIslands').checked?'':'none';
    $('mapWallLayer').style.display=$('mapWalls').checked?'':'none';
    shipLayer.style.display=$('mapShips').checked?'':'none';
    playerLayer.style.display=$('mapPlayers').checked?'':'none';
    var unknown=latestPlayers.length-positioned.length,live=latestDomains.length+positioned.length;
    var namedCells=(worldMap.biomes||[]).filter(function(b){return typeof b.district==='string'&&b.district.trim().length>0;}).length;
    var rt=worldMap.resourceTotals||{};
    var seeded=rt.islands?(' Seeded on them: '+rt.deposits+' metal deposits, '+rt.databanks+' databanks, '+rt.trees+' trees across '+rt.woodedIslands+' wooded islands; '+rt.islandsWithInferredOres+' of '+rt.islands+' catalogued islands have an INFERRED ore table ('+rt.inferredDeposits+' deposits).'):'';
    text('mapStatus','Static map evidence: release MapFile · '+(worldMap.islands||[]).length+' islands · '+(worldMap.biomes||[]).length+' tier cells ('+namedCells+' named, '+((worldMap.biomes||[]).length-namedCells)+' unassigned) · '+(worldMap.walls||[]).length+' wall segments.'+seeded+' Live overlay: '
      +(reporting?(runtimeIslands.length+' simulated island domains · '+latestDomains.length+' ships · '+positioned.length+' positioned players · '+Math.round(ageSeconds||0)+'s snapshot age'):'game server not reporting'));
    var note=$('mapLiveNote');note.style.display=(live||!reporting)?'none':'block';
    if(unknown){note.style.display='block';note.textContent=unknown+' connected player'+(unknown===1?' has':'s have')+' no authoritative world position yet.';}
    else note.textContent='No live positions reported.';
  }

  // ---- live wildlife -----------------------------------------------------
  // WHY THIS IS NOT A POSITION FEED. Every creature on the game server moves on
  // a CLOSED FORM of the clock: a manta's perimeter orbit and a jellyfish
  // shoal's day/night drift are functions of elapsed seconds, with no
  // integration, no entropy and no remembered pose (IslandFaunaMovement). The
  // stats snapshot lands every three seconds, so pushing 460 positions through
  // it would cost bandwidth AND still teleport every animal three times a
  // minute. What the server sends instead is the ROSTER - who is alive, on
  // which island - and its own fauna CLOCK, and this browser evaluates the same
  // function. The result is smooth at any frame rate and is the pose the server
  // actually holds, not an interpolation between two stale samples.
  //
  // NONE OF THE NUMBERS ARE RESTATED HERE. worldMap.faunaModel is a projection
  // of IslandFaunaMapModel.Constants and each island's `fauna` block is
  // precomputed from its envelope by IslandFaunaMapModel.MotionFor, so retuning
  // a manta's speed or an island's geometry moves this map with it. What IS
  // restated is the SHAPE of the formulas below, and that is guarded:
  // AdminFaunaParityTests extracts the marked block, runs it against the C# at
  // fixed timestamps, and fails if the two disagree by a millimetre.
  //
  // Positions are ISLAND-LOCAL and added to the MapFile placement, exactly as
  // the preserved coastline above is, so a creature is always drawn in the
  // right relationship to the rock beneath it.

  // ==== FAUNA MOTION MIRROR BEGIN ====
  function faunaMotion(M){
    function fraction(v){var f=v-Math.floor(v);return f<0?f+1:(f>=1?0:f);}
    function smoothStep(t){return t<=0?0:(t>=1?1:t*t*(3-2*t));}
    function schoolPhase(schoolIndex){return fraction(schoolIndex*M.goldenRatioFraction);}
    function cycleFraction(t){return fraction(t/M.dayNightCycleSeconds);}
    // How DAYTIME it is, 0..1, ramped across dawn and dusk. The ramp is what
    // keeps a shoal drifting rather than teleporting at the phase boundary.
    function dayness(t){
      var c=cycleFraction(t),ramp=M.phaseTransitionFraction;
      return Math.min(smoothStep((c-M.dayBeginsAtCycleFraction)/ramp),
                      smoothStep((M.dayEndsAtCycleFraction-c)/ramp));
    }
    function mantaVertical(lap){return M.mantaVerticalSpanRatio*Math.sin(fraction(lap)*Math.PI);}
    function mantaCentre(p,schoolIndex,t){
      var lap=fraction(t/p.mantaLapSeconds+schoolPhase(schoolIndex)),th=lap*2*Math.PI;
      return {x:p.cx+p.mantaOrbitRadius*Math.sin(th),
              y:p.cy+p.halfHeight*mantaVertical(lap),
              z:p.cz+p.mantaOrbitRadius*Math.cos(th)};
    }
    function jellyCentre(p,schoolIndex,t){
      var d=dayness(t);
      var th=fraction(t/M.jellySecondsPerRevolution+schoolPhase(schoolIndex))*2*Math.PI;
      var r=p.jellyLateralRadius
        *(M.jellyNightRadiusRatio+(M.jellyDayRadiusRatio-M.jellyNightRadiusRatio)*d);
      var nightY=p.minY+(p.maxY-p.minY)*M.walkableHeightFraction;
      return {x:p.cx+r*Math.sin(th),y:nightY+(p.minY-nightY)*d,z:p.cz+r*Math.cos(th)};
    }
    function memberOffset(memberIndex,radius,verticalRadius,t){
      var weave=t*M.weaveRadiansPerSecond;
      var angle=memberIndex*M.goldenAngleRadians+weave;
      var radial=radius*Math.sqrt(fraction((memberIndex+1)*M.goldenRatioFraction));
      var vertical=verticalRadius
        *Math.sin((memberIndex+1)*M.goldenAngleRadians*0.5+weave*0.6);
      return {x:radial*Math.cos(angle),y:vertical,z:radial*Math.sin(angle)};
    }
    function cluster(species){
      return species==='manta'
        ? {r:M.mantaSchoolRadius,v:M.mantaSchoolVerticalRadius}
        : {r:M.jellyShoalRadius,v:M.jellyShoalVerticalRadius};
    }
    function schoolCentre(p,species,schoolIndex,t){
      return species==='manta'?mantaCentre(p,schoolIndex,t):jellyCentre(p,schoolIndex,t);
    }
    function localPose(p,species,schoolIndex,memberIndex,t){
      var c=schoolCentre(p,species,schoolIndex,t),k=cluster(species);
      var o=memberOffset(memberIndex,k.r,k.v,t);
      return {x:c.x+o.x,y:c.y+o.y,z:c.z+o.z};
    }
    return {localPose:localPose,schoolCentre:schoolCentre,cluster:cluster,
            dayness:dayness,cycleFraction:cycleFraction};
  }
  // ==== FAUNA MOTION MIRROR END ====

  var FAUNA=null;        // the evaluator, built once the model has loaded
  var faunaAnchor=null;  // {clock,perf} - the server's fauna clock carried on ours
  var faunaStat=null;    // the live section, or null when nothing may be drawn
  var faunaRoster=[];    // [{node,p,ox,oz,manta,jelly}] joined to the drawn islands
  var faunaById={};      // islandId -> drawn island node
  var faunaPool=[];      // reused SVG groups: this repaints up to 60 times a second
  var faunaFrame=null,faunaLastDrawMs=-1e9,faunaLastNoteMs=-1e9,faunaSignature='';
  // Members are separated only once a school is big enough on screen to have
  // members worth separating. A manta school is 12 m across, which at
  // whole-world zoom is a third of a pixel: four darts stacked on one dot say
  // nothing a single moving mark does not, and cost 460 nodes to say it.
  var FAUNA_MEMBER_PIXELS=5;
  // Two samples of the SAME path give the bearing. Far enough apart to be well
  // above floating-point noise, short enough that it is the tangent.
  var FAUNA_HEADING_DT=0.35;
  var FAUNA_REDUCED_MOTION_MS=1000, FAUNA_IDLE_MS=400;

  function faunaNow(){
    return (window.performance&&performance.now)?performance.now():Date.now();
  }
  // Carry the server's fauna clock on OUR monotonic one rather than on the wall
  // clock, so a browser whose time is thirty seconds out still draws the
  // creatures where the server has them. Re-anchoring on every poll would make
  // the whole world twitch once every four seconds by however much the two
  // clocks disagree; re-anchoring only on a real jump catches the two cases
  // that matter - a restarted game server, and a tab that was suspended.
  function noteFauna(g){
    var f=(g&&g.fauna)||null;
    var live=!!(f&&f.present===true&&f.enabled===true&&(f.islands||[]).length
                &&g.reporting===true&&g.stale!==true);
    var was=faunaSignature;
    faunaStat=live?f:null;
    if(!live){
      faunaAnchor=null;faunaRoster=[];faunaSignature='';
      if(was!==faunaSignature)renderMapDetail();
      return;
    }
    var now=faunaNow(),reported=Number(f.clockSeconds)||0;
    var predicted=faunaAnchor?faunaAnchor.clock+(now-faunaAnchor.perf)/1000:null;
    if(predicted===null||Math.abs(predicted-reported)>2)
      faunaAnchor={clock:reported,perf:now};
    faunaRoster=[];
    (f.islands||[]).forEach(function(row){
      var node=faunaById[row.islandId];
      if(!node||!node.island||!node.island.fauna)return;
      faunaRoster.push({node:node,p:node.island.fauna,
        ox:Number(node.island.x),oz:-Number(node.island.z),
        manta:Math.max(0,Number(row.mantaRays)||0),
        jelly:Math.max(0,Number(row.jellyFish)||0)});
    });
    // The detail panel STATES what is alive, so it has to be rebuilt when that
    // changes - but only then. Re-rendering it every poll would throw away the
    // reader's scroll position four times a minute.
    faunaSignature=faunaRoster.length+':'+f.liveCount;
    if(was!==faunaSignature)renderMapDetail();
  }
  function faunaElapsed(){
    return faunaAnchor?faunaAnchor.clock+(faunaNow()-faunaAnchor.perf)/1000:0;
  }
  function faunaLiveOn(islandId){
    if(!faunaStat)return null;
    var rows=faunaStat.islands||[];
    for(var i=0;i<rows.length;i++)if(rows[i].islandId===islandId)return rows[i];
    return null;
  }
  function faunaVisible(){
    var box=$('mapFauna');
    return !!(FAUNA&&faunaRoster.length&&box&&box.checked);
  }
  function faunaInView(row){
    var pad=Math.max(600,mapView.w*0.06);
    var x=row.ox+row.p.cx,y=row.oz-row.p.cz;
    return x>=mapView.x-pad&&x<=mapView.x+mapView.w+pad
        &&y>=mapView.y-pad&&y<=mapView.y+mapView.h+pad;
  }
  function faunaPush(out,kind,row,schoolIndex,memberIndex,t,member){
    var p=row.p;
    var a=member?FAUNA.localPose(p,kind,schoolIndex,memberIndex,t)
                :FAUNA.schoolCentre(p,kind,schoolIndex,t);
    var b=member?FAUNA.localPose(p,kind,schoolIndex,memberIndex,t+FAUNA_HEADING_DT)
                :FAUNA.schoolCentre(p,kind,schoolIndex,t+FAUNA_HEADING_DT);
    // Screen space: x is world east, y is world NORTH NEGATED, as everything
    // else on this map is. The glyph's nose is at -y, so the rotation that
    // aims it along (sx,sy) is atan2(sx,-sy).
    var sx=b.x-a.x,sy=-(b.z-a.z);
    out.push({kind:kind,row:row,member:member,x:row.ox+a.x,y:row.oz-a.z,
      cluster:FAUNA.cluster(kind).r/mapPx,
      deg:(sx*sx+sy*sy)>1e-9?Math.atan2(sx,-sy)*180/Math.PI:0});
  }
  function faunaSpecies(out,row,kind,count,t,inView){
    if(count<=0)return;
    var schools=Math.max(1,Number(row.p.schools)||1);
    if((FAUNA.cluster(kind).r/mapPx)<FAUNA_MEMBER_PIXELS){
      for(var s=0;s<schools;s++)faunaPush(out,kind,row,s,0,t,false);
      return;
    }
    // Members are only worth computing where they can be seen. Off-screen
    // islands keep their schools, which cost two evaluations each.
    if(!inView){for(var q=0;q<schools;q++)faunaPush(out,kind,row,q,0,t,false);return;}
    var size=Math.max(1,Math.round(count/schools));
    for(var j=0;j<schools;j++)
      for(var m=0;m<size;m++)faunaPush(out,kind,row,j,m,t,true);
  }
  function faunaDrawList(t){
    var out=[];
    for(var i=0;i<faunaRoster.length;i++){
      var row=faunaRoster[i],inView=faunaInView(row);
      faunaSpecies(out,row,'manta',row.manta,t,inView);
      // Jellies join from mid zoom. A shoal turns once in ten minutes and
      // breathes in and out over twenty, so at 40 m a pixel it is a stationary
      // dot beside every island - clutter, not life. The mantas lap in minutes
      // and are what reads as alive at that distance.
      if(mapZoomFactor>=2.2)faunaSpecies(out,row,'jelly',row.jelly,t,inView);
    }
    return out;
  }
  function faunaNode(index){
    var n=faunaPool[index];
    if(!n){
      var g=svgEl('g',{});
      var use=svgEl('use',{});
      g.appendChild(use);
      $('mapFaunaLayer').appendChild(g);
      n=faunaPool[index]={g:g,use:use,cls:'',shape:'',hidden:false};
    }
    return n;
  }
  function paintFauna(list){
    var i;
    for(i=0;i<list.length;i++){
      var e=list[i],n=faunaNode(i);
      var cls='fauna '+e.kind+(e.member?' member':' school');
      if(n.cls!==cls){n.g.setAttribute('class',cls);n.cls=cls;}
      // SIZED SO IT NEVER OUTSHOUTS THE ISLAND IT BELONGS TO. At whole-world
      // zoom an island marker is thirteen pixels and a school mark sitting on
      // its shoulder at the same size reads as a second island, which is worse
      // than not drawing it - so a distant school is deliberately small, and it
      // grows once there is room. Members appear only when you are close enough
      // that the island is far larger than any of them.
      // A MEMBER IS SIZED TO ITS OWN SCHOOL. Members appear as soon as the
      // cluster is five pixels across, which is early enough to be useful, and
      // a fixed ten-pixel glyph in a five-pixel cluster is a blob rather than
      // four animals - so the glyph grows with the room it has, to a ceiling
      // that keeps a manta from pretending to be fifty metres long.
      var far=mapZoomFactor<2.2;
      var ceiling=e.kind==='manta'?10:9;
      var size=e.member?Math.max(4.5,Math.min(ceiling,e.cluster*0.85))
                       :(far?7:(e.kind==='manta'?10:8.5));
      size=Math.round(size*2)/2;
      var shape=e.kind+size;
      if(n.shape!==shape){
        n.use.setAttribute('href',e.kind==='manta'?'#mantaSymbol':'#jellySymbol');
        n.use.setAttribute('x',-size/2);n.use.setAttribute('y',-size/2);
        n.use.setAttribute('width',size);n.use.setAttribute('height',size);
        n.shape=shape;
      }
      n.g.setAttribute('transform','translate('+e.x.toFixed(2)+' '+e.y.toFixed(2)
        +') scale('+mapPx+')'+(e.kind==='manta'?(' rotate('+e.deg.toFixed(1)+')'):''));
      if(n.hidden){n.g.style.display='';n.hidden=false;}
    }
    for(;i<faunaPool.length;i++){
      var spare=faunaPool[i];
      if(!spare.hidden){spare.g.style.display='none';spare.hidden=true;}
    }
  }
  function renderFaunaFrame(){
    var layer=$('mapFaunaLayer');if(!layer)return;
    if(!faunaVisible()){
      if(layer.style.display!=='none')layer.style.display='none';
      return;
    }
    if(layer.style.display==='none')layer.style.display='';
    paintFauna(faunaDrawList(faunaElapsed()));
  }
  function fmtShort(seconds){
    seconds=Math.max(0,Math.round(seconds));
    var m=Math.floor(seconds/60),s=seconds%60;
    return m?(m+'m '+(s<10?'0':'')+s+'s'):(s+'s');
  }
  function faunaPhaseText(t){
    var M=worldMap.faunaModel||{},c=FAUNA.cycleFraction(t);
    var day=c>M.dayBeginsAtCycleFraction&&c<M.dayEndsAtCycleFraction;
    var target=day?M.dayEndsAtCycleFraction:M.dayBeginsAtCycleFraction;
    var until=((target-c)%1+1)%1*M.dayNightCycleSeconds;
    return day
      ? ('It is fauna DAY: the shoals have pushed out past the rim and sunk to the underside of the rock. Night in '+fmtShort(until)+'.')
      : ('It is fauna NIGHT: the shoals have drawn back in and risen to the height a player walks at. Day in '+fmtShort(until)+'.');
  }
  function faunaNoteText(){
    if(!FAUNA)return 'Wildlife: the fauna movement model did not load, so no creature is drawn.';
    if(!faunaStat)
      return 'Wildlife: the game server is not reporting an island-fauna roster, so none is drawn. '
        +'Nothing on this map is animated from a guess - no roster and no clock means no creatures.';
    var mantas=0,jellies=0,minSchool=1e9,maxSchool=0,minShoal=1e9,maxShoal=0;
    faunaRoster.forEach(function(r){
      mantas+=r.manta;jellies+=r.jelly;
      if(r.manta){minSchool=Math.min(minSchool,r.manta);maxSchool=Math.max(maxSchool,r.manta);}
      if(r.jelly){minShoal=Math.min(minShoal,r.jelly);maxShoal=Math.max(maxShoal,r.jelly);}
    });
    function span(lo,hi){return lo>hi?'0':(lo===hi?String(lo):(lo+'-'+hi));}
    return 'Wildlife (live): '+plural(mantas+jellies,'creature','creatures')+' on '
      +plural(faunaRoster.length,'island','islands')+' - '+fmt(mantas)+' manta rays in schools of '
      +span(minSchool,maxSchool)+' orbiting their island, '+fmt(jellies)+' jellyfish in shoals of '
      +span(minShoal,maxShoal)+' on a '+fmtShort(Number((worldMap.faunaModel||{}).dayNightCycleSeconds)||0)
      +' day/night cycle. '+faunaPhaseText(faunaElapsed())
      +' These are not sampled positions: the browser evaluates the game server’s own movement '
      +'against the clock the server reports, which is why they move smoothly between snapshots. '
      +'How MANY there are is Wareborn tuning, not Bossa data.';
  }
  function faunaTick(now){
    faunaFrame=requestAnimationFrame(faunaTick);
    var idle=!faunaVisible();
    // Reduced motion still MOVES the wildlife - it is a live fact and freezing
    // it would be a lie - but it steps once a second rather than once a frame,
    // so nothing on the page animates continuously.
    var minimum=idle?FAUNA_IDLE_MS:(prefersReducedMotion()?FAUNA_REDUCED_MOTION_MS:0);
    if(now-faunaLastDrawMs<minimum)return;
    faunaLastDrawMs=now;
    renderFaunaFrame();
    if(now-faunaLastNoteMs>=1000){faunaLastNoteMs=now;text('mapFaunaNote',faunaNoteText());}
  }
  function startFauna(){
    var model=worldMap.faunaModel;
    if(!model||!model.dayNightCycleSeconds)return;
    FAUNA=faunaMotion(model);
    if(faunaFrame===null)faunaFrame=requestAnimationFrame(faunaTick);
  }

  // ---- island-count reconciliation -------------------------------------
  // Two different KINDS of fact sit next to each other on this page: the static
  // preserved-MapFile projection, and the live set of islands this game server
  // is actually simulating. They are different numbers on purpose, so both are
  // always stated together. The live half is only ever READ from the same live
  // stats the Terrain checkout view renders - it is never assumed, defaulted to
  // zero, or back-filled from the map.
  function simulatedIslandCensus(){
    var g=latestGame;
    if(!g||g.reporting!==true)
      return {known:false,condition:'the game server is not reporting'};
    var t=g.terrain;
    if(!t||t.present!==true)
      return {known:false,condition:'this game server predates terrain telemetry (stats schema '
        +(g.schemaVersion||'unknown')+')'};
    if(g.stale)
      return {known:false,condition:'its last stats snapshot is '+Math.round(g.ageSeconds||0)+'s old'};
    return {known:true,count:(t.islands||[]).length};
  }
  function islandReconciliationText(){
    var census=simulatedIslandCensus();
    var simulated=census.known
      ? (census.count+' currently simulated')
      : ('currently simulated count unavailable: '+census.condition);
    var mapCount=(worldMap.islands||[]).length;
    return (mapLoaded&&mapCount>0
      ? (mapCount+' islands on the preserved release map')
      : 'preserved release map not loaded')+' / '+simulated;
  }
  function renderIslandReconciliation(){
    var line=islandReconciliationText();
    text('mapReconcile',line);text('terrainReconcile',line);
  }

  // ---- terrain checkout ------------------------------------------------
  // The state labels below are the SAME vocabulary the game server derives in
  // IslandTerrainStatePolicy. The console renders what it was told; it never
  // re-derives a peer's lifecycle position from raw fields.
  var STATE_LABELS={'absent':'ABSENT','requesting':'REQUESTING','waiting-ack':'WAITING ACK',
    'ready':'READY','draining':'DRAINING','unloading':'UNLOADING',
    'retained-legacy':'RETAINED (LEGACY)','error':'ERROR'};
  function stateLabel(s){return STATE_LABELS[s]||'UNKNOWN';}
  function stateChip(s){
    var span=document.createElement('span');
    span.className='state-chip '+(STATE_LABELS[s]?s:'');
    span.textContent=stateLabel(s);return span;
  }
  function fmtMs(ms){
    ms=Math.max(0,Number(ms)||0);
    if(ms<1000)return ms+'ms';
    if(ms<60000)return (ms/1000).toFixed(1)+'s';
    return Math.round(ms/60000)+'m';
  }
  function terrainPlayerName(p){
    return p.playerEntityId?('entity '+p.playerEntityId):('slot '+p.slot+' · no entity yet');
  }
  function managedIslands(t){return (t.islands||[]).filter(function(i){return i.managed;});}
  function assetText(p){
    if(!p.asset)return '—';
    var a=p.asset;
    return a.islandId+' · '+fmtMs(a.requestAgeMs)
      +(a.retryCount?' · '+a.retryCount+' retries':'')
      +(a.acknowledged?' · exact ack':(a.fallbackDue?' · ack timed out':' · awaiting ack'));
  }
  function clientText(p){
    return (p.mayRemove?'v1 lifecycle':(p.removeSupported?'legacy ack only':'no remove channel'))
      +(p.legacyRetaining?' · retaining':'');
  }
  function terrainDetailRow(p,columns){
    var tr=document.createElement('tr');tr.className='terrain-detail';
    var td=document.createElement('td');td.colSpan=columns;
    var kv=document.createElement('div');kv.className='terrain-kv';
    function item(label,value){
      var d=document.createElement('div');var b=document.createElement('b');b.textContent=label;
      var s=document.createElement('span');s.textContent=value;d.appendChild(b);d.appendChild(s);kv.appendChild(d);
    }
    item('World centre',Number(p.x).toFixed(1)+', '+Number(p.y).toFixed(1)+', '+Number(p.z).toFixed(1));
    item('Confirmed ground',p.confirmedGroundIslandId||'not confirmed');
    item('Requested destination',p.requestedDestinationIslandId
      ?(p.requestedDestinationIslandId+(p.destinationWaiting?' · waiting':' · ready')):'none');
    item('Pending action',p.pendingAction==='none'?'idle':(p.pendingAction+' '+(p.pendingIslandId||'')));
    item('Cold asset flight',assetText(p));
    item('Correlated ack observed',p.correlatedAckObserved?'yes':'no');
    item('RemoveEntity support',p.removeSupported?'yes':'no (retain-visited compatibility)');
    item('Connect plan',p.connectPlanComplete
      ?(p.settleWaiting?'complete · settle delay running':'complete'):'incomplete');
    item('Checked-out terrain',String(p.readyCount||0));
    item('Warning',p.warning||'none');
    td.appendChild(kv);tr.appendChild(td);return tr;
  }
  function renderTerrainMatrix(t){
    var islands=managedIslands(t);
    var head=$('terrainMatrixHead');
    while(head.children.length>6)head.removeChild(head.lastChild);
    islands.forEach(function(i){
      var th=document.createElement('th');th.className='island-col';
      th.textContent=i.displayName;th.title=i.islandId;head.appendChild(th);
    });
    var columns=6+islands.length;
    var query=($('terrainSearch').value||'').toLowerCase().trim();
    var rows=(t.players||[]).filter(function(p){
      if(!query)return true;
      var hay=(terrainPlayerName(p)+' '+(p.confirmedGroundIslandId||'')+' '
        +(p.requestedDestinationIslandId||'')+' '+(p.pendingIslandId||'')+' '+p.pendingAction+' '
        +(p.warning||'')+' '+(p.islands||[]).map(function(c){
          return c.islandId+' '+stateLabel(c.state);}).join(' ')).toLowerCase();
      return hay.indexOf(query)>=0;
    });
    var body=$('terrainPlayers');clear(body);
    var shown=rows.slice(0,200);
    shown.forEach(function(p){
      var tr=document.createElement('tr');tr.className='player-row';tr.tabIndex=0;
      var expanded=terrainExpandedSlot===p.slot;
      tr.setAttribute('aria-expanded',expanded?'true':'false');
      tr.setAttribute('aria-label','Terrain lifecycle for '+terrainPlayerName(p));
      function toggle(){terrainExpandedSlot=expanded?-1:p.slot;renderTerrainMatrix(t);}
      tr.addEventListener('click',toggle);
      tr.addEventListener('keydown',function(e){if(e.key==='Enter'||e.key===' '){e.preventDefault();toggle();}});
      cell(tr,terrainPlayerName(p));
      cell(tr,p.confirmedGroundIslandId||'—',p.confirmedGroundIslandId?'':'muted');
      cell(tr,p.requestedDestinationIslandId||'—',p.requestedDestinationIslandId?'':'muted');
      cell(tr,p.pendingAction==='none'?'—':(p.pendingAction+' '+(p.pendingIslandId||'')),
        p.pendingAction==='none'?'muted':'');
      cell(tr,assetText(p),p.asset?'':'muted');
      var client=cell(tr,clientText(p));
      if(p.warning){
        var pill=document.createElement('span');pill.className='pill warn';pill.textContent='warning';
        client.insertBefore(document.createTextNode(' '),client.firstChild);
        client.insertBefore(pill,client.firstChild);
        client.title=p.warning;
      }
      islands.forEach(function(i){
        var found=null;
        (p.islands||[]).forEach(function(c){if(c.islandId===i.islandId)found=c;});
        var td=document.createElement('td');
        td.appendChild(stateChip(found?found.state:'absent'));tr.appendChild(td);
      });
      body.appendChild(tr);
      if(expanded)body.appendChild(terrainDetailRow(p,columns));
    });
    text('terrainMatrixCount',rows.length+' player'+(rows.length===1?'':'s')
      +(rows.length>shown.length?' · first '+shown.length+' shown':''));
    $('noTerrainPlayers').style.display=(t.players||[]).length?'none':'block';
    text('terrainMatrixNote',islands.length
      ?'One row per tracked peer; select a row for its lifecycle detail.'
      :'No island is stream-managed, so the matrix has no lifecycle columns.');
  }
  function islandRegistration(i){
    if(i.unconditional)return {label:'unconditional',cls:'pill'};
    if(i.managed)return {label:'managed',cls:'pill ok'};
    if(!i.registered)return {label:'not registered',cls:'pill warn'};
    if(!i.locallyOwned)return {label:'not locally owned',cls:'pill warn'};
    if(!i.hasEnvelope)return {label:'no extracted envelope',cls:'pill warn'};
    return {label:'not managed',cls:'pill warn'};
  }
  function lastEventFor(t,islandId){
    var events=t.events||[];
    for(var i=0;i<events.length;i++)if(events[i].islandId===islandId)return events[i];
    return null;
  }
  function renderTerrainIslands(t){
    var query=($('terrainSearch').value||'').toLowerCase().trim();
    var rows=(t.islands||[]).filter(function(i){
      if(!query)return true;
      return (i.islandId+' '+i.displayName).toLowerCase().indexOf(query)>=0;
    });
    var body=$('terrainIslands');clear(body);
    rows.forEach(function(i){
      var tr=document.createElement('tr');
      var name=cell(tr,i.displayName);
      var id=document.createElement('div');id.className='island-id';id.textContent=i.islandId
        +(i.terrainEntityId?' · entity '+i.terrainEntityId:' · unbound');
      name.appendChild(id);
      var reg=islandRegistration(i);
      var regCell=cell(tr,'');
      var pill=document.createElement('span');pill.className=reg.cls;pill.textContent=reg.label;
      regCell.appendChild(pill);
      cell(tr,String(i.readyPeerCount),'num');
      cell(tr,String(i.loadingPeerCount),'num');
      cell(tr,String(i.drainingPeerCount),'num');
      cell(tr,String(i.unloadingPeerCount),'num');
      cell(tr,String(i.retainedLegacyPeerCount),'num');
      cell(tr,String(i.errorPeerCount),'num');
      cell(tr,i.resourceNodeCount<0
        ?'unknown'
        :(i.resourceNodeCount+' nodes · '+i.checkedOutResourceCount+' checked out'
          +(i.resourceDrainWired?'':' · drain not wired')),
        i.resourceNodeCount<0?'muted':'');
      cell(tr,i.envelope
        ?(Math.round(i.envelope.spanX)+'×'+Math.round(i.envelope.spanY)+'×'+Math.round(i.envelope.spanZ)+' m')
        :'—',i.envelope?'':'muted');
      var last=lastEventFor(t,i.islandId);
      cell(tr,last?(last.kind+' · '+fmtMs(last.ageMs)+' ago'):'—',last?'':'muted');
      body.appendChild(tr);
    });
    text('terrainIslandCount',rows.length+' island'+(rows.length===1?'':'s'));
    $('noTerrainIslands').style.display=(t.islands||[]).length?'none':'block';
  }
  function renderTerrainEvents(t){
    var list=$('terrainEvents');clear(list);
    var events=(t.events||[]).slice(0,40);
    events.forEach(function(e){
      var li=document.createElement('li');li.className='event-line'+(e.success?'':' bad');
      var age=document.createElement('span');age.className='age';age.textContent=fmtMs(e.ageMs);
      var kind=document.createElement('span');kind.textContent=e.kind;
      var who=document.createElement('span');
      who.textContent=(e.islandId||'—')+' · '+(e.playerEntityId?('entity '+e.playerEntityId):('slot '+e.slot));
      var ok=document.createElement('span');ok.textContent=e.success?'ok':'failed';
      li.appendChild(age);li.appendChild(kind);li.appendChild(who);li.appendChild(ok);
      list.appendChild(li);
    });
    $('noTerrainEvents').style.display=events.length?'none':'block';
    text('terrainEventNote',(t.events||[]).length+' of '+(t.eventCapacity||0)+' retained'
      +((t.events||[]).length>events.length?' · newest 40 shown':''));
  }
  function prereqChip(container,ok,label){
    var pill=document.createElement('span');pill.className='pill '+(ok?'ok':'warn');
    pill.textContent=(ok?'✓ ':'· ')+label;container.appendChild(pill);
  }
  function renderAcceptance(t,reporting){
    var box=$('acceptancePrereq');clear(box);
    var mental=null,haven=null;
    (t.islands||[]).forEach(function(i){
      if(i.islandId==='mental-facility')mental=i;
      if(i.islandId==='haven')haven=i;
    });
    var playerSelected=$('targetPlayer').value!=='';
    var mentalReady=!!(mental&&mental.managed);
    prereqChip(box,reporting&&!!gameReporting,'fresh game status');
    prereqChip(box,t.mode==='on','terrain checkout on');
    prereqChip(box,!!haven,'Haven registered');
    prereqChip(box,mentalReady,'Mental Facility stream-managed');
    prereqChip(box,playerSelected,'live player selected');
    var travel=$('acceptanceTravel'),back=$('acceptanceReturn');
    var mentalButton=$('mentalFacilityTravel'),havenButton=$('havenTravel');
    travel.disabled=mentalButton.disabled||!playerSelected||!gameReporting||t.mode!=='on';
    back.disabled=havenButton.disabled||!playerSelected||!gameReporting;
    text('acceptanceNote',t.mode==='on'
      ?'Both steps dispatch the same allowlisted, CSRF-bound travel commands as the Operations panel.'
      :'Travel steps stay disabled until terrain checkout is actually running on the game server.');
  }
  function renderTerrain(g,reporting){
    var t=(g&&g.terrain)||null;
    var unavailable=$('terrainUnavailable');
    var present=!!(t&&t.present);
    var mode=t?t.mode:'unknown';
    var modePill=$('terrainMode');
    modePill.className='pill '+(mode==='on'?'ok':(mode==='unknown'?'':'warn'));
    modePill.textContent=reporting?mode:'not reporting';
    text('terrainHost',(t&&t.hostId&&t.hostId!=='unknown')?t.hostId:'local:primary');
    if(!t)t={present:false,mode:'unknown',islands:[],players:[],events:[],stateCounts:{}};

    // Freshness and mode are separate facts and are reported as both: a stale
    // snapshot of a prerequisite-disabled server must not hide either half.
    var messages=[];
    if(!reporting)messages.push('The game server is not reporting, so its terrain lifecycle is unknown.');
    else if(g.stale)messages.push('These terrain figures are '+Math.round(g.ageSeconds)
      +'s old and may no longer be true.');
    if(reporting&&!present)messages.push('This game server predates terrain telemetry (stats schema '
      +(g.schemaVersion||'unknown')+'). Its terrain lifecycle cannot be reported.');
    else if(reporting&&mode==='off')messages.push('Terrain checkout is off.'
      +' Optional island terrain stays in the immutable connect plan.');
    else if(reporting&&mode==='prerequisite-disabled')messages.push('Terrain checkout was requested'
      +' but is safely disabled: resource interest must also be enabled so resources can never'
      +' outlive their terrain.');
    var message=messages.join(' ');
    unavailable.style.display=message?'block':'none';
    text('terrainUnavailable',message);

    var live=reporting&&present;
    text('terrainState',live?mode:'—');
    text('terrainCandidates',live?String(t.candidateCount||0):'—');
    text('terrainPeers',live?String(t.trackedPeerCount||0):'—');
    text('terrainReady',live?String(t.readyCount||0):'—');
    text('terrainWarnings',live?String(t.warningCount||0):'—');
    text('terrainErrors',live?String(t.errorCount||0):'—');
    text('terrainRadii',live?(Math.round(t.loadRadiusMetres)+' / '+Math.round(t.unloadRadiusMetres)):'—');
    text('terrainTimings',live?(fmtMs(t.assetAckTimeoutMs)+' / '+fmtMs(t.settleDelayMs)):'—');

    var warnings=[];
    (t.players||[]).forEach(function(p){
      if(p.warning)warnings.push(terrainPlayerName(p)+': '+p.warning);
    });
    if((t.errorCount||0)>0)warnings.push(t.errorCount+' peer/island lifecycle steps have failed');
    var banner=$('terrainBanner');
    if(live&&warnings.length){banner.classList.add('show');text('terrainBannerText',warnings.join('; ')+'.');}
    else banner.classList.remove('show');

    renderTerrainMatrix(t);
    renderTerrainIslands(t);
    renderTerrainEvents(t);
    renderAcceptance(t,reporting);
    latestTerrain=t;
  }

  function render(data){
    if(!data){return;}
    text('serverName',data.serverName||'(unnamed server)');
    var input=$('server-name-input');
    if(input && document.activeElement!==input && !input.dataset.touched){input.value=data.serverName||'';}

    var g=data.game||{};
    latestGame=g;
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
    firstRegionTerrainCount=gameReporting?Math.max(0,Number(g.firstRegionTerrainCount)||0):0;
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
    latestPlayers=players;
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
    renderLiveWorldMap(reporting,g.ageSeconds);
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
    var mental=$('mentalFacilityTravel');
    mental.disabled=firstRegionTerrainCount<1;
    text('islandRequirement',(secondIslandRegistered||firstRegionTerrainCount>0)
      ? 'Live terrain: '+(secondIslandRegistered?'Trades Challenge':'')
          +(secondIslandRegistered&&firstRegionTerrainCount>0?' · ':'')
          +(firstRegionTerrainCount>0?firstRegionTerrainCount+' tier-1 B3 island'+(firstRegionTerrainCount===1?'':'s'):'')
      : 'Optional travel is unavailable until its terrain is registered and freshly reported.');

    // After the travel controls and the player selector, because the acceptance
    // panel reports THEIR live prerequisites rather than duplicating them.
    renderTerrain(g,reporting);
    // Both provenance labels state the SAME reconciled pair of counts, so the
    // map and the terrain inventory can never appear to disagree.
    renderIslandReconciliation();

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
    try{worldMap=JSON.parse($('releaseWorldMap').textContent);mapLoaded=true;renderStaticWorldMap();}catch(e){text('mapStatus','Preserved geography could not be loaded.');}
    try{render(JSON.parse($('bootstrap').textContent));}catch(e){}
    renderIslandReconciliation();
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
      .then(function(){button.disabled=false;if(button.id==='tradesTravel'&&!secondIslandRegistered)button.disabled=true;if(button.id==='mentalFacilityTravel'&&firstRegionTerrainCount<1)button.disabled=true;});
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
  $('terrainSearch').addEventListener('input',function(){
    if(latestTerrain){renderTerrainMatrix(latestTerrain);renderTerrainIslands(latestTerrain);}
  });
  // The acceptance run drives the EXISTING guarded travel controls rather than
  // owning a second command path: same allowlist, same CSRF, same journal entry.
  $('acceptanceTravel').addEventListener('click',function(){$('mentalFacilityTravel').click();});
  $('acceptanceReturn').addEventListener('click',function(){$('havenTravel').click();});
  $('targetPlayer').addEventListener('change',function(){
    if(latestTerrain)renderAcceptance(latestTerrain,gameReporting);
  });
  Array.prototype.forEach.call(document.querySelectorAll('[data-domain-filter]'),function(button){button.addEventListener('click',function(){domainFilter=button.dataset.domainFilter;Array.prototype.forEach.call(document.querySelectorAll('[data-domain-filter]'),function(other){other.classList.toggle('active',other===button);});renderDomainInventory();});});
  ['mapBiomes','mapIslands','mapWalls','mapShips','mapPlayers','mapFauna'].forEach(function(id){$(id).addEventListener('change',function(){renderLiveWorldMap(gameReporting,0);renderFaunaFrame();});});
  // ONE search drives the map and the ledger. A second box under the table was
  // a second thing to notice and a second thing to keep in sync.
  ['ledgerFilter','ledgerInferredOnly'].forEach(function(id){var e=$(id);if(e){e.addEventListener('input',applyMapFilter);e.addEventListener('change',applyMapFilter);}});
  $('ledgerFilter').addEventListener('focus',function(){if(mapSearchQuery())applyMapFilter();});
  document.addEventListener('click',function(e){
    if(!e.target.closest||!e.target.closest('.map-search'))closeSearchResults();});
  document.addEventListener('keydown',function(e){
    if(e.key!=='Escape')return;
    closeSearchResults();
    if(document.activeElement===$('ledgerFilter'))return;
    if(mapSelection.kind!=='world')selectWorld();
  });
  // Clicking bare ocean returns the panel to the world overview, so it is never
  // left holding one island after you have moved on.
  $('liveWorldMap').addEventListener('click',function(){if(!mapDragged)selectWorld();});
  $('mapZoomIn').addEventListener('click',function(){zoomMap(.62);});
  $('mapZoomOut').addEventListener('click',function(){zoomMap(1.6);});
  $('mapReset').addEventListener('click',function(){resetMapView();selectWorld();});
  $('liveWorldMap').addEventListener('wheel',function(e){e.preventDefault();var p=mapClientPoint(e);zoomMap(e.deltaY<0 ? .8 : 1.25,p.x,p.y);},{passive:false});
  window.addEventListener('resize',function(){mapAppliedPx=0;applyMapView();});
  (function(){
    var svg=$('liveWorldMap'),drag=null;
    // THE POINTER IS CAPTURED ONLY ONCE A DRAG ACTUALLY STARTS, and this is not
    // a detail. Capturing on pointerdown retargets the compatibility mouse
    // events too, so every `click` on an island marker was delivered to the SVG
    // instead of the marker - which meant clicking an island silently reset the
    // panel to the world overview and the per-island detail looked like it did
    // not exist. Deferring the capture past a 3 px threshold keeps a plain
    // click on its own target while still giving a real drag pointer events
    // that follow the cursor outside the element.
    svg.addEventListener('pointerdown',function(e){
      if(e.button!==0&&e.pointerType==='mouse')return;
      drag={x:e.clientX,y:e.clientY,vx:mapView.x,vy:mapView.y,id:e.pointerId,captured:false};
      mapDragged=false;
    });
    // Panning moves the viewBox only; nothing needs rescaling, which is why the
    // 266 markers stay smooth under the cursor. One screen pixel is mapPx world
    // metres on BOTH axes, because the square viewBox is letterboxed to fit.
    svg.addEventListener('pointermove',function(e){
      if(!drag||e.pointerId!==drag.id)return;
      if(!mapDragged){
        if(Math.abs(e.clientX-drag.x)<=3&&Math.abs(e.clientY-drag.y)<=3)return;
        mapDragged=true;hideHover();fadeMapHint();
        svg.classList.add('dragging');
        try{svg.setPointerCapture(e.pointerId);drag.captured=true;}catch(err){}
      }
      mapView.x=drag.vx-(e.clientX-drag.x)*mapPx;
      mapView.y=drag.vy-(e.clientY-drag.y)*mapPx;
      applyMapView();
    });
    function end(e){
      if(!drag)return;
      svg.classList.remove('dragging');
      if(drag.captured&&svg.hasPointerCapture(drag.id))svg.releasePointerCapture(drag.id);
      drag=null;
      // The click event lands after pointerup, so mapDragged must survive until
      // then; clear it on the next tick instead of here.
      if(mapDragged)setTimeout(function(){mapDragged=false;},0);
    }
    svg.addEventListener('pointerup',end);svg.addEventListener('pointercancel',end);
    svg.addEventListener('pointerleave',hideHover);
  })();
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
