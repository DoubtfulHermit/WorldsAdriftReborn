namespace WorldsAdriftRebornGameServer.Multiplayer.Wilderness
{
    /// <summary>Why a 1211 interact event did or did not become a graduation.</summary>
    public enum ShrineInteractOutcome
    {
        /// <summary>Not aimed at the shrine at all. The overwhelmingly common case; not a refusal.</summary>
        NotTheShrine,

        /// <summary>Aimed at the shrine, but the sender does not own the player entity it rode in on.</summary>
        NotOwner,

        /// <summary>Aimed at the shrine with a verb the shrine does not answer to.</summary>
        WrongVerb,

        /// <summary>Use the shrine.</summary>
        Use,
    }

    /// <summary>
    /// THE ROUTE from a 1211 <c>InteractWithObject</c> to the shrine, as a pure
    /// decision with a NAMED reason.
    ///
    /// WHY THIS IS NOT JUST AN <c>if</c> IN THE HANDLER ANY MORE. On 2026-08-18 a
    /// player stood on the shrine, held E, and nothing happened. The server was
    /// receiving 1211 at frame rate and <c>WildernessGraduationService</c> never
    /// ran - and the handler could not say why, because a 1211 that misses the
    /// shrine branch falls through in silence and a 1211 that carries no event at
    /// all returns even earlier. "The client sent nothing", "the client sent
    /// something we ignored" and "we matched and refused" are three completely
    /// different bugs and the log could not tell them apart.
    ///
    /// So the decision is a value now: the handler logs it, and every outcome that
    /// TOUCHED the shrine says which of the three gates rejected it. A refusal that
    /// logs nothing is itself a bug.
    ///
    /// The gates, unchanged in meaning:
    ///   * the TARGET's registration key, not its verb, is what selects the shrine -
    ///     so a helm interaction can never reach it and a shrine interaction never
    ///     falls through to the helm or mounted-part paths;
    ///   * the verb must be one the shrine advertises (<see cref="WildernessShrine.Accepts"/>);
    ///   * owner-only, because using the shrine moves the SENDER's character and can
    ///     write their crewmates' home rows.
    /// </summary>
    public static class ShrineInteractRouting
    {
        /// <summary>
        /// <paramref name="targetKey"/> is the registration key of the world entity
        /// the interaction names, or null when the target is not a world entity this
        /// server registered (a ship part, another player, a stale id).
        /// </summary>
        public static ShrineInteractOutcome Decide(bool ownsPlayer, int verb, string? targetKey)
        {
            if (targetKey != WildernessShrine.WorldEntityKey) return ShrineInteractOutcome.NotTheShrine;
            if (!ownsPlayer) return ShrineInteractOutcome.NotOwner;
            if (!WildernessShrine.Accepts(verb)) return ShrineInteractOutcome.WrongVerb;
            return ShrineInteractOutcome.Use;
        }

        /// <summary>
        /// Whether an outcome is one a player should be told about in the log. Every
        /// outcome that aimed AT the shrine is worth a line; the rest would be one
        /// line per interaction in the world.
        /// </summary>
        public static bool IsAboutTheShrine(ShrineInteractOutcome outcome) =>
            outcome != ShrineInteractOutcome.NotTheShrine;

        /// <summary>A one-line reason, for the server log.</summary>
        public static string Explain(ShrineInteractOutcome outcome) => outcome switch
        {
            ShrineInteractOutcome.Use => "using the shrine",
            ShrineInteractOutcome.NotOwner =>
                "REFUSED: the sender does not own the player entity this interaction rode in on",
            ShrineInteractOutcome.WrongVerb =>
                "REFUSED: the shrine does not answer to that verb (it advertises "
                + string.Join("/", WildernessShrine.Verbs) + ")",
            _ => "not the shrine",
        };
    }
}
