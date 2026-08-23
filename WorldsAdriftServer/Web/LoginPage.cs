namespace WorldsAdriftServer.Web
{
    /// <summary>
    /// The sign-IN page, the twin of <see cref="SignupPage"/> and served the same
    /// way: a fully self-contained, themed HTML string with no external CSS, fonts,
    /// scripts or images. The form POSTs JSON <c>{username, password}</c> to
    /// <c>/login</c> - the same shape /register takes - and on success the server
    /// answers <c>{ok:true, redirect:"/account"}</c>, which the page follows.
    ///
    /// The palette, the wind canvas and the plank button are copied verbatim from
    /// the sign-up page on purpose: this has to read as the same site, not a login
    /// bolted on beside it. The only shape difference is two fields instead of
    /// three and a link across to /signup for anyone without an account.
    /// </summary>
    internal static class LoginPage
    {
        internal const string ContentType = "text/html; charset=utf-8";

        internal static readonly string Html = @"<!DOCTYPE html>
<html lang=""en"">
<head>
<meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1"">
<meta name=""color-scheme"" content=""light dark"">
<title>Sign In - Worlds Adrift Reborn</title>
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
  background: linear-gradient(180deg, #93b7c8, #bed2d8 55%, #dde7e2);
  font-family: 'Inter', 'Segoe UI', Roboto, 'Helvetica Neue', 'DejaVu Sans', Arial, sans-serif;
  font-size: 16px;
  line-height: 1.55;
  -webkit-text-size-adjust: 100%;
}

#sky {
  position: fixed;
  inset: 0;
  width: 100%;
  height: 100%;
  display: block;
  z-index: 0;
  pointer-events: none;
}

main {
  position: relative;
  z-index: 1;
  width: 100%;
  max-width: 26rem;
  padding: 2.5rem 2rem 1.75rem;
  text-align: center;
}

main::before {
  content: '';
  position: absolute;
  inset: 0;
  z-index: -1;
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

footer {
  margin-top: 2.25rem;
  font-size: .72rem;
  line-height: 1.5;
  color: var(--ink-faint);
  text-shadow: var(--halo);
}
footer a { color: inherit; }

.alt {
  margin-top: 1.4rem;
  font-size: .82rem;
  color: var(--ink-soft);
  text-shadow: var(--halo);
}
.alt a { color: var(--rust); font-weight: 600; text-decoration: none; }
.alt a:hover { text-decoration: underline; }

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
" + PublicSiteChrome.Header("login", false) + @"
<canvas id=""sky"" aria-hidden=""true""></canvas>
<main class=""card"">
  <p class=""mark"">Worlds Adrift Reborn</p>
  <h1>Board the ship</h1>
  <p class=""lede"">Sign in with the name and passphrase you registered. This is the same pair the game client asks for.</p>

  <form id=""login"" novalidate>
    <div class=""field"">
      <label for=""username"">Email Address / Username</label>
      <input id=""username"" name=""username"" type=""text"" autocomplete=""username"" spellcheck=""false"" autocapitalize=""none"" placeholder=""skyhook@example.com"" aria-describedby=""username-err"">
      <span class=""err"" id=""username-err""></span>
    </div>

    <div class=""field"">
      <label for=""password"">Passphrase</label>
      <input id=""password"" name=""password"" type=""password"" autocomplete=""current-password"" aria-describedby=""password-err"">
      <span class=""err"" id=""password-err""></span>
    </div>

    <button type=""submit"" id=""submit"">Sign in</button>
  </form>

  <div class=""status"" id=""status"" role=""status"" aria-live=""polite""></div>

  <p class=""alt"">No account yet? <a href=""/signup"">Sign the crew roster</a>.</p>

  <footer>
    An unofficial, fan-run community server. Not affiliated with, endorsed by, or supported by Bossa Studios.
  </footer>
</main>

<script>
(function () {
  'use strict';

  var form    = document.getElementById('login');
  var button  = document.getElementById('submit');
  var status  = document.getElementById('status');

  var fields = {
    username: { input: document.getElementById('username'), err: document.getElementById('username-err') },
    password: { input: document.getElementById('password'), err: document.getElementById('password-err') }
  };

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
  }

  function hideStatus() {
    status.className = 'status';
    status.textContent = '';
  }

  function showStatus(kind, title, message) {
    status.textContent = '';
    var strong = document.createElement('strong');
    strong.textContent = title;
    status.appendChild(strong);
    if (message) { status.appendChild(document.createTextNode(message)); }
    status.className = 'status show ' + kind;
  }

  function showError(message) {
    showStatus('bad', 'Could not sign in', message);
  }

  form.addEventListener('submit', function (event) {
    event.preventDefault();
    clearFieldErrors();
    hideStatus();

    var username = fields.username.input.value.trim();
    var password = fields.password.input.value;

    // The server is the authority on credentials; the page only stops an empty
    // round-trip. It never says which field is wrong - neither will the server.
    if (username.length === 0) {
      setFieldError('username', 'Enter your username or email address.');
      fields.username.input.focus();
      return;
    }
    if (password.length === 0) {
      setFieldError('password', 'Enter your passphrase.');
      fields.password.input.focus();
      return;
    }

    button.disabled = true;
    var previousLabel = button.textContent;
    button.textContent = 'Signing in...';

    var finished = false;
    function finish() {
      if (finished) { return; }
      finished = true;
      button.disabled = false;
      button.textContent = previousLabel;
    }

    fetch('/login', {
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
      if (result.ok && result.payload && result.payload.ok === true) {
        // Do NOT re-enable the button: we are leaving the page.
        // The fallback exists only for a response that somehow carried no
        // redirect; it is the same landing the server sends, because a fallback
        // that pointed somewhere else would be a second, quieter answer to
        // 'where does signing in take me'.
        var redirect = (result.payload && typeof result.payload.redirect === 'string' && result.payload.redirect) || '/account';
        window.location.assign(redirect);
        return;
      }

      finish();

      if (result.payload && typeof result.payload.error === 'string' && result.payload.error.length > 0) {
        showError(result.payload.error);
        return;
      }

      if (result.statusCode === 401) {
        showError('Incorrect username or password.');
      } else {
        showError('The server refused the request (HTTP ' + result.statusCode + '). Please try again in a moment.');
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
/* Wind. Copied verbatim from the sign-up page so the two screens share one sky.
 * A streak that mostly holds a shallow line, then occasionally rolls through one
 * full turn and drifts forward while it turns - which is what reads as wind
 * rather than confetti. Drawn in CSS pixels; the backing store is scaled by the
 * device ratio once and the context transformed to match. */
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
