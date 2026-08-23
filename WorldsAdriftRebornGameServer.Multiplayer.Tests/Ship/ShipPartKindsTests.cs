using System.Collections.Generic;
using WorldsAdriftRebornGameServer.Multiplayer.Ship;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship
{
    /// <summary>
    /// The classifier that decides which symbol a mounted part is drawn with on the
    /// ship card. Small, but it is the one place where a catalogue of thirty-odd
    /// parts is reduced to a handful of glyphs, so a silent misclassification would
    /// show a helm where an engine is - a drawing that is confidently wrong, which is
    /// the failure mode this whole card is written to avoid.
    /// </summary>
    public class ShipPartKindsTests
    {
        [Theory]
        [InlineData("helm", "Helm01", "deck", ShipPartKinds.Helm)]
        [InlineData("sail", "Sail01", "deck", ShipPartKinds.Sail)]
        [InlineData("deck", "Deck01", "deck", ShipPartKinds.Deck)]
        [InlineData("proceduralEngineDefault", "ModularEngine", "engine", ShipPartKinds.Engine)]
        [InlineData("proceduralWingDefault", "ModularWing", "wing", ShipPartKinds.Wing)]
        [InlineData("lamp", "Lamp01", "deck", ShipPartKinds.Lamp)]
        [InlineData("atlasSkyCore", "CoreMain", "deck", ShipPartKinds.Core)]
        [InlineData("skyCoreGenerator", "CoreGenerator", "coreModule", ShipPartKinds.Core)]
        [InlineData("barrel", "Barrel01", "deck", ShipPartKinds.Other)]
        [InlineData("altimeter", "Altimeter", "shipSurfaces", ShipPartKinds.Other)]
        public void The_catalogue_parts_classify_the_way_a_reader_would_name_them(
            string schematicId, string prefab, string attachment, string expected)
        {
            Assert.Equal(expected, ShipPartKinds.Classify(schematicId, prefab, attachment));
        }

        /// <summary>
        /// The MAIN sky core mounts on "deck" like a barrel does, so a classifier
        /// keyed on the attachment type alone would draw the ship's power plant as an
        /// anonymous crate. Called out separately because it is the one row where the
        /// weakest signal and the right answer disagree.
        /// </summary>
        [Fact]
        public void The_main_sky_core_is_a_core_even_though_it_mounts_on_the_deck()
        {
            Assert.Equal(ShipPartKinds.Core,
                ShipPartKinds.Classify("atlasSkyCore", "CoreMain", "deck"));
        }

        /// <summary>
        /// An unknown part is OTHER, never a guess. A schematic that drew an
        /// unrecognised part as an engine would be inventing a ship.
        /// </summary>
        [Fact]
        public void An_unknown_part_is_other_and_never_a_guess()
        {
            Assert.Equal(ShipPartKinds.Other, ShipPartKinds.Classify("whatever", "Thing01", "deck"));
            Assert.Equal(ShipPartKinds.Other, ShipPartKinds.Classify(null, null, null));
            Assert.Equal(ShipPartKinds.Other, ShipPartKinds.Classify("", "", ""));
        }

        /// <summary>
        /// Case is not a signal: the catalogue writes "atlasSkyCore" and a persisted
        /// record could carry any casing, and the two must not classify differently.
        /// </summary>
        [Fact]
        public void Classification_does_not_depend_on_case()
        {
            Assert.Equal(ShipPartKinds.Helm, ShipPartKinds.Classify("HELM", "", ""));
            Assert.Equal(ShipPartKinds.Core, ShipPartKinds.Classify("SKYCOREGENERATOR", "", ""));
        }

        /// <summary>
        /// Every kind the classifier can return must be one the drawer knows about,
        /// and every kind must have words for a legend. Without this a kind added
        /// here would reach the page as a symbol nobody styled and a label nobody
        /// wrote.
        /// </summary>
        [Fact]
        public void Every_kind_is_published_and_has_words()
        {
            HashSet<string> published = new HashSet<string>(ShipPartKinds.All);
            Assert.Contains(ShipPartKinds.Other, published);

            foreach (string kind in ShipPartKinds.All)
            {
                Assert.False(string.IsNullOrWhiteSpace(ShipPartKinds.Words(kind)));
            }

            // And a kind nobody has heard of still reads as something.
            Assert.Equal("Other part", ShipPartKinds.Words("no-such-kind"));
            Assert.Equal("Other part", ShipPartKinds.Words(null));
        }

        /// <summary>
        /// EVERY row of the real catalogue classifies, and none of the parts a player
        /// is likely to mount first falls through to "other". This is the test that
        /// notices when the catalogue grows a row the card cannot draw.
        /// </summary>
        [Fact]
        public void Every_catalogue_row_classifies_and_the_load_bearing_ones_are_named()
        {
            HashSet<string> named = new HashSet<string>();
            foreach (LoosePartDefinition def in LoosePartCatalogue.All)
            {
                string kind = ShipPartKinds.Classify(def.SchematicId, def.PrefabName, def.AttachmentType);
                Assert.Contains(kind, ShipPartKinds.All);
                named.Add(kind);
            }

            foreach (string expected in new[]
            {
                ShipPartKinds.Helm, ShipPartKinds.Sail, ShipPartKinds.Deck,
                ShipPartKinds.Engine, ShipPartKinds.Wing, ShipPartKinds.Lamp,
                ShipPartKinds.Core,
            })
            {
                Assert.Contains(expected, named);
            }
        }
    }
}
