# Worlds Adrift community engineering data — 2026-08-16 snapshot

Immutable research snapshot of the four Google Sheets supplied by the project
owner, plus the WAEngenius calculator and the extra workbook it consumes at
runtime. These are community measurements and reverse-engineered formulas, not
Bossa source data. Use them as evidence and calibration input, and cross-check
against the shipped client/decompile before making a claim of retail fidelity.

## What was preserved

| Source | Local snapshot | Contents | Status |
|---|---|---|---|
| [Worlds Adrift Engine Science](https://docs.google.com/spreadsheets/d/1xXTrHpvVgybTBUw6tl2MXfYke3Ez9VoBlkqszfePRUU/edit#gid=0) | `workbooks/engine-science/` | 49 × 10 combustion-internals material measurements: fuel efficiency, overheat limit, power, internal speed and aggregate increase | Downloaded |
| [Worlds Adrift Wing Science](https://docs.google.com/spreadsheets/d/1gC2QtSINylJelrvFVG37HzfOFpBlyITBOyZteGfZDkI/edit#gid=1568854898) | `workbooks/wing-science/` | Formatted material rankings plus raw aileron and mechanical-internals measurements | Downloaded |
| [Deleted Google Sheet](https://docs.google.com/spreadsheets/d/1cUHxiOTPgDWxLzJOLGpnrfjLxQUY-MnqEGsn58QDdfg/edit#gid=1926050317) | `workbooks/deleted-sheet-http-410.html` | Unknown—the document and every export endpoint return HTTP 410 | Unrecoverable from Google on snapshot date |
| [Panel Resilience by Cannons Shots](https://docs.google.com/spreadsheets/d/18ERiL81Z9nfiAvB-vDfPpnFroqg2puxZ1gtcsf--hAs/edit#gid=0) | `workbooks/panel-resilience-cannon-shots/` | 202 × 7 material/quality cannon-shot and kg-per-shot experiment | Downloaded; source labels itself in-progress and contains empty-shot `#DIV/0!` rows |
| [WAEngenius](https://wolkenreiter.github.io/WAEngenius/dist/index.html) | `waengenius/source/` | Engine schematic/material calculator application and formulas | Downloaded from upstream commit `944277f5c56163d76eedb78f8e16a400b3caf274` |
| [Engine Power Calculator Data](https://docs.google.com/spreadsheets/d/1RBskFnl2LbcOv9Dr_eLbeUkFY8h8ZMVhwmT5opuGnNg/edit#gid=0) | `workbooks/waengenius-engine-power-data/` | Material mass, Q1–Q10 combustion/propeller boosts, engine part families and standard wood WPU | Discovered in WAEngenius source and downloaded |

Every downloaded workbook directory contains:

- `source.xlsx`: untouched Google XLSX export;
- one display-value CSV per worksheet;
- one `.formulas.csv` per worksheet, preserving formulas where present.

`manifest.json` records source IDs, URLs, tab dimensions, hashes, retrieval time
and the WAEngenius upstream commit. `CHECKSUMS.sha256` covers the archived files.

## WAEngenius formulas worth retaining

The calculator's own code is the provenance for its model:

- schematic tier weight-per-unit factors: `1`, `0.875`, `0.7777`, `0.7083`;
- individual engine power:
  `basePower × (1 + combustionMaterialBoost + propellerMaterialBoost)`;
- engine mass: sum of
  `materialUnitMass × tierWpuFactor × componentMaterialCount`;
- predicted ship speed: `50 × sqrt(2 × totalPower / totalMass)`.

The implementation is preserved at `waengenius/source/app/app.js`. Those
constants are historically valuable, but they still need comparison against
the final shipped build before they become Wareborn authority.

## Known limitations

- Google exports contain the state visible on 2026-08-16, not a versioned
  historical revision or proof of which Worlds Adrift patch was measured.
- The panel-resilience workbook openly says `Early Access 0.2.2.1`,
  `In-Progress`, and `New patch is weird`; do not silently treat its blanks or
  error cells as zero.
- The deleted third spreadsheet has no discoverable Wayback snapshot and its ID
  does not appear in public GitHub code search. Its HTTP 410 response is retained
  so the missing source remains explicit.
- Material naming varies (`Aluminum` versus `Aluminium`) and must be normalized
  only in a derived dataset, never by editing this raw snapshot.
- WAEngenius is credited to its original author/project and retains its upstream
  `LICENSE`. The copied application source is for research/provenance; it is not
  presented as Wareborn work.

