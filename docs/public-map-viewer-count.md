# The public map's viewer count

Status: implemented on `feat/map-viewers`. Not deployed.

The public map at `/map` shows how many people have it open right now, and the
server records that number over time. This document is the design constraint
rather than the feature description: the whole point is that the thing counting
people is **incapable** of saying who they are, and that has to stay true after
the next change.

## What the number means

> **N watching**

*N is the number of browser tabs that have polled in the last 30 seconds.*

Everything about that sentence is deliberate, and every part of it is a caveat:

- **Tabs, not people.** One person with the map open twice counts twice. There
  is no way to fix that which does not involve recognising a person across two
  tabs, which is the thing we are refusing to do.
- **Not instantaneous.** A tab that closes stops beating and drops out when its
  last beat expires, up to 30 seconds later. It does not vanish.
- **Background tabs leave.** Browsers throttle a hidden tab's timers to roughly
  once a minute. The 30-second TTL is deliberately shorter than that, so a tab
  nobody is looking at falls out of the count. That is the right answer for a
  readout that claims to say who is *looking at* the map.
- **Not attestable.** Anyone can poll with a fresh random token and inflate the
  figure, up to a cap of 4096. There is no defence against that which does not
  involve identifying the caller, and identifying the caller is exactly what
  this is built not to do. A slightly wrong number on a fan server's map is the
  cheaper mistake.
- **Never shown as zero.** The page clamps its own display to at least 1: you
  are reading it, so you are watching it, and the server's figure is a poll
  behind on a fresh load.

## How it is counted

1. On page load the browser mints **128 random bits** and holds them in a
   closure variable. Not a cookie, not `localStorage`, not `sessionStorage` -
   reload and you are a new viewer, because a value that survived a reload
   would be a tracking id.
2. Each 3-second poll of `/map/data` carries it as `?v=<hex>`.
3. The server accepts the value only if it is 8-64 characters of ASCII letters
   and digits, and **never percent-decodes it**. Anything else is refused
   outright and simply not counted.
4. What survives is hashed - SHA-256 over a 32-byte salt generated in memory at
   boot, never written anywhere - and *that digest* is what is stored, against
   the instant it was last seen.
5. Entries expire after 30 seconds. Pruning happens on every read **and** every
   write, so nothing outlives the TTL because a schedule was missed.

The tokened poll is served `no-store`; the token-free form of `/map/data` keeps
its 2-second cache, so third parties can still embed the open feed. A heartbeat
a cache can answer is not a heartbeat.

## Why this cannot answer "who" or "where"

Not "is configured not to" - *cannot*:

| Question | Why it is unanswerable |
| --- | --- |
| Who is watching? | The only per-viewer datum in the process is a salted digest of a number the browser made up seconds ago. It is not derived from anything about the person. |
| Where are they? | No source address is read on this path at all. `PublicMapHandler` never asks the session for its remote endpoint. There is no geolocation, no ASN lookup, nothing. |
| What are they using? | No `User-Agent` parsing, no `Accept-Language`, no screen metrics, no fingerprinting of any kind. |
| Where did they come from? | No `Referer` capture. The responses already set `Referrer-Policy: no-referrer`. |
| Was this person here yesterday? | The salt is regenerated every boot and never touches disk, so a digest is meaningless across a restart - the same rule the map's marker ids already follow. |
| Was this person here an hour ago? | Nothing is retained beyond the TTL, and the recorded table has no per-viewer rows at all. |

### What was deliberately not collected

Named so that adding one is visibly a decision, not an oversight: IP address
(in any form, hashed or otherwise), user agent, referrer, `Accept-Language`,
country/region/city, ASN, screen or timezone metrics, any persistent
identifier, any join to an account or character, and any per-request log line.

### The honest residue

Two things the design does leak, recorded rather than hidden:

- On a quiet map, "1 watching" tells you that you are probably alone, and a
  tick from 1 to 2 tells you somebody arrived. That is the irreducible content
  of any presence number, and it is what was asked for.
- The token appears in the request URL, so a fronting proxy's own access log
  will contain it beside the address that log already records. This adds no
  identifying power - the log line already had the address, and the token is a
  meaningless random value that dies in 30 seconds - but it is worth knowing
  before anyone reasons about proxy logs. We add no log line ourselves.

## What is recorded, and where

**Postgres**, schema **v9**, table `map_viewer_samples`. Two columns:

```sql
sampled_at   TIMESTAMPTZ NOT NULL PRIMARY KEY,  -- floored to the minute
viewer_count INTEGER     NOT NULL CHECK (viewer_count >= 0)
```

That is the whole table. No visitor column, no address column, no session
column, no foreign key to a person - **absent from the schema**, not omitted by
policy, so adding one requires a migration an operator watches go past. The
table is also joined to nothing in either direction, so there is no query that
turns a busy minute into a list of who was there. `ViewerSampleRepositoryTests`
asserts all of that against a real database.

### Postgres rather than a file

A file would have been simpler, and this is not gameplay data. Postgres won
because:

- The login server already opens it at boot and fails loudly without it, so
  this adds no new dependency and no new failure mode.
- History has to **survive redeploys**. The production login server is a
  self-contained binary that gets replaced; a file beside it is a file that
  will one day not be there.
- `MAX`, `GREATEST` and a range scan are what a trend readout needs, and doing
  them over an append-only file means writing a parser.
- The schema is versioned and additive-only, so "there is now one more table"
  is a reviewable, reversible, one-script fact.

### Once a minute, not once a request

This is a **privacy** choice before it is a performance one. A row per request
would be a row every three seconds per viewer, and a series that dense is a
visit log even with no identifying column: arrivals and departures are legible
in where the rows start and stop. Sampling on a fixed cadence that runs whether
or not anybody is there breaks that link - a row exists because a minute
passed, not because somebody arrived - and a visitor who reads the map for 30
seconds may leave no trace at all. That is the correct amount of trace for
somebody who looked at a web page.

It is also free: 1,440 rows a day, two fixed-width columns. There is no
retention policy because there is nothing here to expire.

## What each surface shows

| | Public `/map` | Operator console |
| --- | --- | --- |
| Live count | Chip in the status strip: `N watching` | Stat tile |
| History | 24 h, 10-minute buckets, in the About panel | 30 days, hourly buckets |
| Peak | Last 24 h | 30 days **and** all time |
| Per-viewer detail | none | none - there is none to show |

The console is authenticated so it may legitimately show more, and what that
buys is **length, not resolution**. There is no per-viewer detail behind the
operator login because none exists anywhere. If a future change wants the
console to answer "who", it has to add a column first.

## Where the boundary is enforced

- `ViewerToken` - the shape rule. Nothing that is not 8-64 alphanumerics gets
  in, and the value is never decoded into shape.
- `ViewerCensus` - the salted digest, the TTL, the cap, and the prune-on-every-
  access rule.
- `PublicMapProjection` - `viewers` is on the published whitelist as a
  deliberate admission, with the reasoning in the file.
- `SchemaScripts.V9` - two columns, and no third.
- `ViewerHistory` - the wire shape is counts and nothing else.

And the tests that bite if any of it slips:

- `PublicMapProjectionTests` - the leak corpus now seeds a `viewers` object
  carrying an address and a user agent, so a stats file cannot fill the
  published count or smuggle members alongside it; the exact-root-key list
  makes widening a deliberate act.
- `ViewerCensusTests` - addresses, e-mails, user agents and percent-encoded
  addresses are all refused at the door; what is held is not what was sent;
  fingerprints do not survive a salt change; a viewer and a map marker cannot
  be joined.
- `ViewerHistoryTests` - the payload's keys are pinned.
- `SchemaMigratorTests.The_recorded_viewer_series_has_nowhere_to_put_a_visitor`
  - the column names are checked against a list of everything that could name a
  person.
- `ViewerSampleRepositoryTests` - two columns, right types, no foreign keys in
  or out, and a v8 database upgrades to v9 without losing anything.
