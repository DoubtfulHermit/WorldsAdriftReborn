  // ---- the interest & streaming view --------------------------------------
  // The simulation's real structure - which islands are terrain candidates
  // this boot, what each peer currently holds, and the interest radii that
  // decide both - presented as one coherent picture instead of boot-log lines.
  //
  // Radii and budgets are READ from the snapshot, never restated here: the
  // terrain radii ride the schema-v5+ terrain section, the fauna per-peer cap
  // rides the v7+ fauna section, and everything else (resource radii and
  // budget, connect-time steps, the load barrier, spawn pacing, per-peer
  // holdings) rides the v10+ interest section. A server that predates a
  // section gets "not reported", never a plausible number.
  var selectedPeerEntity='';
  var interestLayer=null,candidacyLayer=null;
  var islandPositionsById=null;
  function interestOf(g){
    var i=g&&g.interest;
    return (i&&i.present===true)?i:null;
  }
  function islandPositions(){
    if(islandPositionsById)return islandPositionsById;
    if(!mapLoaded)return {};
    islandPositionsById={};
    (worldMap.islands||[]).forEach(function(i){
      if(i.inventory&&i.inventory.islandId)
        islandPositionsById[i.inventory.islandId]={x:Number(i.x),y:-Number(i.z)};
    });
    return islandPositionsById;
  }
  function notReported(g){
    return 'not reported by this game server (stats schema '+((g&&g.schemaVersion)||'unknown')+')';
  }
  function metres(v){
    v=Number(v)||0;
    return v>=1000?((v/1000).toFixed(v%1000?1:0)+' km'):(Math.round(v)+' m');
  }
  // ---- the boot picture ---------------------------------------------------
  function renderInterestFacts(g,reporting){
    var interest=reporting?interestOf(g):null;
    var t=(g&&g.terrain)||null;
    var terrainLive=reporting&&t&&t.present===true;
    var presence=$('intPresence');
    if(!reporting){presence.className='pill warn';presence.textContent='not reporting';}
    else if(interest){presence.className='pill ok';presence.textContent='live';}
    else{presence.className='pill warn';presence.textContent='interest telemetry absent (schema '+((g&&g.schemaVersion)||'?')+')';}
    var gates=interest?(interest.gates||{}):null;
    var barrier=$('intLoadBarrier');
    if(gates){
      barrier.textContent=gates.loadBarrier?'on':'OFF';
      barrier.className='n '+(gates.loadBarrier?'':'warn-number');
    }else{barrier.textContent='—';barrier.className='n';}
    text('intSpawnPace',gates?((Number(gates.spawnPaceMs)||0)+' ms'):'—');
    text('intTerrainMode',terrainLive?t.mode:'—');
    text('intCandidates',terrainLive
      ?(String(t.candidateCount||0)+' / '+((t.islands||[]).length))
      :'—');
    text('intResourceBudget',interest&&interest.resources
      ?(String(interest.resources.perPeerBudget||0)+' / peer'):'—');
    var fauna=(g&&g.fauna)||null;
    text('intFaunaCap',(reporting&&fauna&&fauna.present!==false&&fauna.perPeerBudget)
      ?(String(fauna.perPeerBudget)+' / peer'):'—');
    text('intConnectSteps',interest
      ?(metres((interest.resources||{}).connectRadiusMetres)+' / '
        +metres((interest.ship||{}).connectRadiusMetres)+' / '
        +metres((interest.terrainConnectRadiusMetres)||0))
      :'—');
    text('intTrackedPeers',terrainLive?String(t.trackedPeerCount||0):'—');
  }
  function interestRow(body,name,keying,load,unload,connect,budget,status,statusCls){
    var tr=document.createElement('tr');
    cell(tr,name);
    cell(tr,keying,'muted');
    cell(tr,load,'num');cell(tr,unload,'num');cell(tr,connect,'num');cell(tr,budget,'num');
    var s=cell(tr,'');
    var pill=document.createElement('span');pill.className='pill '+(statusCls||'');
    pill.textContent=status;s.appendChild(pill);
    body.appendChild(tr);
  }
  function renderInterestSystems(g,reporting){
    var body=$('interestSystems');clear(body);
    var t=(g&&g.terrain)||null;
    var terrainLive=reporting&&t&&t.present===true;
    var interest=reporting?interestOf(g):null;
    var absent=reporting?notReported(g):'game server not reporting';
    if(terrainLive){
      interestRow(body,'Island terrain','island envelope distance',
        metres(t.loadRadiusMetres),metres(t.unloadRadiusMetres),
        interest?metres(interest.terrainConnectRadiusMetres):'—',
        'whole islands',
        t.mode,t.mode==='on'?'ok':'warn');
    }else{
      interestRow(body,'Island terrain','island envelope distance','—','—','—','—',absent,'warn');
    }
    if(interest&&interest.resources){
      interestRow(body,'Resources (trees, ore, databanks)','island envelope distance',
        metres(interest.resources.loadRadiusMetres),metres(interest.resources.unloadRadiusMetres),
        metres(interest.resources.connectRadiusMetres),
        String(interest.resources.perPeerBudget||0)+' nodes / peer',
        interest.resources.enabled?'on':'off',interest.resources.enabled?'ok':'warn');
    }else{
      interestRow(body,'Resources (trees, ore, databanks)','island envelope distance','—','—','—','—',absent,'warn');
    }
    var fauna=(g&&g.fauna)||null;
    var faunaCap=(reporting&&fauna&&fauna.present!==false)?(String(fauna.perPeerBudget||0)+' creatures / peer'):'—';
    if(interest&&interest.fauna){
      interestRow(body,'Wildlife','island envelope distance',
        metres(interest.fauna.loadRadiusMetres),metres(interest.fauna.unloadRadiusMetres),'—',
        faunaCap,interest.fauna.enabled?'on':'off',interest.fauna.enabled?'ok':'warn');
    }else{
      interestRow(body,'Wildlife','island envelope distance','—','—','—',faunaCap,absent,'warn');
    }
    if(interest&&interest.ship){
      interestRow(body,'Ship domains','spatial distance to hull',
        metres(interest.ship.loadRadiusMetres),metres(interest.ship.unloadRadiusMetres),
        metres(interest.ship.connectRadiusMetres),'whole domains','on','ok');
    }else{
      interestRow(body,'Ship domains','spatial distance to hull','—','—','—','—',absent,'warn');
    }
  }
  // ---- terrain candidacy: the load-barrier failure made VISIBLE -----------
  function renderCandidacy(g,reporting){
    var t=(g&&g.terrain)||null;
    var live=reporting&&t&&t.present===true;
    var banner=$('candidacyBanner');
    var chips=$('candidacyChips');clear(chips);
    var interest=reporting?interestOf(g):null;
    var gates=interest?(interest.gates||{}):null;
    if(!live){
      banner.classList.remove('show','spiral');
      text('candidacySummary',reporting
        ?('Terrain candidacy is unknown: '+notReported(g)+'.')
        :'Terrain candidacy is unknown: the game server is not reporting.');
      return;
    }
    var islands=t.islands||[];
    var managed=islands.filter(function(i){return i.managed;});
    var unconditional=islands.filter(function(i){return i.unconditional;});
    var dead=islands.filter(function(i){return !i.managed&&!i.unconditional;});
    text('candidacySummary',
      managed.length+' of '+islands.length+' rolled-out islands are stream-managed terrain candidates this boot'
      +(unconditional.length?(' ('+unconditional.length+' more are unconditional, always loaded)'):'')
      +'. An island that is rolled out but not a candidate has DEAD terrain for every player until a restart fixes its prerequisite.');
    // The failure mode this view exists for: a boot without the load barrier
    // leaves every conditional island's terrain dead, and it must be LOUD.
    var barrierKnownOff=gates?gates.loadBarrier===false:false;
    var allDead=t.mode==='on'&&islands.length>0&&managed.length===0;
    if(allDead||(t.mode==='on'&&barrierKnownOff)){
      banner.classList.add('show','spiral');
      text('candidacyBannerText',
        (allDead?'Not one rolled-out island is a terrain candidate this boot - every conditional island is a dead island. ':'')
        +(barrierKnownOff
          ?'The game server reports the load barrier is OFF (WAREBORN_LOAD_BARRIER). Without it no release island can become a terrain candidate; restart with WAREBORN_LOAD_BARRIER=1.'
          :'This is the exact shape of a boot without WAREBORN_LOAD_BARRIER=1; check the gate and restart.'));
    }else banner.classList.remove('show','spiral');
    islands.slice().sort(function(a,b){return String(a.islandId).localeCompare(String(b.islandId));})
      .forEach(function(i){
        var cls=i.managed?'ok':(i.unconditional?'warn':'dead');
        var chip=document.createElement('span');chip.className='cand-chip '+cls;
        chip.textContent=i.displayName;
        chip.title=i.islandId+' · '+(i.managed?'stream-managed candidate'
          :(i.unconditional?'unconditional (always loaded)'
            :(!i.registered?'not registered this boot'
              :(!i.locallyOwned?'not locally owned'
                :(!i.hasEnvelope?'no extracted envelope - cannot be a candidate':'not managed')))));
        chips.appendChild(chip);
      });
    renderCandidacyOverlay(islands);
  }
  function renderCandidacyOverlay(islands){
    if(!candidacyLayer)return;
    clear(candidacyLayer);
    if(!$('candidacyMapToggle').checked)return;
    var at=islandPositions();
    islands.forEach(function(i){
      if(i.unconditional)return;
      var p=at[i.islandId];if(!p)return;
      var mark=svgEl('g',{'class':'map-cand '+(i.managed?'ok':'dead')},
        i.displayName+' · '+(i.managed?'terrain candidate this boot':'terrain DEAD this boot'));
      mark.appendChild(svgEl('circle',{cx:p.x,cy:p.y,r:260}));
      candidacyLayer.appendChild(mark);
    });
  }
  // ---- the peer inspector and its true-scale rings -------------------------
  function inspectedPeer(){
    for(var i=0;i<latestPlayers.length;i++)
      if(String(latestPlayers[i].entityId)===selectedPeerEntity)return latestPlayers[i];
    return null;
  }
  // side: where this system's label anchors on its ring - 'top', 'bottom' or
  // 'right'. Distinct anchors per system, because two systems configured to
  // the SAME radius (resources and wildlife both ship 600/800) would
  // otherwise write their labels onto each other.
  function ring(x,y,r,cls,label,side){
    interestLayer.appendChild(svgEl('circle',
      {cx:x,cy:y,r:r,'class':'map-interest-ring '+cls}));
    if(label){
      var ax=x,ay=y,tx=0,ty=-6,anchor='middle';
      if(side==='bottom'){ay=y+r;ty=14;}
      else if(side==='right'){ax=x+r;ty=4;tx=8;anchor='start';}
      else{ay=y-r;}
      var t=svgEl('text',{x:tx,y:ty,'text-anchor':anchor,'class':'map-interest-label'});
      t.textContent=label;
      // Constant screen size: the label group carries scale(mapPx) like every
      // other piece of map furniture.
      var wrap=svgEl('g',{});wrap.appendChild(t);
      interestLayer.appendChild(wrap);
      mapMarkers.push({node:wrap,x:ax,y:ay});
      wrap.setAttribute('transform','translate('+ax+' '+ay+') scale('+mapPx+')');
    }
  }
  function renderInterestRings(g,reporting){
    if(!interestLayer)return;
    // Drop stale label registrations before re-drawing, exactly as the live
    // marker pass does for its own layers.
    mapMarkers=mapMarkers.filter(function(m){return m.node.parentNode!==interestLayer;});
    clear(interestLayer);
    if(!reporting||!$('peerRingsToggle').checked)return;
    var p=inspectedPeer();
    if(!p||!p.hasPosition)return;
    var x=Number(p.x),y=-Number(p.z);
    var t=(g&&g.terrain)||null;
    var interest=interestOf(g);
    if(t&&t.present===true&&t.mode==='on'){
      ring(x,y,Number(t.unloadRadiusMetres)||0,'terrain unload',null);
      ring(x,y,Number(t.loadRadiusMetres)||0,'terrain','terrain '+metres(t.loadRadiusMetres)+' / '+metres(t.unloadRadiusMetres),'top');
    }
    if(interest&&interest.resources&&interest.resources.enabled){
      ring(x,y,Number(interest.resources.unloadRadiusMetres)||0,'resource unload',null);
      ring(x,y,Number(interest.resources.loadRadiusMetres)||0,'resource','resources '+metres(interest.resources.loadRadiusMetres)+' / '+metres(interest.resources.unloadRadiusMetres),'bottom');
    }
    if(interest&&interest.fauna&&interest.fauna.enabled){
      ring(x,y,Number(interest.fauna.unloadRadiusMetres)||0,'fauna unload',null);
      ring(x,y,Number(interest.fauna.loadRadiusMetres)||0,'fauna','wildlife '+metres(interest.fauna.loadRadiusMetres)+' / '+metres(interest.fauna.unloadRadiusMetres),'right');
    }
  }
  function holdingsBlock(box,heading){
    var h=document.createElement('h4');h.textContent=heading;box.appendChild(h);
  }
  function holdingsLine(box,textValue,mutedFlag){
    var p=document.createElement('p');if(mutedFlag)p.className='muted';
    p.textContent=textValue;box.appendChild(p);return p;
  }
  function renderPeerHoldings(g,reporting){
    var box=$('peerHoldings');clear(box);
    var p=inspectedPeer();
    if(!reporting){holdingsLine(box,'The game server is not reporting, so nobody’s holdings are known.',true);return;}
    if(!p){holdingsLine(box,'Select a connected peer to see exactly what this boot has streamed to them.',true);return;}
    var t=(g&&g.terrain)||null;
    var interest=interestOf(g);
    holdingsBlock(box,'Terrain checked out');
    var tp=null;
    if(t&&t.present===true)(t.players||[]).forEach(function(row){
      if(row.playerEntityId===p.entityId)tp=row;});
    if(tp){
      var held=(tp.islands||[]).filter(function(c){return c.state!=='absent';});
      if(held.length){
        held.forEach(function(c){
          var line=document.createElement('p');
          line.appendChild(stateChip(c.state));
          line.appendChild(document.createTextNode(' '+c.islandId));
          box.appendChild(line);
        });
      }else holdingsLine(box,'No island terrain is checked out to this peer.',true);
      holdingsLine(box,'Confirmed ground: '+(tp.confirmedGroundIslandId||'not confirmed')
        +' · ready checkouts: '+(tp.readyCount||0));
    }else holdingsLine(box,(t&&t.present===true)
      ?'This peer is not tracked for terrain checkout.'
      :('Terrain holdings: '+notReported(g)+'.'),true);
    var ip=null;
    if(interest)(interest.peers||[]).forEach(function(row){
      if(row.playerEntityId===p.entityId)ip=row;});
    holdingsBlock(box,'Resources checked out');
    if(ip){
      var islands=ip.resourceIslands||[];
      if(islands.length){
        islands.forEach(function(r){
          holdingsLine(box,r.islandId+' · '+r.checkedOut+' nodes');
        });
        holdingsLine(box,'Total: '+(ip.resourceCheckedOut||0)+' of '
          +(interest.resources?interest.resources.perPeerBudget:'?')+' budget');
      }else holdingsLine(box,'No resource nodes are checked out to this peer.',true);
    }else holdingsLine(box,'Resource holdings: '+notReported(g)+'.',true);
    holdingsBlock(box,'Wildlife streamed');
    if(ip)holdingsLine(box,(ip.faunaCheckedOut||0)+' creatures currently streamed to this peer.');
    else holdingsLine(box,'Wildlife holdings: '+notReported(g)+'.',true);
    holdingsBlock(box,'Ship domains');
    var aboard=[];
    latestDomains.forEach(function(d){
      if((d.aboardPlayerEntityIds||[]).indexOf(p.entityId)>=0)aboard.push('aboard hull '+d.hullEntityId);
      if(d.pilotPlayerEntityId===p.entityId)aboard.push('piloting hull '+d.hullEntityId);
    });
    if(ip&&(ip.shipDomainIds||[]).length)
      holdingsLine(box,'Checked out: '+(ip.shipDomainIds||[]).join(', '));
    else if(ip)holdingsLine(box,'No ship domain is checked out to this peer.',true);
    else holdingsLine(box,'Ship-domain checkout: '+notReported(g)+'.',true);
    if(aboard.length)holdingsLine(box,aboard.join(' · '));
  }
  function renderPeerSelect(){
    var select=$('peerInspect');
    var kept=select.value;clear(select);
    var rows=latestPlayers||[];
    var none=document.createElement('option');none.value='';
    none.textContent=rows.length?'Select a peer':'No connected peer';
    select.appendChild(none);
    rows.forEach(function(p){
      var o=document.createElement('option');o.value=String(p.entityId);
      o.textContent='Entity '+p.entityId+' · peer '+p.peerId
        +(p.hasPosition?'':' · no position yet');
      select.appendChild(o);
    });
    if(rows.some(function(p){return String(p.entityId)===kept;}))select.value=kept;
    else if(rows.length===1)select.value=String(rows[0].entityId);
    selectedPeerEntity=select.value;
  }
  // Called from render() on every stats refresh, and from its own controls.
  function renderTopologyView(){
    var g=latestGame||{};
    var reporting=g.reporting===true&&!g.stale;
    renderPeerSelect();
    renderInterestFacts(g,reporting);
    renderInterestSystems(g,reporting);
    renderCandidacy(g,g.reporting===true);
    renderInterestRings(g,g.reporting===true);
    renderPeerHoldings(g,g.reporting===true);
  }
  function wireTopology(){
    // The rings and candidacy marks live in their own SVG layers, created here
    // rather than in the shared map markup: the public map composes the same
    // map-body.html and must not carry operator-only structure.
    candidacyLayer=svgEl('g',{id:'mapCandidacyLayer'});
    var faunaRef=$('mapFaunaLayer');
    faunaRef.parentNode.insertBefore(candidacyLayer,faunaRef);
    interestLayer=svgEl('g',{id:'mapInterestLayer'});
    var hullRef=$('mapShipHullLayer');
    hullRef.parentNode.insertBefore(interestLayer,hullRef);
    $('peerInspect').addEventListener('change',function(){
      selectedPeerEntity=$('peerInspect').value;renderTopologyView();});
    $('peerRingsToggle').addEventListener('change',renderTopologyView);
    $('candidacyMapToggle').addEventListener('change',renderTopologyView);
  }
