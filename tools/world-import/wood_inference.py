#!/usr/bin/env python3
"""Derive a wood table for an island the Cardinal Guild survey never recorded one for.

WHY THIS EXISTS
---------------
This is `metal_inference.py`'s bug, one field over, and it is fixed the same way.

The Cardinal Guild survey carries a `trees` array per island. 72 islands name a
real species list, 2 say the literal string "No trees", and 180 carry an EMPTY
array. `generate-release-tree-placements.py` used to read that empty array as
"this island had no trees" and skip the island entirely, which is how 182 of the
254 release islands - and 32 of the 46 TIER-ONE islands a graduating player is
teleported to - ended up with nothing to chop.

An empty array is UNSURVEYED, not treeless. Four independent lines:

  1. The literal value "No trees" exists and is used on exactly two islands. If
     `[]` also meant "no trees" there would be no reason for anyone to type it.
  2. `docs/research/findings-island-resource-population.md` lists `trees` at
     74/254 in the same PROVED survey-coverage table that lists `pveMetals` at
     38/254 - and the metals gap is already established as a survey gap, by five
     lines of evidence, in that same document.
  3. `docs/research/world-data/PROVENANCE.md`: the surveyor backend serialises
     these arrays with no filtering, and the community's own map UI renders an
     empty list as "No <x> data" - the dataset's authors read their own empty
     arrays as "nobody filed a report".
  4. All 254 islands carry a surveyor name and an exact databank count, and the
     `trees` and `pveMetals` fields fail INDEPENDENTLY: 45 islands have metals
     but no trees, 58 have trees but no metals. Two fields failing independently
     on the same fully-visited islands is coverage, not geography.

WHAT THIS MODULE DOES NOT KNOW
------------------------------
Nothing here is recovered Bossa data. Retail tree positions and retail per-island
species lists are BOTH gone (see ReleaseTreeBudget's remarks: zero of the 465,571
extracted prop placements is a tree). This module answers only "which of the
eight authored woods does an unsurveyed island grow", by INFERENCE from the 72
islands that were surveyed, and every island it touches is stamped
`woodSource: "inferred-tier"` so it can never be mistaken for evidence.

THE LADDER, cheapest evidence first
-----------------------------------
1. `survey`       - the island's own recorded species list. Evidence.
2. `survey-none`  - the island literally says "No trees". Evidence OF ABSENCE,
   and honoured: no seats are emitted at all. Both islands that say it are
   tier 4, so honouring it costs the wilderness nothing.
3. `inferred-tier` - an empty array. Composed from the tier cohort below.

THE DERIVED RULE (computed here from the survey, never hardcoded)
-----------------------------------------------------------------
* Palette. A wood is admissible at tier T if it was observed on any surveyed
  island of tier <= T - the same monotone rule metal_inference.py uses, and the
  measured ladder is just as clean. Of the 117 species rows the survey records,
  cedar appears in none of the 22 tier-1 rows, and hemlock in none of the 22
  tier-1 or 44 tier-2 rows, while between them they account for 22 of the 51
  tier-3/4 rows. Tier 1 therefore admits ash, birch, chestnut, elm, oak and
  palm; cedar joins at tier 2 and hemlock at tier 3.
* Table size. The median species-list length observed at that tier. Survey lists
  are themselves partial - a volunteer names what they walked past - so the
  median under-claims rather than over-claims.

Selection is deterministic, keyed on the island's Steam workshop id through the
same explicit splitmix64 metal_inference.py uses, so the placement file
regenerates byte-identically forever. The seed prefix differs ("wood:" against
"metal:") so an island's woods are not correlated with its ores by construction.
"""

import statistics
from collections import Counter

from metal_inference import _Draw, _seed

#: The survey's whole species vocabulary, lower-cased. Verified as
#: `sorted(set(...))` over all 72 surveyed islands: no ninth name, no synonym.
KNOWN_WOODS = ("ash", "birch", "cedar", "chestnut", "elm", "hemlock", "oak", "palm")

#: The literal survey value for an island a volunteer confirmed as treeless.
NO_TREES = "no trees"


def surveyed_woods(profile):
    """The island's own recorded species, lower-cased and de-duplicated.

    Returns `None` when the survey said "No trees" - which is a statement, not a
    gap - and `[]` when the island was never surveyed for trees at all.
    """
    names = [name.strip().lower() for name in (profile.get("trees") or []) if name.strip()]
    if any(name == NO_TREES for name in names):
        return None
    woods = []
    for name in names:
        if name not in KNOWN_WOODS:
            raise ValueError("unknown surveyed species %r on %s - refusing to guess a wood"
                             % (name, profile.get("workshopId")))
        if name not in woods:
            woods.append(name)
    return woods


class WoodInference:
    """The rule, derived once from every species list the survey did record."""

    def __init__(self, profiles):
        """`profiles` is the survey's per-island properties dict, all 254 of them."""
        observations = []
        for profile in profiles:
            woods = surveyed_woods(profile)
            if woods:
                observations.extend((profile["tier"], wood) for wood in woods)
        if not observations:
            raise ValueError("no surveyed species observation to derive a rule from")

        first_tier = {}
        frequency = {}
        for tier, wood in observations:
            first_tier[wood] = min(first_tier.get(wood, tier), tier)
            frequency.setdefault(tier, Counter())[wood] += 1

        self.tiers = sorted(frequency)
        self.palette = {tier: sorted(wood for wood, low in first_tier.items() if low <= tier)
                        for tier in self.tiers}
        self.frequency = frequency
        self.size = {}
        for tier in self.tiers:
            sizes = [len(woods) for profile in profiles if profile["tier"] == tier
                     for woods in (surveyed_woods(profile),) if woods]
            self.size[tier] = max(1, int(statistics.median(sizes))) if sizes else 1

    def _nearest(self, mapping, tier):
        """The value for `tier`, or the closest tier that has one (ties go deeper)."""
        if tier in mapping:
            return mapping[tier]
        return mapping[min(self.tiers, key=lambda other: (abs(other - tier), -other))]

    def weights(self, tier):
        """Admissible woods at `tier` with their observed frequency, stable order.

        A wood admitted by the palette but never seen at this exact tier still
        gets weight 1: the palette is the claim about availability, the counts are
        only about how typical it is."""
        counts = self._nearest(self.frequency, tier)
        return [(wood, counts.get(wood, 1)) for wood in self._nearest(self.palette, tier)]

    def table_for(self, workshop_id, tier):
        """The inferred lower-cased wood list for one unsurveyed island."""
        draw = _Draw(_seed("wood:" + workshop_id))
        pool = self.weights(tier)
        chosen = []
        wanted = min(self._nearest(self.size, tier), len(pool))
        while len(chosen) < wanted:
            wood = draw.weighted(pool)
            pool = [entry for entry in pool if entry[0] != wood]
            chosen.append(wood)
        return sorted(chosen)


def woods_for(profile, inference):
    """The effective wood list for one island, and where it came from.

    Returns `(woods, source)`. `woods` is `None` only for `survey-none`, which is
    the caller's signal to emit no seats at all rather than to fall through to the
    inference - the survey said something there and it must be honoured.
    """
    surveyed = surveyed_woods(profile)
    if surveyed is None:
        return None, "survey-none"
    if surveyed:
        return surveyed, "survey"
    return inference.table_for(profile["workshopId"], profile["tier"]), "inferred-tier"


def _self_check():
    """Print the derived rule so a reviewer can audit it without reading JSON."""
    import json
    from pathlib import Path
    data = Path(__file__).resolve().parents[2] / "docs/research/world-data/cardinal-guild-islands.json"
    profiles = [f["properties"] for f in json.loads(data.read_text())["features"]]
    rule = WoodInference(profiles)
    surveyed = sum(1 for p in profiles if surveyed_woods(p))
    print("Derived from", surveyed, "surveyed species lists.\n")
    for tier in rule.tiers:
        print(f"tier {tier}: size {rule.size[tier]}, "
              f"palette {len(rule.palette[tier])}: {', '.join(rule.palette[tier])}")
    print()
    counts = Counter(woods_for(p, rule)[1] for p in profiles)
    for source, count in sorted(counts.items()):
        print(f"{source:14s} {count:3d} islands")
    print()
    first = next(p for p in profiles if woods_for(p, rule)[1] == "inferred-tier")
    print("worked example -", first["slug"], "tier", first["tier"], "->",
          json.dumps(rule.table_for(first["workshopId"], first["tier"])))
    for profile in profiles:
        assert rule.table_for(profile["workshopId"], profile["tier"]) == \
               rule.table_for(profile["workshopId"], profile["tier"])
    print("determinism: OK for all", len(profiles), "islands")


if __name__ == "__main__":
    _self_check()
