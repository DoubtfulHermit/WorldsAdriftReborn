# Research reports (2026-08-08)

Seven parallel investigations into the things standing between "two players can
see each other move" and a world worth playing in. Each `brief-*.md` is the
question that was asked; each `findings-*.md` is the answer.

**Read this before trusting any of it: nothing in these reports was executed.**
Every one is static analysis — the decompiled client, this repo's source, and
(for weather only) the shipped log files. No server was run, no client was
launched, no patch was built. Line references are exact and were checked;
predictions about runtime behaviour are predictions. Where a report says
"unverified", it means a competent reader could not settle it from source alone,
and that item is where the risk lives.

They corrected several statements in the archived
`docs/archive/2026-08/roadmap-2026-08-08.md` and in `docs/hosting.md`, and added
rules 13-16 to `docs/multiplayer.md`. Corrections are dated in place.

| report | what it settles | confidence |
|---|---|---|
| [findings-weather.md](findings-weather.md) | The `WeatherCellCoordsC` spam (upstream #34). Refutes the previous account completely: it is a log line, not an exception; nothing is aborted; the 16,012 NREs in the logs are a separate bug. | **Highest of the seven.** The only report with measured evidence — 10,280 and 212,214 error blocks counted in real logs, the abort hypothesis disproven two independent ways (`Dear QA` = 0; weather is last in the config), and the ECS config extracted from the shipped assets. The perf estimate is inference, not a profile, and the recommended fix is untried. |
| [findings-world.md](findings-world.md) | Multi-island worlds. No layout ships; the authoring format survives in the decompile. Establishes fixed point as Q52.12 (÷4096). | High on the negatives — the "no layout anywhere" result is exhaustive (255 bundles, all asset files, GameDB decrypted). The Q52.12 decode is read directly off the conversion source. The three-island slice is a proposal; island radii are unverified, so the 900 m spacing is a guess. |
| [findings-robustness.md](findings-robustness.md) | Reconnect. The game ships a working RETRY/QUIT path; four defects in our shim stop it firing. ~40 lines of C++. | Good — the payoff chain is traced end to end through named files, and ENet's timeout behaviour is read off the vendored source. But the riskiest step (`ENet_Deinitialize` → `ENet_Initialize` on a second connect under Wine) is untested, and the ~30 s recovery figure is a projection. Also contains one live server bug found by reading (`loopTick` used as a wall clock). |
| [findings-entity-removal.md](findings-entity-removal.md) | Despawn. The client-side removal path already exists and works; exactly one link (our discarded callback) is broken. | Good on the client half — the whole path is named and `RemoveEntityOp` already exists on both sides. The ~1 day estimate assumes the MSVC proto registration path works, which was not tried. Its most valuable finding is a risk, not a fix: the 5-channel cap fails silently and there is no version check anywhere. |
| [findings-persistence.md](findings-persistence.md) | Characters and inventory. Identity is already solved over 1088; the true inventory blocker is that the 1082 handler only logs. | Partly **shipped and confirmed** — the roster half became "Persist the character roster", where the client-side rules it names (`Cosmetics == null`, `hasMainCharacter`, the save-response reparse) were tested against a real server and held. The inventory half and the claim that 1088 identity lands at runtime remain unobserved. |
| [findings-resources.md](findings-resources.md) | Harvesting. Nodes are real entities; positions come from the client; the multitool beam cannot fire at all today. | Mixed, and honest about it — **it retracts its own MVP mid-report** (the tree route dies on a `[WorkerType]` attribute). Q1 and Q3 are settled hard (465,571 baked props and zero harvestables; an exhaustive 0/255 bundle scan with positive controls). The damage→yield formula is unrecoverable and must be invented, so any harvesting design is partly a new game. |
| [findings-ships.md](findings-ships.md) | Ships. Viable: motion is gated by authority, not worker type. Three Harmony patches plus scalar state synthesis. | Structurally strong, operationally unproven — the authority-vs-worker-type argument is read off the injection site and the attribute cache, and the stock client's receive half already implements the split. But no ship prefab was opened to confirm `ShipPreprocessor` survives into the bundle, and the `fsimIdHash` echo-suppression risk (shared `WorkerId` ⇒ every client silently drops every ship update) is unverified and would sink the whole approach. Check it first. |

`ecs_config.json` is the client's FixedUpdate system tree, extracted from
`sharedassets0.assets` — evidence for the weather report, and useful on its own:
the entire configured gameplay ECS is seven systems, two of them weather
bookkeeping. Player movement, sailing and the rest are ordinary MonoBehaviours.
Any theory that blames ECS scheduling for a gameplay outage is structurally
unlikely.

## World-expansion additions (2026-08-14)

- [findings-island-pipeline.md](findings-island-pipeline.md) audits PR1's
  stable island identity and Haven-preservation boundary.
- [findings-wamap-import.md](findings-wamap-import.md) records PR2's external
  Jerodar/WAMap parser, exact source revision/schema, integrity results,
  coordinate evidence, anomalies, and the boundary with Bossa's release-era
  production placement MapFile.
