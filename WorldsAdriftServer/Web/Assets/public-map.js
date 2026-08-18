  // ---- the public map's bootstrap ----------------------------------------
  // Everything above this line is the SAME renderer the operator console
  // runs. This file is only the composition: what the marks are called, where
  // the data comes from, and how often it is asked for. There is no operator
  // fragment loaded on this page, so there is no command path, no player
  // table and no terrain matrix to be reached even in principle - and the
  // payload it renders has already been through the server's anonymizing
  // whitelist, so there is no identity in it to show.

  // No MARKS overrides here, and that is the point: the shared renderer's
  // DEFAULT policy is the anonymous one, and only the operator fragment opts
  // out of it. A public page that stays silent stays private.

  function publicSummary(g){
    var box=$('publicSummary');
    if(!box)return;
    clear(box);
    var fauna=(g&&g.fauna)||{};
    // The same stat tiles the world overview uses, so the page reads as one
    // design rather than two.
    box.appendChild(statTile(fmt(Number(g&&g.currentOnline)||0),'Travellers aloft'));
    box.appendChild(statTile(fmt(((g&&g.ships)||[]).length),'Ships on the wind'));
    box.appendChild(statTile(fmt(Number(fauna.liveCount)||0),'Creatures in the sky'));
    box.appendChild(statTile(fmt(((worldMap&&worldMap.islands)||[]).length),'Islands charted'));
  }

  function publicRender(data){
    var g=data||{};
    latestGame=g;
    gameReporting=!!g.reporting;
    // The public payload has no runtime-domain section: that is operator
    // telemetry about which host is simulating what, not a thing in the
    // world. The renderer draws an empty layer for it, which is correct.
    latestRuntimeDomains=[];
    latestPlayers=(g.players||[]);
    latestDomains=(g.ships||[]);
    renderLiveWorldMap(gameReporting,g.ageSeconds);
    renderFaunaFrame();
    renderShipFrame();
    publicSummary(g);

    var pill=$('livePill');
    if(pill){
      pill.textContent=gameReporting?(g.stale?'catching up':'live'):'the world is quiet';
      pill.className='pill'+(gameReporting&&!g.stale?' ok':'');
    }
    text('asof',gameReporting
      ? ('Live, as of '+Math.round(Number(g.ageSeconds)||0)+'s ago.')
      : 'The game server is not reporting right now.');
  }

  function publicRefresh(){
    fetch('/map/data',{headers:{'Accept':'application/json'}})
      .then(function(r){return r.ok?r.json():null;})
      .then(function(d){if(d)publicRender(d);})
      .catch(function(){});
  }

  function publicBoot(){
    try{
      worldMap=JSON.parse($('releaseWorldMap').textContent);
      mapLoaded=true;
      renderStaticWorldMap();
    }catch(e){text('mapStatus','The world map could not be loaded.');}
    try{publicRender(JSON.parse($('bootstrap').textContent));}catch(e){}
  }

  wireMapInteraction();
  publicBoot();
  publicRefresh();
  setInterval(publicRefresh,REFRESH_MS);
