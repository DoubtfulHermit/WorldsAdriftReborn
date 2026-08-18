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
    // ---- the family (Phase 5): a calf takes its mother's place, shifted -----
    // WHICH slots are calves and WHICH adult each trails is seed-derived, so it
    // arrives per group in the LIVE feed as g.calves = [{member,mother}] and is
    // never re-derived here. Only the two lengths and the geometry are restated,
    // and the parity test holds them to a nanometre. No feed, or juveniles off,
    // means no calves array and every member takes its own offset - which is why
    // flag-off draws exactly what it drew before.
    function motherOf(g,memberIndex){
      var c=g&&g.calves;
      if(!c)return -1;
      for(var i=0;i<c.length;i++){if(c[i].member===memberIndex)return c[i].mother;}
      return -1;
    }
    // Behind and below, in the CLUSTER's own rotating frame - the unit tangent at
    // the mother's angle a is (-sin a, 0, cos a), so trailing is its negative.
    // The displacement does not scale with the mother's radius: every calf sits
    // exactly the recovered pair standoff from its mother.
    function calfOffset(motherIndex,radius,verticalRadius,t){
      var o=memberOffset(motherIndex,radius,verticalRadius,t);
      var a=motherIndex*M.goldenAngleRadians+t*M.weaveRadiansPerSecond;
      return {x:o.x+M.calfTrailMetres*Math.sin(a),
              y:o.y-M.calfDropMetres,
              z:o.z-M.calfTrailMetres*Math.cos(a)};
    }
    function familyOffset(memberIndex,radius,verticalRadius,t,g){
      var mother=motherOf(g,memberIndex);
      return mother<0?memberOffset(memberIndex,radius,verticalRadius,t)
                     :calfOffset(mother,radius,verticalRadius,t);
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
      var o=familyOffset(memberIndex,k.r,k.v,t,g);
      return {x:c.x+o.x,y:c.y+o.y,z:c.z+o.z};
    }
    return {localPose:localPose,schoolCentre:schoolCentre,cluster:cluster,
            dayness:dayness,cycleFraction:cycleFraction,motherOf:motherOf};
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
            toBloom:Number(g.toBloom)||0,
            // The family pairing, handed to the mirror as-is. Absent on an
            // older game server and on one with juveniles off, and in both
            // cases the mirror falls back to every member's own offset.
            calves:(g.calves||[]).map(function(c){
              return {member:Number(c.member),mother:Number(c.mother)};
            })});
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


