#!/usr/bin/env python3
"""Freeze the REAL rendered admin console into one self-contained, clickable file.

Why this exists: the admin map is behind auth and behind a running server, so
"does it look good" could only be answered by someone who could boot both. This
takes the page the server actually served - not a mock-up, not a re-render - and
makes it openable from disk, so the map can be reviewed, clicked and zoomed
offline before anything ships.

What it changes, and nothing else:
  * the CSRF token is blanked, so the frozen copy carries no live credential;
  * window.fetch is stubbed to answer the two /admin/api reads with the same
    shape a running server sends when NO game server is reporting, and to refuse
    every write. The page already renders that state honestly, so the preview
    shows the real "static map evidence, no live overlay" view rather than a
    console full of spinners;
  * a banner says what this file is, so a screenshot of it is never mistaken for
    a live console.

Usage:
    ./build_preview.py <served-admin.html> <out.html>
"""
import re
import sys

STUB = """<script>
// ---- offline preview shim -------------------------------------------------
// Frozen copy: there is no server behind this file. The two authenticated GETs
// are answered with the server's own "not reporting" shape so the static map
// evidence renders exactly as it does live; every write is refused.
(function(){
  var NOT_REPORTING={game:{reporting:false,state:'missing',players:[],domains:[]},
                     accounts:{available:false,reason:'offline preview'}};
  window.fetch=function(url,opts){
    var u=String(url);
    if(opts&&String(opts.method||'GET').toUpperCase()!=='GET')
      return Promise.resolve(new Response(JSON.stringify({error:'offline preview'}),
        {status:403,headers:{'Content-Type':'application/json'}}));
    var body=u.indexOf('/stats')>=0?NOT_REPORTING:{};
    return Promise.resolve(new Response(JSON.stringify(body),
      {status:200,headers:{'Content-Type':'application/json'}}));
  };
})();
</script>
<div style="position:sticky;top:0;z-index:99;padding:.55rem .95rem;background:#1d2f3c;
 border-bottom:1px solid #74c9cf;color:#cfe2e8;font:500 .72rem/1.5 ui-sans-serif,system-ui,sans-serif">
<strong style="color:#74c9cf">Offline preview.</strong> This is the real operator console as the
server rendered it, frozen to a file. The map, the detail panel, hover, zoom, search and the island
ledger all work. Nothing is live: there is no game server behind it, so the ship and player overlay
is empty by design and every control that would change something is refused.
</div>
"""


def main() -> int:
    src, out = sys.argv[1], sys.argv[2]
    html = open(src, encoding="utf-8").read()

    # No live credential travels in a file meant to be passed around.
    html = re.sub(r'(name="csrf" value=")[0-9a-f]*(")', r"\1offline-preview\2", html)
    html = re.sub(r"(var CSRF=')[0-9a-f]*(')", r"\1offline-preview\2", html)

    marker = "<body>"
    if marker not in html:
        raise SystemExit("no <body> in the served page")
    html = html.replace(marker, marker + STUB, 1)

    open(out, "w", encoding="utf-8").write(html)

    external = len(re.findall(r'src="http|href="http|@import', html))
    print(f"wrote {out} ({len(html):,} bytes), external references: {external}")
    return 1 if external else 0


if __name__ == "__main__":
    raise SystemExit(main())
