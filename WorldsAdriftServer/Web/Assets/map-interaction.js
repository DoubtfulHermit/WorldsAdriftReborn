
  // ---- map interaction, wired by whichever page mounted the map -----------
  // SHARED, and called by both the operator console and the public map. The
  // pointer-capture rule below is the reason this is a function rather than a
  // block each page copies: a copy would have been fixed once and stayed
  // broken on the other page.
  function wireMapInteraction(){
    ['mapBiomes','mapIslands','mapWalls','mapShips','mapPlayers','mapFauna'].forEach(function(id){$(id).addEventListener('change',function(){renderLiveWorldMap(gameReporting,latestGame?latestGame.ageSeconds:0);renderFaunaFrame();renderShipFrame();});});
    // ONE search drives the map and the ledger. A second box under the table was
    // a second thing to notice and a second thing to keep in sync.
    ['ledgerFilter'].forEach(function(id){var e=$(id);if(e){e.addEventListener('input',applyMapFilter);e.addEventListener('change',applyMapFilter);}});
    $('ledgerFilter').addEventListener('focus',function(){if(mapSearchQuery())applyMapFilter();});
    document.addEventListener('click',function(e){
      if(!e.target.closest||!e.target.closest('.map-search'))closeSearchResults();});
    document.addEventListener('keydown',function(e){
      if(e.key!=='Escape')return;
      closeSearchResults();
      if(document.activeElement===$('ledgerFilter'))return;
      if(mapSelection.kind!=='world')selectWorld();
    });
    // Clicking bare ocean returns the panel to the world overview, so it is never
    // left holding one island after you have moved on.
    $('liveWorldMap').addEventListener('click',function(){if(!mapDragged)selectWorld();});
    $('mapZoomIn').addEventListener('click',function(){zoomMap(.62);});
    $('mapZoomOut').addEventListener('click',function(){zoomMap(1.6);});
    $('mapReset').addEventListener('click',function(){resetMapView();selectWorld();});
    $('liveWorldMap').addEventListener('wheel',function(e){e.preventDefault();var p=mapClientPoint(e);zoomMap(e.deltaY<0 ? .8 : 1.25,p.x,p.y);},{passive:false});
    window.addEventListener('resize',function(){mapAppliedPx=0;applyMapView();});
    (function(){
      var svg=$('liveWorldMap'),drag=null;
      // THE POINTER IS CAPTURED ONLY ONCE A DRAG ACTUALLY STARTS, and this is not
      // a detail. Capturing on pointerdown retargets the compatibility mouse
      // events too, so every `click` on an island marker was delivered to the SVG
      // instead of the marker - which meant clicking an island silently reset the
      // panel to the world overview and the per-island detail looked like it did
      // not exist. Deferring the capture past a 3 px threshold keeps a plain
      // click on its own target while still giving a real drag pointer events
      // that follow the cursor outside the element.
      svg.addEventListener('pointerdown',function(e){
        if(e.button!==0&&e.pointerType==='mouse')return;
        drag={x:e.clientX,y:e.clientY,vx:mapView.x,vy:mapView.y,id:e.pointerId,captured:false};
        mapDragged=false;
      });
      // Panning moves the viewBox only; nothing needs rescaling, which is why the
      // 266 markers stay smooth under the cursor. One screen pixel is mapPx world
      // metres on BOTH axes, because the square viewBox is letterboxed to fit.
      svg.addEventListener('pointermove',function(e){
        if(!drag||e.pointerId!==drag.id)return;
        if(!mapDragged){
          if(Math.abs(e.clientX-drag.x)<=3&&Math.abs(e.clientY-drag.y)<=3)return;
          mapDragged=true;hideHover();fadeMapHint();
          svg.classList.add('dragging');
          try{svg.setPointerCapture(e.pointerId);drag.captured=true;}catch(err){}
        }
        mapView.x=drag.vx-(e.clientX-drag.x)*mapPx;
        mapView.y=drag.vy-(e.clientY-drag.y)*mapPx;
        applyMapView();
      });
      function end(e){
        if(!drag)return;
        svg.classList.remove('dragging');
        if(drag.captured&&svg.hasPointerCapture(drag.id))svg.releasePointerCapture(drag.id);
        drag=null;
        // The click event lands after pointerup, so mapDragged must survive until
        // then; clear it on the next tick instead of here.
        if(mapDragged)setTimeout(function(){mapDragged=false;},0);
      }
      svg.addEventListener('pointerup',end);svg.addEventListener('pointercancel',end);
      svg.addEventListener('pointerleave',hideHover);
    })();
  }
