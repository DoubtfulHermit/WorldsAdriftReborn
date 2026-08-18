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
        /// Self-contained, high-density simulation-console design system. The world
        /// map's tier colours are appended from <see cref="MapTierPalette"/> rather
        /// than written here, so the cell fill, the cell label ink and the legend
        /// swatch cannot drift apart.
        /// </summary>
        private static readonly string Style = StyleHead + MapTierPalette.Css() + "</style>";

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
.world-map{border:1px solid var(--line);border-radius:10px;background:#071017;overflow:hidden;margin-bottom:1rem}.world-map-bar{display:flex;justify-content:space-between;gap:.8rem;align-items:center;flex-wrap:wrap;padding:.72rem .9rem;border-bottom:1px solid var(--line);background:rgba(22,35,45,.65)}.world-map-title strong{display:block;font-size:.68rem;letter-spacing:.08em;text-transform:uppercase}.world-map-title span{display:block;margin-top:.1rem;font-size:.59rem;color:var(--text-faint)}.map-controls{display:flex;align-items:center;flex-wrap:wrap;gap:.35rem}.map-controls button{min-height:2rem;padding:.3rem .58rem;font-size:.62rem}.map-toggle{display:inline-flex;align-items:center;gap:.28rem;padding:.28rem .45rem;border:1px solid var(--line);border-radius:6px;color:var(--text-soft);font-size:.58rem;text-transform:none;letter-spacing:0;margin:0}.map-toggle input{width:auto;min-height:0;margin:0;accent-color:var(--accent)}.world-map-stage{position:relative;height:clamp(25rem,62vh,48rem);overflow:hidden;background:#071017}.world-map-stage svg{display:block;width:100%;height:100%;touch-action:none;cursor:grab}.world-map-stage svg.dragging{cursor:grabbing}.map-ocean{fill:#09151d}.map-world-boundary{fill:none;stroke:#536b78;stroke-width:1.5;vector-effect:non-scaling-stroke}.map-haven-zone{fill:#17322f;opacity:.72}.map-grid line{stroke:#39515d;stroke-width:1;opacity:.35;vector-effect:non-scaling-stroke}.map-biome{stroke:#233a45;stroke-width:1;vector-effect:non-scaling-stroke;cursor:pointer;transition:stroke .12s,stroke-width .12s}.map-biome:hover,.map-biome:focus{stroke:#74c9cf;stroke-width:3;outline:none}.map-biome.unassigned{stroke-dasharray:6 4}.map-cell-label,.map-zone-label{fill:#dce8ed;font:700 330px/1 ui-sans-serif,sans-serif;letter-spacing:.1em;text-anchor:middle;pointer-events:none;paint-order:stroke;stroke:#071017;stroke-width:55;stroke-linejoin:round}.map-cell-label .tier{fill:#c0d0d7;font-size:210px;letter-spacing:.055em}.map-cell-label.unassigned{font-size:270px}.map-zone-label{fill:#8dc8b1;font-size:300px}.map-island{fill:#80939c;stroke:#c0cbd0;stroke-width:1;vector-effect:non-scaling-stroke}.map-island.haven{fill:#71d0a5;stroke:#d6fff0}.map-wall-halo{fill:none;stroke:#071017;stroke-width:5;opacity:.8;stroke-linecap:round;stroke-linejoin:round;vector-effect:non-scaling-stroke}.map-wall{fill:none;stroke-width:2.5;opacity:.98;stroke-linecap:round;stroke-linejoin:round;vector-effect:non-scaling-stroke}.map-wall.type-0{stroke:#74c9cf}.map-wall.type-1{stroke:#9b86d8}.map-wall.type-2{stroke:#d48388}.map-wall.type-3{stroke:#e8963c}.map-wall.type-4{stroke:#a9d6ed}.map-wall.type-5{stroke:#ec8f88;stroke-width:3}.map-runtime-island{fill:none;stroke:#71d0a5;stroke-width:2.5;vector-effect:non-scaling-stroke}.map-ship{fill:#8aa6ff;stroke:#f3f7ff;stroke-width:1.5;vector-effect:non-scaling-stroke}.map-ship.resting{fill:#50647d}.map-player{fill:#71d0a5;stroke:#edfff7;stroke-width:1.5;vector-effect:non-scaling-stroke}.map-marker{cursor:pointer}.map-marker:focus{outline:none}.map-marker:focus .map-ship,.map-marker:focus .map-player{stroke:#fff;stroke-width:3}.map-inspector{position:absolute;top:.75rem;left:.75rem;max-width:min(25rem,calc(100% - 1.5rem));padding:.6rem .72rem;border:1px solid rgba(116,201,207,.34);border-radius:7px;background:rgba(7,15,21,.92);box-shadow:0 8px 24px rgba(0,0,0,.25);pointer-events:none}.map-inspector b{display:block;font-size:.65rem}.map-inspector span{display:block;margin-top:.12rem;color:var(--text-faint);font:500 .58rem/1.45 ui-monospace,SFMono-Regular,Consolas,monospace}.map-compass{position:absolute;right:.8rem;top:.8rem;width:2rem;height:2rem;border:1px solid var(--line-strong);border-radius:50%;display:grid;place-items:center;background:rgba(7,15,21,.8);font-size:.58rem;font-weight:750;color:var(--text-soft);pointer-events:none}.map-scale{position:absolute;right:.8rem;bottom:.75rem;padding:.18rem .35rem;border-bottom:2px solid var(--text-soft);color:var(--text-soft);font-size:.54rem;pointer-events:none}.world-map-legend{display:flex;flex-wrap:wrap;gap:.5rem .8rem;padding:.62rem .9rem;border-top:1px solid var(--line);color:var(--text-faint);font-size:.58rem}.map-legend-break{flex-basis:100%;height:0}.world-map-legend .legend-lead{flex-basis:100%;color:var(--text-soft);font-weight:650;letter-spacing:.04em}.map-swatch{display:inline-block;width:1rem;height:.16rem;margin-right:.3rem;vertical-align:middle;background:var(--accent)}.map-swatch.tier{height:.6rem;border:1px solid #6f7d85}.map-swatch.storm{background:#9b86d8}.map-swatch.sand{background:#e8963c}.map-swatch.edge{background:#ec8f88}.map-swatch.haven{height:.48rem;background:#173f37;border:1px solid #71d0a5}.map-swatch.ship,.map-swatch.player{width:.48rem;height:.48rem;border-radius:2px;background:#8aa6ff}.map-swatch.player{border-radius:50%;background:#71d0a5}.map-swatch.runtime{width:.5rem;height:.5rem;border-radius:50%;background:transparent;border:1px solid #71d0a5}.map-provenance{display:flex;flex-wrap:wrap;align-items:baseline;gap:.5rem .8rem;padding:.6rem .9rem;border-bottom:1px solid var(--line);background:rgba(116,201,207,.045);color:var(--text-faint);font-size:.62rem;line-height:1.6}.map-provenance strong{color:var(--text-soft)}.map-provenance-text{flex:1 1 24rem;min-width:0}
.provenance-tag{flex:0 0 auto;padding:.14rem .45rem;border:1px solid rgba(116,201,207,.42);border-radius:999px;color:var(--accent);font-size:.53rem;font-weight:750;letter-spacing:.11em;text-transform:uppercase;white-space:nowrap}.provenance-tag.live{border-color:rgba(113,208,165,.45);color:var(--good)}
.count-reconcile{display:inline-block;padding:.16rem .48rem;border:1px solid var(--line-strong);border-radius:5px;background:#0b141b;color:var(--text-soft);font:700 .58rem/1.45 ui-monospace,SFMono-Regular,Consolas,monospace;overflow-wrap:anywhere}
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
      <div class=""map-controls"">
        <label class=""map-toggle""><input type=""checkbox"" id=""mapBiomes"" checked>zones</label>
        <label class=""map-toggle""><input type=""checkbox"" id=""mapIslands"" checked>islands</label>
        <label class=""map-toggle""><input type=""checkbox"" id=""mapWalls"" checked>walls</label>
        <label class=""map-toggle""><input type=""checkbox"" id=""mapShips"" checked>ships</label>
        <label class=""map-toggle""><input type=""checkbox"" id=""mapPlayers"" checked>players</label>
        <button type=""button"" id=""mapZoomIn"" aria-label=""Zoom map in"">+</button><button type=""button"" id=""mapZoomOut"" aria-label=""Zoom map out"">&minus;</button><button type=""button"" id=""mapReset"">Whole world</button>
      </div>
    </div>
    <div class=""map-provenance"">
      <span class=""provenance-tag"">map evidence</span>
      <span class=""map-provenance-text""><strong>Island geometry, tier/biome cells, weather walls and the world boundary are a static embedded projection of the preserved Bossa release MapFile.</strong> They are historical map evidence, not live simulation state, and they do not change when the game server does. <strong>Only the ship and player markers, and the ring drawn around each simulated island domain, are live:</strong> this browser refreshes them every 4 seconds from the game server's roughly 3-second stats snapshots. The islands this server is actually simulating are the ones listed under Terrain checkout, not the ones drawn here.</span>
      <span class=""count-reconcile"" id=""mapReconcile"">Reconciling island counts&hellip;</span>
    </div>
    <div class=""world-map-stage""><svg id=""liveWorldMap"" role=""img"" aria-label=""Preserved release-world map evidence - tiered biome cells, Haven corridor, weather walls and island placements - with a live overlay of authoritative ships, players and currently simulated island domains""><defs><symbol id=""releaseIslandSymbol"" viewBox=""-90 -90 180 180""><path d=""M0 -70 62 -22 48 54 -8 72 -67 30 -55 -38Z""></path></symbol><symbol id=""havenIslandSymbol"" viewBox=""-110 -110 220 220""><circle r=""80""></circle><path d=""M0 -58 51 -18 39 44 -6 59 -55 25 -45 -31Z"" fill=""#d6fff0""></path></symbol><symbol id=""shipSymbol"" viewBox=""-170 -170 340 340""><path d=""M0 -145 112 98 0 58 -112 98Z""></path></symbol><symbol id=""playerSymbol"" viewBox=""-145 -145 290 290""><circle r=""105""></circle></symbol><clipPath id=""worldClip""><rect id=""worldClipRect""></rect></clipPath></defs><rect id=""mapOcean"" class=""map-ocean""></rect><g clip-path=""url(#worldClip)""><g id=""mapBiomeLayer""></g><g id=""mapHavenLayer""></g><g id=""mapGrid"" class=""map-grid""></g><g id=""mapWallLayer""></g><g id=""mapIslandLayer""></g><g id=""mapRuntimeIslandLayer""></g><g id=""mapShipLayer""></g><g id=""mapPlayerLayer""></g></g><rect id=""mapWorldBoundary"" class=""map-world-boundary""></rect></svg><div class=""map-inspector"" id=""mapInspector""><b>Release world overview</b><span>Select a zone, ship, or player for exact authored/runtime details.</span></div><div class=""map-compass"">N</div><div class=""map-scale"" id=""mapScale"">6 km</div><div class=""map-empty"" id=""mapLiveNote"">No live positions reported.</div></div>
    <div class=""world-map-legend""><span class=""legend-lead"">Island tier, low to high &mdash; a sequential ramp, so lighter always means a higher tier:</span><span><i class=""map-swatch tier tier-1""></i>T1 Wilderness &middot; temperate</span><span><i class=""map-swatch tier tier-2""></i>T2 Expanse &middot; highlands</span><span><i class=""map-swatch tier tier-3""></i>T3 Remnants &middot; ice</span><span><i class=""map-swatch tier tier-4""></i>T4 Badlands &middot; desert</span><span class=""map-legend-break""></span><span><i class=""map-swatch haven""></i>Haven corridor</span><span><i class=""map-swatch""></i>Wind Rift</span><span><i class=""map-swatch storm""></i>Storm Rift</span><span><i class=""map-swatch sand""></i>Sand Storm</span><span><i class=""map-swatch edge""></i>Haven separator / World End</span><span><i class=""map-swatch ship""></i>Ship (live)</span><span><i class=""map-swatch player""></i>Player (live)</span><span><i class=""map-swatch runtime""></i>Currently simulated island domain (live)</span><span>Every other mark is preserved map evidence</span><span>Drag to pan &middot; wheel to zoom &middot; X east / Z north</span></div>
    <div class=""map-authenticity""><strong>Release MapFile geometry.</strong> The map contains 20 distinct tier/biome cells: 18 have authored district IDs and two Tier-4 Badlands cells are explicitly unassigned. E3 is one cell; the adjacent unnamed cells are not silently invented as E1/E2 or merged into E3. Haven is inside the 36&times;36 km boundary, east of the authored separator, with 12 preserved starter-island placements. None of this geometry is read from the running game server, and none of it is evidence that any of these islands is currently simulated.</div>
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

  // ---- live release-world map ------------------------------------------
  var SVG_NS='http://www.w3.org/2000/svg';
  function svgEl(name,attrs,title){
    var e=document.createElementNS(SVG_NS,name);
    Object.keys(attrs||{}).forEach(function(k){e.setAttribute(k,String(attrs[k]));});
    if(title){var t=document.createElementNS(SVG_NS,'title');t.textContent=title;e.appendChild(t);}
    return e;
  }
  function applyMapView(){
    $('liveWorldMap').setAttribute('viewBox',[mapView.x,mapView.y,mapView.w,mapView.h].join(' '));
    var raw=mapView.w/5,power=Math.pow(10,Math.floor(Math.log(raw)/Math.LN10)),unit=raw/power;
    var nice=(unit>=5?5:(unit>=2?2:1))*power;
    text('mapScale',(nice/1000).toFixed(nice<1000?1:0)+' km');
  }
  function resetMapView(){
    var edge=Math.max(1,Number(worldMap.worldEdgeLength)||36000);
    mapView={x:-edge/2,y:-edge/2,w:edge,h:edge};applyMapView();
  }
  function zoomMap(factor,cx,cy){
    var edge=Math.max(1,Number(worldMap.worldEdgeLength)||36000);
    var next=Math.max(edge/32,Math.min(edge,mapView.w*factor));
    var ratio=next/mapView.w;cx=cx==null?mapView.x+mapView.w/2:cx;cy=cy==null?mapView.y+mapView.h/2:cy;
    mapView={x:cx-(cx-mapView.x)*ratio,y:cy-(cy-mapView.y)*ratio,w:next,h:next};applyMapView();
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
  function mapCellDetail(b,index,hasDistrict){
    var info=biomeInfo(b.type),culture=Number(b.civilization)===1?'Kioki':'Saborian';
    return 'Tier '+b.type+' · '+info.name+' ('+info.terrain+') · '+culture+' · source cell '+(index+1)+'/20 · '
      +(hasDistrict?('authored district '+b.district):'District is null in Bossa release MapFile; no name inferred')
      +' · center X/Z '+Number(b.x).toFixed(1)+', '+Number(b.z).toFixed(1);
  }
  function pathFromSegments(segments){return segments.map(function(w){return 'M '+Number(w.x1)+' '+(-Number(w.z1))+' L '+Number(w.x2)+' '+(-Number(w.z2));}).join(' ');}
  function renderStaticWorldMap(){
    var grid=$('mapGrid'),walls=$('mapWallLayer'),islands=$('mapIslandLayer'),biomes=$('mapBiomeLayer'),haven=$('mapHavenLayer');
    clear(grid);clear(walls);clear(islands);clear(biomes);clear(haven);
    var edge=Math.max(1,Number(worldMap.worldEdgeLength)||36000),half=edge/2,separator=Number(worldMap.havenSeparatorX)||15943.6523;
    ['mapOcean','mapWorldBoundary','worldClipRect'].forEach(function(id){var node=$(id);node.setAttribute('x',-half);node.setAttribute('y',-half);node.setAttribute('width',edge);node.setAttribute('height',edge);});
    (worldMap.biomes||[]).forEach(function(b,index){
      var poly=biomeCell(b,worldMap.biomes,half,separator);if(!poly.length)return;
      var hasDistrict=typeof b.district==='string'&&b.district.trim().length>0,info=biomeInfo(b.type);
      var heading=hasDistrict?('District '+b.district):'Unassigned district';
      var detail=mapCellDetail(b,index,hasDistrict);
      var path=svgEl('path',{d:'M '+poly.map(function(p){return p.x+' '+p.y;}).join(' L ')+' Z','class':'map-biome type-'+b.type+(hasDistrict?'':' unassigned'),tabindex:'0',role:'button','aria-label':heading+' · Tier '+b.type+' '+info.name},heading+' · '+detail);
      path.addEventListener('click',function(e){e.stopPropagation();inspectMap(heading,detail);});
      path.addEventListener('keydown',function(e){if(e.key==='Enter'||e.key===' '){e.preventDefault();inspectMap(heading,detail);}});
      biomes.appendChild(path);
      var label=svgEl('text',{x:b.x,y:-b.z-90,'class':'map-cell-label type-'+b.type+(hasDistrict?'':' unassigned')});
      var districtLine=svgEl('tspan',{x:b.x,dy:'0'});districtLine.textContent=hasDistrict?b.district:'NO DISTRICT';label.appendChild(districtLine);
      var tierLine=svgEl('tspan',{x:b.x,dy:'300','class':'tier'});tierLine.textContent='T'+b.type+' · '+info.name;label.appendChild(tierLine);
      biomes.appendChild(label);
    });
    haven.appendChild(svgEl('rect',{x:separator,y:-half,width:half-separator,height:edge,'class':'map-haven-zone'},'Authored Haven reserve corridor'));
    var zoneLabel=svgEl('text',{x:(separator+half)/2,y:0,'class':'map-zone-label',transform:'rotate(-90 '+((separator+half)/2)+' 0)'});zoneLabel.textContent='HAVEN CORRIDOR';haven.appendChild(zoneLabel);
    for(var p=-half;p<=half;p+=6000){
      grid.appendChild(svgEl('line',{x1:p,y1:-half,x2:p,y2:half}));
      grid.appendChild(svgEl('line',{x1:-half,y1:p,x2:half,y2:p}));
    }
    var names=['Wind Rift','Storm Rift','Typhon','Sand Storm','Ice Storm','World End'];
    names.forEach(function(name,type){var segments=(worldMap.walls||[]).filter(function(w){return Number(w.type)===type;});if(!segments.length)return;var d=pathFromSegments(segments);walls.appendChild(svgEl('path',{d:d,'class':'map-wall-halo'}));walls.appendChild(svgEl('path',{d:d,'class':'map-wall type-'+type},name+' · '+segments.length+' authored segments'));});
    (worldMap.islands||[]).forEach(function(i,index){
      islands.appendChild(svgEl('use',{href:i.haven?'#havenIslandSymbol':'#releaseIslandSymbol',x:Number(i.x)-90,y:-Number(i.z)-90,width:180,height:180,'class':'map-island'+(i.haven?' haven':'')},
        (i.haven?'Haven starter island':'Release island')+' '+(index+1)+' · asset '+(i.asset||'unknown')+' · XYZ '+Number(i.x).toFixed(1)+', '+Number(i.y).toFixed(1)+', '+Number(i.z).toFixed(1)));
    });
    resetMapView();
  }
  function inspectMap(title,detail){$('mapInspector').innerHTML='';var strong=document.createElement('b');strong.textContent=title;var span=document.createElement('span');span.textContent=detail;$('mapInspector').appendChild(strong);$('mapInspector').appendChild(span);}
  function interactiveMarker(layer,shape,title,detail){
    var group=svgEl('g',{'class':'map-marker',tabindex:'0',role:'button','aria-label':title});group.appendChild(shape);group.addEventListener('click',function(e){e.stopPropagation();inspectMap(title,detail);});group.addEventListener('keydown',function(e){if(e.key==='Enter'||e.key===' '){e.preventDefault();inspectMap(title,detail);}});layer.appendChild(group);
  }
  function renderLiveWorldMap(reporting,ageSeconds){
    var runtimeLayer=$('mapRuntimeIslandLayer'),shipLayer=$('mapShipLayer'),playerLayer=$('mapPlayerLayer');
    clear(runtimeLayer);clear(shipLayer);clear(playerLayer);
    var runtimeIslands=latestRuntimeDomains.filter(function(d){return d.kind==='island';});
    runtimeIslands.forEach(function(i){
      runtimeLayer.appendChild(svgEl('circle',{cx:Number(i.x),cy:-Number(i.z),r:155,'class':'map-runtime-island'},
        (i.label||i.domainId)+' · currently simulated island domain, resident on this host · live position '+Number(i.x).toFixed(1)+', '+Number(i.z).toFixed(1)));
    });
    latestDomains.forEach(function(s){
      var x=Number(s.x),y=-Number(s.z),detail='XYZ '+x.toFixed(1)+', '+Number(s.y).toFixed(1)+', '+Number(s.z).toFixed(1)+(s.piloted?' · piloted':' · resting');
      interactiveMarker(shipLayer,svgEl('use',{href:'#shipSymbol',x:x-170,y:y-170,width:340,height:340,'class':'map-ship'+(s.active?'':' resting')}),'Ship '+s.hullEntityId,detail);
    });
    var positioned=latestPlayers.filter(function(p){return p.hasPosition;});
    positioned.forEach(function(p){
      var x=Number(p.x),y=-Number(p.z);
      interactiveMarker(playerLayer,svgEl('use',{href:'#playerSymbol',x:x-145,y:y-145,width:290,height:290,'class':'map-player'}),'Player entity '+p.entityId,'XYZ '+x.toFixed(1)+', '+Number(p.y).toFixed(1)+', '+Number(p.z).toFixed(1));
    });
    $('mapBiomeLayer').style.display=$('mapBiomes').checked?'':'none';
    $('mapIslandLayer').style.display=$('mapIslands').checked?'':'none';
    runtimeLayer.style.display=$('mapIslands').checked?'':'none';
    $('mapWallLayer').style.display=$('mapWalls').checked?'':'none';
    shipLayer.style.display=$('mapShips').checked?'':'none';
    playerLayer.style.display=$('mapPlayers').checked?'':'none';
    var unknown=latestPlayers.length-positioned.length,live=latestDomains.length+positioned.length;
    var namedCells=(worldMap.biomes||[]).filter(function(b){return typeof b.district==='string'&&b.district.trim().length>0;}).length;
    text('mapStatus','Static map evidence: release MapFile · '+(worldMap.islands||[]).length+' islands · '+(worldMap.biomes||[]).length+' tier cells ('+namedCells+' named, '+((worldMap.biomes||[]).length-namedCells)+' unassigned) · '+(worldMap.walls||[]).length+' wall segments. Live overlay: '
      +(reporting?(runtimeIslands.length+' simulated island domains · '+latestDomains.length+' ships · '+positioned.length+' positioned players · '+Math.round(ageSeconds||0)+'s snapshot age'):'game server not reporting'));
    var note=$('mapLiveNote');note.style.display=(live||!reporting)?'none':'block';
    if(unknown){note.style.display='block';note.textContent=unknown+' connected player'+(unknown===1?' has':'s have')+' no authoritative world position yet.';}
    else note.textContent='No live positions reported.';
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
  ['mapBiomes','mapIslands','mapWalls','mapShips','mapPlayers'].forEach(function(id){$(id).addEventListener('change',function(){renderLiveWorldMap(gameReporting,0);});});
  $('mapZoomIn').addEventListener('click',function(){zoomMap(.65);});
  $('mapZoomOut').addEventListener('click',function(){zoomMap(1.5);});
  $('mapReset').addEventListener('click',resetMapView);
  $('liveWorldMap').addEventListener('wheel',function(e){e.preventDefault();var p=mapClientPoint(e);zoomMap(e.deltaY<0 ? .78 : 1.28,p.x,p.y);},{passive:false});
  (function(){
    var svg=$('liveWorldMap'),drag=null;
    svg.addEventListener('pointerdown',function(e){drag={x:e.clientX,y:e.clientY,vx:mapView.x,vy:mapView.y};svg.setPointerCapture(e.pointerId);svg.classList.add('dragging');});
    svg.addEventListener('pointermove',function(e){if(!drag)return;var rect=svg.getBoundingClientRect();mapView.x=drag.vx-(e.clientX-drag.x)/rect.width*mapView.w;mapView.y=drag.vy-(e.clientY-drag.y)/rect.height*mapView.h;applyMapView();});
    function end(e){if(!drag)return;drag=null;svg.classList.remove('dragging');if(svg.hasPointerCapture(e.pointerId))svg.releasePointerCapture(e.pointerId);}
    svg.addEventListener('pointerup',end);svg.addEventListener('pointercancel',end);
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
