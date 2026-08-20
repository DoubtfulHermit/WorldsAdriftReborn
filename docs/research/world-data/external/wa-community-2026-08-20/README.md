# Worlds Adrift community engineering data — 2026-08-20 snapshot

Second research snapshot. It **complements** `wa-community-2026-08-16`; it does not
replace it and nothing in that directory was touched. Everything here was
retrieved by external search on 2026-08-20 and is community measurement or
community wiki content — not Bossa source data. Cross-check against the shipped
client/decompile before making a claim of retail fidelity.

Two headline results:

1. The HTTP-410 spreadsheet from the previous snapshot is **identified and
   partially recovered**. It was **"Worlds Adrift Cannon Science"**, internally
   titled *Cannon Materials Sheet v1.0 - Beta version 0.1.0 by Fallout*. Its
   `Formatted` tab is recovered verbatim from the Wayback Machine.
2. A **sixth workbook that was not in the previous snapshot is alive** and has
   been downloaded: the *Engine Materials Sheet* by Gouki, self-labelled
   *Worlds Adrift Closed Beta 0.1.3.3*. It carries a per-material **Casing
   Health** column — the first per-material durability ranking we hold outside
   the cannon-shot workbook.

## What was preserved

| Source | Local snapshot | Contents | Status |
|---|---|---|---|
| [Worlds Adrift Cannon Science](https://docs.google.com/spreadsheets/d/1cUHxiOTPgDWxLzJOLGpnrfjLxQUY-MnqEGsn58QDdfg/edit#gid=1926050317) | `workbooks/cannon-science/` | 5 raw Wayback captures of the `/edit` page (2019, 2020, 2023) + the CDX index | Recovered from Wayback; Google still returns HTTP 410 |
| [Engine Materials Sheet](https://docs.google.com/spreadsheets/d/1lUYA9dYvxLYVlJUQ6GRrWMl-0ptKT3ASj3-rnhmYr3U/edit) | `workbooks/engine-materials-sheet/` | 46 × 18 `Formatted` + three RAW measurement tabs | Downloaded live (HTTP 200) |
| [Worlds Adrift Wiki: Metal](https://worldsadrift.gamepedia.com/Metal) | `wiki/gamepedia-Metal-wayback-2018.html` | 15-metal panel mass table, rarity, removed-metal history | Wayback (gamepedia dead, Fandom 403) |
| [Worlds Adrift Wiki: Wood](https://worldsadrift.gamepedia.com/Wood) | `wiki/gamepedia-Wood-wayback-2018.html` | 8-wood mass table, removed-wood history | Wayback |
| [Worlds Adrift Wiki: Atlas Sky Core](https://worldsadrift.gamepedia.com/Atlas_Sky_Core) | `wiki/gamepedia-Atlas_Sky_Core-wayback-2018.html` | Base 1000 kg lift, 8 core extensions, 12-metal × Q1–Q10 lift table | Wayback |
| [Advanced Resource Stats (Falagar, 2017-07-06)](https://www.magicgameworld.com/worlds-adrift-advanced-resource-stats/) | `guides/magicgameworld-advanced-resource-stats.html` | 12-metal + 8-wood weight-per-unit | Downloaded live |

`manifest.json` records source IDs, URLs, live/archive status, retrieval time,
tab dimensions and hashes, plus an explicit `attemptedAndFailed` list.
`CHECKSUMS.sha256` covers every archived file.

## File-shape note — one file here is DERIVED, not as-retrieved

Everything in this directory is the untouched bytes as retrieved, with a single
clearly-named exception:

- `workbooks/cannon-science/00-Formatted.derived.csv`

There is no Google export of the deleted sheet to preserve. The Wayback capture
is an HTML page, and the recovered table was parsed out of it *by this session*.
The `.derived.csv` suffix marks it as our reconstruction. The authoritative
bytes are `wayback-20190602112029-edit.html`. The 2019, 2020 and 2023 captures
parse to identical data cells, which is the only corroboration the CSV has.

The `engine-materials-sheet` directory follows the 2026-08-16 convention exactly:
`source.xlsx`, one display-value CSV per worksheet, one `.formulas.csv` per
worksheet.

## Recovered: Cannon Science `Formatted` tab

Verbatim from the 2019-06-02 Wayback capture. Percentages are normalised against
the best performer in each slot; the author's stated method was to craft one
cannon per material with all other slots held at the same Q10 metal, subtract the
blueprint's base stat (51 power), and rank the remainder.

Barrel — Material, Power, Overheat Limit, Weight:

```
Tin        75.0%   53.8%   0.241 kg
Bronze     75.0%   53.8%   0.297 kg
Lead       75.0%   61.5%   0.425 kg
Aluminium  78.5%   53.8%   0.184 kg
Iron       78.6%   69.2%   0.269 kg
Nickel     82.1%   61.5%   0.326 kg
Copper     82.1%   76.9%   0.354 kg
Silver     85.7%   69.2%   0.390 kg
Titanium   89.2%   53.8%   0.213 kg
Gold       92.9%   76.9%   0.489 kg
Tungsten   97.5%  100.0%   0.524 kg
Steel     100.0%   61.5%   0.283 kg
```

Firing Mechanism — Material, Power, Overheat Limit, Rate of Fire, Weight:

```
Tin        28.6%   21.1%   73.1%   0.241 kg
Iron       35.7%   47.4%   73.1%   0.269 kg
Titanium   35.7%   26.3%   73.1%   0.213 kg
Copper     42.9%   63.2%   69.2%   0.354 kg
Gold       50.0%   63.2%   84.6%   0.489 kg
Nickel     57.1%   42.1%   88.5%   0.326 kg
Tungsten   57.1%  100.0%  100.0%   0.524 kg
Steel      60.7%   36.8%   76.9%   0.283 kg
Silver     67.9%   52.6%   76.9%   0.390 kg
Lead       75.0%   42.1%   96.2%   0.425 kg
Bronze     89.3%   21.1%   69.2%   0.297 kg
Aluminium 100.0%   21.1%   69.2%   0.184 kg
```

Ammo Loader — Material, Rate of Fire, Capacity, Weight:

```
Tin        45.2%   44.4%   0.241 kg
Iron       48.4%   55.6%   0.269 kg
Titanium   48.4%   44.4%   0.213 kg
Copper     54.8%   33.3%   0.354 kg
Gold       61.3%   66.7%   0.489 kg
Nickel     64.5%   77.8%   0.326 kg
Tungsten   64.5%  100.0%   0.524 kg
Steel      67.7%   55.6%   0.283 kg
Silver     74.2%   55.6%   0.390 kg
Lead       80.6%   88.9%   0.425 kg
Bronze     90.3%   44.4%   0.298 kg
Aluminium 100.0%   44.4%   0.184 kg
```

The `kg` column is a **per-cannon-component** weight, not the panel
weight-per-unit. Bronze appears as `0.297 kg` in two slots and `0.298 kg` in the
third; that inconsistency is in the source and has been preserved.

## Recovered: per-material Casing **Health** (durability)

From the Engine Materials Sheet `Formatted` tab, rows 33–46. Normalised 0–1
against the best performer, same method as the rest of that workbook. Patch
label on the sheet: **Closed Beta 0.1.3.3**.

```
Material    Health   Mass Efficiency
Copper      0.667    0.185
Aluminium   0.694    0.241
Bronze      0.694    0.206
Tin         0.722    0.231
Titanium    0.722    0.241
Iron        0.750    0.231
Silver      0.778    0.207
Steel       0.778    0.235
Gold        0.833    0.199
Nickel      0.889    0.255
Lead        0.972    0.248
Tungsten    1.000    0.230
```

This is a **ranking, not an absolute hit-point value**, and it covers metals
only — no wood durability was found in any source retrieved for this snapshot.

## Mass: three mutually inconsistent tables

The single most important finding is that there is **no one canonical
weight-per-unit table**. Three independent sources disagree, and they disagree in
ways consistent with rebalancing across patches — Gold and Tungsten even swap
order. Any Wareborn mass model must pick a target patch and say so.

Metals (kg per unit):

| Metal | Falagar 2017-07-06 | Wiki panel WPU (2018 capture) |
|---|---|---|
| Aluminium | 0.2600 | 0.33 |
| Titanium | 0.3000 | 0.35 |
| Tin | 0.3400 | 0.38 |
| Iron | 0.3800 | 0.39 |
| Steel | 0.4000 | 0.50 |
| Bronze | 0.4200 | 0.42 |
| Nickel | 0.4600 | 0.43 |
| Copper | 0.5000 | 0.55 |
| Silver | 0.5500 | 0.66 |
| Lead | 0.6000 | 0.56 |
| Gold | 0.6900 | 0.73 |
| Tungsten | 0.7400 | 0.70 |
| Orthite | — | 0.43 |
| Epilar | — | 0.46 |
| Eternium | — | 0.50 |

Woods (kg per unit):

| Wood | Falagar 2017-07-06 | Wiki (2018 capture) | Local `Standard WPU` tab (2026-08-16 snapshot) |
|---|---|---|---|
| Cedar | 0.2000 | 0.13 | 0.20 |
| Hemlock | 0.2200 | 0.15 | 0.23 |
| Chestnut | 0.2500 | 0.17 | 0.25 |
| Elm | 0.2800 | 0.18 | 0.29 |
| Birch | 0.3200 | 0.20 | 0.32 |
| Ash | 0.3500 | 0.22 | 0.35 |
| Oak | 0.3800 | 0.23 | 0.38 |
| Palm | 0.4100 | 0.25 | 0.41 |

The `Standard WPU` tab already in `wa-community-2026-08-16` matches Falagar to
within 0.01 on every wood, so those two are effectively one source, not two.

The wiki Metal page states explicitly: *"All of the following data is a specific
weight per unit for panels. Each component is believed to have a different weight
per unit, but the order of weight remains the same."* That is the likeliest
explanation for the cannon sheet's third, lower set of `kg` figures.

Three metals — **Orthite, Epilar, Eternium** — appear on the wiki but in none of
the community spreadsheets and in none of the material lists Wareborn currently
uses. Removed materials are also documented: Magnesium 0.13, Palladium 0.87,
Platinum 1.64 kg, plus the woods Ironwood, Mahogany, Ebony and Maple, all last
seen in Alpha 5.3.

## Quality Q1–Q10

The wiki Metal page states quality gives *"a higher statistic boost to the part
you are making, **without any additional cost of weight**"* — i.e. quality scales
component stats but **not** mass. No source retrieved gives a general
quality→durability formula.

The one concrete quality curve found is for Atlas Core lift, from the wiki page,
credited to `Demodraco#0118`. It is **linear in quality**, and the published
numbers fit one formula exactly:

```
lift(metal, Q) = 1000 + (Q + 10) × rate(metal)          for Q in 1..10
```

so Q10 lift is always `1000 + 20 × rate` and the stated max increase is
`20 × rate`. This reproduces every published cell for eleven of the twelve
metals. **Iron is the sole exception**: its listed rate of 3 predicts Q1 = 1033
and Q10 = 1060, but the sheet prints 1034 and 1061 — a uniform +1 offset across
its whole row, and a max-increase of 61 rather than 60. Preserved as published;
do not silently repair it.

Verbatim (kg/quality level, Q1 lift, Q10 lift, max increase, % of base):

```
Aluminum   6     1066   1120   120   12.00%
Titanium   1     1011   1020    20    2.00%
Tin        4.5   1049.5 1090    90    9.00%
Iron       3     1034   1061    61    6.10%
Steel      1.5   1016.5 1030    30    3.00%
Bronze     2.5   1027.5 1050    50    5.00%
Nickel     4     1044   1080    80    8.00%
Copper     7.5   1082.5 1150   150   15.00%
Silver     8     1088   1160   160   16.00%
Lead       2     1022   1040    40    4.00%
Gold       8.5   1093.5 1170   170   17.00%
Tungsten   3.5   1038.5 1070    70    7.00%
```

## Ship lift

From the wiki Atlas Sky Core page: a bare Atlas Sky Core supports **1000 kg**,
"more if proper materials were used". Eight extensions each state a *minimum*
additional lift in their in-game tooltip: Atlas Enhancer +400 kg, Generator
+400 kg, Air Filter +600 kg, Coolant System +600 kg, Stabiliser +600 kg,
Computer +800 kg, plus Circuitry Network and Efficiency Module (tooltips not
captured in the extracted region). No formula for total ship mass, drag, or the
lift/mass relationship was found.

## Known limitations

- The three RAW tabs of the Cannon Science sheet (`Barrel RAW`,
  `Firing Mechanism RAW`, `Ammo Loader RAW`) are **permanently lost**. Wayback
  renders only the sheet's active tab into noscript HTML, and no per-gid,
  `/htmlview`, `/pubhtml` or `/export` capture of that document exists in the CDX
  index. The author's own note says the raw screenshots lived in those tabs.
- The Cannon Science sheet is dated only as *"Beta version 0.1.0"*. Our three
  durability-adjacent sources are now from three different patches: Cannon
  Science `Beta 0.1.0`, Engine Materials `Closed Beta 0.1.3.3`, Panel Resilience
  `Early Access 0.2.2.1`. Do not merge their numbers into one table.
- The wiki captures carry an explicit "needs to be revised and updated to the
  current game version" banner. Their mass numbers are a snapshot of an unknown
  patch, not a final-build authority.
- The Falagar article is a 2017 post served from a live 2026 site, so the
  archived HTML is wrapped in current site chrome. Only the article body is
  evidence.
- The Skycore Science Graph published spreadsheet behind the wiki's interactive
  chart could not be exported (sign-in wall); only the wiki's HTML rendering of
  the same table is held here.
- Material naming still varies (`Aluminum` vs `Aluminium`) across sources and
  must be normalised only in a derived dataset, never by editing this snapshot.
