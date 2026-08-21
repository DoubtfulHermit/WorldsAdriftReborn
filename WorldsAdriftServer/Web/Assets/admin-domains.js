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
  function runtimeDomainById(domainId){
    for(var i=0;i<latestRuntimeDomains.length;i++)if(latestRuntimeDomains[i].domainId===domainId)return latestRuntimeDomains[i];
    return null;
  }
  function updateSharedSelection(d){
    text('observatorySelection',d?(d.label||d.domainId):'Nothing selected');
    text('observatorySelectionId',d?d.domainId:'Choose a live domain on the map or in Simulation.');
    var pill=$('observatorySelectionState');
    var state=d?domainState(d):'none';
    pill.className='pill '+(state==='warning'?'bad':(state==='active'||state==='resident'?'ok':'warn'));
    pill.textContent=state;
    renderInfrastructureSelection(d);
  }
  function setObservatoryMode(mode,focusTab){
    if(['world','simulation','infrastructure'].indexOf(mode)<0)mode='world';
    $('simulation').dataset.observatoryMode=mode;
    Array.prototype.forEach.call(document.querySelectorAll('[data-observatory-panel]'),function(panel){
      panel.hidden=panel.dataset.observatoryPanel!==mode;
    });
    Array.prototype.forEach.call(document.querySelectorAll('[data-observatory-mode-button]'),function(button){
      var active=button.dataset.observatoryModeButton===mode;
      button.setAttribute('aria-selected',active?'true':'false');
      button.tabIndex=active?0:-1;
      if(active&&focusTab===true)button.focus();
    });
    if(mode==='world')requestAnimationFrame(function(){updateMapFurniture();renderShipFrame();});
  }
  function renderInfrastructureSelection(d){
    var box=$('infraSelectionDetail');if(!box)return;clear(box);
    if(!d){box.className='detail-empty compact';box.textContent='Select a domain in World or Simulation. Infrastructure will keep the same selection.';return;}
    box.className='infra-selection-grid';
    [['Domain',d.domainId],['Kind',d.kind||'unknown'],['Host',d.hostId||'unknown'],
     ['Owned entities',String(d.entityCount||0)],['Affinity',d.affinityDomainId||'none'],
     ['Warnings',String(d.warningCount||0)]].forEach(function(pair){addDetailItem(box,pair[0],pair[1]);});
    var ship=shipTelemetryFor(d.domainId);
    if(ship){
      addDetailItem(box,'Authority generation',String(ship.authorityGeneration||0));
      addDetailItem(box,'Replication sequence',String(ship.replicationSequence||0));
      addDetailItem(box,'Delivery age',ship.deliveryAgeMs<0?'never':ship.deliveryAgeMs+'ms');
      addDetailItem(box,'Checkout subscribers',String(ship.subscriberCount||0));
    }
  }
  function selectRuntimeDomain(domainId,syncMap){
    selectedRuntimeDomainId=domainId||'';
    var d=runtimeDomainById(selectedRuntimeDomainId);
    $('domainDetailEmpty').style.display=d?'none':'grid';
    $('domainDetail').className='detail-content'+(d?' show':'');
    updateSharedSelection(d);
    if(!d){renderDomainInventory();return;}
    if(syncMap!==false){
      if(d.kind==='ship'||d.kind==='static-ship'){
        var shipForMap=shipTelemetryFor(d.domainId);if(shipForMap)selectShip(shipForMap.hullEntityId,false);
      }else if(d.kind==='island'&&typeof selectRuntimeIsland==='function')selectRuntimeIsland(d,false);
    }
    text('detailTitle',d.label||d.domainId);text('detailId',d.domainId);
    var status=$('detailStatus');var state=domainState(d);status.className='pill '+(state==='warning'?'bad':(state==='active'||state==='resident'?'ok':'warn'));status.textContent=state;
    var grid=$('detailGrid');clear(grid);
    addDetailItem(grid,'Host',d.hostId||'unknown');addDetailItem(grid,'Kind',d.kind||'unknown');
    addDetailItem(grid,'Owned entities',String(d.entityCount||0));addDetailItem(grid,'Island affinity',d.affinityDomainId||'none');
    addDetailItem(grid,'World position',Number(d.x).toFixed(1)+', '+Number(d.y).toFixed(1)+', '+Number(d.z).toFixed(1));
    addDetailItem(grid,'Warnings',String(d.warningCount||0));
    var shadow=shadowDomainFor(d.domainId);
    if(shadow){
      addDetailItem(grid,'Shadow members',String(shadow.memberCount||0));
      addDetailItem(grid,'Shadow interactions',String(shadow.activeInteractionCount||0)+' active');
      addDetailItem(grid,'Shadow pressure',shadowNumber(shadow.pressure)+' (uncalibrated)');
      addDetailItem(grid,'Shadow note',shadow.descriptor||'none');
      addDetailItem(grid,'Fidelity',shadow.fidelity||'not modelled');
      addDetailItem(grid,'Authority owner',shadow.authorityOwner||'not modelled');
      addDetailItem(grid,'Migration generation',shadow.migrationGeneration==null?'not modelled':String(shadow.migrationGeneration));
    }
    var ship=shipTelemetryFor(d.domainId);
    if(ship){
      addDetailItem(grid,'Authority generation',String(ship.authorityGeneration));
      addDetailItem(grid,'Replication','seq '+ship.replicationSequence+' · '+ship.cadenceMs+'ms');
      addDetailItem(grid,'Last delivery',ship.deliveryAgeMs<0?'never':ship.deliveryAgeMs+'ms ago');
      addDetailItem(grid,'Pilot',ship.pilotPlayerEntityId==null?'none':'entity '+ship.pilotPlayerEntityId);
      addDetailItem(grid,'Crew',(ship.aboardPlayerEntityIds||[]).length?(ship.aboardPlayerEntityIds||[]).join(', '):'none');
      addDetailItem(grid,'Checkout subscribers',String(ship.subscriberCount));
      addDetailItem(grid,'Structure',ship.deckCount+' decks · '+ship.mountedPartCount+' mounted');
      var flight=ship.flight;
      if(flight&&flight.present===true){
        addDetailItem(grid,'Flight mass',Number(flight.massKg).toFixed(0)+' kg');
        addDetailItem(grid,'Canvas',String(flight.unfurledSails)+' / '+String(flight.mountedSails)+' sails unfurled');
        addDetailItem(grid,'Wind sample',Number(flight.windSpeedMps).toFixed(2)+' m/s · '
          +Number(flight.windAngleDegrees).toFixed(1)+'° from bow · ['
          +Number(flight.windX).toFixed(2)+', '+Number(flight.windZ).toFixed(2)+'] · wall '
          +(Number(flight.wallIntensity)*100).toFixed(0)+'%');
        addDetailItem(grid,'Sail force',Number(flight.sailForceNewtons).toFixed(1)+' N');
        addDetailItem(grid,'Engine force',Number(flight.engineForceNewtons).toFixed(1)+' N');
        addDetailItem(grid,'Propulsion acceleration',Number(flight.propulsionAccelerationMps2).toFixed(3)+' m/s²');
        addDetailItem(grid,'Wind carry',Number(flight.windAlongHeadingMps).toFixed(2)+' m/s · sample t='
          +Number(flight.sampledAtSeconds).toFixed(3)+'s');
        addDetailItem(grid,'Predicted settled speed',Number(flight.predictedTerminalSpeedMps).toFixed(2)+' m/s · '
          +(Number(flight.predictedTerminalSpeedMps)*1.94384449).toFixed(1)+' kn');
      }else{
        addDetailItem(grid,'Flight forces','not reported (force model off or older server)');
      }
    }
    text('detailNote',ship
      ? 'Ship motion is emitted hull-first under one authority generation and replication sequence. Affinity is spatial context, not authority ownership.'
      : (d.kind==='island'?'Island ownership is resident on this host. Scheduling and remote migration are not enabled yet.':'Ownership-only static structure; excluded from live ship flight and checkout scheduling.'));
    renderDomainInventory();
  }
  function shadowDomainFor(domainId){
    var d=latestSimulation&&latestSimulation.domains;if(!d)return null;
    for(var i=0;i<d.length;i++)if(d[i].domainId===domainId)return d[i];
    return null;
  }
  function shadowState(){
    if(!latestSimulation||latestSimulation.present!==true)return {key:'absent',label:'not reported',cls:'warn'};
    if(latestSimulation.enabled!==true)return {key:'off',label:'observer off',cls:'warn'};
    if(latestSimulation.hasSnapshot!==true)return {key:'warming',label:'warming up',cls:'warn'};
    return {key:'observing',label:'observing',cls:'ok'};
  }
  function shadowNumber(v){return (Math.round((Number(v)||0)*100)/100).toFixed(2);}
  function renderSimulationShadow(){
    var state=shadowState();
    var pill=$('simulationState');pill.className='pill '+state.cls;pill.textContent=state.label;
    var live=state.key==='observing';
    text('simDomainTotal',live?String(latestSimulation.domainCount||0):'—');
    text('simEntityTotal',live?String(latestSimulation.entityCount||0):'—');
    text('simInteractionTotal',live?String(latestSimulation.interactionCount||0):'—');
    text('simActiveTotal',live?String(latestSimulation.activeInteractionCount||0):'—');
    text('simPressureTotal',live?shadowNumber(latestSimulation.totalCrossDomainPressure):'—');
    text('simulationIdentity',state.key==='absent'?'Not reported':state.key==='off'?'WAREBORN_SIMULATION_MODEL off':state.key==='warming'?'Enabled, no snapshot yet':'Enabled, observing');
    text('simulationSummary',state.key==='absent'?'This game server predates the shadow model, so nothing is claimed about coupling.':state.key==='off'?'The shadow model is compiled in but switched off. Gameplay and network behaviour are identical either way.':state.key==='warming'?'The observer is armed and has not completed its first pass.':(latestSimulation.error?('Observer parked after a fault: '+latestSimulation.error):('Rebuilt '+(latestSimulation.refreshCount||0)+' times. Observation only — no authority changes.')));
    text('simulationCadence',live?('refresh every '+shadowNumber(latestSimulation.refreshIntervalSeconds)+'s · uncalibrated pressure'):'observation only');
    var body=$('simulationInteractions');clear(body);
    var observations=(live&&latestSimulation.interactions)||[];
    observations.forEach(function(e){
      var tr=document.createElement('tr');
      cell(tr,e.a+' ↔ '+e.b);cell(tr,e.kind+' · '+e.strength);cell(tr,e.activity,'muted');
      cell(tr,shadowNumber(e.pressure),'num');
      cell(tr,e.crossDomain?((e.domainA||'unassigned')+' → '+(e.domainB||'unassigned')):'same domain','muted');body.appendChild(tr);
    });
    text('simulationResultCount',observations.length+' interaction'+(observations.length===1?'':'s')+(live?'':' · not reported'));
  }
  function renderWorldInspectorTimeline(g){
    var list=$('worldInspectorTimeline');if(!list)return;clear(list);
    var inspector=g&&g.worldInspector;
    var events=inspector&&Array.isArray(inspector.events)?inspector.events:[];
    if(!events.length){var empty=document.createElement('li');empty.className='muted';empty.textContent=inspector&&inspector.present===true?'No runtime event has been reported.':'Not reported by this game server schema.';list.appendChild(empty);return;}
    events.slice(0,40).forEach(function(event){
      var li=document.createElement('li');
      var age=document.createElement('time');age.textContent=event.ageMs==null?'time not reported':fmtMs(event.ageMs)+' ago';li.appendChild(age);
      var body=document.createElement('span');body.textContent=(event.kind||'runtime event')+(event.domainId?' · '+event.domainId:'')+(event.message?' · '+event.message:'');li.appendChild(body);list.appendChild(li);
    });
  }
  function renderInfrastructure(g,reporting){
    var runtime=(g&&g.runtime)||{},terrain=(g&&g.terrain)||{},interest=(g&&g.interest)||{};
    var domains=runtime.shipDomains||[];
    text('infraHostId',runtime.hostId&&runtime.hostId!=='unknown'?runtime.hostId:'local:primary');
    text('infraHostMode',runtime.hostMode==='local-single-process'?'local single-process':(runtime.hostMode||'not reported'));
    text('infraProcess',runtime.hostMode==='local-single-process'?'one authoritative poll loop':'process topology not reported');
    text('infraUptime',reporting?fmtDur(g.uptimeSeconds):'not reported');
    text('infraSnapshotAge',reporting?(Math.round(Number(g.ageSeconds)||0)+'s'):'not reported');
    text('infraCpu','not reported');text('infraMemory','not reported');text('infraThreads','not reported');
    text('infraRemoteWorkers',runtime.hostMode==='local-single-process'?'none configured or reported':'not reported');
    text('infraOwned',reporting?String(runtime.ownedEntityCount||0):'—');
    text('infraGlobal',reporting?String(runtime.globalEntityCount||0):'—');
    text('infraUnowned',reporting?String(runtime.unownedEntityCount||0):'—');
    text('infraOwnershipIssues',reporting?String(runtime.ownershipIssueCount||0):'—');
    text('infraInterest',reporting?(interest.present===true?'reported':'not reported by schema '+(g.schemaVersion||'unknown')):'not reported');
    text('infraTerrain',reporting?(terrain.present===true?(terrain.mode||'reported'):'not reported by schema '+(g.schemaVersion||'unknown')):'not reported');
    text('infraTerrainPeers',reporting&&terrain.present===true?String(terrain.trackedPeerCount||0):'—');
    text('infraTerrainWarnings',reporting&&terrain.present===true?String(terrain.warningCount||0):'—');
    text('infraShipDomains',reporting?String(domains.length):'—');
    text('infraLiveCadence',reporting?String(domains.filter(function(d){return d.liveCadenceExpected===true;}).length):'—');
    text('infraStaleDeliveries',reporting?String(domains.filter(function(d){return d.staleDelivery===true;}).length):'—');
    text('infraCheckoutGaps',reporting?String(domains.filter(function(d){return d.aboardCheckoutWarning===true;}).length):'—');
    renderInfrastructureSelection(runtimeDomainById(selectedRuntimeDomainId));
    renderWorldInspectorTimeline(g);
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
