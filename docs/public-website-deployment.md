# Public website deployment

The login/REST process serves the WAReborn landing page directly at `GET /`.
Registration remains at `GET /signup` and `POST /register`; the landing page
does not receive credentials or player session data.

## Caddy cutover

Production currently redirects `/` to `/signup`. After deploying the login
server binary that contains `HomeHandler`, remove that redirect from
`/root/Avatar/Caddyfile` and proxy the exact root to the same upstream already
used for the other WAReborn browser routes:

```caddyfile
handle / {
    reverse_proxy 127.0.0.1:8085
}
```

Keep the existing `/signup`, `/register`, `/login`, `/account*`, `/download*`,
`/map*`, `/patchnotes*`, `/patch*`, `/alliance-emblem*`, and `/admin*` handlers.
In particular, do not merge the `/admin*` block into a public matcher: the
application still scopes its operator cookie to `/admin` and enforces operator
authentication on every route in that namespace.

Validate and reload Caddy using the deployment host's established commands,
then verify:

```bash
curl -fsSI https://wareborn.ratlabs.cc/ | head -n 1
curl -fsS https://wareborn.ratlabs.cc/ | grep 'The sky remembers'
curl -fsSI https://wareborn.ratlabs.cc/signup | head -n 1
curl -fsSI https://wareborn.ratlabs.cc/map | head -n 1
curl -sSI https://wareborn.ratlabs.cc/download | grep -Ei 'HTTP/|location:'
curl -sSI https://wareborn.ratlabs.cc/admin | head -n 1
curl -sSI https://wareborn.ratlabs.cc/definitely-not-a-route | head -n 1
```

Expected outcomes are `200` for `/`, `/signup`, and `/map`; the established
login redirect for an unauthenticated `/download`; the existing admin login or
disabled response for `/admin`; and a prompt `404` for the unknown route.
