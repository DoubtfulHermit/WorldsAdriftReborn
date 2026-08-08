using WorldsAdriftReborn.Storage.Policy;
using WorldsAdriftReborn.Storage.Records;
using Xunit;

namespace WorldsAdriftReborn.Storage.Tests
{
    /// <summary>
    /// Sessions exist under one overriding constraint: the client cannot recover
    /// from a token that stops working. Its 28-minute refresh re-authenticates
    /// Steam-only, a failed refresh calls an empty delegate, and nothing further
    /// is ever scheduled. So most of these tests are about a token NOT expiring.
    /// </summary>
    public class SessionRepositoryTests
    {
        [PostgresFact]
        public void A_new_session_is_valid_for_thirty_days()
        {
            using TempDb db = new TempDb();

            AccountRecord account = db.AnAccount();
            SessionRecord session = db.Sessions.Issue(account.AccountId, TempDb.Now);

            Assert.Equal(account.AccountId, session.AccountId);
            Assert.Equal(TempDb.Now, session.IssuedAt);
            Assert.Equal(TempDb.Now.AddDays(30), session.ExpiresAt);
        }

        [PostgresFact]
        public void A_token_resolves_to_its_account()
        {
            using TempDb db = new TempDb();

            AccountRecord account = db.AnAccount();
            SessionRecord issued = db.Sessions.Issue(account.AccountId, TempDb.Now);

            SessionRecord resolved = db.Sessions.Resolve(issued.Token, TempDb.Now)!;

            Assert.Equal(account.AccountId, resolved.AccountId);
            Assert.Equal(issued.Token, resolved.Token);
        }

        [PostgresFact]
        public void Using_a_token_pushes_its_expiry_out_another_thirty_days()
        {
            // The sliding half of the rule. A player who logs in weekly must never
            // reach the expiry.
            using TempDb db = new TempDb();

            AccountRecord account = db.AnAccount();
            SessionRecord issued = db.Sessions.Issue(account.AccountId, TempDb.Now);

            DateTimeOffset later = TempDb.Now.AddDays(20);
            SessionRecord slid = db.Sessions.Resolve(issued.Token, later)!;

            Assert.Equal(later.AddDays(30), slid.ExpiresAt);
            Assert.Equal(later, slid.LastSeenAt);

            // And the slide was actually written, not just returned.
            Assert.Equal(later.AddDays(30), db.Sessions.Peek(issued.Token)!.ExpiresAt);
        }

        [PostgresFact]
        public void A_session_kept_in_use_never_expires()
        {
            // Ten months of weekly play, one token throughout. If this ever fails,
            // somebody playing gets a silently broken client with no message.
            using TempDb db = new TempDb();

            AccountRecord account = db.AnAccount();
            SessionRecord issued = db.Sessions.Issue(account.AccountId, TempDb.Now);

            DateTimeOffset when = TempDb.Now;

            for (int week = 0; week < 44; week++)
            {
                when = when.AddDays(7);
                Assert.NotNull(db.Sessions.Resolve(issued.Token, when));
            }

            Assert.NotNull(db.Sessions.Resolve(issued.Token, when));
        }

        [PostgresFact]
        public void A_token_is_still_good_at_the_exact_instant_it_expires()
        {
            using TempDb db = new TempDb();

            AccountRecord account = db.AnAccount();
            SessionRecord issued = db.Sessions.Issue(account.AccountId, TempDb.Now);

            Assert.NotNull(db.Sessions.Resolve(issued.Token, issued.ExpiresAt));
        }

        [PostgresFact]
        public void A_token_abandoned_for_a_month_stops_working()
        {
            using TempDb db = new TempDb();

            AccountRecord account = db.AnAccount();
            SessionRecord issued = db.Sessions.Issue(account.AccountId, TempDb.Now);

            Assert.Null(db.Sessions.Resolve(issued.Token, TempDb.Now.AddDays(31)));
        }

        [PostgresFact]
        public void An_expired_token_takes_its_row_with_it_so_the_table_does_not_only_grow()
        {
            // There is no scheduled job in this deployment.
            using TempDb db = new TempDb();

            AccountRecord account = db.AnAccount();
            SessionRecord issued = db.Sessions.Issue(account.AccountId, TempDb.Now);

            db.Sessions.Resolve(issued.Token, TempDb.Now.AddDays(31));

            Assert.Null(db.Sessions.Peek(issued.Token));
        }

        [PostgresFact]
        public void An_unknown_or_missing_token_is_null_rather_than_an_exception()
        {
            using TempDb db = new TempDb();

            Assert.Null(db.Sessions.Resolve("not-a-token", TempDb.Now));
            Assert.Null(db.Sessions.Resolve(null, TempDb.Now));
            Assert.Null(db.Sessions.Resolve("", TempDb.Now));
            Assert.Null(db.Sessions.Peek("not-a-token"));
        }

        [PostgresFact]
        public void Two_sessions_for_one_player_coexist()
        {
            // The same player may be signed in from the game and from the sign-up
            // page at once; the second login must not silently break the first.
            using TempDb db = new TempDb();

            AccountRecord account = db.AnAccount();

            SessionRecord game = db.Sessions.Issue(account.AccountId, TempDb.Now);
            SessionRecord web = db.Sessions.Issue(account.AccountId, TempDb.Now);

            Assert.NotEqual(game.Token, web.Token);
            Assert.NotNull(db.Sessions.Resolve(game.Token, TempDb.Now));
            Assert.NotNull(db.Sessions.Resolve(web.Token, TempDb.Now));
        }

        [PostgresFact]
        public void Every_issued_token_is_unique_and_full_length()
        {
            using TempDb db = new TempDb();

            AccountRecord account = db.AnAccount();
            HashSet<string> seen = new HashSet<string>();

            for (int i = 0; i < 50; i++)
            {
                SessionRecord session = db.Sessions.Issue(account.AccountId, TempDb.Now);

                Assert.True(seen.Add(session.Token));
                Assert.Equal(43, session.Token.Length);
            }
        }

        [PostgresFact]
        public void Signing_out_stops_the_token_working()
        {
            using TempDb db = new TempDb();

            AccountRecord account = db.AnAccount();
            SessionRecord session = db.Sessions.Issue(account.AccountId, TempDb.Now);

            Assert.True(db.Sessions.Revoke(session.Token));
            Assert.Null(db.Sessions.Resolve(session.Token, TempDb.Now));
            Assert.False(db.Sessions.Revoke(session.Token));
        }

        [PostgresFact]
        public void An_operator_can_sign_one_player_out_everywhere()
        {
            using TempDb db = new TempDb();

            AccountRecord mine = db.AnAccount("timu");
            AccountRecord theirs = db.AnAccount("friend");

            db.Sessions.Issue(mine.AccountId, TempDb.Now);
            db.Sessions.Issue(mine.AccountId, TempDb.Now);
            SessionRecord untouched = db.Sessions.Issue(theirs.AccountId, TempDb.Now);

            Assert.Equal(2, db.Sessions.RevokeAllFor(mine.AccountId));
            Assert.NotNull(db.Sessions.Resolve(untouched.Token, TempDb.Now));
        }

        [PostgresFact]
        public void Sweeping_expired_sessions_leaves_live_ones_alone()
        {
            using TempDb db = new TempDb();

            AccountRecord account = db.AnAccount();

            SessionRecord old = db.Sessions.Issue(account.AccountId, TempDb.Now);
            SessionRecord fresh = db.Sessions.Issue(account.AccountId, TempDb.Now.AddDays(20));

            Assert.Equal(1, db.Sessions.DeleteExpired(TempDb.Now.AddDays(31)));
            Assert.Null(db.Sessions.Peek(old.Token));
            Assert.NotNull(db.Sessions.Peek(fresh.Token));
        }

        [PostgresFact]
        public void The_lifetime_the_repository_uses_is_the_one_the_policy_states()
        {
            // The repository must not have arithmetic of its own; if it did, the
            // policy could be changed with no effect.
            using TempDb db = new TempDb();

            AccountRecord account = db.AnAccount();
            SessionRecord session = db.Sessions.Issue(account.AccountId, TempDb.Now);

            Assert.Equal(AccountPolicy.ExpiryFrom(TempDb.Now), session.ExpiresAt);
        }
    }
}
