using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    public class HierarchyLifecyclePolicyTests
    {
        [Theory]
        [InlineData(false, true, true)]
        [InlineData(true, false, true)]
        [InlineData(true, true, false)]
        [InlineData(false, false, false)]
        public void Unavailable_or_disposing_hierarchy_never_runs_injected_lifecycle(
            bool behaviourPresent,
            bool readerPresent,
            bool active)
        {
            Assert.False(HierarchyLifecyclePolicy.MayRunInjectedLifecycle(
                behaviourPresent, readerPresent, active));
        }

        [Fact]
        public void Fully_injected_active_hierarchy_keeps_retail_lifecycle()
        {
            Assert.True(HierarchyLifecyclePolicy.MayRunInjectedLifecycle(
                behaviourPresent: true,
                transformStateReaderPresent: true,
                gameObjectActive: true));
        }
    }
}
