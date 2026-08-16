namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// One positively identified v1 asset-loaded response. Legacy clients send
    /// an opaque eight-byte response and therefore never produce this value.
    /// </summary>
    public readonly record struct AssetLoadedAck(
        ulong PeerId,
        string AssetType,
        string Name,
        string Context);

    /// <summary>
    /// Narrow process-local handoff from the ENet packet parser to runtime
    /// loaders. Subscribers are invoked only for protobuf responses carrying
    /// the Wareborn v1 marker; an arbitrary channel-0 packet is not enough.
    /// </summary>
    public static class AssetLoadedAckRouter
    {
        private static readonly object Gate = new();
        private static readonly Dictionary<Action<AssetLoadedAck>, int> Subscribers = new();

        /// <summary>
        /// Adds a callback. Repeated subscriptions of the same delegate are
        /// idempotent for delivery (one invocation) and reference-counted for
        /// disposal, which makes service restarts safe.
        /// </summary>
        public static IDisposable Subscribe(Action<AssetLoadedAck> callback)
        {
            ArgumentNullException.ThrowIfNull(callback);
            lock (Gate)
            {
                Subscribers.TryGetValue(callback, out int references);
                Subscribers[callback] = references + 1;
            }
            return new Subscription(callback);
        }

        /// <summary>Publishes one already validated correlated response.</summary>
        public static IReadOnlyList<Exception> Publish(AssetLoadedAck ack)
        {
            Action<AssetLoadedAck>[] snapshot;
            lock (Gate)
            {
                snapshot = Subscribers.Keys.ToArray();
            }
            List<Exception>? errors = null;
            foreach (Action<AssetLoadedAck> callback in snapshot)
            {
                try
                {
                    callback(ack);
                }
                catch (Exception error)
                {
                    (errors ??= new()).Add(error);
                }
            }
            return errors is null ? Array.Empty<Exception>() : errors;
        }

        private sealed class Subscription : IDisposable
        {
            private Action<AssetLoadedAck>? _callback;

            public Subscription(Action<AssetLoadedAck> callback) => _callback = callback;

            public void Dispose()
            {
                Action<AssetLoadedAck>? callback = Interlocked.Exchange(ref _callback, null);
                if (callback == null) return;
                lock (Gate)
                {
                    if (!Subscribers.TryGetValue(callback, out int references)) return;
                    if (references <= 1) Subscribers.Remove(callback);
                    else Subscribers[callback] = references - 1;
                }
            }
        }
    }
}
