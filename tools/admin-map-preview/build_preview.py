#!/usr/bin/env python3
"""Freeze the REAL rendered admin console into one self-contained, clickable file.

Why this exists: the admin map is behind auth and behind a running server, so
"does it look good" could only be answered by someone who could boot both. This
takes the page the server actually served - not a mock-up, not a re-render - and
makes it openable from disk, so the map can be reviewed, clicked and zoomed
offline before anything ships.

What it changes, and nothing else:
  * the CSRF token is blanked, so the frozen copy carries no live credential;
  * window.fetch is stubbed to answer the two /admin/api reads, and to refuse
    every write. The stub reports an EMPTY server - no players, no ships, no
    terrain - plus a wildlife roster, because the wildlife is the one live thing
    on this map that a file can honestly reproduce: the console does not receive
    creature positions, it evaluates the game server's own movement against the
    clock the server reports, so a stub that supplies a roster and a clock makes
    the map animate exactly as it does live. The roster is read out of the page's
    OWN embedded catalogue - the counts the seeding rule places on the tier-1
    world - so no number in it is invented here;
  * a banner says what this file is, and says the wildlife is driven by this
    file's own clock, so a screenshot of it is never mistaken for a live console.

Usage:
    ./build_preview.py <served-admin.html> <out.html>
"""
import re
import sys

STUB = """<script>
// ---- offline preview shim -------------------------------------------------
// Frozen copy: there is no server behind this file. The two authenticated GETs
// are answered here and every write is refused.
//
// THE WILDLIFE IS REAL ARITHMETIC, NOT A CANNED ANIMATION. The live console is
// never sent creature positions: it is sent a roster and the game server's own
// fauna clock, and it evaluates the server's movement itself. So a stub that
// supplies those two things makes this file draw the identical motion - the
// same closed form, the same constants, just anchored on this page's clock
// instead of a server's. The roster below is READ OUT OF THE PAGE, from the
// embedded release catalogue's own per-island fauna block, so nothing about it
// is invented in this shim.
(function(){
  var t0=Date.now(),roster=null;
  function readRoster(){
    if(roster)return roster;
    roster=[];
    try{
      var world=JSON.parse(
        document.getElementById('releaseWorldMap').textContent);
      for(var i=0;i<world.islands.length&&roster.length<46;i++){
        var island=world.islands[i];
        if(!island.inventory||!island.fauna)continue;
        // The tier-1 world, which is what the live server currently boots.
        if(Number(island.inventory.surveyTier)!==1)continue;
        roster.push({islandId:island.inventory.islandId,
                     mantaRays:island.fauna.manta,
                     jellyFish:island.fauna.jelly});
      }
    }catch(e){roster=[];}
    return roster;
  }
  function stats(){
    var live=readRoster(),total=0;
    for(var i=0;i<live.length;i++)total+=live[i].mantaRays+live[i].jellyFish;
    return {serverName:'Offline preview \\u2014 no game server behind this file',
      game:{reporting:true,state:'ok',ageSeconds:0,stale:false,
            uptimeSeconds:Math.round((Date.now()-t0)/1000),
            relayMode:'offline preview',build:'offline-preview',
            totalConnects:0,totalDisconnects:0,currentOnline:0,peakOnline:0,
            wireHealthWarning:false,secondIslandRegistered:false,
            firstRegionTerrainCount:0,schemaVersion:7,players:[],
            // Present but empty, so the island-count reconciliation reads
            // "0 currently simulated" instead of claiming this stub is an old
            // game server that predates terrain telemetry.
            terrain:{present:true,requested:false,enabled:false,mode:'off',
                     hostId:'offline-preview',authority:'offline preview',
                     loadRadiusMetres:0,unloadRadiusMetres:0,assetAckTimeoutMs:0,
                     settleDelayMs:0,candidateCount:0,trackedPeerCount:0,
                     readyCount:0,warningCount:0,errorCount:0,eventCapacity:0,
                     stateCounts:{},players:[],islands:[],events:[]},
            runtime:{hostMode:'offline preview',hostId:'offline-preview',
                     ownedEntityCount:0,globalEntityCount:0,unownedEntityCount:0,
                     ownershipIssueCount:0,domains:[],shipDomains:[]},
            fauna:{present:true,enabled:true,
                   clockSeconds:(Date.now()-t0)/1000,
                   liveCount:total,budget:4000,demand:total,perPeerBudget:24,
                   poseIntervalMs:250,islands:live}},
      accounts:{available:false,reason:'offline preview'}};
  }
  window.fetch=function(url,opts){
    var u=String(url);
    if(opts&&String(opts.method||'GET').toUpperCase()!=='GET')
      return Promise.resolve(new Response(JSON.stringify({error:'offline preview'}),
        {status:403,headers:{'Content-Type':'application/json'}}));
    var body=u.indexOf('/stats')>=0?stats():{};
    return Promise.resolve(new Response(JSON.stringify(body),
      {status:200,headers:{'Content-Type':'application/json'}}));
  };
})();
</script>
<div style="position:sticky;top:0;z-index:99;padding:.55rem .95rem;background:#1d2f3c;
 border-bottom:1px solid #74c9cf;color:#cfe2e8;font:500 .72rem/1.5 ui-sans-serif,system-ui,sans-serif">
<strong style="color:#74c9cf">Offline preview.</strong> This is the real operator console as the
server rendered it, frozen to a file. The map, the detail panel, hover, zoom, search and the island
ledger all work, and every control that would change something is refused.
<strong style="color:#74c9cf">Nothing here is a live server:</strong> a stub inside this file answers
the two authenticated reads with an EMPTY world &mdash; no players, no ships, no terrain &mdash; plus
the wildlife roster the preserved catalogue&rsquo;s own seeding rule places on the tier-1 world.
The creatures really are moving, on the game server&rsquo;s own movement maths, but anchored to
<em>this file&rsquo;s</em> clock rather than a running server&rsquo;s.
</div>
"""


def main() -> int:
    src, out = sys.argv[1], sys.argv[2]
    html = open(src, encoding="utf-8").read()

    # No live credential travels in a file meant to be passed around.
    html = re.sub(r'(name="csrf" value=")[0-9a-f]*(")', r"\1offline-preview\2", html)
    html = re.sub(r"(var CSRF=')[0-9a-f]*(')", r"\1offline-preview\2", html)

    marker = "<body>"
    if marker not in html:
        raise SystemExit("no <body> in the served page")
    html = html.replace(marker, marker + STUB, 1)

    open(out, "w", encoding="utf-8").write(html)

    external = len(re.findall(r'src="http|href="http|@import', html))
    print(f"wrote {out} ({len(html):,} bytes), external references: {external}")
    return 1 if external else 0


if __name__ == "__main__":
    raise SystemExit(main())
