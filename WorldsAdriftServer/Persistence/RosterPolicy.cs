using WorldsAdriftServer.Objects.CharacterSelection;

namespace WorldsAdriftServer.Persistence
{
    /// <summary>
    /// Pure roster rules. No I/O, no statics, no clock - everything here is a
    /// function of its arguments so it can be unit tested without a disk or a
    /// running server.
    ///
    /// The rules encode what the *client* requires of a character list. They are
    /// not arbitrary: each one corresponds to a specific line in the decompiled
    /// client that misbehaves when the rule is broken.
    /// </summary>
    internal static class RosterPolicy
    {
        /// <summary>
        /// How many real characters a player may own. The roster sent to the
        /// client is this plus one trailing empty slot.
        /// </summary>
        internal const int MaxCharacters = 5;

        /// <summary>
        /// The client decides a slot is empty by <c>Cosmetics == null</c>
        /// (LobbySystem.cs:509) - the uid is *not* what marks it. An entry with a
        /// non-null but empty Cosmetics dictionary is therefore read as a real
        /// character and then dereferenced, NREing in
        /// CharacterCustomisationVisualizer.cs:422.
        /// </summary>
        internal static bool IsEmptySlot(CharacterCreationData c)
        {
            return c == null || c.Cosmetics == null;
        }

        /// <summary>
        /// Bossa's social code parses the uid as a GUID
        /// (SocialHelper.cs:30-47): it checks for a '-' and then calls
        /// <c>new Guid(uid)</c>. The upstream placeholder
        /// "valid-UIDs-have-at-least-one-" passes the first check and throws on
        /// the second, so anything we persist must be a real GUID.
        /// </summary>
        internal static bool IsValidUid(string? uid)
        {
            return !string.IsNullOrWhiteSpace(uid) && Guid.TryParse(uid, out _);
        }

        /// <summary>
        /// Puts a stored roster into the exact shape the client expects:
        /// real characters in stable order, every uid a GUID, Id renumbered to
        /// the array index the client will overwrite it with anyway
        /// (LobbySystem.cs:492), the current serverIdentifier stamped on, and
        /// exactly one trailing empty slot while there is room to create.
        ///
        /// An existing trailing empty slot is reused rather than reminted so its
        /// uid stays stable across restarts.
        /// </summary>
        internal static List<CharacterCreationData> Normalize(
            IEnumerable<CharacterCreationData>? stored,
            string serverIdentifier,
            Func<CharacterCreationData> newEmptySlot)
        {
            List<CharacterCreationData> real = new List<CharacterCreationData>();
            CharacterCreationData? reusableEmpty = null;

            foreach (CharacterCreationData c in stored ?? Enumerable.Empty<CharacterCreationData>())
            {
                if (c == null)
                {
                    continue;
                }

                if (IsEmptySlot(c))
                {
                    // Keep the first empty slot we see so its uid survives a
                    // restart; drop any others (the client only ever shows one).
                    reusableEmpty ??= c;
                    continue;
                }

                if (real.Count < MaxCharacters)
                {
                    real.Add(c);
                }
            }

            List<CharacterCreationData> result = new List<CharacterCreationData>(real);

            if (real.Count < MaxCharacters)
            {
                result.Add(reusableEmpty ?? newEmptySlot());
            }

            for (int i = 0; i < result.Count; i++)
            {
                CharacterCreationData c = result[i];
                c.Id = i;
                c.serverIdentifier = serverIdentifier;

                if (!IsValidUid(c.characterUid))
                {
                    c.characterUid = Guid.NewGuid().ToString();
                }
            }

            return result;
        }

        /// <summary>
        /// Applies one incoming save to a stored roster and returns the new
        /// roster, normalized.
        ///
        /// Matching is by characterUid. A save whose uid is missing or not a GUID
        /// is treated as a brand new character and given one - the client re-reads
        /// the roster from our response (LobbySystem.cs:429-435), so a
        /// server-assigned uid propagates back without a round trip.
        ///
        /// A save that arrives with null Cosmetics against an existing *real*
        /// character is a partial update (the seenIntro flip at Enter World is the
        /// known case). It must not blank the character, so appearance fields are
        /// carried over from the stored copy.
        /// </summary>
        internal static List<CharacterCreationData> Upsert(
            IEnumerable<CharacterCreationData>? stored,
            CharacterCreationData incoming,
            string serverIdentifier,
            Func<CharacterCreationData> newEmptySlot)
        {
            if (incoming == null)
            {
                return Normalize(stored, serverIdentifier, newEmptySlot);
            }

            List<CharacterCreationData> list =
                (stored ?? Enumerable.Empty<CharacterCreationData>())
                .Where(c => c != null)
                .ToList();

            if (!IsValidUid(incoming.characterUid))
            {
                incoming.characterUid = Guid.NewGuid().ToString();
            }

            int at = list.FindIndex(c => c.characterUid == incoming.characterUid);

            if (at >= 0)
            {
                CharacterCreationData previous = list[at];

                if (incoming.Cosmetics == null && previous.Cosmetics != null)
                {
                    incoming.Cosmetics = previous.Cosmetics;
                    incoming.UniversalColors = previous.UniversalColors;
                }

                if (string.IsNullOrWhiteSpace(incoming.Name))
                {
                    incoming.Name = previous.Name;
                }

                list[at] = incoming;
            }
            else if (list.Count(c => !IsEmptySlot(c)) < MaxCharacters)
            {
                list.Add(incoming);
            }

            return Normalize(list, serverIdentifier, newEmptySlot);
        }

        /// <summary>
        /// Builds the response body. <c>hasMainCharacter</c> is read with
        /// JObject.GetValue (LobbySystem.cs:515) and omitting it NREs, so it is
        /// always written even though it is always true here.
        /// </summary>
        internal static CharacterListResponse ToResponse(List<CharacterCreationData> roster)
        {
            CharacterListResponse response = new CharacterListResponse(roster);

            // The last entry is the create-a-character slot, so unlocked slots
            // must include it (this matches the upstream behaviour).
            response.unlockedSlots = roster.Count;
            response.hasMainCharacter = true;
            response.havenFinished = true;

            return response;
        }
    }
}
