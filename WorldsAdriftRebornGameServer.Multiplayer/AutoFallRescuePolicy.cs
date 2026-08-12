namespace WorldsAdriftRebornGameServer.Multiplayer
{
    /// <summary>
    /// WHETHER the server automatically yanks a player home when they drop below
    /// the island, and - when it does not - how deep a fall it still catches.
    ///
    /// WHY THIS EXISTS. The automatic fall-rescue was written for a world with
    /// exactly one thing to stand on, where "below the island" could only mean
    /// "fell off it". Once ships fly, that stops being true: a player flying,
    /// boarding, or descending on a ship is below the island ON PURPOSE, and the
    /// old rescue would snatch them straight back to Haven mid-flight. So
    /// recovery becomes a MANUAL button - F10, client side, which places the
    /// local player at Haven and calls the game's own respawn path - and the
    /// automatic yank is OFF by default.
    ///
    /// "Off" does NOT mean "unrecoverable", though: this server still writes no
    /// fall damage and no lower world bound, so a genuine fall through the bottom
    /// of the world accelerates forever. The deep safety net
    /// (<see cref="FallPolicy.DeepFloorY"/>, -2000 m, ~1.17 km below the deepest
    /// authored island) stays armed even when the ordinary rescue is off, so a
    /// true fall-out-of-the-world is still caught while a ship flying just under
    /// an island is left alone.
    ///
    /// Pure: it reads one environment variable and turns it into a bool and a
    /// floor, so the mapping is unit-tested rather than discovered by flying off
    /// an island. The environment read itself lives in
    /// <see cref="EnabledFromEnvironment"/> and is the only impure edge.
    /// </summary>
    public static class AutoFallRescuePolicy
    {
        /// <summary>
        /// The environment variable that turns the LEGACY automatic rescue back
        /// on. Absent or falsey (the default now that F10 exists) means the
        /// ordinary rescue is off and only the deep net is armed.
        /// </summary>
        public const string EnvVar = "WAREBORN_AUTO_FALL_RESCUE";

        /// <summary>
        /// Whether a value read from <see cref="EnvVar"/> means "on". Accepts the
        /// obvious truthy spellings a human types into a shell - <c>1</c>,
        /// <c>true</c>, <c>yes</c>, <c>on</c> - case- and whitespace-insensitive.
        /// Anything else, including null and empty, is OFF: the safe default is
        /// the one that does not surprise a flying player.
        /// </summary>
        public static bool ParseEnabled(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            switch (value.Trim().ToLowerInvariant())
            {
                case "1":
                case "true":
                case "yes":
                case "on":
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Reads <see cref="EnvVar"/> from the real environment. The single impure
        /// call; everything the tests touch goes through <see cref="ParseEnabled"/>.
        /// </summary>
        public static bool EnabledFromEnvironment()
        {
            return ParseEnabled(System.Environment.GetEnvironmentVariable(EnvVar));
        }

        /// <summary>
        /// The world-y floor the <see cref="FallWatch"/> should trigger on, given
        /// the mode: the ordinary island floor when the automatic rescue is on,
        /// the deep net when it is off. This is the whole behavioural difference
        /// between the two modes - the machinery around it is identical.
        /// </summary>
        public static long FloorYFor(bool autoRescueEnabled)
        {
            return autoRescueEnabled ? FallPolicy.FloorY : FallPolicy.DeepFloorY;
        }

        /// <summary>
        /// One line for the startup banner, so "why did / didn't I get yanked
        /// home" is answerable from the same log without reading this file.
        /// </summary>
        public static string DescribeMode(bool autoRescueEnabled)
        {
            if (autoRescueEnabled)
            {
                return "automatic fall-rescue is ON (" + EnvVar + " set): anybody below y = "
                    + FallPolicy.FloorMetres.ToString("0.#") + " m is teleported home. Legacy "
                    + "behaviour; a ship flown below the island will be yanked back to spawn.";
            }

            return "automatic fall-rescue is OFF (default; press F10 in-game to recover). Only a "
                + "fall through the world - below y = " + FallPolicy.DeepFloorMetres.ToString("0.#")
                + " m, ~1.2 km under the deepest island - is still caught automatically, so a ship "
                + "flying below an island is left alone. Set " + EnvVar + "=1 to restore the old yank.";
        }
    }
}
