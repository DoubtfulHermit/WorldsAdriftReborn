# Worlds Adrift community engineering data — 2026-08-20**b** snapshot

Third research snapshot, retrieved by `feat/massalign`. It **complements**
`wa-community-2026-08-16` and `wa-community-2026-08-20`; it replaces neither, and
**nothing in either of those directories was touched** — both are immutable and
checksum-verified.

The `b` suffix is because the `2026-08-20` snapshot was taken earlier the *same day*.
Same date, different session, separate directory.

Ten sources. **Every fetch returned HTTP 200** — nothing here was walled, rate-limited
or partially recovered, which is unusual for this lineage and is exactly why it was
worth doing now.

## ⚠ TWO PATCH EPOCHS LIVE IN THIS DIRECTORY. DO NOT MIX THEM.

|  | **final era** (what WAReborn ships) | **calculator era** |
|---|---|---|
| held here by | `sciencesheet.xls` | Metal Chart, Fureniku |
| aluminium | 0.33 | 0.26 |
| iron | 0.39 | 0.38 |
| **tungsten** | **0.70** | **0.80** (back-solved correction: 0.74) |
| **gold** | **0.73** | **0.69** |
| ordering | gold **>** tungsten | tungsten **>** gold |
| orthite / epilar / eternium | present | absent |

These are **not competing measurements of one truth**. They are different balance
passes, each correct in its own patch. Picking the best-evidenced value per material
across them produces a ranking **no version of the game ever had**.

The rule, and it is enforced by a unit test
(`Multiplayer/Materials/MaterialMassEpoch.cs`, `MaterialMassEpochTests`):

> **PICK AN EPOCH AND BE INTERNALLY CONSISTENT. NEVER MIX ROWS ACROSS EPOCHS.**

## Why this snapshot exists

`findings-material-mass.md` §1.2 records that one sheet in this lineage is already
**HTTP 410 and permanently lost**, with three of its four tabs unrecoverable even from
the Wayback Machine. Every source below is one owner-deletion from the same fate. So
they are **copied, not linked**.

`sciencesheet.xls` was fetched **first**, because it is the source of the mass table the
server actually ships.

## What was preserved

| Source | Local path | Contents | Epoch |
|---|---|---|---|
| **Engine Science `sciencesheet.xls`** | `workbooks/engine-science-sciencesheet-xls/` | **the final-era mass table, 15 metals** + resilience boost + cooling factor | **final** |
| Metal Chart | `workbooks/metal-chart-endurance/` | per-metal `ENDURANCE`, `ENDUR/WEIGHT`, weights, usage notes | calculator |
| Node Resilience Sheet | `workbooks/node-resilience-sheet/` | Q1–Q10 pulses-to-falloff per metal | — |
| Fureniku weight/lift (2 gids) | `workbooks/fureniku-weight-lift/` | unit weight **and** atlas lift, in one table | mixed — see below |
| Atlas lift table | `workbooks/sheet-1fPpHFB3…/` | Q1–Q10 lift per metal | — |
| Cannon barrel effectiveness | `workbooks/sheet-1dO2jRfS…/` | power / overheat per material | — |
| Alerion effectiveness | `workbooks/sheet-1Psd3FAx…/` | power / pivot speed per material | — |
| Engine part catalogue | `workbooks/sheet-1HWwusbR…/` | part, material type, tier, main stat | — |
| Ship part catalogue | `workbooks/sheet-17WPEuZd…/` | part, material type, tier, body material | — |

## Three results worth reading the manifest for

1. **The table we ship now has two independent witnesses.** `sciencesheet.xls`'s
   `weight` column reproduces the wiki Metal page **15 of 15 metals, zero residual** —
   including orthite, epilar and eternium, which appear in no community weight table.
   Before this, the wiki was the sole source for the numbers the server serves.

2. **The epoch trap, caught in the wild.** Fureniku's sheet publishes calculator-era
   weights (tungsten 0.80) **beside** the atlas lift rates we ship. That is also the
   provenance of our own lift column: **our catalogue is knowingly cross-epoch — mass
   from the final era, lift from the calculator era.** Defensible, since lift is a
   different quantity and three unrelated sources publish the same rates, but now
   written down so it reads as deliberate rather than as the bug above. Both new copies
   reproduce iron's anomalous `+1` max lift (61, not 60), confirming it as a source
   quirk and not our transcription error.

3. **Two new durability tables, deliberately unwired.** `sciencesheet.xls`'s resilience
   boost (every value an exact multiple of `1/78`) and the Metal Chart's `ENDURANCE`.
   They **disagree** with each other and with the Update 29.4 table — different epochs.
   Nothing was wired to them, because the blocking work is still a damage model, not
   better numbers (`findings-material-mass.md` §6.4).

## Derived files

One derived file exists and it is labelled as such in the manifest:
`workbooks/engine-science-sciencesheet-xls/00-Sheet1.derived.csv`, produced this session
by dumping `Sheet1` of the `.xls` with `xlrd`. **It is not an as-retrieved export.**
Verify against the binary before trusting it.

The `.xls` itself is a CloudConvert re-encode of an upstream Google Sheet, per its own
OLE metadata (`Last Saved By: cloudconvert_21`, 2025-05-22). Preserved exactly as
served.

## Caveat on completeness

`…/export?format=csv` returns only a workbook's **default tab** unless a `gid` is given.
Where only one tab was fetched, that is all this snapshot holds — the other tabs of
those workbooks are **not** archived and may still be lost.

## Verifying

```sh
cd docs/research/world-data/external/wa-community-2026-08-20b
sha256sum -c CHECKSUMS.sha256
```
