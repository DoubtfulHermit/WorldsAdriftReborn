using System.Diagnostics;

namespace RelayBot
{
    /// <summary>
    /// Real-wire two-peer ship acceptance. Both Bot instances use the production
    /// ENet channels, protobuf envelopes and generated component serializers.
    /// The server is expected to have one disposable persisted hull and helm.
    /// </summary>
    internal static class ShipAcceptance
    {
        public static int Run(Bot a, Bot b, int timeoutSeconds)
        {
            Console.WriteLine("[ship-wire] starting two-peer helm/domain acceptance");
            a.EnableShipAcceptance();
            b.EnableShipAcceptance();

            if (!WaitFor(() => Ready(a) && Ready(b), timeoutSeconds,
                    "both peers to receive the disposable hull, deck and helm"))
                return 1;

            if (a.ShipHullEntityId != b.ShipHullEntityId
                || a.HelmEntityId != b.HelmEntityId)
                return Fail("peers received different ship-domain entity ids");

            long hull = a.ShipHullEntityId;
            long helm = a.HelmEntityId;
            a.DrainRemovedEntities(); b.DrainRemovedEntities();
            a.DrainReaddedEntities(); b.DrainReaddedEntities();

            // Pilot A: aboard label, Man event, then real 1111 control input.
            a.SetAboard(true);
            long aMotion0 = a.HullMotionUpdates;
            long bMotion0 = b.HullMotionUpdates;
            long aWake0 = a.HelmWakeUpdates;
            long bWake0 = b.HelmWakeUpdates;
            a.ManHelm();
            if (!WaitFor(() => a.HullMotionUpdates > aMotion0
                    && b.HullMotionUpdates > bMotion0,
                    4, "the unchanged pre-control hull prime on both peers"))
                return 1;
            double startX = a.LastHullX;
            double startZ = a.LastHullZ;
            aMotion0 = a.HullMotionUpdates;
            bMotion0 = b.HullMotionUpdates;
            a.SetShipInput(0.8f, 0.45f);

            if (!WaitFor(() => a.HullMotionUpdates >= aMotion0 + 8
                    && b.HullMotionUpdates >= bMotion0 + 8
                    && a.HelmWakeUpdates >= aWake0 + 2
                    && b.HelmWakeUpdates >= bWake0 + 2
                    && Distance2D(startX, startZ, a.LastHullX, a.LastHullZ) > 0.25,
                    12, "pilot A motion and mounted-helm wake on both peers"))
                return 1;

            long observerFrame = b.LastHullTimestamp;
            if (!WaitFor(() => a.TryGetHullFrame(observerFrame, out _), 3,
                    "both peers to receive the same authoritative hull frame"))
                return 1;
            a.TryGetHullFrame(observerFrame, out var aPosition);
            b.TryGetHullFrame(observerFrame, out var bPosition);
            if (Distance3D(aPosition, bPosition) > 0.001)
                return Fail("peers decoded different poses for hull frame " + observerFrame);

            if (!WaitFor(() => b.RemoteAboardFrames >= 3, 5,
                    "observer B to receive A in the hull-relative frame"))
                return 1;

            // The exact production seam: A emits a few raw invalid/bias-zero
            // samples while still canonically aboard. B must never receive that
            // coordinate-frame detach.
            long invalidBefore = b.RemoteInvalidRelativeFrames;
            long aboardBefore = b.RemoteAboardFrames;
            a.InjectBriefContactSeam(3);
            if (!WaitFor(() => b.RemoteAboardFrames >= aboardBefore + 3, 4,
                    "post-seam aboard relay frames"))
                return 1;
            if (b.RemoteInvalidRelativeFrames != invalidBefore)
                return Fail("observer received a raw invalid relative frame during the aboard grace seam");

            // Clean handoff to B, followed by stale input from A. A valid B stream
            // must continue; the server's authority generation rejects A's old token.
            a.SetShipInput(0f, 0f);
            a.ReleaseHelm();
            a.SetAboard(false);
            Thread.Sleep(500);
            b.SetAboard(true);
            b.ManHelm();
            Thread.Sleep(350);
            long handoffMotion = a.HullMotionUpdates;
            b.SetShipInput(0.55f, -0.35f);
            a.SetShipInput(-1f, 1f); // deliberately stale old-pilot write
            if (!WaitFor(() => a.HullMotionUpdates >= handoffMotion + 8, 8,
                    "pilot B motion after handoff while stale A input is present"))
                return 1;

            // Stop and leave the coordinate frame before checkout testing.
            b.SetShipInput(0f, 0f);
            b.ReleaseHelm();
            b.SetAboard(false);
            a.MoveIslandLocal(208, 6.7, 4);
            b.MoveIslandLocal(208, 6.7, 4);
            Thread.Sleep(1800); // aboard grace + at least one interest reconcile
            a.DrainRemovedEntities(); b.DrainRemovedEntities();
            a.DrainReaddedEntities(); b.DrainReaddedEntities();

            // Only A walks beyond the 1 km unload radius. B stays beside the ship.
            a.MoveIslandLocal(1708, 6.7, 4);
            var removedA = new List<long>();
            if (!WaitFor(() =>
                {
                    removedA.AddRange(a.DrainRemovedEntities());
                    return removedA.Contains(hull) && removedA.Contains(helm);
                }, 12, "far peer A member-first/root-last ship removal"))
                return 1;

            long[] shipRemoved = removedA.Where(id => id == hull || id == helm).ToArray();
            if (Array.IndexOf(shipRemoved, hull) != shipRemoved.Length - 1)
                return Fail("far peer removal was not member-first/root-last: " + string.Join(",", shipRemoved));
            if (b.DrainRemovedEntities().Any(id => id == hull || id == helm))
                return Fail("near peer B lost the ship when only peer A left range");

            // A returns. Asset request then AddEntity must reconstruct root before
            // current members, while B remains untouched throughout.
            a.MoveIslandLocal(208, 6.7, 4);
            var readdedA = new List<long>();
            if (!WaitFor(() =>
                {
                    readdedA.AddRange(a.DrainReaddedEntities());
                    return readdedA.Contains(hull) && readdedA.Contains(helm);
                }, 15, "returning peer A root-first/member-last re-add"))
                return 1;

            long[] shipAdded = readdedA.Where(id => id == hull || id == helm).ToArray();
            if (shipAdded.Length < 2 || shipAdded[0] != hull)
                return Fail("returning peer re-add was not root-first: " + string.Join(",", shipAdded));

            Console.WriteLine("[ship-wire] PASS: two pilots, coherent hull/member frames, aboard seam hold,"
                + " independent checkout and legal re-entry over real ENet/generated component bytes.");
            return 0;
        }

        private static bool Ready(Bot bot) => bot.HasAuthority
            && bot.ShipHullEntityId > 0 && bot.HelmEntityId > 0;

        private static bool WaitFor(Func<bool> condition, int seconds, string description)
        {
            var clock = Stopwatch.StartNew();
            while (clock.Elapsed.TotalSeconds < seconds)
            {
                if (condition()) return true;
                Thread.Sleep(50);
            }
            Console.Error.WriteLine("[ship-wire] TIMEOUT waiting for " + description + ".");
            return false;
        }

        private static int Fail(string reason)
        {
            Console.Error.WriteLine("[ship-wire] FAIL: " + reason);
            return 1;
        }

        private static double Distance2D(double ax, double az, double bx, double bz)
        {
            double dx = bx - ax;
            double dz = bz - az;
            return Math.Sqrt((dx * dx) + (dz * dz));
        }

        private static double Distance3D((double X, double Y, double Z) a,
            (double X, double Y, double Z) b)
        {
            double dx = b.X - a.X;
            double dy = b.Y - a.Y;
            double dz = b.Z - a.Z;
            return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        }
    }
}
