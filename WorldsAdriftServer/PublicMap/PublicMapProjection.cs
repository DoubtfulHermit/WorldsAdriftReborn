using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using WorldsAdriftServer.Admin;

namespace WorldsAdriftServer.PublicMap
{
    /// <summary>
    /// The PUBLIC map's data projection: the anonymized half of a two-tier
    /// design in which the authenticated admin console and the public map are
    /// ONE renderer fed by TWO projections. This class is the entire boundary
    /// between them - there is no auth check downstream of it, because nothing
    /// sensitive survives it to be protected.
    ///
    /// The rules, in force here and nowhere else:
    ///
    ///   WHITELIST, never blacklist. Every field the public feed carries is
    ///   named in this file, read from the TYPED snapshot
    ///   (<see cref="GameStatsSnapshot"/>), and rebuilt into a fresh JObject.
    ///   A field the game server starts writing tomorrow - a player name, a
    ///   ship owner, anything - does not flow here until someone adds a line,
    ///   and PublicMapProjectionTests' leak corpus is there to make that
    ///   addition a deliberate act.
    ///
    ///   ABSENCE is the signal. A public marker simply has no name field; the
    ///   shared renderer draws whatever a marker carries and labels the rest
    ///   generically ("Traveller", "Ship"). There is no isPublic flag for a
    ///   renderer branch to get wrong, and promoting a field later (opt-in
    ///   named ships, say) is one line here plus its test - not a renderer
    ///   change.
    ///
    ///   IDENTITY is unlinkable, but markers are stable. Entity ids are small
    ///   integers and peer ids are addresses; neither may appear. Instead each
    ///   live marker gets an opaque token: SHA-256 over a per-process random
    ///   salt plus the id. Stable across polls (so a marker glides instead of
    ///   teleporting), rotated every server restart (so nobody can correlate a
    ///   marker across days), and not brute-forceable without the salt.
    ///
    /// What the public feed carries: snapshot freshness, the online COUNT, the
    /// map's own viewer COUNT, the fauna roster and clock (creature counts and a
    /// number of seconds - no identity exists in them), anonymous positioned
    /// player markers, and anonymous ship markers with pose/silhouette hints.
    /// What it never carries: names, account or character ids, peer ids,
    /// RTT/health/packet telemetry, connect times, pilot/aboard linkage, entity
    /// ids, or any operator surface. And, new with the viewer count and worth
    /// naming because it is the field somebody would reach for next: no source
    /// address, no user agent, no referrer, no country, in any form, hashed or
    /// otherwise - the server does not read them to build this payload at all.
    /// </summary>
    internal static class PublicMapProjection
    {
        /// <summary>
        /// The process-lifetime anonymization salt. Regenerated every boot on
        /// purpose: marker tokens must not be correlatable across restarts.
        /// </summary>
        internal static readonly byte[] ProcessSalt = RandomNumberGenerator.GetBytes(32);

        /// <summary>
        /// Builds the public live payload. Pure: everything it reads arrives
        /// as a parameter, so tests can drive it with a fabricated snapshot
        /// and a fixed salt.
        ///
        /// <paramref name="viewers"/> is the map's own audience - see the note on
        /// the <c>viewers</c> field below. It is a parameter rather than a read of
        /// <see cref="ViewerCensus.Shared"/> so that this stays a pure function of
        /// its arguments, which is the property the leak corpus depends on.
        /// </summary>
        internal static JObject Project(GameStatsResult result, byte[] salt, int viewers = 0)
        {
            JObject root = new JObject
            {
                ["reporting"] = result.State == GameStatsState.Ok,
                ["state"] = result.State.ToString().ToLowerInvariant(),
            };

            // ADMITTED DELIBERATELY: how many browser tabs have this page open.
            //
            // It belongs on a whitelist that otherwise refuses everything about
            // people, so it is worth saying exactly why it is not the same kind of
            // thing. It is a COUNT with no members: it is computed from ephemeral
            // per-page-load tokens the browser mints and the server holds only as
            // salted hashes for thirty seconds (see ViewerCensus), the server never
            // reads a source address to produce it, and there is no companion array
            // anywhere in this payload that a viewer appears in. Publishing the
            // integer 4 tells a reader that four tabs are open and cannot be made
            // to tell them whose - not by correlating it with the player markers
            // (different salt kind, different population, no shared key), and not
            // by watching it change, which is exactly as informative as watching a
            // "4 travellers" chip change.
            //
            // The honest caveat: on a very quiet map, a count of 1 means the person
            // reading it is probably alone, and a count that ticks from 1 to 2 tells
            // them somebody else arrived. That is the irreducible content of any
            // presence number and it is what was asked for.
            //
            // Set BEFORE the degraded branch, because it is a fact about the
            // WEBSITE rather than about the game server: people are still looking at
            // this page while the game server is down, and dropping the field during
            // an outage would make the page unable to tell "nobody is here" from
            // "we stopped counting".
            root["viewers"] = Math.Max(0, viewers);

            if (result.State != GameStatsState.Ok || result.Snapshot == null)
            {
                root["players"] = new JArray();
                root["ships"] = new JArray();
                return root;
            }

            GameStatsSnapshot s = result.Snapshot;
            root["ageSeconds"] = Math.Round(result.Age.TotalSeconds, 1);
            root["stale"] = result.Stale;
            root["currentOnline"] = s.CurrentOnline;
            root["fauna"] = ProjectFauna(s.Fauna);
            root["players"] = ProjectPlayers(s.Players, salt);
            root["ships"] = ProjectShips(s.ShipDomains, salt);
            root["shipModel"] = ProjectShipModel(s.ShipModel);
            return root;
        }

        /// <summary>
        /// The fauna section, REBUILT rather than forwarded even though
        /// <see cref="GameFaunaStat"/> already allowlisted it: the admin copy
        /// carries operator capacity tuning (budget, demand, per-peer budget,
        /// pose cadence) that the public page has no business knowing. What
        /// survives is exactly what drawing a creature needs - the clock, the
        /// per-island roster - and none of it is identity.
        /// </summary>
        private static JObject ProjectFauna(GameFaunaStat fauna)
        {
            JArray islands = new JArray();
            if (fauna.Json["islands"] is JArray roster)
            {
                foreach (JToken token in roster)
                {
                    if (token is not JObject island) continue;
                    islands.Add(new JObject
                    {
                        ["islandId"] = (string?)island["islandId"] ?? "",
                        ["mantaRays"] = (int?)island["mantaRays"] ?? 0,
                        ["jellyFish"] = (int?)island["jellyFish"] ?? 0,
                    });
                }
            }

            return new JObject
            {
                ["present"] = fauna.Present,
                ["enabled"] = fauna.Enabled,
                ["clockSeconds"] = (double?)fauna.Json["clockSeconds"] ?? 0,
                ["liveCount"] = fauna.LiveCount,
                ["islands"] = islands,
                ["ecology"] = ProjectEcology(fauna.Json["ecology"] as JObject),
            };
        }

        /// <summary>
        /// The ecology block, ADMITTED DELIBERATELY (schema v9). Everything in
        /// it is world geometry (bloom paths - the fauna equivalent of an
        /// island's coastline), creature counts, and behaviour labels the game
        /// derives from its own clock; there is no player, account, peer or
        /// entity identity anywhere in the section, which is the same test the
        /// fauna roster above already passed. The shared renderer needs the
        /// blooms and groups to animate field-following motion, and the
        /// capacity/expressed/quiet numbers are what lets the page say "this
        /// island is deliberately quiet" instead of looking broken - a public
        /// visitor is exactly who would otherwise report it as a bug.
        ///
        /// Rebuilt field by field, like everything here, even though the source
        /// (<see cref="GameFaunaStat"/>'s own allowlisted rebuild) is already
        /// sanitized: the projection's whitelist must never depend on an
        /// upstream one staying strict.
        /// </summary>
        private static JObject ProjectEcology(JObject? e)
        {
            JArray islands = new JArray();
            if (e?["islands"] is JArray rows)
            {
                foreach (JToken token in rows)
                {
                    if (token is not JObject island) continue;

                    JArray groups = new JArray();
                    if (island["groups"] is JArray groupRows)
                    {
                        foreach (JToken groupToken in groupRows)
                        {
                            if (groupToken is not JObject group) continue;
                            groups.Add(new JObject
                            {
                                ["species"] = (string?)group["species"] ?? "manta",
                                ["index"] = (int?)group["index"] ?? 0,
                                ["bloom"] = (int?)group["bloom"] ?? 0,
                                ["members"] = (int?)group["members"] ?? 0,
                                ["behaviour"] = (string?)group["behaviour"] ?? "Cruise",
                                ["epochSeconds"] = (double?)group["epochSeconds"] ?? 0,
                                ["durationSeconds"] = (double?)group["durationSeconds"] ?? 0,
                                ["toBloom"] = (int?)group["toBloom"] ?? 0,
                            });
                        }
                    }

                    JArray blooms = new JArray();
                    if (island["blooms"] is JArray bloomRows)
                    {
                        foreach (JToken bloomToken in bloomRows)
                        {
                            if (bloomToken is not JObject bloom) continue;
                            JObject rebuilt = new JObject
                            {
                                ["species"] = (string?)bloom["species"] ?? "manta",
                                ["index"] = (int?)bloom["index"] ?? 0,
                            };
                            foreach (string field in new[]
                            {
                                "amplitude", "sigma", "annulusRadius", "radialDrift",
                                "angularDrift", "omegaRadial", "omegaAngular",
                                "omegaMigration", "phaseRadial", "phaseAngular", "baseAngle",
                            })
                            {
                                rebuilt[field] = (double?)bloom[field] ?? 0;
                            }
                            blooms.Add(rebuilt);
                        }
                    }

                    islands.Add(new JObject
                    {
                        ["islandId"] = (string?)island["islandId"] ?? "",
                        ["quietFactor"] = (double?)island["quietFactor"] ?? 0,
                        ["mantaCapacity"] = (int?)island["mantaCapacity"] ?? 0,
                        ["jellyCapacity"] = (int?)island["jellyCapacity"] ?? 0,
                        ["mantaExpressed"] = (int?)island["mantaExpressed"] ?? 0,
                        ["jellyExpressed"] = (int?)island["jellyExpressed"] ?? 0,
                        // The rhythm's phase: a fact about the WORLD's ecology
                        // ("the jellies here are collapsing"), the exact thing a
                        // public map of a living world is for, and identity-free.
                        ["mantaPhase"] = (string?)island["mantaPhase"] ?? "Bloom",
                        ["mantaPhaseFraction"] = (double?)island["mantaPhaseFraction"] ?? 0,
                        ["jellyPhase"] = (string?)island["jellyPhase"] ?? "Bloom",
                        ["jellyPhaseFraction"] = (double?)island["jellyPhaseFraction"] ?? 0,
                        ["groups"] = groups,
                        ["blooms"] = blooms,
                    });
                }
            }

            return new JObject
            {
                ["enabled"] = (bool?)e?["enabled"] ?? false,
                ["islands"] = islands,
            };
        }

        /// <summary>
        /// The dead-reckoning model: the acceleration, speed ceiling and error
        /// tolerance the browser carries a hull forward with between snapshots.
        /// Pure physics - the same constants for every ship in the world, no
        /// more identifying than gravity - and the map cannot draw a moving
        /// hull without them. Rebuilt rather than forwarded, like everything
        /// else here.
        /// </summary>
        private static JObject ProjectShipModel(GameShipModelStat model)
        {
            JObject m = model.Json;
            return new JObject
            {
                ["present"] = (bool?)m["present"] ?? false,
                ["accelMps2"] = (double?)m["accelMps2"] ?? 0,
                ["maxSpeedMps"] = (double?)m["maxSpeedMps"] ?? 0,
                ["windowSeconds"] = (double?)m["windowSeconds"] ?? 0,
                ["maxWindowSeconds"] = (double?)m["maxWindowSeconds"] ?? 0,
                ["toleratedErrorMetres"] = (double?)m["toleratedErrorMetres"] ?? 0,
            };
        }

        /// <summary>
        /// Player markers: an opaque id and a position, nothing else. Players
        /// without a reported position are represented only in the online
        /// COUNT - an unpositioned public marker would say "someone connected
        /// but not where", which is the kind of presence telemetry this feed
        /// exists to avoid narrating.
        /// </summary>
        private static JArray ProjectPlayers(IReadOnlyList<GamePlayerStat> players, byte[] salt)
        {
            JArray projected = new JArray();
            foreach (GamePlayerStat p in players)
            {
                if (!p.HasPosition) continue;
                projected.Add(new JObject
                {
                    ["id"] = AnonymousId("player", p.EntityId, salt),
                    ["hasPosition"] = true,
                    ["x"] = p.X,
                    ["y"] = p.Y,
                    ["z"] = p.Z,
                });
            }
            return projected;
        }

        /// <summary>
        /// Ship markers: opaque id, pose, the motion terms the browser
        /// dead-reckons with, and the hull's real outline.
        ///
        /// WHY THE PUBLIC MAP GETS REAL HULL SILHOUETTES. A hull outline is a
        /// shape in the world, like an island's coastline - it is what anyone
        /// standing on a nearby island can already see, and it carries no
        /// name, no owner and no account. It is also the whole point of
        /// sharing the map: the ships people built are the thing worth
        /// looking at, and a generic triangle would be a map of nothing.
        ///
        /// The honest caveat, recorded rather than hidden: a sufficiently
        /// distinctive custom hull is recognisable to someone who has seen it
        /// in game, so a silhouette is a weak fingerprint in a way a triangle
        /// is not. That is accepted here because the same shape is already
        /// visible to any player who flies past it, and because nothing in
        /// this payload turns "that hull" into "that person" - no name, no
        /// owner uid, no pilot linkage, and a marker token that changes every
        /// time the server restarts. If that trade is ever unwanted, the fix
        /// is one line: drop the hull block and the renderer falls back to
        /// its plain ship mark on its own.
        ///
        /// Pilot and aboard linkage stays admin-only regardless: publishing
        /// which anonymous player rides which ship would let the two
        /// anonymized streams re-identify each other. The hull's
        /// <c>ownerCharacterUid</c> is likewise never copied - it is the one
        /// field in the hull block that names a person.
        /// </summary>
        private static JArray ProjectShips(IReadOnlyList<GameShipDomainStat> ships, byte[] salt)
        {
            JArray projected = new JArray();
            foreach (GameShipDomainStat ship in ships)
            {
                string domainId = (string?)ship.Json["domainId"] ?? "";
                JObject marker = new JObject
                {
                    // The renderer keys ships by hullEntityId; giving it the
                    // opaque token keeps selection and dead reckoning working
                    // while the real entity id never leaves the server.
                    ["hullEntityId"] = AnonymousId("ship", domainId, salt),
                    ["id"] = AnonymousId("ship", domainId, salt),
                    ["x"] = (double?)ship.Json["x"] ?? 0,
                    ["y"] = (double?)ship.Json["y"] ?? 0,
                    ["z"] = (double?)ship.Json["z"] ?? 0,
                    ["active"] = (bool?)ship.Json["active"] ?? false,
                    ["deckCount"] = Math.Max(0, (int?)ship.Json["deckCount"] ?? 0),
                    // The motion terms the browser carries a hull forward with
                    // between snapshots. Geometry, not identity.
                    ["yawRadians"] = (double?)ship.Json["yawRadians"] ?? 0,
                    ["yawRateRadPerSec"] = (double?)ship.Json["yawRateRadPerSec"] ?? 0,
                    ["vxMps"] = (double?)ship.Json["vxMps"] ?? 0,
                    ["vyMps"] = (double?)ship.Json["vyMps"] ?? 0,
                    ["vzMps"] = (double?)ship.Json["vzMps"] ?? 0,
                    ["hull"] = ProjectHull(ship.Json["hull"] as JObject),
                };
                if (ship.Json["headingDegrees"]?.Type is JTokenType.Float or JTokenType.Integer)
                {
                    marker["headingDegrees"] = (double?)ship.Json["headingDegrees"];
                }
                projected.Add(marker);
            }
            return projected;
        }

        /// <summary>
        /// The hull block, rebuilt field by field. Everything here describes
        /// the SHIP - its outline, its dimensions, what it is built from -
        /// and nothing describes who owns it. Note what is conspicuously
        /// absent and must stay absent: <c>ownerCharacterUid</c>.
        /// </summary>
        private static JObject ProjectHull(JObject? hull)
        {
            JArray outline = new JArray();
            if (hull?["outline"] is JArray ring)
            {
                foreach (JToken point in ring)
                {
                    outline.Add((double?)point ?? 0);
                }
            }

            return new JObject
            {
                ["present"] = (bool?)hull?["present"] ?? false,
                ["docked"] = (bool?)hull?["docked"] ?? false,
                ["beamMetres"] = (double?)hull?["beamMetres"] ?? 0,
                ["keelMetres"] = (double?)hull?["keelMetres"] ?? 0,
                ["deckPlaneMetres"] = (double?)hull?["deckPlaneMetres"] ?? 0,
                ["bowLocalZMetres"] = (double?)hull?["bowLocalZMetres"] ?? 0,
                ["sternLocalZMetres"] = (double?)hull?["sternLocalZMetres"] ?? 0,
                ["cellCount"] = Math.Max(0, (int?)hull?["cellCount"] ?? 0),
                ["hullDeckCount"] = Math.Max(0, (int?)hull?["hullDeckCount"] ?? 0),
                ["sectionCount"] = Math.Max(0, (int?)hull?["sectionCount"] ?? 0),
                ["keelIsLongestAxis"] = (bool?)hull?["keelIsLongestAxis"] ?? false,
                // What it is built OF is a property of the ship, like its
                // beam - visible to anyone who walks its deck.
                ["woodId"] = (string?)hull?["woodId"] ?? "",
                ["woodQuality"] = Math.Max(0, (int?)hull?["woodQuality"] ?? 0),
                ["metalId"] = (string?)hull?["metalId"] ?? "",
                ["metalQuality"] = Math.Max(0, (int?)hull?["metalQuality"] ?? 0),
                ["outline"] = outline,
            };
        }

        /// <summary>
        /// The opaque marker token: 12 hex chars of SHA-256 over
        /// salt || kind || raw id. The kind is hashed in so a player and a
        /// ship that happen to share a numeric id still get unrelated tokens.
        /// </summary>
        internal static string AnonymousId(string kind, long entityId, byte[] salt) =>
            AnonymousId(kind, entityId.ToString(System.Globalization.CultureInfo.InvariantCulture), salt);

        internal static string AnonymousId(string kind, string raw, byte[] salt)
        {
            byte[] material = Encoding.UTF8.GetBytes(kind + " " + raw);
            byte[] payload = new byte[salt.Length + material.Length];
            Buffer.BlockCopy(salt, 0, payload, 0, salt.Length);
            Buffer.BlockCopy(material, 0, payload, salt.Length, material.Length);
            byte[] digest = SHA256.HashData(payload);
            return Convert.ToHexString(digest, 0, 6).ToLowerInvariant();
        }

        /// <summary>
        /// Serializes with the same HTML-escaping the admin payload uses, so
        /// the identical string is safe both as an HTTP body and inlined into
        /// a page's script bootstrap in Phase B.
        /// </summary>
        internal static string Serialize(JObject payload)
        {
            using StringWriter sw = new StringWriter();
            using (JsonTextWriter writer = new JsonTextWriter(sw))
            {
                writer.Formatting = Formatting.None;
                writer.StringEscapeHandling = StringEscapeHandling.EscapeHtml;
                payload.WriteTo(writer);
            }
            return sw.ToString();
        }
    }
}
