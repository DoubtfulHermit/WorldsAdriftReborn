  // ---- the map's audience (operator view) ----------------------------------
  // The same aggregate rows the public map draws, over a longer window: a month
  // of hourly buckets and the all-time peak, against the public page's day.
  //
  // Worth being explicit about what this login buys, because "the console can
  // see more" is normally how a privacy boundary erodes: it buys LENGTH, not
  // RESOLUTION. There is no per-viewer detail on this page because there is none
  // in the table - see AdminViewerReport and docs/public-map-viewer-count.md.
  //
  // Note also what this page does NOT do: it does not mint a viewer token and
  // does not poll /map/data, so an operator watching the console is not counted
  // as somebody watching the map.

  var ADMIN_VIEWERS_MS=60000;   // the rows only change once a minute

  function adminViewersRender(d){
    text('viewersNow',fmt(Number(d.now)||0));
    text('viewersPeak30',fmt(Number(d.peak)||0));
    text('viewersPeakAll',fmt(Number(d.peakAllTime)||0));
    text('viewersMinutes',fmt(Number(d.recordedMinutes)||0));

    var pill=$('viewerRecording');
    if(pill){
      pill.textContent=d.recording?'recording':'history unavailable';
      pill.className='pill'+(d.recording?' ok':'');
    }

    var box=$('adminViewerSpark');
    if(!box)return;
    clear(box);
    if(!d.points||!d.points.length){
      box.appendChild(el('div','spark-empty','No history recorded yet.'));
      return;
    }
    var peak=Math.max(Number(d.peak)||0,Number(d.now)||0);
    box.appendChild(viewerSparkline(d.points,peak,
      'Map viewers over the last 30 days, peaking at '+peak+'.'));
    box.appendChild(sparkFoot([
      [Number(d.now)||0,' now'],
      [peak,' peak, 30 days'],
      [Number(d.peakAllTime)||0,' peak, all time']]));
  }

  function adminViewersFetch(){
    fetch('/admin/api/viewers',{headers:{'Accept':'application/json'}})
      .then(function(r){return r.ok?r.json():null;})
      .then(function(d){if(d)adminViewersRender(d);})
      .catch(function(){});
  }

  adminViewersFetch();
  setInterval(adminViewersFetch,ADMIN_VIEWERS_MS);
