
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
      var svg=$('liveWorldMap'),drag=null,pinch=null,live={};
      // EVERY POINTER CURRENTLY DOWN IS TRACKED, not just the newest one. The
      // map used to keep a single `drag`, so a second finger simply overwrote
      // it: a two-finger pinch was read as a one-finger pan by the newer
      // finger, the viewBox span never changed, and pinch-to-zoom looked like
      // it did not exist on a phone. Both pointers do reach this handler - the
      // capture below was never what swallowed them - so the fix is to keep
      // them, not to change how they arrive.
      function midpointGesture(){
        var ids=Object.keys(live);
        if(ids.length<2)return;
        // Two fingers is a pinch, so whatever the first one had started is
        // handed over here rather than left running alongside it.
        if(drag){if(drag.captured&&svg.hasPointerCapture(drag.id))svg.releasePointerCapture(drag.id);drag=null;}
        // A pinch is a gesture, not a click. mapDragged is what stops the
        // trailing compatibility click being read as a tap on bare ocean and
        // silently resetting the panel to the world overview.
        mapDragged=true;hideHover();fadeMapHint();
        var a=live[ids[0]],b=live[ids[1]];
        pinch={a:ids[0],b:ids[1],dist:Math.max(1,Math.hypot(b.x-a.x,b.y-a.y)),
               mx:(a.x+b.x)/2,my:(a.y+b.y)/2};
      }
      // Pinching zooms about the MIDPOINT BETWEEN THE FINGERS, the same way the
      // wheel zooms about the cursor, so the map grows out of what you are
      // pinching rather than out of the middle of the stage. The clamps are not
      // reimplemented here: zoomMap owns them, so touch and wheel cannot
      // disagree about how far in or out you are allowed to go.
      function pinchMove(){
        var a=live[pinch.a],b=live[pinch.b];
        if(!a||!b)return;
        var dist=Math.max(1,Math.hypot(b.x-a.x,b.y-a.y)),mx=(a.x+b.x)/2,my=(a.y+b.y)/2;
        var point=mapClientPoint({clientX:mx,clientY:my});
        zoomMap(pinch.dist/dist,point.x,point.y);
        // Two-finger panning falls out of the same gesture: the midpoint's
        // travel moves the viewBox by exactly the maths the one-finger drag
        // uses, one screen pixel being mapPx world metres on both axes.
        if(mx!==pinch.mx||my!==pinch.my){
          mapView.x-=(mx-pinch.mx)*mapPx;mapView.y-=(my-pinch.my)*mapPx;applyMapView();
        }
        pinch.dist=dist;pinch.mx=mx;pinch.my=my;
      }
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
        live[e.pointerId]={x:e.clientX,y:e.clientY};
        if(Object.keys(live).length>1){midpointGesture();return;}
        drag={x:e.clientX,y:e.clientY,vx:mapView.x,vy:mapView.y,id:e.pointerId,captured:false};
        mapDragged=false;
      });
      // Panning moves the viewBox only; nothing needs rescaling, which is why the
      // 266 markers stay smooth under the cursor. One screen pixel is mapPx world
      // metres on BOTH axes, because the square viewBox is letterboxed to fit.
      svg.addEventListener('pointermove',function(e){
        if(live[e.pointerId]){live[e.pointerId].x=e.clientX;live[e.pointerId].y=e.clientY;}
        if(pinch){pinchMove();return;}
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
        if(e&&live[e.pointerId])delete live[e.pointerId];
        var ids=Object.keys(live);
        if(pinch){
          if(ids.length>1){midpointGesture();return;}
          pinch=null;
          // Lifting one finger of a pinch hands the map back to a one-finger
          // pan instead of freezing it until the other finger comes up too.
          // mapDragged is already true, so this pans immediately and no tap
          // can be manufactured out of the tail of a gesture.
          if(ids.length===1){
            var rest=live[ids[0]];
            drag={x:rest.x,y:rest.y,vx:mapView.x,vy:mapView.y,id:Number(ids[0]),captured:false};
            return;
          }
        }
        if(drag){
          if(drag.captured&&svg.hasPointerCapture(drag.id))svg.releasePointerCapture(drag.id);
          drag=null;
        }
        if(ids.length)return;
        svg.classList.remove('dragging');
        // The click event lands after pointerup, so mapDragged must survive until
        // then; clear it on the next tick instead of here.
        if(mapDragged)setTimeout(function(){mapDragged=false;},0);
      }
      svg.addEventListener('pointerup',end);svg.addEventListener('pointercancel',end);
      // A pointer released off the map still has to be forgotten, or the stale
      // entry makes the NEXT single touch look like a second finger and starts
      // a pinch nobody asked for. The map's own handler has already deleted it
      // by the time this one bubbles, so it fires at most once per pointer.
      window.addEventListener('pointerup',function(e){if(live[e.pointerId])end(e);});
      window.addEventListener('pointercancel',function(e){if(live[e.pointerId])end(e);});
      svg.addEventListener('pointerleave',hideHover);
    })();
  }
