  // ---- the public map ----------------------------------------------------
  // The renderer above this line is the same one the operator console runs.
  // This file is the public page's own part: what it says, what it shows in
  // the strip along the top, and the control for finding a traveller.
  //
  // It sets no MARKS overrides, and that is deliberate. The shared renderer's
  // defaults are already the anonymous ones - only the operator fragment opts
  // out of them - so a page that stays quiet stays private.
  //
  // Note what is NOT here: the long explanations of how any of this works.
  // They live in the About panel, behind a button, because a map should show
  // you the world rather than describe its own methods at you.

  var travIndex=0;       // which traveller the finder is on
  var travFocusId=null;  // and which one to keep highlighted between polls

  // ---- the strip along the top -------------------------------------------

  function chip(value,label){
    var box=el('div','pub-chip');
    box.appendChild(el('strong','',value));
    box.appendChild(el('span','',label));
    return box;
  }

  // Day or night, and how long until it turns. Uses the movement model's own
  // cycle maths rather than a second copy of it, so this readout can never
  // disagree with the creatures on the map.
  function dayNightChip(){
    var M=worldMap.faunaModel||{};
    if(!FAUNA||!faunaStatPresent()||!M.dayNightCycleSeconds)return null;
    var c=FAUNA.cycleFraction(faunaElapsed());
    var day=c>M.dayBeginsAtCycleFraction&&c<M.dayEndsAtCycleFraction;
    var target=day?M.dayEndsAtCycleFraction:M.dayBeginsAtCycleFraction;
    var until=((target-c)%1+1)%1*M.dayNightCycleSeconds;
    return chip(day?'Day':'Night',(day?'night':'day')+' in '+fmtShort(until));
  }

  function faunaStatPresent(){
    var f=latestGame&&latestGame.fauna;
    return !!(f&&f.present);
  }

  function renderStrip(g){
    var box=$('publicStrip');
    if(!box)return;
    clear(box);
    if(!gameReporting){
      box.appendChild(el('div','pub-chip quiet','The server is offline right now.'));
      return;
    }
    var f=(g&&g.fauna)||{};
    var wilds=(f.islands||[]).length;
    // The count of travellers you can SEE, which is what the finder steps
    // through. currentOnline also counts anyone the server has not placed
    // yet, and two different traveller numbers on one page is just confusing.
    var trav=travellers().length,ships=((g.ships)||[]).length,live=Number(f.liveCount)||0;
    box.appendChild(chip(fmt(trav),trav===1?'traveller':'travellers'));
    box.appendChild(chip(fmt(ships),ships===1?'ship':'ships'));
    box.appendChild(chip(fmt(live),live===1?'creature':'creatures'));
    if(wilds)box.appendChild(chip(fmt(wilds),wilds===1?'island with wildlife':'islands with wildlife'));
    // The map's own audience, after the world's own numbers because it is a
    // fact about the page rather than about the world. Before the day/night
    // chip, which tickStrip finds by being the LAST child.
    box.appendChild(viewerChip());
    var dn=dayNightChip();
    if(dn)box.appendChild(dn);
  }

  // The clock ticks between polls, so the countdown updates on its own.
  function tickStrip(){
    if(!gameReporting)return;
    var box=$('publicStrip');
    if(!box||!box.lastChild)return;
    var dn=dayNightChip();
    if(!dn)return;
    var last=box.lastChild;
    if(last.className==='pub-chip'&&last.childNodes.length===2
       &&(last.childNodes[0].textContent==='Day'||last.childNodes[0].textContent==='Night')){
      box.replaceChild(dn,last);
    }
  }

  // ---- finding a traveller ------------------------------------------------
  // Travellers are single dots on a 36 km map, so without this you would
  // never find one. Ships do not get the same control: a hull is drawn at its
  // real size and shape and you can see it, and the user asked for travellers.
  // If ships ever need it, it is this same code with a different list.

  function travellers(){
    return (latestPlayers||[]).filter(function(p){return p.hasPosition;});
  }

  function travLabel(){
    var list=travellers();
    if(!list.length)return 'Nobody is flying right now.';
    return 'Traveller '+(travIndex+1)+' of '+list.length;
  }

  function travSync(){
    var list=travellers(),has=list.length>0;
    // Someone may have logged off while we were pointed at them. Keep the
    // index in range and carry on rather than throwing.
    if(travIndex>=list.length)travIndex=0;
    if(travFocusId){
      var still=false;
      for(var i=0;i<list.length;i++)if(list[i].id===travFocusId){still=true;travIndex=i;break;}
      if(!still)travFocusId=null;
    }
    ['travFind','travPrev','travNext'].forEach(function(id){
      var b=$(id);
      if(b){b.disabled=!has;b.setAttribute('aria-disabled',has?'false':'true');}
    });
    text('travState',travLabel());
    paintTravHighlight();
  }

  // Live markers are rebuilt on every poll, so the highlight has to be put
  // back after each one. The marker order matches the positioned list, which
  // is how a dot is matched to the traveller it belongs to.
  function paintTravHighlight(){
    var layer=$('mapPlayerLayer');
    if(!layer)return;
    var list=travellers();
    Array.prototype.forEach.call(layer.childNodes,function(node,i){
      var on=!!travFocusId&&list[i]&&list[i].id===travFocusId;
      node.classList.toggle('found',on);
    });
  }

  function travGo(step){
    var list=travellers();
    if(!list.length)return;
    travIndex=((travIndex+step)%list.length+list.length)%list.length;
    var t=list[travIndex];
    travFocusId=t.id;
    flyTo(Number(t.x),-Number(t.z),900);
    text('travState',travLabel());
    paintTravHighlight();
  }

  // ---- about --------------------------------------------------------------

  function toggleAbout(){
    var panel=$('aboutPanel'),btn=$('aboutToggle');
    if(!panel||!btn)return;
    var open=panel.hasAttribute('hidden');
    if(open)panel.removeAttribute('hidden');else panel.setAttribute('hidden','');
    btn.setAttribute('aria-expanded',open?'true':'false');
    btn.textContent=open?'Hide':'About this map';
    if(open)panel.scrollIntoView({block:'nearest',behavior:prefersReducedMotion()?'auto':'smooth'});
  }

  // ---- the poll -----------------------------------------------------------

  function publicRender(data){
    var g=data||{};
    latestGame=g;
    gameReporting=!!g.reporting;
    // No runtime-domain section on the public feed: which host simulates what
    // is the operator's business, not part of the world.
    latestRuntimeDomains=[];
    latestPlayers=(g.players||[]);
    latestDomains=(g.ships||[]);
    renderLiveWorldMap(gameReporting,g.ageSeconds);
    renderFaunaFrame();
    renderShipFrame();
    renderStrip(g);
    travSync();

    var pill=$('livePill');
    if(pill){
      pill.textContent=gameReporting?(g.stale?'catching up':'live'):'offline';
      pill.className='pill'+(gameReporting&&!g.stale?' ok':'');
    }
    text('asof',gameReporting
      ? ('Updated '+Math.round(Number(g.ageSeconds)||0)+'s ago')
      : 'Waiting for the server');
  }

  function publicRefresh(){
    // The poll carries this tab's ephemeral token, which is how the server
    // counts open tabs without knowing anything about them. See
    // public-map-viewers.js.
    fetch('/map/data'+viewerQuery(),{headers:{'Accept':'application/json'}})
      .then(function(r){return r.ok?r.json():null;})
      .then(function(d){if(d)publicRender(d);})
      .catch(function(){});
  }

  function publicBoot(){
    try{
      worldMap=JSON.parse($('releaseWorldMap').textContent);
      mapLoaded=true;
      renderStaticWorldMap();
    }catch(e){text('mapStatus','The map could not be loaded.');}
    try{publicRender(JSON.parse($('bootstrap').textContent));}catch(e){}
    var about=$('aboutToggle');
    if(about)about.addEventListener('click',toggleAbout);
    var find=$('travFind'),prev=$('travPrev'),next=$('travNext');
    if(find)find.addEventListener('click',function(){travGo(travFocusId?1:0);});
    if(prev)prev.addEventListener('click',function(){travGo(-1);});
    if(next)next.addEventListener('click',function(){travGo(1);});
  }

  wireMapInteraction();
  viewerBoot();
  publicBoot();
  publicRefresh();
  setInterval(publicRefresh,REFRESH_MS);
  setInterval(tickStrip,1000);
