  // ---- live wildlife -----------------------------------------------------
  // WHY THIS IS NOT A POSITION FEED. Every creature on the game server moves on
  // a CLOSED FORM of the clock: a manta's perimeter orbit and a jellyfish
  // shoal's day/night drift are functions of elapsed seconds, with no
  // integration, no entropy and no remembered pose (IslandFaunaMovement). The
  // stats snapshot lands every three seconds, so pushing 460 positions through
  // it would cost bandwidth AND still teleport every animal three times a
  // minute. What the server sends instead is the ROSTER - who is alive, on
  // which island - and its own fauna CLOCK, and this browser evaluates the same
  // function. The result is smooth at any frame rate and is the pose the server
  // actually holds, not an interpolation between two stale samples.
  //
  // NONE OF THE NUMBERS ARE RESTATED HERE. worldMap.faunaModel is a projection
  // of IslandFaunaMapModel.Constants and each island's `fauna` block is
  // precomputed from its envelope by IslandFaunaMapModel.MotionFor, so retuning
  // a manta's speed or an island's geometry moves this map with it. What IS
  // restated is the SHAPE of the formulas below, and that is guarded:
  // AdminFaunaParityTests extracts the marked block, runs it against the C# at
  // fixed timestamps, and fails if the two disagree by a millimetre.
  //
  // Positions are ISLAND-LOCAL and added to the MapFile placement, exactly as
  // the preserved coastline above is, so a creature is always drawn in the
  // right relationship to the rock beneath it.

  // ==== FAUNA MOTION MIRROR BEGIN ====
  function faunaMotion(M){
    function fraction(v){var f=v-Math.floor(v);return f<0?f+1:(f>=1?0:f);}
    function smoothStep(t){return t<=0?0:(t>=1?1:t*t*(3-2*t));}
    function schoolPhase(schoolIndex){return fraction(schoolIndex*M.goldenRatioFraction);}
    function cycleFraction(t){return fraction(t/M.dayNightCycleSeconds);}
    // How DAYTIME it is, 0..1, ramped across dawn and dusk. The ramp is what
    // keeps a shoal drifting rather than teleporting at the phase boundary.
    function dayness(t){
      var c=cycleFraction(t),ramp=M.phaseTransitionFraction;
      return Math.min(smoothStep((c-M.dayBeginsAtCycleFraction)/ramp),
                      smoothStep((M.dayEndsAtCycleFraction-c)/ramp));
    }
    function mantaVertical(lap){return M.mantaVerticalSpanRatio*Math.sin(fraction(lap)*Math.PI);}
    function mantaCentre(p,schoolIndex,t){
      var lap=fraction(t/p.mantaLapSeconds+schoolPhase(schoolIndex)),th=lap*2*Math.PI;
      return {x:p.cx+p.mantaOrbitRadius*Math.sin(th),
              y:p.cy+p.halfHeight*mantaVertical(lap),
              z:p.cz+p.mantaOrbitRadius*Math.cos(th)};
    }
    function jellyCentre(p,schoolIndex,t){
      var d=dayness(t);
      var th=fraction(t/M.jellySecondsPerRevolution+schoolPhase(schoolIndex))*2*Math.PI;
      var r=p.jellyLateralRadius
        *(M.jellyNightRadiusRatio+(M.jellyDayRadiusRatio-M.jellyNightRadiusRatio)*d);
      var nightY=p.minY+(p.maxY-p.minY)*M.walkableHeightFraction;
      return {x:p.cx+r*Math.sin(th),y:nightY+(p.minY-nightY)*d,z:p.cz+r*Math.cos(th)};
    }
    function memberOffset(memberIndex,radius,verticalRadius,t){
      var weave=t*M.weaveRadiansPerSecond;
      var angle=memberIndex*M.goldenAngleRadians+weave;
      var radial=radius*Math.sqrt(fraction((memberIndex+1)*M.goldenRatioFraction));
      var vertical=verticalRadius
        *Math.sin((memberIndex+1)*M.goldenAngleRadians*0.5+weave*0.6);
      return {x:radial*Math.cos(angle),y:vertical,z:radial*Math.sin(angle)};
    }
    function cluster(species){
      return species==='manta'
        ? {r:M.mantaSchoolRadius,v:M.mantaSchoolVerticalRadius}
        : {r:M.jellyShoalRadius,v:M.jellyShoalVerticalRadius};
    }
    function schoolCentre(p,species,schoolIndex,t){
      return species==='manta'?mantaCentre(p,schoolIndex,t):jellyCentre(p,schoolIndex,t);
    }
    function localPose(p,species,schoolIndex,memberIndex,t){
      var c=schoolCentre(p,species,schoolIndex,t),k=cluster(species);
      var o=memberOffset(memberIndex,k.r,k.v,t);
      return {x:c.x+o.x,y:c.y+o.y,z:c.z+o.z};
    }
    return {localPose:localPose,schoolCentre:schoolCentre,cluster:cluster,
            dayness:dayness,cycleFraction:cycleFraction};
  }
  // ==== FAUNA MOTION MIRROR END ====

  var FAUNA=null;        // the evaluator, built once the model has loaded
  var faunaAnchor=null;  // {clock,perf} - the server's fauna clock carried on ours
  var faunaStat=null;    // the live section, or null when nothing may be drawn
  var faunaRoster=[];    // [{node,p,ox,oz,manta,jelly}] joined to the drawn islands
  var faunaById={};      // islandId -> drawn island node
  var faunaPool=[];      // reused SVG groups: this repaints up to 60 times a second
  var faunaFrame=null,faunaLastDrawMs=-1e9,faunaLastNoteMs=-1e9,faunaSignature='';
  // Members are separated only once a school is big enough on screen to have
  // members worth separating. A manta school is 12 m across, which at
  // whole-world zoom is a third of a pixel: four darts stacked on one dot say
  // nothing a single moving mark does not, and cost 460 nodes to say it.
  var FAUNA_MEMBER_PIXELS=5;
  // Two samples of the SAME path give the bearing. Far enough apart to be well
  // above floating-point noise, short enough that it is the tangent.
  var FAUNA_HEADING_DT=0.35;
  var FAUNA_REDUCED_MOTION_MS=1000, FAUNA_IDLE_MS=400;

  function faunaNow(){
    return (window.performance&&performance.now)?performance.now():Date.now();
  }
  // Carry the server's fauna clock on OUR monotonic one rather than on the wall
  // clock, so a browser whose time is thirty seconds out still draws the
  // creatures where the server has them. Re-anchoring on every poll would make
  // the whole world twitch once every four seconds by however much the two
  // clocks disagree; re-anchoring only on a real jump catches the two cases
  // that matter - a restarted game server, and a tab that was suspended.
  function noteFauna(g){
    var f=(g&&g.fauna)||null;
    var live=!!(f&&f.present===true&&f.enabled===true&&(f.islands||[]).length
                &&g.reporting===true&&g.stale!==true);
    var was=faunaSignature;
    faunaStat=live?f:null;
    if(!live){
      faunaAnchor=null;faunaRoster=[];faunaSignature='';
      if(was!==faunaSignature)renderMapDetail();
      return;
    }
    var now=faunaNow(),reported=Number(f.clockSeconds)||0;
    var predicted=faunaAnchor?faunaAnchor.clock+(now-faunaAnchor.perf)/1000:null;
    if(predicted===null||Math.abs(predicted-reported)>2)
      faunaAnchor={clock:reported,perf:now};
    faunaRoster=[];
    (f.islands||[]).forEach(function(row){
      var node=faunaById[row.islandId];
      if(!node||!node.island||!node.island.fauna)return;
      faunaRoster.push({node:node,p:node.island.fauna,
        ox:Number(node.island.x),oz:-Number(node.island.z),
        manta:Math.max(0,Number(row.mantaRays)||0),
        jelly:Math.max(0,Number(row.jellyFish)||0)});
    });
    // The detail panel STATES what is alive, so it has to be rebuilt when that
    // changes - but only then. Re-rendering it every poll would throw away the
    // reader's scroll position four times a minute.
    faunaSignature=faunaRoster.length+':'+f.liveCount;
    if(was!==faunaSignature)renderMapDetail();
  }
  function faunaElapsed(){
    return faunaAnchor?faunaAnchor.clock+(faunaNow()-faunaAnchor.perf)/1000:0;
  }
  function faunaLiveOn(islandId){
    if(!faunaStat)return null;
    var rows=faunaStat.islands||[];
    for(var i=0;i<rows.length;i++)if(rows[i].islandId===islandId)return rows[i];
    return null;
  }
  function faunaVisible(){
    var box=$('mapFauna');
    return !!(FAUNA&&faunaRoster.length&&box&&box.checked);
  }
  function faunaInView(row){
    var pad=Math.max(600,mapView.w*0.06);
    var x=row.ox+row.p.cx,y=row.oz-row.p.cz;
    return x>=mapView.x-pad&&x<=mapView.x+mapView.w+pad
        &&y>=mapView.y-pad&&y<=mapView.y+mapView.h+pad;
  }
  function faunaPush(out,kind,row,schoolIndex,memberIndex,t,member){
    var p=row.p;
    var a=member?FAUNA.localPose(p,kind,schoolIndex,memberIndex,t)
                :FAUNA.schoolCentre(p,kind,schoolIndex,t);
    var b=member?FAUNA.localPose(p,kind,schoolIndex,memberIndex,t+FAUNA_HEADING_DT)
                :FAUNA.schoolCentre(p,kind,schoolIndex,t+FAUNA_HEADING_DT);
    // Screen space: x is world east, y is world NORTH NEGATED, as everything
    // else on this map is. The glyph's nose is at -y, so the rotation that
    // aims it along (sx,sy) is atan2(sx,-sy).
    var sx=b.x-a.x,sy=-(b.z-a.z);
    out.push({kind:kind,row:row,member:member,x:row.ox+a.x,y:row.oz-a.z,
      cluster:FAUNA.cluster(kind).r/mapPx,
      deg:(sx*sx+sy*sy)>1e-9?Math.atan2(sx,-sy)*180/Math.PI:0});
  }
  function faunaSpecies(out,row,kind,count,t,inView){
    if(count<=0)return;
    var schools=Math.max(1,Number(row.p.schools)||1);
    if((FAUNA.cluster(kind).r/mapPx)<FAUNA_MEMBER_PIXELS){
      for(var s=0;s<schools;s++)faunaPush(out,kind,row,s,0,t,false);
      return;
    }
    // Members are only worth computing where they can be seen. Off-screen
    // islands keep their schools, which cost two evaluations each.
    if(!inView){for(var q=0;q<schools;q++)faunaPush(out,kind,row,q,0,t,false);return;}
    var size=Math.max(1,Math.round(count/schools));
    for(var j=0;j<schools;j++)
      for(var m=0;m<size;m++)faunaPush(out,kind,row,j,m,t,true);
  }
  function faunaDrawList(t){
    var out=[];
    for(var i=0;i<faunaRoster.length;i++){
      var row=faunaRoster[i],inView=faunaInView(row);
      faunaSpecies(out,row,'manta',row.manta,t,inView);
      // Jellies join from mid zoom. A shoal turns once in ten minutes and
      // breathes in and out over twenty, so at 40 m a pixel it is a stationary
      // dot beside every island - clutter, not life. The mantas lap in minutes
      // and are what reads as alive at that distance.
      if(mapZoomFactor>=2.2)faunaSpecies(out,row,'jelly',row.jelly,t,inView);
    }
    return out;
  }
  function faunaNode(index){
    var n=faunaPool[index];
    if(!n){
      var g=svgEl('g',{});
      var use=svgEl('use',{});
      g.appendChild(use);
      $('mapFaunaLayer').appendChild(g);
      n=faunaPool[index]={g:g,use:use,cls:'',shape:'',hidden:false};
    }
    return n;
  }
  function paintFauna(list){
    var i;
    for(i=0;i<list.length;i++){
      var e=list[i],n=faunaNode(i);
      var cls='fauna '+e.kind+(e.member?' member':' school');
      if(n.cls!==cls){n.g.setAttribute('class',cls);n.cls=cls;}
      // SIZED SO IT NEVER OUTSHOUTS THE ISLAND IT BELONGS TO. At whole-world
      // zoom an island marker is thirteen pixels and a school mark sitting on
      // its shoulder at the same size reads as a second island, which is worse
      // than not drawing it - so a distant school is deliberately small, and it
      // grows once there is room. Members appear only when you are close enough
      // that the island is far larger than any of them.
      // A MEMBER IS SIZED TO ITS OWN SCHOOL. Members appear as soon as the
      // cluster is five pixels across, which is early enough to be useful, and
      // a fixed ten-pixel glyph in a five-pixel cluster is a blob rather than
      // four animals - so the glyph grows with the room it has, to a ceiling
      // that keeps a manta from pretending to be fifty metres long.
      var far=mapZoomFactor<2.2;
      var ceiling=e.kind==='manta'?10:9;
      var size=e.member?Math.max(4.5,Math.min(ceiling,e.cluster*0.85))
                       :(far?7:(e.kind==='manta'?10:8.5));
      size=Math.round(size*2)/2;
      var shape=e.kind+size;
      if(n.shape!==shape){
        n.use.setAttribute('href',e.kind==='manta'?'#mantaSymbol':'#jellySymbol');
        n.use.setAttribute('x',-size/2);n.use.setAttribute('y',-size/2);
        n.use.setAttribute('width',size);n.use.setAttribute('height',size);
        n.shape=shape;
      }
      n.g.setAttribute('transform','translate('+e.x.toFixed(2)+' '+e.y.toFixed(2)
        +') scale('+mapPx+')'+(e.kind==='manta'?(' rotate('+e.deg.toFixed(1)+')'):''));
      if(n.hidden){n.g.style.display='';n.hidden=false;}
    }
    for(;i<faunaPool.length;i++){
      var spare=faunaPool[i];
      if(!spare.hidden){spare.g.style.display='none';spare.hidden=true;}
    }
  }
  function renderFaunaFrame(){
    var layer=$('mapFaunaLayer');if(!layer)return;
    if(!faunaVisible()){
      if(layer.style.display!=='none')layer.style.display='none';
      return;
    }
    if(layer.style.display==='none')layer.style.display='';
    paintFauna(faunaDrawList(faunaElapsed()));
  }
  function fmtShort(seconds){
    seconds=Math.max(0,Math.round(seconds));
    var m=Math.floor(seconds/60),s=seconds%60;
    return m?(m+'m '+(s<10?'0':'')+s+'s'):(s+'s');
  }
  function faunaPhaseText(t){
    var M=worldMap.faunaModel||{},c=FAUNA.cycleFraction(t);
    var day=c>M.dayBeginsAtCycleFraction&&c<M.dayEndsAtCycleFraction;
    var target=day?M.dayEndsAtCycleFraction:M.dayBeginsAtCycleFraction;
    var until=((target-c)%1+1)%1*M.dayNightCycleSeconds;
    return day
      ? ('It is fauna DAY: the shoals have pushed out past the rim and sunk to the underside of the rock. Night in '+fmtShort(until)+'.')
      : ('It is fauna NIGHT: the shoals have drawn back in and risen to the height a player walks at. Day in '+fmtShort(until)+'.');
  }
  function faunaNoteText(){
    if(!FAUNA)return 'Wildlife: the fauna movement model did not load, so no creature is drawn.';
    if(!faunaStat)
      return 'Wildlife: the game server is not reporting an island-fauna roster, so none is drawn. '
        +'Nothing on this map is animated from a guess - no roster and no clock means no creatures.';
    var mantas=0,jellies=0,minSchool=1e9,maxSchool=0,minShoal=1e9,maxShoal=0;
    faunaRoster.forEach(function(r){
      mantas+=r.manta;jellies+=r.jelly;
      if(r.manta){minSchool=Math.min(minSchool,r.manta);maxSchool=Math.max(maxSchool,r.manta);}
      if(r.jelly){minShoal=Math.min(minShoal,r.jelly);maxShoal=Math.max(maxShoal,r.jelly);}
    });
    function span(lo,hi){return lo>hi?'0':(lo===hi?String(lo):(lo+'-'+hi));}
    return 'Wildlife (live): '+plural(mantas+jellies,'creature','creatures')+' on '
      +plural(faunaRoster.length,'island','islands')+' - '+fmt(mantas)+' manta rays in schools of '
      +span(minSchool,maxSchool)+' orbiting their island, '+fmt(jellies)+' jellyfish in shoals of '
      +span(minShoal,maxShoal)+' on a '+fmtShort(Number((worldMap.faunaModel||{}).dayNightCycleSeconds)||0)
      +' day/night cycle. '+faunaPhaseText(faunaElapsed())
      +' These are not sampled positions: the browser evaluates the game server’s own movement '
      +'against the clock the server reports, which is why they move smoothly between snapshots.';
  }
  function faunaTick(now){
    faunaFrame=requestAnimationFrame(faunaTick);
    var idle=!faunaVisible();
    // Reduced motion still MOVES the wildlife - it is a live fact and freezing
    // it would be a lie - but it steps once a second rather than once a frame,
    // so nothing on the page animates continuously.
    var minimum=idle?FAUNA_IDLE_MS:(prefersReducedMotion()?FAUNA_REDUCED_MOTION_MS:0);
    if(now-faunaLastDrawMs<minimum)return;
    faunaLastDrawMs=now;
    renderFaunaFrame();
    if(now-faunaLastNoteMs>=1000){faunaLastNoteMs=now;text('mapFaunaNote',faunaNoteText());}
  }
  function startFauna(){
    var model=worldMap.faunaModel;
    if(!model||!model.dayNightCycleSeconds)return;
    FAUNA=faunaMotion(model);
    if(faunaFrame===null)faunaFrame=requestAnimationFrame(faunaTick);
  }


