  var CSRF = '{{csrfToken}}';
  // The operator console OPTS IN to naming the entity behind each mark. This
  // is the only place those words exist, so they never reach a public page.
  MARKS.playerTitle=function(p){return 'Player entity '+p.entityId;};
  MARKS.playerKicker='Live player';
  MARKS.playerPairs=function(p){return [['Entity',String(p.entityId)]];};
  MARKS.shipTitle=function(d){return 'Ship - hull entity '+d.hullEntityId;};
  MARKS.shipIdRow=function(d){return (d.domainId||'no domain id')+'  \u00b7  hull entity '+d.hullEntityId;};
  MARKS.worldKicker='Preserved release world';
  MARKS.islandKicker='Release island';
  MARKS.mapStatusText=function(s){
    return 'Static map evidence: release MapFile · '+s.islands+' islands · '+s.cells
      +' tier cells ('+s.namedCells+' named, '+(s.cells-s.namedCells)+' unassigned) · '
      +s.walls+' wall segments.'+s.seeded+' Live overlay: '
      +(s.reporting
        ? (s.domains+' simulated island domains · '+s.ships+' ships · '+s.players
           +' positioned players · '+Math.round(s.ageSeconds||0)+'s snapshot age')
        : 'game server not reporting');};
  MARKS.showsMethod=true;
  MARKS.shipCrewWords=function(d){return [d.piloted?'Piloted':'No pilot'];};
  MARKS.crewTile=function(stats,d,statTile){
    stats.appendChild(statTile((d.aboardPlayerEntityIds||[]).length,'Players aboard'));};
  // The authenticated per-hull geometry endpoint, keyed on the real hull entity
  // id this page already knows. Same drawing as the public map's, plus the
  // catalogue title of each mounted part, which the anonymous projection drops.
  MARKS.shipGeometryUrl=function(id,rev){
    return '/admin/api/ship-geometry?hull='+encodeURIComponent(id)
      +'&rev='+encodeURIComponent(rev);};
  MARKS.shipBuiltHeading='What it is and who owns it';
  MARKS.shipIdentityRows=function(d,h){return [
    ['Owner character uid',h.ownerCharacterUid?h.ownerCharacterUid:'unowned'],
    ['Pilot entity',d.pilotPlayerEntityId==null?'none':String(d.pilotPlayerEntityId)],
    ['Players aboard',(d.aboardPlayerEntityIds||[]).length
      ?(d.aboardPlayerEntityIds||[]).join(', '):'none']];};
  MARKS.shipBuiltNote='Ships carry no name anywhere in this game, so a hull is identified by '
    +'its entity id and its owner\u2019s character uid, both read from the server\u2019s own '
    +'build ledger. Hull materials are the dominant wood and metal the craft consumed; a ship '
    +'built before materials were recorded reads as birch and iron.';
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
    updateAdminShell(reporting,g);
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
    latestSimulation=g.simulation||null;
    renderSimulationShadow();
    renderTopology();
    renderLiveWorldMap(reporting,g.ageSeconds);
    if(selectedRuntimeDomainId&&latestRuntimeDomains.some(function(d){return d.domainId===selectedRuntimeDomainId;}))selectRuntimeDomain(selectedRuntimeDomainId,false);
    else{if(selectedRuntimeDomainId)selectedRuntimeDomainId='';updateSharedSelection(null);renderDomainInventory();}

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
    // The interest-and-streaming view renders off the same snapshot; declared
    // in admin-topology.js, which is later in the load order but hoists into
    // this one shared closure.
    renderTopologyView();
    renderInfrastructure(g,reporting);
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
