# PER-MATERIAL MASS, DENSITY AND DURABILITY — sources, reconciliation, and what we actually ship

**2026-08-20. Branch `research/material-mass`, off `research/storm-sky` @ `83d461f`. Research and data consolidation only — no server change, no client mod, production untouched.**

Machine-readable companion: **`docs/research/gathering/data/material-properties.json`** — 16 top-level sections, 23 materials, 9 sources. That file is the authority for values; this document is the argument and the provenance.

Companion research: `findings-storm-walls.md` (the wall force model consumes ship mass) and `findings-storm-sky.md`.

> **UPDATED 2026-08-20 by `feat/massalign`.** New material added as §2.7, §7 and §10.
> Two claims in the original text were wrong and are struck through where they stand.

---

## ⚠ THE EPOCH TRAP — READ THIS BEFORE CHANGING ANY MASS NUMBER

**The surviving weight tables are DIFFERENT PATCH EPOCHS, not competing measurements
of one truth.** Each was correct when it was measured. Their material orderings
genuinely differ — the final era puts gold above tungsten, the calculator era puts
tungsten above gold. So:

> **PICK AN EPOCH AND BE INTERNALLY CONSISTENT. NEVER MIX ROWS ACROSS EPOCHS.**

Averaging, or cherry-picking the best-evidenced value per material, produces a
material ranking **no version of the game ever had** — worse than either table alone.

**THE SPECIFIC MISTAKE THE NEXT READER WILL MAKE.** §2.5 below back-solves the deleted
cannon sheet and recovers **tungsten = 0.74**. That is an excellent result, it is
correct, and **it belongs to the calculator era**, where it corrects that sheet's 0.80.
It says *nothing* about the final era, which independently publishes **0.70**. Importing
0.74 into our catalogue would leave 22 final-era rows and one calculator-era row.

**We ship the final era.** That decision, the reasons for it, and a mechanical guard
against exactly this mistake now live in
`WorldsAdriftRebornGameServer.Multiplayer/Materials/MaterialMassEpoch.cs`, unit-tested
by `MaterialMassEpochTests`. The 0.74 and 0.80 values are enumerated there as
*forbidden* so the mistake fails a test with a message naming the trap, rather than
shipping.

| | final era (**shipped**) | calculator era |
|---|---|---|
| tungsten | **0.70** | 0.80, corrected to 0.74 |
| gold | **0.73** | 0.69 |
| ordering | gold **>** tungsten | tungsten **>** gold |
| cedar / chestnut | **0.13 / 0.17** | 0.15 / 0.19 |
| orthite, epilar, eternium | **present** | absent entirely |
| quantity | **panel** weight-per-unit | **engine-component** WPU, tier-scaled by `[1, 0.875, 0.7777, 0.7083]` |

---

## 0. THE ANSWER IN SIX LINES

1. **The research is in two places, and that is the whole story.** There are the community sheets everyone remembers — *and* a complete, already-wired **per-material mass model in our own server**, built from a different table. They disagree.
2. **Per-material ship mass is not a future feature. It is live and flying today**, via `1257 ParentingMassAdderState` → `HullMassCalculator.HullMassKg`.
3. **The shipped client carries no mass and no durability at all** — PROVED byte-exact. Both are server-authoritative. The community numbers are therefore neither corroborated nor contradicted by the client; it never carried them.
4. **`1258` is `ShipLiftState` — LIFT, not mass.** The flat 1,000,000 is a lift seed. Three statements in the handover, the roadmap and this project's own source comments are wrong about this.
5. **The 1e6 seed never protected anything.** What keeps flight working is a Harmony patch pinning `AtlasMultiplier` to `1f`. **The real cliff is lift, not mass.**
6. **Durability has real data, no mechanism, and its proxies contradict each other.** A damage model — not better numbers — is the blocking work. The **Update 29.4 resilience table is now recovered** (§6.3), pulled out of a published chart's embedded JSON; it is metals-only, and **no wood durability figure exists in any source found.**

---

## 1. SOURCES — 11 in repo, 3 newly retrieved

### 1.1 Already in the repo

| source | what it holds | provenance |
|---|---|---|
| `world-data/external/wa-community-2026-08-16/workbooks/waengenius-engine-power-data/` | material mass, Q1–Q10 boosts, engine families, standard wood WPU | COMMUNITY-MEASURED |
| …`/panel-resilience-cannon-shots/` | 202 × 7 cannon-shot / kg-per-shot experiment | COMMUNITY-MEASURED |
| …`/engine-science/` | 49 × 10 combustion internals: efficiency, overheat, power, speed | COMMUNITY-MEASURED |
| …`/wing-science/` | material rankings, **raw aileron + mechanical-internals kg readings** | COMMUNITY-MEASURED |
| …`/waengenius/source/app/app.js` | the calculator's own model and tier WPU factors | COMMUNITY-MODELLED |
| **`03-Standard-WPU` tab** *(found this session)* | **a fourth wood column** — not the one WAEngenius reads | COMMUNITY-MEASURED |
| **`MaterialCatalog.cs`** *(found this session)* | our server's shipped material table | PROVED (ours) |
| **wiki `Atlas_Sky_Core`** *(found this session)* | per-metal lift table | WIKI |
| **`Resilience.wikitext`**, **`Panels.wikitext`** *(found this session)* | resilience prose; per-panel bar values | WIKI |
| `gathering/data/materials.json` | item catalogue — **no mass or durability fields** | ours |
| `Wood.wikitext`, `Metal.wikitext`, `Metals.wikitext`, `Update_*.wikitext` | the wiki mass tables and the rebalance history | WIKI |

### 1.2 Newly retrieved and archived

New dated snapshot **`wa-community-2026-08-20/`**, with its own manifest and checksums. **The 2026-08-16 snapshot was not touched** — it is immutable and checksummed, per the hard rules.

- **The deleted sheet is partially recovered.** The wing-science workbook names it in its own credits: *"based on Fallout's Cannon Materials Sheet"*. With a name to search on, the `Formatted` tab came back off the Wayback Machine — *"Worlds Adrift Cannon Science / Cannon Materials Sheet v1.0 - Beta version 0.1.0 by Fallout"*, per-cannon-component kg for 12 metals. **Its three RAW tabs are NOT recoverable** — Wayback rendered only the active tab, and Google still returns 410.
- **A sixth workbook nobody had archived**, still live: Gouki's *Engine Materials Sheet*.
- **An independent 2017 weight table** — Falagar, magicgameworld.com. This one turned out to be decisive (§2.3).

### 1.3 Archived 2026-08-20 by `feat/massalign` — snapshot `wa-community-2026-08-20b`

A **third** snapshot directory, named `-20b` because the `-20` snapshot was taken
earlier the same day and is immutable. **Neither the 2026-08-16 nor the 2026-08-20
snapshot was touched.** Ten sources, all HTTP 200, own manifest and `CHECKSUMS.sha256`.

| source | what it holds | why it mattered |
|---|---|---|
| **`sciencesheet.xls`** (Worlds-Adrift-Engine-Science repo) | the final-era weight table, 15 metals, + resilience boost + cooling factor | **the source of the table we ship** — archived first, and it independently corroborates the wiki (§2.7) |
| "Metal Chart" | per-metal `ENDURANCE` + calculator-era weights | a durability table we did not hold |
| "Node Resilience Sheet" (Ryu_Khan / Simayi) | Q1–Q10 pulses-to-falloff per metal | confirms the flat 60-resilience large panel |
| Fureniku weight/lift ×2 gids | calculator-era unit weight **and** atlas lift, in one table | the epoch trap, caught in the wild (§2.7) |
| `1fPpHFB3…` | the atlas lift table, Q1–Q10 | third independent copy of the lift rates we ship |
| `1dO2jRfS…`, `1Psd3FAx…` | cannon-barrel and alerion material effectiveness | unwired |
| `1HWwusbR…`, `17WPEuZd…` | engine / ship-part catalogues by tier | unwired |

**Retrieval note, worth keeping:** `worldsadrift.fandom.com` HTML returns **403** to
curl. `…/api.php?action=parse&page=X&format=json&prop=wikitext` returns **200** and is
the way in. One sheet in this lineage is already **410 and gone forever** (§1.2); the
rest are one owner-deletion away, which is why they are copied rather than linked.

---

## 2. MASS — the consolidated picture and the honest disagreement

Full per-material values live in `material-properties.json`. What matters here is the structure of the evidence.

**Woods agree on ordering everywhere:** cedar < hemlock < chestnut < elm < birch < ash < oak < palm.
**Metals do not.** The wiki puts tungsten lighter than gold; the community sheets put it heavier.

Three things established this session that nobody had:

### 2.1 Two community "sheets" are one table

`Large Panel Kg ÷ 40` reproduces the WAEngenius weight column **exactly — 19 of 19 shared materials, zero residual.** A "Large Panel" is 40 units. Falagar 2017 likewise matches the `03-Standard-WPU` tab within 0.01 on all 8 woods.

**So four apparent wood tables collapse to three, and the panel sheet is not independent corroboration of WAEngenius — it is the same numbers ×40.** Any argument that cites both as agreeing sources is double-counting.

### 2.2 The wing-science raw readings had never been fitted

That workbook contains **raw in-game kilogram readings**. Least squares through Bronze:

| table | RMS error vs in-game readings |
|---|---|
| community | **1.03 kg** |
| wiki Alpha 6 | 3.50 kg |

**Honest caveat, and it matters:** the aileron and mechanical-internals delta series are **identical**. That is one measurement replicated, not two independent ones, and it is integer-rounded. Treat the fit as suggestive, not decisive.

### 2.3 Tungsten is settled — and §2.5 then settled it decisively

The wing fit reproduced every metal within a kilogram **except tungsten**, off by ~4 kg in both slots, implying **~0.73**. Falagar — retrieved afterwards, independently — says **0.74**. The wiki says **0.70**.

Three sources cluster at 0.70–0.74. The sheets' **0.80 is the outlier** and looks like an error in the WAEngenius column, inherited by the panel sheet through the shared ×40 table (§2.1). **§2.5 closes this with a fourth, independent line of evidence: 0.74.**

### 2.4 The recovered cannon sheet collapses the "third table"

The deleted sheet (§1.2) turns out not to be a third mass table at all.

Dividing the recovered cannon weights by Falagar's WPU gives a **constant 0.70833** — min 0.70714, max 0.71000, n = 12. That is exactly the WAEngenius **schematic tier factor 0.7083** already in our notes. `Falagar_WPU × 0.7083` reproduces **11 of 12** published cannon weights to three decimals.

So the cannon sheet is the *same* mass table at a different schematic tier. **Three unrelated authors agree**, and the count of genuinely independent mass tables drops again.

### 2.5 Back-solving the tier factor recovers a clean WPU table — VERIFIED

Inverting the relation — `WPU = cannon_kg / 0.70833` — over all 12 barrel weights:

| metal | back-solved WPU | | metal | back-solved WPU |
|---|---|---|---|---|
| Aluminium | 0.2598 | | Nickel | 0.4602 |
| Titanium | 0.3007 | | Copper | 0.4998 |
| Tin | 0.3402 | | Silver | 0.5506 |
| Iron | 0.3798 | | Lead | 0.6000 |
| Steel | 0.3995 | | Gold | 0.6904 |
| Bronze | 0.4193 | | **Tungsten** | **0.7398** |

**Every one lands on a round hundredth**, most on a round fiftieth. A ratio fitted to fit *one* number would not do that twelve times. This is a genuine recovery of the underlying table, not a curve fit.

Cross-checking tungsten against the three candidates — only one produces a tier factor consistent with the other eleven metals:

| candidate WPU | implied tier factor | source |
|---|---|---|
| 0.70 | 0.74857 | wiki |
| **0.74** | **0.70811** | **Falagar — consistent** |
| 0.80 | 0.65500 | WAEngenius / panel |

**Tungsten is 0.74.** The back-solve also puts **gold at 0.69, below tungsten** — Falagar's ordering, *contradicting* the wiki, which swaps them.

*Verified independently in this repo by arithmetic on the archived `00-Formatted.derived.csv`, not taken on report.*

### 2.6 What to trust, and why not to average

> **Revised after §2.4–2.5.** The original recommendation below was "keep the wiki table",
> on the grounds that it was the only RECOVERED source and the only one covering three
> metals. The first half no longer holds: the cannon back-solve is now the
> **best-corroborated metal mass evidence we have** — three unrelated authors, a clean
> twelve-value table, internally consistent to a round hundredth. It contradicts the wiki
> on tungsten (0.74 vs 0.70) and on the gold/tungsten ordering.
>
> **The tables are not competing measurements of one truth — they are different patch
> epochs**, and the orderings genuinely changed between them. So the real rule is:
> **pick an epoch and be internally consistent; never mix rows across tables.**
> - For **metals**, the Falagar/cannon-derived table is the better-evidenced one.
> - The wiki remains the **only** source of mass for **orthite, epilar and eternium**, so
>   an all-metals table still cannot be built from Falagar alone.
> - ~~Our shipped values (cedar 0.15, chestnut 0.19) match **neither** table — wiki says
>   0.13/0.17, Falagar/repo say 0.20/0.25. Ours sit between. That is worth a deliberate
>   decision rather than leaving it as an accident.~~
>   **FALSE — retracted 2026-08-20, see §2.7.** We ship cedar **0.13** and chestnut
>   **0.17**, i.e. the wiki table exactly, and always have. 0.15/0.19 is the
>   *WAEngenius* `WEIGHT` column, which we do **not** ship. Verified mechanically,
>   23 of 23 rows. This retraction matters because the false claim propagated into a
>   work brief that asked for a migration which had, in fact, already happened.

**Original recommendation, superseded in part:** keep the wiki table. It is the only RECOVERED source, and the only one covering **orthite, epilar and eternium** — adopting the community table would leave three metals with no number at all.

**Do not average the tables.** The orderings differ, so an average produces a material ranking **no version of the game ever had**.

Patch notes explain the drift but do not date it. *"Wood weight reduced across the board by 15%"* (Beta 0.2.0.5) fits the wood step in direction for all 8 woods, mean ratio 0.846 against a stated 0.85 — but reproduces only 4 of 8 under rounding. **Dating is unresolved and recorded as such:** Update 31 reduced copper, silver and gold, and the community table is lower on exactly those three (suggesting it is *later*) — yet it lacks the three metals Update 29 introduced (suggesting *earlier*). Both cannot be true. Left open rather than guessed.

### 2.7 WHAT WE ACTUALLY SHIP — measured, not assumed (2026-08-20)

**The headline: the final-era table was already shipped, in full, before this session
started. The intended migration was a no-op.**

`MaterialCatalog.cs` was parsed and every `mass:` argument compared against the wiki
table and against `material-properties.json`. **23 of 23 retail materials match
exactly**, metals and woods, including orthite, epilar and eternium. There is exactly
**one** mass table in the codebase — no duplicate in `ShipMaterial.cs` (it declares the
property, it does not carry values) and none in `itemData.json` (no mass field at all).
`material-properties.json` already recorded this correctly in its
`mass_used_by_wareborn_server` field and in the `wiki_alpha6` source note.

So the honest result of this work is a **verification, plus provenance repair and a
guard** — not a value change. **Every hull in the live world flies today exactly as it
did yesterday.** See §10 for the measured consequences.

#### A third, independent copy of the table — `sciencesheet.xls`

Newly archived this session (`wa-community-2026-08-20b`, §1.3): the `weight` column of
`sciencesheet.xls` from the *Worlds-Adrift-Engine-Science* repository is the final-era
table **verbatim, all 15 metals**:

```
aluminum 0.33  titanium 0.35  tin 0.38  iron 0.39  bronze 0.42  nickel 0.43
orthite  0.43  epilar   0.46  steel 0.50  eternium 0.50  copper 0.55
lead     0.56  silver   0.66  tungsten 0.70  gold 0.73
```

This matters for three reasons:

1. It is **independent of the wiki**, so the table we ship now has **two** unrelated
   sources agreeing row-for-row with zero residual. Previously the wiki was the sole
   witness.
2. It **carries orthite, epilar and eternium**, confirming these are not a wiki
   invention.
3. It **confirms tungsten 0.70 and gold 0.73** in this epoch — independently of the
   cannon back-solve, and *disagreeing* with it, exactly as the epoch model predicts.

#### The epoch trap caught in the wild — our own lift column

Fureniku's sheet (`gid-720499601`, newly archived) publishes a `Unit Weight` column
**and** the atlas `KG per Quality` column **side by side in one table**. Its weights are
calculator era (titanium 0.30, iron 0.38, **tungsten 0.80**, aluminium 0.26) while its
lift rates are exactly the ones we ship (titanium 1, steel 1.5, lead 2, bronze 2.5,
iron 3, tungsten 3.5, nickel 4, tin 4.5, aluminium 6, copper 7.5).

**So our catalogue is already cross-epoch: mass from the final era, atlas lift from the
calculator era.** This is defensible — lift is a *different quantity*, the wiki's Atlas
Core page publishes the same rates, and a third copy turned up in
`sheet-1fPpHFB3…` — but it was not previously written down, and a reader who
discovers it could easily mistake it for the bug this document warns against.
**Recorded here so it is a known, deliberate crossing rather than a discovery.**
Iron's anomalous `+1` (max lift 61, not 60) reproduces in both newly archived copies,
confirming it as a source quirk and not a transcription error on our side.

#### Newly held durability data — deliberately NOT wired

Two per-metal durability tables arrived with this snapshot and **nothing was done with
them**, because §6.4 still holds: there is no damage model to attach them to.

- **`sciencesheet.xls` `resilience boost`** — every value is an exact multiple of
  `1/78` (tungsten `1+70/78`, eternium `1+52/78`, copper/silver/gold `1+40/78`). That
  clean signature suggests a real readout. It **disagrees with the Update 29.4 table**
  of §6.3, which ranks copper/silver/gold **last**, not fourth. Two epochs; do not mix.
- **"Metal Chart" `ENDURANCE`** — aluminium 219, steel 239, titanium 213, iron 187,
  bronze 162, nickel 194, copper 200, tin 174. It is bundled with a **calculator-era**
  weight column (`.26kg`, `.38kg`, `.40kg`), so it belongs to that epoch.
- **"Node Resilience Sheet"** — Q1–Q10 pulses-to-falloff per metal, and independently
  confirms the flat `Large Panel - 60 Resilience` that §6.2 relies on.

---

## 3. THE SHIPPED CLIENT CARRIES NEITHER — PROVED

`MaterialManager`, `level0` PathID 1373, parsed byte-exact via the raw-data method: **6304 / 6304 bytes consumed, 0 remaining.** 32 materials, carrying only:

- name, id
- a 4-value `{metal, wood, none, atlas}` enum
- colours

**No mass. No durability. No quality scaling.** Swept all 8 DLLs and 55,885 MonoBehaviours; the bundles are island terrain only.

### 3.1 A false-negative mechanism worth adding to the method canon

**`grep -a` on .NET DLLs is itself a false negative.** User strings are **UTF-16LE**, so `Ship weighs more` returned nothing until dual UTF-8/UTF-16 dumps were built — at which point the control landed in `Assembly-CSharp.dll` as it must.

This is a *fifth* mechanism on top of the four in `understorm-s1-live-findings.md` §5, and it is nastier, because `-a` is the documented fix for the binary case and it is not sufficient here.

### 3.2 What this means for the community data

Mass and durability are **server-authoritative**. The client assigns Rigidbody mass straight from `1257` and is Reader-only throughout. So the community numbers are **neither corroborated nor contradicted** by the shipped client — the question the brief asked cannot be answered from the client, because the client never carried the data. That is a real answer, not a failure to find one.

---

## 4. WHAT OUR SERVER DOES TODAY — three corrections

### 4.1 `1258` is lift; mass is `1257`, and it is already per-material

- `1258 ShipLiftState` — the flat **1,000,000 is `totalLift`**, not mass (`ComponentsSerializer.cs:614-634`).
- `1257 ParentingMassAdderState` — the mass component, served at `ComponentsSerializer.cs:3284` → `ShipMassKgFor` → `HullMassCalculator.HullMassKg`. It feeds **both** the wire and flight agility: `sqrt(800/mass)`, clamped `[0.5, 1.6]`.

**Per-material ship mass is live and flying today.**

### 4.2 The 1e6 seed never protected anything

`AtlasMultiplier` evaluates to **0.0** on an unmodified client (arithmetic on the shipped formula — the countdown expired years ago). So `TotalLift = 0 × 1e6 = 0`, and `IsOverloaded` reduces to `totalMass > 0` — **true for any ship, a cedar skiff included.** A generous lift seed cannot survive multiplication by zero.

What actually keeps vertical flight working is **`WorldsAdriftReborn/Patching/Flight/EndOfTheWorld_Patch.cs`** — a Harmony prefix pinning the `AtlasMultiplier` getter to `1f`.

> ⚠ **This corrects `findings-storm-walls.md` §7**, which concluded that `totalMass == 0` was the thing keeping flight alive. That reasoning was sound *for an unmodified client* and missed the patch. **Reasoning from the decompile alone, without checking our own Harmony patches, is a false-negative mechanism in its own right** — the decompile describes retail, not what we run.

### 4.3 "1257 is known-absent" is stale

That comment sits at `ComponentsSerializer.cs:626`, **inside the 1258 branch**, while the 1257 branch 2,600 lines below serves a fully derived per-material mass. The comment describes a world that no longer exists.

---

## 5. THE ATLAS CLIFF — it is LIFT, not mass

**What happens when mass becomes real? Nothing. It already is, and nothing broke.**

With the patch in place `TotalLift = 1,000,000` and hull mass tops out orders of magnitude below it — solid gold would need roughly **685 cells** to overload.

**The real cliff is on the lift side.** The recovered retail base is a **1,000 kg** sky core plus per-metal `AtlasLiftPerQualityKg`. The moment lift is made real, the budget collapses **1,000×**:

| hull | mass | vs a bare 1,000 kg core |
|---|---|---|
| one-cell iron | 780 kg | **78% of budget** |
| one-cell gold | 1,460 kg | **grounded instantly** |

Three things to settle **before** touching lift:

1. **`UnitsPerHullCell = 2000` is CHOSEN**, and would become load-bearing world-wide.
2. **Whether `ShipLiftVisualizer` is injected at all** — a null `_state` also yields 0, which is a second, independent path to the same failure.
3. **Core internals are not modelled**, so lift above the 1,000 kg base **cannot currently be earned.**

The overload string is quoted only as read: `"Ship weighs more than its atlas sky core can lift."` (`ShipControlsBehaviour.cs:283`). **Never invent client strings.**

**Shared quantities for the wall work** (`findings-storm-walls.md`): mass in kg, lift in kg, `windMultiplier = 1 − clamp01(mass/4000) × 0.75`.

---

## 6. DURABILITY — real data, no mechanism, contradictory proxies

### 6.1 The cannon workbook is mostly empty

**13 usable rows out of 200.** The other 187 are `#DIV/0!` from blank shot counts — **missing, not zero**. Averaging over them would manufacture numbers.

### 6.2 The data that survives argues against per-material resilience

- Every large panel takes **10–15 shots regardless of material** — a 1.5× spread against a **5× mass range**.
- `Panels.wikitext` gives **every** panel `bar1value = 60`.
- Lead has the **second-highest** casing Health (0.972) and the **fewest** shots (11) — the two proxies contradict.
- The `Kg Per Shot` column is **mass ÷ shots**, not an independent measurement.

**Panel resilience may simply not have been per-material.** That is a legitimate reading of the evidence, not a gap in it.

### 6.3 RESILIENCE RECOVERED — the gap §8 called the highest-value target is closed

**The wiki `Resilience` page looked empty because its data is an embedded chart.** Every CSV, gviz and per-gid export is walled (400/401/404) — **but the published chart carries its own dataset as escaped JSON inside the chart HTML.** Fetching `pubchart?oid=225442159` returned the full table, document title *"Resilience (Update 29.4)"*:

| material | resilience | | material | resilience |
|---|---|---|---|---|
| **Tungsten** | **85.77** | | Iron | 31.39 |
| Eternium | 66.06 | | Tin / Orthite | 26.28 |
| Epilar / Steel | 45.99 | | Titanium / Bronze | 21.17 |
| Aluminum / Nickel | 36.13 | | Lead | 16.42 |
| | | | Copper / Silver / Gold | 6.57 |

Three things make this the strongest durability source we hold:

1. **Every full-precision float is an exact multiple of `0.5 / 137`** (copper/silver/gold `9/137`, tungsten `117.5/137`, eternium `90.5/137`). That is the signature of real in-game readouts, not community estimates.
2. It is **Update 29.4** — by far the latest patch of any source in this document.
3. It includes **orthite, epilar and eternium**, which appear in no spreadsheet we hold.

**Two caveats, and they matter:**
- It is a **boost percentage against the best performer**, not absolute HP. It gives ordering and ratio, not a number to put in a component.
- It **disagrees sharply with the 0.1.3.3 Casing Health ordering** from the engine sheet. Two patches apart, so both may be right in their epoch — but they cannot both be used at once.

*Archived at `wa-community-2026-08-20/workbooks/resilience-update-29-4/`, raw chart HTML alongside the derived CSV, checksums verified. The same technique recovered the Skycore lift chart.*

### 6.4 There is nothing to attach it to

No damage model: `DamageService|ApplyDamage|TakeDamage` → **0 hits**; control `HullMassCalculator` → 3 files, sweep live. `ShipMaterial.Durability` is read by **exactly one thing: its own unit test.**

**Better durability numbers are not the blocking work. A damage model is.**

---

## 7. TWO PROVENANCE DEFECTS FOUND — ✅ BOTH FIXED 2026-08-20

1. **`ShipMaterial.cs` documents `Durability` and `HeatResistance` as CHOSEN. They are not** — **12/12 exact matches** to wing-science Casing Health and engine-science overheat respectively. `MaterialCatalog.cs` already has this right; **the two files contradict each other.**
   **FIXED** (`feat/massalign`): both the class remarks and the two property doc-comments in `ShipMaterial.cs` now read COMMUNITY-MEASURED for the twelve metals and CHOSEN for the woods, the three invented metals and our two additions — which is what `MaterialCatalog.cs` always said. The correction is annotated in place so the change is auditable.
2. **`feature-roadmap.md` (~2294)** claims the ÷40 relation corroborates our numbers "20 rows out of 20". It corroborates the **community** table — which is **not the table we ship**. They differ on **14 of 16** materials.
   **FIXED**: the claim is struck and replaced in place, with the reason (it double-counted one source *and* aimed it at the wrong epoch) and with the corroboration that is actually valid — wiki + `sciencesheet.xls`, 15 of 15 metals (§2.7).

Both were provenance errors of the exact class the project rules forbid: a CHOSEN label on recovered data, and a corroboration claim pointed at the wrong table. **A third of the same family was found and retracted this session** — the §2.6 claim that we ship cedar 0.15 / chestnut 0.19. All three shared one root cause: *a statement about our own code that nobody re-derived from our own code.*

---

## 8. GAPS I COULD NOT CLOSE

- **There is no Pine.** Worlds Adrift never had one — `git grep -inw pine` returns only pipelines, an emblem colour, and one deliberately-invalid test fixture; control `chestnut` → 147 hits. **The nearest real analogue to "pine vs chestnut" is cedar vs chestnut, 0.13 vs 0.17.**
- **The absolute hull-frame scale.** The wiki publishes *panel* WPU and states other components differ, so `UnitsPerHullCell` stays CHOSEN — permanently, absent a new source.
- **Wood durability from any source at all.** Re-confirmed after the resilience recovery: **every durability source we hold is metals-only.** This is now the largest single hole in the material data.
- ~~**Orthite, epilar, eternium** have mass and nothing else.~~ **Partly closed** — §6.3 gives all three a resilience figure. Mass for them still comes only from the wiki.
- **The cannon sheet's three RAW tabs** — Wayback renders only the active tab; no per-gid, `/htmlview`, `/pubhtml` or `/export` capture exists in CDX, and Google still returns 410. **Permanently lost.**
- **The spreadsheets behind both recovered charts** remain walled. Only the *plotted series* came back, so any unplotted column is still unknown.
- **No general quality→durability formula.** But quality is settled on the mass side: it does **not** scale mass — the wiki states higher quality boosts stats *"without any additional cost of weight"*. The one concrete quality curve is atlas lift, linear: `lift = 1000 + (Q + 10) × rate`, fitting 11 of 12 metals exactly (iron's row carries a uniform +1 offset, preserved as found).
- **Fandom live** — 403 to curl, 402 to WebFetch. Wiki figures therefore come from **2018 Wayback gamepedia captures carrying an "out of date" banner.** That is a provenance caveat on every wiki number in this document.
- ~~**Highest-value remaining target: the wiki Resilience page's embedded sheet.**~~ **CLOSED — see §6.3.**
- **Unexhausted:** a parallel sweep of Reddit, Steam guides and GitHub for further tables was dispatched but had not reported. Nothing here depends on it.

---

## 10. MEASURED CONSEQUENCES OF THE SHIPPED TABLE (2026-08-20)

**Delta from this session's work: ZERO on every hull.** The values did not move, so
nothing flying changed. The absolute figures, and the counterfactual, are below.

The "calculator-era" column is what the masses **would** have been had we actually been
shipping that table and switched — i.e. the change the work brief expected. It is
recorded because it quantifies what was *not* done to the live world.

| hull | **shipped (final era)** | calculator era | Δ if we had switched | agility | wall ramp |
|---|---|---|---|---|---|
| **legacy birch/iron, 2 cell 1 deck** | **1071 kg** | 1206 kg | −11.2% | 0.864 | 0.799 |
| legacy birch/iron, 1 cell | 476 kg | 536 kg | −11.2% | 1.296 | 0.911 |
| 1-cell all-iron | 780 kg | 760 kg | +2.6% | 1.013 | 0.854 |
| 1-cell all-gold | 1460 kg | 1380 kg | +5.8% | 0.740 | 0.726 |
| 1-cell cedar skiff | 260 kg | 300 kg | −13.3% | **1.600 (clamped**, raw 1.754) | 0.951 |
| 4-cell palm/aluminium, 2 decks | 2394 kg | 2700 kg | −11.3% | 0.578 | 0.551 |
| 6-cell oak/steel, 4 decks | 3976 kg | 4256 kg | −6.6% | **0.500 (clamped**, raw 0.449) | 0.254 |

**1. The legacy hull is 1071 kg and did not move.** The roadmap's figure was already
computed from this table — `4500 units × (0.20×0.8 + 0.39×0.2)`. Re-verified and now
**pinned by a test**, so a future mass edit fails rather than silently invalidating the
roadmap.

**2. Flight agility.** `sqrt(800/mass)` clamped `[0.5, 1.6]`. **No hull changed and no
hull crossed a clamp boundary as a result of this work.** For the record, the clamps
bite at **≤312.5 kg** (max) and **≥3200 kg** (min). Two hulls sit outside today and
already did: the cedar skiff pins to 1.600, and any hull past ~3200 kg pins to 0.500 —
a 6-cell oak/steel ship is already there. **That is a pre-existing finding worth acting
on:** past 3200 kg the agility model stops discriminating, so material choice ceases to
matter for large ships. Not changed here; flagged.

**3. The wall weight ramp** — `1 − clamp01(mass/4000) × 0.75`, saturating at 4000 kg.
**Our hulls sit high on this ramp: 0.80–0.95 for everything a normal player flies.** A
1071 kg legacy ship still takes **80%** of full wall force. Saturation needs ~4000 kg,
which in practice means a 6-cell metal ship — reachable, but rare. **For the concurrent
wall-visuals work: mass attenuation is nearly a no-op for typical ships, so walls
should be tuned as though the ramp is ~0.8, not as though it varies widely.**

**4. The atlas margin is intact, and is now guarded.** `EndOfTheWorld_Patch.cs` pins
`AtlasMultiplier` to `1f`, so `TotalLift` is the seeded 1,000,000 kg. The legacy hull
keeps a **934×** margin; even an absurd 32-cell solid-gold ship (58,400 kg) keeps
**17.1×**. `IsOverloaded` cannot fire. **The `1258` seed was not touched** — but it was
*moved* out of the untested game-server assembly into
`Multiplayer/Materials/ShipLiftPolicy.cs`, because mutation testing proved it was
guarded by nothing (§9). Making lift realistic remains roadmap **F2**.

**5. `UnitsPerHullCell = 2000` is CHOSEN and still looks right.** It is calibrated so a
one-cell iron hull lands at 780 kg against the old flat 800 kg. Nothing in this session
makes it look wrong, and **it was not retuned.** The one honest caveat is (2) above:
because the agility clamp saturates at 3200 kg, this constant decides how many cells a
ship gets before agility stops responding — currently about three metal cells. That is
a balance question, not a fidelity one.

---

## 9. WHAT WAS NOT DONE

*(As of the original 2026-08-20 research pass.)* No server code changed. No client mod built or installed. Nothing pushed, nothing deployed. Production not touched. The 2026-08-16 snapshot was not modified. No schema change, no test change.

### 9.1 What `feat/massalign` then did, and the escape it found

**No material mass value was changed** — §2.7 explains why there was nothing to change.
What changed: the provenance repairs of §7, the epoch guard, and one real hole.

**A guard escaped on the first attempt, exactly as the brief predicted.** The `1258`
lift seed was a bare `1000000f` literal inline in `ComponentsSerializer`, an assembly
with **no test project**. Mutating it to `1000f` — which grounds *every ship in the live
world*, since the legacy hull already masses 1071 kg — **passed all 5,422 tests in
silence.** No string match, no assertion, nothing.

Fixed by moving the decision into `Multiplayer`, where it can be tested:
`Materials/ShipLiftPolicy.cs` owns the seed and the invariant, the serializer now reads
it, and `ShipLiftPolicyTests` holds it against the heaviest buildable hull. The same
mutation now turns **4 tests red**, one of which names the legacy hull by mass. The
lesson generalises and is the same one §4.2 records about the decompile: **a number
that lives where there is no test project is a number that is not guarded, however
carefully it is commented.**

Verified mutations, all restored afterwards:

| mutation | tests red | caught by |
|---|---|---|
| tungsten `0.70` → `0.74` (**the epoch trap**) | 7 | epoch guard names the trap verbatim; also the existing pin test and the gold/tungsten ordering test |
| `1258` lift seed `1e6` → `1e3` (**escaped first**) | 0 → **4** after the fix | `ShipLiftPolicyTests` |
| `UnitsPerHullCell` `2000` → `2200` | 5 | the 1071 kg legacy canary + the stock-iron-hull test |

**Still not done:** production untouched and read-only, no client mod built, no push, no
deploy, no schema change. The 2026-08-16 and 2026-08-20 snapshots were not modified.
