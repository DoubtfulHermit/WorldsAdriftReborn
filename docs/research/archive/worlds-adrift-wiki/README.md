# The Worlds Adrift wiki, archived

A snapshot of the community wiki's **raw wikitext**, taken 2026-08-19/20.

## Why this is in the repository

Worlds Adrift's servers went dark in 2019. Almost everything this project has
recovered about how the game *behaved* — as opposed to how its code was
written — comes from three places: the shipped client, the Internet Archive,
and this wiki. Two of those are already fragile. Fandom pages get rewritten,
merged and deleted, and a wiki for a seven-year-dead game is not a durable
record of anything.

It earned its place here. Twice in one day this project built the wrong thing
because nobody thought to ask a community source what a thing was CALLED:

* **Fuel** was implemented per-hull because an agent searched the client for a
  "fuel tank" prefab and found none. The tank is the **Power Generator**.
* **Instrument mounting** was patched to force gauges onto railings, because
  nobody knew **bar pipes** existed. They were in this repo's own
  `valid-icons.txt` on line 873, nine days before a wiki page named them.

The decompile cannot tell you the name of a thing you have not thought to
search for. That is what this archive is for.

## What is here

| path | what |
| --- | --- |
| `pages/*.wikitext` | 425 pages, raw wikitext, one file each. Greppable and diffable, which one JSON blob is not. |
| `allpages.json` | The same content as fetched, title → wikitext. The canonical capture; keep it even though it duplicates `pages/`, because it is the untouched artefact. |
| `html/*.html` | 11 rendered captures, kept because rendered tables sometimes carry data the wikitext templates hide. |

## How to treat what is in it

**This is the WEAKEST evidence class this project uses.** Label anything drawn
from it `WIKI`, and confirm it against the decompile or the shipped assets
before acting. Its job is to tell you *what to look for*; the client tells you
*how it works*.

Two specific hazards, both met in practice:

* **It is not date-stamped per claim.** Alpha-era and final-release balance sit
  side by side. Atlas core lift is the known example: staff quoted
  "two in the second biome for 250KG each" in April 2017, against the final
  scheme of 1000 kg base plus upgrades to 6000 kg. Date-tag anything numeric.
* **Absence here proves nothing.** The 2017 Sails page had a *Weight* heading
  with nothing under it, later deleted — an empty slot, not evidence the
  statistic did not exist.

## Provenance

Fetched from the Fandom wiki via its API. Some pages also exist in earlier
Gamepedia form on the Internet Archive and differ materially; where a
difference mattered it is recorded in `docs/plans/feature-roadmap.md` and
`docs/plans/reality-inventory.md` rather than reconciled here. This archive is
deliberately a capture, not an edit — nothing in `pages/` has been corrected,
including the wiki's own errors.
