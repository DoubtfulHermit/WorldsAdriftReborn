using System.Linq;
using WorldsAdriftRebornGameServer.Multiplayer.Materials;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Materials
{
    /// <summary>
    /// The material table is the keystone of ship materials: crafting matches
    /// against it, the 1099 wire list is built from it, and hull mass is computed
    /// from it. These tests pin the RECOVERED facts (which materials exist, their
    /// categories and rarities) and the ORDERING the recovered descriptions imply,
    /// which is the only thing making the chosen densities defensible.
    /// </summary>
    public class MaterialCatalogTests
    {
        [Fact]
        public void Every_retail_metal_is_present_with_its_recovered_rarity()
        {
            // RECOVERED from itemData.json. If this list ever changes, the change
            // is a claim about retail and needs a source, not a commit.
            var expected = new (string Id, int Rarity)[]
            {
                ("iron", 0), ("lead", 0), ("bronze", 0),
                ("tin", 1), ("orthite", 1), ("steel", 1), ("copper", 1),
                ("titanium", 2), ("nickel", 2), ("epilar", 2), ("silver", 2),
                ("aluminium", 3), ("gold", 3), ("eternium", 3), ("tungsten", 3),
            };

            foreach ((string id, int rarity) in expected)
            {
                ShipMaterial? material = MaterialCatalog.Find(id);
                Assert.NotNull(material);
                Assert.Equal(MaterialCategory.Metal, material!.Category);
                Assert.Equal(rarity, material.Rarity);
                Assert.True(material.IsRetail, id + " is a retail metal");
            }
        }

        [Fact]
        public void Every_retail_wood_is_present_and_untiered()
        {
            // RECOVERED: itemData.json gives the woods no rarity at all.
            string[] expected = { "cedar", "hemlock", "chestnut", "elm", "birch", "ash", "oak", "palm" };

            foreach (string id in expected)
            {
                ShipMaterial? material = MaterialCatalog.Find(id);
                Assert.NotNull(material);
                Assert.Equal(MaterialCategory.Wood, material!.Category);
                Assert.Null(material.Rarity);
            }
            Assert.Equal(expected.Length, MaterialCatalog.Woods.Count());
        }

        [Fact]
        public void Cobalt_and_aurium_are_flagged_as_not_retail()
        {
            // This project added them so every placed ore node yields something.
            // Anyone reading a stat off them must know it is not recovered.
            Assert.False(MaterialCatalog.Find("cobalt")!.IsRetail);
            Assert.False(MaterialCatalog.Find("aurium")!.IsRetail);
            Assert.All(
                MaterialCatalog.Materials.Where(m => m.IsRetail),
                m => Assert.NotEqual(string.Empty, m.Description));
        }

        [Fact]
        public void The_recovered_Alpha6_mass_table_is_reproduced_exactly()
        {
            // RECOVERED, verbatim from the Worlds Adrift wiki Metal page as archived
            // in 2019. These are retail's numbers, not ours; a change here is a claim
            // about retail and needs a source.
            var expected = new (string Id, double Kg)[]
            {
                ("aluminium", 0.33), ("titanium", 0.35), ("tin", 0.38), ("iron", 0.39),
                ("bronze", 0.42), ("nickel", 0.43), ("orthite", 0.43), ("epilar", 0.46),
                ("steel", 0.50), ("eternium", 0.50), ("copper", 0.55), ("lead", 0.56),
                ("silver", 0.66), ("tungsten", 0.70), ("gold", 0.73),
                // Wood page, same source.
                ("cedar", 0.13), ("hemlock", 0.15), ("chestnut", 0.17), ("elm", 0.18),
                ("birch", 0.20), ("ash", 0.22), ("oak", 0.23), ("palm", 0.25),
            };

            foreach ((string id, double kg) in expected)
            {
                Assert.Equal(kg, MaterialCatalog.Find(id)!.MassPerUnitKg, 3);
            }
        }

        [Fact]
        public void Retails_mass_order_is_NOT_real_world_density_and_must_not_be_corrected()
        {
            // The trap: real steel and iron have almost identical density, and real
            // lead is denser than silver. Retail disagreed with physics on both, and
            // a well-meaning "fix" towards real densities would silently rebalance
            // the game away from the recovered table. Pin the surprising pairs.
            Assert.True(Mass("steel") > Mass("iron"), "retail: steel 0.50 > iron 0.39");
            Assert.True(Mass("silver") > Mass("lead"), "retail: silver 0.66 > lead 0.56");
            Assert.True(Mass("gold") > Mass("tungsten"), "retail: gold 0.73 > tungsten 0.70");
        }

        [Fact]
        public void The_wood_order_that_three_independent_sources_agree_on_holds()
        {
            // The wiki Alpha 6 table, the Steam Comprehensive Guide's earlier table
            // and the WAEngenius workbook disagree on VALUES but give this exact
            // order. It is the single most corroborated fact in the material data.
            string[] lightToHeavy = { "cedar", "hemlock", "chestnut", "elm", "birch", "ash", "oak", "palm" };
            for (int i = 1; i < lightToHeavy.Length; i++)
            {
                Assert.True(Mass(lightToHeavy[i - 1]) < Mass(lightToHeavy[i]),
                    lightToHeavy[i - 1] + " must be lighter than " + lightToHeavy[i]);
            }
        }

        [Fact]
        public void Every_wood_is_lighter_than_every_metal()
        {
            double heaviestWood = MaterialCatalog.Woods.Max(m => m.MassPerUnitKg);
            double lightestMetal = MaterialCatalog.Metals.Min(m => m.MassPerUnitKg);
            Assert.True(heaviestWood < lightestMetal,
                "palm at " + heaviestWood + " must still be lighter than aluminium at " + lightestMetal);
        }

        [Fact]
        public void The_recovered_sky_core_lift_formula_reproduces_every_published_row()
        {
            // lift = 1000 + rate * (10 + quality), solved from the wiki's Q1/Q10
            // endpoints. If this expression is right it must hit BOTH endpoints for
            // every metal the table covers - which is what makes it recovered rather
            // than fitted.
            var published = new (string Id, double Q1, double Q10)[]
            {
                ("gold", 1093.5, 1170), ("silver", 1088, 1160), ("copper", 1082.5, 1150),
                ("aluminium", 1066, 1120), ("tin", 1049.5, 1090), ("nickel", 1044, 1080),
                ("tungsten", 1038.5, 1070), ("bronze", 1027.5, 1050),
                ("lead", 1022, 1040), ("steel", 1016.5, 1030), ("titanium", 1011, 1020),
            };

            foreach ((string id, double q1, double q10) in published)
            {
                Assert.Equal(q1, MaterialCatalog.SkyCoreLiftKg(id, 1), 2);
                Assert.Equal(q10, MaterialCatalog.SkyCoreLiftKg(id, 10), 2);
            }
        }

        [Fact]
        public void The_wikis_iron_row_is_internally_inconsistent_and_the_formula_is_preferred()
        {
            // HONESTY NOTE. Eleven of the wiki's twelve rows fit
            // lift = 1000 + rate*(10+quality) to the decimal. IRON does not: at
            // rate 3 the formula gives Q1 = 1033 and Q10 = 1060, but the published
            // row reads 1034 and 1061 - off by exactly one at BOTH endpoints, which
            // is the signature of a transcription slip in one row rather than a
            // different rule for iron. The formula is kept and this test records the
            // discrepancy so nobody "fixes" it later without knowing.
            Assert.Equal(1033.0, MaterialCatalog.SkyCoreLiftKg("iron", 1), 2);
            Assert.Equal(1060.0, MaterialCatalog.SkyCoreLiftKg("iron", 10), 2);
        }

        [Fact]
        public void A_core_with_no_metal_internals_lifts_the_bare_recovered_thousand_kilos()
        {
            Assert.Equal(1000.0, MaterialCatalog.SkyCoreLiftKg(null, 5));
            Assert.Equal(1000.0, MaterialCatalog.SkyCoreLiftKg("oak", 5));      // a wood is never an internal
            Assert.Equal(1000.0, MaterialCatalog.SkyCoreLiftKg("mithril", 5));  // unknown
            // Out-of-range quality clamps rather than extrapolating.
            Assert.Equal(MaterialCatalog.SkyCoreLiftKg("gold", 1), MaterialCatalog.SkyCoreLiftKg("gold", -3));
            Assert.Equal(MaterialCatalog.SkyCoreLiftKg("gold", 10), MaterialCatalog.SkyCoreLiftKg("gold", 99));
        }

        [Fact]
        public void Conductivity_is_derived_from_the_recovered_lift_table_not_invented()
        {
            // Retail's own ranking, so the descriptions and the numbers cannot drift
            // apart: gold "extremely high conductivity" tops it, silver "master of
            // only conductivity" and copper "a great conductor" follow, titanium
            // "a bad conductor" is last of the twelve.
            Assert.Equal(1.0, MaterialCatalog.Find("gold")!.Conductivity, 3);
            Assert.True(MaterialCatalog.Find("silver")!.Conductivity > 0.9);
            Assert.True(MaterialCatalog.Find("copper")!.Conductivity > 0.85);
            Assert.True(MaterialCatalog.Find("titanium")!.Conductivity < 0.15);
            Assert.True(MaterialCatalog.Find("titanium")!.Conductivity
                < MaterialCatalog.Find("copper")!.Conductivity);

            // Timber is an insulator and is never a core internal, so it conducts
            // nothing at all.
            Assert.All(MaterialCatalog.Woods, w => Assert.Equal(0.0, w.Conductivity));
        }

        [Fact]
        public void The_measured_durability_and_heat_rankings_match_the_recovered_descriptions()
        {
            // MEASURED (wing-science casing health): tungsten "unparalleled
            // resistance" is the most durable, copper "otherwise unexceptional" the
            // least of the twelve tested.
            Assert.Equal(1.0, MaterialCatalog.Find("tungsten")!.Durability, 3);
            Assert.True(MaterialCatalog.Find("lead")!.Durability > 0.95);   // "durable and strong"
            Assert.True(MaterialCatalog.Find("copper")!.Durability < 0.7);

            // MEASURED (engine-science MECHANICAL internals overheat): tungsten top,
            // tin "susceptible to heat" bottom. The COMBUSTION column would have
            // ranked tin near the top - that column is heat dissipation, not
            // resistance - so this pins that the right one was used.
            Assert.Equal(1.0, MaterialCatalog.Find("tungsten")!.HeatResistance, 3);
            Assert.True(MaterialCatalog.Find("tin")!.HeatResistance < 0.3);
            Assert.True(MaterialCatalog.Find("tin")!.HeatResistance
                < MaterialCatalog.Find("titanium")!.HeatResistance);
        }

        [Fact]
        public void Lookup_is_case_insensitive_because_the_island_catalogue_is_Title_case()
        {
            // release-runtime-catalog.json spells it "Aluminium"; itemData.json
            // spells the same metal "aluminium". Both must resolve.
            Assert.Same(MaterialCatalog.Find("aluminium"), MaterialCatalog.Find("Aluminium"));
            Assert.Same(MaterialCatalog.Find("birch"), MaterialCatalog.Find("Birch"));
            Assert.Same(MaterialCatalog.Find("iron"), MaterialCatalog.Find(" Iron "));
        }

        [Fact]
        public void An_unknown_or_empty_id_returns_null_and_never_throws()
        {
            Assert.Null(MaterialCatalog.Find(null));
            Assert.Null(MaterialCatalog.Find(""));
            Assert.Null(MaterialCatalog.Find("   "));
            Assert.Null(MaterialCatalog.Find("mithril"));
            Assert.Null(MaterialCatalog.Find("fuel"));       // real item, not a ship material
            Assert.Null(MaterialCatalog.Find("atlasShard")); // ditto
        }

        // ------------------------------------------------------------------
        // Satisfies - the rule that makes "a recipe that wants metal" accept
        // iron OR copper OR aluminium.
        // ------------------------------------------------------------------

        [Fact]
        public void A_Metal_requirement_accepts_every_metal_and_no_wood()
        {
            foreach (ShipMaterial m in MaterialCatalog.Metals)
            {
                Assert.True(MaterialCatalog.Satisfies("Metal", m.Id), m.Id + " is a metal");
            }
            foreach (ShipMaterial w in MaterialCatalog.Woods)
            {
                Assert.False(MaterialCatalog.Satisfies("Metal", w.Id), w.Id + " is not a metal");
            }
        }

        [Fact]
        public void A_Wood_requirement_accepts_every_wood_and_no_metal()
        {
            foreach (ShipMaterial w in MaterialCatalog.Woods)
            {
                Assert.True(MaterialCatalog.Satisfies("Wood", w.Id));
            }
            foreach (ShipMaterial m in MaterialCatalog.Metals)
            {
                Assert.False(MaterialCatalog.Satisfies("Wood", m.Id));
            }
        }

        [Fact]
        public void The_clients_WoodSlashMetal_pseudo_category_accepts_either_family()
        {
            Assert.True(MaterialCatalog.Satisfies("Wood/Metal", "oak"));
            Assert.True(MaterialCatalog.Satisfies("Wood/Metal", "tungsten"));
            Assert.False(MaterialCatalog.Satisfies("Wood/Metal", "fuel"));
        }

        [Fact]
        public void A_concrete_requirement_stays_exactly_as_strict_as_before()
        {
            // Widening families must not quietly widen a slot that names one thing.
            Assert.True(MaterialCatalog.Satisfies("iron", "iron"));
            Assert.True(MaterialCatalog.Satisfies("iron", "Iron"));
            Assert.False(MaterialCatalog.Satisfies("iron", "copper"));
            // Non-material ids fall through to exact equality, so fuel and atlas
            // shards keep working and cannot be paid for with gold.
            Assert.True(MaterialCatalog.Satisfies("fuel", "fuel"));
            Assert.False(MaterialCatalog.Satisfies("atlasShard", "gold"));
        }

        [Fact]
        public void IsFamily_distinguishes_a_choice_from_a_fixed_ingredient()
        {
            Assert.True(MaterialCatalog.IsFamily("Metal"));
            Assert.True(MaterialCatalog.IsFamily("Wood"));
            Assert.True(MaterialCatalog.IsFamily("Wood/Metal"));
            Assert.False(MaterialCatalog.IsFamily("iron"));
            Assert.False(MaterialCatalog.IsFamily("atlasShard"));
            Assert.False(MaterialCatalog.IsFamily(null));
        }

        [Fact]
        public void The_legacy_defaults_are_what_the_server_used_to_hardcode()
        {
            // Not a guess: Deck.MaterialTypeId was "birch" and
            // ShipPartSalvagePolicy mapped "Metal" -> "iron".
            Assert.Equal("birch", MaterialCatalog.LegacyDefaultFor("Wood").Id);
            Assert.Equal("iron", MaterialCatalog.LegacyDefaultFor("Metal").Id);
            Assert.Equal("birch", MaterialCatalog.LegacyDefaultFor(null).Id);
        }

        private static double Mass(string id) => MaterialCatalog.Find(id)!.MassPerUnitKg;
    }
}
