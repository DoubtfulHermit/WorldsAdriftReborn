using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;

namespace RelayBot
{
    /// <summary>
    /// Orchestrator: two bots, one process, one monotonic clock. Connects both
    /// to the game server, waits until both hold authority (i.e. both finished
    /// the join handshake), soaks for the requested duration while they circle
    /// and relay, then writes the per-second staleness CSV and prints a
    /// FLAT/GROWING verdict.
    ///
    /// Exit codes: 0 = FLAT, 1 = GROWING, 2 = the soak never got off the ground.
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            string host = "127.0.0.1";
            int port = 7777;
            double minutes = 10;
            string csvPath = "relaybot-soak.csv";
            int setupTimeoutSeconds = 120;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--host": host = args[++i]; break;
                    case "--port": port = int.Parse(args[++i]); break;
                    case "--minutes": minutes = double.Parse(args[++i], CultureInfo.InvariantCulture); break;
                    case "--csv": csvPath = args[++i]; break;
                    case "--setup-timeout": setupTimeoutSeconds = int.Parse(args[++i]); break;
                    default:
                        Console.Error.WriteLine("unknown argument: " + args[i]);
                        Console.Error.WriteLine("usage: RelayBot [--host H] [--port P] [--minutes M] [--csv FILE] [--setup-timeout S]");
                        return 2;
                }
            }

            long soakSeconds = (long)Math.Round(minutes * 60);
            Console.WriteLine($"[soak] target {host}:{port}, {minutes} min measurement, CSV -> {csvPath}");

            if (Enet.Initialize() < 0)
            {
                Console.Error.WriteLine("[soak] ENet initialization failed.");
                return 2;
            }

            // Touch the game-assembly machinery before any bot connects, so the
            // one-shot ComponentDatabase scan cannot race the handshake.
            _ = GameComponents.TypeUpdate;
            System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(GameComponents).TypeHandle);

            var metrics = new Metrics();
            var sendLog = new ConcurrentDictionary<(int, uint, int), long>();
            var entityOwners = new ConcurrentDictionary<long, int>();
            using var cts = new CancellationTokenSource();

            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

            var bots = new[]
            {
                new Bot(0, "botA", host, port, metrics, sendLog, entityOwners, cts.Token),
                new Bot(1, "botB", host, port, metrics, sendLog, entityOwners, cts.Token),
            };

            var threads = new List<Thread>();
            threads.Add(StartBot(bots[0]));
            // Stagger the joins: A completes (or is deep into) its spawn sequence
            // before B connects, which is the sequence real players produce and
            // the one the mirror's park/flush path is built around.
            Thread.Sleep(3000);
            threads.Add(StartBot(bots[1]));

            // Wait for both bots to hold authority = both fully joined.
            var setupClock = Stopwatch.StartNew();
            while (!bots.All(b => b.HasAuthority))
            {
                if (bots.Any(b => b.FailureReason != null || b.Disconnected)
                    || setupClock.Elapsed.TotalSeconds > setupTimeoutSeconds
                    || cts.IsCancellationRequested)
                {
                    cts.Cancel();
                    foreach (Thread t in threads) t.Join(5000);
                    foreach (Bot b in bots)
                    {
                        Console.Error.WriteLine($"[soak] {b.FailureReason ?? (b.Disconnected ? "disconnected during setup" : b.HasAuthority ? "ok" : "never received authority")}");
                    }
                    Console.Error.WriteLine("[soak] setup failed - no measurement.");
                    return 2;
                }
                Thread.Sleep(100);
            }

            long startNs = (long)(Stopwatch.GetTimestamp() * (1e9 / Stopwatch.Frequency));
            metrics.StartMeasurement(startNs);
            Console.WriteLine($"[soak] both bots publishing (entities {bots[0].MyEntityId} and {bots[1].MyEntityId}); measuring for {soakSeconds} s.");

            long lastProgress = 0;
            while (!cts.IsCancellationRequested)
            {
                long elapsed = ((long)(Stopwatch.GetTimestamp() * (1e9 / Stopwatch.Frequency)) - startNs) / 1_000_000_000L;
                if (elapsed >= soakSeconds)
                {
                    break;
                }
                if (elapsed / 60 > lastProgress)
                {
                    lastProgress = elapsed / 60;
                    Console.WriteLine($"[soak] {elapsed}/{soakSeconds} s elapsed...");
                }
                if (bots.Any(b => b.Disconnected || b.FailureReason != null))
                {
                    Console.Error.WriteLine("[soak] a bot dropped mid-soak; ending measurement early at " + elapsed + " s.");
                    soakSeconds = Math.Max(elapsed, 1);
                    break;
                }
                Thread.Sleep(250);
            }

            cts.Cancel();
            foreach (Thread t in threads) t.Join(10000);
            foreach (Bot b in bots) b.FlushOpenGap();

            // ---- results ----
            var names = new[] { "botA", "botB" };
            using (var w = new StreamWriter(csvPath))
            {
                metrics.WriteCsv(w, soakSeconds, names);
            }
            Console.WriteLine($"[soak] wrote {csvPath}");

            foreach (Metrics.GapEvent gap in metrics.Gaps)
            {
                Console.WriteLine($"[soak] GAP: {names[gap.Bot]} heard nothing for {gap.GapSeconds:0.##} s ({gap.Stream}) around t={gap.AtSecond:0.#} s");
            }
            foreach (Metrics.DisconnectEvent d in metrics.Disconnects)
            {
                Console.WriteLine($"[soak] DISCONNECT: {names[d.Bot]} at t={d.AtSecond:0.#} s");
            }

            Metrics.Verdict verdict = metrics.ComputeVerdict(soakSeconds, bots.Length);
            Console.WriteLine();
            Console.WriteLine($"[soak] sends in window: {verdict.TotalSends}, matched relays: {verdict.Matched}"
                + $" ({(verdict.TotalSends > 0 ? 100.0 * verdict.Matched / verdict.TotalSends : 0):0.#}% delivered), unmatched: {verdict.Unmatched}"
                + $" (unmatched = relayed updates whose timestamp had no recorded send)");
            Console.WriteLine($"[soak] staleness overall: p50 {verdict.OverallP50:0.##} ms, p95 {verdict.OverallP95:0.##} ms, max {verdict.OverallMax:0.##} ms");
            Console.WriteLine($"[soak] {verdict.Detail}");
            Console.WriteLine($"[soak] gaps>1s: {verdict.GapCount}, disconnects: {verdict.DisconnectCount}");

            if (verdict.Matched == 0)
            {
                Console.WriteLine("[soak] VERDICT: NO DATA");
                return 2;
            }

            Console.WriteLine($"[soak] VERDICT: {(verdict.Flat ? "FLAT" : "GROWING")}"
                + $" (drift {verdict.DriftMs:+0.##;-0.##;0} ms, trend {verdict.SlopeMsOverSoak:+0.##;-0.##;0} ms over soak; threshold 20 ms)");
            return verdict.Flat ? 0 : 1;
        }

        private static Thread StartBot(Bot bot)
        {
            var thread = new Thread(bot.Run) { IsBackground = true, Name = bot.Name };
            thread.Start();
            return thread;
        }
    }
}
