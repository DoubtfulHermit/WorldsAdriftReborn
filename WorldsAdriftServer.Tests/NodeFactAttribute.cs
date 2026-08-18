using System.Diagnostics;
using Xunit;

namespace WorldsAdriftServer.Tests
{
    /// <summary>
    /// A [Fact] that needs a JavaScript engine on PATH, and skips - loudly, with
    /// a reason - when there is not one.
    ///
    /// Shaped exactly like <see cref="WorldsAdriftReborn.Storage.Tests.PostgresFactAttribute"/>
    /// and for the same reason: the tests that need no engine must always run,
    /// and a contributor without node should get a suite that says "skipped, here
    /// is what it would have checked" rather than one that teaches them red is
    /// normal.
    ///
    /// The probe runs once and is cached, so a missing engine costs one process
    /// start for the whole assembly.
    /// </summary>
    public sealed class NodeFactAttribute : FactAttribute
    {
        public NodeFactAttribute()
        {
            if (Unavailable != null)
            {
                Skip = Unavailable;
            }
        }

        private static readonly Lazy<string?> unavailable = new Lazy<string?>(Check);

        internal static string? Unavailable => unavailable.Value;

        /// <summary>The interpreter to run, once it is known to exist.</summary>
        internal const string Interpreter = "node";

        /// <summary>
        /// Runs <paramref name="scriptPath"/> and returns its standard output.
        /// Throws on a non-zero exit with the engine's own stderr attached,
        /// because "the mirror did not parse" is a failure a reader has to be
        /// able to diagnose from the test output alone.
        /// </summary>
        internal static string Run(string scriptPath, string argument)
        {
            ProcessStartInfo start = new ProcessStartInfo(Interpreter)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            start.ArgumentList.Add(scriptPath);
            start.ArgumentList.Add(argument);

            using Process process = Process.Start(start)
                ?? throw new InvalidOperationException("could not start " + Interpreter);
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    Interpreter + " exited " + process.ExitCode + ": " + error);
            }
            return output;
        }

        private static string? Check()
        {
            try
            {
                ProcessStartInfo start = new ProcessStartInfo(Interpreter)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                };
                start.ArgumentList.Add("--version");
                using Process? process = Process.Start(start);
                if (process == null) return "'" + Interpreter + "' could not be started.";
                process.StandardOutput.ReadToEnd();
                process.StandardError.ReadToEnd();
                process.WaitForExit();
                return process.ExitCode == 0 ? null
                    : "'" + Interpreter + " --version' exited " + process.ExitCode + ".";
            }
            catch (Exception e)
            {
                return "'" + Interpreter + "' is not on PATH, so the admin console's fauna "
                    + "movement mirror cannot be evaluated against the C# it mirrors. Install "
                    + "Node.js and re-run. (" + e.Message + ")";
            }
        }
    }
}
