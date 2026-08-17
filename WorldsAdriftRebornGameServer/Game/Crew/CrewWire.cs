using Bossa.Travellers.Crew;

namespace WorldsAdriftRebornGameServer.Game.Crew
{
    /// <summary>
    /// Turns the live ledger into the exact shape 6900 carries.
    ///
    /// One builder used by BOTH the first serve and every later push, because
    /// they must agree: a serve that disagreed with a push would give the crew UI
    /// one truth at checkout and another a second later, and the client rebuilds
    /// its whole panel from whatever arrived last.
    /// </summary>
    internal static class CrewWire
    {
        /// <summary>
        /// The crew as this player sees it.
        ///
        /// <c>CurrentCrewLeaderId</c> is empty for a crewless player rather than
        /// absent: the field is not optional on the wire, and a null would be a
        /// different thing to the client than "nobody leads me".
        /// </summary>
        internal static CrewMembershipStateData For(string uid)
        {
            Multiplayer.Crew.Crew? crew = CrewService.CrewOf(uid);

            Improbable.Collections.List<CrewSlot> members = new Improbable.Collections.List<CrewSlot>();
            if (crew != null)
            {
                foreach (string member in crew.Members)
                {
                    int? slot = crew.SlotOf(member);
                    members.Add(new CrewSlot(
                        member,
                        slot ?? -1,
                        // Active means "this seat is really taken", which for us
                        // is "they have chosen a seat" - a member with no seat is
                        // in the crew but not sitting in the UI's grid.
                        slot.HasValue,
                        CrewService.NameOf(member)));
                }
            }

            Improbable.Collections.Map<string, string> invites = new Improbable.Collections.Map<string, string>();
            foreach (string crewId in CrewService.InvitesFor(uid))
            {
                Multiplayer.Crew.Crew? from = CrewService.ById(crewId);
                if (from == null) continue;
                // key -> value is crew id -> who is asking, which is the only
                // thing the invited player needs in order to decide.
                invites[crewId] = CrewService.NameOf(from.LeaderUid);
            }

            return new CrewMembershipStateData(
                uid,
                CrewService.NameOf(uid),
                crew?.LeaderUid ?? string.Empty,
                members,
                invites,
                crew?.NumSlots ?? Multiplayer.Crew.CrewPolicy.DefaultSlots,
                // Beacon cooldown is not implemented; zero means "ready", which is
                // honest rather than pretending a cooldown we never tick.
                0L);
        }
    }
}
