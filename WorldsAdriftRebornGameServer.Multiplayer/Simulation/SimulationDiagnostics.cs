using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace WorldsAdriftRebornGameServer.Multiplayer.Simulation
{
    /// <summary>
    /// The snapshot as text. Pure and total: same snapshot, same lines, no clock, no
    /// console. Whoever calls this decides when - and it must not be every tick. The
    /// cadence rule lives in SimulationObserverPolicy so it can be tested rather than
    /// asserted in a comment.
    /// </summary>
    public static class SimulationDiagnostics
    {
        /// <summary>
        /// How many edge lines the explanation may print. A busy world can hold
        /// hundreds of edges and this goes to a log a human reads; the summary line
        /// already carries the true totals, so truncation costs nothing but noise.
        /// </summary>
        public const int MaxEdgeLines = 12;

        public static IReadOnlyList<string> Format(WorldSnapshot snapshot)
        {
            var lines = new List<string>(2 + snapshot.DomainCount)
            {
                "[sim] domains=" + snapshot.DomainCount
                    + " entities=" + snapshot.EntityCount
                    + " interactions=" + snapshot.InteractionCount,
            };

            foreach (DomainSnapshot domain in snapshot.Domains)
            {
                lines.Add("[sim] domain " + Pad(domain.Id.Value, 18)
                    + " kind=" + Pad(domain.Kind, 8)
                    + " members=" + Pad(domain.MemberCount.ToString(CultureInfo.InvariantCulture), 4)
                    + " pressure=" + Fixed(domain.InteractionPressure));
            }

            int printed = 0;
            foreach (InteractionSnapshot interaction in snapshot.Interactions)
            {
                if (!interaction.IsCrossDomain || interaction.Pressure <= 0) continue;
                if (printed == MaxEdgeLines)
                {
                    lines.Add("[sim]   ... more cross-domain edges not shown");
                    break;
                }
                // The "why" line. Section 23 wants a snapshot to be able to say
                // "ship:893 pressure increased because: control edge from player";
                // this is the honest version of that with today's evidence.
                lines.Add("[sim]   edge " + interaction.A.Value + " <-> " + interaction.B.Value
                    + " kind=" + interaction.Kind
                    + " strength=" + interaction.Strength
                    + " latency=" + interaction.LatencySensitivity
                    + " activity=" + interaction.Activity
                    + " pressure=" + Fixed(interaction.Pressure));
                printed++;
            }

            return lines;
        }

        /// <summary>The same lines as one newline-joined block, for a single log write.</summary>
        public static string FormatBlock(WorldSnapshot snapshot)
        {
            IReadOnlyList<string> lines = Format(snapshot);
            var b = new StringBuilder();
            for (int i = 0; i < lines.Count; i++)
            {
                if (i > 0) b.Append('\n');
                b.Append(lines[i]);
            }
            return b.ToString();
        }

        // InvariantCulture on purpose: a German-locale server printing "0,18" would
        // silently change the shape of a line an operator greps.
        private static string Fixed(double value) => value.ToString("0.00", CultureInfo.InvariantCulture);

        private static string Pad(string value, int width) =>
            (value ?? "").Length >= width ? value ?? "" : (value ?? "").PadRight(width);
    }
}
