using WorldsAdriftReborn.Storage.Records;
using Xunit;

namespace WorldsAdriftReborn.Storage.Tests
{
    /// <summary>
    /// Schema v7 - social invites.
    ///
    /// The invariants worth testing here are the ones pushed into the DATABASE
    /// rather than into the code above it, because those are the ones that hold
    /// even when a future call site forgets to check: one live invite per pair,
    /// no self-invite, and the two closed vocabularies the client throws on.
    /// </summary>
    public class SocialInviteRepositoryTests
    {
        private static SocialInviteRecord AnInvite(
            Guid invitee,
            Guid? inviter,
            string target = "crew:1",
            string status = SocialInviteStatus.New,
            string? id = null)
        {
            return new SocialInviteRecord(
                id ?? "invite:" + Guid.NewGuid().ToString("N"),
                target,
                SocialTargetType.Crew,
                invitee,
                inviter,
                string.Empty,
                status,
                TempDb.Now,
                TempDb.Now);
        }

        private static (Guid Invitee, Guid Inviter) TwoCharacters(TempDb db)
        {
            AccountRecord account = db.AnAccount();
            CharacterRecord invitee = TempDb.ACharacter(account.AccountId, "Bones", 0);
            CharacterRecord inviter = TempDb.ACharacter(account.AccountId, "Billy", 1);
            db.Characters.Save(invitee);
            db.Characters.Save(inviter);
            return (invitee.CharacterUid, inviter.CharacterUid);
        }

        [PostgresFact]
        public void RoundTripsAnInvite()
        {
            using TempDb db = new TempDb();
            (Guid invitee, Guid inviter) = TwoCharacters(db);

            SocialInviteRecord invite = AnInvite(invitee, inviter);
            Assert.True(db.SocialInvites.TryInsert(invite));

            SocialInviteRecord? read = db.SocialInvites.Find(invite.InviteId);

            Assert.NotNull(read);
            Assert.Equal(invite.TargetId, read!.TargetId);
            Assert.Equal(invitee, read.CharacterUid);
            Assert.Equal(inviter, read.InviterUid);
            Assert.Equal(SocialInviteStatus.New, read.Status);
        }

        /// <summary>
        /// A null inviter is not missing data: it is how an APPLICATION is
        /// distinguished from an invite, by the client itself. It must survive the
        /// round trip as a null rather than being coerced to anything.
        /// </summary>
        [PostgresFact]
        public void AnApplicationKeepsItsNullInviter()
        {
            using TempDb db = new TempDb();
            (Guid applicant, _) = TwoCharacters(db);

            SocialInviteRecord application = AnInvite(applicant, inviter: null);
            db.SocialInvites.TryInsert(application);

            Assert.Null(db.SocialInvites.Find(application.InviteId)!.InviterUid);
        }

        /// <summary>
        /// Caught by the partial unique index rather than by a read-then-write, so
        /// two invites racing from two sessions cannot both pass a check.
        /// </summary>
        [PostgresFact]
        public void RefusesASecondLiveInviteForTheSamePair()
        {
            using TempDb db = new TempDb();
            (Guid invitee, Guid inviter) = TwoCharacters(db);

            Assert.True(db.SocialInvites.TryInsert(AnInvite(invitee, inviter)));
            Assert.False(db.SocialInvites.TryInsert(AnInvite(invitee, inviter)));
        }

        /// <summary>
        /// The index is PARTIAL on status = 'new' for a reason: rejecting an
        /// invitation must not blacklist that player from the crew forever.
        /// </summary>
        [PostgresFact]
        public void ARejectedInviteDoesNotBlockALaterOne()
        {
            using TempDb db = new TempDb();
            (Guid invitee, Guid inviter) = TwoCharacters(db);

            SocialInviteRecord first = AnInvite(invitee, inviter);
            db.SocialInvites.TryInsert(first);
            db.SocialInvites.Resolve(first.InviteId, SocialInviteStatus.Rejected, TempDb.Now);

            Assert.True(db.SocialInvites.TryInsert(AnInvite(invitee, inviter)));
        }

        /// <summary>
        /// The double-click guard: Resolve only moves an invite OUT of 'new', so
        /// a second accept cannot join the same player twice.
        /// </summary>
        [PostgresFact]
        public void AnInviteCanOnlyBeResolvedOnce()
        {
            using TempDb db = new TempDb();
            (Guid invitee, Guid inviter) = TwoCharacters(db);

            SocialInviteRecord invite = AnInvite(invitee, inviter);
            db.SocialInvites.TryInsert(invite);

            Assert.True(db.SocialInvites.Resolve(invite.InviteId, SocialInviteStatus.Accepted, TempDb.Now));
            Assert.False(db.SocialInvites.Resolve(invite.InviteId, SocialInviteStatus.Accepted, TempDb.Now));
        }

        [PostgresFact]
        public void ReadsFromBothEndsOfAnInvite()
        {
            using TempDb db = new TempDb();
            (Guid invitee, Guid inviter) = TwoCharacters(db);

            db.SocialInvites.TryInsert(AnInvite(invitee, inviter, target: "crew:1"));
            db.SocialInvites.TryInsert(AnInvite(invitee, inviter, target: "crew:2"));

            Assert.Equal(2, db.SocialInvites.ForCharacter(invitee).Count);
            Assert.Single(db.SocialInvites.ForTarget("crew:1"));
        }

        [PostgresFact]
        public void CancellingACrewsInvitesLeavesOtherCrewsAlone()
        {
            using TempDb db = new TempDb();
            (Guid invitee, Guid inviter) = TwoCharacters(db);

            db.SocialInvites.TryInsert(AnInvite(invitee, inviter, target: "crew:1"));
            db.SocialInvites.TryInsert(AnInvite(invitee, inviter, target: "crew:2"));

            Assert.Equal(1, db.SocialInvites.CancelAllForTarget("crew:1", TempDb.Now));

            Assert.Equal(SocialInviteStatus.Cancelled, db.SocialInvites.ForTarget("crew:1")[0].Status);
            Assert.Equal(SocialInviteStatus.New, db.SocialInvites.ForTarget("crew:2")[0].Status);
        }

        /// <summary>
        /// Deleting a character must not leave an invite pointing at nobody - the
        /// same CASCADE discipline as every other per-character table since v2.
        /// </summary>
        [PostgresFact]
        public void DeletingACharacterTakesTheirInvitesWithThem()
        {
            using TempDb db = new TempDb();
            (Guid invitee, Guid inviter) = TwoCharacters(db);

            db.SocialInvites.TryInsert(AnInvite(invitee, inviter));
            db.Characters.Delete(invitee);

            Assert.Empty(db.SocialInvites.ForCharacter(invitee));
        }

        [PostgresFact]
        public void DeletingTheINVITERAlsoRemovesTheOffer()
        {
            using TempDb db = new TempDb();
            (Guid invitee, Guid inviter) = TwoCharacters(db);

            db.SocialInvites.TryInsert(AnInvite(invitee, inviter));
            db.Characters.Delete(inviter);

            Assert.Empty(db.SocialInvites.ForCharacter(invitee));
        }

        /// <summary>
        /// Raw SQL, because these are constraints the repository would never let a
        /// caller violate - and that is exactly why they need testing directly. If
        /// every insert goes through code that already refuses the bad value, the
        /// CHECK could be missing and nothing would notice.
        /// </summary>
        [PostgresFact]
        public void TheDatabaseRefusesAStatusTheClientWouldThrowOn()
        {
            using TempDb db = new TempDb();
            (Guid invitee, Guid inviter) = TwoCharacters(db);

            Assert.ThrowsAny<Exception>(() => db.Execute(
                "INSERT INTO social_invites (invite_id, target_id, target_type, character_uid, "
                + "inviter_uid, message, status, created_at, updated_at) VALUES "
                + "('i', 'crew:1', 'crew_member', @invitee, @inviter, '', 'pondering', @at, @at);",
                ("invitee", invitee), ("inviter", inviter), ("at", TempDb.Now.UtcDateTime)));
        }

        [PostgresFact]
        public void TheDatabaseRefusesATargetTypeTheClientWouldThrowOn()
        {
            using TempDb db = new TempDb();
            (Guid invitee, Guid inviter) = TwoCharacters(db);

            Assert.ThrowsAny<Exception>(() => db.Execute(
                "INSERT INTO social_invites (invite_id, target_id, target_type, character_uid, "
                + "inviter_uid, message, status, created_at, updated_at) VALUES "
                + "('i', 'g:1', 'guild_member', @invitee, @inviter, '', 'new', @at, @at);",
                ("invitee", invitee), ("inviter", inviter), ("at", TempDb.Now.UtcDateTime)));
        }

        [PostgresFact]
        public void TheDatabaseRefusesASelfInvite()
        {
            using TempDb db = new TempDb();
            (Guid invitee, _) = TwoCharacters(db);

            Assert.ThrowsAny<Exception>(() => db.Execute(
                "INSERT INTO social_invites (invite_id, target_id, target_type, character_uid, "
                + "inviter_uid, message, status, created_at, updated_at) VALUES "
                + "('i', 'crew:1', 'crew_member', @uid, @uid, '', 'new', @at, @at);",
                ("uid", invitee), ("at", TempDb.Now.UtcDateTime)));
        }
    }
}
