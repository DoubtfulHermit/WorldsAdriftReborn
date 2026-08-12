using System.Linq;
using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    /// <summary>
    /// The three components that make the client's own ShipVisualizer enable, and
    /// the way they attach to the hull's seed. Like the rest of the ship work these
    /// are VALUES that fail silently: a missing id leaves the visualizer at
    /// m_Enabled = 0 with no error anywhere, so the set and the order are pinned
    /// here rather than trusted to a reviewer.
    /// </summary>
    public class ShipRecognitionTests
    {
        [Fact]
        public void The_recognition_set_is_exactly_the_ShipVisualizer_require_triple()
        {
            // 8062 ShipOwnersDeprecatedState, 8071 ShipPartCountState, 4349
            // ShipRegisteredCharactersState - the complete [Require] set of
            // ShipVisualizer (VERIFIED, ShipFrame_unityclient's [Require] map). Not
            // a subset, not a superset: two of three leaves it disabled, and a fourth
            // would enable a visualizer we have not reasoned about.
            Assert.Equal(new uint[] { 8062, 8071, 4349 },
                ShipRecognition.SeedComponents.ToArray());
        }

        [Fact]
        public void The_hull_seed_appends_recognition_after_the_existence_four_when_on()
        {
            uint[] seed = WorldEntities.HullSeedComponents(recogniseShip: true).ToArray();

            // Existence four first, recognition three last: the all-or-nothing batch
            // must never risk a recognition serialize before the geometry.
            Assert.Equal(new uint[] { 190602, 1209, 1099, 1130, 8062, 8071, 4349 }, seed);
        }

        [Fact]
        public void The_hull_seed_is_the_proven_four_when_recognition_is_off()
        {
            Assert.Equal(new uint[] { 190602, 1209, 1099, 1130 },
                WorldEntities.HullSeedComponents(recogniseShip: false).ToArray());
        }

        [Fact]
        public void The_part_counts_describe_a_one_helm_unowned_ship()
        {
            // The values 8071 carries: exactly one Helm, nothing else. Cosmetic (HUD
            // only), but pinned so a later edit cannot quietly claim the ship has a
            // core or a sail it does not.
            Assert.Equal(1u, ShipRecognition.AttachedHelmCount);
            Assert.Equal(0u, ShipRecognition.AttachedSailCount);
            Assert.Equal(0u, ShipRecognition.AttachedCoreCount);
            Assert.Equal(0u, ShipRecognition.AttachedRespawnerCount);
        }
    }
}
