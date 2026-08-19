  // ---- the client's welcome message ---------------------------------------
  // Server-driven greeting. The client fetches /welcomeMessage on startup, so
  // whatever is saved here is what the next player to launch reads; there is no
  // second copy of this text inside the client to keep in step, which is the
  // whole reason it moved out of the client in the first place.
  //
  // EVERY top-level name in this fragment is prefixed `welcome` / `WELCOME_` on
  // purpose. The console is ONE closure built by concatenating these files, so a
  // top-level name that collides with another fragment's does not break this
  // card - it silently breaks that other panel, in a browser nobody is watching.
  //
  // Reads CSRF from admin-console.js, so it must be composed after it.

  // Mirrors ServerConfigPolicy.MaxWelcomeMessageLength. Duplicated here only to
  // draw the counter; the server is what actually refuses an over-long message,
  // and its refusal is what the status line shows.
  var WELCOME_MAX = 4000;
  var welcomeInFlight = false;

  function welcomeSetStatus(message, cls) {
    var line = $('welcomeStatusLine');
    if (!line) return;
    line.textContent = message;
    line.className = cls ? ('note ' + cls) : 'note';
  }

  function welcomeSetPill(label, cls) {
    var pill = $('welcomeState');
    if (!pill) return;
    pill.textContent = label;
    pill.className = 'pill' + (cls ? ' ' + cls : '');
  }

  function welcomeRecount() {
    var box = $('welcomeText');
    if (!box) return;
    text('welcomeCount', box.value.length + ' / ' + WELCOME_MAX + ' characters');
  }

  function welcomeFill(message) {
    var box = $('welcomeText');
    if (box) box.value = message;
    welcomeRecount();
  }

  function welcomeLoad() {
    fetch('/admin/api/welcome', {
      credentials: 'same-origin',
      headers: { 'Accept': 'application/json' }
    })
      .then(function (r) {
        if (r.status === 401) { location.href = '/admin'; return null; }
        return r.ok ? r.json() : null;
      })
      .then(function (d) {
        if (!d || typeof d.message !== 'string') {
          welcomeSetPill('unavailable', 'warn');
          welcomeSetStatus('Could not read the stored welcome message.', 'err');
          return;
        }
        welcomeFill(d.message);
        welcomeSetPill('stored', 'ok');
      })
      .catch(function () {
        welcomeSetPill('unavailable', 'warn');
        welcomeSetStatus('Could not read the stored welcome message.', 'err');
      });
  }

  function welcomeSubmit() {
    var box = $('welcomeText');
    if (!box || welcomeInFlight) return;

    welcomeInFlight = true;
    welcomeSetPill('saving');
    welcomeSetStatus('Saving…');

    fetch('/admin/api/welcome', {
      method: 'POST',
      credentials: 'same-origin',
      headers: {'Content-Type':'application/json','Accept':'application/json','X-Wareborn-Admin':'1','X-Wareborn-CSRF':CSRF},
      body: JSON.stringify({ message: box.value })
    })
      .then(function (r) {
        if (r.status === 401) { location.href = '/admin'; return null; }
        return r.json().then(
          function (j) { return { ok: r.ok, data: j }; },
          function () { return { ok: false, data: null }; });
      })
      .then(function (res) {
        welcomeInFlight = false;
        if (!res) return;
        if (res.ok && res.data && res.data.ok === true) {
          // Refill from the response, not from what was typed: the server
          // normalises, and the operator should see the text that is actually
          // stored rather than discover the difference from a player.
          if (typeof res.data.message === 'string') welcomeFill(res.data.message);
          welcomeSetPill('saved', 'ok');
          welcomeSetStatus('Saved. Players see this the next time they log in.');
          return;
        }
        welcomeSetPill('not saved', 'warn');
        welcomeSetStatus(
          (res.data && res.data.message) ? res.data.message : 'The server refused the change.',
          'err');
      })
      .catch(function () {
        welcomeInFlight = false;
        welcomeSetPill('not saved', 'warn');
        welcomeSetStatus('The console could not reach the server; nothing was saved.', 'err');
      });
  }

  // Self-booting, like the map-audience card: it touches only its own card, so
  // it needs nothing wired for it and adds no line to admin-wiring.js.
  (function () {
    var box = $('welcomeText');
    if (box) box.addEventListener('input', welcomeRecount);
    var button = $('welcomeSaveButton');
    if (button) button.addEventListener('click', welcomeSubmit);
    welcomeLoad();
  })();
