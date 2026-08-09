using System.Collections.Generic;
using System.Linq;
using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// A departed peer's every stored component reference must be surfaced for
    /// destruction exactly once - missing one leaks native memory, and the map's
    /// own shape (entityId -> componentId -> refId) is all the truth there is.
    /// </summary>
    public class ComponentRefCleanupTests
    {
        [Fact]
        public void All_refs_across_every_entity_and_component_are_returned()
        {
            Dictionary<long, Dictionary<uint, ulong>> slice = new()
            {
                { 11, new Dictionary<uint, ulong> { { 1081, 100 }, { 1086, 101 } } },
                { 22, new Dictionary<uint, ulong> { { 1109, 200 } } },
            };

            List<ulong> refs = ComponentRefCleanup.RefsForDepartedPeer(slice).ToList();

            Assert.Equal(new ulong[] { 100, 101, 200 }.OrderBy(x => x),
                         refs.OrderBy(x => x));
            Assert.Equal(3, refs.Count);
        }

        [Fact]
        public void A_null_slice_yields_nothing()
        {
            Assert.Empty(ComponentRefCleanup.RefsForDepartedPeer(null));
        }

        [Fact]
        public void An_empty_slice_yields_nothing()
        {
            Assert.Empty(ComponentRefCleanup.RefsForDepartedPeer(
                new Dictionary<long, Dictionary<uint, ulong>>()));
        }

        [Fact]
        public void An_entity_with_an_empty_component_map_contributes_nothing()
        {
            Dictionary<long, Dictionary<uint, ulong>> slice = new()
            {
                { 11, new Dictionary<uint, ulong>() },
                { 22, new Dictionary<uint, ulong> { { 1109, 200 } } },
            };

            Assert.Equal(new ulong[] { 200 },
                         ComponentRefCleanup.RefsForDepartedPeer(slice).ToArray());
        }

        [Fact]
        public void Distinct_refs_are_never_collapsed_even_if_ids_repeat_across_entities()
        {
            // The same componentId can appear under two entities with DIFFERENT
            // refs - both are real native references and both must be freed.
            Dictionary<long, Dictionary<uint, ulong>> slice = new()
            {
                { 11, new Dictionary<uint, ulong> { { 1081, 100 } } },
                { 22, new Dictionary<uint, ulong> { { 1081, 101 } } },
            };

            Assert.Equal(2, ComponentRefCleanup.RefsForDepartedPeer(slice).Count());
        }
    }
}
