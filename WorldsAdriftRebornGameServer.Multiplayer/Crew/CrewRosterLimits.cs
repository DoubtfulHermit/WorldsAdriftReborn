using System;

namespace WorldsAdriftRebornGameServer.Multiplayer.Crew
{
    /// <summary>
    /// How many entries a crew may put in front of the retail Social Sheet.
    ///
    /// This is a RENDERING limit before it is a game rule, and getting it wrong
    /// does not degrade the panel - it destroys it. The client builds one flat
    /// list of members AND outstanding invites (CrewClient.GetCrewMembers: the
    /// member list, then every invite whose status is "new", appended to the same
    /// list), then draws it in CrewScreen.RefreshCrewSlots:
    ///
    ///     for (int i = 0; i &lt; crewSlots.Count; i++) {
    ///         if (crewSlots[i].IsLeader) { _crewLeader.Setup(...); continue; }
    ///         _crewUIObjects[num].Setup(...);
    ///         num++;
    ///     }
    ///
    /// <c>_crewUIObjects</c> is built once with exactly <c>MaxCrewSlots</c> = 5
    /// entries and the leader is drawn into its own widget, so the list may hold
    /// at most 5 NON-LEADER entries - a leader plus five others, six in total.
    /// The 6th non-leader entry indexes past the end and throws, and the throw
    /// lands in SocialCharacterSheet.TriggerAllianceExceptionHandler, which is
    /// shared with alliances: one crew with too many pending invites takes out
    /// the ENTIRE sheet, both tabs, with the same "Can't retrieve alliance or
    /// crew data" the localhost-URL bug produced.
    ///
    /// Retail's own value for this is NOT recoverable. <c>SocialConstants</c> in
    /// the client's GameDB schema does carry a MAX_INVITES field - evidence that
    /// a limit existed - but it has ZERO consumers anywhere in the decompiled
    /// client, its only defined row key is ALLIANCE rather than crew, and the row
    /// data lived in Bossa's GameDB: nothing in the shipped install carries a
    /// value for it. What IS recoverable is the arithmetic above, and that is
    /// what this type encodes. See docs/research/findings-social-api.md.
    ///
    /// Pure and engine-free so both servers can share one answer and it can be
    /// tested exhaustively.
    /// </summary>
    public static class CrewRosterLimits
    {
        /// <summary>
        /// <c>CrewScreen.MaxCrewSlots</c> - the number of non-leader bars the
        /// client pre-builds, and therefore the hard index bound.
        /// </summary>
        public const int ClientNonLeaderRows = 5;

        /// <summary>
        /// Everything the sheet can draw at once: the leader in its own widget
        /// plus <see cref="ClientNonLeaderRows"/> others. Members and outstanding
        /// invites share this budget because the client concatenates them.
        /// </summary>
        public const int ClientRenderableEntries = ClientNonLeaderRows + 1;

        /// <summary>
        /// The most live invites a crew may hold on top of the members it has.
        ///
        /// Two ceilings, and the tighter one wins:
        ///
        ///  - the GAME rule, <c>numSlots - memberCount</c>: never offer a seat
        ///    that does not exist. <c>numSlots</c> counts the leader, because the
        ///    ledger seats the leader as a member;
        ///  - the CLIENT rule, <see cref="ClientRenderableEntries"/>: never let
        ///    members plus invites exceed what the sheet can draw.
        ///
        /// The client rule is not redundant. <c>CrewPolicy.MaxSlots</c> is 8, so a
        /// crew configured above six seats would pass the game rule and still
        /// crash the panel.
        /// </summary>
        public static int MaxLiveInvites(int numSlots, int memberCount)
        {
            int seats = Math.Min(numSlots, ClientRenderableEntries);
            return Math.Max(0, seats - Math.Max(0, memberCount));
        }

        /// <summary>Whether one more invite fits.</summary>
        public static bool MayHoldAnotherInvite(int numSlots, int memberCount, int liveInvites)
        {
            return Math.Max(0, liveInvites) < MaxLiveInvites(numSlots, memberCount);
        }

        /// <summary>
        /// How many member rows may be put on the wire, whatever the store holds.
        ///
        /// The cap above stops a crew GROWING past what the sheet can draw, but it
        /// cannot fix rows written before it existed, or by a future path that
        /// forgets to ask. The sheet must not be destructible by data, so the
        /// emitters clamp too: a truncated roster is a bad panel, an over-long one
        /// is no panel at all.
        /// </summary>
        public static int EmittableMembers(int memberCount)
        {
            return Math.Min(Math.Max(0, memberCount), ClientRenderableEntries);
        }

        /// <summary>
        /// How many invite rows may be put on the wire, given the members already
        /// going out beside them. Invites yield first: they are transient, and a
        /// member who vanished from their own crew panel is the worse bug.
        /// </summary>
        public static int EmittableInvites(int memberCount, int liveInviteCount)
        {
            int used = EmittableMembers(memberCount);
            return Math.Max(0, Math.Min(Math.Max(0, liveInviteCount), ClientRenderableEntries - used));
        }
    }
}
