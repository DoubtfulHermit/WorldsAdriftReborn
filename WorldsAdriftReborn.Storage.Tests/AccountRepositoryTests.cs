using WorldsAdriftReborn.Storage.Records;
using Xunit;

namespace WorldsAdriftReborn.Storage.Tests
{
    /// <summary>
    /// The repository is thin glue, so what these protect is mostly the four-rule
    /// login resolution that will sit on top of it: which account a request lands
    /// on, and when a Steam link may be made.
    /// </summary>
    public class AccountRepositoryTests
    {
        [PostgresFact]
        public void A_new_account_comes_back_with_everything_the_login_response_needs()
        {
            using TempDb db = new TempDb();

            AccountRecord account = db.Accounts.Create(
                "Timu", "Timu the Bold", "hunter22", null, TempDb.Now)!;

            Assert.True(account.AccountId > 0);
            Assert.Equal("timu", account.UsernameKey);
            Assert.Equal("Timu", account.Username);
            Assert.Equal("Timu the Bold", account.DisplayName);
            Assert.Equal(TempDb.Now, account.CreatedAt);
            Assert.Null(account.LastLoginAt);
            Assert.Null(account.SteamUserKey);
        }

        [PostgresFact]
        public void An_account_created_without_a_display_name_still_has_a_screen_name()
        {
            // Empty screenName is the QUIT dialog, so it falls back rather than
            // relying on every future call site to remember.
            using TempDb db = new TempDb();

            AccountRecord account = db.Accounts.Create(
                "Timu", "   ", "hunter22", null, TempDb.Now)!;

            Assert.Equal("Timu", account.DisplayName);
        }

        [PostgresFact]
        public void The_password_is_never_stored_as_the_player_typed_it()
        {
            using TempDb db = new TempDb();

            AccountRecord account = db.AnAccount();

            Assert.DoesNotContain("hunter22", account.PasswordHash);
            Assert.StartsWith("pbkdf2$sha256$210000$", account.PasswordHash);
        }

        [PostgresFact]
        public void A_taken_username_is_an_answer_the_signup_page_can_render_not_a_crash()
        {
            using TempDb db = new TempDb();

            Assert.NotNull(db.Accounts.Create("timu", "timu", "hunter22", null, TempDb.Now));
            Assert.Null(db.Accounts.Create("timu", "timu", "different", null, TempDb.Now));
            Assert.Null(db.Accounts.Create("TIMU", "timu", "different", null, TempDb.Now));
            Assert.Null(db.Accounts.Create("  Timu  ", "timu", "different", null, TempDb.Now));

            Assert.Equal(1, db.Accounts.Count());
        }

        [PostgresFact]
        public void An_unusable_username_or_password_is_a_fault_the_caller_should_have_caught()
        {
            using TempDb db = new TempDb();

            Assert.Throws<ArgumentException>(
                () => db.Accounts.Create("", "x", "hunter22", null, TempDb.Now));
            Assert.Throws<ArgumentException>(
                () => db.Accounts.Create("<script>", "x", "hunter22", null, TempDb.Now));
            Assert.Throws<ArgumentException>(
                () => db.Accounts.Create("timu", "x", "", null, TempDb.Now));
        }

        [PostgresFact]
        public void A_player_finds_their_account_however_they_type_their_name()
        {
            using TempDb db = new TempDb();

            AccountRecord created = db.AnAccount("Timu");

            Assert.Equal(created.AccountId, db.Accounts.FindByUsername("Timu")!.AccountId);
            Assert.Equal(created.AccountId, db.Accounts.FindByUsername("timu")!.AccountId);
            Assert.Equal(created.AccountId, db.Accounts.FindByUsername("TIMU")!.AccountId);
            Assert.Equal(created.AccountId, db.Accounts.FindByUsername(" Timu ")!.AccountId);
        }

        [PostgresFact]
        public void An_unknown_username_is_null_rather_than_an_exception()
        {
            using TempDb db = new TempDb();

            Assert.Null(db.Accounts.FindByUsername("nobody"));
            Assert.Null(db.Accounts.FindByUsername(null));
            Assert.Null(db.Accounts.FindByUsername(""));
            Assert.Null(db.Accounts.FindById(999999));
        }

        [PostgresFact]
        public void The_right_password_resolves_the_account_and_a_wrong_one_does_not()
        {
            using TempDb db = new TempDb();

            AccountRecord created = db.AnAccount("timu");

            Assert.Equal(created.AccountId, db.Accounts.Verify("timu", "hunter22")!.AccountId);
            Assert.Null(db.Accounts.Verify("timu", "hunter23"));
            Assert.Null(db.Accounts.Verify("timu", ""));
            Assert.Null(db.Accounts.Verify("timu", null));
        }

        [PostgresFact]
        public void An_unknown_username_and_a_wrong_password_are_the_same_answer()
        {
            // Both null, and both having actually run PBKDF2, so the response time
            // does not answer "does this username exist" for anyone who asks.
            using TempDb db = new TempDb();

            db.AnAccount("timu");

            Assert.Null(db.Accounts.Verify("nobody", "hunter22"));
            Assert.Null(db.Accounts.Verify("timu", "hunter23"));
        }

        [PostgresFact]
        public void A_password_login_can_adopt_the_requests_steam_id()
        {
            // Rule 1 of the four-rule resolution: the friend types a password once
            // and never sees the form again.
            using TempDb db = new TempDb();

            AccountRecord account = db.AnAccount("timu");

            Assert.True(db.Accounts.LinkSteamUserKey(account.AccountId, "76561198012345678"));
            Assert.Equal(
                account.AccountId,
                db.Accounts.FindBySteamUserKey("76561198012345678")!.AccountId);
        }

        [PostgresFact]
        public void The_placeholder_a_steamless_client_sends_is_never_linked()
        {
            // Linking on the literal "steamUserId" would make every Steam-less
            // player one shared account.
            using TempDb db = new TempDb();

            AccountRecord account = db.AnAccount("timu");

            Assert.False(db.Accounts.LinkSteamUserKey(account.AccountId, "steamUserId"));
            Assert.False(db.Accounts.LinkSteamUserKey(account.AccountId, null));
            Assert.False(db.Accounts.LinkSteamUserKey(account.AccountId, "1234"));

            Assert.Null(db.Accounts.FindById(account.AccountId)!.SteamUserKey);
            Assert.Null(db.Accounts.FindBySteamUserKey("steamUserId"));
        }

        [PostgresFact]
        public void A_second_account_on_a_shared_machine_signs_in_without_stealing_the_steam_link()
        {
            // Two friends, two usernames, one Steam client. The second link fails
            // and changes nothing; the second friend's login still works.
            using TempDb db = new TempDb();

            AccountRecord first = db.AnAccount("timu");
            AccountRecord second = db.AnAccount("friend");

            Assert.True(db.Accounts.LinkSteamUserKey(first.AccountId, "76561198012345678"));
            Assert.False(db.Accounts.LinkSteamUserKey(second.AccountId, "76561198012345678"));

            Assert.Equal(
                first.AccountId,
                db.Accounts.FindBySteamUserKey("76561198012345678")!.AccountId);
            Assert.Null(db.Accounts.FindById(second.AccountId)!.SteamUserKey);
            Assert.NotNull(db.Accounts.Verify("friend", "hunter22"));
        }

        [PostgresFact]
        public void An_account_created_with_a_placeholder_steam_id_stores_none()
        {
            using TempDb db = new TempDb();

            AccountRecord account = db.Accounts.Create(
                "timu", "timu", "hunter22", "steamUserId", TempDb.Now)!;

            Assert.Null(account.SteamUserKey);
        }

        [PostgresFact]
        public void A_successful_login_can_be_stamped()
        {
            using TempDb db = new TempDb();

            AccountRecord account = db.AnAccount();

            Assert.True(db.Accounts.TouchLastLogin(account.AccountId, TempDb.Now.AddHours(1)));
            Assert.Equal(
                TempDb.Now.AddHours(1),
                db.Accounts.FindById(account.AccountId)!.LastLoginAt);

            Assert.False(db.Accounts.TouchLastLogin(999999, TempDb.Now));
        }

        [PostgresFact]
        public void Timestamps_survive_the_round_trip_as_the_same_instant()
        {
            using TempDb db = new TempDb();

            AccountRecord account = db.AnAccount();
            AccountRecord read = db.Accounts.FindById(account.AccountId)!;

            Assert.Equal(TempDb.Now, read.CreatedAt);
            Assert.Equal(TimeSpan.Zero, read.CreatedAt.Offset);
        }
    }
}
