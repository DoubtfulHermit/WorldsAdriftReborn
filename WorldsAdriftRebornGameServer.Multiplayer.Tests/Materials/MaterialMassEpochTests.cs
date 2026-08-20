using System;
using System.Collections.Generic;
using System.Linq;
using WorldsAdriftRebornGameServer.Multiplayer.Materials;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Materials
{
    /// <summary>
    /// Guards the epoch decision itself, not just the twenty-three numbers.
    ///
    /// MaterialCatalogTests already pins every shipped mass, so an arbitrary typo is
    /// caught there. What that test CANNOT do is explain a failure. These tests exist
    /// so that the specific, likely, well-sourced mistake - importing a row out of the
    /// calculator-era table, above all tungsten 0.74 - fails with a message that names
    /// the trap, instead of an anonymous "expected 0.70, got 0.74".
    /// </summary>
    public class MaterialMassEpochTests
    {
        [Fact]
        public void The_shipped_catalogue_is_one_internally_consistent_epoch()
        {
            IReadOnlyList<string> violations = MaterialMassEpoch.Violations();
            Assert.True(violations.Count == 0,
                "Shipped material masses are not a single epoch:"
                + Environment.NewLine + "  " + string.Join(Environment.NewLine + "  ", violations));
        }

        [Fact]
        public void Tungsten_is_the_final_era_070_and_NOT_the_cannon_back_solved_074()
        {
            // THE trap, called out by name. findings-material-mass.md section 2.5
            // back-solves 0.74 from the recovered cannon sheet. That is a genuine
            // result and it corrects the CALCULATOR era's 0.80. The final era, which
            // is the table we ship, independently says 0.70. Both are right in their
            // own epoch; only one of them belongs in this catalogue.
            Assert.Equal(0.70, MaterialCatalog.Find("tungsten")!.MassPerUnitKg, 3);
            Assert.Contains(0.74, MaterialMassEpoch.ForeignEpochMasses["tungsten"]);
            Assert.Contains(0.80, MaterialMassEpoch.ForeignEpochMasses["tungsten"]);
        }

        [Fact]
        public void Gold_outweighs_tungsten_which_is_the_final_eras_ordering_not_the_calculator_eras()
        {
            // The two epochs do not merely differ in value, they SWAP these two. This
            // is the cheapest single check that the whole table is the right one:
            // final era gold 0.73 > tungsten 0.70; calculator era tungsten 0.80 (or
            // 0.74) > gold 0.69.
            Assert.True(MaterialCatalog.Find("gold")!.MassPerUnitKg
                > MaterialCatalog.Find("tungsten")!.MassPerUnitKg,
                "final era: gold 0.73 > tungsten 0.70. If this inverted, the calculator-era "
                + "table has been imported.");
        }

        [Fact]
        public void No_shipped_retail_mass_is_a_known_value_from_another_epoch()
        {
            // Stated separately from Violations() so the intent survives a refactor of
            // that method.
            foreach (ShipMaterial material in MaterialCatalog.Materials.Where(m => m.IsRetail))
            {
                if (!MaterialMassEpoch.ForeignEpochMasses.TryGetValue(
                        material.Id, out IReadOnlyList<double>? foreign))
                {
                    continue;
                }

                foreach (double f in foreign)
                {
                    Assert.False(Math.Abs(material.MassPerUnitKg - f) <= MaterialMassEpoch.MassToleranceKg,
                        material.Id + " ships " + material.MassPerUnitKg.ToString("0.00")
                        + " kg/unit, which is its value in a DIFFERENT patch epoch. Never mix "
                        + "rows across epochs.");
                }
            }
        }

        [Fact]
        public void The_epoch_table_and_the_foreign_table_never_claim_the_same_value()
        {
            // A foreign value that coincides with the shipped one is not a signal, it
            // is a landmine: Violations() would report a correct row as an epoch
            // breach. Bronze is the real case - 0.42 in both epochs - and it is
            // correctly absent from ForeignEpochMasses.
            foreach ((string id, IReadOnlyList<double> foreign) in MaterialMassEpoch.ForeignEpochMasses)
            {
                double shipped = MaterialMassEpoch.FinalEraMassPerUnitKg[id];
                foreach (double f in foreign)
                {
                    Assert.False(Math.Abs(shipped - f) <= MaterialMassEpoch.MassToleranceKg,
                        id + ": " + f.ToString("0.00") + " is listed as a foreign-epoch value but "
                        + "equals the shipped one, so it can only produce false failures.");
                }
            }

            Assert.False(MaterialMassEpoch.ForeignEpochMasses.ContainsKey("bronze"),
                "bronze is 0.42 in both epochs, so it carries no foreign value");
        }

        [Fact]
        public void Every_retail_material_including_the_three_the_other_epoch_lacks_has_a_mass()
        {
            // The completeness argument for choosing this epoch: orthite, epilar and
            // eternium exist in NO community weight table. If they ever fall out of
            // the catalogue, the stated reason for the choice has gone with them.
            foreach (string id in new[] { "orthite", "epilar", "eternium" })
            {
                ShipMaterial? material = MaterialCatalog.Find(id);
                Assert.NotNull(material);
                Assert.False(material!.MassIsChosen,
                    id + " mass is RECOVERED from the final-era table, not chosen");
                Assert.Equal(MaterialMassEpoch.FinalEraMassPerUnitKg[id], material.MassPerUnitKg, 3);
                Assert.False(MaterialMassEpoch.ForeignEpochMasses.ContainsKey(id),
                    id + " appears in no other epoch's table, so it can have no foreign value");
            }
        }

        [Fact]
        public void Our_own_additions_are_exempt_because_no_epoch_has_an_opinion_on_them()
        {
            // cobalt and aurium are this project's, not retail's. They must stay OUT of
            // the epoch table, and their masses must stay honestly labelled CHOSEN.
            foreach (string id in new[] { "cobalt", "aurium" })
            {
                ShipMaterial material = MaterialCatalog.Find(id)!;
                Assert.False(material.IsRetail);
                Assert.True(material.MassIsChosen, id + " mass is ours and must say so");
                Assert.False(MaterialMassEpoch.FinalEraMassPerUnitKg.ContainsKey(id));
            }
        }

        [Fact]
        public void Changing_the_hull_mass_of_the_legacy_ship_is_a_deliberate_act()
        {
            // The one number the roadmap quotes, and the one every ship in the live
            // world is built from: legacy birch frame with iron fittings, two cells and
            // one deck. 4500 units x (0.20 x 0.8 + 0.39 x 0.2) = 1071 kg.
            //
            // This is the canary for the whole table. If a mass edit lands, this is the
            // test that says how much the live world just moved, and the roadmap figure
            // has to move with it.
            double mass = HullMassCalculator.HullMassKg(HullMaterials.Legacy, cellCount: 2, deckCount: 1);
            Assert.Equal(1071.0, mass, 1);

            // And it must stay clear of both agility clamps, or every legacy ship in
            // the world silently pins to a bound.
            double agility = HullMassCalculator.AgilityScale(mass);
            Assert.True(agility > HullMassCalculator.MinAgility, "legacy hull must not pin to min agility");
            Assert.True(agility < HullMassCalculator.MaxAgility, "legacy hull must not pin to max agility");
            Assert.Equal(0.864, agility, 3);
        }

        [Fact]
        public void The_atlas_lift_margin_survives_the_shipped_mass_table()
        {
            // The safety property the brief demands stay safe. EndOfTheWorld_Patch pins
            // AtlasMultiplier to 1f, so TotalLift is the flat 1,000,000 kg seed on 1258.
            // IsOverloaded is totalMass > TotalLift. The heaviest hull anyone can
            // plausibly build must stay orders of magnitude under it, or making lift
            // realistic (roadmap F2) stops being a free choice and becomes a live bug.
            // Read the REAL seed rather than restating it: a local copy of the number
            // was the original escape - it kept passing while the shipped seed was
            // mutated to 1e3.
            double seededTotalLiftKg = ShipLiftPolicy.SeededTotalLiftKg;

            double heaviestPerUnit = MaterialCatalog.Materials.Max(m => m.MassPerUnitKg);
            Assert.Equal(0.73, heaviestPerUnit, 3);       // gold

            // A 32-cell solid-gold hull with 32 decks is far beyond anything the world
            // holds and still does not come close.
            double absurd = HullMassCalculator.HullMassKg(
                new HullMaterials(woodId: null, woodQuality: 1, metalId: "gold", metalQuality: 1),
                cellCount: 32, deckCount: 32);
            Assert.True(absurd < seededTotalLiftKg / 10.0,
                "even an absurd hull (" + absurd.ToString("0") + " kg) must stay an order of "
                + "magnitude under the 1,000,000 kg lift seed");

            // The seed is not ours to retune here; F2 owns that decision.
            Assert.True(seededTotalLiftKg / HullMassCalculator.HullMassKg(HullMaterials.Legacy, 2, 1) > 900.0,
                "the legacy hull must keep at least a 900x lift margin");
        }
    }
}
