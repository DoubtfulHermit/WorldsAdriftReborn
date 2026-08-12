# SKEPTIC'S REPORT — read this one first

## THE MINIMUM PLAYABLE LOOP — five elements, nothing more
A player walks to a **metal nugget** visibly on the island surface — the same nugget, same
entity id, the other player can see. They point and press one button. The server hears
*"peer P used slot 0 on entity N"*, credits them, and pushes back a replacement 1081
`inventoryList` **and** an `8060 ReceiveSalvageFeedback("iron", 12)` which makes the game's
own HUD render **"Salvaged Iron x12"** with zero UI work from us. The nugget changes state so
both players — and anyone joining ten minutes later — see it gone. They open the Multitool
Craft tab (**no world entity needed**) and craft one item that consumed that iron.

**A shared node · a heard input · an authored yield with visible feedback · a shared
depletion · one sink.** Everything else in the nine specs is out.

## RECOMMENDATION: build it — but not the version any spec describes, and not first
**M0 (log hygiene) → RECONNECT → the §8 experiment → M1–M6, persistence spliced after M4.**

**Reconnect is not a feature — it is a test harness.** The scarcest resource in this project
is a two-client window with a remote human. Reconnect converts a window from "one server
build, then relaunch both clients through login and character select" into "type RETRY,
iterate again". **It is the only item on the roadmap that multiplies the rate at which every
other item can be validated.** That reframing is the most valuable line in the report.

**And the case FOR gathering that no report made explicitly:** it is the only item on the
roadmap that gives the second player **a reason to exist**. Ships, weather, islands and
reconnect all make a solitary world nicer. A contested, depleting, shared resource is the
first mechanic where another human changes your experience.

## ⭐ CORRECTION: THE FLOATING "+N" POPUP EXISTS, AND IT IS FREE
The brief (and my summary) listed "no floating +N popup exists" as a known gap. **Wrong.**
`FeedbackScreen.OnFeedbackSalvagedReceived` → `TryIncrementingActiveSalvageFeedback` renders
**`"Salvaged {item.name} x{Quantity}"`** as a pooled, accumulating, 10-second HUD card (max 3
concurrent). Driven by `8060 FeedbackListener` — **already seeded**, server-owned, **no grant
needed**. One method call: `AddReceiveSalvageFeedback(new ReceiveSalvageFeedback(id, qty))`.
`findings-tools` buried it under "free UI polish"; **it belongs on the critical path**,
because it is the only harvest feedback that does not depend on the inventory rendering
correctly.
*Caveat:* an unknown `itemTypeId` makes `LookupItem` return null and `FeedbackScreen`
dereferences `.name` → **NREs the HUD**.

## ⭐ NEW BUG: `stackingMax = -1` CORRUPTS CRAFTING (nobody caught this)
Known: it hides count labels. **Unknown until now:**
`CraftingMaterialSlot.ReturnItemToInventory:405` computes `Mathf.Min(CurrentAmount, -1)` =
**−1**, calls `AddToInventory(itemTypeId, −1, …)`, then `CurrentAmount -= num` — which
**INCREASES** the slot amount. `if (CurrentAmount <= 0)` never fires and **the slot can never
be emptied.** Pulling a material out of a crafting slot *adds* material.

## ⭐ NEW, MEASURED: THE MOD LOGS ITSELF INTO THE GROUND CLIENT-SIDE
In the latest two-client session log (54 MB) the single most frequent line is **ours**:
**327,713 × `not touching BossaNet.UseBossaNet`**, plus 22,147 × `not touching Logging.Level`.
That is **~10× the NRE count and roughly 90% of all log lines** — the client-side twin of the
server bug fixed in 766eafd. **Fix before the test session or the session's evidence is
buried.** Baselines for comparison: `Attempting to add existing id` 22,000 · `Dear QA` must
stay 0 · `ChararacterDrunk` 35,147.

## ⭐ THE 3–4 DAY SAVING: node relay does NOT need a rewrite
`findings-node-relay` proves correctly that `RelayToOtherPlayers` cannot address a non-player
entity, then concludes 600–900 lines and 5–6 days must land first. **That conflates two
things.** `RelayToOtherPlayers` exists to forward **client-originated** updates. Node state is
**server-authored** — nothing client-originated ever needs relaying to a node. And the server
can **already** address any entity on any peer:
`SendOPHelper.SendComponentUpdateOp(destination, entityId, ...)` takes an explicit entityId.
Broadcasting node state is `foreach (peer that has the node) SendComponentUpdateOp(...)` —
**tens of lines, not hundreds.** What is genuinely needed is much smaller: the node registry
(already a blocker in its own right) and two lines of ownership hardening. Its late-join
analysis and its "mutate the registry first, then broadcast; never broadcast to a peer that
hasn't been sent the node" rule are correct and worth keeping.

## ⭐ THE 1211 GRANT IS OBSERVABLE WITH NO NODE AT ALL
`findings-tools` set P0's exit criterion as "a `UseItemKeyPressed` with a non-invalid target"
— but P0 spawns nothing, so on its own terms it cannot meet it. **It can, for a reason nobody
found:** `PlayerLookingAt.GetInteractiveObject:159-170` accepts *either* an
`InteractiveObjectVisualizer` **or** any `EntityFinder.IsSpatialOsEntity` — **including the
other player's avatar.** Aim at the other player, left-click, read the log. **The cheapest
meaningful experiment in the corpus.**

## ADJUDICATED CONTRADICTIONS
- **Metal vs trees** → **metal, unanimously.** The tree MVP is dead; do not un-retract it a third time.
- **Nugget vs databank** → **nugget.** The databank is marginally cheaper and is a **dead end**
  (yields nothing without the scan/knowledge stack). Cheapest-to-spawn is the wrong target
  when they differ by one component. Keep boulder as fallback #1, databank as #2.
- **Beam vs interact** → **interact first.** `findings-tools` says 2002 is required even for
  pure harvest — **true of the beam, not of harvesting.** Interact needs **one** grant vs
  five, no serializer branches, no `maxBoltDistance` guess, and avoids five more 60 Hz
  streams on a relay with **no allowlist**. The beam is the right long-term verb; build it
  second on a proven wire.
- **"Smallest observable change"** → the tool-unlock ladder is genuinely the cheapest
  *player-visible* change **and a dead end for gathering**. Best demo, worst foundation. Keep
  it as a **control** in the probe build: if the unlock card doesn't appear, the push path is
  broken, not the gathering logic.
- **Server-authored positions** win decisively, plus one point the winner missed: the
  handshake would be **the first thing to punch a hole in** the `Players.Owns` gate that makes
  node server-authority work at all.

## ASSUMPTIONS NOBODY IS TESTING (ranked)
**A. The coordinate chain has never been checked against a running client.** `findings-node-
spawning` is the strongest empirical work in the corpus — but the island world origin is
**derived, not observed**, and four transforms chain off it. **Exactly the shape of the
"positions exist nowhere" error it corrected: rigorous where measured, unverified where
inferred.** If wrong, every node is misplaced and the failure looks like "it didn't spawn".
**B. That a non-Traveller prefab name resolves at all** — two loaders, unknown casing, and
`MakeEntity` has **no else branch**, so failure is silent.
**C. That the interest response doesn't abort.** All four call sites pass
`failOnComponentInitError: true`. **Cheapest mitigation nobody proposed: pass `false` for node
entities** — the mirror path already does. Converts "invisible node, no idea why" into "node
with a named missing component".
**D. That granting 1211 doesn't reproduce the desync.** ~60 Hz publish, and the relay has
**no allowlist at all**. `findings-tools` scopes the allowlist as a separate step —
**disagree: one commit.** Shipping the grant alone re-runs the experiment that already failed.
**E. `IsWaitingForServer` will brick the first session** — three latches, no timeout, no
rollback; the server answers 1 of 15 `1082` events. **The first time a tester drags an item
their inventory is dead.** Test costs nothing: it is already true today.
**F. The two clients may be the same player** — the uid collision, tolerable until anything is
keyed on uid.
**G/H.** Whether `FeedbackScreen` is instantiated; whether the beam route is needed at all.

## EFFORT — ~14–21 WORKING DAYS, plus 2–3 two-client windows
M0 hygiene **0.5–1 d** · M1 per-entity seeding + node registry **2–3 d** (corpus said 1; it is
a 550-line switch **uncovered by the unit suite**) · M2 one visible node **1–2 d** (three
silent failure modes that all present as "nothing appeared") · M3 1211 grant + allowlist
**0.5–1 d** · M4 yield + InventoryStore + item-id allocator **3–4 d** · M5 depletion +
late-join replay **1–2 d** · **M6 one craftable recipe 5–8 d — a WEEK, where
`findings-crafting` says step 0 is 2 lines.** Those 2 lines make a recipe *visible*, not
craftable; the transaction has three latches with no timeout, two NRE traps, a length
invariant that throws, and a bricking mode where the player cannot even switch recipe.
**Anyone quoting "a few days" is quoting M0+M3.**
Reconnect **~2 d**, not 1 — `GameState.ComponentMap` is **never cleaned** by `ForgetPeer`, and
a reconnecting client reusing a peer slot has never been reasoned about.
**No estimate includes design time** — the damage→yield and quality→stat formulas must be
invented, so gathering is partly a design project.

## POLISH GAPS — what will feel broken even when it works
**Harvesting will look like telekinesis** — the remote seed has no 2105/2106, so player B sees
player A standing motionless while a rock loses health. **A bigger immersion break than any
missing number**, and it lands the moment two people harvest together.
**The very first inventory grid is already wrong** — the default glider at (0,0) 3×4 spans
y 0–3 and overwrites the belt blockers.
**Dragging anything greys the panel forever.** **Every gathered material reads "Quality: 0".**
**Once the node is gone the island is empty forever** — budget a respawn timer before the
*second* session. **Collect SFX is suppressed exactly when a tester has the panel open to
watch the item arrive** — grant metal, ≥6, panel closed. **Relog loses everything** — the gap
most likely to make people stop playing, and why persistence belongs right after M4.
Already in today's log: `"Schematic is null"` ×4, and the crafting panel shows two phantom
values (12 s, 30 kg) seeded from a serialization scratchpad.

## THE §8 EXPERIMENT — one build, one human, two clients, ~90 minutes
**Design principle, and why it differs from every plan in the corpus: NO PROBE MAY DEPEND ON
AN EARLIER PROBE PASSING.** Every corpus plan is a chain, and a chain spends a whole window
discovering step 1 failed. This is a **ladder of independent, timer-driven probes**.

| T | probe | answers |
|---|---|---|
| +20 s | **tool-unlock control** — push `8051 = 6` | does the push path reach a live client at all? if this fails nothing below means anything |
| +30 s | 1088 identity grep — **zero code** | has the identity chain ever run |
| +40 s | **three nuggets**, exact `fixedPoint190602`, **three prefab spellings**, `failOnComponentInitError: false` | A, B, C + non-Traveller asset-ack. If it renders 4096× away, A failed and is instantly diagnosable |
| +60 s | **grant 1211** | D, H — and **aim at the other player and click**: yields a non-invalid target *even if the nuggets failed entirely* |
| +80 s | **unconditional yield on a timer** (independent of 1211): 1081 push + 8060 event | G, does the count render (`stackingMax` in one observation), Metal SFX with panel closed |
| +95 s | tester drags the iron one cell | E — confirm the brick so it gets budgeted |
| +110 s | **move a nugget 500 m down via 190602 to every peer** | **settles the largest scoping question in the corpus** — can the server address a non-player entity with existing helpers? |
| +130 s | **client 2 joins late, same character** | late-join replay + the uid collision |
| +150 s | both walk for two minutes | regression: did any of it break working movement |

~200–300 lines of throwaway/first-draft server glue. No C++, no new wire message, no
substantive client change. **Retires A–H in one window before committing 20 days.**

## COULD NOT DETERMINE
Whether the extracted island world origin is correct — **the single biggest unknown.**
Which asset loader serves a node prefab and what casing it wants. Any node prefab's
`RequiredComponents`. Whether `FeedbackScreen` is instantiated. Whether the interact verb
produces a prompt on a nugget. Whether the 60 Hz 1211 stream causes visible harm.
The damage→yield and quality→stat formulas — **unrecoverable, must be invented.**
Did **not** re-verify the UnityPy prefab findings — the most expensive results in the corpus,
taken on trust, and the ones where a second opinion would be most valuable.
