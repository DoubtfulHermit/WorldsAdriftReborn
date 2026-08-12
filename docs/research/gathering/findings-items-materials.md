# FINDINGS — ITEM & MATERIAL DATA MODEL

**LEAD: there is no item table in the shipped client.** The client is a pure *consumer*
— `InventoryItemManager : IJsonSerialisable` (`acs/InventoryItemManager.cs:10`) receives
the whole table as gzipped JSON over component **1097 `ReferenceDataState`**, field
`inventoryData` (`gencode/.../ReferenceDataStateData.cs:12`;
`ReferenceDataVisualiser.cs:96-102`). The authoritative table was a **server-side Scala
GSim asset that died with SpatialOS**.

**The de-facto authority today is our own reconstruction**, `Game/Items/Config/itemData.json`
— 644 KB, 335 entries → **281 usable items** (52 have an empty `itemTypeID`, 2 ids dup).

**24 raw materials: 15 metals, 8 woods, 1 fuel.**

## THE RECONSTRUCTION IS FAITHFUL — validated two ways
- **283/283** non-empty entries resolve to an icon that actually ships in
  `globalgamemanagers` (1010 icons under `Icons/`). **Zero dangling icons.**
- The 15 metal and 8 wood names match the independent Cardinal Guild survey **exactly,
  1:1, in both directions**.
It is recovered Bossa content, not invented — but it is **incomplete** (see the gaps).

## MATERIALS — extracted to `data/materials.tsv`
Metals are all 3×2, `equippable=false`, icon `metals/Metal_<Name>`. **Rarity groups are a
clean 3/4/4/4:**
- r0 `iron, lead, bronze` · r1 `tin, orthite, steel, copper`
- r2 `titanium, nickel, epilar, silver` · r3 `aluminium, gold, eternium, tungsten`

Community metal ids run **1–16 contiguous EXCEPT id 6**, which is a genuine gap.
Woods (3×2, no rarity, icon `woods/Wood_<Name>`, note `Wood_palm` is lowercase):
`cedar, hemlock, chestnut, elm, birch, ash, oak, palm`.
Fuel: `fuel`, 2×2, **quality-exempt** (`ScannableData.cs:325` excludes it explicitly).

The 24 descriptions encode the stat archetypes — **weight, hardness, heat resistance,
stress resistance, conductivity**. `conductivityFinalStat` is a first-class field
(`PredictedStatDataExtra.cs:11`), confirming conductivity was a real derived stat.

## THREE DISTINCT SCHEMAS — do not conflate
**(a) Item TYPE, what the client parses** — `acs/InventoryItemData.cs:6-30`, exactly nine
fields: `itemTypeId, name, category, iconName, stackingMax, numOfSlotsWidth,
numOfSlotsHeight, equippable, wearable`. **`description`, `rarity`, `metadata`,
`colours`, `rewards` are NOT on this class.** Our `ItemHelper.GetReferenceItems`
(`:81-97`) emits exactly these nine with the renames applied — that mapping is correct.

**(b) Item INSTANCE on the wire** — `gencode/.../ScalaSlottedInventoryItem.cs:9-35`, 14
fields: `itemId, itemTypeId, amount, slotType, utilitySlotNum, xPosition, yPosition,
rotated, hotBarSlotNum, timeToBuild, quality, lockBoxItem, meta, rarity`.
**`slotType` is parsed with `Enum.Parse` (`InventorySlotData.cs:99`) — an invalid string
is a HARD CRASH.** `-1` means "unused" for utilitySlotNum/positions/hotbar.

**(c) Client runtime** — `InventorySlotData.ParseFromServerInventoryItem:93-111`.

## QUALITY — the signature mechanic, decoded
**A per-instance integer on the 1–10 scale, carried by the material, not a tier lookup
and not a per-material curve.**
- `int` everywhere: `RawMaterial.quality`, `MetalRockStateData.quality`,
  `ScalaSlottedInventoryItem.quality`, `CraftingSlotData.quality`.
- **Range 1–10 confirmed by two independent sources**: 380 per-island observations across
  254 islands span exactly 1–10; the 239 salvage yield rows span 0 and 3–10.
- **`InventorySlotData.cs:38` initialises `quality = 50` — a DEAD default**, always
  overwritten at `:106`. Do not read it as a 0–100 scale.
- **Quality belongs to the source node**, assigned at spawn:
  `MetalRockStateData` carries `{metalTypeId, quality}` together (`:16-18`).
- **Only Metal and Wood display it** — `ShipBlueprintMaterialUI.cs:83-87` renders
  `"Q" + quality + "+ " + name`. **It is a FLOOR, not an exact match.**
- **A crafting slot is single-quality**: `CraftingMaterialSlot.cs:350-352` accepts more
  material only if `quality == _craftingSlotData.quality`; the first insert sets it.
  Rejection text at `:544`. **Quality does not average** — one quality per material slot.

**The quality→crafted-stat formula is NOT RECOVERABLE.** Output stats are computed
server-side and pushed as pre-normalised bars: `PredictedStatData{statId,
baseNormalized, modifierNormalized}` (`gencode/.../PredictedStatData.cs:7-11`). The
client only renders them. Values >1.0 overflow into a second "modifier" bar
(`ScannableData.cs:285-287`) — **quality can push a stat above its 100% baseline**, which
is the mechanic's whole point. Known stat ids (`SchematicData.cs:301-312`): `power,
boost, fuelEfficiency, overheatLimit, rateOfFire, airBrake, hpStat, frag, choke, range`.
Baseline `baseHPValue = 2000f`. **Must be invented; the 24 descriptions are the guide.**

## `RawMaterial` — `gencode/.../RawMaterial.cs:7-23`
`{string materialTypeId; int quality; string category; Map<string,string> meta}`.
Mapping to the item table is **by string**. `category` is **denormalised** onto it — a
copy of `InventoryItemData.category` — so populate both consistently or the `"Q{n}+"`
display and `IsSameMaterialType` break.
Slot wrapper is `SlottedMaterial{int index; RawMaterial rawMaterial; int amount;
Option<RawMaterial> customizationMaterial}` — **4 fields, not 2** as previously recorded.

## THE BIG FIND: a real salvage yield table exists and we are DISCARDING IT
All 134 `Salvage` items carry a `rewards` field — a complete tier-keyed yield table.
**`ItemHelper.ValidItem` has no `rewards` property, so System.Text.Json silently drops it.**

Format `{"<tier>": {"a": amount, "q": quality, "item": itemTypeId}}` where the key is the
**island tier 1–4** (matching the Cardinal Guild `tier` field exactly) and `.1`/`.2`
suffixes are **additional simultaneous yields on the same tier**:
```json
"scrapItem-crackedminingdrill": {
  "3":   {"a":125,"q":5,"item":"bronze"},  "3.1": {"a":40,"q":0,"item":"fuel"},
  "4":   {"a":125,"q":8,"item":"bronze"},  "4.1": {"a":40,"q":0,"item":"fuel"} }
```
239 rows over 134 items; 73 have multiple entries; amounts 15–400; **quality rises with
island tier** (t1 median q≈4–7, t4 median q≈8–10). **All 239 resolve — zero dangling.**
Extracted to `data/salvage_yields.tsv`. **This is the single highest-value artifact here.**

## ITEM IDS — a prior convention is NOT a client requirement
**There is no numeric item-type id space.** `itemTypeId` is a string and is the only key
(`InventoryItemManager.cs:84`). **Do not invent numeric item ids.**
`itemId` (int) is an **opaque per-inventory instance handle**. An exhaustive search found
**no reserved-range logic anywhere** — every use is an equality lookup. So:
> **The "ids under 100 are reserved" comment (`ItemHelper.cs:136`) is a project
> convention, not a client requirement.** No client code branches on it.

**Collision behaviour:** `InventoryContents.cs:401` uses `.Find(x => x.serverItemId == i)`
— first match wins, **silently**. Duplicate ids corrupt lookups with no error, and
**there is no allocator anywhere in the server** (grep for `nextItemId` returns nothing).
Harvesting needs one.

## LIVE DEFECTS FOUND
- **`ItemHelper.GetItem` is an unguarded indexer** (`:70`) — any unknown id throws
  `KeyNotFoundException`.
- **`DevItems()` references `"head_olk"`, which is NOT in itemData.json** (`:157`) →
  `GetStashItems(dev:true)` throws. Intended item is `head_goatmask`.
- `MakeItem` defaults `quality: 0`, which for a Metal/Wood item is out of range and
  renders as `"Quality: 0"`.
- **`stackingMax` is never set** in itemData.json → defaults to `-1` → **every item
  currently advertises unlimited stacking.**
- Colour metadata formatting is inconsistent: `torso_poncho` uses `"566B8E"`,
  `head_devhat` uses `"#15161B"`.
- `scrapItemselenistswoodenorrery` — **missing hyphen**, fails
  `StartsWith("scrapItem-")` so its description never displays.
- `torse_squireVariantA` — typo for `torso_`.

## CATEGORIES ARE STRINGS WITH NO ENUM
`rawMaterialsArray = {"Metal","Wood","Fuel"}` (`InventoryItemManager.cs:18,112-115`).
**Any new material category must be added there AND in `ItemHelper.cs:107` or it stops
being a material.** Crafting slot matching (`:117-120`) accepts a **category**, a
**specific itemTypeId**, or the magic string **`"Wood/Metal"`**.
Consumables/schematics/scrap are recognised by **prefix conventions, not fields**:
`scrapItem-`, `steamInvBundle-`, or an itemTypeId matching a `SchematicData.schematicId`.

`CharacterSlotType` has 12 values; **itemData.json populates only 6** (`None, Body, Head,
Feet, Tool, Utility`). Utility slots are **numbered** — occupancy is
`(slotType, utilitySlotNum)`, so several items share `Utility`. **Utility items require
`meta["totalHealth"]`** or `GearWearablesVisualizer.cs:79-95` errors.

## COMMUNITY ISLAND TABLES ARE CONSISTENT — USE THEM
Metals 15v15 exact both directions; woods 8v8 exact; quality 1–10 matches; island tiers
1–4 match the `rewards` keys exactly; 239/239 salvage rewards resolve.
254 islands surveyed, **61 with metal data, 74 with tree data**; tiers `1:46 2:50 3:82
4:76`. **PvE and PvP metal tables differ per island** — pick one ruleset or model both.
Safe to populate `MetalRockStateData{metalTypeId, quality}` directly from
`data/island_resources.json`. Caveats: ~75% of islands need a generator (the tier→quality
distribution is a good prior), and `type_id` is a **community** id — keep it as an
external cross-reference, never on the wire.

## WHAT THE RECONSTRUCTION IS MISSING — 729 of 1010 shipped icons have no item
Systematic gaps, all with shipped icons and no entry:
- **3 metals**: `magnesium`, `palladium`, `platinum` — **one is very likely metal id 6**.
- **5 woods**: `ebony`, `ironwood`, `mahogany`, `maple`, `wood_palm2`.
- **An entire missing material class** — `Icons/materials/` (30 icons, 0 items):
  `chitin, leather, cloth, plantfiber, pigment, glassshards, ancientglass, ship_core,
  ship_engine`, plus per-biome `neuralcluster`/`conductivevessels`/`beetle_biomeN`/
  `mantaray_biomeN` sets. **These are the creature/plant materials — a whole harvesting
  economy absent from our table.**
- **68 foods** — the consumable category is entirely missing.
- 98 ship parts, 115 further scrap items, 87 clothing, 19 procedural ship-part icons.
- **The 52 blank-id rows are all female clothing variants** (`*_female` icons). The
  original table had gendered entries and the reconstruction lost the key.

## COULD NOT DETERMINE
The quality→stat formula (Scala GSim, must be invented). The identity of metal id 6.
`stackingMax` for any item. Whether the unused metals/woods/materials icons were live at
sunset or cut content. The original female-variant naming. `casingQuality`
(`SalvageAndRepairStateData:31`) — an `Option<float>` distinct from the int scale, no
client reader found. Real `salvageRatio`/`salvageDamagePerPeriod` values.
Nothing was executed.

## DATA EXTRACTED (in `data/`)
`materials.tsv|json` (24) · `salvage_yields.tsv|json` (239) · `items_all.tsv` (283) ·
`island_resources.json` (254 islands)
