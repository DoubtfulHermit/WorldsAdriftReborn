  // ---- the viewer sparkline (shared) ---------------------------------------
  // Drawing only. Both the public map's About panel and the operator console's
  // audience card render the same series here, for the same reason the map
  // itself is one renderer with two projections: a second copy would drift, and
  // the two pages differ in how MUCH history they ask for, not in how a line is
  // drawn.
  //
  // Nothing in this file fetches anything or knows what a viewer is. It takes an
  // array of numbers.

  function svgEl(tag){return document.createElementNS('http://www.w3.org/2000/svg',tag);}

  // An area under a line, no axes and no grid: the point is the shape, and the
  // numbers worth reading are printed beneath it by the caller. The viewBox is
  // one unit per bucket with preserveAspectRatio off, so the same series draws
  // correctly at any width without recomputing anything.
  function viewerSparkline(points,peak,label){
    var w=points.length,h=32,top=peak>0?peak:1,i,y;
    var svg=svgEl('svg');
    svg.setAttribute('viewBox','0 0 '+(w>1?w-1:1)+' '+h);
    svg.setAttribute('preserveAspectRatio','none');
    svg.setAttribute('class','spark-svg');
    svg.setAttribute('role','img');
    svg.setAttribute('aria-label',label||('Viewers over time, peaking at '+peak+'.'));

    var line='',area='M0,'+h;
    for(i=0;i<w;i++){
      y=h-(Number(points[i])||0)/top*(h-2);
      line+=(i?' L':'M')+i+','+y.toFixed(2);
      area+=' L'+i+','+y.toFixed(2);
    }
    area+=' L'+(w>1?w-1:0)+','+h+' Z';

    var fill=svgEl('path');
    fill.setAttribute('d',area);
    fill.setAttribute('class','spark-area');
    svg.appendChild(fill);

    var stroke=svgEl('path');
    stroke.setAttribute('d',line);
    stroke.setAttribute('class','spark-line');
    svg.appendChild(stroke);
    return svg;
  }

  // The row of numbers under a sparkline: pairs of [value, label]. A div rather
  // than a paragraph because the About panel styles every <p> inside it for
  // prose width and prose margins, which is the wrong shape for this.
  function sparkFoot(pairs){
    var foot=el('div','spark-foot'),i;
    for(i=0;i<pairs.length;i++){
      foot.appendChild(el('strong','',fmt(pairs[i][0])));
      foot.appendChild(el('span','',pairs[i][1]));
    }
    return foot;
  }
