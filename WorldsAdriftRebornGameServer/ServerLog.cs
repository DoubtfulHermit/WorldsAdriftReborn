using System;

namespace WorldsAdriftRebornGameServer
{
    /// <summary>
    /// Logging with a hot path that can be switched off.
    ///
    /// Every line the server prints is a synchronous write, and under systemd it
    /// travels stdout -> script(pty) -> bash -> journald -> disk. That happens on
    /// the SAME thread that polls ENet and relays component updates, so the cost
    /// is not "some disk I/O somewhere" - it is main-loop stall time.
    ///
    /// It was measured at up to 1,207 lines in a single second with two players
    /// connected, sustained at 500-800/s, because each incoming component update
    /// printed five lines and two clients publish bone data every tick. Position
    /// relays died for seconds at a time while animation kept flowing, which is
    /// exactly what "we stopped seeing each other move" looks like.
    ///
    /// It never showed up locally: there stdout is a terminal, with no pty relay
    /// and no journald behind it. The bug only appears once the server is behind
    /// systemd, which is why moving to the VPS "caused" it.
    ///
    /// So: per-packet lines go through <see cref="Trace"/> and are OFF unless
    /// WAREBORN_LOG_VERBOSE is set. Anything that happens once per connection,
    /// per entity or per error keeps using Console.WriteLine directly - those are
    /// rare enough to be free and are what you actually read when diagnosing.
    /// </summary>
    internal static class ServerLog
    {
        /// <summary>
        /// Per-packet logging. Off by default; set WAREBORN_LOG_VERBOSE=1 to
        /// restore the old firehose when you genuinely need to trace a packet.
        ///
        /// PARSED, not merely tested for presence. This read used to be
        /// <c>!string.IsNullOrEmpty(...)</c>, which turned the firehose ON for
        /// <c>WAREBORN_LOG_VERBOSE=0</c> - the one value an operator would reach
        /// for to be sure it was off. That is not a cosmetic bug: the docblock
        /// above measures this path at 500-1,200 synchronous stdout writes per
        /// second on the ENet thread, i.e. exactly the main-loop stall that reads
        /// as "we stopped seeing each other move". Every other opt-in flag in
        /// this server parses its value; this one now uses the same shared
        /// tokeniser ("1"/"true"/"yes") as the terrain and fauna switches.
        /// </summary>
        internal static readonly bool Verbose =
            Multiplayer.Islands.IslandTerrainInterestPolicy.EnabledFrom(
                Environment.GetEnvironmentVariable("WAREBORN_LOG_VERBOSE"));

        /// <summary>
        /// A line that can fire once per packet. Compiled out at runtime unless
        /// verbose logging is on.
        ///
        /// Callers must not do string concatenation at the call site - that cost
        /// is paid whether or not the line is printed. Use the overloads below,
        /// or guard with <see cref="Verbose"/>.
        /// </summary>
        internal static void Trace(string message)
        {
            if (Verbose)
            {
                Console.WriteLine(message);
            }
        }

        internal static void Trace(string a, object b)
        {
            if (Verbose)
            {
                Console.WriteLine(a + b);
            }
        }

        internal static void Trace(string a, object b, string c)
        {
            if (Verbose)
            {
                Console.WriteLine(a + b + c);
            }
        }

        internal static void Trace(string a, object b, string c, object d)
        {
            if (Verbose)
            {
                Console.WriteLine(a + b + c + d);
            }
        }

        internal static void Trace(string a, object b, string c, object d, string e, object f)
        {
            if (Verbose)
            {
                Console.WriteLine(a + b + c + d + e + f);
            }
        }

        /// <summary>
        /// Prints the logging mode once at startup so a quiet log is never
        /// mistaken for a dead server.
        /// </summary>
        internal static void AnnounceMode()
        {
            Console.WriteLine(Verbose
                ? "[info] verbose per-packet logging is ON (WAREBORN_LOG_VERBOSE). Expect hundreds of lines per second and a slower main loop."
                : "[info] per-packet logging is off. Set WAREBORN_LOG_VERBOSE=1 to trace individual packets.");
        }
    }
}
