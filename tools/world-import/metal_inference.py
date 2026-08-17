#!/usr/bin/env python3
"""Derive a metal table for an island the Cardinal Guild survey never recorded one for.

WHY THIS EXISTS
---------------
Retail stored a per-island `IslandResourceSpawnerState` (component 1010) whose
fields 6 and 7 are `map<string,int> metalDepositQuantities` and
`map<string,int> metalDepositQualities`. The Cardinal Guild survey's
`pveMetals: [{name, quality}]` is a readout of field 7 - same shape, same
1..10 quality domain. That component also carries `minMetalRockDeposits`, a
floor on how many deposits an island's spawner produces.

So an island whose survey entry has an empty `pveMetals` array is an island the
community never read the map off, NOT an island whose map was empty. Every one
of the 254 islands carries `surveyCreatedBy`, `surveyUpdatedBy` and an exact
databank count; only 38 carry a PvE metal table. That is a coverage gap.

WHAT THIS MODULE DOES NOT KNOW
------------------------------
The worker that populated those two maps was Scala and is lost. Nothing here is
recovered Bossa data. Everything this module produces is INFERENCE from the 38
PvE plus 33 PvP tables the survey did record, and every island it touches is
stamped `metalSource: "inferred-tier"` in the catalogue so the inference can
never be mistaken for evidence.

THE LADDER, cheapest evidence first
-----------------------------------
1. `survey-pve`  - the island's own recorded PvE table. Evidence.
2. `survey-pvp`  - no PvE table but the same physical island WAS read on the
   PvP shard. Still an observation of that island, one ruleset removed.
3. `inferred-tier` - neither. Composed from the cohort of islands of the same
   tier that WERE surveyed, by the rules below.

THE DERIVED RULE (computed here from the survey, never hardcoded)
-----------------------------------------------------------------
* Palette. A metal is admissible at tier T if it was observed on any surveyed
  island of tier <= T. This is monotone by construction: a metal available in
  the shallows is available deeper. Measured, the ladder is clean -
  Aluminium/Orthite/Eternium are absent from all 39 tier-1 and tier-2
  observations while accounting for 18.9% of the 366 tier-3/4 observations
  (Poisson P(0 | 7.4) ~ 6e-4).
* Quality. Drawn from the empirical quality histogram of that tier. The bands
  are tight and tier-ordered in the real data: tier 4 has 280 observations and
  not one below quality 7.
* Table size. The median table size observed at that tier. Survey tables are
  themselves partial samples, so the median under-claims rather than over-claims.

Selection is deterministic, keyed on the island's Steam workshop id through an
explicit splitmix64 - no `random` module, no Python-version dependence, so the
catalogue regenerates byte-identically forever.
"""

import statistics
from collections import Counter

MASK64 = (1 << 64) - 1


def splitmix64(state):
    """One splitmix64 step. Written out so the draw never depends on a stdlib PRNG."""
    state = (state + 0x9E3779B97F4A7C15) & MASK64
    z = state
    z = ((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9) & MASK64
    z = ((z ^ (z >> 27)) * 0x94D049BB133111EB) & MASK64
    return state, z ^ (z >> 31)


def _seed(text):
    value = 14695981039346656037
    for byte in text.encode():
        value = ((value ^ byte) * 1099511628211) & MASK64
    return value


class _Draw:
    """A deterministic weighted-choice stream seeded by one island."""

    def __init__(self, seed):
        self.state = seed

    def next(self, bound):
        self.state, value = splitmix64(self.state)
        return value % bound if bound > 0 else 0

    def weighted(self, items):
        """Pick one (item, weight) pair. `items` must already be in a stable order."""
        total = sum(weight for _, weight in items)
        cut = self.next(total)
        for item, weight in items:
            cut -= weight
            if cut < 0:
                return item
        return items[-1][0]


class MetalInference:
    """The rule, derived once from every metal table the survey did record."""

    def __init__(self, profiles):
        """`profiles` is the survey's per-island properties dict, all 254 of them."""
        observations = [(p["tier"], m)
                        for p in profiles
                        for m in (p.get("pveMetals") or []) + (p.get("pvpMetals") or [])]
        if not observations:
            raise ValueError("no surveyed metal observation to derive a rule from")

        # Lowest tier each metal was ever seen at, and the per-tier frequency and
        # quality histograms. Sorted lists everywhere: the draw must not depend on
        # dict iteration order.
        first_tier = {}
        frequency = {}
        quality = {}
        for tier, metal in observations:
            name = metal["name"]
            first_tier[name] = min(first_tier.get(name, tier), tier)
            frequency.setdefault(tier, Counter())[name] += 1
            quality.setdefault(tier, Counter())[metal["quality"]] += 1

        self.tiers = sorted(frequency)
        self.palette = {tier: sorted(name for name, low in first_tier.items() if low <= tier)
                        for tier in self.tiers}
        self.frequency = frequency
        self.quality = {tier: sorted(counts.items()) for tier, counts in quality.items()}
        self.size = {}
        for tier in self.tiers:
            sizes = [len(table)
                     for p in profiles if p["tier"] == tier
                     for table in (p.get("pveMetals") or [], p.get("pvpMetals") or [])
                     if table]
            self.size[tier] = max(1, int(statistics.median(sizes)))

    def _nearest(self, mapping, tier):
        """The value for `tier`, or the closest tier that has one (ties go deeper)."""
        if tier in mapping:
            return mapping[tier]
        return mapping[min(self.tiers, key=lambda other: (abs(other - tier), -other))]

    def weights(self, tier):
        """Admissible metals at `tier` with their observed frequency, stable order.

        A metal admitted by the palette but never seen at this exact tier still
        gets weight 1 - the palette is the claim about availability, the counts
        are only about how typical it is."""
        counts = self._nearest(self.frequency, tier)
        return [(name, counts.get(name, 1)) for name in self._nearest(self.palette, tier)]

    def table_for(self, workshop_id, tier):
        """The inferred `[{name, quality}]` table for one unsurveyed island."""
        draw = _Draw(_seed("metal:" + workshop_id))
        pool = self.weights(tier)
        qualities = self._nearest(self.quality, tier)
        chosen = []
        wanted = min(self._nearest(self.size, tier), len(pool))
        while len(chosen) < wanted:
            name = draw.weighted(pool)
            pool = [entry for entry in pool if entry[0] != name]
            chosen.append({"name": name,
                           "quality": draw.weighted(qualities)})
        return sorted(chosen, key=lambda metal: metal["name"])


def metals_for(profile, inference):
    """The effective metal table for one island, and where it came from.

    Returns `(table, source)` where source is one of `survey-pve`, `survey-pvp`
    or `inferred-tier`. Only `survey-pve` is what retail's own PvE shard had;
    the other two are labelled so nothing downstream can confuse them with it.
    """
    if profile.get("pveMetals"):
        return profile["pveMetals"], "survey-pve"
    if profile.get("pvpMetals"):
        return profile["pvpMetals"], "survey-pvp"
    return inference.table_for(profile["workshopId"], profile["tier"]), "inferred-tier"


def _self_check():
    """Print the derived rule so a reviewer can audit it without reading JSON."""
    import json
    from pathlib import Path
    data = Path(__file__).resolve().parents[2] / "docs/research/world-data/cardinal-guild-islands.json"
    profiles = [f["properties"] for f in json.loads(data.read_text())["features"]]
    rule = MetalInference(profiles)
    print("Derived from", sum(1 for p in profiles if p["pveMetals"]), "PvE and",
          sum(1 for p in profiles if p["pvpMetals"]), "PvP surveyed tables.\n")
    for tier in rule.tiers:
        qualities = [q for q, _ in rule.quality[tier]]
        print(f"tier {tier}: size {rule.size[tier]}, quality {min(qualities)}-{max(qualities)}, "
              f"palette {len(rule.palette[tier])}: {', '.join(rule.palette[tier])}")
    print()
    counts = Counter(metals_for(p, rule)[1] for p in profiles)
    for source, count in sorted(counts.items()):
        print(f"{source:14s} {count:3d} islands")
    print()
    first = next(p for p in profiles if not p["pveMetals"] and not p["pvpMetals"])
    print("worked example -", first["slug"], "tier", first["tier"], "->",
          json.dumps(rule.table_for(first["workshopId"], first["tier"])))
    # Determinism is the whole point: the same id must always give the same table.
    for profile in profiles:
        assert rule.table_for(profile["workshopId"], profile["tier"]) == \
               rule.table_for(profile["workshopId"], profile["tier"])
    print("determinism: OK for all", len(profiles), "islands")


if __name__ == "__main__":
    _self_check()
