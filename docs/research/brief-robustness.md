# RESEARCH BRIEF 5 — ROBUSTNESS, SESSIONS & RECONNECT

## Mission
Make the server survive real-world use without a human babysitting it. Tonight exposed
concrete fragility over the internet:
- GHOST SESSIONS: after the server restarted, a client kept rendering the world and looked
  fine, but the server had forgotten it. The player was invisible to everyone and never
  reconnected. Only a manual client restart fixed it.
- STALE AVATARS: a disconnected player's rig stays forever (no removal message exists).
- ONE-AT-A-TIME: players kept ending up connected in sequence rather than together, partly
  because of the above.
- The server has no session concept at all: PeerManager keyed by ENetPeerHandle, no
  identity, no timeout policy, no reconnect path.

## Read first (mandatory)
- /home/ttanurhan/Games/WAReborn-src/docs/multiplayer.md
- Repo: /home/ttanurhan/Games/WAReborn-src (branch `multiplayer`), especially
  WorldsAdriftRebornGameServer.cs (main loop, OnNewClientConnected/OnClientDisconnected,
  PeerIdentity, PlayerRegistry, the mirror + resend logic) and
  WorldsAdriftRebornCoreSdk/enetLayer.cpp (ENet_Poll, connect/disconnect events).

## Sources of truth
- Decompiled game C#: SCRATCH/acs/   - generated: SCRATCH/gencode/   (SCRATCH = .../scratchpad)
- ENet source: /home/ttanurhan/Games/WAReborn-src/WorldsAdriftRebornCoreSdk/enet/

## Questions — answer ALL with file:line evidence
Q1. ENet's own reliability: what timeout/keepalive does enet_host_service give us, what
    events does the client get when the server vanishes, and does our C++ layer surface
    disconnects to the CLIENT at all (we handle them server-side only)? Cite enetLayer.cpp
    and enet/protocol.c.
Q2. CLIENT-SIDE DETECTION: what should the mod do when the connection drops - can it detect
    it (a disconnect callback, a heartbeat, ENet peer state) and tell the player or trigger a
    reconnect, instead of silently rendering a dead world?
Q3. RECONNECT: design the smallest reconnect that works. What state must the server keep to
    let a returning player resume (entity id reuse? new entity + cleanup of the old?), and
    what must the client re-request. Note there is no entity removal message yet.
Q4. SESSION IDENTITY: today a peer is identified only by its ENetPeer pointer. What is the
    smallest reliable durable identity (see also the persistence research - do NOT duplicate
    its work, just state the dependency)?
Q5. SERVER HYGIENE: recommend concrete hardening - peer timeouts, orphaned-entity cleanup,
    bounded collections (PeerIdentity/PlayerRegistry/AppearanceStore all grow), and what
    should be logged so a future failure is diagnosable without adding new telemetry.
Q6. OPERATIONS: the server runs under systemd on a VPS (units wareborn-login, wareborn-game,
    /opt/wareborn). Recommend restart policy, health checking, and how a client should behave
    when the server restarts under it.

## Deliverable
EXHAUSTIVE findings to SCRATCH/research/findings-robustness.md with file:line citations, a
recommended design, an ordered plan, risks, and explicit unverified items.
Return a summary under 700 words. Do NOT edit repo files.
