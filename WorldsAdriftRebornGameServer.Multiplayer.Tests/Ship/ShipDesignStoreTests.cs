using WorldsAdriftRebornGameServer.Multiplayer.Ship;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests.Ship
{
    /// <summary>
    /// The 1208 command-&gt;ack state machine, asserted purely. The game handler is thin
    /// glue over these; if the machine is right the client's Load/Edit/Save/Reset/Unload
    /// round-trip is right.
    /// </summary>
    public class ShipDesignStoreTests
    {
        [Fact]
        public void Fresh_player_has_exactly_the_starter_frame()
        {
            var d = new PlayerShipDesigns();
            Assert.Single(d.Slots);
            Assert.Equal(StarterFrame.Uuid, d.Slots[0].Uuid);
            Assert.Equal(39, d.Slots[0].Data.Length);
            Assert.False(d.Active);
            Assert.Equal(PlayerShipDesigns.NoSlot, d.LoadedSlot);
        }

        [Fact]
        public void Load_valid_slot_activates_and_copies_working_hull()
        {
            var d = new PlayerShipDesigns();
            Assert.True(d.LoadSlot(0));
            Assert.True(d.Active);
            Assert.Equal(0, d.LoadedSlot);
            Assert.False(d.Modified);
            Assert.NotNull(d.WorkingHull);
            Assert.Equal(d.Slots[0].Data, d.WorkingHull);
            // working hull is a COPY - editing it must not mutate the saved slot
            Assert.NotSame(d.Slots[0].Data, d.WorkingHull);
        }

        [Fact]
        public void Load_out_of_range_slot_is_a_noop_false()
        {
            var d = new PlayerShipDesigns();
            Assert.False(d.LoadSlot(5));
            Assert.False(d.LoadSlot(-1));
            Assert.False(d.Active);
            Assert.Null(d.WorkingHull);
        }

        [Fact]
        public void Edited_hull_is_applied_only_when_valid_and_active()
        {
            var d = new PlayerShipDesigns();

            // not active yet -> rejected
            Assert.False(d.ApplyEditedHull(ThreeCellHull()));

            d.LoadSlot(0);
            Assert.True(d.ApplyEditedHull(ThreeCellHull()));
            Assert.True(d.Modified);
            Assert.Equal(ThreeCellHull(), d.WorkingHull);
        }

        [Fact]
        public void Malformed_edited_hull_is_dropped_never_throws()
        {
            var d = new PlayerShipDesigns();
            d.LoadSlot(0);
            byte[] good = (byte[])d.WorkingHull!.Clone();

            // garbage, truncated, empty, null - all rejected, working hull unchanged
            Assert.False(d.ApplyEditedHull(new byte[] { 0xFF, 0xFF, 0xFF }));
            Assert.False(d.ApplyEditedHull(new byte[0]));
            Assert.False(d.ApplyEditedHull(null));
            Assert.Equal(good, d.WorkingHull);
        }

        [Fact]
        public void Save_persists_the_working_hull_into_the_slot()
        {
            var d = new PlayerShipDesigns();
            d.LoadSlot(0);
            d.ApplyEditedHull(ThreeCellHull());
            Assert.True(d.Modified);

            Assert.True(d.Save(0));
            Assert.False(d.Modified);
            Assert.Equal(ThreeCellHull(), d.Slots[0].Data);
        }

        [Fact]
        public void Save_without_active_or_bad_slot_is_false()
        {
            var d = new PlayerShipDesigns();
            Assert.False(d.Save(0));         // not active
            d.LoadSlot(0);
            Assert.False(d.Save(3));         // out of range
        }

        [Fact]
        public void Reset_reloads_the_saved_geometry()
        {
            var d = new PlayerShipDesigns();
            byte[] starter = StarterFrame.HullBlob();
            d.LoadSlot(0);
            d.ApplyEditedHull(ThreeCellHull());
            Assert.True(d.Modified);

            Assert.True(d.Reset(0));
            Assert.False(d.Modified);
            Assert.Equal(starter, d.WorkingHull);
        }

        [Fact]
        public void Unload_clears_the_editor()
        {
            var d = new PlayerShipDesigns();
            d.LoadSlot(0);
            d.StartEditing(1234);
            Assert.True(d.Unload());
            Assert.False(d.Active);
            Assert.Equal(PlayerShipDesigns.NoSlot, d.LoadedSlot);
            Assert.Null(d.WorkingHull);
            Assert.Equal(0, d.EditingShipyardEntityId);
        }

        [Fact]
        public void Start_and_stop_editing_track_the_shipyard()
        {
            var d = new PlayerShipDesigns();
            d.LoadSlot(0);
            d.StartEditing(999);
            Assert.Equal(999, d.EditingShipyardEntityId);
            d.StopEditing();
            Assert.Equal(0, d.EditingShipyardEntityId);
            // stopping editing does NOT unload the design
            Assert.True(d.Active);
        }

        [Fact]
        public void Rename_updates_the_slot_name()
        {
            var d = new PlayerShipDesigns();
            Assert.True(d.Rename(0, "My Skiff"));
            Assert.Equal("My Skiff", d.Slots[0].Name);
            Assert.False(d.Rename(9, "nope"));
        }

        [Fact]
        public void Rename_persists_into_what_the_1207_serve_reads()
        {
            // The 1208 rename handler calls Rename then re-serves 1207 from the store;
            // ComponentsSerializer's 1207 checkout also reads the same store's Slots. Both
            // must observe the new name, so a renamed frame keeps its name across a
            // re-push and a re-checkout (this was the reported "rename doesn't persist").
            long id = 0x5151_0002;
            ShipDesignStore.Forget(id);
            var d = ShipDesignStore.For(id);

            Assert.True(d.Rename(0, "Nimbus"));

            // what the 1207 serve branch iterates (designs.Slots[i].Name)
            var served = ShipDesignStore.For(id).Slots[0].Name;
            Assert.Equal("Nimbus", served);

            // a null name is stored as empty, never left as the old name or null
            Assert.True(d.Rename(0, null!));
            Assert.Equal("", ShipDesignStore.For(id).Slots[0].Name);

            ShipDesignStore.Forget(id);
        }

        [Fact]
        public void Rename_does_not_disturb_the_loaded_editor_state()
        {
            // Renaming a slot must not unload the working design or flip Active, so the
            // panel restored on Done still has EDIT enabled.
            var d = new PlayerShipDesigns();
            d.LoadSlot(0);
            d.ApplyEditedHull(ThreeCellHull());

            Assert.True(d.Rename(0, "Anvil"));
            Assert.True(d.Active);
            Assert.Equal(0, d.LoadedSlot);
            Assert.Equal(ThreeCellHull(), d.WorkingHull);
        }

        [Fact]
        public void Store_seeds_once_and_is_stable_per_entity()
        {
            long id = 0x5151_0001;
            ShipDesignStore.Forget(id);
            var a = ShipDesignStore.For(id);
            var b = ShipDesignStore.For(id);
            Assert.Same(a, b);
            ShipDesignStore.Forget(id);
        }

        // a valid 3-cell ShipPlan blob (from ShipPlanModel) used as an "edited" hull
        private static byte[] ThreeCellHull()
        {
            var plan = new ShipPlanModel(new[]
            {
                new ShipCellModel(0, 0, ShipSectionModel.MakeDefault(), ShipSectionModel.MakeDefault()),
                new ShipCellModel(1, 0, ShipSectionModel.MakeDefault(), null),
                new ShipCellModel(2, 0, ShipSectionModel.MakeDefault(), ShipSectionModel.MakeDefault()),
            });
            return plan.Encode();
        }
    }
}
