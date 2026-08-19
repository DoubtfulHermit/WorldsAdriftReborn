Worlds Adrift shut down in 2019. Wareborn is a fan-run server that puts it back online. This is what has changed on it, newest first.

Plenty is still missing, and things break. Where something is ours rather than Bossa's, it says so.

## 2026-08-19 | Starting the game | client patch

### Steam is no longer required

- The client starts without Steam running.
- Steam was never the problem you saw. It initialised fine, handed over a username, and then died fifteen seconds later waiting for an auth ticket. Worlds Adrift was delisted in 2019, so that ticket never arrives. The client asked again twice every twenty seconds, forever.
- Boot now takes about nine seconds. It used to hang for fifteen and then loop.
- Pressing LOGIN logs you in. There was a "link your Steam account" dialog sitting in front of every single sign-in. It is gone.
- The PvE card on the splash screen is greyed out. There is no PvE server. That screen was never a chooser anyway. Neither card had a click handler on it.
- CREATE ACCOUNT and FORGOT PASSWORD used to open dead Bossa pages. They point here now. FORUMS is now MAP and opens the [live map](/map).
- A connection error used to show one button, QUIT, which closed the client. A short outage or a typo cost you the whole session. The dialog can be dismissed.

### Alliance crests

- Every alliance has a crest, and its leader composes one at [/account](/account). Shape, division, a device, three colours.
- Sixty-one devices to choose from, up from fourteen.
- Retail never had a crest editor. The game only ever downloaded a picture from a URL somebody else had set. The builder is ours. The slot it fills is theirs.

### The whale

- There is now **one** whale in the entire world instead of one per region. It works through the islands of a region in turn, then crosses open sky to the next one, and that region has no whale at all until it comes back. A full lap takes a little over two hours.
- A ship parked in one place was being machine-gunned with whale song. It kept crossing the load boundary and calling again each time. Fixed.

### Haven

- The Revival Chamber stands on the ground instead of being buried to its waist. Half the building used to be under the terrain.
- The teleport plate moved to the foot of the building. The cost of standing the chamber up is that its one doorway is now nearly ten metres up a sheer wall, so the room itself cannot be walked into.

## 2026-08-18 | A world with something in it | six client patches

The biggest day so far. Almost everything below landed on the same date, in six cuts.

### The world is fuller

- The release world holds **5,347** things to find, up from 2,195.
- Metal: **1,930** deposits spread across all 254 islands. There were 354, on 38 of them. The other 216 islands had terrain, databanks and nothing to mine.
- Trees: **13,266** across 251 islands, up from 3,767 across 72.
- Every Tier-1 island has wood and ore now. Four of the 46 had metal before. Fourteen had trees.
- It is also near where you arrive. The nearest tree to a landing point is 6 to 36 metres away, median 13. The nearest ore is 8 to 57 metres, median 28. On the Isle of Lynerea it used to be 256 metres, if you found it at all.
- Only 61 islands were ever surveyed for metal before the shutdown, and 44 of those by one person. Islands with a survey keep their own numbers. The rest get metal inferred from what their tier cohort was carrying. That is a guess with evidence behind it, not a recovered fact, and the density rule we picked is ours.

### Wildlife

- Manta rays and jellyfish live on the islands. Rays circle in schools. Jellyfish drift under the rock by day and rise to deck height at night.
- Four jellyfish species. The rays have their tails and their sexes back.
- Populations rise and fall on their own. An island blooms, gets crowded, and falls back. The rays trail the food.
- A school travels with a mother and a calf. The calf is a quarter her size.
- Islands are not interchangeable. Bigger ones support more animals, some are nearly empty, and five Tier-1 islands are deliberately bare. That is the world, not a bug.
- A day is ten minutes long. That number is the client's own, not ours.
- All of this is built on components Bossa shipped and left inert: health, age, mortality, gender, species, flocking, habitat. The ecology on top of them is ours.

### A whale

- Something large enough to be worth going to look at. It flies a slow circuit over the islands and passes any one of them once a lap.
- It calls every couple of minutes. The call carries four kilometres. The whale itself only loads at 1.2 km, so you hear it coming about two and a half minutes before there is anything to see.
- The animal was finished and put in the game and then never given anything to do. It also came with five jokes bolted to it, including a two-hundred-metre light casting hard shadows. Those are off.

### Getting off Haven

- There is a shrine on Haven. Use it and it sends you to a random Tier-1 island.
- A crew arrives together, on their leader's island. The draw is spent once and then remembered, so the island you land on is your island.
- Haven is a sealed corridor. The nearest Tier-1 island is 9.3 km away and none of it streams in from there, so a teleport was the only way out. Bossa's own quest chain ends by telling you to use the Revival Chamber platform and teleport to the Wilderness with everyone standing on it. This is that.

### Crews and alliances

- Crews work. Found one, invite, accept, boot, leave, disband. They survive a restart.
- Alliances work. Ranks and permissions, invitations, applications, a motto, and the browser.
- Both run over the game's own Social Sheet, rebuilt from the client. Seventeen alliance endpoints. Before this, pressing CREATE on an alliance answered `E00001` and stopped there.
- Two bugs in the retail client are reproduced deliberately, because the client depends on them. One of them checks the chat permission before it lets you set the motto.
- Two real bugs turned up on the way. Accepting an application was impossible for anyone, and editing a rank would have demoted every member of it.

### A public map

- There is a live map of the world at [/map](/map). No login.
- Ships are drawn as the actual outline of the hull someone built, seen from above, pointing where they are heading.
- Wildlife moves on it. The server sends a headcount and its clock, and your browser runs the same movement maths the server runs, so the animals glide rather than jump every few seconds.
- Players are plain dots. No names, no accounts, nothing that says who anyone is. The ids behind the dots are scrambled and thrown away every restart.
- Open a ship and you get a general arrangement drawing of it. Plan above, profile below, one scale, bow to the right, every part where its owner mounted it.
- Click an island for what is on it: ore, trees, databanks, wildlife.

### Ships

- Hull material changes how a ship flies. A heavy hull handles like one.
- Salvage a docked part back off a ship and get the excess materials returned.

### Trees fall

- Cut a section and it topples. The direction comes from a hash of the tree, so everyone watching sees the same fall.
- The log it sheds drops, rolls, settles and is retired. Bossa's code spawns a replacement tree and swaps a mask. It never authored a fall at all.

### Frame rate

- In-world frame rate went from about **50 to about 120**.
- Unity sizes its job-worker pool from the number of CPUs it can see. On a 28-thread machine that is 27 workers, and under Wine every job dispatch woke all of them. That worked out at roughly 35,000 context switches per rendered frame.
- Pinning the game to a single CPU is what fixed it. One CPU or none: two is worse, three is a disaster, and letting it have everything is halfway back.
- Worst frames dropped from 41-47 ms to 27-33 ms.

### Loading

- Island asset bundles load asynchronously again. Our own offline-asset patch had quietly turned the game's async loader into a blocking one, and that was the stutter on approach.
- Resources stream per island rather than by raw distance, so an island stops looking empty while you stand on it.
- You are never restored onto terrain your client has not loaded.

### Still bad

- The worst Tier-1 island takes about thirty seconds to stream in. That is too long to call an island loaded.
- Nobody has mined the 1,930 new deposits in a live client. Only the ones that were already there.
- 13,266 trees have never been through the two-player desync soak.
- Whether the retail crew and alliance panels drive all of this correctly is unproven against a live client. The rules and the storage behind them are tested to death. The UI in front of them is not.

## 2026-08-17 | Where you left off

- You come back where you logged out.
- Crews are stored, so a crew is still a crew after a restart.
- The Tier-1 world went in: 46 islands across four map cells.
- Distant islands are visible as solid, hard-edged, hazed shells instead of empty sky.
- An operator console for the server, and the world map that grew into the public one.

## 2026-08-07 | Getting it running | 7 to 16 August

The first ten days. Everything here is the floor the rest stands on.

- Two people can be in the world at once and see each other, including worn gear, gliders and grapple lines.
- Accounts, sign-up, sign-in, and a self-updating patcher so the client keeps itself current.
- Mining. A deposit has a crust and a core, and you break one to get at the other.
- Crafting, and a knowledge tree you feed by scanning databanks.
- Ships. Design a hull, save the design, gather the materials, craft it, and it spawns as something you can board.
- Flight. Man the helm and fly it. The mouse flies the ship.
- Your inventory, your knowledge, your ships and where you left them all survive a restart.
- A loading screen that hides the spawn-in work, instead of dropping you into a world that is still assembling itself.
