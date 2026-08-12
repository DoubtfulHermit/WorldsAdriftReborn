using WorldsAdriftServer.Objects.CharacterSelection;
using WorldsAdriftServer.Persistence;
using Xunit;

namespace WorldsAdriftServer.Tests
{
    /// <summary>
    /// Every assertion here stands for a client-side line that misbehaves when
    /// the rule is broken; the comments in RosterPolicy name them.
    /// </summary>
    public class RosterPolicyTests
    {
        private const string Server = "community_server";

        private static CharacterCreationData Real(string name, string? uid = null)
        {
            return new CharacterCreationData(
                0,
                uid ?? Guid.NewGuid().ToString(),
                name,
                "serverName?",
                Server,
                new Dictionary<CharacterSlotType, ItemData>
                {
                    { CharacterSlotType.Head, new ItemData("1", "hat", default, 100f) },
                },
                new CharacterUniversalColors(),
                true,
                false,
                false);
        }

        private static CharacterCreationData Empty()
        {
            return new CharacterCreationData(
                0, Guid.NewGuid().ToString(), "New Traveller", "serverName?", Server,
                null, new CharacterUniversalColors(), true, false, false);
        }

        private static List<CharacterCreationData> Normalize(params CharacterCreationData[] stored)
        {
            return RosterPolicy.Normalize(stored, Server, Empty);
        }

        // ---- shape of the list the client receives -------------------------

        [Fact]
        public void Normalize_appends_exactly_one_empty_slot()
        {
            List<CharacterCreationData> roster = Normalize(Real("Billy"), Real("Silver"));

            Assert.Equal(3, roster.Count);
            Assert.True(RosterPolicy.IsEmptySlot(roster[2]));
            Assert.All(roster.Take(2), c => Assert.False(RosterPolicy.IsEmptySlot(c)));
        }

        [Fact]
        public void Normalize_collapses_duplicate_empty_slots()
        {
            List<CharacterCreationData> roster = Normalize(Real("Billy"), Empty(), Empty(), Empty());

            Assert.Equal(2, roster.Count);
            Assert.Single(roster.Where(RosterPolicy.IsEmptySlot));
        }

        [Fact]
        public void Normalize_keeps_the_empty_slot_last()
        {
            List<CharacterCreationData> roster = Normalize(Empty(), Real("Billy"));

            Assert.False(RosterPolicy.IsEmptySlot(roster[0]));
            Assert.True(RosterPolicy.IsEmptySlot(roster[1]));
        }

        [Fact]
        public void Normalize_reuses_a_stored_empty_slot_so_its_uid_survives_restarts()
        {
            CharacterCreationData empty = Empty();
            string uid = empty.characterUid;

            Assert.Equal(uid, Normalize(Real("Billy"), empty).Last().characterUid);
        }

        [Fact]
        public void Normalize_drops_the_empty_slot_when_the_roster_is_full()
        {
            CharacterCreationData[] full = Enumerable.Range(0, RosterPolicy.MaxCharacters)
                .Select(i => Real("Traveller " + i)).ToArray();

            List<CharacterCreationData> roster = Normalize(full);

            Assert.Equal(RosterPolicy.MaxCharacters, roster.Count);
            Assert.DoesNotContain(roster, RosterPolicy.IsEmptySlot);
        }

        [Fact]
        public void Normalize_numbers_ids_by_position()
        {
            List<CharacterCreationData> roster = Normalize(Real("Billy"), Real("Silver"));

            Assert.Equal(new[] { 0, 1, 2 }, roster.Select(c => c.Id).ToArray());
        }

        [Fact]
        public void Normalize_stamps_the_current_server_identifier()
        {
            CharacterCreationData stale = Real("Billy");
            stale.serverIdentifier = "an_old_deployment";

            Assert.All(RosterPolicy.Normalize(new[] { stale }, "a_new_deployment", Empty),
                c => Assert.Equal("a_new_deployment", c.serverIdentifier));
        }

        [Fact]
        public void Normalize_tolerates_an_empty_store()
        {
            List<CharacterCreationData> roster = RosterPolicy.Normalize(null, Server, Empty);

            Assert.Single(roster);
            Assert.True(RosterPolicy.IsEmptySlot(roster[0]));
        }

        // ---- uids ----------------------------------------------------------

        [Fact]
        public void The_upstream_placeholder_uid_is_rejected()
        {
            // Passes SocialHelper's Contains("-") check and then throws in
            // new Guid(uid) - the exact upstream bug.
            Assert.False(RosterPolicy.IsValidUid("valid-UIDs-have-at-least-one-"));
            Assert.False(RosterPolicy.IsValidUid("UID"));
            Assert.False(RosterPolicy.IsValidUid(""));
            Assert.False(RosterPolicy.IsValidUid(null));
            Assert.True(RosterPolicy.IsValidUid(Guid.NewGuid().ToString()));
        }

        [Fact]
        public void Normalize_replaces_unusable_uids_and_keeps_them_unique()
        {
            CharacterCreationData a = Real("Billy", "valid-UIDs-have-at-least-one-");
            CharacterCreationData b = Real("Silver", "valid-UIDs-have-at-least-one-");

            List<CharacterCreationData> roster = Normalize(a, b);

            Assert.All(roster, c => Assert.True(RosterPolicy.IsValidUid(c.characterUid)));
            Assert.Equal(roster.Count, roster.Select(c => c.characterUid).Distinct().Count());
        }

        [Fact]
        public void Normalize_preserves_a_uid_that_is_already_a_guid()
        {
            string uid = Guid.NewGuid().ToString();

            Assert.Equal(uid, Normalize(Real("Billy", uid))[0].characterUid);
        }

        // ---- saving --------------------------------------------------------

        [Fact]
        public void Upsert_updates_the_character_with_a_matching_uid_in_place()
        {
            CharacterCreationData stored = Real("Billy");
            CharacterCreationData incoming = Real("Billy Renamed", stored.characterUid);

            List<CharacterCreationData> roster = RosterPolicy.Upsert(
                new[] { stored }, incoming, Server, Empty);

            Assert.Equal(2, roster.Count); // the character plus the empty slot
            Assert.Equal("Billy Renamed", roster[0].Name);
        }

        [Fact]
        public void Upsert_adds_a_character_with_an_unknown_uid()
        {
            List<CharacterCreationData> roster = RosterPolicy.Upsert(
                new[] { Real("Billy") }, Real("Silver"), Server, Empty);

            Assert.Equal(new[] { "Billy", "Silver" },
                roster.Where(c => !RosterPolicy.IsEmptySlot(c)).Select(c => c.Name).ToArray());
        }

        [Fact]
        public void Upsert_mints_a_uid_when_the_client_sends_none()
        {
            CharacterCreationData incoming = Real("Nameless", "not-a-guid");

            List<CharacterCreationData> roster = RosterPolicy.Upsert(
                null, incoming, Server, Empty);

            Assert.True(RosterPolicy.IsValidUid(roster[0].characterUid));
        }

        [Fact]
        public void Upsert_does_not_blank_a_character_on_a_partial_save()
        {
            // The seenIntro flip at Enter World is a second save against an
            // existing character. If it ever arrives without cosmetics, the
            // character must survive it - a null Cosmetics turns the entry into
            // an empty slot, i.e. silently deletes the character.
            CharacterCreationData stored = Real("Billy");
            CharacterCreationData incoming = new CharacterCreationData(
                0, stored.characterUid, "Billy", "serverName?", Server,
                null, new CharacterUniversalColors(), true, true, false);

            List<CharacterCreationData> roster = RosterPolicy.Upsert(
                new[] { stored }, incoming, Server, Empty);

            Assert.False(RosterPolicy.IsEmptySlot(roster[0]));
            Assert.Equal("Billy", roster[0].Name);
            Assert.True(roster[0].seenIntro);
        }

        [Fact]
        public void Upsert_fills_the_empty_slot_when_the_client_reuses_its_uid()
        {
            CharacterCreationData empty = Empty();
            CharacterCreationData created = Real("Brand New", empty.characterUid);

            List<CharacterCreationData> roster = RosterPolicy.Upsert(
                new[] { empty }, created, Server, Empty);

            Assert.Equal(2, roster.Count);
            Assert.Equal("Brand New", roster[0].Name);
            Assert.True(RosterPolicy.IsEmptySlot(roster[1]));
            Assert.NotEqual(empty.characterUid, roster[1].characterUid);
        }

        [Fact]
        public void Upsert_refuses_to_exceed_the_character_limit()
        {
            CharacterCreationData[] full = Enumerable.Range(0, RosterPolicy.MaxCharacters)
                .Select(i => Real("Traveller " + i)).ToArray();

            List<CharacterCreationData> roster = RosterPolicy.Upsert(
                full, Real("One Too Many"), Server, Empty);

            Assert.Equal(RosterPolicy.MaxCharacters, roster.Count);
            Assert.DoesNotContain(roster, c => c.Name == "One Too Many");
        }

        [Fact]
        public void Upsert_survives_a_null_body()
        {
            List<CharacterCreationData> roster = RosterPolicy.Upsert(
                new[] { Real("Billy") }, null!, Server, Empty);

            Assert.Equal("Billy", roster[0].Name);
        }

        // ---- response shape -------------------------------------------------

        [Fact]
        public void The_response_carries_every_field_the_client_reads()
        {
            CharacterListResponse response = RosterPolicy.ToResponse(Normalize(Real("Billy")));

            // hasMainCharacter is read with JObject.GetValue and NREs if absent.
            Assert.True(response.hasMainCharacter);
            Assert.True(response.havenFinished);
            Assert.Equal(response.characterList.Count, response.unlockedSlots);
            Assert.NotEmpty(response.characterList);
        }
    }
}
