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

        internal const string Html = @"<!DOCTYPE html>
<html lang=""en"">
<head>
<meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1"">
<meta name=""color-scheme"" content=""light dark"">
<title>Create an Account - Worlds Adrift Reborn</title>
<style>
:root {
  --sky-far:   #b9c6cb;
  --sky-near:  #cfd8d8;
  --hull:      #eceee7;
  --hull-edge: #b4bfc0;
  --ink:       #1e2b33;
  --ink-soft:  #55666f;
  --ink-faint: #7d8d95;
  --field:     #f7f8f4;
  --field-edge:#adbabd;
  --verdigris: #1c6d5e;
  --verdigris-hi: #17564b;
  --brass:     #866423;
  --rust:      #a8452c;
  --on-accent: #f4f7f3;
  --seam:      #9fb0ae;
  --shadow:    0 1px 2px rgba(24,40,48,.10), 0 12px 32px -14px rgba(24,40,48,.38);
}
@media (prefers-color-scheme: dark) {
  :root {
    --sky-far:   #0b1117;
    --sky-near:  #121b22;
    --hull:      #18222a;
    --hull-edge: #2c3c47;
    --ink:       #dce5e9;
    --ink-soft:  #9aacb5;
    --ink-faint: #74868f;
    --field:     #111a21;
    --field-edge:#33454f;
    --verdigris: #5cb5a0;
    --verdigris-hi: #74c9b4;
    --brass:     #c3a05c;
    --rust:      #e08163;
    --on-accent: #08161a;
    --seam:      #3a4c55;
    --shadow:    0 1px 0 rgba(255,255,255,.03) inset, 0 18px 44px -18px rgba(0,0,0,.75);
  }
}

* { box-sizing: border-box; }

body {
  margin: 0;
  min-height: 100vh;
  padding: 2rem 1rem 3rem;
  display: flex;
  align-items: center;
  justify-content: center;
  color: var(--ink);
  background-color: var(--sky-near);
  background-image:
    radial-gradient(60rem 26rem at 78% -12%, color-mix(in srgb, var(--brass) 16%, transparent), transparent 70%),
    radial-gradient(48rem 30rem at 8% 106%, color-mix(in srgb, var(--verdigris) 14%, transparent), transparent 72%),
    linear-gradient(180deg, var(--sky-far), var(--sky-near));
  font-family: 'Inter', 'Segoe UI', Roboto, 'Helvetica Neue', 'DejaVu Sans', Arial, sans-serif;
  font-size: 16px;
  line-height: 1.55;
  -webkit-text-size-adjust: 100%;
}

/* Horizon: the far edge of the shattered sky. */
body::before {
  content: '';
  position: fixed;
  left: 0; right: 0; top: 46%;
  height: 1px;
  background: linear-gradient(90deg, transparent, color-mix(in srgb, var(--seam) 70%, transparent) 22%, color-mix(in srgb, var(--seam) 70%, transparent) 78%, transparent);
  opacity: .55;
  pointer-events: none;
}

.card {
  position: relative;
  z-index: 1;
  width: 100%;
  max-width: 27rem;
  background: var(--hull);
  border: 1px solid var(--hull-edge);
  border-radius: 3px;
  box-shadow: var(--shadow);
  padding: 2.25rem 1.9rem 1.6rem;
  overflow: hidden;
}

/* Lashed canvas seam across the head of the card. */
.card::before {
  content: '';
  position: absolute;
  inset: 0 0 auto 0;
  height: 5px;
  background-image: repeating-linear-gradient(
    115deg,
    var(--verdigris) 0 7px,
    transparent 7px 13px
  );
  background-color: color-mix(in srgb, var(--verdigris) 22%, transparent);
  opacity: .85;
}

.mark {
  font-family: ui-monospace, 'DejaVu Sans Mono', 'Cascadia Mono', Consolas, 'Liberation Mono', monospace;
  font-size: .66rem;
  letter-spacing: .26em;
  text-transform: uppercase;
  color: var(--brass);
  margin: 0 0 .55rem;
}

h1 {
  font-family: ui-monospace, 'DejaVu Sans Mono', 'Cascadia Mono', Consolas, 'Liberation Mono', monospace;
  font-size: clamp(1.35rem, 5.4vw, 1.7rem);
  font-weight: 600;
  letter-spacing: .05em;
  line-height: 1.2;
  margin: 0 0 .5rem;
}

.lede {
  margin: 0 0 1.75rem;
  color: var(--ink-soft);
  font-size: .93rem;
  max-width: 34ch;
}

.field { margin-bottom: 1.1rem; }

label {
  display: block;
  font-family: ui-monospace, 'DejaVu Sans Mono', 'Cascadia Mono', Consolas, 'Liberation Mono', monospace;
  font-size: .68rem;
  letter-spacing: .17em;
  text-transform: uppercase;
  color: var(--ink-soft);
  margin-bottom: .4rem;
}

input {
  width: 100%;
  font: inherit;
  color: var(--ink);
  background: var(--field);
  border: 1px solid var(--field-edge);
  border-radius: 2px;
  padding: .62rem .7rem;
  transition: border-color .12s ease, box-shadow .12s ease;
}
input::placeholder { color: var(--ink-faint); }

input:focus-visible,
button:focus-visible,
a:focus-visible {
  outline: 2px solid var(--verdigris-hi);
  outline-offset: 2px;
  border-color: var(--verdigris);
}

input[aria-invalid='true'] { border-color: var(--rust); }

.hint {
  display: block;
  margin-top: .35rem;
  font-size: .77rem;
  color: var(--ink-faint);
}

.err {
  display: none;
  margin-top: .35rem;
  font-size: .8rem;
  color: var(--rust);
}
.err.show { display: block; }

button {
  width: 100%;
  margin-top: .35rem;
  font-family: ui-monospace, 'DejaVu Sans Mono', 'Cascadia Mono', Consolas, 'Liberation Mono', monospace;
  font-size: .78rem;
  letter-spacing: .17em;
  text-transform: uppercase;
  color: var(--on-accent);
  background: var(--verdigris);
  border: 1px solid transparent;
  border-radius: 2px;
  padding: .78rem 1rem;
  cursor: pointer;
  transition: background-color .12s ease, transform .06s ease;
}
button:hover:not(:disabled) { background: var(--verdigris-hi); }
button:active:not(:disabled) { transform: translateY(1px); }
button:disabled { opacity: .55; cursor: progress; }

.status {
  display: none;
  margin-top: 1.15rem;
  padding: .75rem .85rem;
  border-left: 3px solid var(--ink-faint);
  background: color-mix(in srgb, var(--ink-faint) 10%, transparent);
  font-size: .89rem;
}
.status.show { display: block; }
.status.ok {
  border-left-color: var(--verdigris);
  background: color-mix(in srgb, var(--verdigris) 13%, transparent);
}
.status.bad {
  border-left-color: var(--rust);
  background: color-mix(in srgb, var(--rust) 12%, transparent);
}
.status strong { display: block; margin-bottom: .2rem; }
.status code {
  font-family: ui-monospace, 'DejaVu Sans Mono', Consolas, monospace;
  font-size: .93em;
  padding: 0 .25em;
  background: color-mix(in srgb, var(--ink) 12%, transparent);
  border-radius: 2px;
}

footer {
  margin-top: 1.7rem;
  padding-top: 1rem;
  border-top: 1px solid color-mix(in srgb, var(--hull-edge) 75%, transparent);
  font-size: .74rem;
  line-height: 1.5;
  color: var(--ink-faint);
}

@media (max-width: 26rem) {
  .card { padding: 1.9rem 1.25rem 1.35rem; }
}

@media (prefers-reduced-motion: reduce) {
  *, *::before, *::after {
    transition-duration: .01ms !important;
    animation-duration: .01ms !important;
    animation-iteration-count: 1 !important;
  }
  button:active:not(:disabled) { transform: none; }
}
</style>
</head>
<body>
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
</body>
</html>
";
    }
}
