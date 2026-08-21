  // ---- application shell -------------------------------------------------
  // The admin is one authenticated document for a deliberate reason: the map,
  // domain inspector and guarded controls share a live snapshot and selection.
  // It is presented as routed workspaces, however, so an operator works in one
  // context at a time instead of scrolling through the entire server surface.
  var ADMIN_ROUTES={
    overview:{group:'overview',eyebrow:'Administration',title:'Overview',description:'Players, health and current server activity.'},
    world:{group:'observatory',eyebrow:'World inspector',title:'World',description:'Live geography, ships, players, wildlife and release-world evidence.'},
    simulation:{group:'observatory',eyebrow:'World inspector',title:'Simulation',description:'Authoritative domains, ownership, replication and observed interactions.'},
    infrastructure:{group:'observatory',eyebrow:'World inspector',title:'Infrastructure',description:'The real process and authority host running this world.'},
    streaming:{group:'streaming',eyebrow:'Runtime',title:'Streaming',description:'Per-peer resource, wildlife, ship and terrain interest.'},
    terrain:{group:'terrain',eyebrow:'Runtime',title:'Terrain checkout',description:'Detailed loading, readiness, acknowledgements, draining and unload state.'},
    operator:{group:'operator',eyebrow:'Live controls',title:'World controls',description:'Review and dispatch guarded travel, summon and positioning commands.'},
    operations:{group:'operations',eyebrow:'Live controls',title:'Recovery',description:'Targeted ship and player intervention with completion receipts.'},
    system:{group:'system',eyebrow:'Configuration',title:'System',description:'Accounts, server identity, welcome message and public patch notes.'}
  };
  var activeAdminRoute='overview';

  function normaliseAdminRoute(value){
    var route=String(value||'').replace(/^#/,'').toLowerCase();
    if(route==='observatory')route='world';
    return ADMIN_ROUTES[route]?route:'overview';
  }
  function closeAdminNavigation(){
    document.body.classList.remove('nav-open');
    var toggle=$('navToggle');if(toggle)toggle.setAttribute('aria-expanded','false');
  }
  function renderAdminRoute(route,focusContent){
    route=normaliseAdminRoute(route);activeAdminRoute=route;
    var meta=ADMIN_ROUTES[route];
    Array.prototype.forEach.call(document.querySelectorAll('[data-admin-page]'),function(page){
      page.hidden=page.dataset.adminPage!==meta.group;
    });
    Array.prototype.forEach.call(document.querySelectorAll('[data-admin-route]'),function(link){
      var active=link.dataset.adminRoute===route;
      if(active)link.setAttribute('aria-current','page');else link.removeAttribute('aria-current');
    });
    text('pageEyebrow',meta.eyebrow);text('pageTitle',meta.title);text('pageDescription',meta.description);
    document.title=meta.title+' - Wareborn administration';
    if(meta.group==='observatory')setObservatoryMode(route,false);
    closeAdminNavigation();
    window.scrollTo(0,0);
    if(focusContent===true){var host=$('adminPageHost');if(host)host.focus({preventScroll:true});}
  }
  function navigateAdmin(route,replace,focusContent){
    route=normaliseAdminRoute(route);
    var hash='#'+route;
    if(replace===true)history.replaceState(null,'',hash);
    else if(location.hash!==hash)history.pushState(null,'',hash);
    renderAdminRoute(route,focusContent===true);
  }
  function wireAdminNavigation(){
    Array.prototype.forEach.call(document.querySelectorAll('[data-admin-route]'),function(link){
      link.addEventListener('click',function(e){e.preventDefault();navigateAdmin(link.dataset.adminRoute,false,true);});
    });
    var toggle=$('navToggle');if(toggle)toggle.addEventListener('click',function(){
      var open=!document.body.classList.contains('nav-open');document.body.classList.toggle('nav-open',open);toggle.setAttribute('aria-expanded',open?'true':'false');
    });
    var scrim=$('sidebarScrim');if(scrim)scrim.addEventListener('click',closeAdminNavigation);
    window.addEventListener('popstate',function(){renderAdminRoute(location.hash,false);});
    document.addEventListener('keydown',function(e){if(e.key==='Escape')closeAdminNavigation();});
    navigateAdmin(location.hash,true,false);
  }
  function updateAdminShell(reporting,game){
    var fresh=reporting===true&&game&&game.stale!==true;
    text('sidebarStatus',fresh?'Game server online':(reporting?'Snapshot stale':'Game server unavailable'));
    text('sidebarBuild',fresh?('Build '+(game.build||'unknown')):'Live telemetry unavailable');
    var dot=$('sidebarStatusDot');if(dot)dot.className='status-dot '+(fresh?'ok':(reporting?'warn':'bad'));
  }
