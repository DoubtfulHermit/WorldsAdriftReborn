  // ---- how many people are looking at this ---------------------------------
  // The map's own audience: a chip in the strip, and a day of history behind
  // the About button. The line itself is drawn by the shared map-viewers.js.
  //
  // WHAT THIS BROWSER TELLS THE SERVER, in full: one random number, minted here,
  // once, for this page load. That is the entire contribution. There is no
  // cookie, nothing in localStorage or sessionStorage, no fingerprinting, no
  // analytics beacon and no third party - reload the page and you are a new
  // viewer, because a value that survived a reload would be a tracking id and
  // this deliberately is not one. Two tabs are two viewers for the same reason:
  // making them one would mean recognising you across tabs, which is precisely
  // what we are avoiding.
  //
  // The consequence, stated so nobody has to guess: the number counts TABS with
  // the map open, not people, and a closed tab fades out of it after about half
  // a minute rather than vanishing.

  var VIEWER_TREND_MS=300000;   // the history only moves once a minute; five is plenty
  var viewerTrend=null;

  // 128 bits of randomness as hex. crypto.getRandomValues wherever it exists;
  // the Math.random fallback is for old browsers and is fine, because nothing
  // here is a secret - a colliding token would undercount by one, which is the
  // worst outcome available.
  var VIEWER_TOKEN=(function(){
    var bytes=new Array(16),i;
    var c=window.crypto||window.msCrypto;
    if(c&&c.getRandomValues){
      var buf=new Uint8Array(16);
      c.getRandomValues(buf);
      for(i=0;i<16;i++)bytes[i]=buf[i];
    }else{
      for(i=0;i<16;i++)bytes[i]=Math.floor(Math.random()*256);
    }
    var out='';
    for(i=0;i<16;i++)out+=('0'+bytes[i].toString(16)).slice(-2);
    return out;
  })();

  // Appended to the live poll so the server can tell one tab from another
  // without knowing anything about either. The server refuses anything that is
  // not plain letters and digits, which this always is.
  function viewerQuery(){return '?v='+VIEWER_TOKEN;}

  // ---- the chip ------------------------------------------------------------

  // Never below one. You are reading the page, so you are watching it: the
  // server's number is a poll behind on a fresh load, and "0 watching" on a page
  // somebody is looking at is simply wrong.
  function viewerCount(){
    var n=Number(latestGame&&latestGame.viewers)||0;
    return n<1?1:n;
  }

  // One word, whatever the number: "1 watching" and "4 watching" both read, and
  // the strip beside it is no place for a plural rule.
  function viewerChip(){return chip(fmt(viewerCount()),'watching');}

  // ---- the trend behind the About button -----------------------------------

  function viewerTrendFetch(){
    fetch('/map/viewers',{headers:{'Accept':'application/json'}})
      .then(function(r){return r.ok?r.json():null;})
      .then(function(d){if(d){viewerTrend=d;renderViewerTrend();}})
      .catch(function(){});
  }

  function renderViewerTrend(){
    var box=$('viewerSpark');
    if(!box)return;
    clear(box);
    if(!viewerTrend||!viewerTrend.points||!viewerTrend.points.length){
      box.appendChild(el('div','spark-empty','No history recorded yet.'));
      return;
    }
    var peak=Math.max(Number(viewerTrend.peak)||0,viewerCount());
    box.appendChild(viewerSparkline(viewerTrend.points,peak,
      'Viewers over the last 24 hours, peaking at '+peak+'.'));
    box.appendChild(sparkFoot([[viewerCount(),' now'],[peak,' peak in the last 24 hours']]));
  }

  // The "now" figure under the line moves with every poll, but redrawing a
  // hidden panel is work nobody sees, so this only repaints while the About
  // panel is actually open.
  function viewerTrendTick(){
    var panel=$('aboutPanel');
    if(panel&&!panel.hasAttribute('hidden'))renderViewerTrend();
  }

  function viewerBoot(){
    viewerTrendFetch();
    setInterval(viewerTrendFetch,VIEWER_TREND_MS);
    setInterval(viewerTrendTick,REFRESH_MS);
  }
