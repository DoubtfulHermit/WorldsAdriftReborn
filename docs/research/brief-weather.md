# RESEARCH BRIEF 7 — THE WEATHER ECS EXCEPTION STORM (upstream issue #34)

## Mission
Kill a per-frame exception storm that costs performance in EVERY session, including
single-player. Client logs show THOUSANDS of NullReferenceExceptions per session
(6000-12000+ observed) originating from EcsBootstrap.FixedUpdate, with the deeper frames
showing BossaECS AddToIdComponentToEntityMapS<WeatherCellCoordsC,uint>.Execute and
IdComponentToEntityMapWrapperSystem. This is upstream issue #34 ("WeatherCellCoordsC error
spam", open since Jan 2023).

Prior partial analysis (verify it, do not trust it): SystemBase.TryExecute guards only
Execute(); the filter phase and handler call are unguarded and composite wrappers have no
catch, so the NRE unwinds the whole ECS tree every FixedUpdate, killing every system
scheduled after it. It repeats forever because the duplicate-id branch in
AddToIdComponentToEntityMapS never marks the entity, so it re-matches each frame.

## Read first (mandatory)
- /home/ttanurhan/Games/WAReborn-src/docs/multiplayer.md
- Repo: /home/ttanurhan/Games/WAReborn-src (branch `multiplayer`), esp. the client mod
  WorldsAdriftReborn/Patching/ (Harmony patch conventions used throughout)

## Sources of truth
- Decompiled game C#: SCRATCH/acs/ (EcsBootstrap, BossaECS.Core.System.SystemBase,
  BossaECS.Framework.Systems.AddToIdComponentToEntityMapS, IdComponentToEntityMapWrapperSystem,
  WASystems.Components.Weather.WeatherCellCoordsC)
- Client logs with real stacks: ~/Games/WorldsAdrift/BepInEx/LogOutput.log
(SCRATCH = .../scratchpad)

## Questions — answer ALL with file:line evidence
Q1. ROOT CAUSE: exactly which dereference is null, and why. Trace WeatherCellCoordsC through
    the ECS registration path. Is the real cause that a weather entity/component the server
    never provides is expected to exist?
Q2. BLAST RADIUS: confirm or refute that the exception aborts sibling systems scheduled after
    it in the same FixedUpdate. If it does, enumerate what else silently never runs - this
    may be hiding OTHER broken features.
Q3. FIX OPTIONS, ranked by risk:
    (a) Harmony patch to mark/skip the offending entity so it stops re-matching,
    (b) Harmony patch to guard the unguarded call site,
    (c) provide the missing weather data server-side so the system succeeds legitimately,
    (d) disable the weather system entirely.
    For each: exact patch target (type + method), expected side effects, and what visibly
    changes in game (does weather still work?).
Q4. VERIFICATION: how do we prove the fix worked and measure the gain - what to count in the
    log, and is there a frame-time signal we can capture without new tooling?
Q5. UPSTREAM: this is a known open upstream issue. Would the fix be a clean, self-contained
    contribution? (Do NOT open a PR - just assess.)

## Deliverable
EXHAUSTIVE findings to SCRATCH/research/findings-weather.md with file:line citations, a
recommended fix with exact patch shape, verification method, risks, and unverified items.
Return a summary under 700 words. Do NOT edit repo files.
