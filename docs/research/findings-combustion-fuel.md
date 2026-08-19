# Findings — combustion and fuel

**Status:** current. Written 2026-08-19 on `feat/ship-fuel`.

This file existed only as a citation for a long time: five code sites pointed at
it and it was not in the tree. It now says what they were pointing at.

**The long-form treatment is `docs/plans/feature-roadmap.md` §12**, which carries
the full retail reconstruction, the component field tables, the constraints and
the phased plan. This file is the short evidence index plus the §6 the code
citations actually want.

Source-of-truth order, as always: current code and tests, then a live log, then
the retail decompile at `/home/ttanurhan/Games/WAReborn-decompiled`, then this.
The community wiki is the **weakest** source here and is labelled WIKI wherever
it is used.

## 1. The chain, one line each

1. Islands spawn fuel pods — fabric entities literally named `"Egg"`
   (`acs/IslandProxyVisualizer.cs:160-175`, `acs/EggPreprocessor.cs`). **PROVED.**
2. You salvage them with the gauntlet, like a metal node, and get the raw
   material `"fuel"` (`acs/InventoryItemManager.cs:18`). **PROVED.**
3. A ship carried fuel TANK entities holding `1106 FuelTankState
   { capacity, fuel, subtanks }`; the ship root aggregated them into
   `AccumulatedData.field5_fuel_tanks`, a `Map<EntityId, FuelData>`. **PROVED.**
4. You refuelled by holding E on the tank — `ShipFuelTankPreprocessor` bakes
   `InteractiveObjectVisualizer` with `InteractVerb.Activate`. No `Refuel` verb
   exists in the enum. **PROVED.**
5. Engines were the consumers: `1104 FuelConsumerState { fuelTankId, attached }`
   bound one engine to one tank; burn rate rode on
   `1116 ShipEngineState.consumption` and `1113 ShipControlState.fuelConsumption`,
   continuous and throttle-driven. **PROVED.**
6. How thirsty an engine was, was a crafting stat: `fuelEfficiency`, rolled by an
   engine's Mechanical Internals and Propeller slots (`acs/SchematicData.cs`).
   **PROVED.**
7. The gauge is `1105 FuelGaugeState { capacity, fuel }`, and
   `FuelGaugeVisualizer` `[Require]`s that and nothing else. **PROVED.**
8. Empty did nothing visible: no `fuel <= 0` branch, no warning, no sound, no
   localization key anywhere in the client. The ship stops accelerating and
   coasts; it does not fall, because lift is `ShipLiftState 1258`'s and the
   client only replays server motion. **PROVED by absence, three ways.**

## 2. The canister yield — the one preserved number

3 salvage shots, **8 + 8 + 9 = 25 fuel**. **WIKI/RECOVERED**
(`worldsadrift.fandom.com/wiki/Fuel`, `/wiki/Resources`, `/wiki/Mining`),
encoded verbatim in `Multiplayer/FuelCanister.cs` including the uneven last
shot, which is the distinctive part of the real curve.

## 3. What is unrecoverable, and why

Everything about *moving* fuel — transfer amounts, the depletion loop, tank
capacities, per-engine burn rates, the value of one fuel unit — lived on the
GSim (Scala), which is gone. Three independent confirmations that it is not in
the client: `ShipConfiguration.cs` ships ~40 flight tunables and no fuel entry;
`ConfigKeys.cs` has no fuel key; every fuel schema field is optional with a
proto default of zero.

Everything this server picks is therefore **WAREBORN TUNING**, listed with its
reasoning in roadmap §13.5.

## 4. What this server can and cannot reproduce

Two hard constraints, both from the shipped client:

* **No fuel tank prefab.** The 349-name entity-prefab census
  (`Multiplayer/Ship/client-entity-prefabs.txt`) has `fuelgauge`, `fueldeposit`,
  `fuelextractor`, `fueleggspawnerequip` and `egg` — and no ship fuel tank. So
  the per-tank 1106 / per-engine 1104 topology is not reproducible, and fuel is
  per-HULL here, which is the level retail's own aggregation used anyway.
* **A verb cannot be invented.** `InteractiveObjectVisualizer` caches
  `Interactions.FirstOrDefault(i => i.verb == Verb)` once in `OnEnable`, against
  a verb baked into the prefab at export. The **atlas sky core** is the only ship
  part with a baked `Activate` and no other claimant, so it is this server's
  refuel door.

## 5. The gauge defect

`FuelGaugeVisualizer` has exactly one `[Require]` and it is **1105**. The
catalogue seeded that row **1236**, which the prefab has no reader for, so the
visualiser never enabled and the needle could never move — silently, because a
Unity visualiser logs nothing when a `[Require]` is unsatisfied. Fixed on
`feat/ship-fuel`: the row now seeds `1105, 1236` and `ComponentsSerializer`
serves 1105 from the hull's tank. Roadmap §13.3 enumerates every fuel-related
visualiser and what each requires, because "one component is enough" is the
assumption that has cost this repo four dead props.

The needle sweeps **+135° (empty) to −135° (full)**, 270°, with four odometer
digits and a powers-of-1000 magnitude roller. It is smoothed twice — a
`DelayedInterpolator` with `Delay = 2.0` seconds, then a lerp — so **it is
supposed to lag about two seconds behind the wire.**

## 6. Honest unknowns — live capture only

These are what the code citations point at. None can be settled headless.

1. **The fuel canister's exact resolvable prefab name.** `"Egg"` is the best
   match in the verified prefab-names table for the prefab the fuel
   `EggPreprocessor` runs on; it is not proven to be the fuel variant. Override
   with `WAREBORN_FUELPOD_ASSET`.
2. **Whether the stock client renders a canister and accepts the beam** with
   exactly the served set 190602 + 1099 + 2102 + 1016. `1235` (the break state)
   is not served, so the break VFX/SFX may not fire even when the salvage works.
3. **Canister relocation.** Retail canister locations changed on every island
   resource reset (the ~1.5–2 h "understorm" that also replaced ore nodes and
   scrap piles) — **WIKI**. This build places them once. Flagged, not built.
4. **Whether `"fuel"`'s `amountRequired` in the shipped refdata is retail's.**
   `FuelRefdataIntegrityTests` pins it at the shipped value rather than a chosen
   one, precisely because we cannot prove it.
5. **Whether the sky core shows an E prompt at all,** now that it is served an
   `Activate` entry for the first time. The verb is baked; the serve is new.
6. **What that prompt says.** Predicted: nothing but the generic Activate glyph.
   `InteractionEntry.description` is transmitted and **never rendered** by the
   shipped client (zero non-gencode references), and there are no fuel strings in
   `LocalizationSchema.cs`.

## 7. Where the code is

| concern | file |
| --- | --- |
| canister yield + salvage ledger | `Multiplayer/FuelCanister.cs` |
| canister placement + item id | `Multiplayer/FuelPods.cs` |
| tank numbers, burn/deposit maths | `Multiplayer/Ship/Fuel/ShipFuelPolicy.cs` |
| per-hull tank ledger | `Multiplayer/Ship/Fuel/ShipFuelLedger.cs` |
| gauge push budget | `Multiplayer/Ship/Fuel/FuelGaugePushTracker.cs` |
| burn, refuel, gauge push, thrust gate | `Game/ShipFuelService.cs` |
| the 1105 serve | `Game/Components/ComponentsSerializer.cs`, the `componentId == 1105` branch |
| the refuel interact | `Game/PartInteractionService.cs` |
| the throttle clamp | `Game/Components/Update/Handlers/ShipControlInput_Handler.cs` |
