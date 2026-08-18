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

  function selectShip(hullEntityId){
    mapSelection={kind:'ship',hullEntityId:hullEntityId};
    clearMapHighlights();
    renderShipFrame();
    renderMapDetail();
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

