  // ---- the operator command panel ---------------------------------------
  // Send ANY player ANYWHERE, or summon a ship for them, against the real
  // /admin/api/operator/ endpoints. The design rule this whole panel obeys:
  // it only ever POSTS BACK the `selector` strings the targets endpoint gave
  // it, so it cannot construct an invalid target - the one exception is the
  // clearly-labelled advanced selector box, whose free text the SERVER
  // validates and refuses with a sentence this panel shows verbatim.
  var latestTargets=null;          // the last successful /targets snapshot
  var targetsRefusal=null;         // the last refusal body, when there is one
  var opInFlight=false;            // one command at a time, mirrored in the UI
  var opBridgeBusy=false;          // the trigger bridge still holds a command
  var opPending=null;              // {path,body,sentence} awaiting confirm
  var opAction='teleport';         // teleport | summon-ship
  var opDest='island';             // island | coord | player | home | spawn
  var opHullMode='owned';          // owned | exact
  function operatorHeaders(){
    return {'Accept':'application/json','X-Wareborn-Admin':'1','X-Wareborn-CSRF':CSRF};
  }
  function refreshTargets(){
    fetch('/admin/api/operator/targets',{headers:operatorHeaders(),credentials:'same-origin'})
      .then(function(r){
        if(r.status===401){location.href='/admin';return null;}
        return r.json().then(function(j){return {ok:r.ok,data:j};});
      })
      .then(function(result){
        if(!result)return;
        if(result.ok){latestTargets=result.data;targetsRefusal=null;}
        else targetsRefusal=result.data;
        renderOperatorPanel();
      })
      .catch(function(){
        targetsRefusal={code:'unreachable',
          reason:'The operator roster request could not reach the login server.'};
        renderOperatorPanel();
      });
  }
  function opPlayerLabel(p){
    var name=p.characterName?p.characterName:'(no character name)';
    return name+' · entity '+p.entityId;
  }
  function opOption(select,value,label){
    var o=document.createElement('option');o.value=value;o.textContent=label;
    select.appendChild(o);return o;
  }
  // Rebuild a <select> while keeping the operator's current choice if it still
  // exists - the same courtesy the recovery selectors extend.
  function opRebuild(select,rows,toValue,toLabel,promptLabel,emptyLabel){
    var kept=select.value;clear(select);
    opOption(select,'',rows.length?promptLabel:emptyLabel);
    rows.forEach(function(row){opOption(select,toValue(row),toLabel(row));});
    if(rows.some(function(row){return toValue(row)===kept;}))select.value=kept;
  }
  function opDurabilityPill(p){
    var pill=document.createElement('span');
    if(p.selectorIsDurable){pill.className='pill ok';pill.textContent='durable uid';}
    else{pill.className='pill warn';pill.textContent='entity-only';
      pill.title='No character uid has been published for this row yet, so commands are '
        +'addressed to the session entity id, which can go stale between render and click.';}
    return pill;
  }
  function renderOperatorRoster(){
    var body=$('opRoster');clear(body);
    var players=(latestTargets?latestTargets.players:[])||[];
    $('noOpPlayers').style.display=players.length?'none':'block';
    players.forEach(function(p){
      var tr=document.createElement('tr');
      cell(tr,p.characterName||'(no character name)',p.characterName?'':'muted');
      var idCell=cell(tr,'');idCell.appendChild(opDurabilityPill(p));
      var idText=document.createElement('div');idText.className='island-id';
      idText.textContent=p.selector;idCell.appendChild(idText);
      cell(tr,String(p.entityId),'num');
      cell(tr,p.peerId,'muted');
      cell(tr,p.hasPosition
        ?(Number(p.x).toFixed(1)+', '+Number(p.y).toFixed(1)+', '+Number(p.z).toFixed(1))
        :'no position yet',p.hasPosition?'num':'muted');
      var pick=document.createElement('td');
      var button=document.createElement('button');button.type='button';
      button.className='btn ghost';button.textContent='Use as target';
      button.addEventListener('click',function(){
        $('opTarget').value=p.selector;syncOperatorForm();
      });
      pick.appendChild(button);tr.appendChild(pick);
      body.appendChild(tr);
    });
  }
  function opIslandLabel(i){
    return i.displayName+' · T'+i.cellTier+' · '+i.cellId
      +(i.terrainKnown?' · terrain live':' · terrain not registered this boot');
  }
  function renderOperatorIslands(){
    var select=$('opIsland');
    var islands=(latestTargets?latestTargets.islands:[])||[];
    var query=($('opIslandSearch').value||'').toLowerCase().trim();
    var rows=islands.filter(function(i){
      if(!query)return true;
      return (i.displayName+' '+i.id+' '+i.cellId+' t'+i.cellTier).toLowerCase().indexOf(query)>=0;
    });
    opRebuild(select,rows,function(i){return i.selector;},opIslandLabel,
      'Select an island','No island matches that search');
    text('opIslandCount',rows.length+' of '+islands.length+' islands');
  }
  function renderOperatorPanel(){
    var players=(latestTargets?latestTargets.players:[])||[];
    var ships=(latestTargets?latestTargets.ships:[])||[];
    var banner=$('opUnavailable');
    if(targetsRefusal){
      banner.classList.add('show');
      text('opUnavailableText',(targetsRefusal.reason||'The operator roster is unavailable.')
        +(targetsRefusal.code?' (code '+targetsRefusal.code+')':''));
    }else banner.classList.remove('show');
    var freshness=$('opFreshness');
    if(latestTargets){
      var age=Math.round(Number(latestTargets.ageSeconds)||0);
      freshness.className='pill '+(latestTargets.stale?'warn':'ok');
      freshness.textContent=(latestTargets.stale?'stale · ':'live · ')+age+'s old';
    }else{freshness.className='pill warn';freshness.textContent='no roster yet';}
    renderOperatorRoster();
    opRebuild($('opTarget'),players,
      function(p){return p.selector;},opPlayerLabel,
      'Select a player','No connected player');
    opRebuild($('opDestPlayer'),players,
      function(p){return p.selector;},opPlayerLabel,
      'Select the destination player','No connected player');
    opRebuild($('opHull'),ships,
      function(s){return s.selector;},
      function(s){return 'hull '+s.hullEntityId
        +(s.ownerCharacterUid?(' · owner '+s.ownerCharacterUid.slice(0,8)+'…'):' · owner unknown')
        +(s.piloted?' · PILOTED':'');},
      'Select an exact hull','No registered ship');
    renderOperatorIslands();
    var bridge=$('opBridge');
    if(opBridgeBusy){bridge.classList.add('show');}
    else bridge.classList.remove('show');
    syncOperatorForm();
  }
  // Shows and hides the form areas that belong to the current action and
  // destination kind, and keeps the segmented controls honest.
  function syncOperatorForm(){
    Array.prototype.forEach.call(document.querySelectorAll('[data-op-action]'),function(b){
      b.classList.toggle('active',b.dataset.opAction===opAction);});
    Array.prototype.forEach.call(document.querySelectorAll('[data-op-dest]'),function(b){
      b.classList.toggle('active',b.dataset.opDest===opDest);});
    Array.prototype.forEach.call(document.querySelectorAll('[data-op-hull]'),function(b){
      b.classList.toggle('active',b.dataset.opHull===opHullMode);});
    $('opDestBlock').style.display=opAction==='teleport'?'':'none';
    $('opHullBlock').style.display=opAction==='summon-ship'?'':'none';
    $('opDestIsland').style.display=(opAction==='teleport'&&opDest==='island')?'':'none';
    $('opDestCoord').style.display=(opAction==='teleport'&&opDest==='coord')?'':'none';
    $('opDestPlayerWrap').style.display=(opAction==='teleport'&&opDest==='player')?'':'none';
    $('opDestFixed').style.display=(opAction==='teleport'&&(opDest==='home'||opDest==='spawn'))?'':'none';
    text('opDestFixedText',opDest==='home'
      ?'The target is sent to their own recorded home island.'
      :'The target is sent to the Haven spawn point - the one destination with evidenced ground.');
    $('opHullExactWrap').style.display=(opAction==='summon-ship'&&opHullMode==='exact')?'':'none';
    var advanced=$('opTargetCustom').value.trim().length>0;
    $('opTargetCustomNote').style.display=advanced?'':'none';
    // A pending confirmation is for ONE exact command; touching the form
    // discards it rather than silently confirming something else.
    if(opPending){opPending=null;renderOperatorReview();}
    $('opReview').disabled=opInFlight;
  }
  function operatorTargetSelector(){
    var custom=$('opTargetCustom').value.trim();
    if(custom.length)return custom;
    return $('opTarget').value;
  }
  // Builds the exact request the confirm step will send, or a message saying
  // which piece is missing. Pure over the form state, so what is REVIEWED is
  // by construction what is SENT.
  function buildOperatorRequest(){
    var target=operatorTargetSelector();
    if(!target)return {error:'Pick a target player from the roster (or enter an advanced selector).'};
    if(opAction==='summon-ship'){
      var body={target:target};
      var sentence;
      if(opHullMode==='exact'){
        var hull=$('opHull').value;
        if(!hull)return {error:'Pick the exact hull to summon, or switch back to their own ship.'};
        body.hull=hull;
        sentence='Summon exact '+hull+' to '+target+'.';
      }else{
        sentence='Summon the ship OWNED by '+target+' to them.';
      }
      return {path:'/admin/api/operator/summon-ship',body:body,sentence:sentence};
    }
    var destination='';
    if(opDest==='island'){
      destination=$('opIsland').value;
      if(!destination)return {error:'Pick a destination island (use the search box to narrow 254 of them).'};
    }else if(opDest==='coord'){
      var x=$('opX').value.trim(),y=$('opY').value.trim(),z=$('opZ').value.trim();
      if(x===''||y===''||z==='')return {error:'Enter all three world coordinates (metres).'};
      destination='coord:'+x+','+y+','+z;
    }else if(opDest==='player'){
      var other=$('opDestPlayer').value;
      if(!other)return {error:'Pick the player the target should be sent to.'};
      destination='player:'+other;
    }else{
      destination=opDest;
    }
    return {path:'/admin/api/operator/teleport',
      body:{target:target,destination:destination},
      sentence:'Teleport '+target+' to '+destination+'.'};
  }
  function renderOperatorReview(){
    var box=$('opConfirm');
    if(!opPending){box.classList.remove('show');return;}
    box.classList.add('show');
    text('opConfirmSentence',opPending.sentence);
    text('opConfirmPayload','POST '+opPending.path+'\n'+JSON.stringify(opPending.body,null,2));
  }
  function reviewOperatorCommand(){
    var built=buildOperatorRequest();
    if(built.error){renderOperatorOutcome(null,null,built.error);return;}
    opPending=built;
    clearOperatorOutcome();
    renderOperatorReview();
  }
  function clearOperatorOutcome(){
    var out=$('opOutcome');out.className='op-outcome';clear(out);
  }
  // The refusal `reason` sentences were written to be SHOWN; render them
  // verbatim, next to the machine code a support thread can quote.
  function renderOperatorOutcome(response,submitted,formError){
    var out=$('opOutcome');clear(out);
    out.className='op-outcome show';
    var head=document.createElement('div');head.className='op-outcome-head';
    var pill=document.createElement('span');
    if(formError){
      pill.className='pill warn';pill.textContent='not sent';
      head.appendChild(pill);
      out.appendChild(head);
      var p=document.createElement('p');p.textContent=formError;out.appendChild(p);
      return;
    }
    var ok=response&&response.ok===true;
    pill.className='pill '+(ok?'ok':'bad');
    pill.textContent=ok?'accepted':('refused · '+((response&&response.code)||'no code'));
    head.appendChild(pill);
    var action=document.createElement('strong');
    action.textContent=(response&&response.action)||'operator';
    head.appendChild(action);
    out.appendChild(head);
    var message=document.createElement('p');
    message.textContent=ok
      ?((response&&response.message)||'Accepted.')
      :((response&&response.reason)||'The command was refused without a reason sentence.');
    out.appendChild(message);
    if(ok&&response.warnings&&response.warnings.length){
      var list=document.createElement('ul');list.className='op-warnings';
      response.warnings.forEach(function(w){
        var li=document.createElement('li');
        var wp=document.createElement('span');wp.className='pill warn';wp.textContent='warning';
        li.appendChild(wp);li.appendChild(document.createTextNode(' '+w));
        list.appendChild(li);
      });
      out.appendChild(list);
    }
    if(submitted){
      var echoHead=document.createElement('div');echoHead.className='op-echo-head';
      echoHead.textContent='Exactly what was submitted';out.appendChild(echoHead);
      var echo=document.createElement('pre');echo.className='op-echo';
      echo.textContent='POST '+submitted.path+'\n'+JSON.stringify(submitted.body,null,2);
      out.appendChild(echo);
    }
  }
  function sendOperatorCommand(){
    if(!opPending||opInFlight)return;
    var submitted=opPending;opPending=null;renderOperatorReview();
    opInFlight=true;
    $('opSend').disabled=true;$('opReview').disabled=true;
    fetch(submitted.path,{method:'POST',credentials:'same-origin',
      headers:Object.assign({'Content-Type':'application/json'},operatorHeaders()),
      body:JSON.stringify(submitted.body)})
      .then(function(r){
        if(r.status===401){location.href='/admin';return null;}
        return r.json().then(function(j){return j;});
      })
      .then(function(data){
        if(!data)return;
        opBridgeBusy=data.ok!==true&&data.code==='busy';
        renderOperatorOutcome(data,submitted,null);
      })
      .catch(function(){
        renderOperatorOutcome({ok:false,code:'unreachable',
          reason:'The command request could not reach the login server.'},submitted,null);
      })
      .then(function(){
        opInFlight=false;
        $('opSend').disabled=false;$('opReview').disabled=false;
        refreshTargets();refresh();
      });
  }
  function wireOperator(){
    Array.prototype.forEach.call(document.querySelectorAll('[data-op-action]'),function(b){
      b.addEventListener('click',function(){opAction=b.dataset.opAction;syncOperatorForm();});});
    Array.prototype.forEach.call(document.querySelectorAll('[data-op-dest]'),function(b){
      b.addEventListener('click',function(){opDest=b.dataset.opDest;syncOperatorForm();});});
    Array.prototype.forEach.call(document.querySelectorAll('[data-op-hull]'),function(b){
      b.addEventListener('click',function(){opHullMode=b.dataset.opHull;syncOperatorForm();});});
    $('opIslandSearch').addEventListener('input',renderOperatorIslands);
    $('opTarget').addEventListener('change',syncOperatorForm);
    $('opTargetCustom').addEventListener('input',syncOperatorForm);
    $('opReview').addEventListener('click',reviewOperatorCommand);
    $('opSend').addEventListener('click',sendOperatorCommand);
    $('opCancel').addEventListener('click',function(){opPending=null;renderOperatorReview();});
    $('opRefreshTargets').addEventListener('click',refreshTargets);
    refreshTargets();
    // The roster names people and reads positions out of the same stats file
    // the dashboard polls; 5 s keeps it honest without doubling the load the
    // 1.5 s stats poll already carries (each roster read also resolves names
    // against the account database).
    setInterval(refreshTargets,5000);
  }
