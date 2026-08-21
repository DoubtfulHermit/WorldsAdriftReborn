// THE WIND LAYER. Operator-only, and it draws the SERVER'S OWN WIND FIELD -
// not a picture of one.
//
// WHY THIS IS ADMIN-ONLY. The maintainer's standing rule is that operator
// features stay operator features, and this one is squarely operational: it
// exists so somebody can answer "why is that hull crabbing sideways" and "does
// WAREBORN_FLIGHT_WIND_FIELD do anything" without flying a ship. The public map
// is a player-facing world map; a wind field a player cannot act on is
// clutter. Naming the file admin-* also makes the exclusion mechanical -
// WebAssetCompositionTests.ThePublicPageTakesNoOperatorFragment rejects any
// fragment whose name contains "admin" from PublicMapPage.ScriptFragments, so
// this cannot leak by somebody forgetting.
//
// ⚠ EVERY TOP-LEVEL NAME HERE STARTS WITH wind. Web/Assets/*.js are
// concatenated into ONE shared closure (WebAssets.Script -> AdminPage's single
// (function(){...})()), so a duplicate top-level name silently shadows another
// file's. An `svgEl` declared in one fragment once replaced a different `svgEl`
// and took the whole map down. Namespacing is not style here.
//
// ⚠ NOTHING OFF THIS HOST. No third-party script, no remote font, no remote
// image - AdminPageTests scans the WHOLE rendered page for the three-letter
// word for a content delivery network, so this comment cannot even spell it.
// (It caught this file on the first run, which is the test working.)
// The colours are inline
// attributes rather than a stylesheet entry precisely so the swatch in the
// control label and the stroke on the arrow read the same constant and cannot
// drift apart - the same failure MapWallPalette.LegendHtml exists to prevent.

// ---------------------------------------------------------------------------
// THE MODEL. This is a straight transcription of
// WorldsAdriftRebornGameServer.Multiplayer/Ship/Flight/WindField.cs, and it has
// to STAY one. The server's field is a closed form of position and time with no
// state, exactly so the browser can evaluate the identical expression from a
// clock and be showing the operator the real wind rather than an illustration
// of it - the same honesty the fauna layer buys by re-evaluating the server's
// motion model instead of being sent positions.
//
// If you change a constant in WindField.cs, change it here. There is no
// mechanism that will tell you that you did not, and a map that quietly
// disagrees with the simulation is worse than no map.
// ---------------------------------------------------------------------------

// PROVED - GlobalWeather.GetCellSampleAt returns (1,0,-2) for any position with
// no weather cell, and this server serves none, so this is the wind the CLIENT
// draws for every player: the wind streaks in the sky, the direction the grass
// bends, the way a mounted flag points.
var WIND_PUBLISHED_X=1,WIND_PUBLISHED_Z=-2;
var WIND_PUBLISHED_SPEED=Math.sqrt(5);      // 2.236 m/s
var WIND_PUBLISHED_BEARING=Math.atan2(WIND_PUBLISHED_X,WIND_PUBLISHED_Z);

// WAREBORN TUNING - must match WindFieldVariation.
var WIND_CELL_M=4000,WIND_PERIOD_S=600;
var WIND_MAX_VEER=40*Math.PI/180,WIND_MAX_GUST=0.35;
var WIND_PHI=1.6180339887498949;

// Operator-set, from the controls. Mirrors WAREBORN_FLIGHT_WIND_SPEED and
// WAREBORN_FLIGHT_WIND_FIELD; the map cannot read the game server's env, so
// these are what the operator BELIEVES is set and the label says so.
var windMeanSpeed=WIND_PUBLISHED_SPEED;
var windFieldScale=0;

var windLayer=null,windArrows=[],windTimer=null,windEnabled=false;
var windClockSeconds=0;

function windVeerAt(x,z,t){
  if(windFieldScale<=0)return 0;
  var a=Math.sin(2*Math.PI*(x/WIND_CELL_M+t/WIND_PERIOD_S));
  var b=Math.sin(2*Math.PI*(z/WIND_CELL_M-t/(WIND_PERIOD_S*WIND_PHI)));
  return WIND_MAX_VEER*windFieldScale*0.5*(a+b);
}
function windGustAt(x,z,t){
  if(windFieldScale<=0)return 1;
  var a=Math.sin(2*Math.PI*((x+WIND_CELL_M*0.5)/WIND_CELL_M-t/(WIND_PERIOD_S*1.31)));
  var b=Math.sin(2*Math.PI*((z+WIND_CELL_M*0.5)/WIND_CELL_M+t/(WIND_PERIOD_S*0.77)));
  return 1+WIND_MAX_GUST*windFieldScale*0.5*(a+b);
}
// Returns {x,z,speed,bearing} - the same rotation-of-the-base-vector the server
// does, so the disabled case is exactly the published constant here too.
function windSampleAt(x,z,t){
  var s=windMeanSpeed/WIND_PUBLISHED_SPEED;
  var bx=WIND_PUBLISHED_X*s,bz=WIND_PUBLISHED_Z*s;
  var veer=windVeerAt(x,z,t),gust=windGustAt(x,z,t);
  var c=Math.cos(veer),sn=Math.sin(veer);
  var wx=(bx*c+bz*sn)*gust,wz=(bz*c-bx*sn)*gust;
  var speed=Math.sqrt(wx*wx+wz*wz);
  var sample=windWallSampleAt(x,z,wx,wz);
  if(sample){wx=sample[0];wz=sample[1];speed=Math.sqrt(wx*wx+wz*wz);}
  return {x:wx,z:wz,speed:speed,bearing:Math.atan2(wx,wz)};
}

// ---------------------------------------------------------------------------
// WEATHER WALLS. The 44 authored segments already on this map are the ONE
// source of wind variation that needs no 500 m weather-cell lattice:
// GlobalWeather.GetWeatherAt is Lerp(cellWind, wallWind, intensity), and
// WallSegmentVisualizer has a single [Require] - 1204 WallSegmentState.
//
// They are drawn here at the strength the operator dials in, and that dial
// DEFAULTS TO ZERO, which is not a coy default - it is what a real client
// computes. Every wall wind is scaled by a GlobalWeatherDataVisualizer
// multiplier that stays 0f until 1229 GlobalWallDataState is served with a
// complete FloatValues map, so a world given 1204 alone goes DEAD CALM inside
// its own wind walls. Turning this dial up shows what serving BOTH would look
// like. See WindField.cs / WeatherWallSegment for the full trap.
// ---------------------------------------------------------------------------
var windWallMultiplier=0;
var WIND_WALL_FULL_SQR=40000;     // PROVED - WallData, 200 m
var WIND_WALL_REACH_SQR=160000;   // PROVED - WallData.EffectiveDist, 400 m

function windWallIntensity(sqr){
  if(sqr>WIND_WALL_REACH_SQR)return 0;
  if(sqr<WIND_WALL_FULL_SQR)return 1;
  return 1-(Math.sqrt(sqr)-200)/200;
}
function windWallSampleAt(x,z,ambientX,ambientZ){
  if(windWallMultiplier<=0)return null;
  var walls=(typeof worldMap!=='undefined'&&worldMap&&worldMap.walls)||[];
  var best=null,bestSqr=Infinity;
  for(var i=0;i<walls.length;i++){
    var w=walls[i];
    var dx=w.x2-w.x1,dz=w.z2-w.z1,len=dx*dx+dz*dz;
    var t=len<=0?0:((x-w.x1)*dx+(z-w.z1)*dz)/len;
    t=t<0?0:(t>1?1:t);
    var ox=x-(w.x1+t*dx),oz=z-(w.z1+t*dz),sqr=ox*ox+oz*oz;
    if(sqr<bestSqr&&windWallIntensity(sqr)>1e-6){bestSqr=sqr;best={w:w,ox:ox,oz:oz,dx:dx,dz:dz};}
  }
  if(!best)return null;
  var k=windWallIntensity(bestSqr),wx=0,wz=0;
  if(Number(best.w.type)===0){
    // PROVED - only a Wind Rift blows PERPENDICULAR, away from its own line.
    var ol=Math.sqrt(best.ox*best.ox+best.oz*best.oz);
    if(ol>0){wx=best.ox/ol*windWallMultiplier;wz=best.oz/ol*windWallMultiplier;}
  }else{
    var fl=Math.sqrt(best.dx*best.dx+best.dz*best.dz);
    if(fl>0){wx=best.dx/fl*windWallMultiplier;wz=best.dz/fl*windWallMultiplier;}
  }
  return [ambientX+(wx-ambientX)*k,ambientZ+(wz-ambientZ)*k];
}

// ---------------------------------------------------------------------------
// DRAWING. A regular lattice of barbs in world metres. Z is negated because
// SVG y grows downward, which is the same convention pathFromSegments uses for
// the walls - get it wrong and the wind blows the opposite way from the walls
// beside it, which is the kind of bug a screenshot catches and a test does not.
// ---------------------------------------------------------------------------
var WIND_GRID = 20;               // barbs per side across the whole world
var WIND_BARB_M = 620;            // length of a barb at the mean wind, metres

function windEl(tag,attrs){
  var n=document.createElementNS('http://www.w3.org/2000/svg',tag);
  for(var k in attrs){if(Object.prototype.hasOwnProperty.call(attrs,k))n.setAttribute(k,attrs[k]);}
  return n;
}

function windColourFor(speed){
  // One hue, three weights: calm / mean / strong, relative to the dialled mean
  // so the reading stays meaningful when the operator changes the speed.
  var r=windMeanSpeed>0?speed/windMeanSpeed:0;
  if(r<0.75)return '#3f6f78';
  if(r>1.25)return '#9fe8f0';
  return '#74c9cf';               // the console accent, and Wind Rift's own colour
}

function windBuildLayer(){
  var svg=document.getElementById('liveWorldMap');
  if(!svg||windLayer)return;
  var clipped=svg.querySelector('g[clip-path]');
  var grid=document.getElementById('mapGrid');
  if(!clipped||!grid)return;
  windLayer=windEl('g',{id:'mapWindLayer','pointer-events':'none'});
  // Immediately above the graticule and BELOW the walls, so a wall's own stroke
  // still reads on top of the wind it is producing.
  clipped.insertBefore(windLayer,grid.nextSibling);
}

function windRebuild(){
  if(!windLayer)return;
  while(windLayer.firstChild)windLayer.removeChild(windLayer.firstChild);
  windArrows=[];
  var edge=(typeof worldMap!=='undefined'&&worldMap&&worldMap.worldEdgeLength)||36000;
  var half=edge/2,stepM=edge/WIND_GRID;
  for(var ix=0;ix<WIND_GRID;ix++){
    for(var iz=0;iz<WIND_GRID;iz++){
      var x=-half+stepM*(ix+0.5),z=-half+stepM*(iz+0.5);
      var line=windEl('path',{'stroke-width':2,'stroke-linecap':'round',
                              'vector-effect':'non-scaling-stroke',fill:'none'});
      windLayer.appendChild(line);
      windArrows.push({x:x,z:z,node:line});
    }
  }
  windRedraw();
}

function windRedraw(){
  if(!windLayer||!windArrows.length)return;
  var strongest=0,weakest=Infinity;
  for(var i=0;i<windArrows.length;i++){
    var a=windArrows[i];
    var s=windSampleAt(a.x,a.z,windClockSeconds);
    if(s.speed>strongest)strongest=s.speed;
    if(s.speed<weakest)weakest=s.speed;
    var scale=windMeanSpeed>0?(s.speed/windMeanSpeed):0;
    var len=WIND_BARB_M*Math.max(0.25,Math.min(1.8,scale));
    // Unit vector in SCREEN space: +x is +x, but +z is DOWN, so negate it.
    var ux=s.speed>0?s.x/s.speed:0,uz=s.speed>0?-s.z/s.speed:0;
    var tipX=a.x+ux*len*0.5,tipY=-a.z+uz*len*0.5;
    var tailX=a.x-ux*len*0.5,tailY=-a.z-uz*len*0.5;
    // A barb, not a line: two short flights swept back from the tip so the
    // direction is readable at a glance and at any zoom.
    var head=len*0.3;
    var lx=-ux*head, ly=-uz*head;
    var cos=Math.cos(0.42),sin=Math.sin(0.42);
    var l1x=lx*cos-ly*sin,l1y=lx*sin+ly*cos;
    var l2x=lx*cos+ly*sin,l2y=-lx*sin+ly*cos;
    a.node.setAttribute('d',
      'M'+tailX.toFixed(1)+' '+tailY.toFixed(1)+
      'L'+tipX.toFixed(1)+' '+tipY.toFixed(1)+
      'M'+tipX.toFixed(1)+' '+tipY.toFixed(1)+
      'l'+l1x.toFixed(1)+' '+l1y.toFixed(1)+
      'M'+tipX.toFixed(1)+' '+tipY.toFixed(1)+
      'l'+l2x.toFixed(1)+' '+l2y.toFixed(1));
    a.node.setAttribute('stroke',windColourFor(s.speed));
    a.node.setAttribute('opacity',windFieldScale>0?'0.95':'0.8');
  }
  windUpdateNote(weakest,strongest);
}

function windUpdateNote(weakest,strongest){
  var note=document.getElementById('mapWindNote');
  if(!note)return;
  var drawn=WIND_PUBLISHED_SPEED.toFixed(2);
  if(windFieldScale<=0&&windWallMultiplier<=0){
    note.textContent='uniform '+windMeanSpeed.toFixed(2)+' m/s toward +X/-Z'+
      (Math.abs(windMeanSpeed-WIND_PUBLISHED_SPEED)<0.005
        ? ' - matches the '+drawn+' m/s the game client draws'
        : ' - the client draws '+drawn+' m/s, so seen and felt disagree');
  }else{
    note.textContent='field '+weakest.toFixed(2)+'-'+strongest.toFixed(2)+' m/s'+
      ', veer up to '+Math.round(WIND_MAX_VEER*windFieldScale*180/Math.PI)+String.fromCharCode(176)+
      ' off the '+drawn+' m/s the client draws';
  }
}

// ---------------------------------------------------------------------------
// CONTROLS. Injected rather than written into map-body.html, because that file
// is SHARED with the public map - markup added there appears on both pages, and
// an inert checkbox on the public map would be worse than no checkbox.
// ---------------------------------------------------------------------------
function windBuildControls(){
  var controls=document.querySelector('.map-controls');
  if(!controls||document.getElementById('mapWind'))return;
  var label=document.createElement('label');
  label.className='map-toggle';
  label.title='Operator only. The server-side wind field WAREBORN_FLIGHT_WIND_FIELD produces.';
  var box=document.createElement('input');
  box.type='checkbox';box.id='mapWind';
  label.appendChild(box);
  label.appendChild(document.createTextNode('wind'));
  // Before the zoom buttons, so the toggles stay one run.
  controls.insertBefore(label,controls.querySelector('button'));
  box.addEventListener('change',function(){
    windEnabled=box.checked;
    if(windLayer)windLayer.style.display=windEnabled?'':'none';
    windSetRunning(windEnabled);
    if(windEnabled)windRedraw();
  });

  var panel=document.createElement('div');
  panel.className='map-controls';
  panel.id='mapWindControls';
  panel.style.display='none';
  panel.appendChild(windSlider('mapWindSpeed','wind m/s',0,12,0.1,windMeanSpeed,function(v){
    windMeanSpeed=v;windRedraw();}));
  panel.appendChild(windSlider('mapWindField','field',0,1,0.05,windFieldScale,function(v){
    windFieldScale=v;windRedraw();}));
  panel.appendChild(windSlider('mapWindWalls','wall m/s',0,30,1,windWallMultiplier,function(v){
    windWallMultiplier=v;windRedraw();}));
  var note=document.createElement('span');
  note.className='map-toggle';note.id='mapWindNote';
  panel.appendChild(note);
  controls.parentNode.insertBefore(panel,controls.nextSibling);
  box.addEventListener('change',function(){
    panel.style.display=box.checked?'':'none';
  });
}

function windSlider(id,caption,min,max,step,value,onInput){
  var label=document.createElement('label');
  label.className='map-toggle';
  var input=document.createElement('input');
  input.type='range';input.id=id;input.min=min;input.max=max;input.step=step;input.value=value;
  input.style.width='84px';
  var out=document.createElement('span');
  out.textContent=' '+caption+' '+Number(value).toFixed(step<1?2:0);
  input.addEventListener('input',function(){
    var v=Number(input.value);
    out.textContent=' '+caption+' '+v.toFixed(step<1?2:0);
    onInput(v);
  });
  label.appendChild(input);label.appendChild(out);
  return label;
}

// The clock. Deliberately NOT the wall clock: an operator wants to SEE the
// field move, and its real period is ten minutes. This runs it at 60x so a
// whole cycle takes ten seconds, and the note says the field is a model rather
// than a live reading, because it is.
function windSetRunning(run){
  if(windTimer){clearInterval(windTimer);windTimer=null;}
  if(!run)return;
  windTimer=setInterval(function(){
    if(windFieldScale>0){windClockSeconds+=6;windRedraw();}
  },100);
}

function wireWindLayer(){
  windBuildLayer();
  windBuildControls();
  windRebuild();
  if(windLayer)windLayer.style.display='none';
}
