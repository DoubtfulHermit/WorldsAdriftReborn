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
    if(mapPx!==mapAppliedPx){mapAppliedPx=mapPx;rescaleMapFurniture();renderShipFrame();}
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
  // own record - never a scaled or rounded estimate.
  //
  // ON PROVENANCE, AND WHERE IT LIVES NOW. Which ore a deposit carries was
  // never surveyed on 193 of the 254 islands; those tables are composed from
  // the same-tier cohort by tools/world-import/metal_inference.py, and the
  // catalogue still records that per island (`oresInferred`, `oreSource`). The
  // PANEL no longer says so: this console presents the world's data as data,
  // and an operator reading an ore table is being told what is in the world,
  // not how the project came by it. The record stays in the source, in the
  // catalogue and in docs/research, because that is how WE avoid fooling
  // ourselves about which numbers came from Bossa - it is engineering hygiene,
  // not a caption.
  function oreName(metal){
    return String(metal||'').replace(/(^|[\s-])([a-z])/g,function(all,lead,ch){return lead+ch.toUpperCase();});
  }
  function oreSummary(inv){
    if(!inv.ores||!inv.ores.length)return 'no metal deposits';
    return inv.ores.map(function(o){return oreName(o.metal)+' quality '+o.quality+' x'+o.deposits;}).join(', ');
  }
  function oreTable(ores){
    var table=el('table','md-table');
    var head=el('tr');
    head.appendChild(el('th','','Ore'));
    head.appendChild(el('th','','Quality'));
    var th=el('th','n','Deposits');head.appendChild(th);
    var thead=el('thead');thead.appendChild(head);table.appendChild(thead);
    var body=el('tbody');
    ores.forEach(function(o){
      var row=el('tr');
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
    // A tile may legitimately have NO number: a hull whose shape is unavailable
    // has no keel to print, and its card passes an em dash. That has to reach
    // the page AS an em dash - running it through a number formatter turned it
    // into "NaN", which is the console confidently reporting a measurement it
    // does not have.
    var numeric=value!==''&&value!==null&&value!==undefined&&isFinite(Number(value));
    var b=el('b','',numeric?fmt(value):String(value));
    if(!numeric||!Number(value))b.className='zero';
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
  var NOT_PRESENT_FUEL='Fuel pods: 0 on every island. The only fuel pods in the world are the hand-placed ones on Haven.';
  var NOT_PRESENT_LOOT='Loot containers: 0. There are none anywhere in the world.';

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
    else if(mapSelection.kind==='ship')detailShip(panel,scroll,mapSelection.hullEntityId);
    else detailWorld(panel,scroll);
    panel.appendChild(scroll);
  }
  function detailWorld(panel,scroll){
    var rt=worldMap.resourceTotals||{};
    var head=el('div','md-head');
    head.appendChild(el('div','md-kicker',MARKS.worldKicker));
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
    stats.appendChild(statTile((worldMap.biomes||[]).length,'Zones'));
    scroll.appendChild(stats);

    var intro=mdBlock(null);
    var p=el('p','md-p');
    p.appendChild(document.createTextNode('Hover any island or zone to see what it is. '));
    p.appendChild(el('strong','','Click one to open it here.'));
    p.appendChild(document.createTextNode(' Zoom past a few kilometres and the islands take on their names and their real preserved coastlines.'));
    intro.appendChild(p);
    scroll.appendChild(intro);

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
    if(!MARKS.showsMethod)wild.appendChild(el('p','md-p',MARKS.wildlifeLine));
    if(MARKS.showsMethod)wild.appendChild(el('p','md-p',
      'Manta rays patrol each island’s perimeter and jellyfish drift under it by day and rise to '
      +'walking height at night. Across the whole catalogue the seeding rule would place '
      +fmt(planned)+' creatures on '+plural(plannedIslands,'island','islands')
      +'; how many actually exist depends on the world the game server booted and its creature '
      +'budget. Open any island for its own roster.'));
    if(MARKS.showsMethod)wild.appendChild(el('p','md-p',faunaNoteText()));
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
    head.appendChild(el('div','md-kicker',inv?MARKS.islandKicker:'Haven starter placement'));
    head.appendChild(el('h3','md-title',inv?inv.name:'Haven starter island'));
    if(inv){
      var info=biomeInfo(inv.cellTier);
      head.appendChild(subLine([tierChip(inv.cellTier),info.name,cultureName(inv),'Zone '+inv.cell]));
      if(MARKS.showsMethod)head.appendChild(el('div','md-id',inv.islandId+'  ·  asset '+(i.asset||'unknown')));
    }else{
      head.appendChild(subLine(['Haven reserve corridor','Hand-tuned, not surveyed']));
      if(MARKS.showsMethod)head.appendChild(el('div','md-id','asset '+(i.asset||'unknown')));
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
      ore.appendChild(oreTable(inv.ores));
    }else{
      ore.appendChild(el('p','md-p','No metal deposits are seeded on this island.'));
    }
    scroll.appendChild(ore);

    var trees=mdBlock('Trees');
    if(inv.trees){
      trees.appendChild(el('p','md-p',plural(inv.trees,'tree','trees')+' are seeded here.'));
      if(inv.woods&&inv.woods.length){
        trees.appendChild(chipRow(inv.woods.map(function(w){return w.charAt(0).toUpperCase()+w.slice(1);})));
        if(MARKS.showsMethod)trees.appendChild(el('p','md-p','The survey records WHICH woods grow on this island but not how many of each, and the seats cycle through the species above - so no per-species split is published here rather than a made-up one.'));
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
      if(!MARKS.showsMethod)conflict.style.display='none';
      conflict.appendChild(el('strong','','Two preserved tiers disagree. '));
      conflict.appendChild(document.createTextNode(
        'The MapFile puts this island in a Tier '+inv.cellTier+' zone, but the Cardinal Guild survey recorded it as Tier '
        +inv.surveyTier+'. Both are preserved facts and neither is dropped to make the other consistent. The map colours by the '
        +'MapFile cell tier, which is why the chip above reads Tier '+inv.cellTier+'.'));
      notes.appendChild(conflict);
    }
    if(!flags.length&&Number(inv.surveyTier)===Number(inv.cellTier))
      notes.appendChild(el('p','md-p',MARKS.showsMethod
        ? ('Nothing further was flagged: no revival chamber, no turrets, not marked dangerous, and the survey tier agrees with the MapFile cell tier.')
        : 'Nothing unusual here: no revival chamber, no turrets, nothing marked dangerous.'));
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
      ['Zone',inv.cell]].concat(MARKS.showsMethod
        ? [['Island id',inv.islandId],['Workshop asset',i.asset||'unknown']]
        : [])));
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
      +' - '+plural(manta+jelly,'creature','creatures')+' in all.'
      +(MARKS.showsMethod
        ? (' The seeding rule reads this island’s Cardinal Guild SURVEY tier, which is Tier '
           +inv.surveyTier
           +(Number(inv.surveyTier)===Number(inv.cellTier)
             ? '.' : ', not the Tier '+inv.cellTier+' its MapFile cell carries.'))
        : '')));
    block.appendChild(chipRow(['Manta ray','Jellyfish']));

    var live=faunaLiveOn(inv.islandId),state=el('p','md-p');
    if(!faunaStat){
      state.textContent=MARKS.showsMethod
        ? ('The game server is not reporting an island-fauna roster, so nothing is '
           +'claimed about what is alive here right now. The counts above are what the seeding '
           +'rule places when it runs.')
        : 'No live wildlife reported right now.';
    }else if(!live){
      state.textContent=MARKS.showsMethod
        ? ('The game server is reporting island fauna and this island is NOT in its '
           +'roster, so nothing is alive here this run - normally the world-wide creature budget '
           +'running out before this island was reached.')
        : 'Nothing living here at the moment.';
    }else{
      state.appendChild(el('strong','','Live now. '));
      state.appendChild(document.createTextNode(
        plural(Number(live.mantaRays)||0,'manta ray','manta rays')+' and '
        +plural(Number(live.jellyFish)||0,'jellyfish','jellyfish')
        +' on this island'
        +(MARKS.showsMethod
          ? ' on the running game server, and the map is drawing them where the '
            +'server has them: it evaluates the server’s own movement against the clock the '
            +'server reports, rather than sampling positions.'
          : ' right now.')));
    }
    block.appendChild(state);

    block.appendChild(kv([
      ['Manta orbit',fmt(Math.round(f.mantaOrbitRadius))+' m out, one lap in '+fmtShort(f.mantaLapSeconds)],
      ['Shoal drift',fmt(Math.round(f.jellyLateralRadius*(Number(model.jellyNightRadiusRatio)||0)))
        +' m at night to '+fmt(Math.round(f.jellyLateralRadius*(Number(model.jellyDayRadiusRatio)||0)))+' m by day'],
      ['Day/night cycle',fmtShort(Number(model.dayNightCycleSeconds)||0)],
      ['Manta speed',(Number(model.mantaMetresPerSecond)||0)+' m/s, constant']
    ]));

    scroll.appendChild(block);
  }
  function detailZone(panel,scroll,z){
    var b=z.biome,info=biomeInfo(b.type),roll=(worldMap.cells||{})[b.cellId];
    var head=el('div','md-head');
    head.appendChild(backButton('Whole world',function(){selectWorld();}));
    head.appendChild(el('div','md-kicker','Map zone'));
    head.appendChild(el('h3','md-title',zoneTitle(b)));
    head.appendChild(subLine([tierChip(b.type),info.name,info.terrain,Number(b.civilization)===1?'Kioki':'Saborian']));
    if(MARKS.showsMethod)head.appendChild(el('div','md-id','cell '+b.cellId+'  ·  source cell '+(z.index+1)+' of '+(worldMap.biomes||[]).length));
    panel.appendChild(head);

    if(!roll){
      var noneBlock=mdBlock('No catalogued islands');
      noneBlock.appendChild(el('p','md-p','No island in the release catalogue sits inside this zone, so there is nothing to roll up.'
        +(b.authoredDistrict?'':' This cell has no district name.')));
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
    scroll.appendChild(stats);

    var ore=mdBlock('Metal deposits by ore across this zone');
    if(roll.ores&&roll.ores.length)ore.appendChild(oreTable(roll.ores));
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
        var meta=plural(n.inv.deposits,'deposit','deposits')+' · '+plural(n.inv.databanks,'databank','databanks');
        list.appendChild(listRow(n.inv.name,meta,false,function(){focusIsland(n);}));
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
      ['District',b.authoredDistrict?b.district:'none'],
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
    block.appendChild(el('p','md-p','Read from the game server’s own stats snapshot, refreshed every 1.5 seconds. Unlike everything else on this map, this is live and will change.'));
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
    shipNodes.forEach(function(n){
      n.mark.classList.remove('selected');
      if(n.path)n.path.classList.remove('selected');
    });
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
            inv.revival?'revival':'',inv.turrets?'turrets':'',inv.dangerous?'dangerous':''
           ].join(' ').toLowerCase();
  }

  // ---- search: one filter for the map and the ledger ---------------------
  function mapSearchQuery(){return (($('ledgerFilter')||{}).value||'').trim().toLowerCase();}
  function islandMatches(node){
    var q=mapSearchQuery();
    return !q||node.hay.indexOf(q)>=0;
  }
  function applyMapFilter(){
    var q=mapSearchQuery(),active=!!q;
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
  // which no amount of clicking a map ever does. Every column is data about the
  // world: the catalogue still records which ore tables were composed rather
  // than surveyed, but that is a fact about this project's sources, not about
  // the world, and it does not belong in a row an operator is reading to find
  // out where the iron is.
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
    var shown=0,db=0,dep=0,tr=0;
    all.forEach(function(node){
      if(!islandMatches(node))return;
      var inv=node.inv;
      shown++;db+=Number(inv.databanks||0);dep+=Number(inv.deposits||0);tr+=Number(inv.trees||0);
      var row=document.createElement('tr');
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
      cell(row,oreSummary(inv),'wrap ore');
      cell(row,inv.fuelPods,'n zero');
      cell(row,inv.lootContainers,'n zero');
      cell(row,ledgerNotes(inv)||'—','wrap'+(ledgerNotes(inv)?'':' zero'));
      frag.appendChild(row);
    });
    body.appendChild(frag);
    var empty=$('ledgerEmpty');if(empty)empty.hidden=shown>0;
    text('ledgerStatus',shown===all.length
      ? (MARKS.showsMethod
          ? ('All '+all.length+' catalogued islands, sorted by zone then name. Haven’s 12 '
             +'hand-tuned placements are not in the release catalogue and are not listed here.')
          : ('All '+all.length+' islands, sorted by zone then name.'))
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
        '. Counts are lengths of lists in the catalogue the game server seeds from.'));
      foot.appendChild(totals);
      var none=document.createElement('div');
      none.textContent='Fuel pods and loot containers are 0 for every island. The only fuel pods in the world are the hand-placed ones on Haven.';
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
    buildShips(latestDomains,ageSeconds);
    var positioned=latestPlayers.filter(function(p){return p.hasPosition;});
    positioned.forEach(function(p){
      var x=Number(p.x),y=-Number(p.z);
      var pTitle=MARKS.playerTitle(p);
      liveMarker(playerLayer,'map-player','#playerSymbol',13,x,y,pTitle,{
        kicker:MARKS.playerKicker,title:pTitle,heading:'Authoritative player position',
        summary:'X '+x.toFixed(1)+' · Y '+Number(p.y).toFixed(1)+' · Z '+Number(p.z).toFixed(1),
        pairs:MARKS.playerPairs(p).concat([['World X',x.toFixed(1)],['World Y',Number(p.y).toFixed(1)],
               ['World Z',Number(p.z).toFixed(1)]])});
    });
    // The wildlife roster and its clock come from the same snapshot, and the
    // animation loop reads them; nothing is drawn on this pass.
    noteFauna(latestGame);
    text('mapFaunaNote',faunaNoteText());
    text('mapShipNote',shipNoteText());
    $('mapBiomeLayer').style.display=$('mapBiomes').checked?'':'none';
    $('mapIslandLayer').style.display=$('mapIslands').checked?'':'none';
    $('mapShellLayer').style.display=$('mapIslands').checked?'':'none';
    runtimeLayer.style.display=$('mapIslands').checked?'':'none';
    $('mapWallLayer').style.display=$('mapWalls').checked?'':'none';
    shipLayer.style.display=$('mapShips').checked?'':'none';
    $('mapShipHullLayer').style.display=$('mapShips').checked?'':'none';
    playerLayer.style.display=$('mapPlayers').checked?'':'none';
    var unknown=latestPlayers.length-positioned.length,live=latestDomains.length+positioned.length;
    var namedCells=(worldMap.biomes||[]).filter(function(b){return typeof b.district==='string'&&b.district.trim().length>0;}).length;
    var rt=worldMap.resourceTotals||{};
    var seeded=rt.islands?(' Seeded on them: '+rt.deposits+' metal deposits, '+rt.databanks+' databanks, '+rt.trees+' trees across '+rt.woodedIslands+' wooded islands.'):'';
    text('mapStatus',MARKS.mapStatusText({
      islands:(worldMap.islands||[]).length,
      cells:(worldMap.biomes||[]).length,
      namedCells:namedCells,
      walls:(worldMap.walls||[]).length,
      seeded:seeded,
      reporting:reporting,
      domains:runtimeIslands.length,
      ships:latestDomains.length,
      players:positioned.length,
      ageSeconds:ageSeconds}));
    var note=$('mapLiveNote');note.style.display=(live||!reporting)?'none':'block';
    if(unknown){note.style.display='block';note.textContent=unknown+' connected player'+(unknown===1?' has':'s have')+' no authoritative world position yet.';}
    else note.textContent='No live positions reported.';
  }

