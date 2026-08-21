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
  // The interaction shadow model's latest section, or null when the game server
  // never reported one. Only the admin fragments read it: an internal observation
  // overlay has no business on the public map.
  var latestSimulation = null;
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
  //
  // THE DEFAULTS BELOW ARE THE ANONYMOUS ONES, deliberately. A page that
  // forgets to state its policy gets the safe answer, and the operator
  // fragment is what opts IN to naming people. That way the failure mode of
  // this seam is a console that says too little, never a public page that
  // says too much - and the operator's wording does not even ship to a
  // reader who was never meant to see it.
  var MARKS={
    playerTitle:function(){return 'A traveller';},
    playerKicker:'Someone, somewhere',
    playerPairs:function(){return [];},
    shipTitle:function(){return 'A ship';},
    shipIdRow:function(){return '';},
    // No crew words and no crew tile: whether a hull is crewed, and by whom,
    // is somebody's whereabouts. The public map says only what the SHIP is
    // doing.
    shipCrewWords:function(){return [];},
    // The console leads this line with where the geometry came from; the
    // public map just says what is on the map. Same numbers either way.
    // Whether panels explain HOW the map knows what it knows. Off by default:
    // the operator wants the caveats, a reader wants the world. The public
    // page keeps the same material in its About panel, in plainer words.
    showsMethod:false,
    wildlifeLine:'Manta rays circle each island. Jellyfish drift below by day and rise at night.',
    worldKicker:'The world',
    islandKicker:'Island',
    // Short by default. The live numbers already have a home in the page's
    // own strip, so the map itself does not need to repeat them in a sentence.
    mapStatusText:function(s){
      return s.islands+' islands · '+s.cells+' zones';},
    crewTile:null,
    // Where a page asks for one hull's STATIC geometry - the side elevation,
    // the decks and the mounted parts the ship card draws. Static per hull, so
    // it is fetched once when a card opens rather than pushed with every poll,
    // exactly as an island's coastline is served once rather than re-sent.
    //
    // THE REVISION IS IN THE URL, and that is not decoration. The response is
    // cacheable - it has to be, or the point of keeping it out of the poll is
    // lost - and the revision is a hash of the drawing itself, so a URL
    // carrying it addresses CONTENT rather than a ship. Without it, mounting a
    // lamp changed the drawing while the browser went on serving the previous
    // one out of its own HTTP cache: measured, in a headless run that moved a
    // helm on the server and watched the card not notice.
    //
    // The DEFAULT is the public map's endpoint, keyed on the same opaque marker
    // token the public feed labels a ship with; the operator fragment overrides
    // it with the authenticated one keyed on the real hull entity id. Same
    // failure mode as every other default here: a page that forgets to state
    // its policy asks the anonymous endpoint, which cannot answer with an
    // identity it was never given.
    shipGeometryUrl:function(id,rev){
      return '/map/ship?id='+encodeURIComponent(id)+'&rev='+encodeURIComponent(rev);},
    shipBuiltHeading:'What it is built from',
    shipIdentityRows:function(){return [];},
    shipBuiltNote:'Hull materials are the dominant wood and metal the craft consumed; '
      +'a ship built before materials were recorded reads as birch and iron.'
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
