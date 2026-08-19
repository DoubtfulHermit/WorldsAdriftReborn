// The patch-notes editor's one job: put the CURRENT notes in the box.
//
// Fetched rather than rendered into the page, and that is the point. The
// source is whatever /patchnotes is serving this second - the committed file,
// or an override somebody stored - and asking the public route for it means the
// operator edits the exact text a visitor is reading. A copy templated into the
// dashboard would be a second path to the same string and could disagree with
// it.
//
// Self-booting, like the viewer card: it touches only its own card, so it wires
// nothing and is appended last.
(function(){
  var box = document.getElementById('patch-notes-input');
  var state = document.getElementById('patchNotesState');
  if(!box) return;

  fetch('/patchnotes/source',{cache:'no-store'})
    .then(function(r){ if(!r.ok) throw new Error('HTTP '+r.status); return r.text(); })
    .then(function(text){
      // Never clobber an operator who started typing while this was in flight.
      if(box.value === '') box.value = text;
      if(state) state.textContent = 'Loaded the notes /patchnotes is serving now.';
    })
    .catch(function(e){
      if(state) state.textContent = 'Could not load the current notes (' + e.message +
        '). Saving from an empty box would replace them, so type nothing here until it loads.';
    });
})();
