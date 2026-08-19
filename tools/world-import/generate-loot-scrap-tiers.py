#!/usr/bin/env python3
"""Generate loot-scrap-tiers.txt - the tier-keyed scrap table loot containers
draw from.

WHY THIS EXISTS. The pure Multiplayer assembly deliberately has no reference to
the game server project, so it cannot read
WorldsAdriftRebornGameServer/Game/Items/Config/itemData.json at runtime. The loot
roll has to be pure and unit-testable, so the one fact it needs out of that file
- which scrap item belongs to which island tier, and how big it is - is projected
here into an embedded table.

WHAT IS EVIDENCE.

  RECOVERED, all of it. Every row below comes straight out of itemData.json:

    * WHICH ids exist. The 134 `scrapItem-*` rows of category `Salvage`. These are
      real retail ids - each row's iconName matches an entry in the shipped icon
      atlas (docs/research/valid-icons.txt, 250 `scrap items/*` icons), and the
      decompile handles the `scrapItem-` prefix at InventoryTooltipPopup.cs:113
      and ScannableData.cs:368, reading Meta["title"]/Meta["description"] - which
      is exactly the metadata block shape the data file carries. Two-way match.

    * WHICH TIER each belongs to. The `rewards` block is keyed by tier string:
      {"3": {"a": 80, "q": 6, "item": "titanium"}}. A ".1"/".2" suffix is a second
      or third yield at the SAME tier, so the tier set is the key set with the
      suffix stripped. Distribution: tier 1 -> 41 items, 2 -> 50, 3 -> 32, 4 -> 86.

    * HOW BIG each is. width/height, verbatim. Scrap runs up to 5x3, which is why
      the container grid has to be generous.

  NOT EVIDENCE, and NOT emitted here: how many items a container holds, and how
  likely any one of them is. Those are WAREBORN TUNING and they live in
  Multiplayer/Loot/LootTable.cs where they can be argued about in one place.

The two rows of category "Founder's Pack" and the one row with no category are
excluded: a Founder's Tome is an entitlement, not island loot.

LootScrapTableIntegrityTests re-reads itemData.json and asserts this file still
matches it, so the two cannot drift apart silently.

Usage:  python3 tools/world-import/generate-loot-scrap-tiers.py
"""

import json
import os

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
ITEMS = os.path.join(
    ROOT, "WorldsAdriftRebornGameServer", "Game", "Items", "Config", "itemData.json")
OUTPUT = os.path.join(
    ROOT, "WorldsAdriftRebornGameServer.Multiplayer", "Loot", "loot-scrap-tiers.txt")

PREFIX = "scrapItem-"
SALVAGE = "Salvage"


def tiers_of(row):
    """The distinct island tiers this scrap item's rewards are keyed by."""
    out = set()
    for key in (row.get("rewards") or {}):
        head = str(key).split(".")[0]
        if head.isdigit():
            out.add(int(head))
    return sorted(out)


def main():
    with open(ITEMS, encoding="utf-8") as handle:
        items = json.load(handle)

    rows = []
    for row in items:
        item_id = str(row.get("itemTypeID") or "")
        if not item_id.startswith(PREFIX):
            continue
        if row.get("category") != SALVAGE:
            continue
        tiers = tiers_of(row)
        if not tiers:
            continue
        rows.append((item_id, int(row["width"]), int(row["height"]), tiers))

    rows.sort(key=lambda r: r[0])

    lines = [
        "# Tier-keyed scrap table for loot containers. GENERATED - do not hand-edit.",
        "# Source: WorldsAdriftRebornGameServer/Game/Items/Config/itemData.json",
        "# Regenerate: python3 tools/world-import/generate-loot-scrap-tiers.py",
        "# itemTypeID<TAB>width<TAB>height<TAB>tiers(comma-separated island tiers)",
    ]
    for item_id, width, height, tiers in rows:
        lines.append("{}\t{}\t{}\t{}".format(
            item_id, width, height, ",".join(str(t) for t in tiers)))

    os.makedirs(os.path.dirname(OUTPUT), exist_ok=True)
    with open(OUTPUT, "w", encoding="utf-8") as handle:
        handle.write("\n".join(lines) + "\n")

    per_tier = {t: sum(1 for r in rows if t in r[3]) for t in (1, 2, 3, 4)}
    print("wrote {} rows to {}".format(len(rows), OUTPUT))
    print("per tier: " + ", ".join("T{}={}".format(t, n) for t, n in per_tier.items()))


if __name__ == "__main__":
    main()
