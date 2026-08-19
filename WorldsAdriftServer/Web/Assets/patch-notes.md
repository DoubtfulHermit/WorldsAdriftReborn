Worlds Adrift shut down in 2019. Wareborn is a fan-run server that puts it back online.

Every commit, newest first. 535 of them since 2026-08-07. Merges are left out - they only repeat what the commits under them already say.

## 2026-08-19 | 48 commits

* daecd01 Correct the production claim: the release world IS on, and the numbers decompose
* 35a0317 Kill a creature with the beam, not with a weapons system
* 5febf4b Size phase 7 from evidence, and withdraw the corpse-grounding plan
* 6241cc4 Make regenerating the patch notes a step of the deploy
* 77863d7 Record the gate results for the three landed phases
* 38a51b5 Separate two comment blocks that ran together
* 893fa93 Record what landed, and fold the maintainer's lifecycle account into phase 7
* 0aa0fe8 Pay plant fibre and berries off the cut that already pays wood
* 3774e15 Make the live ground-profile cache safe for parallel test classes
* 67d3709 Lay a felled log along the ground instead of flat through it
* 058877d Draw a deposit's metal from its island instead of hardcoding iron
* d756972 Pay a node's own quality into the item it yields
* 7686267 Offer the alliance crest as a PNG, not only as a vector
* 707a28e Plan the resource economy, and correct three audit findings first
* 2fc5846 Size the save-menu preview so the crest is not clipped
* 8bb53d0 Record the tree-fall deploy
* e73cefa Ask before Delete all throws the layers away
* a7cf2d0 Give the portal tabs, and the emblem a layered editor
* 5a69250 Show the log that falls, and make the trunk break up piece by piece
* 0a7506c Record the 2026.08.19-2 release that unbreaks the patcher
* 0fc418e Say in the journal why a portal action was refused
* d7d00bf Record the PLAY-hang outage and its cause
* a99926a fix(mod): PLAY connects again instead of loading forever
* 8fd1a08 Make /patchnotes a commit log instead of a write-up
* 2184b17 Give the server screen Bossa's own copy back, and only grey the PvE card
* 153728a Record the 2026.08.19-1 patcher release
* 1621a28 Record the account-portal deploy
* df166e6 Sign in lands on an account, not on a download button
* e5babd1 Record the /patchnotes deploy, and say the log is a log
* 7499fce Write down what shipped, and put it at /patchnotes
* b33cf4b Serve the welcome message, and let an operator edit it
* 2e0406b Say which label the welcome copy replaced, and catch the second one
* d6dfabc Let the welcome scroll be written by whoever runs the server
* aa0e240 Send PATCH NOTES to a page a person can read
* dc07538 docs: why the crest URL must be plain http (client TLS tops out at 1.0)
* b90894d Alliance crest URL names the origin the request came in on
* 57823d4 Give alliances fifty drawn devices, and give players the vector
* 8a1335c One whale for the whole world, migrating from zone to zone
* 5692044 Give every alliance a crest, and let its leader compose one
* 81fde92 Stand the Revival Chamber up instead of burying it to its waist
* 44428c0 docs: record what the launch actually proved
* befb951 feat(mod): stop telling players to go and look at things that are gone
* e838f7d feat(mod): login screen links and copy stop pointing at dead Bossa
* 3229446 feat(mod): stop offering a PvE server that does not exist
* db52287 feat(mod): pressing LOGIN logs you in
* f460087 feat(mod): the client no longer needs Steam to start
* 2bd3113 fix(mod): one broken patch class no longer silently kills the rest
* ee8e483 docs: plan for cutting Steam out of the client launch path

## 2026-08-18 | 101 commits

* 951ab0b fix(map): stop the viewer sparkline's svgEl shadowing the renderer's
* 84b5d37 Draw the ship on its card, not just its outline on the map
* d5c9b87 Count who is watching the map, without being able to say who they are
* 708686d Let two fingers zoom the map, not fight over which one is panning
* 14feab5 Stop a hovering ship being machine-gunned with whale song
* 086c87c Tell the operator where to stand and when to look up
* 6d52d9d Stop the public map explaining itself, and let you find a traveller
* d914ceb Let the soak see the whale, and make one bad brace fail a test instead of a console
* 655b010 Take the joke off the whale
* 54cb421 Put the whale in the sky, give it a voice, and draw it on both maps
* beecec4 Give the world one animal big enough to be an event, and a path it can be found on
* f25ae26 Make the gate able to see a calf, and the map able to show one
* dc7d033 Let the calf be a quarter the size of its mother
* e108cac Give a school a mother and a calf to travel in it
* 9767999 Answer 1166 before anything is allowed to be small
* f3e6642 Stop the whole world sharing one heartbeat, and stop it being emptier than the world it replaced
* 491d537 Teach the schools to feed, dive under the rock, and cross to the other bloom
* 6940bf6 Polish the interest rings: distinct label anchors and a dotted wildlife ring
* 8ac46c0 Give the populations a rhythm: islands swing between bloom and collapse, and the rays trail the food
* 13f8961 Publish the interest picture in the stats snapshot (schema v10)
* 1b11946 Wire the ecology: field-following schools, sized populations, and both maps drawing it
* 5ce067c Add the operator command panel and the interest-and-streaming view to the console
* 345c549 harness: exec the game server so cleanup kills the server, not its subshell
* 765cd84 harness: run cleanup on SIGTERM/SIGINT, not just EXIT
* bf08077 Build the public map on the shared renderer, and close its label seams
* d06e602 Pin the cross-version tolerance the operator fields depend on
* beeb7ac Extract the console's front end into shared asset files
* 10fbb19 Stage the ecological field and the capacity model, unwired, with the wiring designed
* 889e908 Carry the resolved target out of resolution instead of on a placeholder command
* 8bad020 Stop the teleport-to-a-player warning crying wolf about a fall
* 07aada3 Give the mantas their tails and genders back, and the jellies their four species
* 1eacfe3 Strip 33 tests that assert what a stronger neighbour already asserts
* c0ea20a Let an operator act on any player, not only themselves
* f988c50 admin: put the fleet on the map, as the hulls their owners built
* 0d6a1ea Prove the ground under the fauna gate, and adopt the layered ecology as the target
* 676b8d3 Serve an anonymized public live map feed at /map
* ff37ed0 Reconcile the terrain-readiness blocker: it was the missing load barrier
* cc4624f Cover the shared endpoints alliances borrow from crews
* 96f9241 Record what the alliance work leaves unproven without a live client
* 1bdfe78 Stop resolved invites lingering in a player's own invitations list
* a33cc63 Record the merged gate numbers in the interest findings
* 59563f5 Make the social sheet's spinner turn at a speed, not at a speed per frame
* 579b495 Refuse an alliance boot addressed to an alliance the caller is not in
* 1cfda9c Serve the alliance half of the social API, so CREATE stops answering E00001
* 3e4c243 Give every tier-1 island wood, and put it where a player lands
* c3571f2 Give island resources island-keyed interest, so an island stops looking empty
* c5e1d9c Cost the wildlife before growing it, and find out why it is expensive
* c7b03ea Answer the respawner question, and stop the client throwing on every E
* 66c68e9 admin: put the world's wildlife on the map, moving, live
* 79e00ca Move the chamber to the spot the user measured out for us
* 7ace330 fix(fauna): face the direction of travel, per species
* f50a340 admin: make the world map an interactive map, with real island detail
* 65e9d50 Say why the shrine refuses, and clear the ground it stands on
* f21e4d4 fix(fauna): island-keyed interest, recovered patrol band, and schools
* c25398a Stop a crew's invites from destroying the whole Social Sheet
* 1076ae9 Bring the chamber back and put the teleporter in it
* 49c02ce Ship Nightfall as translucent zones, and put the island inventory on the page
* 3df1da2 Make the social host follow REST_ServerUrl instead of copying it
* c00f914 Give the world map back its colours, and say what is on each island
* 2af92ad Say plainly what the spawn seed's 2 m stand-off means for the walk
* f14b128 Move the shrine out of the metal camp, onto an object you can reach
* ae99171 fix(admin): make the world map's tier colours an ordinal, colour-blind-safe ramp
* aa2259c Make the Wilderness shrine's interact prompt reachable
* 2865af5 perf: fix in-world frame rate — collapse Wine's fsync futex herd (51 -> 120 fps)
* 2ae8ea3 Record that island fauna is wired, and what it is not
* 0f42f76 Let the relay bot see island fauna
* b901cd5 Put island fauna on the wire
* de55536 Record the b652034 deployment and the v7 migration
* b652034 Return knowledge gain to stock values
* 455066f Run the relay soak natively, so the deploy gate works again
* 8ee2398 Assert the recovered fauna movement instead of the island transform
* 7a596d5 Add pure island fauna core for jellyfish and manta rays
* 7bde571 Stop reissuing a live crew id, and write the social API contract down
* 94d6bc3 Write down the shrine's crew rule and what it is still guessing
* ab08331 Stand the Wilderness shrine on Haven and wire it to teleport
* 87980e9 Reseat trees around the newly inferred deposits
* f455451 Record the island resource population evidence
* de0c570 Populate metal on every island from the surveyed tier cohort
* de3b2c9 Add a metal-table provenance to the island survey profile
* f7fc470 Decide where the Wilderness shrine sends a player
* 18bbd48 Make hull mass change how a ship flies
* c2099aa Give the ship hull its materials on the wire
* 3228a71 Accept material variety when crafting
* b6c11fe Grow the surveyed species on every wooded release island
* 2e0c85d Author harvestable tree seats for the release world
* 9336a93 Drop, drive and retire the log a cut tree sheds
* 5ef0023 Author the fall a felled tree section never had
* 859a257 Point the client at our social server and stop forcing alliances off
* 02e05d7 Serve the Bossa social API from the login server
* 07ae259 Add schema v7 for social invites
* 6642e7e Key the crew checkout snapshot on the ledger key
* 9ba4492 Record what Tier 1 already does and what it deliberately does not
* 298a8b5 Cover the Tier-1 rollout and its connect cost
* 4480270 Give release-world deposits their atlas shards
* bbe14ef Name the release-world tiers so the Wilderness cannot drift
* 7d898bc Load island asset bundles asynchronously
* 12a398c Attribute frame spikes to asset loading
* 8350f6b Never restore a player onto terrain their client does not have
* a1eec9e Select the pre-alliance crew UI
* 7c245a6 Record the 958c8e1 deployment and the v6 database migration
* 958c8e1 Grant the client authority over the crew action component

## 2026-08-17 | 37 commits

* 567b0b3 Wire crews to the client
* 3122ffb Persist crews
* 6f18a41 Add the pure crew domain, and a plan to revive crews faithfully
* cb5c621 Make the compact island shell solid, hard-edged and hazed
* d827501 Record the c31e8be deployment and the v5 database migration
* c31e8be Return a player to where they logged out
* 5760dc2 Give the compact island shell its underside back
* 54c70a0 Put the compact island shell where the island actually is
* 481b288 Defer unbound world registrations instead of killing the boot
* 34fd642 Record the ccfb138 deployment and repair the patcher default pack
* 7771b20 Label admin map provenance and reconcile island counts
* f7c53b6 Prefer the retail-LOD distant island shell
* 3d63eb6 Add staged complete release-world rollout
* d910c76 Record corrected zone signage deployment
* 5f1afbb Clarify release world tiers and district signage
* b12a92a Record authentic SVG map deployment
* 17cc5d2 Redesign release world map as authentic SVG cartography
* e4065bb Record live world map deployment
* 3d64a7f Add live release-world operations map
* 22f3a9e Record island shell activation fix
* bede97e Start island shell waiter after activation
* c6331b3 Record distant shell deployment
* 7c99dac Add distant island visual shells
* 9b189b6 Record aboard interest deployment
* ee19d2b Record Trades proximity lifecycle acceptance
* 7fab2e2 Prefer ship-derived interest while aboard
* d0e994b Record authoritative interest deployment
* 1aa9fe4 Follow authoritative player transforms for world interest
* 2e88b31 Record terrain re-entry acceptance
* 09ffc22 Record terrain return fix deployment
* b52f504 Release terrain after confirmed teleport return
* c72823a Record terrain acceptance deployment
* 069a372 Restore metal deposit visual variety
* c2fa596 Seed the complete tier-one B3 map foundation
* eef206c Document consolidated terrain checkout milestones
* fa83318 Observe optional terrain checkout from /admin
* 7cbb376 Stream optional island terrain by peer interest

## 2026-08-16 | 13 commits

* 710b441 Archive Worlds Adrift community engineering data
* ab908d2 Seed recalled ship motion with persisted rotation
* 41a91c5 Complete component lifecycle before entity checkout removal
* 8794f90 Complete client cleanup for removed ship entities
* e2d88f0 Recall ships directly above selected players
* 25442cd Confirm teleport arrivals from bounded transforms
* fbccb4a Document Mental Facility staged deployment
* 07270f1 Add guarded tier-one island test landing
* 3a28706 Correct first region rollout to tier-one B3
* effc639 Seed opt-in first-region terrain topology
* 768be8c Document 175839f production deployment
* 175839f Redesign admin simulation fabric
* 6f89710 Route world interest through local domains

## 2026-08-15 | 25 commits

* ec22297 Accelerate opposing helm input without increasing network cadence
* e6adf29 Trace helm input latency end to end
* 49de2cf Predict local helm feedback without a server round trip
* 9d50267 Guard hierarchy teardown during streamed entity removal
* 585541e Document 634aca2 production deployment
* 634aca2 Stabilize two-peer ship and avatar replication
* 8d95523 Rebuild ship checkout after admin recall
* 191daf1 Restore ship-relative remote avatar rendering
* 5929b46 Prevent stale spawn plans crashing ship persistence
* a2d2b4d Stop reliable upstream movement spirals
* 61dfbc3 Add real-wire two-peer ship acceptance
* adea795 Add deterministic two-peer ship acceptance gate
* 4a18bf2 Record ship recovery console deployment
* d867781 Replace ship nudge diagnostic with recovery controls
* 7a179dd Record professional admin console deployment
* 48b381d Redesign admin as a simulation operations console
* 4208424 Record admin control panel deployment
* ab9bc94 Document authenticated world operations
* 18d89b3 Turn world inspector into an authenticated control panel
* fafd9ba Document domain-aligned relay and local inspector
* a5bed13 Align aboard relay with ship domain frames and add inspector
* d648441 Record headless ship catchup correction
* 9143c5a Keep ship domains out of generic catchup
* 2b96f38 Record remote ship connect deployment
* 718d926 Gate remote ship domains during connect

## 2026-08-14 | 44 commits

* f132cec Record ship-domain corrective deployment
* 489517f Fix ship steering, passenger coherence, and re-entry
* 7a69e48 Use island-scale visibility for ship domains
* 6a2273f Add coherent local ship simulation domains
* d27e9f2 Record multiplayer server deployment
* 8bc961d Document multiplayer replication incident
* ba5987a Fix late-join entities and ship replication stalls
* 217959a Document native client crash fix release
* 3a7cd31 Fix native spawn decoder heap corruption
* 3e815cd Document NTP-independent client release
* 2627810 Make client time sync fail open
* 9d89305 Add read-only world ownership directory
* af565a5 Add region topology and clean project history
* 5dd6c49 Document staged resource login rollout
* 203b132 Stage resource streaming after client activation
* 88e73a0 Document resource login crash deployment
* a6de2cf Prevent duplicate resource checkout during login
* e292a57 Document PR4 native deployment
* 1af716c Add multi-island resource interest
* d6784fb Fix first admin teleport request being ignored
* 573c91d Keep streamed resources authoritative
* 92c0763 Add safe admin tools and guarded island travel
* f54455d Add island identity and first production terrain
* 21383ce Add validated WAMap reference importer
* ced4ebf Update handover for panel geometry diagnostic
* 355d842 Offset ship panels beyond the exterior skin
* c4badb3 Add canonical Wareborn engineering handover
* 7c3e6c4 Project roof panels above the hull envelope
* b2204c1 Snap panels to the aimed exterior hull face
* 171a2e5 Detect inactive panel placement phantoms
* a224cd7 Snap ship panels to exterior hull skin
* f837c5a Materialize every crafted ship-part output
* fc4efec Fix RemoveEntity Windows Int64 ABI
* 3b82e1d Enable ship-part salvage within owned yards
* 9afb534 Salvage docked ship parts for recipe materials
* e11ccb4 Implement docked ship salvage and refund blueprint excess
* c7e71c8 Avoid double-rotating restored deck panels
* 709f6b4 Normalize all ship-part placement and interaction timing
* 53f074f Allow empty shipyards to recapture persisted ships
* 8877df2 Retain visited resources until native unload is portable
* dd2bdab Fix streamed resource authority and legacy unloads
* e2e88bf Configure public server automatically in WAPatch
* 549c3d6 Document Wareborn progress and public join flow
* 10bdc84 Run game server natively on Linux

## 2026-08-13 | 40 commits

* 3d91d4a Stream resources by proximity and improve helm and sails
* 80756d2 Fix station pickup, ship undocking, and latched helm throttle
* 4edf0de Fix live helm and sail interaction after mounting
* f1bf4e3 Populate Haven with a whole-island starter-biome resource field
* 13a5303 feat(resources): deterministic dense metal-deposit field from real Haven surface
* 9cab761 Fix idle crafting UI blocker divergence
* 29a8a97 Spawn chain: ack timeout + gated-last-step park; rescue now acks; precache world prefabs
* ca5227e Craft-UI self-heal: diagnose and clear the stale CraftingInProgress latch
* 91e77d7 Station pickup: pack a placed shipyard/assembly station back into inventory
* a25d089 Gate the static test ship behind WAREBORN_STATIC_SHIP (default OFF)
* 918f982 Orientation probe v3: one-time per-mesh hull breakdown
* c027be5 Orientation probe v2: per-entity report, exclude mounted-entity subtrees from hull span
* 94a0d00 Add ground-truth orientation probe: log RENDERED hull/helm yaw + hull-local deck span
* f1b5ff9 ship: verify the long-hull path end to end, and make a wide hull unmistakable
* efc0b34 fix(ship): settle orientation - the bow is +Z everywhere; helm yaw back to 0
* f57fdd9 fix(flight): helm lock gains a yaw knob; Man E-hold clamped at the real timer seam
* 009ff0f feat(parts): mounted sail/lamp/horn are interactable - furl, switch, honk
* a006994 feat(flight): v3 - the mouse flies the ship (pitch + roll integrated)
* ed78071 fix(flight): helm mounts rotation-locked to the bow; Man hold clamped in the mod
* a44aebb fix(flight): the world has not ended - pin AtlasMultiplier at 1; faster helm grab
* cb80ed6 feat(flight): v2 feel pass - live helm, inertia + banking, pilot rig fixes
* 57fcb4c fix(flight): serve the mass chain, dismount on the pilot's real exit signal
* 9113ea9 diag(helm): trace ShipHelmPlacement verdict inputs on placement attempts
* 5458188 fix(flight): a MOUNTED helm serves the Man interact entry, not the generic PickUp
* b22d4f8 feat(flight): piloted ship flight - Man the helm and fly the built ship
* b745be0 fix(perf): kill the two live NRE floods + name silent interact failures
* e0b1fd6 tools(perf): thread-wall profiler + prepared WINE_CPU_TOPOLOGY experiment
* 4289653 perf(client): remove the mod's own steady-state frame costs
* 8d3434d feat(client): stutter attribution probe - every frame spike becomes one named line
* ae7c415 feat(skycore): restore the orphaned socket system - modules snap onto the CoreMain base
* 56fcbad fix(server): ledger-gate every first-time-setup AddComponent send; close the MarkServed gaps
* 2ada5b0 Client flood guards: skip missing worn-item keys, mute the benign visualiser-enable warning
* b3ef9f6 fix(skycore): the generator is the base of the core chain, not a coreModule
* 2cd1ff7 fix(craft): every station craft renders its part; no craft may eat materials it cannot show
* 742c84b feat(loading): whole static world behind the loading screen; shard hidden until exposed
* 978810a fix(atlas): release the ghost crystal when the collected shard entity sinks
* ef7b381 fix(atlas): follow the core slot's pose instead of reparenting the shard view
* 0d75ebc fix(shards+fuel): push shard availability unconditionally; no PickUp prompt on canisters
* ed11dcf fix(salvage): give salvageable resources a real material so the beam works
* 9cfb8a5 feat(trees): recover all 65 per-species skeletons and cut each species with its own

## 2026-08-12 | 39 commits

* ae01977 feat(resources): lodge atlas shards in handshake-spawned deposits
* ee71df1 Deposit mining fidelity: render diagnostics, core-slot shard lodging, retail crust->core->yield staging
* 404e201 fix(fuel): rework fuel acquisition from PICKUP to SALVAGE, with the retail 8/8/9 curve
* 3fa79fa feat(trees): per-species wood yield + respawn knob; document the falling-log and berry gaps
* 2369633 Harden the real resource-spawn handshake for live deployment
* 7b83d7d Fix joiner burst: built ships/decks behind the barrier + pace the instantiation op
* 9518a7c fix(wearables): only rig-registerable utility-slot items enter 1280
* 2930d87 fix(ship): make built decks placeable and re-positionable
* d5d7e9b Implement the real WA island resource-placement handshake (server half)
* fa8fb60 fix(persistence): re-dock restored ship to its shipyard by persisted position
* 49042fa fix(wearables): exclude worn deployables from 1280 WearableUtilsState
* a73c0c9 Connect-time spatial interest streaming + remote-avatar re-seed fix
* 5dc71f8 Add shipped 190000/190001/190002 loading barrier to hide spawn-in work
* ed36c76 feat(fuel): acquirable FUEL crafting material - fuel pods + generalized lodgeable pickup
* 9abf8f3 feat(trees): deterministic tree respawn (P1-9) so the island stops deforesting
* 2355f82 feat(atlas): reconstructed refdata - real atlasShard item, recipes, spawn knob
* 5a98610 feat(atlas): retail atlas-shard acquisition vertical (deposit->mine->release->pickup->grant)
* 367424b Fix ship/shipyard ownership showing red after relog
* 62da85e fix(craft): run station craft-start under a guard scope so a throw can't wedge the station
* 4175a5a fix(mount): sail mounts across the whole deck; document per-part surface decisions
* e8c01fa fix(craft): keep the crafted schematic on the timed-craft COMPLETE push
* ecdc609 feat(persistence): thread built-ship owner into the world-state record
* 2c4b320 fix(mount): helm mounts across the whole deck (attachmentType "deck") + coherent reseed rotation
* 72c48a7 feat(deck): dynamic per-frame deck generation from ShipPlan
* 2a30ce1 feat(persistence): comprehensive ship-build persistence (owner, loose parts, mounts)
* 6e4006b fix(mount): honor placed rotation + unblock re-placement; diagnose PartNotMountable
* 46cc852 fix(helm): stop client freeze when crafting/loading a Helm loose part
* aa62fbd fix(parts): correct lamp loose-part prefab to real asset Lamp01; pin all names to client assets
* b1dedfc test(craft): lock in exact-amount consumption (no whole-stack wipe)
* b9ceb48 Fix relog shipbuilding-awareness gate (client-mod)
* 1aa7f8a Allow parented placement for ALL deployables (shipyard was un-placeable near other entities)
* bfbb992 fix(craft): stop timed station craft sticking in the crafting animation
* 6318d1b Wire shipyard build-access prerequisites for crafted-part lift
* 5ac835d Fix station craft blocked after one part: guarantee return-to-idle
* 4116280 Fidelity cheap wins: shipyard fold-out, craft materialize + timed craft, scan note text
* 4f1999a Fix empty personal Crafting tab: route crafting state per (player, target)
* 46b1f47 Generalize loose-part crafting to every CraftingStation ship part
* 0a5deb4 Derive learned schematics from purchased nodes + faithful bench categories
* dbae909 Fix multi-slot station crafting: route per-slot 1005 to the station

## 2026-08-11 | 41 commits

* 5d4c134 Colin dup-seed fix + TESTING: grant whole catalogue as learned (show all recipes)
* a051592 Fix shipyard placement crash: never allocate entity id 0 + dedup spawn seeds
* bfdde01 Fix UI crashes: serve 1240 (restores Logbook + Tab strip); crash-safe crafting-station open
* d72e46b Wire the full craft catalogue to the knowledge tree + make every recipe craftable
* 358b51d Fix assembly-station placement stranding tools + allow crafting-station parents
* 79bf5ba Add part-mounting flow (Phase 2): lift a loose part onto a built ship
* 402da70 test: pin knowledge restore to the live 9bae0367 DB row
* 61068b5 Persist knowledge; fix relog inventory wipe (roster-rewrite cascade)
* c7e21a1 Fix assembly station placement: use the loadable "CraftingStation" prefab
* a2166c7 Fix all inventory/schematic icons to real client textures
* 0ee3a15 Add Assembly Station: place + interact opens the parts crafting UI
* 8fda2a4 Fix stale test: lamp is now CraftingStation category
* 5fe951f Workbench prep: lamp->CraftingStation category, assemblyStation+lamp learnable for testing
* 38af26a Merge ship-part catalogue + craft-loose-part; add lamp as test-starter
* 1202a1a Craft a loose lamp ship-part as a world entity (Phase 1)
* c018ad3 Phase 0: add engine ship-part schematic to the served catalogue
* d6493ac Harden inventory DB load so enabling persistence can never wipe items
* dc76072 Persistence: shared world state survives restart (placed shipyards + built ships)
* 7032490 Ship-build correctness: dock above, one ship per yard, live rename
* 18c9f2b Ship-build Phase 3: craft completion spawns a real boardable ship
* daab400 Ship-build: quality:0 test recipe + push inventory back on rejected drag (un-grey)
* 58e8f97 Ship blueprint Phase 2: material loading + craft transaction
* 1848a3d Ship-build: fix BLANK panel on Done by re-emitting the interact-open signal
* 41aa804 Ship-build Phase 1: non-empty cost bill on blueprint select
* 64d87b1 Ship-build: repopulate panel on Done + fix owner-id so SAVE enables
* add3763 Ship-build: clear BusyModel on EVERY 1270 command, not just RefreshBlueprints
* c8cbda8 Ship-build: serve 1271 + 1450 so the editor-active interest batch survives
* 45e7162 Ship-build Phase 2: real starter frame + 1208 command handler + 1206 editor state
* bac3ca8 Shipyard build lists: kill the loading spinner (serve 1208/1270/1274 + reply)
* f096e51 Add pure server-side ShipPlan load/save model
* 44c67ff Route real client icons for shipyard (item_shipyard) + makeshiftStorage (2x2_makeshift_storage)
* 7d17e72 Placed shipyard console interaction: Craft prompt + ship-build UI open
* a3ad3ff Make 59 crafted outputs usable: data-driven deployable placement + item flags
* fd11e76 Make shipyard item equippable (Utility slot) so it can go on the hotbar for deploy
* 8997edc Thread peer into HandleOnePlacement so StopPlacing can reach the client
* 037db17 Placement: send 1019 StopPlacingItemEvent + Placing=false on confirm/fail so the client drops the preview ghost
* 46920a0 Deployable shipyard placement: 1017/1019 player components, 1017+1211 handlers, runtime spawn seam with rotation (WAREBORN_PLACEMENT=1)
* fb86fe9 Crash-proof crafting/knowledge: add 6 stub records for SCHEMATIC_FIXED node info-panel lookups + reference-data crash-safety validator
* 58948ac Remap recipe categories to valid client CraftingCategory enum (Utility/ShipParts/CraftingComponents crash Enum.Parse and blank the crafting panel)
* c68dcef Only learn catalogue-resolvable schematics on node unlock (unmapped node learning its raw name NREs the client crafting list)
* f03525a TESTING: remap recipe materials to client-known ids

## 2026-08-10 | 25 commits

* 8464b46 TESTING: set all recipe ingredient amounts to 1 for easy crafting
* 9e3213c Wire WA recipe catalogue into knowledge-gated crafting
* 76ca8ce Databank on-surface spawn position + testing grant amount + carry metal placement fix
* 0934b0e Knowledge loop: scan a databank to gain, spend on the tree to learn
* 91feb0c Personal crafting: file-backed catalogue + real 1003 transaction
* dc32d0b Make the metal deposit render: seed the global biome table it blocks on
* 3d6a78b Implement real anchored metal-deposit ore mining (crust + core loop)
* d78d76d Add cobalt and aurium metals so every placed ore node yields
* 64ad57e Re-parent the deck under the hull so standing on it carries the player
* 1687726 Echo owner's own 1073 relativeTo back to arm the ship carry
* d61488a Recognise the spawned hull as a ship: seed 8062/8071/4349 so ShipVisualizer enables
* 1bd1a1c Ferry: WAREBORN_SHIP_FERRY_START_DELAY holds the ship at rest so a player can board before it flies
* f2ff609 Keep bolted ship parts awake so they follow the moving hull
* b40cf86 Seed bolted ship parts hull-relative so they follow the moving hull
* 0adc35b Make ship spawn position env-configurable (WAREBORN_SHIP_POS) to place it without a rebuild
* e592041 Stop the deck losing its solid collider on a repeat interest request
* 61ab260 Reconcile ShipFrameTests Y with the empirical raised hull height
* 00f745d Raise ship spawn Y so the deck sits above ground, not embedded
* ec7e441 Bolt a walkable Deck01 onto the static ship hull
* d8a3c4e Manual F10 recovery + deep-net fall policy + arbitrary-coord teleport
* 10e5a63 Boardable ship: Helm01 part, Man verb, and aboard-detection (step 5)
* 98c42f2 Make the spawned ship fly: 1130 control-point carry probe + ferry (steps 3-4)
* 546fa80 Make metal ore harvestable: wire the salvage-hit path end to end
* c50667e Relay glider/tool visibility as low-rate 6910 events, not per-frame
* db9793b Pace the AfterPlayer world spawn and make the test population count configurable

## 2026-08-09 | 19 commits

* 6d2e73a Fix Tab->Schematics sub-screen NRE that breaks the character sheet
* ea4f539 Distribute ore + trees across the reachable ground band for testing
* f426d8d Stop relaying 6910 entirely - unreliable wasn't enough, the volume bufferbloated the link
* 888cc01 Relay 6910 UtilitySlotActivatedState UNRELIABLY - fix the tool-fire congestion regression
* 533ee82 Close the gathering loop: harvest hit -> inventory yield + native toast (Phase 5.4)
* 29958c5 Place MetalNugget resource nodes on Haven (Phase 4) + fixes
* 1e62f6f Phase 3.1/3.2: grant 1211 authority so hotbar tool-switch (keys 1-8) works
* f073907 Add browser /login page and login-gated /download page
* 52bec6e Serve the client patch through the login server, not Caddy
* e3ab04b Add a self-update patcher pipeline for the client
* c10b194 Add operator admin panel on the login server, fed by a game-server stats file
* 167eee0 FIX 2: stop the PB_*_Serialize buffer leaking on every send
* 6f1076a FIX 3: destroy native component refs on ForgetPeer and on reseed overwrite
* 2f5b70f FIX 1: crash-isolate the packet loop so one bad packet can't drop everyone
* 4e03c6a Guard the harness against the one update shape v2 invented, and make bot deaths speak
* e8ea661 relaybot: pin the pre-relay-v2 baseline soak (VERDICT: FLAT)
* ea2cb69 relaybot: headless two-bot harness that measures true relay staleness
* ad20993 Relay movement by cadence, not arrival, because rate is not age
* 6e36c02 Drain the queue faster than it fills, and log what is on the wire

## 2026-08-08 | 61 commits

* 7f5783a Stop one unknown component costing you the whole other player
* ee54277 Bucket a session's traces over time, so 'it gets worse' can be measured
* 67f4827 Teach the server to say "this entity does not have that component"
* b3a66f8 Give the world a bottom, because the client never had one
* 70a75a2 Diagnosis: the error storm is not the stall, and my second guess was wrong too
* 8a317b4 Diagnosis: I blamed the tree for the tab menu and I was wrong
* 08fb983 One choppable tree on Haven, four metres from the player spawn
* c81b63a Spawn a ship on Haven, because a ship is four components, not a shipyard
* 326c47f Stop the inventory destroying itself, then give it a home
* 8034906 Teleport players by 190607, the parentless path that needs no new authority
* 10f0202 Generalise the spawn seam from {island, player} to N world entities
* a8eef58 Research: the first ship, and it is far cheaper than we assumed
* aae0b57 Research: no island in the world has a single tree placed as scenery
* ce19ef4 Extract the ship-entity evidence: prefab census, Require maps, hull blob
* ee3d536 Research: the authored first hour, and it does not involve a ship
* db1fb38 research(loop): world-wide island prop census + recovered tree wood types
* 6524036 Extract the authored first hour from the shipped client assets
* 03a8471 docs: findings on the harvest transaction (loop research)
* 83d8610 Stop attaching stack traces to ordinary log lines
* 3f438bb Stop ChararacterDrunk from re-throwing an NRE every single frame
* f292799 Probe: report which floating-origin strategy is actually live
* 73309c5 Spawn on the measured Haven point, not the pre-TRS one
* 3d715a5 Fix island surface extractor: compose full TRS, not a sum of localPositions
* e947b51 Spawn on Haven: make 190602 seeding entity-aware, and stop lying about Haven
* d95c046 Sign-up page: wind, and the game's own UI
* ca15dda Serve sign-up over HTTPS at wareborn.ratlabs.cc
* 14cda33 Run the login server natively; Wine cannot do Postgres SCRAM crypto
* 1ccfb32 Accounts: real login, sign-up page, per-account character rosters
* ae2c074 Add WorldsAdriftReborn.Storage: accounts, sessions and characters
* c238d39 docs: deploy servers with publish -r win-x64, not build + flat glob
* f969d44 Ask the client to show its own login form
* 43c4b16 Database: SQLite, and our own objection to it was wrong
* 4523a1d Login UI confirmed present, plus the sign-up page and its landmines
* fd84722 Auth research: the login form is already in the game
* baf56f5 Accounts research: the real Steam id is already on the wire
* f03b2a1 Spawn research: the 110km mystery was never a bug, and our surface tables are wrong
* bcfbabb Haven research: spawn there, but seed isNewPlayer false anyway
* 5e9aa7a Add the phased plan for accounts, starter island, resources and tools
* fdccc25 Fix the interaction-release seed on the correct argument
* 78bc269 Log an untouched config key once, not 327,713 times
* 87818e7 Add tree-harvest spec: one integer harvests a tree
* fed6ed6 Add node-spawning research and the extracted island surface tables
* 84fcd91 Add gathering research: progression, metal deposits, inventory
* 5666dda Add tool-system research: one line makes tool use observable
* 3ae1c76 Add gathering research: crafting, items/materials, node relay
* 766eafd Stop the server logging itself to a standstill
* a8ac373 Prove ENet survives deinit/init in one Wine process
* 47cf127 Decide local-vs-remote by components only, never by name
* cfc6eab Backfill a regression suite over the multiplayer rules
* 7735a87 Send the client's reliable packets reliably
* ecd3d76 Stop fabricating component values that damage the client
* 292d3c8 Record round-2 verification: three claims corrected
* c545bfe Measure the mirror timeouts in seconds, not in ENet events
* 5e31589 Restore player input during the first 25 seconds after world load
* 1a1ed0e Correct the docs against the seven research reports
* 40287f3 Add the real Worlds Adrift world layout
* 35e330e Add the seven research briefs and findings
* 3a1860c Persist the character roster
* 53dfa7c Document VPS hosting, ports, deployment and client distribution
* e42669b Fix one-way visibility safely, and relay high-rate streams unreliably
* ab43a9c Set the client game port via an exported native setter, not an environment variable

## 2026-08-07 | 42 commits

* 684e54b Fix the infinite sky-fall: disable mirror resends, guard the local rig by component
* fcece88 Resend mirror ops so the joining client reliably spawns the other player
* baf2f67 Stop drawing our own rope: the game renders it natively once 1098 is seeded
* 753657a Re-assert rope line width every update + diagnose the wedge
* 4f95743 Hide the remote grapple tube unconditionally, not just while the rope is up
* 754cade Hide the remote grapple tube continuously while grappling, not once at bind
* 4ea6f7e Hide the game's GrapplingHookTube on remote rigs; add local-player fall telemetry
* 3a49c50 Style the remote grapple line: thin dark rope, not a magenta wedge
* 0044868 Make the anti-yeet neutralize deterministic: run it in FixedUpdate
* fc765fa Phase 4a: replicate the grapple rope line
* 0c6e860 Roadmap: mark Phases 1a/2/3 + yeet fix done; flag Phase 4 as the hard frontier
* 4d06732 Fix 'yeet into the sky on second join': neutralize remote rig physics on frame one
* deeb606 Phase 3: replicate the glider via UtilitySlotActivatedState (6910)
* 69e5148 Fix asymmetric visibility: fallback flush for parked mirror ops
* 4d37822 Phase 2: relay worn gear to other players
* 5399f04 Phase 1a: adopt PlayerVisualizer's interpolator for smooth remote movement
* b7f7329 Roadmap: add phased remote-player fidelity plan (smoothing, gear, glider, grapple/VFX)
* e4325bc RemoteRigMover: always position globally, never yield to unresolved parenting
* 7bf0b49 Fix remote rigs falling: keep RemoteRigMover authoritative, suppress PlayerVisualizer
* e7b9754 Sync remote player animation via 1073, and adopt the native positioner
* 70c9df6 Appearance hardening: forget on disconnect, re-arm publish on re-entry
* b4d15da Relay real player appearance
* b745a8e Document the working multiplayer: architecture, operations, roadmap
* c44ac3b Move plain remote rigs from TransformState; sample-decode relayed transforms
* 9f47d2b Give every client the same island entity id so Parent references resolve
* 92d1cd0 Diagnose the plain remote rig and un-cull its render layer
* 5c56086 Two-phase mirror: request the plain Traveller asset before AddEntityOp
* 55bbd19 Mirror remote players with prefab context Default, not Player
* 0e69621 Only run first-time setup and authority grant against the sender's own entity
* 3ffe069 Fix rig tamer freezing the LOCAL player
* a984cfa Client: stop remote rigs from simulating - display only
* aeef3a7 Shrink remote avatar seed to the minimum the body-builder requires
* edc1348 Seed remote avatars with the game's own second-stage player component set
* d7ea4b4 Client: keep the camera on the first Traveller rig that claims it
* be36bed Client: disable cameras and audio listeners on remote player rigs
* ae8971b Client: never let a second Traveller steal the local-player singletons
* de6124f Client: let only the first CameraBinder drive the camera
* 892cfee Seed remote avatars with TransformState only, and document component ids
* f6b0cff Support multiple simultaneous players
* 0a40ecf Add mingw-w64 cross-compile build for CoreSdkDll
* 6a110bd Add pure multiplayer policy modules with unit tests
* 7b93a5c Fix fatal CLR abort when registering component update handlers

## 2021-08-07 | Built on WorldsAdriftReborn

Wareborn is not a from-scratch server. It stands on the original WorldsAdriftReborn project, which worked out how to talk to the client at all - 135 commits by killzoms, sp00ktober, mmjr-x, Cat and others, from 2021 onwards. That history is in this repository and is not listed above, because it is theirs and not ours.
