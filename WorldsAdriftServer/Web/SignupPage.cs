namespace WorldsAdriftServer.Web
{
    /// <summary>
    /// The static account sign-up page, served verbatim by the HTTP layer.
    /// Fully self-contained: no external CSS, fonts, scripts or images.
    /// The form POSTs JSON to /register and renders the reply in place.
    /// </summary>
    internal static class SignupPage
    {
        internal const string ContentType = "text/html; charset=utf-8";

        internal static readonly string Html = @"<!DOCTYPE html>
<html lang=""en"">
<head>
<meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1"">
<meta name=""color-scheme"" content=""light dark"">
<title>Create an Account - Worlds Adrift Reborn</title>
<style>
:root {
  --ink:        #26313d;
  --ink-soft:   #43525f;
  --ink-faint:  #5d6b76;
  --cream:      #ece7db;
  --field:      rgba(74, 80, 96, .60);
  --field-edge: rgba(30, 36, 48, .30);
  --field-ink:  #f0ece2;
  --field-hint: #c2c6cf;
  --timber-lo:  #c68d60;
  --timber-mid: #d9a074;
  --timber-hi:  #eebd8e;
  --timber-ink: #4a2c14;
  --batten:     #a97244;
  --batten-lo:  #8e5d36;
  --batten-edge:#7d4d2a;
  --rust:       #a8321f;
  --good:       #2c6b52;
  --veil:       rgba(255, 255, 255, .34);
  --halo:       0 1px 0 rgba(255,255,255,.55);
}

@media (prefers-color-scheme: dark) {
  :root {
    --ink:       #e4e9ec;
    --ink-soft:  #b3c0c8;
    --ink-faint: #8b99a3;
    --field:     rgba(96, 106, 124, .40);
    --field-edge:rgba(180, 200, 215, .16);
    --field-ink: #eef1f3;
    --field-hint:#9aa6b2;
    --rust:      #ef8a6b;
    --good:      #7fd2b3;
    --veil:      rgba(6, 12, 20, .46);
    --halo:      0 1px 3px rgba(0,0,0,.65);
  }
}

* { box-sizing: border-box; }

body {
  margin: 0;
  min-height: 100vh;
  padding: 2.5rem 1.25rem 3rem;
  display: flex;
  align-items: center;
  justify-content: center;
  color: var(--ink);
  /* Fallback only: the canvas paints over this the moment it runs. */
  background: linear-gradient(180deg, #93b7c8, #bed2d8 55%, #dde7e2);
  font-family: 'Inter', 'Segoe UI', Roboto, 'Helvetica Neue', 'DejaVu Sans', Arial, sans-serif;
  font-size: 16px;
  line-height: 1.55;
  -webkit-text-size-adjust: 100%;
}

/* The wind. Fixed and behind everything. */
#sky {
  position: fixed;
  inset: 0;
  width: 100%;
  height: 100%;
  display: block;
  z-index: 0;
  pointer-events: none;
}

/* The game hangs its menus straight on the world with no panel behind them, so
   this does too. The only concession is a soft veil: the sky underneath moves
   and changes brightness, and small text over moving sky is unreadable. */
main {
  position: relative;
  z-index: 1;
  width: 100%;
  max-width: 26rem;
  padding: 2.5rem 2rem 1.75rem;
  text-align: center;
}

/* The veil lives on a pseudo-element that overhangs the column on every side,
   with the gradient reaching full transparency before that larger box ends.
   Painting it on `main` itself left a visible rectangle: the radial had not
   finished fading by the time the element's own edge cut it off. */
main::before {
  content: '';
  position: absolute;
  inset: 0;
  z-index: -1;
  /* closest-side is the whole trick: the ellipse reaches its last stop exactly
     at the nearest edge, so the veil is fully transparent before the box ends
     and there is no rectangle to see. An earlier version overhung the column
     with a negative inset, which faded just as nicely but added its overhang to
     the page's scrollable area and produced a phantom scrollbar. */
  background: radial-gradient(closest-side ellipse at 50% 48%, var(--veil), transparent);
  pointer-events: none;
}

.mark {
  font-size: .68rem;
  letter-spacing: .38em;
  text-transform: uppercase;
  color: var(--ink-soft);
  text-shadow: var(--halo);
  margin: 0 0 1.15rem;
}
.mark::before { content: '— '; }
.mark::after  { content: ' —'; }

h1 {
  font-size: clamp(1.5rem, 6vw, 2rem);
  font-weight: 300;
  letter-spacing: .04em;
  line-height: 1.15;
  margin: 0 0 .6rem;
  text-wrap: balance;
  text-shadow: var(--halo);
}

.lede {
  margin: 0 auto 2rem;
  max-width: 30ch;
  color: var(--ink-soft);
  font-size: .93rem;
  text-shadow: var(--halo);
}

.field { margin-bottom: .85rem; text-align: left; }

label {
  display: block;
  font-size: .66rem;
  letter-spacing: .2em;
  text-transform: uppercase;
  color: var(--ink-soft);
  text-shadow: var(--halo);
  margin-bottom: .35rem;
}

/* Flat slate bars, no radius, cream text - the game's own input treatment. */
input {
  width: 100%;
  font: inherit;
  color: var(--field-ink);
  background: var(--field);
  border: 1px solid var(--field-edge);
  border-radius: 0;
  padding: .6rem .7rem;
  -webkit-backdrop-filter: blur(3px);
  backdrop-filter: blur(3px);
  transition: background-color .12s ease, border-color .12s ease;
}
input::placeholder { color: var(--field-hint); }
input:hover { background: color-mix(in srgb, var(--field) 78%, #ffffff 22%); }
input[aria-invalid='true'] { border-color: var(--rust); }

input:focus-visible,
button:focus-visible {
  outline: 2px solid var(--timber-hi);
  outline-offset: 3px;
}

.hint, .err {
  display: block;
  margin-top: .3rem;
  font-size: .76rem;
  line-height: 1.45;
  text-shadow: var(--halo);
}
.hint { color: var(--ink-faint); }
.err  { display: none; color: var(--rust); font-weight: 500; }
.err.show { display: block; }

/* ── the plank ──────────────────────────────────────────────────────────────
   The game's buttons are a tan board held by a darker batten at each end, the
   battens standing slightly proud top and bottom. The battens are pseudo-
   elements rather than markup because the script writes button.textContent
   while the request is in flight, which would wipe any child node. */
button {
  position: relative;
  width: 100%;
  margin: 1.9rem 0 .3rem;
  padding: .82rem 1.5rem;
  font: inherit;
  font-size: .8rem;
  font-weight: 600;
  letter-spacing: .17em;
  text-transform: uppercase;
  color: var(--timber-ink);
  border: 1px solid #a4744a;
  border-radius: 1px;
  cursor: pointer;
  background-image:
    linear-gradient(180deg, rgba(255,255,255,.34), rgba(255,255,255,0) 44%),
    repeating-linear-gradient(90deg, rgba(120,78,44,.07) 0 3px, transparent 3px 9px),
    linear-gradient(180deg, var(--timber-hi), var(--timber-mid) 46%, var(--timber-lo));
  box-shadow: 0 2px 0 rgba(112,72,40,.42), 0 12px 26px -14px rgba(38,24,10,.85);
  transition: filter .12s ease, transform .06s ease;
}
button::before,
button::after {
  content: '';
  position: absolute;
  top: -9px;
  bottom: -9px;
  width: 12px;
  border: 1px solid var(--batten-edge);
  border-radius: 1px;
  background-image: linear-gradient(90deg, var(--batten), var(--batten-lo));
  box-shadow: 0 2px 0 rgba(90,58,30,.35);
}
button::before { left: -7px; }
button::after  { right: -7px; }

button:hover:not(:disabled) { filter: brightness(1.06); }
button:active:not(:disabled) { transform: translateY(1px); }
button:disabled { filter: saturate(.45) brightness(.94); cursor: progress; }

.status {
  display: none;
  margin-top: 1.6rem;
  padding: .8rem .9rem;
  text-align: left;
  font-size: .88rem;
  color: var(--ink);
  background: var(--field);
  border-left: 3px solid var(--ink-faint);
  -webkit-backdrop-filter: blur(4px);
  backdrop-filter: blur(4px);
  color: var(--field-ink);
}
.status.show { display: block; }
.status.ok  { border-left-color: var(--good); }
.status.bad { border-left-color: var(--rust); }
.status strong { display: block; margin-bottom: .25rem; letter-spacing: .04em; }
.status code {
  font-family: ui-monospace, 'DejaVu Sans Mono', Consolas, monospace;
  font-size: .93em;
  padding: 0 .3em;
  background: rgba(0,0,0,.28);
}

footer {
  margin-top: 2.25rem;
  font-size: .72rem;
  line-height: 1.5;
  color: var(--ink-faint);
  text-shadow: var(--halo);
}

@media (max-width: 26rem) {
  main { padding: 2rem 1.1rem 1.5rem; }
  button::before { left: -5px; }
  button::after  { right: -5px; }
}

@media (prefers-reduced-motion: reduce) {
  *, *::before, *::after {
    transition-duration: .01ms !important;
    animation-duration: .01ms !important;
    animation-iteration-count: 1 !important;
  }
  button:active:not(:disabled) { transform: none; }
}
</style>" + PublicSiteChrome.Style + PublicSiteChrome.PlayerStyle + @"
</head>
<body class=""wa-player wa-auth"">
" + PublicSiteChrome.Header("signup", false) + @"
<canvas id=""sky"" aria-hidden=""true""></canvas>
<main class=""card"">
  <p class=""mark"">Worlds Adrift Reborn</p>
  <h1>Sign the crew roster</h1>
  <p class=""lede"">Pick a name and a passphrase. You will use the same pair to board from the game client.</p>

  <form id=""signup"" novalidate>
    <div class=""field"">
      <label for=""username"">Email Address / Username</label>
      <input id=""username"" name=""username"" type=""text"" autocomplete=""username"" spellcheck=""false"" autocapitalize=""none"" placeholder=""skyhook@example.com"" aria-describedby=""username-hint username-err"">
      <span class=""hint"" id=""username-hint"">3-64 characters. Letters, digits and @ . + _ - only. An email address works.</span>
      <span class=""err"" id=""username-err""></span>
    </div>

    <div class=""field"">
      <label for=""password"">Passphrase</label>
      <input id=""password"" name=""password"" type=""password"" autocomplete=""new-password"" aria-describedby=""password-hint password-err"">
      <span class=""hint"" id=""password-hint"">6-256 characters.</span>
      <span class=""err"" id=""password-err""></span>
    </div>

    <div class=""field"">
      <label for=""confirm"">Confirm passphrase</label>
      <input id=""confirm"" name=""confirm"" type=""password"" autocomplete=""new-password"" aria-describedby=""confirm-err"">
      <span class=""err"" id=""confirm-err""></span>
    </div>

    <button type=""submit"" id=""submit"">Create account</button>
  </form>

  <div class=""status"" id=""status"" role=""status"" aria-live=""polite""></div>

  <footer>
    An unofficial, fan-run community server. Not affiliated with, endorsed by, or supported by Bossa Studios.
  </footer>
</main>

<script>
(function () {
  'use strict';

  var form    = document.getElementById('signup');
  var button  = document.getElementById('submit');
  var status  = document.getElementById('status');

  var fields = {
    username: { input: document.getElementById('username'), err: document.getElementById('username-err') },
    password: { input: document.getElementById('password'), err: document.getElementById('password-err') },
    confirm:  { input: document.getElementById('confirm'),  err: document.getElementById('confirm-err')  }
  };

  var NAME_CHARS = /^[A-Za-z0-9@.+_-]+$/;

  function setFieldError(key, message) {
    var f = fields[key];
    f.err.textContent = message || '';
    f.err.classList.toggle('show', !!message);
    if (message) {
      f.input.setAttribute('aria-invalid', 'true');
    } else {
      f.input.removeAttribute('aria-invalid');
    }
  }

  function clearFieldErrors() {
    setFieldError('username', '');
    setFieldError('password', '');
    setFieldError('confirm', '');
  }

  function hideStatus() {
    status.className = 'status';
    status.textContent = '';
  }

  function showStatus(kind, title, bodyNodes) {
    status.textContent = '';
    var strong = document.createElement('strong');
    strong.textContent = title;
    status.appendChild(strong);
    (bodyNodes || []).forEach(function (node) { status.appendChild(node); });
    status.className = 'status show ' + kind;
  }

  function showError(message) {
    showStatus('bad', 'Could not create the account', [document.createTextNode(message)]);
  }

  function text(value) { return document.createTextNode(value); }

  function code(value) {
    var el = document.createElement('code');
    el.textContent = value;
    return el;
  }

  // Mirrors the server-side rules exactly. Returns the first problem found, or null.
  function validate(username, password, confirm) {
    if (username.length === 0) {
      return { field: 'username', message: 'Enter a username or email address.' };
    }
    if (username.length < 3) {
      return { field: 'username', message: 'Too short - use at least 3 characters (you have ' + username.length + ').' };
    }
    if (username.length > 64) {
      return { field: 'username', message: 'Too long - use at most 64 characters (you have ' + username.length + ').' };
    }
    if (!NAME_CHARS.test(username)) {
      return { field: 'username', message: 'Remove any character that is not a letter, a digit, or one of @ . + _ - (spaces are not allowed).' };
    }
    if (password.length === 0) {
      return { field: 'password', message: 'Enter a passphrase.' };
    }
    if (password.length < 6) {
      return { field: 'password', message: 'Too short - use at least 6 characters (you have ' + password.length + ').' };
    }
    if (password.length > 256) {
      return { field: 'password', message: 'Too long - use at most 256 characters (you have ' + password.length + ').' };
    }
    if (confirm !== password) {
      return { field: 'confirm', message: 'The two passphrases do not match. Retype them to be sure.' };
    }
    return null;
  }

  function onSuccess(username) {
    showStatus('ok', 'Account created', [
      text('Launch the game and sign in on the landing screen using the login form built into the game itself: put '),
      code(username),
      text(' in the Email Address field and the passphrase you just chose in the Password field. There is nothing else to activate.')
    ]);
    form.reset();
    fields.username.input.focus();
  }

  form.addEventListener('submit', function (event) {
    event.preventDefault();
    clearFieldErrors();
    hideStatus();

    var username = fields.username.input.value.trim();
    var password = fields.password.input.value;
    var confirm  = fields.confirm.input.value;

    var problem = validate(username, password, confirm);
    if (problem) {
      setFieldError(problem.field, problem.message);
      fields[problem.field].input.focus();
      return;
    }

    button.disabled = true;
    var previousLabel = button.textContent;
    button.textContent = 'Sending...';

    var finished = false;
    function finish() {
      if (finished) { return; }
      finished = true;
      button.disabled = false;
      button.textContent = previousLabel;
    }

    fetch('/register', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ username: username, password: password })
    }).then(function (response) {
      return response.text().then(function (raw) {
        var payload = null;
        try {
          payload = raw ? JSON.parse(raw) : null;
        } catch (parseError) {
          payload = null;
        }
        return { ok: response.ok, statusCode: response.status, payload: payload };
      });
    }).then(function (result) {
      finish();

      if (result.ok && result.payload && result.payload.success === true) {
        onSuccess(result.payload.username || username);
        return;
      }

      if (result.payload && typeof result.payload.error === 'string' && result.payload.error.length > 0) {
        showError(result.payload.error);
        return;
      }

      if (result.ok) {
        showError('The server answered, but the reply could not be understood. Your account may or may not have been created - try logging in first, and sign up again only if that fails.');
      } else {
        showError('The server refused the request (HTTP ' + result.statusCode + ') and gave no reason. Please try again in a moment.');
      }
    }).catch(function () {
      finish();
      showError('Could not reach the server. Check your connection and try again - if it keeps failing, the server may be down for maintenance.');
    });
  });

  Object.keys(fields).forEach(function (key) {
    fields[key].input.addEventListener('input', function () { setFieldError(key, ''); });
  });
})();
</script>
<script>
/* Wind.
 *
 * Ported from the Legends Awakened launcher's `air` element (src/js/fx.js):
 * a streak that mostly holds a shallow line, then occasionally rolls through
 * one full turn and carries on. The roll DRIFTS FORWARD while it turns, so it
 * reads as a flourish rather than a reversal - that single detail is what makes
 * it look like wind instead of confetti, so it is kept exactly.
 *
 * Drawn in CSS pixels: the backing store is scaled by the device ratio once and
 * the context is transformed to match, so nothing below thinks about it.
 */
(function () {
  var canvas = document.getElementById('sky');
  if (!canvas || !canvas.getContext) return;
  var ctx = canvas.getContext('2d');
  if (!ctx) return;

  var reduced = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  var darkQuery = window.matchMedia ? window.matchMedia('(prefers-color-scheme: dark)') : null;

  var PALETTE = {
    light: { top: '#93b7c8', mid: '#bed2d8', low: '#dde7e2', sun: 'rgba(255,238,200,.5)',  wind: '255,255,255' },
    dark:  { top: '#070d14', mid: '#101c26', low: '#1b2c35', sun: 'rgba(255,178,110,.28)', wind: '206,238,235' }
  };
  var P = PALETTE.light;
  function pickPalette() { P = (darkQuery && darkQuery.matches) ? PALETTE.dark : PALETTE.light; }
  pickPalette();

  var W = 0, H = 0, wind = [];

  function spawn(seeded) {
    var z = Math.random();
    return {
      x: seeded ? Math.random() * W : -40 - Math.random() * 120,
      /* Kept out of the very bottom of the frame: the gradient goes almost
         white down there in the light theme and a pale streak vanishes. */
      y: H * (0.04 + Math.random() * 0.68),
      spd: 0.9 + z * 1.9,
      th: (Math.random() - 0.5) * 0.06,
      tw: 0, twirl: 0, trail: [],
      size: 0.7 + z * 1.9,
      op: 0.22 + z * 0.40,
      life: seeded ? 0.3 + Math.random() * 0.7 : 1,
      decay: 0.0005 + Math.random() * 0.0008,
      seed: Math.random() * 9
    };
  }

  function resize() {
    W = Math.max(1, window.innerWidth);
    H = Math.max(1, window.innerHeight);
    var dpr = Math.min(window.devicePixelRatio || 1, 1.5);
    canvas.width = Math.round(W * dpr);
    canvas.height = Math.round(H * dpr);
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    wind.length = 0;
    var n = W < 700 ? 24 : 44;
    for (var i = 0; i < n; i++) wind.push(spawn(true));
  }

  function update(t) {
    for (var i = wind.length - 1; i >= 0; i--) {
      var p = wind[i];
      p.life -= p.decay;
      if (p.twirl > 0) {
        p.th += p.tw; p.twirl--;
        p.x += Math.cos(p.th) * p.spd * 0.55 + p.spd * 0.75;
        p.y += Math.sin(p.th) * p.spd * 0.5;
      } else {
        p.th *= 0.9;
        p.th += Math.sin(t * 0.7 + p.seed) * 0.005;
        if (p.th > 0.3) p.th = 0.3; else if (p.th < -0.3) p.th = -0.3;
        if (Math.random() < 0.0025 && p.x > 50 && p.x < W - 70) {
          p.twirl = (24 + Math.random() * 22) | 0;
          p.tw = (Math.random() < 0.5 ? 1 : -1) * 6.283 / p.twirl;
        }
        p.x += Math.cos(p.th) * p.spd;
        p.y += Math.sin(p.th) * p.spd;
      }
      if (p.y < -20) { p.y = H + 10; p.trail.length = 0; }
      else if (p.y > H + 20) { p.y = -10; p.trail.length = 0; }
      p.trail.push(p.x, p.y);
      if (p.trail.length > 68) p.trail.splice(0, 2);
      if (p.life <= 0 || p.x > W + 90) wind[i] = spawn(false);
    }
  }

  function render(t) {
    var g = ctx.createLinearGradient(0, 0, 0, H);
    g.addColorStop(0, P.top);
    g.addColorStop(0.55, P.mid);
    g.addColorStop(1, P.low);
    ctx.fillStyle = g;
    ctx.fillRect(0, 0, W, H);

    var sun = ctx.createRadialGradient(W * 0.76, H * 0.12, 0, W * 0.76, H * 0.12, Math.max(W, H) * 0.6);
    sun.addColorStop(0, P.sun);
    sun.addColorStop(1, 'rgba(0,0,0,0)');
    ctx.fillStyle = sun;
    ctx.fillRect(0, 0, W, H);

    /* Tail to head in three chunks, each fatter and more opaque than the last,
       so a streak has direction without needing a gradient stroke. */
    ctx.lineCap = 'round';
    ctx.lineJoin = 'round';
    var seg = [[0, 0.34, 0.28], [0.3, 0.67, 0.55], [0.63, 1, 1]];
    for (var i = 0; i < wind.length; i++) {
      var p = wind[i], T = p.trail, n = T.length >> 1;
      if (n <= 3) continue;
      for (var s = 0; s < 3; s++) {
        var i0 = (n * seg[s][0]) | 0;
        var i1 = Math.min(n - 1, Math.ceil(n * seg[s][1]));
        var k = seg[s][2];
        if (i1 - i0 < 1) continue;
        ctx.globalAlpha = Math.max(0, Math.min(1, p.life)) * p.op * k;
        ctx.lineWidth = p.size * (0.45 + 0.55 * k);
        ctx.strokeStyle = 'rgba(' + P.wind + ',1)';
        ctx.beginPath();
        ctx.moveTo(T[i0 * 2], T[i0 * 2 + 1]);
        for (var j = i0 + 1; j <= i1; j++) ctx.lineTo(T[j * 2], T[j * 2 + 1]);
        ctx.stroke();
      }
    }
    ctx.globalAlpha = 1;
  }

  var raf = null;
  function frame() {
    var t = performance.now() / 1000;
    update(t);
    render(t);
    raf = requestAnimationFrame(frame);
  }
  function start() { if (raf === null && !reduced) raf = requestAnimationFrame(frame); }
  function stop() { if (raf !== null) { cancelAnimationFrame(raf); raf = null; } }

  /* Reduced motion gets the same sky, standing still: the simulation is stepped
     far enough for the streaks to grow their tails, then drawn once. A blank
     sky would be a worse answer than a still one. */
  function still() {
    for (var i = 0; i < 90; i++) update(i / 60);
    render(1.5);
  }

  window.addEventListener('resize', function () { resize(); if (reduced) still(); });
  document.addEventListener('visibilitychange', function () {
    if (document.hidden) stop(); else start();
  });
  if (darkQuery) {
    var onTheme = function () { pickPalette(); if (reduced) still(); };
    if (darkQuery.addEventListener) darkQuery.addEventListener('change', onTheme);
    else if (darkQuery.addListener) darkQuery.addListener(onTheme);
  }

  resize();
  if (reduced) still(); else start();
})();
</script>
</body>
</html>
";
    }
}
