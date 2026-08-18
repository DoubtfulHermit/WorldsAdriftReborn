  // HOW OFTEN THIS CONSOLE ASKS, AND WHY IT IS NOT FOUR SECONDS ANY MORE.
  // The game server rewrites its snapshot every three, so a reader that asks
  // every four is guaranteed to sometimes be holding a generation whose
  // replacement it has already missed - the live overlay was routinely five to
  // seven seconds behind the world for no reason but the interval. That matters
  // for ships in a way it never did for counters: a hull under way is drawn by
  // carrying its last measurement forward, and the further behind the
  // measurement is, the more of the mark is this browser's arithmetic rather
  // than the server's word.
  //
  // MEASURED BEFORE CHANGING, because a poll is a cost someone pays. One
  // /admin/api/stats read on the running login server: 1.5 ms and 5.9 kB, and
  // over twenty-two seconds of polling the browser logged NO long task at all -
  // a full render, 254 ledger rows and 6,500 nodes included, does not reach the
  // 50 ms threshold. At this interval that is about 4 kB/s and a millisecond of
  // server CPU per second, for one operator. The GAME SERVER's three-second
  // write is untouched; this is only how often the console reads the file that
  // is already being written.
  var REFRESH_MS = {{refreshMs}};
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
  // WHAT A LIVE MARKER IS CALLED is the page's business, not the renderer's.
  // The operator console names the entity behind a mark; the public map has
  // no entity to name and must not invent one. Keeping the words here - and
  // the geometry, the motion and the hit-testing shared - is what lets one
  // renderer serve both without a single `if (public)` in the drawing code.
  var MARKS={
    playerTitle:function(p){return 'Player entity '+p.entityId;},
    playerKicker:'Live player',
    playerPairs:function(p){return [['Entity',String(p.entityId)]];},
    shipTitle:function(d){return 'Ship - hull entity '+d.hullEntityId;}
  };
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
