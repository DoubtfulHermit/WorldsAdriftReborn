using System;
using System.Collections.Generic;

namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// Turns the client's interaction lifecycle into a rising edge. Some zero-time
    /// Activate visualisers issue more than one completed interaction while the use
    /// key remains held. Stateful parts must consume only the first completion and
    /// re-arm from the client's release/default lifecycle, never from elapsed time.
    /// </summary>
    public sealed class InteractionActivationGate
    {
        private readonly HashSet<(long PlayerEntityId, long TargetEntityId)> _active = new();

        /// <summary>True exactly once between lifecycle releases for this player/target.</summary>
        public bool TryBegin(long playerEntityId, long targetEntityId)
        {
            if (playerEntityId <= 0) throw new ArgumentOutOfRangeException(nameof(playerEntityId));
            if (targetEntityId <= 0) throw new ArgumentOutOfRangeException(nameof(targetEntityId));
            return _active.Add((playerEntityId, targetEntityId));
        }

        /// <summary>
        /// Re-arms the named target. An invalid/default target is the client's
        /// end-of-interaction edge when it is no longer looking at an object, so it
        /// re-arms every target held by that player.
        /// </summary>
        public void Release(long playerEntityId, long targetEntityId)
        {
            if (playerEntityId <= 0) return;
            if (targetEntityId > 0)
            {
                _active.Remove((playerEntityId, targetEntityId));
                return;
            }
            ReleasePlayer(playerEntityId);
        }

        /// <summary>Disconnect cleanup: entity ids can be reused on a later session.</summary>
        public void ReleasePlayer(long playerEntityId) =>
            _active.RemoveWhere(edge => edge.PlayerEntityId == playerEntityId);
    }
}
