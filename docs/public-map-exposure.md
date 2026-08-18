# Exposing the public live map at wareborn.ratlabs.cc/map

Status: written in Phase A of the public-map work. **Nothing in this document
has been applied to production**; it is the exact change to make at deploy
time, by hand.

## What the login server already serves

The login server (TCP 8085, host process `wareborn-login`) answers three
unauthenticated routes, all handled by `PublicMapHandler` - a class that is
structurally separate from the admin console, reads no cookies, and can reach
none of the admin command bridge:

| Route | Payload | Server cache header |
| --- | --- | --- |
| `GET /map` | The public map page (self-contained HTML, no external hosts) | `public, max-age=60` |
| `GET /map/data` | Anonymized live snapshot (fauna clock/roster, anonymous player and ship markers). Rebuilt at most once per 2 s regardless of viewer count; the raw stats file is never served. | `public, max-age=2` |
| `GET /map/world` | The static preserved-release world catalogue (the heavy one, ~island shells and inventories). Static per build. | `public, max-age=3600` |

Everything else under `/map` is answered 404 by the login server itself, so no
other route can shadow the public prefix.

## The Caddy addition

Production fronts the login server with the Avatar-stack Caddy container at
`wareborn.ratlabs.cc`, which already proxies `/patch/*` to the host's 8085.
The `/map` namespace is proxied the same way - same upstream, no new
credentials, no headers stripped or added for auth because the endpoint has
none:

```caddy
# inside the existing wareborn.ratlabs.cc site block
handle /map* {
    reverse_proxy host.docker.internal:8085
}
```

Use whatever upstream address the existing `/patch*` handle in that site block
uses (it is the same login-server upstream; on this stack that has been the
host gateway IP rather than `host.docker.internal` - copy the working one).

## Recommended hardening at the proxy (optional but cheap)

The login server already emits correct `Cache-Control`, `nosniff`,
`Referrer-Policy: no-referrer` and open CORS (the feed is credential-free and
read-only; the anonymizing projection, not the transport, is the privacy
boundary). At the Caddy layer it is worth adding:

```caddy
handle /map* {
    # Honour the backend's Cache-Control and serve repeat polls from the
    # proxy cache if the cache module is enabled; otherwise the backend's
    # own 2 s single-entry cache already bounds the work per poll.
    reverse_proxy host.docker.internal:8085
}
```

- **Rate limiting**: if the stack's Caddy has the `rate_limit` plugin, cap
  `/map/data` at something generous like 2 r/s per remote IP with a burst of
  10 - the page polls every ~3 s, so a legitimate viewer never gets near it.
  If the plugin is not built in (stock Caddy: it is not), skip it; the 2 s
  server-side cache means even an abusive poller costs one string write per
  request, not a stats-file read.
- **Compression**: `encode zstd gzip` on the site block (if not already
  global) matters for `/map/world`, which is the large static payload.
- Do **not** add `Access-Control-Allow-Credentials` or widen anything under
  `/admin`; the admin console stays exactly as deployed.

## Verifying after applying

```sh
curl -fsS https://wareborn.ratlabs.cc/map/data | jq '{reporting, currentOnline, ships: (.ships|length), players: (.players|length)}'
curl -fsS https://wareborn.ratlabs.cc/map/data | grep -Ei 'peerId|entityId|rtt|account|character' && echo LEAK || echo clean
curl -o /dev/null -sw '%{http_code}\n' https://wareborn.ratlabs.cc/map
```

The second command must print `clean`.
