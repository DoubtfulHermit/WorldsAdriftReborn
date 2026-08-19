// LOOT CONTAINERS ON THE OPERATOR MAP - and NOWHERE ELSE.
//
// THIS FILE IS THE PRIVACY BOUNDARY, and it is structural rather than a flag.
// It is listed in AdminPage.AdminScriptFragments and is deliberately NOT in
// PublicMapPage.ScriptFragments, so the public map at /map does not merely hide
// the loot UI - it never receives the code that draws it. That is the same
// mechanism PublicMapPage documents for the operator command panel and the
// player table, and WebAssetCompositionTests asserts it both ways.
//
// map-render.js therefore calls into here through OPTIONAL hooks:
//
//     if (typeof wbLootIslandBlock === 'function') wbLootIslandBlock(scroll, inv);
//
// On the console the function exists and the block is drawn. On the public page
// the name is undefined, the branch is skipped, and the shared renderer stays a
// single implementation rather than forking into two that drift.
//
// EVERY TOP-LEVEL NAME HERE STARTS WITH wbLoot. Web/Assets/*.js are concatenated
// into ONE shared closure, so a duplicate top-level name silently shadows
// another file's. That has broken this map before: an `svgEl` declared in one
// fragment replaced a different `svgEl` and took the whole map down with it.
// Namespacing is not style here, it is the only thing preventing a repeat.
//
// NOTHING OFF THIS HOST. No content-delivery network, no web font, no remote
// image, no fetch. A test greps the whole rendered page for the three-letter
// name of the first of those, so do not write it here either - this comment
// used to, and the test caught it.

// The number of containers seeded on one island, or null when the island is not
// in the release catalogue (Haven's twelve hand-tuned placements are not).
function wbLootCountOf(inv) {
  if (!inv) return null;
  var n = Number(inv.lootContainers);
  return isFinite(n) ? n : 0;
}

// The ledger cell. Returns a string because the ledger writes text, and an
// em-dash rather than a zero for an island we can say nothing about.
function wbLootLedgerValue(inv) {
  var n = wbLootCountOf(inv);
  return n === null ? '—' : String(n);
}

// One clause for the island hover card, or null to add nothing.
function wbLootHoverFact(inv) {
  var n = wbLootCountOf(inv);
  if (!n) return null;
  return n + (n === 1 ? ' loot container' : ' loot containers');
}

// The stat tile on the island panel. `make` is map-render.js's own statTile, so
// this file never builds a second copy of that markup.
function wbLootIslandStatTile(stats, inv, make) {
  var n = wbLootCountOf(inv);
  if (n === null) return;
  stats.appendChild(make(n, 'Loot containers'));
}

// The stat tile on the world panel.
function wbLootWorldStatTile(stats, totals, make) {
  if (!totals) return;
  var n = Number(totals.lootContainers);
  if (!isFinite(n)) return;
  stats.appendChild(make(n, 'Loot containers'));
}

// The island panel's own block: how many, what is in them, and - stated plainly
// rather than implied - which half of that is recovered and which half is this
// project's tuning. The map's whole contract is that a guess is never presented
// as a fact.
function wbLootIslandBlock(scroll, inv, mdBlock, el, chipRow, plural, showsMethod) {
  var n = wbLootCountOf(inv);
  if (n === null) return;

  var block = mdBlock('Loot containers');

  if (!n) {
    block.appendChild(el('p', 'md-p',
      'No loot containers. This island’s measured surface has no pair of seats '
      + 'that satisfies the 20 m spacing rule, so nothing was placed rather than '
      + 'something being wedged in.'));
    scroll.appendChild(block);
    return;
  }

  block.appendChild(el('p', 'md-p',
    plural(n, 'loot container', 'loot containers') + ' are seeded here, holding salvageable '
    + 'scrap drawn from the tier ' + inv.cellTier + ' table. Press E to open one.'));

  if (showsMethod) {
    block.appendChild(chipRow(['Tier ' + inv.cellTier + ' scrap', '20 m spacing', 'area-budgeted']));

    block.appendChild(el('p', 'md-p',
      'RECOVERED: the placement itself. Unlike trees, which Bossa authored by hand '
      + 'from editor markers, loot containers were placed by an algorithm the shipped '
      + 'client still contains — LootablePerAreaDataVisualizer budgets them by '
      + 'mostly-flat surface area, and IslandDataBankAndLootableSpawnerVisualizer '
      + 'seats them at least 20 m apart and sinks each one 15–30 cm into the '
      + 'ground along the surface normal. All three rules are used here, over this '
      + 'island’s own extracted surface.'));

    block.appendChild(el('p', 'md-p',
      'RECOVERED: what is inside. Every item is a real retail scrapItem id, and which '
      + 'ids a tier ' + inv.cellTier + ' island may hold comes from the data’s own '
      + 'tier-keyed rewards blocks — not from a choice made here.'));

    block.appendChild(el('p', 'md-p',
      'WAREBORN TUNING: how many. The nineteen tuning constants on component 1244 '
      + 'did not ship, so the budget curve’s floor, ceiling and exponent are this '
      + 'project’s, as is the two-to-five items a container holds. The survey’s '
      + 'own databank counts are the only calibration anchor that survived and they '
      + 'are saturated at five, so they give no slope.'));

    block.appendChild(el('p', 'md-p',
      'NOT IN THEM: schematics. They were real inventory items in retail, but every '
      + 'acquisition path in the shipped client runs through the knowledge tree — '
      + 'which is why KnowledgeUseResponseType lists FullInventory as a way to fail '
      + 'buying a node. Nothing here puts one in a chest.'));
  }

  scroll.appendChild(block);
}

// One line for the world panel's prose summary, or the empty string.
function wbLootWorldLine(totals) {
  if (!totals) return '';
  var n = Number(totals.lootContainers);
  var islands = Number(totals.islandsWithLoot);
  if (!isFinite(n) || !n) return '';
  // "of them" would read as "of the WOODED islands", which is a different set.
  return ' ' + n + ' loot containers across ' + islands + ' islands.';
}

// Marks an island node that carries loot, so the operator can see WHERE the
// containers are without opening every panel.
//
// A SMALL DOT RATHER THAN A DOT PER CONTAINER. At whole-world zoom the map is
// 36 km across and an island is a 13-pixel glyph, so 2,243 individual container
// marks would be sub-pixel noise that hid the islands they sit on. What an
// operator can actually use at this scale is "which islands have loot", so the
// mark is one badge per island and the COUNT lives in the panel, the hover card
// and the ledger.
//
// Styled inline rather than through a CSS class on purpose: console.css is
// shared with the public map, and a rule added there would ship to a page that
// must not have one. The colour is taken from the console's own --warn token
// rather than written out as a hex literal - the palette has been changed once
// already and there is a test that fails on the retired one, which is precisely
// what a hard-coded colour here would reintroduce.
function wbLootDecorateIslandNode(group, inv) {
  var n = wbLootCountOf(inv);
  if (!n || !group) return;

  group.setAttribute('data-wb-loot', String(n));

  var inner = group.querySelector('g.mk');
  if (!inner) return;

  // Same namespace the renderer draws every other mark in. Built by hand rather
  // than through map-render.js's svgEl, because that helper is a top-level name
  // in the shared closure and reaching for it from here is exactly the coupling
  // that made an `svgEl` collision break this map once already.
  // The radius carries the COUNT, because 252 of the 266 islands have loot and a
  // fixed badge would therefore say almost nothing - it would mark "everywhere"
  // and only distinguish the fourteen that have none. Sized 1.6 px at the floor
  // of 2 containers to 3.4 px at the ceiling of 12, so a rich island reads at a
  // glance and a thin one still shows.
  var badge = document.createElementNS('http://www.w3.org/2000/svg', 'circle');
  badge.setAttribute('r', (1.6 + Math.min(1, (n - 2) / 10) * 1.8).toFixed(2));
  badge.setAttribute('cx', '7.5');
  badge.setAttribute('cy', '-7.5');
  badge.style.fill = 'var(--warn)';
  badge.style.stroke = 'var(--bg)';
  badge.setAttribute('stroke-width', '0.9');
  badge.setAttribute('pointer-events', 'none');

  var label = document.createElementNS('http://www.w3.org/2000/svg', 'title');
  label.textContent = n + (n === 1 ? ' loot container' : ' loot containers');
  badge.appendChild(label);

  inner.appendChild(badge);
}

// Extra search terms, so typing "loot" or "chest" in the ledger filter finds
// every island that has some.
function wbLootHaystack(inv) {
  var n = wbLootCountOf(inv);
  return n ? ' loot chest chests container containers' : '';
}
