using WorldsAdriftRebornGameServer.Multiplayer;
using Xunit;

namespace WorldsAdriftRebornGameServer.Multiplayer.Tests
{
    public sealed class AssetLoadedAckRouterTests
    {
        [Fact]
        public void Publish_delivers_all_exact_fields()
        {
            AssetLoadedAck? observed = null;
            using IDisposable subscription = AssetLoadedAckRouter.Subscribe(ack => observed = ack);
            AssetLoadedAck expected = new(42, "UnityPrefab", "MentalFacility", "unityclient");

            AssetLoadedAckRouter.Publish(expected);

            Assert.Equal(expected, observed);
        }

        [Fact]
        public void Duplicate_delegate_is_delivered_once_and_reference_counted()
        {
            int calls = 0;
            void Observe(AssetLoadedAck _) => calls++;
            IDisposable first = AssetLoadedAckRouter.Subscribe(Observe);
            IDisposable second = AssetLoadedAckRouter.Subscribe(Observe);

            AssetLoadedAckRouter.Publish(new(1, "type", "name", "context"));
            Assert.Equal(1, calls);

            first.Dispose();
            AssetLoadedAckRouter.Publish(new(1, "type", "name", "context"));
            Assert.Equal(2, calls);

            second.Dispose();
            AssetLoadedAckRouter.Publish(new(1, "type", "name", "context"));
            Assert.Equal(2, calls);
        }

        [Fact]
        public void Disposed_subscription_receives_nothing()
        {
            int calls = 0;
            IDisposable subscription = AssetLoadedAckRouter.Subscribe(_ => calls++);
            subscription.Dispose();

            AssetLoadedAckRouter.Publish(new(1, "type", "name", "context"));

            Assert.Equal(0, calls);
        }

        [Fact]
        public void Failing_subscriber_is_reported_without_blocking_the_rest()
        {
            bool secondRan = false;
            using IDisposable first = AssetLoadedAckRouter.Subscribe(
                _ => throw new InvalidOperationException("broken subscriber"));
            using IDisposable second = AssetLoadedAckRouter.Subscribe(_ => secondRan = true);

            IReadOnlyList<Exception> errors = AssetLoadedAckRouter.Publish(
                new(1, "type", "name", "context"));

            Assert.True(secondRan);
            InvalidOperationException error = Assert.IsType<InvalidOperationException>(
                Assert.Single(errors));
            Assert.Equal("broken subscriber", error.Message);
        }
    }
}
