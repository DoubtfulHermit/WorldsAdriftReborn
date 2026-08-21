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
  wireMapInteraction();
  wireOperator();
  wireTopology();
  boot();
  // AFTER boot(): the wind layer inserts itself into the SVG the static render
  // builds and reads worldMap for the world edge and the wall segments, so it
  // cannot run before there is a map to attach to.
  wireWindLayer();
  refresh();
  setInterval(refresh,REFRESH_MS);
