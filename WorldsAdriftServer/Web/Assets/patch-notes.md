Worlds Adrift shut down in 2019. Wareborn is a fan-run server that puts it back online.

Every commit, newest first. 812 of them since 2026-08-07. Merges are left out - they only repeat what the commits under them already say.

## 2026-08-25 | 50 commits

* 7e04da22 Review the public status for truthful mass and the attitude spline
* 39cada8f Write down the dock-bubble behaviour and close the island-envelope entry
* 263df30a Let a yard's own bubble excuse the island envelope it stands inside
* fc8f593b Dock against the shipyard bubble the player can actually see
* 542c148e Record what the shipyard "bubble" actually is and what survives of it
* e70c44b2 Stage client release 2026.08.25-1 without publishing it
* 25450bda Record the client attitude-spline correction and why option 3 is deferred
* 19127067 Smooth ship attitude in the client behind a live A/B toggle
* 3c2e1f8e Give replayed ship attitude the derivative the wire does not carry
* bc6e1215 Keep the fixed-step wire clock on wall clock when steps are dropped
* 47befd42 Record the turn-vibration mechanism and the nine hypotheses it killed
* 09220c7d Stamp a legacy flight point for the simulation it actually represents
* 58321509 Record the gates-off docking-snapshot byte defect in the integration log
* 419d2ad6 Omit the null docking snapshot so a gates-off world state stays byte-identical
* 5a45360a Record the Step 6 integration conflicts, decisions and mutation log
* 6f5ab5e1 Scope the parked-vector-restore needle to AdapterFor's consuming call
* a9c8ba9c Gate the one-stamp chain from committed pose to docking commit
* 67851be8 Prove the vector rest snap and docking capture stay out of each other's way
* 0b82f372 Encode per-step collision evaluation as a hard response prerequisite
* dba2da4d Fire the vector reseed from the transactional docking freeze too
* 209de608 Route the collision observation through the one stamp minter and mass truth
* 1757bc10 Unify all six flight-runtime gates in the one tested flag parser
* 2ad75563 Stop inventing a retire stamp and a second durable write per dock commit
* 66b0d7c7 Make the docking transaction durable-first with non-fatal peer publication
* 97f4b62b Refuse a clearance whose subject the evaluator silently dropped
* 6d928fba Keep steady docked hulls quiet under the docking transaction
* 4a73aff1 Wire the gated collision observer and transactional docking into the tick
* 4f65be74 Complete the pure collision/docking runtime acceptance surface
* f507b135 Unify frame identity on FlightAuthorityStamp
* aa72d8d4 Adopt the recovered collision/docking runtime checkpoint after review
* 89f8385a Pin the restart-only flag lifetime that stamp monotonicity depends on
* f990fab6 Restore the exact pre-branch 1258 seeding journal wording on the OFF path
* 69f842c2 Consult the same core identity evidence in the lift plan as in the audit
* a32226c4 Prove the vector extension survives the production System.Text.Json round trip
* f890b95a Settle a tilted hull level at a bounded rate instead of popping it flat at rest
* 8243ce05 Carry the commanded baseline and wall air through the one shared wind decision
* 0c070046 Prove the observer shadow leaves the scalar path bit-identical
* 587f7429 Omit the null vector extension so an OFF-path world state stays byte-identical
* e9962736 Serve 1258 lift capacity through the one plan the runtime enforces
* 04c63c0d Drive promoted hulls through the vector authority runtime
* a36709c8 Mint every accepted frame's stamp and pose in one authority adapter
* e68a5545 Add the per-hull vector flight runtime and its durable extension
* b3417cd5 Wire the authentic lift, gravity and core-loss policy through the reviewed seam
* 99f3fbe6 Gate vector authority and the lift runtime behind parsed, warned flags
* 59b07d68 Extract the snapshot cache policy and lock the cache
* b9e95421 Canonicalise the fingerprint hash order across restarts
* 6a64f7f3 Serve every mass consumer off the one snapshot
* 348c6bdc Add the typed, provenance-labelled ship mass snapshot
* c6644a23 Add the shared flight authority stamp and pose contracts
* ff031b82 Keep flight propulsion tiers additive

## 2026-08-24 | 24 commits

* a92b0151 Stabilize mounted instruments during ship turns
* 6407ec5d Reject CLR-incompatible client releases
* 7cb83639 Pin low-speed homepage claim
* 602d73ba Align homepage flight truth check
* 71801eb6 Report low-speed client acceptance candidate
* 671c800d Keep low-speed ship follower state coherent
* 37fa4453 Report fixed flight publication timing
* 6fc3ed44 Phase-lock fixed flight publication
* e8648c68 Report mounted follower drain candidate
* f340ddf8 Drain mounted followers after ship rest
* 14a812e6 Report proven rest heartbeat correction
* f66cabf2 Stop rest heartbeats reviving stale ship velocity
* b12ee060 Report safe ship restore candidate
* c3bbbf84 Restore returning players to clear ship decks
* ed097f2e Report moving helm rewind correction
* a029db17 Do not rewind moving ships on helm entry
* 855fd5ec Withdraw rejected ship continuity trial
* 9b30bd7a Update homepage flight status contract
* c3e89b59 Update public flight acceptance status
* de984f90 Remove client ship motion batching
* 7da48b8c Report homepage release marker cleanly
* f9731bde Keep public release status synchronized
* 4969f0d4 Keep flight hull pose single-authority
* 47f9b5d2 Restore players aboard durable ships

## 2026-08-23 | 30 commits

* ff0206bf Restore the full-width emblem workspace
* 95940fb8 Fix overlapping decks and restore engine visuals
* e86779d1 Unify the player-facing website
* 1462d804 Unify the public site visual system
* 2fa38b26 Revert "Repair deck and engine presentation"
* 3936bb06 Refine the public roadmap experience
* c4a84a54 Repair deck and engine presentation
* d8555ac9 Tighten the public landing truth boundary
* b239085f Build the WAReborn public landing page
* c8669197 Make patch note history cutoff deterministic
* 42d6346f Hide duplicate virtual deck rendering
* 013875a5 Publish live retail engine visual state
* 54411147 Restore instruments mounting on bar pipes
* 1d12582d Correct C4 equilibrium sail trim shadow
* b6fd5089 Add isolated bare-hull drive tuning
* 009060f5 Keep local pilots anchored during ship acceleration
* 20a12094 Fail closed without live sail yaw state
* 6ab5996d Expose live flight shadow comparisons
* bc32c54d Document the Gitea miner intrusion investigation
* 2f1d1c40 Record Pack C moving restart and stall results
* c61d9e6b Add aim-independent native ship interactions
* 604d0100 Harden semantic client acceptance bridge
* cbfee88c Record Pack C acceptance and VPS incident
* bfe3640c Profile slow authoritative loop stages
* 60fac69d Instrument world persistence stalls
* 57144b17 Allow one-shot local session enrollment
* 8fafd3e2 Remember protected game sessions for unattended tests
* c3877e53 Preserve test bridge opt-in across Wine handoff
* 8645515b Harden semantic client acceptance bridge
* 8fcb1ffa Add opt-in semantic client test bridge

## 2026-08-22 | 37 commits

* 6ca11d96 Decouple fixed flight physics from publication
* e663e8f4 Prepare fixed-step flight acceptance
* e17cc53e Increase sail power within recovered balance bracket
* 68ba9499 Restore retail residual ship drag
* fcd7fb0d Complete ship restores on authoritative boarding
* c5ef3ca1 Prefer exact ship decks during logout restore
* 705cde89 Gate ship logout restores on collider checkout
* 2d5f2df9 Stage boundary test ships safely
* f94a2d46 Add exact player acceptance scripts
* d8af77e8 Group visual acceptance into test sessions
* 23010812 Plan staged flight and worker visual acceptance
* 710cd4c7 Make game deployments preserve live state
* 1fdc7a0c Keep moving ship domains frame-aligned
* a73a2d4d Record live unpowered ship settling trace
* 5916d124 Trust authentic ship interaction prompts
* efe3b760 Resolve ship interactions in the grounded surface frame
* c2348ca0 Record final retail flight program integration
* 3c944db9 Gate the authoritative fuel lifecycle rollout
* fc02ef6d Cross-review fuel wall and worker foundations
* 325eb8f4 Add bounded worker authority foundations
* 515dccb8 Add deterministic vector wall and storm shadow policies
* 80c19cdc Make fuel follow authoritative hull propulsion
* ee37c554 Record flight waves one and two consolidation
* da109eba Integrate flight shadow force collision and docking policies
* b82368c2 Prepare authentic docking lifecycle
* bc1d65ac Add deterministic collision shadow primitives
* 0159c86c Add authentic lift and overload shadow policies
* fd5a21fe Harden vector shadow inputs
* bda1f152 Record flight wave one mutation review
* 31db3a41 Integrate flight wave one seams
* 40b5db4e Add deterministic vector flight shadow primitives
* 25aa300f Add fixed flight clock and durable snapshots
* e2bc98d3 Harden retail world-boundary stepping
* 11931bd6 Define the retail flight reconstruction program
* dbc41e69 Record the retail flight reconstruction phases
* c3699faa Enable recovered retail flight bounds
* 560885bd Add opt-in retail world flight bounds

## 2026-08-21 | 23 commits

* af341779 Harden ship-part authority and document physics gaps
* ee9d923e Unify authoritative ship force evaluation
* ef4fd4bb Secure and activate sail flight interactions
* eb356f7e Rebuild admin console as routed workspaces
* a25e3023 Record World Inspector deployment
* c2c65caf Regenerate patch notes for World Inspector
* 1f57ce05 Record World Inspector implementation
* 030da364 Align inspector truth test with linked selection
* 4e4ee7d1 Harden World Inspector truth boundary
* 1a5a4277 Add linked admin observability modes
* d2588836 Add authenticated World Inspector telemetry
* 645b4153 Record helm latency deployment
* 9c7fc08b Accept helm input without takeover delay
* 815bcdc5 Record sail wake deployment
* b92ba07c Wake flight from sail controls
* ae12fac9 Record flight and wind-wall deployment
* 82922389 Record integrated flight and wall work
* 48bf9dd3 Restore authentic wind-wall influence bands
* 6b4d8554 Calibrate and expose recovered ship forces
* 8686986f Gate sail and helm input lifecycles
* 35998345 Record wind carry and wall rollout
* b46f242f Carry ambient wind through centred sail propulsion
* ffc858c9 Record integrated wind and storm deployment

## 2026-08-20 | 69 commits

* 95bdabe5 Note why the wall flag is checked before the catalogue is touched
* fb6873b0 Simulation: guard the inspector wiring, and write the shadow model down
* d0909218 Write down how to actually LOOK at the weather walls
* b8ab76d1 Simulation: add the interaction shadow model above domain ownership
* e53e0e87 docs(storm-s3): record the §4 verdict and what S3 actually shipped
* 0cb400d4 Record the walls phase in the roadmap, and two findings from building it
* cc0fc6ea Close two mutation escapes in the wall guards
* c2f92918 Serve 1204 WallSegmentState so the client renders the 44 weather walls
* 4a81e200 fix(storm-s3): close an escaped mutation - move the seat arithmetic out of the untestable assembly
* d0604e46 Material mass: the table was already right; guard the epoch, fix the provenance
* 5a01de1b feat(storm-s3): re-roll deposit placement at each island's own storm end
* 6ccdf5d4 Kill a stale comment that said we have no mass model
* 8b811f2f Tungsten is 0.74: the cannon sheet back-solves the whole WPU table
* 108ee0d4 S2: record what the per-island reset measured, and correct the plan's one wrong seam
* b7d0aa86 Recover the resilience table off a published chart's embedded JSON
* 5e8109ae Retract my atlas conclusion: I read the decompile and forgot we patch it
* cb703d07 Mass is already real and flying; the cliff was never mass, it is lift
* ecfdf756 Bossa says it themselves: the wall gate is soft by design, not a threshold
* 272d0240 Recover the deleted cannon sheet, find a sixth workbook, and settle tungsten
* 0e4c7378 Consolidate every per-material mass and durability source into one dataset
* 8c124eab The weight memory is real: recover the ship-wall force model
* 0a016887 S2: close the mutation that escaped - move the scope decision into the tested assembly
* 83d461f0 The dark sky rides the WALL texture, not the weather lattice: Lead D answered
* bc22a4a2 S2: reset each island's resources when THAT island's storm ends
* 1c6b5520 The dark sky is real, but it is not 1254: kill my own lead, find the storm channel
* 247dbeb5 Field report: the first live understorm, and the two defects watching it found
* a8b49396 Measure the journal: it is not clean, so the rotation step is not optional
* 024e321c Record what S1 found the plan got wrong, now that the code exists
* 0477c270 S1: seed 1254 from the live schedule, and record what the bundle sweep settled
* 30ce4943 S1: the understorm - island lightning timer, telegraph, and the resource reset it drives
* b59ae15a Record the security phase as deferred, and correct the audit's ownership-gate count
* bebfe502 wind: one wind field, and the finding that retail was becalmed too
* de4037a6 Audit the server architecture; fix the inverted WAREBORN_LOG_VERBOSE parse
* 911a7282 The Blight's last unknown closes: "blight" is line 17 of the prefab census
* 56674366 CORRECTION: the Blight's "needs a client mod" blocker was a FALSE ZERO
* 2e8c53a9 storms 14.4.1: only the two COSMETIC storm classes need the weather lattice
* e27eaa55 Recover the storm cycle: understorms are reachable now, and they ARE the node respawn
* febf8f13 Bring the roadmap and handover up to date for a session handover
* 9dbb6a60 Record the SC5 soak, including what it cannot prove
* 823a9aa4 Name the commit that broke the string, as its own entry says to
* edc670ce Restore the overload string I got wrong, and record why
* a760a23c Put the belt divider where the client thinks it is
* bdd51f2d Archive the Worlds Adrift wiki into the repository
* 4bfa35c7 Use the overload message the game actually printed
* e907240e Say why the bare-hull wind is not gated on a sky core
* f44aaf1b WAREBORN_FLIGHT_WIND_SPEED: a knob for the tier that had none
* f21e4e47 Replace the estimated ship-part diff with the exact 58
* d36adc68 Retract a false positive of my own, and add the rule that prevents it
* 146ebaac Answer the bar pipe question as far as static analysis can take it
* 81be23fc A bare hull moves: restore retail's wind force on the hull itself
* 9afe9ca1 Separate the two prefab flags SC5 rests on; only one is load-bearing
* 6976ca7c Record that the bar pipe sat in this repo for nine days, on line 873
* adedbffa Correct the document's own opening claim, which was wrong
* e6036ee7 Give a mounted bar pipe a REAL hierarchy key, so a ship is above it
* 7a2cf99f Unblock cooking from creature combat in the ranking
* 9be427c7 Confirm the community leads against the client, and correct three sections
* f3d123a7 Reorder the closing sections and register the fourth oracle properly
* dc7a1cb5 Note that dye icons are procedurally tinted, so their absence proves nothing
* e8310c39 Fold in the community pass, and record which half of the method did what
* 6a7695d6 Rebalance the ranked list; turrets are two archetypes, not seven
* 6288b9d8 Add the live defects, clothing, and the food system's real shape
* 9d5675eb Read the knowledge tree as an enumeration, not as a spend graph
* ab0867a5 Add the 98-icon enumeration and the limits section
* 20826f46 Name the blocker from the source, and scope the branch that removes it
* ebdcb97a Fill in ship parts, items, food, ciphers and the ranked top 20
* 1f845bfa Start the reality inventory: enumerate the client, do not audit our claims
* 2ec51315 Put the fuel tank where the client already says it is: the generator
* b6a209d8 Correct 11.11: it was one decision, not three gates, and a missing part
* ea8d645c Ship the Bar Pipe, the part the instruments were always meant to sit on

## 2026-08-19 | 117 commits

* dd34ca0e Delete the placement patch instead of adding a third gate to it
* c4a2ccc3 Record the container confirmation, and name the error class
* 6e55bbed Renumber the duplicated roadmap section
* dfe49642 Give the game server a post-deploy check that is able to fail
* 58d761fb Record the gates this branch actually passed
* aa970da7 Resolve the placement field once, not once a frame
* 9fd93e57 Stop the server telling its own log that its gaps are unfixable
* 8b4847d8 Pin the guard that keeps the bunker drain out of the gauntlet trap
* fe873109 Write down the five things a live session settled, and one it did not
* a9a489b2 Feed the tank a canister at a time, not a unit every four seconds
* 9e0123d1 Resolve a mounted part's ship through 8066, not the Unity hierarchy
* 39503a7d Move refuelling off a prompt that says "Activate Atlas Pulse"
* 5f0aea50 Stop a ship container reporting "It's locked." to its own owner
* a61e188a Record the afternoon, and correct four inherited claims
* 97e2dcb3 Write down what the two-state relay is, and what it is not
* b945124f Ask the soak whether the level is defensible, not only whether it grew
* fb197748 Give the relay two numbers it never reported about itself
* 7152ac6a Stop fuel's throttle mirror outliving the pilot who set it
* f5cd2c0d Give the refuel rollback an honest inverse
* 81db75f0 Answer the query feat/ship-flight's phase F5 asked fuel for
* 9771bcc8 Keep fuel off the hot paths it does not belong on
* 50a226fe Correct the roadmap where fuel used to be a known defect
* 75021f73 Prove the fuel wiring is connected, because the suite cannot see it
* a33283bc Record that sail material changed weight, not thrust
* 6a08c602 Make the fuel gauge's needle move, and give fuel something to be for
* 79a797c9 Treat near-zero thrust as undriven so hulls can actually settle
* de78989d Cross-check the recovered physics against the community record
* bc9b47e5 Make engine thrust and sail power tunable without a rebuild
* fad2295b Pin what stops a moored ship sailing itself away overnight
* caafa928 Upgrade the atlas-lift blocker from unknown to proved, and flag the cliff
* af75ad75 Drop a salvaged container's inventory binding
* f27da94e Reflow a comment the container branch pushed over the margin
* 6c0a50dd Document the recovered ship physics, component by component
* 11f25578 Give fuel a tank, a burn rate and a push budget
* d4bb1a6f Flip a bolted container's prompt on
* fcdc80ee Fly ships on recovered forces instead of a commanded speed
* 1075279a Stop the 1210 branch claiming storage is unservable
* 67e1fbbb Establish how fuelling actually worked, before building any of it
* 1251a5b3 Record what the ship-component audit got to change its mind about
* a6d8bf76 Let deck parts mount on things already placed
* 57646832 Open the four ship storage containers
* 7cb0057f Audit the 37 assembly-bench ship components against the client's own requirements
* ad55b946 Write out the portal states nobody ever looks at
* 033b1975 Centre the current tab on a phone, and show a form in flight
* b2100ffa Rebuild the portal on a design-token layer
* 5ca430d0 Seed a crafted Window with metal, because the client ships no wooden window mesh
* 6920b7f4 Lower the spike threshold under the hitch players feel
* 6a82bac3 Take the CoreSdk per-op log off the main thread
* 46cc7e9f Move the two editor-markup tests off the portal shell's test file
* 4bd3e4ec Mirrored layers, and a grid the editor snaps to
* 68ae8438 Record salvage confirmed in a live client
* f11990f6 Record the live-client confirmation of chests, felling and tree yields
* 01d28485 Record Phase 5, and correct the plan's reading of the .1 reward keys
* 2ee83ff0 Pin the reward field names: a "tidy" rename binds them to nothing
* 6556b977 Two itemData.json defects that made two scrap items unsalvageable
* 8e72095e Make scrap worth picking up: SALVAGE pays its recovered reward table
* 6c5592bd Record the afternoon's four deploys, and six corrections
* f6242f1c Scrap yields no cloth: withdraw two proposals built on that premise
* 5834a9a4 Group the changelog in one pass instead of re-querying per day
* 033d5470 Adopt an unactivated container rather than filling it with gauntlets
* 46ea3ffc Compress the object catalogue, which just became a megabyte
* 7c9fffc6 Give the object panel groups a player can browse rather than one long list
* 01ac220b Put the two hundred traced objects in front of the player
* 150b57ac Audit every wiki feature against the code, and phase the gap
* 70c67a3d Allocate the destination item id only once a move is certain
* 593fc737 Show loot containers on the operator map, and not on the public one
* daecd01d Correct the production claim: the release world IS on, and the numbers decompose
* 35a03171 Kill a creature with the beam, not with a weapons system
* 5febf4bd Size phase 7 from evidence, and withdraw the corpse-grounding plan
* ebed3c23 Seed loot containers, and make them openable and lootable
* 6241cc44 Make regenerating the patch notes a step of the deploy
* 77863d7d Record the gate results for the three landed phases
* 38a51b59 Separate two comment blocks that ran together
* 893fa933 Record what landed, and fold the maintainer's lifecycle account into phase 7
* 0aa0fe8f Pay plant fibre and berries off the cut that already pays wood
* 3774e15d Make the live ground-profile cache safe for parallel test classes
* 67d3709b Lay a felled log along the ground instead of flat through it
* 058877de Draw a deposit's metal from its island instead of hardcoding iron
* ea7d6183 Plan loot containers, and correct three claims the resource audit got wrong
* d7569725 Pay a node's own quality into the item it yields
* 76862676 Offer the alliance crest as a PNG, not only as a vector
* 707a28ee Plan the resource economy, and correct three audit findings first
* 2fc58462 Size the save-menu preview so the crest is not clipped
* 8bb53d09 Record the tree-fall deploy
* e73cefaa Ask before Delete all throws the layers away
* a7cf2d0e Give the portal tabs, and the emblem a layered editor
* 5a69250d Show the log that falls, and make the trunk break up piece by piece
* a1dafdde Trace 200 more emblem objects, without touching the frozen fifty
* 0a7506c6 Record the 2026.08.19-2 release that unbreaks the patcher
* 0fc418e0 Say in the journal why a portal action was refused
* d7d00bf3 Record the PLAY-hang outage and its cause
* a99926a1 fix(mod): PLAY connects again instead of loading forever
* 8fd1a089 Make /patchnotes a commit log instead of a write-up
* 2184b172 Give the server screen Bossa's own copy back, and only grey the PvE card
* 153728aa Record the 2026.08.19-1 patcher release
* 1621a28d Record the account-portal deploy
* df166e6b Sign in lands on an account, not on a download button
* e5babd15 Record the /patchnotes deploy, and say the log is a log
* 7499fce9 Write down what shipped, and put it at /patchnotes
* b33cf4bf Serve the welcome message, and let an operator edit it
* 2e0406bf Say which label the welcome copy replaced, and catch the second one
* d6dfabc5 Let the welcome scroll be written by whoever runs the server
* aa0e2409 Send PATCH NOTES to a page a person can read
* dc075389 docs: why the crest URL must be plain http (client TLS tops out at 1.0)
* b90894d6 Alliance crest URL names the origin the request came in on
* 57823d4a Give alliances fifty drawn devices, and give players the vector
* 8a1335c9 One whale for the whole world, migrating from zone to zone
* 5692044c Give every alliance a crest, and let its leader compose one
* 81fde922 Stand the Revival Chamber up instead of burying it to its waist
* 44428c03 docs: record what the launch actually proved
* befb9518 feat(mod): stop telling players to go and look at things that are gone
* e838f7d4 feat(mod): login screen links and copy stop pointing at dead Bossa
* 32294464 feat(mod): stop offering a PvE server that does not exist
* db522877 feat(mod): pressing LOGIN logs you in
* f4600870 feat(mod): the client no longer needs Steam to start
* 2bd31136 fix(mod): one broken patch class no longer silently kills the rest
* ee8e483a docs: plan for cutting Steam out of the client launch path

## 2026-08-18 | 101 commits

* 951ab0b0 fix(map): stop the viewer sparkline's svgEl shadowing the renderer's
* 84b5d371 Draw the ship on its card, not just its outline on the map
* d5c9b871 Count who is watching the map, without being able to say who they are
* 708686dd Let two fingers zoom the map, not fight over which one is panning
* 14feab51 Stop a hovering ship being machine-gunned with whale song
* 086c87c0 Tell the operator where to stand and when to look up
* 6d52d9db Stop the public map explaining itself, and let you find a traveller
* d914ceb0 Let the soak see the whale, and make one bad brace fail a test instead of a console
* 655b0104 Take the joke off the whale
* 54cb421b Put the whale in the sky, give it a voice, and draw it on both maps
* beecec46 Give the world one animal big enough to be an event, and a path it can be found on
* f25ae265 Make the gate able to see a calf, and the map able to show one
* dc7d033c Let the calf be a quarter the size of its mother
* e108cacf Give a school a mother and a calf to travel in it
* 9767999c Answer 1166 before anything is allowed to be small
* f3e66427 Stop the whole world sharing one heartbeat, and stop it being emptier than the world it replaced
* 491d5377 Teach the schools to feed, dive under the rock, and cross to the other bloom
* 6940bf60 Polish the interest rings: distinct label anchors and a dotted wildlife ring
* 8ac46c03 Give the populations a rhythm: islands swing between bloom and collapse, and the rays trail the food
* 13f8961e Publish the interest picture in the stats snapshot (schema v10)
* 1b11946b Wire the ecology: field-following schools, sized populations, and both maps drawing it
* 5ce067c4 Add the operator command panel and the interest-and-streaming view to the console
* 345c5497 harness: exec the game server so cleanup kills the server, not its subshell
* 765cd844 harness: run cleanup on SIGTERM/SIGINT, not just EXIT
* bf080776 Build the public map on the shared renderer, and close its label seams
* d06e602a Pin the cross-version tolerance the operator fields depend on
* beeb7acc Extract the console's front end into shared asset files
* 10fbb19c Stage the ecological field and the capacity model, unwired, with the wiring designed
* 889e9083 Carry the resolved target out of resolution instead of on a placeholder command
* 8bad020b Stop the teleport-to-a-player warning crying wolf about a fall
* 07aada39 Give the mantas their tails and genders back, and the jellies their four species
* 1eacfe32 Strip 33 tests that assert what a stronger neighbour already asserts
* c0ea20aa Let an operator act on any player, not only themselves
* f988c50b admin: put the fleet on the map, as the hulls their owners built
* 0d6a1eab Prove the ground under the fauna gate, and adopt the layered ecology as the target
* 676b8d35 Serve an anonymized public live map feed at /map
* ff37ed0d Reconcile the terrain-readiness blocker: it was the missing load barrier
* cc4624f1 Cover the shared endpoints alliances borrow from crews
* 96f92419 Record what the alliance work leaves unproven without a live client
* 1bdfe787 Stop resolved invites lingering in a player's own invitations list
* a33cc630 Record the merged gate numbers in the interest findings
* 59563f55 Make the social sheet's spinner turn at a speed, not at a speed per frame
* 579b4951 Refuse an alliance boot addressed to an alliance the caller is not in
* 1cfda9c7 Serve the alliance half of the social API, so CREATE stops answering E00001
* 3e4c243e Give every tier-1 island wood, and put it where a player lands
* c3571f2d Give island resources island-keyed interest, so an island stops looking empty
* c5e1d9c6 Cost the wildlife before growing it, and find out why it is expensive
* c7b03eaf Answer the respawner question, and stop the client throwing on every E
* 66c68e99 admin: put the world's wildlife on the map, moving, live
* 79e00ca0 Move the chamber to the spot the user measured out for us
* 7ace330b fix(fauna): face the direction of travel, per species
* f50a340d admin: make the world map an interactive map, with real island detail
* 65e9d502 Say why the shrine refuses, and clear the ground it stands on
* f21e4d4c fix(fauna): island-keyed interest, recovered patrol band, and schools
* c25398a9 Stop a crew's invites from destroying the whole Social Sheet
* 1076ae94 Bring the chamber back and put the teleporter in it
* 49c02ce2 Ship Nightfall as translucent zones, and put the island inventory on the page
* 3df1da2c Make the social host follow REST_ServerUrl instead of copying it
* c00f9147 Give the world map back its colours, and say what is on each island
* 2af92ad0 Say plainly what the spawn seed's 2 m stand-off means for the walk
* f14b1287 Move the shrine out of the metal camp, onto an object you can reach
* ae991719 fix(admin): make the world map's tier colours an ordinal, colour-blind-safe ramp
* aa2259cb Make the Wilderness shrine's interact prompt reachable
* 2865af5b perf: fix in-world frame rate — collapse Wine's fsync futex herd (51 -> 120 fps)
* 2ae8ea31 Record that island fauna is wired, and what it is not
* 0f42f76f Let the relay bot see island fauna
* b901cd51 Put island fauna on the wire
* de555365 Record the b652034 deployment and the v7 migration
* b6520346 Return knowledge gain to stock values
* 455066f7 Run the relay soak natively, so the deploy gate works again
* 8ee23980 Assert the recovered fauna movement instead of the island transform
* 7a596d5a Add pure island fauna core for jellyfish and manta rays
* 7bde571a Stop reissuing a live crew id, and write the social API contract down
* 94d6bc35 Write down the shrine's crew rule and what it is still guessing
* ab08331e Stand the Wilderness shrine on Haven and wire it to teleport
* 87980e99 Reseat trees around the newly inferred deposits
* f4554519 Record the island resource population evidence
* de0c570f Populate metal on every island from the surveyed tier cohort
* de3b2c96 Add a metal-table provenance to the island survey profile
* f7fc4705 Decide where the Wilderness shrine sends a player
* 18bbd484 Make hull mass change how a ship flies
* c2099aa5 Give the ship hull its materials on the wire
* 3228a715 Accept material variety when crafting
* b6c11fe6 Grow the surveyed species on every wooded release island
* 2e0c85d8 Author harvestable tree seats for the release world
* 9336a939 Drop, drive and retire the log a cut tree sheds
* 5ef00233 Author the fall a felled tree section never had
* 859a257d Point the client at our social server and stop forcing alliances off
* 02e05d74 Serve the Bossa social API from the login server
* 07ae259c Add schema v7 for social invites
* 6642e7eb Key the crew checkout snapshot on the ledger key
* 9ba44929 Record what Tier 1 already does and what it deliberately does not
* 298a8b5b Cover the Tier-1 rollout and its connect cost
* 4480270f Give release-world deposits their atlas shards
* bbe14ef0 Name the release-world tiers so the Wilderness cannot drift
* 7d898bcc Load island asset bundles asynchronously
* 12a398c5 Attribute frame spikes to asset loading
* 8350f6b3 Never restore a player onto terrain their client does not have
* a1eec9ef Select the pre-alliance crew UI
* 7c245a68 Record the 958c8e1 deployment and the v6 database migration
* 958c8e13 Grant the client authority over the crew action component

## 2026-08-17 | 37 commits

* 567b0b3e Wire crews to the client
* 3122ffb9 Persist crews
* 6f18a414 Add the pure crew domain, and a plan to revive crews faithfully
* cb5c621f Make the compact island shell solid, hard-edged and hazed
* d8275015 Record the c31e8be deployment and the v5 database migration
* c31e8bea Return a player to where they logged out
* 5760dc21 Give the compact island shell its underside back
* 54c70a0c Put the compact island shell where the island actually is
* 481b2880 Defer unbound world registrations instead of killing the boot
* 34fd6429 Record the ccfb138 deployment and repair the patcher default pack
* 7771b20e Label admin map provenance and reconcile island counts
* f7c53b6b Prefer the retail-LOD distant island shell
* 3d63eb6c Add staged complete release-world rollout
* d910c768 Record corrected zone signage deployment
* 5f1afbb1 Clarify release world tiers and district signage
* b12a92ad Record authentic SVG map deployment
* 17cc5d21 Redesign release world map as authentic SVG cartography
* e4065bb5 Record live world map deployment
* 3d64a7f0 Add live release-world operations map
* 22f3a9e3 Record island shell activation fix
* bede97e3 Start island shell waiter after activation
* c6331b35 Record distant shell deployment
* 7c99dac5 Add distant island visual shells
* 9b189b60 Record aboard interest deployment
* ee19d2b2 Record Trades proximity lifecycle acceptance
* 7fab2e2b Prefer ship-derived interest while aboard
* d0e994b4 Record authoritative interest deployment
* 1aa9fe4b Follow authoritative player transforms for world interest
* 2e88b319 Record terrain re-entry acceptance
* 09ffc228 Record terrain return fix deployment
* b52f504b Release terrain after confirmed teleport return
* c72823a8 Record terrain acceptance deployment
* 069a3729 Restore metal deposit visual variety
* c2fa5969 Seed the complete tier-one B3 map foundation
* eef206ce Document consolidated terrain checkout milestones
* fa833188 Observe optional terrain checkout from /admin
* 7cbb376b Stream optional island terrain by peer interest

## 2026-08-16 | 13 commits

* 710b4416 Archive Worlds Adrift community engineering data
* ab908d21 Seed recalled ship motion with persisted rotation
* 41a91c53 Complete component lifecycle before entity checkout removal
* 8794f904 Complete client cleanup for removed ship entities
* e2d88f0a Recall ships directly above selected players
* 25442cd0 Confirm teleport arrivals from bounded transforms
* fbccb4ac Document Mental Facility staged deployment
* 07270f1c Add guarded tier-one island test landing
* 3a287062 Correct first region rollout to tier-one B3
* effc639c Seed opt-in first-region terrain topology
* 768be8c3 Document 175839f production deployment
* 175839f1 Redesign admin simulation fabric
* 6f897103 Route world interest through local domains

## 2026-08-15 | 25 commits

* ec22297d Accelerate opposing helm input without increasing network cadence
* e6adf29f Trace helm input latency end to end
* 49de2cfe Predict local helm feedback without a server round trip
* 9d502670 Guard hierarchy teardown during streamed entity removal
* 585541ed Document 634aca2 production deployment
* 634aca2e Stabilize two-peer ship and avatar replication
* 8d955230 Rebuild ship checkout after admin recall
* 191daf10 Restore ship-relative remote avatar rendering
* 5929b463 Prevent stale spawn plans crashing ship persistence
* a2d2b4d9 Stop reliable upstream movement spirals
* 61dfbc3b Add real-wire two-peer ship acceptance
* adea795c Add deterministic two-peer ship acceptance gate
* 4a18bf28 Record ship recovery console deployment
* d8677813 Replace ship nudge diagnostic with recovery controls
* 7a179dd7 Record professional admin console deployment
* 48b381df Redesign admin as a simulation operations console
* 42084246 Record admin control panel deployment
* ab9bc940 Document authenticated world operations
* 18d89b3e Turn world inspector into an authenticated control panel
* fafd9ba1 Document domain-aligned relay and local inspector
* a5bed13c Align aboard relay with ship domain frames and add inspector
* d6484419 Record headless ship catchup correction
* 9143c5ad Keep ship domains out of generic catchup
* 2b96f386 Record remote ship connect deployment
* 718d926c Gate remote ship domains during connect

## 2026-08-14 | 44 commits

* f132cecf Record ship-domain corrective deployment
* 489517f0 Fix ship steering, passenger coherence, and re-entry
* 7a69e48e Use island-scale visibility for ship domains
* 6a2273f2 Add coherent local ship simulation domains
* d27e9f2d Record multiplayer server deployment
* 8bc961d2 Document multiplayer replication incident
* ba5987a9 Fix late-join entities and ship replication stalls
* 217959aa Document native client crash fix release
* 3a7cd319 Fix native spawn decoder heap corruption
* 3e815cd6 Document NTP-independent client release
* 26278103 Make client time sync fail open
* 9d893053 Add read-only world ownership directory
* af565a58 Add region topology and clean project history
* 5dd6c496 Document staged resource login rollout
* 203b1325 Stage resource streaming after client activation
* 88e73a02 Document resource login crash deployment
* a6de2cfd Prevent duplicate resource checkout during login
* e292a578 Document PR4 native deployment
* 1af716ca Add multi-island resource interest
* d6784fb5 Fix first admin teleport request being ignored
* 573c91d6 Keep streamed resources authoritative
* 92c07630 Add safe admin tools and guarded island travel
* f54455d0 Add island identity and first production terrain
* 21383ce6 Add validated WAMap reference importer
* ced4ebf7 Update handover for panel geometry diagnostic
* 355d842d Offset ship panels beyond the exterior skin
* c4badb33 Add canonical Wareborn engineering handover
* 7c3e6c4f Project roof panels above the hull envelope
* b2204c12 Snap panels to the aimed exterior hull face
* 171a2e59 Detect inactive panel placement phantoms
* a224cd76 Snap ship panels to exterior hull skin
* f837c5a1 Materialize every crafted ship-part output
* fc4efec6 Fix RemoveEntity Windows Int64 ABI
* 3b82e1d2 Enable ship-part salvage within owned yards
* 9afb5345 Salvage docked ship parts for recipe materials
* e11ccb47 Implement docked ship salvage and refund blueprint excess
* c7e71c80 Avoid double-rotating restored deck panels
* 709f6b4f Normalize all ship-part placement and interaction timing
* 53f074fe Allow empty shipyards to recapture persisted ships
* 8877df27 Retain visited resources until native unload is portable
* dd2bdab2 Fix streamed resource authority and legacy unloads
* e2e88bf8 Configure public server automatically in WAPatch
* 549c3d68 Document Wareborn progress and public join flow
* 10bdc84e Run game server natively on Linux

## 2026-08-13 | 38 commits

* 3d91d4aa Stream resources by proximity and improve helm and sails
* 80756d26 Fix station pickup, ship undocking, and latched helm throttle
* 4edf0de9 Fix live helm and sail interaction after mounting
* f1bf4e3f Populate Haven with a whole-island starter-biome resource field
* 9cab7618 Fix idle crafting UI blocker divergence
* 29a8a973 Spawn chain: ack timeout + gated-last-step park; rescue now acks; precache world prefabs
* ca5227e8 Craft-UI self-heal: diagnose and clear the stale CraftingInProgress latch
* 91e77d79 Station pickup: pack a placed shipyard/assembly station back into inventory
* a25d0897 Gate the static test ship behind WAREBORN_STATIC_SHIP (default OFF)
* 918f9827 Orientation probe v3: one-time per-mesh hull breakdown
* c027be54 Orientation probe v2: per-entity report, exclude mounted-entity subtrees from hull span
* 94a0d009 Add ground-truth orientation probe: log RENDERED hull/helm yaw + hull-local deck span
* f1b5ff90 ship: verify the long-hull path end to end, and make a wide hull unmistakable
* efc0b344 fix(ship): settle orientation - the bow is +Z everywhere; helm yaw back to 0
* f57fdd95 fix(flight): helm lock gains a yaw knob; Man E-hold clamped at the real timer seam
* 009ff0ff feat(parts): mounted sail/lamp/horn are interactable - furl, switch, honk
* a0069946 feat(flight): v3 - the mouse flies the ship (pitch + roll integrated)
* ed780715 fix(flight): helm mounts rotation-locked to the bow; Man hold clamped in the mod
* a44aebb6 fix(flight): the world has not ended - pin AtlasMultiplier at 1; faster helm grab
* cb80ed6f feat(flight): v2 feel pass - live helm, inertia + banking, pilot rig fixes
* 57fcb4c4 fix(flight): serve the mass chain, dismount on the pilot's real exit signal
* 9113ea96 diag(helm): trace ShipHelmPlacement verdict inputs on placement attempts
* 54581882 fix(flight): a MOUNTED helm serves the Man interact entry, not the generic PickUp
* b22d4f8d feat(flight): piloted ship flight - Man the helm and fly the built ship
* b745be06 fix(perf): kill the two live NRE floods + name silent interact failures
* e0b1fd63 tools(perf): thread-wall profiler + prepared WINE_CPU_TOPOLOGY experiment
* 4289653e perf(client): remove the mod's own steady-state frame costs
* 8d3434d8 feat(client): stutter attribution probe - every frame spike becomes one named line
* ae7c4151 feat(skycore): restore the orphaned socket system - modules snap onto the CoreMain base
* 56fcbad4 fix(server): ledger-gate every first-time-setup AddComponent send; close the MarkServed gaps
* b3ef9f64 fix(skycore): the generator is the base of the core chain, not a coreModule
* 2cd1ff7a fix(craft): every station craft renders its part; no craft may eat materials it cannot show
* 742c84bd feat(loading): whole static world behind the loading screen; shard hidden until exposed
* 978810a5 fix(atlas): release the ghost crystal when the collected shard entity sinks
* ef7b3818 fix(atlas): follow the core slot's pose instead of reparenting the shard view
* 0d75ebc3 fix(shards+fuel): push shard availability unconditionally; no PickUp prompt on canisters
* ed11dcff fix(salvage): give salvageable resources a real material so the beam works
* 9cfb8a52 feat(trees): recover all 65 per-species skeletons and cut each species with its own

## 2026-08-12 | 41 commits

* 13a53039 feat(resources): deterministic dense metal-deposit field from real Haven surface
* 2ada5b0f Client flood guards: skip missing worn-item keys, mute the benign visualiser-enable warning
* ae01977c feat(resources): lodge atlas shards in handshake-spawned deposits
* ee71df17 Deposit mining fidelity: render diagnostics, core-slot shard lodging, retail crust->core->yield staging
* 404e2015 fix(fuel): rework fuel acquisition from PICKUP to SALVAGE, with the retail 8/8/9 curve
* 3fa79fa2 feat(trees): per-species wood yield + respawn knob; document the falling-log and berry gaps
* 2369633b Harden the real resource-spawn handshake for live deployment
* 7b83d7dd Fix joiner burst: built ships/decks behind the barrier + pace the instantiation op
* 9518a7c6 fix(wearables): only rig-registerable utility-slot items enter 1280
* 2930d87e fix(ship): make built decks placeable and re-positionable
* d5d7e9b2 Implement the real WA island resource-placement handshake (server half)
* fa8fb60b fix(persistence): re-dock restored ship to its shipyard by persisted position
* 49042fa1 fix(wearables): exclude worn deployables from 1280 WearableUtilsState
* a73c0c92 Connect-time spatial interest streaming + remote-avatar re-seed fix
* 5dc71f89 Add shipped 190000/190001/190002 loading barrier to hide spawn-in work
* ed36c76b feat(fuel): acquirable FUEL crafting material - fuel pods + generalized lodgeable pickup
* 9abf8f33 feat(trees): deterministic tree respawn (P1-9) so the island stops deforesting
* 2355f82b feat(atlas): reconstructed refdata - real atlasShard item, recipes, spawn knob
* 5a98610b feat(atlas): retail atlas-shard acquisition vertical (deposit->mine->release->pickup->grant)
* 367424b4 Fix ship/shipyard ownership showing red after relog
* 62da85ec fix(craft): run station craft-start under a guard scope so a throw can't wedge the station
* 4175a5aa fix(mount): sail mounts across the whole deck; document per-part surface decisions
* e8c01fac fix(craft): keep the crafted schematic on the timed-craft COMPLETE push
* ecdc6092 feat(persistence): thread built-ship owner into the world-state record
* 2c4b320e fix(mount): helm mounts across the whole deck (attachmentType "deck") + coherent reseed rotation
* 72c48a77 feat(deck): dynamic per-frame deck generation from ShipPlan
* 2a30ce10 feat(persistence): comprehensive ship-build persistence (owner, loose parts, mounts)
* 6e4006b6 fix(mount): honor placed rotation + unblock re-placement; diagnose PartNotMountable
* 46cc8528 fix(helm): stop client freeze when crafting/loading a Helm loose part
* aa62fbd8 fix(parts): correct lamp loose-part prefab to real asset Lamp01; pin all names to client assets
* b1dedfc4 test(craft): lock in exact-amount consumption (no whole-stack wipe)
* b9ceb48b Fix relog shipbuilding-awareness gate (client-mod)
* 1aa7f8a7 Allow parented placement for ALL deployables (shipyard was un-placeable near other entities)
* bfbb9923 fix(craft): stop timed station craft sticking in the crafting animation
* 6318d1ba Wire shipyard build-access prerequisites for crafted-part lift
* 5ac835d5 Fix station craft blocked after one part: guarantee return-to-idle
* 41162801 Fidelity cheap wins: shipyard fold-out, craft materialize + timed craft, scan note text
* 4f1999ae Fix empty personal Crafting tab: route crafting state per (player, target)
* 46b1f470 Generalize loose-part crafting to every CraftingStation ship part
* 0a5deb48 Derive learned schematics from purchased nodes + faithful bench categories
* dbae9091 Fix multi-slot station crafting: route per-slot 1005 to the station

## 2026-08-11 | 41 commits

* 5d4c1347 Colin dup-seed fix + TESTING: grant whole catalogue as learned (show all recipes)
* a0515924 Fix shipyard placement crash: never allocate entity id 0 + dedup spawn seeds
* bfdde01d Fix UI crashes: serve 1240 (restores Logbook + Tab strip); crash-safe crafting-station open
* d72e46b9 Wire the full craft catalogue to the knowledge tree + make every recipe craftable
* 358b51d1 Fix assembly-station placement stranding tools + allow crafting-station parents
* 79bf5bae Add part-mounting flow (Phase 2): lift a loose part onto a built ship
* 402da705 test: pin knowledge restore to the live 9bae0367 DB row
* 61068b54 Persist knowledge; fix relog inventory wipe (roster-rewrite cascade)
* c7e21a10 Fix assembly station placement: use the loadable "CraftingStation" prefab
* a2166c75 Fix all inventory/schematic icons to real client textures
* 0ee3a15f Add Assembly Station: place + interact opens the parts crafting UI
* 8fda2a4b Fix stale test: lamp is now CraftingStation category
* 5fe951f8 Workbench prep: lamp->CraftingStation category, assemblyStation+lamp learnable for testing
* 38af26ae Merge ship-part catalogue + craft-loose-part; add lamp as test-starter
* 1202a1a7 Craft a loose lamp ship-part as a world entity (Phase 1)
* c018ad37 Phase 0: add engine ship-part schematic to the served catalogue
* d6493acd Harden inventory DB load so enabling persistence can never wipe items
* dc760729 Persistence: shared world state survives restart (placed shipyards + built ships)
* 7032490e Ship-build correctness: dock above, one ship per yard, live rename
* 18c9f2b4 Ship-build Phase 3: craft completion spawns a real boardable ship
* daab400d Ship-build: quality:0 test recipe + push inventory back on rejected drag (un-grey)
* 58e8f97f Ship blueprint Phase 2: material loading + craft transaction
* 1848a3d4 Ship-build: fix BLANK panel on Done by re-emitting the interact-open signal
* 41aa8045 Ship-build Phase 1: non-empty cost bill on blueprint select
* 64d87b17 Ship-build: repopulate panel on Done + fix owner-id so SAVE enables
* add3763c Ship-build: clear BusyModel on EVERY 1270 command, not just RefreshBlueprints
* c8cbda8f Ship-build: serve 1271 + 1450 so the editor-active interest batch survives
* 45e7162b Ship-build Phase 2: real starter frame + 1208 command handler + 1206 editor state
* bac3ca8f Shipyard build lists: kill the loading spinner (serve 1208/1270/1274 + reply)
* f096e516 Add pure server-side ShipPlan load/save model
* 44c67ffc Route real client icons for shipyard (item_shipyard) + makeshiftStorage (2x2_makeshift_storage)
* 7d17e720 Placed shipyard console interaction: Craft prompt + ship-build UI open
* a3ad3ffe Make 59 crafted outputs usable: data-driven deployable placement + item flags
* fd11e76a Make shipyard item equippable (Utility slot) so it can go on the hotbar for deploy
* 8997edc3 Thread peer into HandleOnePlacement so StopPlacing can reach the client
* 037db177 Placement: send 1019 StopPlacingItemEvent + Placing=false on confirm/fail so the client drops the preview ghost
* 46920a0e Deployable shipyard placement: 1017/1019 player components, 1017+1211 handlers, runtime spawn seam with rotation (WAREBORN_PLACEMENT=1)
* fb86fe9f Crash-proof crafting/knowledge: add 6 stub records for SCHEMATIC_FIXED node info-panel lookups + reference-data crash-safety validator
* 58948ac1 Remap recipe categories to valid client CraftingCategory enum (Utility/ShipParts/CraftingComponents crash Enum.Parse and blank the crafting panel)
* c68dcef9 Only learn catalogue-resolvable schematics on node unlock (unmapped node learning its raw name NREs the client crafting list)
* f03525a3 TESTING: remap recipe materials to client-known ids

## 2026-08-10 | 25 commits

* 8464b464 TESTING: set all recipe ingredient amounts to 1 for easy crafting
* 9e3213c3 Wire WA recipe catalogue into knowledge-gated crafting
* 76ca8cea Databank on-surface spawn position + testing grant amount + carry metal placement fix
* 0934b0ee Knowledge loop: scan a databank to gain, spend on the tree to learn
* 91feb0c3 Personal crafting: file-backed catalogue + real 1003 transaction
* dc32d0b7 Make the metal deposit render: seed the global biome table it blocks on
* 3d6a78be Implement real anchored metal-deposit ore mining (crust + core loop)
* d78d76d9 Add cobalt and aurium metals so every placed ore node yields
* 64ad57ea Re-parent the deck under the hull so standing on it carries the player
* 16877267 Echo owner's own 1073 relativeTo back to arm the ship carry
* d61488a1 Recognise the spawned hull as a ship: seed 8062/8071/4349 so ShipVisualizer enables
* 1bd1a1c0 Ferry: WAREBORN_SHIP_FERRY_START_DELAY holds the ship at rest so a player can board before it flies
* f2ff609a Keep bolted ship parts awake so they follow the moving hull
* b40cf86e Seed bolted ship parts hull-relative so they follow the moving hull
* 0adc35b5 Make ship spawn position env-configurable (WAREBORN_SHIP_POS) to place it without a rebuild
* e5920416 Stop the deck losing its solid collider on a repeat interest request
* 61ab260a Reconcile ShipFrameTests Y with the empirical raised hull height
* 00f745d6 Raise ship spawn Y so the deck sits above ground, not embedded
* ec7e4410 Bolt a walkable Deck01 onto the static ship hull
* d8a3c4e4 Manual F10 recovery + deep-net fall policy + arbitrary-coord teleport
* 10e5a636 Boardable ship: Helm01 part, Man verb, and aboard-detection (step 5)
* 98c42f2b Make the spawned ship fly: 1130 control-point carry probe + ferry (steps 3-4)
* 546fa80d Make metal ore harvestable: wire the salvage-hit path end to end
* c50667e6 Relay glider/tool visibility as low-rate 6910 events, not per-frame
* db9793bf Pace the AfterPlayer world spawn and make the test population count configurable

## 2026-08-09 | 19 commits

* 6d2e73a3 Fix Tab->Schematics sub-screen NRE that breaks the character sheet
* ea4f539e Distribute ore + trees across the reachable ground band for testing
* f426d8d3 Stop relaying 6910 entirely - unreliable wasn't enough, the volume bufferbloated the link
* 888cc015 Relay 6910 UtilitySlotActivatedState UNRELIABLY - fix the tool-fire congestion regression
* 533ee820 Close the gathering loop: harvest hit -> inventory yield + native toast (Phase 5.4)
* 29958c53 Place MetalNugget resource nodes on Haven (Phase 4) + fixes
* 1e62f6f7 Phase 3.1/3.2: grant 1211 authority so hotbar tool-switch (keys 1-8) works
* f0739072 Add browser /login page and login-gated /download page
* 52bec6e6 Serve the client patch through the login server, not Caddy
* e3ab04b7 Add a self-update patcher pipeline for the client
* c10b194a Add operator admin panel on the login server, fed by a game-server stats file
* 167eee00 FIX 2: stop the PB_*_Serialize buffer leaking on every send
* 6f1076af FIX 3: destroy native component refs on ForgetPeer and on reseed overwrite
* 2f5b70f8 FIX 1: crash-isolate the packet loop so one bad packet can't drop everyone
* 4e03c6aa Guard the harness against the one update shape v2 invented, and make bot deaths speak
* e8ea6617 relaybot: pin the pre-relay-v2 baseline soak (VERDICT: FLAT)
* ea2cb690 relaybot: headless two-bot harness that measures true relay staleness
* ad20993e Relay movement by cadence, not arrival, because rate is not age
* 6e36c021 Drain the queue faster than it fills, and log what is on the wire

## 2026-08-08 | 61 commits

* 7f5783ab Stop one unknown component costing you the whole other player
* ee54277a Bucket a session's traces over time, so 'it gets worse' can be measured
* 67f4827c Teach the server to say "this entity does not have that component"
* b3a66f80 Give the world a bottom, because the client never had one
* 70a75a23 Diagnosis: the error storm is not the stall, and my second guess was wrong too
* 8a317b46 Diagnosis: I blamed the tree for the tab menu and I was wrong
* 08fb9830 One choppable tree on Haven, four metres from the player spawn
* c81b63a7 Spawn a ship on Haven, because a ship is four components, not a shipyard
* 326c47f5 Stop the inventory destroying itself, then give it a home
* 8034906f Teleport players by 190607, the parentless path that needs no new authority
* 10f0202c Generalise the spawn seam from {island, player} to N world entities
* a8eef582 Research: the first ship, and it is far cheaper than we assumed
* aae0b57b Research: no island in the world has a single tree placed as scenery
* ce19ef4d Extract the ship-entity evidence: prefab census, Require maps, hull blob
* ee3d536e Research: the authored first hour, and it does not involve a ship
* db1fb382 research(loop): world-wide island prop census + recovered tree wood types
* 65240368 Extract the authored first hour from the shipped client assets
* 03a84710 docs: findings on the harvest transaction (loop research)
* 83d86103 Stop attaching stack traces to ordinary log lines
* 3f438bb0 Stop ChararacterDrunk from re-throwing an NRE every single frame
* f292799a Probe: report which floating-origin strategy is actually live
* 73309c51 Spawn on the measured Haven point, not the pre-TRS one
* 3d715a5b Fix island surface extractor: compose full TRS, not a sum of localPositions
* e947b517 Spawn on Haven: make 190602 seeding entity-aware, and stop lying about Haven
* d95c0466 Sign-up page: wind, and the game's own UI
* ca15ddab Serve sign-up over HTTPS at wareborn.ratlabs.cc
* 14cda336 Run the login server natively; Wine cannot do Postgres SCRAM crypto
* 1ccfb329 Accounts: real login, sign-up page, per-account character rosters
* ae2c0749 Add WorldsAdriftReborn.Storage: accounts, sessions and characters
* c238d39d docs: deploy servers with publish -r win-x64, not build + flat glob
* f969d44f Ask the client to show its own login form
* 43c4b167 Database: SQLite, and our own objection to it was wrong
* 4523a1d7 Login UI confirmed present, plus the sign-up page and its landmines
* fd847222 Auth research: the login form is already in the game
* baf56f54 Accounts research: the real Steam id is already on the wire
* f03b2a1e Spawn research: the 110km mystery was never a bug, and our surface tables are wrong
* bcfbabbe Haven research: spawn there, but seed isNewPlayer false anyway
* 5e9aa7af Add the phased plan for accounts, starter island, resources and tools
* fdccc258 Fix the interaction-release seed on the correct argument
* 78bc269f Log an untouched config key once, not 327,713 times
* 87818e78 Add tree-harvest spec: one integer harvests a tree
* fed6ed66 Add node-spawning research and the extracted island surface tables
* 84fcd914 Add gathering research: progression, metal deposits, inventory
* 5666ddae Add tool-system research: one line makes tool use observable
* 3ae1c760 Add gathering research: crafting, items/materials, node relay
* 766eafde Stop the server logging itself to a standstill
* a8ac3736 Prove ENet survives deinit/init in one Wine process
* 47cf1277 Decide local-vs-remote by components only, never by name
* cfc6eab4 Backfill a regression suite over the multiplayer rules
* 7735a87e Send the client's reliable packets reliably
* ecd3d766 Stop fabricating component values that damage the client
* 292d3c83 Record round-2 verification: three claims corrected
* c545bfe4 Measure the mirror timeouts in seconds, not in ENet events
* 5e31589a Restore player input during the first 25 seconds after world load
* 1a1ed0e4 Correct the docs against the seven research reports
* 40287f3a Add the real Worlds Adrift world layout
* 35e330e2 Add the seven research briefs and findings
* 3a1860c3 Persist the character roster
* 53dfa7ca Document VPS hosting, ports, deployment and client distribution
* e42669ba Fix one-way visibility safely, and relay high-rate streams unreliably
* ab43a9c9 Set the client game port via an exported native setter, not an environment variable

## 2026-08-07 | 17 commits

* 684e54b1 Fix the infinite sky-fall: disable mirror resends, guard the local rig by component
* fcece883 Resend mirror ops so the joining client reliably spawns the other player
* baf2f675 Stop drawing our own rope: the game renders it natively once 1098 is seeded
* 753657ab Re-assert rope line width every update + diagnose the wedge
* 4f95743e Hide the remote grapple tube unconditionally, not just while the rope is up
* 754cade3 Hide the remote grapple tube continuously while grappling, not once at bind
* 4ea6f7e3 Hide the game's GrapplingHookTube on remote rigs; add local-player fall telemetry
* 3a49c508 Style the remote grapple line: thin dark rope, not a magenta wedge
* 0044868e Make the anti-yeet neutralize deterministic: run it in FixedUpdate
* fc765fa6 Phase 4a: replicate the grapple rope line
* 0c6e8603 Roadmap: mark Phases 1a/2/3 + yeet fix done; flag Phase 4 as the hard frontier
* 4d067322 Fix 'yeet into the sky on second join': neutralize remote rig physics on frame one
* deeb6063 Phase 3: replicate the glider via UtilitySlotActivatedState (6910)
* 69e51482 Fix asymmetric visibility: fallback flush for parked mirror ops
* 4d37822a Phase 2: relay worn gear to other players
* 5399f042 Phase 1a: adopt PlayerVisualizer's interpolator for smooth remote movement
* b7f73291 Roadmap: add phased remote-player fidelity plan (smoothing, gear, glider, grapple/VFX)

## 2021-08-07 | Built on WorldsAdriftReborn

Wareborn is not a from-scratch server. It stands on the original WorldsAdriftReborn project, which worked out how to talk to the client at all - 160 commits by killzoms, sp00ktober, mmjr-x, Cat and others, from 2021 onwards. That history is in this repository and is not listed above, because it is theirs and not ours.
