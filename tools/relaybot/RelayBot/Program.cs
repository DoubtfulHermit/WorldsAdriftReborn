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
            bool rewritten1073 = false;
            bool shipAcceptance = false;
            bool requireFauna = false;
            (double X, double Y, double Z)? centre = null;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--host": host = args[++i]; break;
                    case "--port": port = int.Parse(args[++i]); break;
                    case "--minutes": minutes = double.Parse(args[++i], CultureInfo.InvariantCulture); break;
                    case "--csv": csvPath = args[++i]; break;
                    case "--setup-timeout": setupTimeoutSeconds = int.Parse(args[++i]); break;
                    // The server is running relay v2 (WAREBORN_RELAY_V2 != 0):
                    // relayed 1073 timestamps are SERVER-ISSUED synthetic stamps,
                    // so the bots verify their strict monotonicity instead of
                    // trying to match them to sends, and 1073 sends leave the
                    // delivery denominator. Without this flag every relayed 1073
                    // counts as unmatched and "delivered %" is meaningless.
                    case "--rewritten-1073": rewritten1073 = true; break;
                    case "--ship-acceptance": shipAcceptance = true; break;
                    // The soak is being used as the FAUNA gate: a run in which no
                    // bot was ever shown a creature, or shown creatures but no
                    // poses, must FAIL rather than print a confident FLAT. This
                    // repo has produced exactly that lie (2026-08-18: a soak
                    // reported FLAT at 3,866 creatures while carrying zero).
                    case "--require-fauna": requireFauna = true; break;
                    // Stand the bots somewhere other than the Haven spawn, in world
                    // METRES: "--centre 7376.4,25.2,6231.7". Needed to soak anything
                    // whose interest is island-scoped, because the spawn is 3.8 km
                    // from the nearest island. See the Bot constructor.
                    case "--centre":
                    {
                        string[] parts = args[++i].Split(',');
                        if (parts.Length != 3)
                        {
                            Console.Error.WriteLine("--centre wants X,Y,Z in world metres");
                            return 2;
                        }
                        centre = (
                            double.Parse(parts[0], CultureInfo.InvariantCulture),
                            double.Parse(parts[1], CultureInfo.InvariantCulture),
                            double.Parse(parts[2], CultureInfo.InvariantCulture));
                        break;
                    }
                    default:
                        Console.Error.WriteLine("unknown argument: " + args[i]);
                        Console.Error.WriteLine("usage: RelayBot [--host H] [--port P] [--minutes M] [--csv FILE] [--setup-timeout S] [--rewritten-1073] [--ship-acceptance] [--centre X,Y,Z] [--require-fauna]");
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
                new Bot(0, "botA", host, port, rewritten1073, metrics, sendLog, entityOwners, cts.Token, centre),
                new Bot(1, "botB", host, port, rewritten1073, metrics, sendLog, entityOwners, cts.Token, centre),
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

            if (shipAcceptance)
            {
                int result = ShipAcceptance.Run(bots[0], bots[1], setupTimeoutSeconds);
                cts.Cancel();
                foreach (Thread t in threads) t.Join(10000);
                return result;
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
                    // Say WHO and WHY, immediately. The v2 gate of 2026-08-09
                    // aborted here with nothing but "a bot dropped" - the
                    // FailureReason existed the whole time and was never
                    // printed, which cost the diagnosis a round trip.
                    foreach (Bot b in bots)
                    {
                        if (b.FailureReason != null)
                        {
                            Console.Error.WriteLine($"[soak] {b.Name} DIED at t={elapsed} s: {b.FailureReason}");
                        }
                        else if (b.Disconnected)
                        {
                            Console.Error.WriteLine($"[soak] {b.Name} was disconnected by the server/transport at t={elapsed} s.");
                        }
                    }
                    Console.Error.WriteLine("[soak] ending measurement early at " + elapsed + " s.");
                    soakSeconds = Math.Max(elapsed, 1);
                    break;
                }
                Thread.Sleep(250);
            }

            cts.Cancel();
            foreach (Thread t in threads) t.Join(10000);
            foreach (Bot b in bots)
            {
                b.FlushOpenGap();
                // A death is a first-class result, not a footnote: it goes into
                // the metrics so the summary can never again say "disconnects: 0"
                // about a run that ended BECAUSE a bot died.
                if (b.FailureReason != null)
                {
                    metrics.RecordBotDeath(b.Index, b.FailureReason);
                }
            }

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

            foreach ((int botIdx, string reason) in metrics.BotDeaths)
            {
                Console.WriteLine($"[soak] BOT DEATH: {names[botIdx]}: {reason}");
            }

            Metrics.Verdict verdict = metrics.ComputeVerdict(soakSeconds, bots.Length);
            Console.WriteLine();
            Console.WriteLine($"[soak] sends in window: {verdict.TotalSends}, matched relays: {verdict.Matched}"
                + $" ({(verdict.TotalSends > 0 ? 100.0 * verdict.Matched / verdict.TotalSends : 0):0.#}% delivered), unmatched: {verdict.Unmatched}"
                + $" (unmatched = relayed updates whose timestamp had no recorded send)"
                + (rewritten1073 ? $", heartbeats: {verdict.Heartbeats} (timestampless re-sends, not matchable by design)" : ""));
            Console.WriteLine($"[soak] staleness overall: p50 {verdict.OverallP50:0.##} ms, p95 {verdict.OverallP95:0.##} ms, max {verdict.OverallMax:0.##} ms");
            Console.WriteLine($"[soak] {verdict.Detail}");
            // ISLAND FAUNA, reported unconditionally so a run where the feature was
            // meant to be on and produced nothing says so, instead of looking like a
            // clean soak. Both numbers are zero on every world without fauna.
            long faunaCheckouts = bots.Sum(b => b.FaunaEntitiesAdded);
            long faunaPoses = bots.Sum(b => b.FaunaPoseUpdates);
            Console.WriteLine($"[soak] fauna: {faunaCheckouts} creature checkout(s),"
                + $" {faunaPoses} 190602 pose update(s) received.");

            // ARRIVAL COST, measured rather than derived. The plan costs fauna
            // arrival at 0.24 s per creature (two 120 ms SendInterval cadences);
            // this reports the real per-creature gap inside each arrival burst.
            // Gaps above 5 s separate bursts (different islands, or a reconcile
            // pause) and are excluded from the per-creature figure.
            var arrivalGapsMs = new List<double>();
            foreach (Bot b in bots)
            {
                long[] times = b.FaunaArrivalTimesNs;
                for (int i = 1; i < times.Length; i++)
                {
                    double gapMs = (times[i] - times[i - 1]) / 1e6;
                    if (gapMs < 5000) arrivalGapsMs.Add(gapMs);
                }
            }
            if (arrivalGapsMs.Count > 0)
            {
                arrivalGapsMs.Sort();
                double median = arrivalGapsMs[arrivalGapsMs.Count / 2];
                Console.WriteLine($"[soak] fauna arrival: median {median:0} ms between creatures"
                    + $" ({arrivalGapsMs.Count} gap(s), max {arrivalGapsMs[^1]:0} ms)"
                    + $" -> a {24}-creature island streams in over ~{median * 23 / 1000.0:0.0} s.");
            }
            Console.WriteLine($"[soak] gaps>1s: {verdict.GapCount}, disconnects: {verdict.DisconnectCount}, bot deaths: {verdict.BotDeaths}"
                + $", decode errors: {verdict.DecodeErrors}"
                + (rewritten1073 ? $", 1073 timeline violations: {verdict.TimelineViolations}" : ""));

            // A soak a bot did not survive proves nothing about staleness,
            // whatever the surviving seconds happened to measure - the 0-second
            // "FLAT" this replaced was exactly that lie. A transport disconnect
            // is the same lie with a different exit: either way a bot was gone
            // and the window is truncated.
            if (verdict.BotDeaths > 0 || verdict.DisconnectCount > 0)
            {
                Console.WriteLine("[soak] VERDICT: ABORTED (a bot "
                    + (verdict.BotDeaths > 0 ? "died" : "was disconnected")
                    + " mid-soak; the numbers cover only the seconds before it)");
                return 2;
            }

            if (verdict.Matched == 0)
            {
                Console.WriteLine("[soak] VERDICT: NO DATA");
                return 2;
            }

            // The fauna gate: required and absent is a FAILED measurement, not a
            // flat one. Checkouts without poses also fail - an AddEntity that is
            // never followed by a 190602 means the pose sender is broken.
            if (requireFauna && (faunaCheckouts == 0 || faunaPoses == 0))
            {
                Console.WriteLine("[soak] VERDICT: NO FAUNA (--require-fauna was set and the bots "
                    + (faunaCheckouts == 0 ? "were never shown a creature" : "saw creatures but no poses")
                    + "; the fauna path was NOT measured)");
                return 2;
            }

            // Decode errors or non-monotonic delivered stamps are wire-level
            // defects: the relay may be "flat" and still be sending garbage.
            if (verdict.DecodeErrors > 0 || verdict.TimelineViolations > 0)
            {
                Console.WriteLine($"[soak] VERDICT: DEFECTIVE ({verdict.DecodeErrors} decode error(s), "
                    + $"{verdict.TimelineViolations} timeline violation(s); staleness itself was {(verdict.Flat ? "flat" : "growing")})");
                return 1;
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
