  // ---- live ships, drawn as the hulls their owners built ------------------
  // WHY THIS ONE IS A POSITION FEED WHEN THE WILDLIFE IS NOT. Every creature on
  // this map moves on a CLOSED FORM of the clock, so the browser evaluates the
  // server's own function and draws the pose the server actually holds. A SHIP
  // has no such function: it moves under a player's hands and under the flight
  // integrator, and no formula outside that loop knows what the stick did. So a
  // ship's position is a MEASUREMENT, and it arrives with the three-second stats
  // snapshot like everything else here.
  //
  // WHAT IS MEASURED AND WHAT IS SMOOTHED, kept separate on purpose. Between
  // snapshots the hull is carried along the VELOCITY THE SERVER ITSELF REPORTED -
  // dead reckoning, which is what the game client does between two control
  // points, not an interpolation between two stale samples. It is allowed to run
  // only as long as the server's own acceleration limit keeps the guess under
  // twenty metres, and then the mark STOPS. A hull being reckoned is drawn
  // DASHED and its panel prints how long ago it was measured and how far the
  // reckoning could be out; a hull at rest reports exactly zero on every axis and
  // is drawn solid, because that mark is the measurement.
  //
  // THE SHAPE IS THE PLAYER'S OWN HULL. Not a boat icon: the game server decodes
  // the ShipPlan bytes the player built and publishes the plan-view ring off its
  // section geometry, so the taper, the curve bulge and the raked prow are all
  // the ones in the game, at the client's own hull scale, in metres. A ship
  // whose bytes will not decode is drawn as a plain mark and says so - never as a
  // substitute shape.
  //
  // NONE OF THE MOTION NUMBERS ARE RESTATED HERE. The window, the acceleration
  // and the error budget all come off the RUNNING server's flight tuning, and
  // AdminShipParityTests cuts the marked mirror below out of the real served page
  // and asserts it returns what ShipMapMotion.PoseAt returns.

  // ==== SHIP MOTION MIRROR BEGIN ====
  function shipMotion(M){
    var cap=Math.max(0,Number(M.maxWindowSeconds)||0);
    var win=Math.max(0,Number(M.windowSeconds)||0);
    if(win>cap)win=cap;
    var accel=Math.max(0,Number(M.accelMps2)||0);
    // The seconds actually applied: the age, floored at zero and capped at the
    // window. A negative age - two hosts, one of them under Wine, and one clock
    // that ran backwards - draws the measurement rather than reckoning into the
    // past.
    function reckoned(age){
      if(!(age>0))return 0;
      return age>win?win:age;
    }
    function poseAt(s,age){
      var t=reckoned(age);
      return {x:s.x+s.vx*t,z:s.z+s.vz*t,yaw:s.yaw+s.yawRate*t};
    }
    // How far the reckoning could be out after `seconds`, at the integrator's
    // acceleration limit. Quadratic, because that is what a bounded acceleration
    // gives; a linear figure would be a much weaker claim than the server
    // actually supports.
    function errorBound(seconds){
      if(!(accel>0)||!(seconds>0))return 0;
      return 0.5*accel*seconds*seconds;
    }
    return {reckoned:reckoned,poseAt:poseAt,errorBound:errorBound,windowSeconds:win};
  }
  // ==== SHIP MOTION MIRROR END ====

  var SHIPS=null;          // the evaluator, or null when no model was published
  var shipNodes=[];        // [{d,state,mark,markUse,hull,path,keel,keelMetres}]
  var shipAnchor=null;     // {age,perf} - the snapshot's age carried on our clock
  var shipFrame=null,shipLastDrawMs=-1e9;

  // How many screen pixels of keel a hull needs before its real outline is worth
  // more than the chevron. Below this a 20 m ship at whole-world zoom is a smear
  // two pixels long, and drawing it there would be a mark that says nothing while
  // pretending to say everything.
  var SHIP_HULL_MIN_PX=14;
  var SHIP_REDUCED_MOTION_MS=1000, SHIP_IDLE_MS=400;

  function shipNow(){
    return (window.performance&&performance.now)?performance.now():Date.now();
  }
  function shipModel(){
    var g=latestGame,m=g&&g.shipModel;
    return (m&&m.present===true)?m:null;
  }
  function shipAge(){
    if(!shipAnchor)return 0;
    return shipAnchor.age+(shipNow()-shipAnchor.perf)/1000;
  }
  function shipState(d){
    return {x:Number(d.x)||0,z:Number(d.z)||0,yaw:Number(d.yawRadians)||0,
            vx:Number(d.vxMps)||0,vz:Number(d.vzMps)||0,
            yawRate:Number(d.yawRateRadPerSec)||0};
  }
  function shipIsMeasuredExactly(s){return s.vx===0&&s.vz===0&&s.yawRate===0;}
  function shipMoving(){
    for(var i=0;i<shipNodes.length;i++)if(!shipIsMeasuredExactly(shipNodes[i].state))return true;
    return false;
  }
  function shipHullPath(outline){
    if(!outline||outline.length<6)return null;
    // Hull-local metres, flat x,z. The map's own frame is X east and Z north
    // drawn upward, so a hull-local z becomes a negative y exactly as an island
    // shell's does.
    var d='M';
    for(var k=0;k+1<outline.length;k+=2)
      d+=' '+Number(outline[k])+' '+(-Number(outline[k+1]))+(k+3<outline.length?' L':'');
    return d+' Z';
  }
  function shipTitle(d){return MARKS.shipTitle(d);}
  function shipSummary(d,s){
    var speed=Math.sqrt(s.vx*s.vx+s.vz*s.vz);
    return (MARKS.shipCrewWords(d)[0]||(d.hull&&d.hull.docked?'Docked in a shipyard':'Resting'))
      +(speed>0.05?(' - making '+speed.toFixed(1)+' m/s'):'')
      +' - X '+s.x.toFixed(1)+' Z '+s.z.toFixed(1);
  }
  function shipHullSize(d){
    var h=d.hull;
    if(!h||!h.present)return 0;
    return Math.max(Number(h.keelMetres)||0,Number(h.beamMetres)||0);
  }

  function buildShips(domains,ageSeconds){
    var hullLayer=$('mapShipHullLayer'),markLayer=$('mapShipLayer');
    if(!hullLayer||!markLayer)return;
    clear(hullLayer);
    shipNodes=[];
    // The snapshot's age at the moment it was READ, carried forward on this
    // browser's monotonic clock. Not the wall clock: a machine that resyncs
    // mid-session would otherwise teleport every hull.
    shipAnchor={age:Math.max(0,Number(ageSeconds)||0),perf:shipNow()};
    var model=shipModel();
    SHIPS=model?shipMotion(model):null;

    (domains||[]).forEach(function(d){
      var s=shipState(d),h=d.hull||{},size=shipHullSize(d);
      var node={d:d,state:s,hull:null,path:null,keel:null,keelMetres:size,
                markT:'',useT:'',hullT:''};

      var path=shipHullPath(h.outline);
      if(h.present===true&&path){
        var g=svgEl('g',{});
        var body=svgEl('path',{d:path,'class':'map-ship-hull'});
        g.appendChild(body);
        // A keel line from stern to bow. A short hull is wider than it is long -
        // a stock cell is twelve metres of beam to four of keel - so without it
        // a stubby ship's outline gives no clue which way it is pointing, and
        // which way it is pointing is real data on this map.
        var bow=Number(h.bowLocalZMetres)||0,stern=Number(h.sternLocalZMetres)||0;
        if(bow>stern){
          g.appendChild(svgEl('path',{d:'M 0 '+(-stern)+' L 0 '+(-bow),'class':'map-ship-keel'}));
        }
        hullLayer.appendChild(g);
        node.hull=g;node.path=body;
        body.addEventListener('click',function(e){e.stopPropagation();if(!mapDragged)selectShip(d.hullEntityId);});
        body.addEventListener('pointerenter',function(){body.classList.add('hot');});
        body.addEventListener('pointerleave',function(){body.classList.remove('hot');});
        attachHover(body,function(){return shipHoverCard(node);});
      }

      var mark=svgEl('g',{'class':'map-marker ship-mark',tabindex:'0',role:'button','aria-label':shipTitle(d)});
      var inner=svgEl('g',{'class':'mk'});
      inner.appendChild(svgEl('circle',{r:12,'class':'mk-hit'}));
      inner.appendChild(svgEl('circle',{r:9,'class':'mk-ring'}));
      var use=svgEl('use',{href:'#shipSymbol',x:-7.5,y:-7.5,width:15,height:15,
                           'class':'map-ship mk-ship'+(d.active?'':' resting')});
      inner.appendChild(use);
      var t=document.createElementNS(SVG_NS,'title');t.textContent=shipTitle(d);mark.appendChild(t);
      mark.appendChild(inner);
      markLayer.appendChild(mark);
      mark.addEventListener('click',function(e){e.stopPropagation();if(!mapDragged)selectShip(d.hullEntityId);});
      mark.addEventListener('pointerenter',function(){mark.classList.add('hot');if(node.path)node.path.classList.add('hot');});
      mark.addEventListener('pointerleave',function(){mark.classList.remove('hot');if(node.path)node.path.classList.remove('hot');});
      attachHover(mark,function(){return shipHoverCard(node);});
      node.mark=mark;node.markUse=use;
      shipNodes.push(node);
    });

    renderShipFrame();
    if(shipFrame===null)shipFrame=requestAnimationFrame(shipTick);
    // A part bolted on while a card is OPEN changes the drawing. Noticing that
    // is the whole job of the one integer the live feed carries: if the ship on
    // screen is now on a different geometry revision than the one drawn, redraw
    // the card, which re-fetches. Nothing happens on the overwhelmingly common
    // path where the revision is unchanged.
    if(mapSelection.kind==='ship'){
      var open=String(mapSelection.hullEntityId),held=SHIP_GEOM[open];
      (domains||[]).forEach(function(d){
        if(String(d.hullEntityId)!==open||!held)return;
        if(held.want!==(Number((d.hull||{}).geometryRevision)||0))renderMapDetail();
      });
    }
  }

  function shipHoverCard(node){
    var d=node.d,s=node.state;
    return [shipTitle(d),
            (d.hull&&d.hull.present)?'Live ship - real hull outline':'Live ship - hull shape unavailable',
            shipSummary(d,s),
            'Click for live detail'];
  }

  // Paint every hull at the pose the model says it holds RIGHT NOW. Cheap by
  // construction: five ships is five transform writes, and the outline path is
  // built once per poll rather than per frame.
  function renderShipFrame(){
    if(!shipNodes.length)return;
    var age=shipAge(),selected=mapSelection.kind==='ship'?String(mapSelection.hullEntityId):null;
    for(var i=0;i<shipNodes.length;i++){
      var n=shipNodes[i],s=n.state;
      var p=SHIPS?SHIPS.poseAt(s,age):{x:s.x,z:s.z,yaw:s.yaw};
      var sx=p.x,sy=-p.z,deg=p.yaw*180/Math.PI;
      var here='translate('+sx.toFixed(2)+' '+sy.toFixed(2)+')';
      // Only write what changed. A resting hull holds one pose for its whole
      // life, and rewriting the same attribute sixty times a second is both
      // wasted work and a standing invitation to the compositor.
      var markT=here+' scale('+mapPx+')';
      if(n.markT!==markT){n.mark.setAttribute('transform',markT);n.markT=markT;}
      var useT='rotate('+deg.toFixed(1)+')';
      if(n.useT!==useT){n.markUse.setAttribute('transform',useT);n.useT=useT;}
      var isSelected=selected!==null&&String(n.d.hullEntityId)===selected;
      n.mark.classList.toggle('selected',isSelected);
      if(!n.hull)continue;
      var hullT=here+' rotate('+deg.toFixed(1)+')';
      if(n.hullT!==hullT){n.hull.setAttribute('transform',hullT);n.hullT=hullT;}
      // PROGRESSIVE DISCLOSURE, per ship rather than per zoom level, because
      // ships are not all the same size: a big hull earns its outline sooner
      // than a small one, at the same zoom.
      var shown=(n.keelMetres/mapPx)>=SHIP_HULL_MIN_PX;
      n.hull.style.display=shown?'':'none';
      n.mark.classList.toggle('hull-shown',shown);
      var carrying=!!SHIPS&&!shipIsMeasuredExactly(s);
      var held=carrying&&age>SHIPS.reckoned(age)+0.05;
      n.path.classList.toggle('reckoned',carrying&&SHIPS.reckoned(age)>0);
      n.path.classList.toggle('held',held);
      n.mark.classList.toggle('held',held);
      n.path.classList.toggle('resting',!n.d.active);
      n.path.classList.toggle('selected',isSelected);
    }
  }
  function shipTick(now){
    shipFrame=requestAnimationFrame(shipTick);
    // Nothing to animate when every hull is at rest: the mark is the
    // measurement, and repainting it sixty times a second would move nothing.
    var idle=!shipNodes.length||!shipMoving()||!$('mapShips').checked;
    var minimum=idle?SHIP_IDLE_MS:(prefersReducedMotion()?SHIP_REDUCED_MOTION_MS:0);
    if(now-shipLastDrawMs<minimum)return;
    shipLastDrawMs=now;
    renderShipFrame();
  }

  function shipNoteText(){
    var g=latestGame;
    if(!g||g.reporting!==true)
      return 'Ships: the game server is not reporting, so none is drawn.';
    if(!shipModel())
      return 'Ships: this game server predates ship geometry telemetry (stats schema '
        +(g.schemaVersion||'unknown')+'), so hulls are drawn at their last reported '
        +'position with no shape and no motion between snapshots.';
    if(!shipNodes.length)
      return 'Ships: the game server reports no built ships in the world, so none is drawn.';
    var shaped=0,moving=0,docked=0;
    shipNodes.forEach(function(n){
      if(n.d.hull&&n.d.hull.present)shaped++;
      if(!shipIsMeasuredExactly(n.state))moving++;
      if(n.d.hull&&n.d.hull.docked)docked++;
    });
    var m=shipModel(),age=shipAge(),t=SHIPS?SHIPS.reckoned(age):0;
    return 'Ships (live): '+plural(shipNodes.length,'built ship','built ships')+' - '
      +shaped+' drawn as the real hull their owner built, '+(shipNodes.length-shaped)
      +' with no decodable hull shape, '+docked+' docked in a shipyard, '+moving+' under way. '
      +'Position is MEASURED - it arrives with the game server’s stats snapshot like everything '
      +'else live on this map - and this browser '
      +'carries a moving hull forward along the velocity the server itself reported, for at most '
      +(Number(m.windowSeconds)||0).toFixed(1)+' s - the point at which the server’s own '
      +(Number(m.accelMps2)||0).toFixed(1)+' m/s² acceleration limit could have put it '
      +(Number(m.toleratedErrorMetres)||0).toFixed(0)+' m out. Right now the last measurement is '
      +age.toFixed(1)+' s old and '+t.toFixed(1)+' s of that is being carried; a dashed outline '
      +'is a hull this browser moved, a solid one is a hull the server placed.';
  }

  // ---- the hull schematic in the ship card --------------------------------
  // THE CARD DRAWS THE SHIP, not an icon of one. The map already shows the hull
  // from above; what a card can add is the half of the build the plan view
  // cannot express. A ShipPlan is keyed (cellNumber, deckNumber) - along-ship
  // and VERTICAL - so the deck axis is half of what the player made, and from
  // above a two-deck ship and a one-deck ship are the same drawing.
  //
  // So the card is a general arrangement: PLAN above, PROFILE below, both at one
  // scale on one keel axis with the bow to the right, so a feature in one view
  // sits directly over the same feature in the other. Both outlines come off the
  // player's own ShipPlan bytes - the plan ring with the live payload, the
  // elevation from the geometry document below - and the parts are drawn where
  // the mount ledger says they were bolted on.
  //
  // WHERE THE GEOMETRY COMES FROM, and why it is not in the poll. A hull's shape
  // is STATIC: the elevation, the decks and the part places do not change from
  // one snapshot to the next, while a ship's position does. The live feed is
  // read every few seconds by every viewer, so a drawing riding it would be
  // re-sent to say the same thing forever - the same reason an island's
  // coastline is served from its own document rather than with every poll. It is
  // fetched ONCE per hull, when a card is opened, and again only when the live
  // feed's geometryRevision says the drawing has actually changed (someone
  // mounted a lamp). A hull with no revision, or a fetch that fails, is SAID so
  // in the card - never papered over with a substitute shape.
  // Keyed on the hull, never pruned: a world holds a handful of ships and a
  // stale entry cannot be wrong, because the revision it holds is a hash of the
  // drawing - a hull rebuilt into a different shape has a different revision and
  // is refetched, and one rebuilt into the SAME shape is the same drawing.
  var SHIP_GEOM={};   // hullEntityId -> {want,state,data,reason}

  var SHIP_KIND_WORDS={helm:'Helm',sail:'Sail',engine:'Engine',wing:'Wing',
                       lamp:'Lamp',core:'Sky core',deck:'Deck piece',part:'Other part'};
  function shipKind(k){return SHIP_KIND_WORDS[k]?k:'part';}

  function shipCardChanged(id){
    if(mapSelection.kind==='ship'&&String(mapSelection.hullEntityId)===id)renderMapDetail();
  }
  // The geometry for one ship, at the revision the live feed currently reports.
  // Returns immediately with whatever state it is in; the card draws that state
  // and is re-rendered when a fetch lands.
  function shipGeometry(d){
    var id=String(d.hullEntityId),want=Number((d.hull||{}).geometryRevision)||0;
    var e=SHIP_GEOM[id];
    if(e&&e.want===want)return e;
    e={want:want,state:want?'loading':'absent',data:null,reason:''};
    SHIP_GEOM[id]=e;
    if(!want)return e;
    fetch(MARKS.shipGeometryUrl(id,want),{headers:{'Accept':'application/json'}})
      .then(function(r){return r.ok?r.json():null;})
      .then(function(j){
        // A newer revision may have superseded this request while it was out.
        if(SHIP_GEOM[id]!==e)return;
        if(!j){e.state='error';e.reason='unreachable';}
        else if(j.ok!==true){e.state='error';e.reason=String(j.reason||'refused');}
        else{e.state='ok';e.data=j.geometry||{};}
        shipCardChanged(id);
      })
      .catch(function(){
        if(SHIP_GEOM[id]!==e)return;
        e.state='error';e.reason='unreachable';shipCardChanged(id);
      });
    return e;
  }

  // A round number of metres for the scale bar: the largest of these that fits
  // in the 40% of the keel the caller offers it, so the bar is always a number a
  // reader can add up rather than "23.7 m".
  var SHIP_SCALE_STEPS=[1,2,5,10,20,50,100,200,500];
  function shipScaleStep(metres){
    var best=SHIP_SCALE_STEPS[0];
    for(var i=0;i<SHIP_SCALE_STEPS.length;i++)
      if(SHIP_SCALE_STEPS[i]<=metres)best=SHIP_SCALE_STEPS[i];
    return best;
  }

  // The part glyphs. Each kind gets its own SHAPE, not just its own colour, so
  // the schematic still reads for anyone who cannot tell the colours apart.
  function shipPartGlyph(kind,x,y,title){
    var g=svgEl('g',{'class':'sc-part sc-k-'+kind,
                     transform:'translate('+x.toFixed(1)+' '+y.toFixed(1)+')'},title);
    if(kind==='helm'){
      g.appendChild(svgEl('circle',{r:4.2,'class':'sc-glyph'}));
      g.appendChild(svgEl('path',{d:'M -4.2 0 L 4.2 0 M 0 -4.2 L 0 4.2','class':'sc-spoke'}));
    }else if(kind==='sail'){
      g.appendChild(svgEl('path',{d:'M 0 -5 L 4.3 3 L -4.3 3 Z','class':'sc-glyph'}));
    }else if(kind==='engine'){
      g.appendChild(svgEl('rect',{x:-4,y:-3.4,width:8,height:6.8,'class':'sc-glyph'}));
    }else if(kind==='wing'){
      g.appendChild(svgEl('path',{d:'M 0 -4.6 L 4.6 0 L 0 4.6 L -4.6 0 Z','class':'sc-glyph'}));
    }else if(kind==='lamp'){
      g.appendChild(svgEl('circle',{r:2.6,'class':'sc-glyph'}));
    }else if(kind==='core'){
      g.appendChild(svgEl('circle',{r:4.6,'class':'sc-glyph'}));
      g.appendChild(svgEl('circle',{r:1.7,'class':'sc-spoke'}));
    }else if(kind==='deck'){
      g.appendChild(svgEl('rect',{x:-5,y:-1.8,width:10,height:3.6,'class':'sc-glyph'}));
    }else{
      g.appendChild(svgEl('path',{d:'M -3.4 -3.4 L 3.4 3.4 M 3.4 -3.4 L -3.4 3.4','class':'sc-spoke'}));
    }
    return g;
  }

  // Build the drawing. `h` is the hull block off the live feed (the plan ring
  // and the dimensions); `g` is the fetched geometry (the elevation, the decks
  // and the parts) or null. Either half may be missing and the other is still
  // drawn - a card that refused to draw the plan because the elevation had not
  // arrived would be hiding something it already has.
  function shipSchematicSvg(h,g){
    var plan=(h&&h.present===true&&h.outline&&h.outline.length>=6)?h.outline:null;
    var prof=(g&&g.present===true&&g.profile&&g.profile.length>=6)?g.profile:null;
    if(!plan&&!prof)return null;

    var zMin=1e9,zMax=-1e9,xMin=1e9,xMax=-1e9,yMin=1e9,yMax=-1e9,k;
    if(plan)for(k=0;k+1<plan.length;k+=2){
      var px=Number(plan[k])||0,pz=Number(plan[k+1])||0;
      if(px<xMin)xMin=px; if(px>xMax)xMax=px;
      if(pz<zMin)zMin=pz; if(pz>zMax)zMax=pz;
    }
    if(prof)for(k=0;k+1<prof.length;k+=2){
      var qz=Number(prof[k])||0,qy=Number(prof[k+1])||0;
      if(qz<zMin)zMin=qz; if(qz>zMax)zMax=qz;
      if(qy<yMin)yMin=qy; if(qy>yMax)yMax=qy;
    }
    if(!plan){xMin=0;xMax=0;}
    if(!prof){yMin=0;yMax=0;}
    var keel=Math.max(0.01,zMax-zMin),beam=Math.max(0,xMax-xMin),tall=Math.max(0,yMax-yMin);

    // One scale for both bands, fitted to the card's width and to a height that
    // leaves the panel room to be a panel. The bow is to the RIGHT in both.
    // The gap between the bands and the top pad are not taste: a part glyph is
    // drawn ON the edge of its band and reaches about five units past it, and
    // the view label sits above the band, so too tight a gap puts a sail through
    // the word PROFILE. Measured against the tallest case - a part at the head
    // of the hull - rather than nudged until it looked right.
    var W=372,PAD=22,GAP=26,FOOT=30,MAXRATIO=0.80;
    var bands=(beam>0?1:0)+(tall>0?1:0);
    var gap=bands===2?GAP:0;
    // The SVG fills the card's width, so what actually has to be bounded is the
    // ASPECT RATIO, not a pixel height: a card is a card, and a drawing taller
    // than it is wide pushes everything else off the panel. Within that, one
    // scale serves both bands - the plan and the profile must be comparable or
    // the pair is two unrelated pictures.
    var s=Math.min((W-2*PAD)/keel,(W*MAXRATIO-PAD-FOOT-gap)/Math.max(0.01,beam+tall));
    // Centred rather than left-aligned. A hull whose scale is set by its HEIGHT
    // - a stubby ship is genuinely wider than it is long, so its plan view is a
    // tall rectangle - cannot fill the width, and pinning it to the left edge
    // reads as a drawing that failed to load rather than as a short ship.
    var left=(W-keel*s)/2;
    var planPx=beam*s,profPx=tall*s;
    var planTop=PAD,profTop=PAD+planPx+gap;
    var H=PAD+planPx+gap+profPx+FOOT;
    function X(z){return left+(z-zMin)*s;}
    function PY(x){return planTop+(x-xMin)*s;}
    function QY(y){return profTop+(yMax-y)*s;}

    var svg=svgEl('svg',{'class':'ship-schematic',viewBox:'0 0 '+W.toFixed(1)+' '+H.toFixed(1),
      role:'img','aria-label':'Schematic of this hull: plan view above, side elevation below, '
        +keel.toFixed(1)+' metres from stern to bow'});

    if(plan){
      var pd='M';
      for(k=0;k+1<plan.length;k+=2)
        pd+=' '+X(Number(plan[k+1])).toFixed(1)+' '+PY(Number(plan[k])).toFixed(1)
           +(k+3<plan.length?' L':'');
      svg.appendChild(svgEl('path',{d:pd+' Z','class':'sc-hull'}));
      // The centreline. A stock hull cell is twelve metres of beam to four of
      // keel, so a short ship is genuinely wider than it is long and without
      // this the plan view gives no clue which way is forward.
      svg.appendChild(svgEl('path',{d:'M '+X(zMin).toFixed(1)+' '+PY(0).toFixed(1)
        +' L '+X(zMax).toFixed(1)+' '+PY(0).toFixed(1),'class':'sc-centre'}));
      var planLabel=svgEl('text',{x:left,y:planTop-11,'class':'sc-view'});
      planLabel.textContent='Plan';svg.appendChild(planLabel);
    }

    if(prof){
      var qd='M';
      for(k=0;k+1<prof.length;k+=2)
        qd+=' '+X(Number(prof[k])).toFixed(1)+' '+QY(Number(prof[k+1])).toFixed(1)
           +(k+3<prof.length?' L':'');
      svg.appendChild(svgEl('path',{d:qd+' Z','class':'sc-hull'}));

      // Each deck at the height it actually is, spanning only the cells it
      // actually covers: an upper deck over the after two cells of a six-cell
      // hull is a poop deck, and a full-length line would be a claim the hull
      // bytes do not make.
      ((g&&g.decks)||[]).forEach(function(deck,i){
        var y=QY(Number(deck.planeMetres)||0);
        svg.appendChild(svgEl('path',{d:'M '+X(Number(deck.sternZMetres)||0).toFixed(1)+' '+y.toFixed(1)
          +' L '+X(Number(deck.bowZMetres)||0).toFixed(1)+' '+y.toFixed(1),'class':'sc-deck'},
          'Deck '+(i+1)+' - its walkable plane is '+(Number(deck.planeMetres)||0).toFixed(2)
          +' m above the keel'));
      });
      var profLabel=svgEl('text',{x:left,y:profTop-11,'class':'sc-view'});
      profLabel.textContent='Profile';svg.appendChild(profLabel);
    }

    // The parts, on both views, at the hull-local place the mount ledger holds.
    ((g&&g.parts)||[]).forEach(function(part){
      var kind=shipKind(String(part.kind||'part'));
      var words=(part.title&&String(part.title))||SHIP_KIND_WORDS[kind];
      var z=Number(part.z)||0,x=Number(part.x)||0,y=Number(part.y)||0;
      var where=' at '+z.toFixed(1)+' m fore-and-aft, '+x.toFixed(1)+' m off the centreline, '
                +y.toFixed(1)+' m up';
      if(plan)svg.appendChild(shipPartGlyph(kind,X(z),PY(x),words+where));
      if(prof)svg.appendChild(shipPartGlyph(kind,X(z),QY(y),words+where));
    });

    // Which end is which, and how big any of it is. Without the bar a reader
    // cannot tell a twelve-metre sloop from a sixty-metre freighter: both fill
    // the card.
    var base=PAD+planPx+gap+profPx;
    svg.appendChild(svgEl('path',{d:'M '+(X(zMax)-11).toFixed(1)+' '+(base+13).toFixed(1)
      +' L '+X(zMax).toFixed(1)+' '+(base+13).toFixed(1),'class':'sc-bow'}));
    svg.appendChild(svgEl('path',{d:'M '+(X(zMax)-4).toFixed(1)+' '+(base+10).toFixed(1)
      +' L '+X(zMax).toFixed(1)+' '+(base+13).toFixed(1)
      +' L '+(X(zMax)-4).toFixed(1)+' '+(base+16).toFixed(1),'class':'sc-bow'}));
    var bow=svgEl('text',{x:X(zMax)-14,y:base+16,'class':'sc-axis','text-anchor':'end'});
    bow.textContent='bow';svg.appendChild(bow);

    var step=shipScaleStep(keel/2.5);
    svg.appendChild(svgEl('path',{d:'M '+left.toFixed(1)+' '+(base+13).toFixed(1)
      +' L '+(left+step*s).toFixed(1)+' '+(base+13).toFixed(1),'class':'sc-bar'}));
    svg.appendChild(svgEl('path',{d:'M '+left.toFixed(1)+' '+(base+9.5).toFixed(1)
      +' L '+left.toFixed(1)+' '+(base+16.5).toFixed(1)
      +' M '+(left+step*s).toFixed(1)+' '+(base+9.5).toFixed(1)
      +' L '+(left+step*s).toFixed(1)+' '+(base+16.5).toFixed(1),'class':'sc-bar'}));
    var bar=svgEl('text',{x:left+step*s+6,y:base+16,'class':'sc-axis'});
    bar.textContent=step+' m';svg.appendChild(bar);
    return svg;
  }

  // The legend under the drawing: one chip per kind actually mounted, with the
  // same glyph the drawing uses, so nothing on the schematic is unexplained.
  function shipPartLegend(g){
    var parts=(g&&g.parts)||[],seen={},order=[];
    parts.forEach(function(p){
      var kind=shipKind(String(p.kind||'part'));
      if(!seen[kind]){seen[kind]={n:0,title:''};order.push(kind);}
      seen[kind].n++;
      if(!seen[kind].title&&p.title)seen[kind].title=String(p.title);
    });
    if(!order.length)return null;
    var wrap=el('div','sc-legend');
    order.forEach(function(kind){
      var item=el('span','sc-key');
      var mark=svgEl('svg',{'class':'ship-schematic sc-swatch',viewBox:'-8 -8 16 16',
                            'aria-hidden':'true',focusable:'false'});
      mark.appendChild(shipPartGlyph(kind,0,0));
      item.appendChild(mark);
      item.appendChild(el('span','',(seen[kind].title||SHIP_KIND_WORDS[kind])
        +(seen[kind].n>1?(' ×'+seen[kind].n):'')));
      wrap.appendChild(item);
    });
    return wrap;
  }

  // The whole block, including what it says when it cannot draw. Every branch
  // NAMES what is missing: an empty panel is the one answer that tells a reader
  // nothing, and "the hull bytes will not decode" and "the game server is not
  // reporting" and "this build publishes no elevation" are three different
  // things a reader may want to act on.
  function shipSchematicBlock(d,h){
    var block=mdBlock('The ship, drawn');
    var g=shipGeometry(d),data=(g.state==='ok')?g.data:null;
    var svg=shipSchematicSvg(h,data);

    var planDrawn=!!(h&&h.present===true);
    var parts=(data&&data.parts)||[];
    if(svg)block.appendChild(svg);
    // The legend is a list of what is BOLTED TO THIS SHIP, so it is worth
    // showing even when there is no hull to draw it on: knowing a helm is
    // aboard is a different fact from knowing the shape of the ship it is
    // aboard, and losing the first because we lack the second would be the
    // card hiding something it has.
    var legend=shipPartLegend(data);
    if(legend)block.appendChild(legend);

    if(!planDrawn){
      block.appendChild(el('p','md-p',MARKS.showsMethod
        ? ('No hull shape is published for this ship, so there is nothing to draw. '
           +'The game server could not find or could not decode the ShipPlan bytes for this hull. '
           +'A substitute shape is deliberately NOT drawn: a made-up hull beside real ones would '
           +'be worse than no hull at all.')
        : 'We do not have this hull’s shape, so it cannot be drawn.'));
      if(parts.length){
        block.appendChild(el('p','md-p','What is mounted on it is still known - listed above - '
          +'but with no hull to place '+(parts.length===1?'it':'them')+' on, nothing is drawn.'));
      }
    }
    if(g.state==='loading'){
      block.appendChild(el('p','md-p','Fetching this hull’s elevation…'));
    }else if(g.state==='absent'){
      block.appendChild(el('p','md-p',MARKS.showsMethod
        ? ('This game server publishes no hull geometry, so only the plan view is drawn - '
           +'no side elevation, no decks and no mounted parts. That is an older game server, '
           +'not a ship without decks.')
        : 'Only the view from above is available for this ship.'));
    }else if(g.state==='error'){
      block.appendChild(el('p','md-p','This hull’s elevation could not be fetched ('
        +g.reason+'), so only the plan view is drawn.'));
    }else if(data&&data.present!==true&&planDrawn){
      block.appendChild(el('p','md-p',MARKS.showsMethod
        ? ('No side elevation is published for this hull, so the decks are not drawn. '
           +'The plan view above is still the real outline.')
        : 'No side view is available for this ship.'));
    }else if(svg&&MARKS.showsMethod){
      block.appendChild(el('p','md-p','Both views are the player’s own hull: the game server decodes '
        +'the ShipPlan bytes this ship was crafted from and takes the widest point of each of its '
        +plural(Number(h.sectionCount)||0,'section','sections')+' for the plan and the top and '
        +'bottom of each for the elevation, at the game’s own hull scale, in metres. The decks are '
        +'the levels the hull actually carries, drawn only across the cells they cover, and each '
        +'part is at the hull-local place its owner bolted it to. The drawing is fetched once per '
        +'hull rather than with every snapshot: a hull’s shape is static, and only its position '
        +'moves.'));
    }
    return block;
  }

  function selectShip(hullEntityId){
    var syncDomain=arguments.length<2?true:arguments[1];
    mapSelection={kind:'ship',hullEntityId:hullEntityId};
    clearMapHighlights();
    renderShipFrame();
    renderMapDetail();
    if(syncDomain!==false){
      var d=shipByHull(hullEntityId);if(d&&d.domainId)selectRuntimeDomain(d.domainId,false);
    }
  }
  function shipByHull(hullEntityId){
    for(var i=0;i<latestDomains.length;i++)
      if(String(latestDomains[i].hullEntityId)===String(hullEntityId))return latestDomains[i];
    return null;
  }
  function shipMaterialText(h){
    var parts=[];
    if(h.woodId)parts.push(h.woodId+' (quality '+(Number(h.woodQuality)||1)+')');
    if(h.metalId)parts.push(h.metalId+' (quality '+(Number(h.metalQuality)||1)+')');
    return parts.length?parts.join(' and '):'not recorded';
  }
  function detailShip(panel,scroll,hullEntityId){
    var d=shipByHull(hullEntityId);
    var head=el('div','md-head');
    head.appendChild(backButton('Whole world',function(){selectWorld();}));
    head.appendChild(el('div','md-kicker','Live ship'));
    if(!d){
      head.appendChild(el('h3','md-title','Ship no longer reported'));
      head.appendChild(subLine(['Hull entity '+hullEntityId]));
      panel.appendChild(head);
      var gone=mdBlock('What happened');
      gone.appendChild(el('p','md-p',MARKS.showsMethod?('This hull was in the last snapshot but is not in this one. '
        +'It has been deleted, salvaged, or its domain has been torn down. Nothing is drawn for it.')
        :'This ship is no longer here.'));
      scroll.appendChild(gone);
      return;
    }
    var h=d.hull||{},s=shipState(d);
    var speed=Math.sqrt(s.vx*s.vx+s.vz*s.vz);
    head.appendChild(el('h3','md-title',MARKS.shipTitle(d)));
    // Whether anyone is AT THE HELM is a fact about a person's whereabouts, so
    // the crew line is the operator's; every page still gets the ship's own
    // state (docked, under way).
    head.appendChild(subLine(MARKS.shipCrewWords(d).concat([
      h.docked?'Docked in a shipyard':'Not docked',
      d.active?'Live cadence':'Resting'])));
    if(MARKS.shipIdRow(d))head.appendChild(el('div','md-id',MARKS.shipIdRow(d)));
    panel.appendChild(head);

    var stats=el('div','md-stats');
    stats.appendChild(statTile(h.present?(Number(h.keelMetres)||0).toFixed(1):'—','Keel, metres'));
    stats.appendChild(statTile(h.present?(Number(h.beamMetres)||0).toFixed(1):'—','Beam, metres'));
    stats.appendChild(statTile(d.deckCount||0,'Deck panels'));
    stats.appendChild(statTile(d.mountedPartCount||0,'Mounted parts'));
    if(MARKS.crewTile)MARKS.crewTile(stats,d,statTile);
    stats.appendChild(statTile(speed.toFixed(1),'Speed, m/s'));
    scroll.appendChild(stats);

    // The drawing comes FIRST, above the tables. The card is opened to look at
    // a ship, and a reader should not have to scroll past three blocks of prose
    // to find the picture of it.
    scroll.appendChild(shipSchematicBlock(d,h));

    var shape=mdBlock('The shape on the map');
    if(h.present){
      shape.appendChild(el('p','md-p',MARKS.showsMethod
        ? ('The outline drawn for this ship is the plan view of the hull '
           +'its owner built. The game server decodes the ShipPlan bytes this hull was crafted from '
           +'and takes the widest point of each of its '
           +plural(Number(h.sectionCount)||0,'section','sections')+', at the game’s own hull scale, '
           +'in metres - so the taper, the curve of a section pulled outboard and the rake of an '
           +'overhanging prow on the map are the ones on the ship.')
        : 'The real outline of this hull, seen from above.'));
      shape.appendChild(kv([
        ['Keel, stern to bow',(Number(h.keelMetres)||0).toFixed(2)+' m'],
        ['Beam, port to starboard',(Number(h.beamMetres)||0).toFixed(2)+' m'],
        ['Deck plane above the keel',(Number(h.deckPlaneMetres)||0).toFixed(2)+' m'],
        ['Bow, hull-local',(Number(h.bowLocalZMetres)||0).toFixed(2)+' m'],
        ['Stern, hull-local',(Number(h.sternLocalZMetres)||0).toFixed(2)+' m'],
        ['Hull cells',String(Number(h.cellCount)||0)],
        ['Hull decks',String(Number(h.hullDeckCount)||0)],
        ['Sections in the outline',String(Number(h.sectionCount)||0)]]));
      if(h.keelIsLongestAxis!==true){
        if(MARKS.showsMethod)shape.appendChild(el('p','md-p','This hull is WIDER THAN IT IS LONG - its beam exceeds its '
          +'keel - so its bow runs across its short side. That is the design, not a drawing error: '
          +'a stock hull cell is twelve metres of beam to four of keel, so a short ship really is '
          +'broader than it is long, and the outline shows it that way.'));
      }
    }else{
      shape.appendChild(el('p','md-p',MARKS.showsMethod
        ? ('No hull shape is published for this ship, so it is drawn as a '
           +'plain mark and nothing about its size is shown. That means the game server could not find '
           +'or could not decode the ShipPlan bytes for this hull. A substitute shape is deliberately '
           +'NOT drawn: a made-up outline on a map of real ones would be worse than no outline.')
        : 'We do not have this hull’s shape, so it is drawn as a plain mark.'));
    }
    scroll.appendChild(shape);

    var where=mdBlock('Where it is, and how sure that is');
    var age=shipAge(),reck=SHIPS?SHIPS.reckoned(age):0;
    var p=SHIPS?SHIPS.poseAt(s,age):{x:s.x,z:s.z,yaw:s.yaw};
    where.appendChild(kv([
      ['Measured position','X '+s.x.toFixed(1)+'  Y '+(Number(d.y)||0).toFixed(1)+'  Z '+s.z.toFixed(1)],
      ['Measured heading',(((s.yaw*180/Math.PI)%360+360)%360).toFixed(1)+'° from north'],
      ['Measured '+(reck>0?'':'and drawn '),age.toFixed(1)+' s ago'],
      ['Velocity reported by the server','X '+s.vx.toFixed(2)+'  Y '+(Number(d.vyMps)||0).toFixed(2)
        +'  Z '+s.vz.toFixed(2)+' m/s'],
      ['Turn rate reported',(s.yawRate*180/Math.PI).toFixed(2)+' °/s']]));
    if(shipIsMeasuredExactly(s)){
      where.appendChild(el('p','md-p',MARKS.showsMethod
        ? ('This hull is AT REST: the server reports exactly zero on every '
           +'axis, so the mark on the map is the measurement itself. Nothing about its position has '
           +'been smoothed, extrapolated or guessed.')
        : 'Sitting still.'));
    }else if(SHIPS){
      where.appendChild(kv([
        ['Drawn position','X '+p.x.toFixed(1)+'  Z '+p.z.toFixed(1)],
        ['Carried forward by',reck.toFixed(2)+' s of dead reckoning'],
        ['Could be out by at most',SHIPS.errorBound(reck).toFixed(1)+' m']]));
      if(MARKS.showsMethod)where.appendChild(el('p','md-p','This hull is UNDER WAY, so the mark is not purely a '
        +'measurement. The browser carries it along the velocity the server itself reported - the '
        +'same thing the game client does between two control points - and stops after '
        +SHIPS.windowSeconds.toFixed(1)+' s, which is where the server’s own acceleration '
        +'limit could have put the guess more than '
        +(Number((shipModel()||{}).toleratedErrorMetres)||0).toFixed(0)+' m out. Its outline is '
        +'drawn DASHED for exactly as long as it is being carried.'));
      if(age>reck+0.05){
        if(MARKS.showsMethod)where.appendChild(el('p','md-p','The mark has STOPPED. The last measurement is now '
          +age.toFixed(1)+' s old, past the '+SHIPS.windowSeconds.toFixed(1)+' s this browser is '
          +'allowed to carry it, so the hull is being held where the budget ran out rather than '
          +'drawn gliding on nothing. The real ship has kept moving.'));
      }
    }else{
      if(MARKS.showsMethod)where.appendChild(el('p','md-p','This game server publishes no ship-motion model, so nothing is '
        +'carried forward at all: the mark is the last measured position and it will step when the '
        +'next snapshot lands.'));
    }
    scroll.appendChild(where);

    var built=mdBlock(MARKS.shipBuiltHeading);
    // WHO owns, pilots and rides a hull is the operator's row set, supplied by
    // the page. The public map returns none of them - not "unowned", not
    // "none", but no row at all - so a projection that one day carried an
    // owner still could not put it on the page.
    built.appendChild(kv(MARKS.shipIdentityRows(d,h).concat([
      ['Hull materials',shipMaterialText(h)],
      ['Docked in a shipyard',h.docked?'yes':'no'],
      ['Deck panels',String(d.deckCount||0)],
      ['Mounted parts',String(d.mountedPartCount||0)],
      ['Checkout subscribers',String(d.subscriberCount||0)],
      ['Authority generation',String(d.authorityGeneration||0)],
      ['Replication','sequence '+(d.replicationSequence||0)+' at '+(d.cadenceMs||0)+' ms'],
      ['Last delivery',(Number(d.deliveryAgeMs)<0)?'never':((d.deliveryAgeMs||0)+' ms ago')]])));
    built.appendChild(el('p','md-p',MARKS.shipBuiltNote));
    scroll.appendChild(built);
  }
