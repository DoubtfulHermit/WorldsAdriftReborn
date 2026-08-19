using WorldsAdriftServer.Emblems;
using Xunit;

namespace WorldsAdriftServer.Tests
{
    /// <summary>
    /// What the emblem code's version field is FOR: a crest saved before the
    /// device table changed must still be the crest it was.
    ///
    /// The device table went from fourteen entries to sixty-one when the drawn
    /// sheet landed, and three procedural devices the sheet draws better were
    /// dropped, which shifted every index after them. Every stored crest in the
    /// database is a version 1 code written against the OLD table. So the two ways
    /// this can go wrong are both silent, and both are held here:
    ///
    ///  - refusing version 1 outright, which would fall every alliance back to its
    ///    generated default and wipe every crest anybody had built;
    ///  - accepting version 1 and reading its indices against the NEW table, which
    ///    would leave the crest saved but turn its anchor into a bolt.
    ///
    /// Neither shows up as an error anywhere. They show up as players asking why
    /// their alliance mark changed.
    /// </summary>
    public class EmblemVersionTests
    {
        /// <summary>
        /// What each version 1 device index was called, straight out of the
        /// vocabulary as it shipped. Written down here rather than derived,
        /// because a test that derived it from the current table would agree with
        /// any table.
        /// </summary>
        private static readonly string[] Version1Devices =
        {
            "None", "Hexagon", "Star", "Gear", "Compass rose", "Bolt", "Ring",
            "Triangle", "Crescent", "Saltire", "Cross", "Anchor", "Chevrons", "Sun",
        };

        [Fact]
        public void The_version_1_table_is_the_length_the_migration_assumes()
        {
            Assert.Equal(EmblemVocabulary.LegacyChargeCount, Version1Devices.Length);
        }

        [Fact]
        public void Every_version_1_device_still_means_the_device_of_that_name()
        {
            for (int legacy = 0; legacy < Version1Devices.Length; legacy++)
            {
                string code = "1-0-0-" + legacy + "-0-0-0";

                Assert.True(EmblemSpec.TryParse(code, out EmblemSpec spec),
                    "version 1 code " + code + " no longer parses - every stored crest just vanished.");

                Assert.Equal(Version1Devices[legacy],
                    EmblemVocabulary.ChargeNames[(int)spec.Charge]);
            }
        }

        [Fact]
        public void A_version_1_code_and_the_version_2_code_it_becomes_draw_the_same_crest()
        {
            // The names matching is not enough on its own: it would still pass if
            // the migration landed on an entry that was named right and drawn
            // wrong. This compares the pixels.
            for (int legacy = 0; legacy < EmblemVocabulary.LegacyChargeCount; legacy++)
            {
                Assert.True(EmblemSpec.TryParse("1-2-5-" + legacy + "-11-3-13", out EmblemSpec old));

                string migrated = old.ToCode();
                Assert.StartsWith(EmblemSpec.Version + "-", migrated, StringComparison.Ordinal);

                Assert.True(EmblemSpec.TryParse(migrated, out EmblemSpec now));
                Assert.Equal(old, now);

                Assert.Equal(EmblemPainter.Render(old, 64), EmblemPainter.Render(now, 64));
            }
        }

        [Fact]
        public void The_three_devices_the_sheet_replaced_land_on_the_drawn_ones()
        {
            // The only entries whose PICTURE changed, and the reason the version
            // moved at all. Each maps to the traced drawing of the same subject.
            foreach ((int legacy, string name) in new[]
            {
                (4, "Compass rose"),
                (11, "Anchor"),
                (13, "Sun"),
            })
            {
                int now = EmblemVocabulary.MigrateCharge(legacy);

                Assert.True(EmblemVocabulary.IsDrawnDevice((EmblemVocabulary.Charge)now),
                    name + " should have migrated to the drawn device, not stayed procedural.");
                Assert.Equal(name, EmblemVocabulary.ChargeNames[now]);
            }
        }

        [Fact]
        public void A_stored_marker_written_before_the_bump_still_reads()
        {
            // The database holds this string, not a URL and not a picture.
            Assert.True(
                EmblemUrlPolicy.TryReadStored("wareborn:emblem:1-0-6-3-1-7-13", out EmblemSpec spec));

            Assert.Equal(EmblemVocabulary.Shape.Heater, spec.Shape);
            Assert.Equal(EmblemVocabulary.Division.Bordure, spec.Division);
            Assert.Equal("Gear", EmblemVocabulary.ChargeNames[(int)spec.Charge]);
            Assert.Equal(1, spec.FieldColour);
            Assert.Equal(7, spec.DetailColour);
            Assert.Equal(13, spec.ChargeColour);
        }

        [Fact]
        public void Reading_an_old_code_and_writing_it_back_upgrades_it_exactly_once()
        {
            Assert.True(EmblemUrlPolicy.TryReadStored("wareborn:emblem:1-4-9-12-5-13-0", out EmblemSpec spec));

            string stored = EmblemUrlPolicy.Store(spec);
            Assert.Equal("wareborn:emblem:2-4-9-10-5-13-0", stored);

            Assert.True(EmblemUrlPolicy.TryReadStored(stored, out EmblemSpec again));
            Assert.Equal(spec, again);
        }

        [Fact]
        public void A_device_index_that_was_never_valid_is_still_refused()
        {
            // Version 1 had fourteen devices. A version 1 code naming device 20 was
            // not an emblem then, and migrating it would invent one the player
            // never chose.
            for (int legacy = EmblemVocabulary.LegacyChargeCount; legacy < 70; legacy++)
            {
                Assert.False(EmblemSpec.TryParse("1-0-0-" + legacy + "-0-0-0", out _));
            }
        }

        /// <summary>
        /// The generated crests, pinned.
        ///
        /// An alliance that has never opened the builder has NO stored code - its
        /// crest is recomputed from its uid on every request. So the arithmetic in
        /// <see cref="EmblemSpec.DefaultFor"/> is as load-bearing as any stored
        /// value, and dividing by a table that grew would have re-rolled every one
        /// of them the day the sheet landed. It rolls in the version 1 index space
        /// and migrates, and these literals are what stops that from quietly
        /// changing again the next time the table does.
        /// </summary>
        [Theory]
        // Each of these is the version 1 code the shipped build produced for that
        // uid, with ONLY the version and the device index moved by the migration:
        //   ...-1-3-4-13-... Sun    -> ...-2-3-4-40-... Sun    (drawn)
        //   ...-1-0-2-3-...  Gear   -> ...-2-0-2-3-...  Gear   (unmoved)
        //   ...-1-2-0-13-... Sun    -> ...-2-2-0-40-... Sun    (drawn)
        //   ...-1-0-6-9-...  Saltire-> ...-2-0-6-8-...  Saltire(shifted by one)
        [InlineData("2f9b6f2e-1c31-4f4a-9a3e-8d0f9c6b7a10", "2-3-4-40-6-1-4")]
        [InlineData("00000000-0000-0000-0000-000000000000", "2-0-2-3-6-1-0")]
        [InlineData("ffffffff-ffff-ffff-ffff-ffffffffffff", "2-2-0-40-6-1-0")]
        [InlineData("11111111-2222-3333-4444-555555555555", "2-0-6-8-6-1-0")]
        public void A_generated_crest_is_pinned_to_what_it_has_always_been(string id, string code)
        {
            Assert.Equal(code, EmblemSpec.DefaultFor(Guid.Parse(id)).ToCode());
        }

        [Fact]
        public void A_generated_crest_never_lands_on_a_device_version_1_could_not_express()
        {
            // The corollary of rolling in the old index space: every generated
            // crest is one of the fourteen devices that existed when the first one
            // was minted. That is the property, not an accident of the constants.
            Random random = new Random(4242);

            for (int i = 0; i < 500; i++)
            {
                byte[] bytes = new byte[16];
                random.NextBytes(bytes);

                EmblemSpec spec = EmblemSpec.DefaultFor(new Guid(bytes));
                string name = EmblemVocabulary.ChargeNames[(int)spec.Charge];

                Assert.Contains(name, Version1Devices);
                Assert.NotEqual("None", name);
            }
        }
    }
}
