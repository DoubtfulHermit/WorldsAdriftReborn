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
    // ---- the ecology (schema v9): groups circulate MOVING FIELD MAXIMA ------
    // The bloom parameters arrive per island in the LIVE feed (they depend on
    // the game server's world seed); only the time part is restated here. The
    // vertical laws are deliberately the RECOVERED ones above - the manta's
    // midpoint-to-top band driven by its orbit fraction, the jelly's day/night
    // blend - because altitude is a recovery and the field is tuning.
    function bloomCentre(b,t){
      var r=b.annulusRadius+b.radialDrift*Math.sin(b.omegaRadial*t+b.phaseRadial);
      var a=b.baseAngle+b.omegaMigration*t
           +b.angularDrift*Math.sin(b.omegaAngular*t+b.phaseAngular);
      return {x:r*Math.sin(a),z:r*Math.cos(a)};
    }
    function groupOrbitRadius(b,species,schoolIndex){
      var ratio=species==='manta'?M.mantaCirculationSigmaRatio:M.jellyCirculationSigmaRatio;
      var spread=1+(M.maxGroupSpread-1)*fraction((schoolIndex+1)*M.goldenRatioFraction);
      return b.sigma*ratio*spread;
    }
    function groupOrbitFraction(b,species,schoolIndex,t){
      var r=groupOrbitRadius(b,species,schoolIndex);
      var speed=species==='manta'?M.mantaOrbitSpeed:M.jellyOrbitSpeed;
      return fraction((speed/Math.max(r,1))*t/(2*Math.PI)+schoolPhase(schoolIndex));
    }
    function ecologyCentre(p,b,species,schoolIndex,t,radiusMultiplier){
      var c=bloomCentre(b,t);
      // The ANGLE always comes from the unscaled radius: a rate that followed
      // a feed's pinch would make the angle an integral of history.
      var r=groupOrbitRadius(b,species,schoolIndex)
        *(radiusMultiplier===undefined?1:radiusMultiplier);
      var a=2*Math.PI*groupOrbitFraction(b,species,schoolIndex,t);
      var y;
      if(species==='manta'){
        // The band keeps the ISLAND lap's pace, not the bloom orbit's: a bloom
        // circuit is half a minute and the recovered climb takes a whole lap.
        y=p.cy+p.halfHeight*mantaVertical(fraction(t/p.mantaLapSeconds+schoolPhase(schoolIndex)));
      }else{
        var d=dayness(t),nightY=p.minY+(p.maxY-p.minY)*M.walkableHeightFraction;
        y=nightY+(p.minY-nightY)*d;
      }
      return {x:p.cx+c.x+r*Math.sin(a),y:y,z:p.cz+c.z+r*Math.cos(a)};
    }
    function bloomFor(p,species,schoolIndex){
      var set=p.blooms&&p.blooms[species];
      if(!set||!set.length)return null;
      return set[((schoolIndex%set.length)+set.length)%set.length];
    }
    // ---- behaviours (Phase 4): the published (behaviour, epoch) descriptor --
    // g = {behaviour,epochSeconds,durationSeconds,bloom,toBloom} from the live
    // feed. Every excursion is NEUTRAL AT ITS EDGES (the bump envelope is zero
    // with zero slope at both ends; a migration ends fully at its destination),
    // so a descriptor up to one poll stale still agrees with the server at the
    // segment boundary - by construction, not by luck.
    function segmentFraction(g,t){
      if(!g||!(g.durationSeconds>0))return 1;
      var f=(t-g.epochSeconds)/g.durationSeconds;
      return f<0?0:(f>1?1:f);
    }
    function bump(f){
      return Math.min(smoothStep(f/M.excursionRamp),smoothStep((1-f)/M.excursionRamp));
    }
    function behaviourRadiusMultiplier(g,t){
      return g&&g.behaviour==='Feed'?1-M.feedRadiusPinch*bump(segmentFraction(g,t)):1;
    }
    function diveFraction(g,t){
      return g&&g.behaviour==='Dive'?bump(segmentFraction(g,t)):0;
    }
    function ecologyGroupBloom(p,species,g,schoolIndex,which){
      var set=p.blooms&&p.blooms[species];
      if(!set||!set.length)return null;
      var idx=g?(which==='to'?g.toBloom:g.bloom):schoolIndex;
      return set[((idx%set.length)+set.length)%set.length];
    }
    function schoolCentre(p,species,schoolIndex,t,g){
      var b=g?ecologyGroupBloom(p,species,g,schoolIndex,'from')
             :bloomFor(p,species,schoolIndex);
      if(!b)return species==='manta'?mantaCentre(p,schoolIndex,t):jellyCentre(p,schoolIndex,t);
      var mult=behaviourRadiusMultiplier(g,t);
      var c=ecologyCentre(p,b,species,schoolIndex,t,mult);
      if(g&&g.behaviour==='Migrate'&&g.toBloom!==g.bloom){
        var b2=ecologyGroupBloom(p,species,g,schoolIndex,'to');
        if(b2){
          var c2=ecologyCentre(p,b2,species,schoolIndex,t,mult);
          var m=smoothStep(segmentFraction(g,t));
          c={x:c.x+(c2.x-c.x)*m,y:c.y+(c2.y-c.y)*m,z:c.z+(c2.z-c.z)*m};
        }
      }
      var dive=diveFraction(g,t);
      if(dive>0){
        var divedY=p.minY-(p.maxY-p.minY)*M.diveBelowFloorFraction;
        c.y=c.y+(divedY-c.y)*dive;
      }
      return c;
    }
    function localPose(p,species,schoolIndex,memberIndex,t,g){
      var c=schoolCentre(p,species,schoolIndex,t,g),k=cluster(species);
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
    // The ecology join (schema v9): per-island bloom parameters and group
    // structure ride the live feed. Attached onto a COPY of the static fauna
    // params so the mirror sees one parameter object either way, and an older
    // game server (no ecology block) leaves the roster exactly as before.
    var eco={},ecoOn=false;
    if(f.ecology&&f.ecology.enabled===true){
      ecoOn=true;
      (f.ecology.islands||[]).forEach(function(row){eco[row.islandId]=row;});
    }
    (f.islands||[]).forEach(function(row){
      var node=faunaById[row.islandId];
      if(!node||!node.island||!node.island.fauna)return;
      var p=node.island.fauna,mg=null,jg=null;
      var e=ecoOn?eco[row.islandId]:null;
      if(e&&(e.blooms||[]).length){
        var blooms={manta:[],jelly:[]};
        (e.blooms||[]).forEach(function(b){
          (b.species==='jelly'?blooms.jelly:blooms.manta).push(b);
        });
        p=Object.assign({},p,{blooms:blooms});
        mg=[];jg=[];
        (e.groups||[]).forEach(function(g){
          (g.species==='jelly'?jg:mg).push({index:Number(g.index)||0,
            members:Math.max(1,Number(g.members)||1),
            // The (behaviour, epoch) descriptor, handed to the mirror as-is.
            behaviour:String(g.behaviour||'Cruise'),
            epochSeconds:Number(g.epochSeconds)||0,
            durationSeconds:Number(g.durationSeconds)||0,
            bloom:Number(g.bloom)||0,
            toBloom:Number(g.toBloom)||0});
        });
      }
      faunaRoster.push({node:node,p:p,
        ox:Number(node.island.x),oz:-Number(node.island.z),
        manta:Math.max(0,Number(row.mantaRays)||0),
        jelly:Math.max(0,Number(row.jellyFish)||0),
        mg:mg,jg:jg});
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
  function faunaPush(out,kind,row,schoolIndex,memberIndex,t,member,g){
    var p=row.p;
    var a=member?FAUNA.localPose(p,kind,schoolIndex,memberIndex,t,g)
                :FAUNA.schoolCentre(p,kind,schoolIndex,t,g);
    var b=member?FAUNA.localPose(p,kind,schoolIndex,memberIndex,t+FAUNA_HEADING_DT,g)
                :FAUNA.schoolCentre(p,kind,schoolIndex,t+FAUNA_HEADING_DT,g);
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
    var members=(FAUNA.cluster(kind).r/mapPx)>=FAUNA_MEMBER_PIXELS&&inView;
    // Ecology group structure when the live feed carries it: each group has its
    // own index (which selects its bloom and phase) and its own member count.
    var groups=kind==='manta'?row.mg:row.jg;
    if(groups&&groups.length){
      for(var g=0;g<groups.length;g++){
        var desc=groups[g];
        if(!members){faunaPush(out,kind,row,desc.index,0,t,false,desc);continue;}
        for(var n=0;n<desc.members;n++)
          faunaPush(out,kind,row,desc.index,n,t,true,desc);
      }
      return;
    }
    var schools=Math.max(1,Number(row.p.schools)||1);
    if(!members){
      // Members are only worth computing where they can be seen. Off-screen
      // islands keep their schools, which cost two evaluations each.
      for(var s=0;s<schools;s++)faunaPush(out,kind,row,s,0,t,false);
      return;
    }
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
    if(!FAUNA)return;
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
    var note='Wildlife (live): '+plural(mantas+jellies,'creature','creatures')+' on '
      +plural(faunaRoster.length,'island','islands')+' - '+fmt(mantas)+' manta rays in schools of '
      +span(minSchool,maxSchool)+' orbiting their island, '+fmt(jellies)+' jellyfish in shoals of '
      +span(minShoal,maxShoal)+' on a '+fmtShort(Number((worldMap.faunaModel||{}).dayNightCycleSeconds)||0)
      +' day/night cycle. '+faunaPhaseText(faunaElapsed());
    if(faunaStat.ecology&&faunaStat.ecology.enabled===true){
      var quiet=0,phases={};
      (faunaStat.ecology.islands||[]).forEach(function(r){
        if(Number(r.quietFactor)===0){quiet++;return;}
        var p=String(r.jellyPhase||'');
        if(p)phases[p]=(phases[p]||0)+1;
      });
      var swing=Object.keys(phases).sort().map(function(k){return phases[k]+' '+k;}).join(', ');
      note+=' The ECOLOGY layer is on: populations follow each island’s own size and schools '
        +'circulate drifting feeding grounds'
        +(swing?('; the population rhythm has '+swing
          +' (the rays trail the jellies by a couple of minutes)'):'')
        +(quiet?('; '+plural(quiet,'island is','islands are')
          +' deliberately quiet - a real zero, not missing data'):'')+'.';
    }
    return note
      +' These are not sampled positions: the browser evaluates the game server’s own movement '
      +'against the clock the server reports, which is why they move smoothly between snapshots.';
  }
  function faunaTick(now){
    faunaFrame=requestAnimationFrame(faunaTick);
    // Idle only when NEITHER the wildlife nor the whale has anything to draw -
    // a world with a whale and no creatures still has something moving in it.
    var idle=!faunaVisible()&&!whaleVisible();
    // Reduced motion still MOVES the wildlife - it is a live fact and freezing
    // it would be a lie - but it steps once a second rather than once a frame,
    // so nothing on the page animates continuously.
    var minimum=idle?FAUNA_IDLE_MS:(prefersReducedMotion()?FAUNA_REDUCED_MOTION_MS:0);
    if(now-faunaLastDrawMs<minimum)return;
    faunaLastDrawMs=now;
    renderFaunaFrame();
    renderWhaleFrame();
    if(now-faunaLastNoteMs>=1000){
      faunaLastNoteMs=now;
      text('mapFaunaNote',faunaNoteText());
      text('mapWhaleNote',whaleNoteText());
    }
  }
  function startFauna(){
    var model=worldMap.faunaModel;
    if(model&&model.dayNightCycleSeconds)FAUNA=faunaMotion(model);
    // The loop starts for the WHALE too: its circuits are a separate static
    // block, so a build that shipped one model and not the other must still
    // animate whatever it does have rather than nothing.
    if(!FAUNA&&!whaleCircuits().length)return;
    if(faunaFrame===null)faunaFrame=requestAnimationFrame(faunaTick);
  }



  // ---- the sky whale ------------------------------------------------------
  // ONE ANIMAL PER REGION, and the same honesty rule the wildlife above
  // follows: nothing here is drawn from a guess. The CIRCUIT is static
  // geometry that ships in worldMap.whaleCircuits - computed once by the game
  // server's own SkyWhalePlan, in TRAVEL ORDER, so the browser never re-derives
  // the ring and cannot fly a different loop than the game does. WHICH regions
  // actually carry a whale, and AT WHAT CLOCK, are live facts that arrive in the
  // stats feed. No roster and no clock means no whale.
  //
  // The waypoints arrive as ISLAND-LOCAL offsets, exactly as the preserved
  // coastlines do, and are added to the drawn MapFile placement - so the animal
  // is always in the right relationship to the rocks it flies between.

  // ==== SKY WHALE MOTION MIRROR BEGIN ====
  function whaleMotion(){
    function fraction(v){var f=v-Math.floor(v);return f<0?f+1:(f>=1?0:f);}
    // Uniform CLOSED Catmull-Rom. The term order below is restated verbatim
    // from SkyWhaleCircuit.CubicPosition/CubicTangent, and
    // AdminSkyWhaleParityTests fails at a nanometre if the two disagree - so do
    // not "tidy" one side into Horner form without doing the same to the other.
    function segmentAt(ring,lap){
      var n=ring.length,s=fraction(lap)*n,i=Math.floor(s);
      if(i>=n)i=n-1;
      return {p0:ring[((i-1)%n+n)%n],p1:ring[i],p2:ring[(i+1)%n],p3:ring[(i+2)%n],t:s-i};
    }
    function cubicPosition(p0,p1,p2,p3,t){
      return 0.5*((2*p1)+((-p0+p2)*t)+(((2*p0)-(5*p1)+(4*p2)-p3)*t*t)
                 +((-p0+(3*p1)-(3*p2)+p3)*t*t*t));
    }
    function cubicTangent(p0,p1,p2,p3,t){
      return 0.5*((-p0+p2)+(2*((2*p0)-(5*p1)+(4*p2)-p3)*t)
                 +(3*(-p0+(3*p1)-(3*p2)+p3)*t*t));
    }
    function positionAt(ring,lap){
      var s=segmentAt(ring,lap);
      return {x:cubicPosition(s.p0.x,s.p1.x,s.p2.x,s.p3.x,s.t),
              y:cubicPosition(s.p0.y,s.p1.y,s.p2.y,s.p3.y,s.t),
              z:cubicPosition(s.p0.z,s.p1.z,s.p2.z,s.p3.z,s.t)};
    }
    function tangentAt(ring,lap){
      var s=segmentAt(ring,lap);
      return {x:cubicTangent(s.p0.x,s.p1.x,s.p2.x,s.p3.x,s.t),
              y:cubicTangent(s.p0.y,s.p1.y,s.p2.y,s.p3.y,s.t),
              z:cubicTangent(s.p0.z,s.p1.z,s.p2.z,s.p3.z,s.t)};
    }
    // Absolute elapsed seconds, never an age: the game server's own circuit is
    // a function of absolute time so that a restart replays the identical path,
    // and a second evaluator that used an age would drift away from it.
    function lapAt(c,t){return fraction(t/c.circuitSeconds+c.phaseFraction);}
    return {positionAt:positionAt,tangentAt:tangentAt,lapAt:lapAt,fraction:fraction};
  }
  // ==== SKY WHALE MOTION MIRROR END ====

  var WHALE=whaleMotion();
  var whaleAnchor=null;   // {clock,perf} - the server's whale clock on ours
  var whaleStat=null;     // the live section, or null when nothing may be drawn
  var whaleRoster=[];     // [{regionId,ring,circuitSeconds,phaseFraction,call}]
  var whalePool=[],whalePathPool=[],whaleCallPool=[],whalePathKey='';
  // Two samples of the SAME path give the bearing, as the fauna glyphs do.
  var WHALE_HEADING_DT=0.6;

  function whaleCircuits(){return (worldMap&&worldMap.whaleCircuits)||[];}
  // The ring in DRAWN world metres: each waypoint's island-local offset added
  // to the MapFile placement the map already draws that island at. Null when an
  // island of the circuit is not on this map - a partial ring would be a
  // different loop, and a different loop is worse than no whale.
  function whaleRing(circuit){
    var ring=[],points=circuit.waypoints||[];
    for(var i=0;i<points.length;i++){
      var node=faunaById[points[i].islandId];
      if(!node||!node.island)return null;
      ring.push({x:Number(node.island.x)+Number(points[i].lx),
                 y:Number(node.island.y)+Number(points[i].ly),
                 z:Number(node.island.z)+Number(points[i].lz)});
    }
    return ring.length>=3?ring:null;
  }
  function noteWhale(g){
    var w=(g&&g.skyWhale)||null;
    var live=!!(w&&w.present===true&&w.enabled===true&&(w.regions||[]).length
                &&g.reporting===true&&g.stale!==true);
    whaleStat=live?w:null;
    if(!live){whaleAnchor=null;whaleRoster=[];return;}

    // Carried on OUR monotonic clock rather than the wall clock, and
    // re-anchored only on a real jump - the same rule and the same two cases
    // (a restarted game server, a suspended tab) as the fauna clock above.
    var now=faunaNow(),reported=Number(w.clockSeconds)||0;
    var predicted=whaleAnchor?whaleAnchor.clock+(now-whaleAnchor.perf)/1000:null;
    if(predicted===null||Math.abs(predicted-reported)>2)
      whaleAnchor={clock:reported,perf:now};

    var byRegion={};
    whaleCircuits().forEach(function(c){byRegion[c.regionId]=c;});
    whaleRoster=[];
    (w.regions||[]).forEach(function(row){
      var c=byRegion[row.regionId];if(!c)return;
      var ring=whaleRing(c);if(!ring)return;
      whaleRoster.push({regionId:row.regionId,ring:ring,
        circuitSeconds:Number(c.circuitSeconds)||0,
        phaseFraction:Number(c.phaseFraction)||0,
        lengthMetres:Number(c.lengthMetres)||0,
        callIndex:Number(row.callIndex)||0,
        callX:Number(row.callX)||0,callY:Number(row.callY)||0,callZ:Number(row.callZ)||0});
    });
  }
  function whaleElapsed(){
    return whaleAnchor?whaleAnchor.clock+(faunaNow()-whaleAnchor.perf)/1000:0;
  }
  function whaleVisible(){
    var box=$('mapFauna');
    return !!(whaleRoster.length&&box&&box.checked);
  }
  function whaleNode(pool,index,cls,make){
    var n=pool[index];
    if(!n){n=pool[index]={el:make()};n.el.setAttribute('class',cls);
      $('mapWhaleLayer').appendChild(n.el);}
    return n;
  }
  // The circuit itself, drawn once per roster rather than once per frame: it is
  // static geometry and repainting a 12-waypoint polyline sixty times a second
  // would cost more than the animal on it.
  function paintWhalePaths(){
    var key=whaleRoster.map(function(r){return r.regionId;}).join('|');
    if(key===whalePathKey)return;
    whalePathKey=key;
    var i;
    for(i=0;i<whaleRoster.length;i++){
      var r=whaleRoster[i];
      var n=whaleNode(whalePathPool,i,'whale-path',function(){return svgEl('path',{});});
      var d='',steps=r.ring.length*16;
      for(var s=0;s<=steps;s++){
        var p=WHALE.positionAt(r.ring,s/steps);
        d+=(s?'L':'M')+p.x.toFixed(1)+' '+(-p.z).toFixed(1)+' ';
      }
      n.el.setAttribute('d',d);
      n.el.style.display='';
    }
    for(;i<whalePathPool.length;i++)whalePathPool[i].el.style.display='none';
  }
  function renderWhaleFrame(){
    var layer=$('mapWhaleLayer');if(!layer)return;
    if(!whaleVisible()){
      if(layer.style.display!=='none')layer.style.display='none';
      return;
    }
    if(layer.style.display==='none')layer.style.display='';
    paintWhalePaths();
    var t=whaleElapsed(),i;
    for(i=0;i<whaleRoster.length;i++){
      var r=whaleRoster[i];
      var a=WHALE.positionAt(r.ring,WHALE.lapAt(r,t));
      var b=WHALE.positionAt(r.ring,WHALE.lapAt(r,t+WHALE_HEADING_DT));
      // Screen space: x is world east, y is world NORTH NEGATED, as everything
      // else on this map is. The glyph's nose is at -y.
      var sx=b.x-a.x,sy=-(b.z-a.z);
      var deg=(sx*sx+sy*sy)>1e-9?Math.atan2(sx,-sy)*180/Math.PI:0;
      // Sized so a 173 m animal never outshouts the island it is passing, and
      // never shrinks below a mark you can find at whole-world zoom.
      var size=Math.round(Math.max(11,Math.min(26,172.88/mapPx))*2)/2;
      var g=whaleNode(whalePool,i,'whale',function(){
        var el=svgEl('g',{});el.appendChild(svgEl('use',{href:'#whaleSymbol'}));return el;});
      var use=g.el.firstChild;
      use.setAttribute('x',-size/2);use.setAttribute('y',-size/2);
      use.setAttribute('width',size);use.setAttribute('height',size);
      g.el.setAttribute('transform','translate('+a.x.toFixed(2)+' '+(-a.z).toFixed(2)
        +') scale('+mapPx+') rotate('+deg.toFixed(1)+')');
      g.el.style.display='';

      // WHERE THE LAST CALL CAME FROM. Not derived here: the station rides the
      // live feed, because it is a discrete event pinned to one place for two
      // minutes and a second derivation could disagree with the wire.
      var c=whaleNode(whaleCallPool,i,'whale-call',function(){return svgEl('circle',{});});
      c.el.setAttribute('cx',r.callX.toFixed(2));
      c.el.setAttribute('cy',(-r.callZ).toFixed(2));
      c.el.setAttribute('r',(((worldMap.whaleModel||{}).callRadiusMetres)||0).toFixed(0));
      c.el.style.display='';
    }
    for(;i<whalePool.length;i++){
      whalePool[i].el.style.display='none';
      if(whaleCallPool[i])whaleCallPool[i].el.style.display='none';
    }
  }
  function whaleNoteText(){
    if(!whaleStat){
      var w=(latestGame&&latestGame.skyWhale)||null;
      if(w&&w.present===true&&w.enabled!==true)
        return 'Sky whale: the game server has the feature and it is switched OFF, so none is drawn.';
      if(w&&w.present===true)
        return 'Sky whale: switched on, but the server reports no whale - a region needs at least '
          +(((worldMap.whaleModel||{}).minimumIslandsPerRegion)||3)+' islands to carry one.';
      return 'Sky whale: this game server predates the feature and reports no whale section, so none is drawn.';
    }
    var M=worldMap.whaleModel||{};
    var laps=whaleRoster.map(function(r){return r.circuitSeconds;});
    var lo=Math.min.apply(null,laps),hi=Math.max.apply(null,laps);
    return 'Sky whale (live): '+plural(whaleRoster.length,'whale','whales')+', one per region, each on a '
      +'closed circuit through every island of its own region - '
      +(lo===hi?fmtShort(lo):(fmtShort(lo)+' to '+fmtShort(hi)))+' a lap at '
      +(Number(M.metresPerSecond)||0)+' m/s average, '+(Number(M.altitudeAboveIslandMetres)||0)
      +' m above each island it crosses. It calls every '+fmtShort(Number(M.callIntervalSeconds)||0)
      +'; the ring marks where the current call is sounding from, and it is '
      +fmt(Number(M.callRadiusMetres)||0)+' m across against a '+fmt(Number(M.loadRadiusMetres)||0)
      +' m radius at which the animal itself becomes visible - which is why you hear it before you see it. '
      +'The animal is a RECOVERED prefab (172.88 m long, one required component); its path, speed, '
      +'altitude and call cadence are WAREBORN TUNING - Worlds Adrift cut the whale and shipped no '
      +'behaviour for it at all.';
  }
