namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// Bounded fallback proof that a server-issued teleport actually landed.
    ///
    /// The preferred proof is still 1073 lastExecutedRequest. Some shipped
    /// clients execute 190607 but never publish that optional ack field. Their
    /// owner-authored, unparented 190602 transform is still useful evidence,
    /// provided it agrees with an exact outstanding server destination. Two
    /// consecutive nearby samples are required so one corrupt/jump sample can
    /// never advance the peer's world-interest centre.
    /// </summary>
    public sealed class TeleportArrivalTracker
    {
        public const double ArrivalRadiusMetres = 12.0;
        public const int RequiredConsecutiveSamples = 2;

        private sealed class Pending
        {
            public Pending(int request, FixedPointPosition destination)
            {
                Request = request;
                Destination = destination;
            }

            public int Request { get; }
            public FixedPointPosition Destination { get; }
            public int ConsecutiveSamples { get; set; }
        }

        private readonly Dictionary<long, Pending> _pending = new();

        public void Arm(long entityId, int request, FixedPointPosition destination)
        {
            _pending[entityId] = new Pending(request, destination);
        }

        /// <summary>
        /// Observes one owner-authored transform. Returns the request proved by
        /// this sample, or null until the bounded proof is complete.
        /// </summary>
        public int? Observe(
            long entityId,
            FixedPointPosition position,
            bool? parentPresent)
        {
            if (!_pending.TryGetValue(entityId, out Pending? pending))
            {
                return null;
            }

            // A true parent edge means local coordinates. Null is accepted:
            // generated updates are sparse and omit the parent field on nearly
            // every frame; players are seeded parentless on this server.
            if (parentPresent == true || !IsNear(position, pending.Destination))
            {
                pending.ConsecutiveSamples = 0;
                return null;
            }

            pending.ConsecutiveSamples++;
            if (pending.ConsecutiveSamples < RequiredConsecutiveSamples)
            {
                return null;
            }

            _pending.Remove(entityId);
            return pending.Request;
        }

        public void Cancel(long entityId) => _pending.Remove(entityId);

        public int? Outstanding(long entityId)
        {
            return _pending.TryGetValue(entityId, out Pending? pending)
                ? pending.Request
                : null;
        }

        public static bool IsNear(FixedPointPosition position, FixedPointPosition destination)
        {
            double dx = position.MetresX - destination.MetresX;
            double dy = position.MetresY - destination.MetresY;
            double dz = position.MetresZ - destination.MetresZ;
            return dx * dx + dy * dy + dz * dz
                <= ArrivalRadiusMetres * ArrivalRadiusMetres;
        }
    }
}
