using System.Security.Cryptography;
using System.Text;

namespace WorldsAdriftReborn.Storage.Policy
{
    /// <summary>
    /// Pure account rules. No I/O, no connection, no clock that it did not get as
    /// an argument - everything here is a function of what it is handed, so it
    /// can be unit tested without a database.
    ///
    /// No SqliteConnection may ever appear in this file. The split is what lets
    /// the rules that matter (what a username may be, what a stored hash looks
    /// like, how long a session lives) be read and tested in one place.
    /// </summary>
    public static class AccountPolicy
    {
        // ---- usernames -----------------------------------------------------

        /// <summary>Shortest username we accept.</summary>
        public const int MinUsernameLength = 3;

        /// <summary>
        /// Longest username we accept. Generous because the game's own form
        /// labels its first field "Email Address" - a player who types an email
        /// address into a box that says Email Address is behaving correctly, and
        /// refusing them would be our bug, not theirs.
        /// </summary>
        public const int MaxUsernameLength = 64;

        /// <summary>
        /// Shortest password we accept. Low on purpose: this is a five-friend
        /// server with no lockout and no rate limiting, so a rule that locks a
        /// friend out of their own game costs more than it buys. The brake on
        /// guessing is PBKDF2 at ~50-100 ms, not this number.
        /// </summary>
        public const int MinPasswordLength = 6;

        /// <summary>
        /// Longest password we accept. PBKDF2 cost does not grow with input
        /// length, so this only exists to stop a megabyte arriving in a POST.
        /// </summary>
        public const int MaxPasswordLength = 256;

        /// <summary>
        /// The lookup key for a username: trimmed and lowercased with the
        /// invariant culture.
        ///
        /// Invariant, not current-culture: ToLower under a Turkish locale maps
        /// 'I' to a dotless 'i', so the same typed name would key differently
        /// depending on the server's locale - and the server runs under Wine,
        /// where the locale is whatever the prefix says.
        ///
        /// The typed form is kept separately (see <see cref="TypedUsername"/>)
        /// so the player still sees the capitalisation they chose.
        /// </summary>
        public static string NormalizeUsername(string? username)
        {
            return TypedUsername(username).ToLowerInvariant();
        }

        /// <summary>
        /// The username as the player typed it, with surrounding whitespace
        /// removed. Trailing whitespace is invisible in the form and would
        /// otherwise make two visually identical names into two accounts.
        /// </summary>
        public static string TypedUsername(string? username)
        {
            return username == null ? string.Empty : username.Trim();
        }

        /// <summary>
        /// Whether a typed username is one we will store.
        ///
        /// The character set is deliberately narrow: the name is echoed back as
        /// screenName and rendered by the client, so anything that could be read
        /// as markup, a path segment or a control character is refused at the
        /// door rather than escaped at every use.
        /// </summary>
        public static bool IsUsableUsername(string? username)
        {
            string typed = TypedUsername(username);

            if (typed.Length < MinUsernameLength || typed.Length > MaxUsernameLength)
            {
                return false;
            }

            bool hasAlphanumeric = false;

            foreach (char c in typed)
            {
                bool alphanumeric =
                    (c >= 'a' && c <= 'z') ||
                    (c >= 'A' && c <= 'Z') ||
                    (c >= '0' && c <= '9');

                if (alphanumeric)
                {
                    hasAlphanumeric = true;
                    continue;
                }

                if (c != '.' && c != '_' && c != '-' && c != '@' && c != '+')
                {
                    return false;
                }
            }

            // A name of nothing but punctuation is technically storable and
            // useless to look at in a character list.
            return hasAlphanumeric;
        }

        /// <summary>
        /// Whether a password is one we will hash. Deliberately only a length
        /// rule: composition rules push people towards one predictable password
        /// they reuse, which is the exact thing the sign-up page warns against.
        /// </summary>
        public static bool IsUsablePassword(string? password)
        {
            return password != null
                && password.Length >= MinPasswordLength
                && password.Length <= MaxPasswordLength;
        }

        // ---- passwords -----------------------------------------------------

        /// <summary>The only hash algorithm this build produces.</summary>
        public const string HashAlgorithm = "pbkdf2";

        /// <summary>The PRF behind PBKDF2.</summary>
        public const string HashPrf = "sha256";

        /// <summary>
        /// PBKDF2 iterations. Chosen to match OWASP's PBKDF2-HMAC-SHA256 figure,
        /// which costs roughly 50-100 ms here. Stored in the hash string, so
        /// raising it later re-hashes on next login rather than invalidating
        /// every password.
        /// </summary>
        public const int HashIterations = 210_000;

        /// <summary>Salt length in bytes.</summary>
        public const int SaltBytes = 16;

        /// <summary>Derived key length in bytes.</summary>
        public const int HashBytes = 32;

        /// <summary>
        /// Hashes a password into the algorithm-agile string
        /// <c>pbkdf2$sha256$210000$&lt;salt&gt;$&lt;hash&gt;</c>.
        ///
        /// The algorithm, the PRF and the iteration count travel with the hash so
        /// that moving to Argon2 later is a migration - verify with whatever the
        /// stored string says, re-hash on the next successful login - rather than
        /// a flag day that logs everybody out.
        ///
        /// PBKDF2 rather than Argon2id, which is better, because Argon2 means a
        /// NuGet package with native code inside a Wine-hosted server, and that
        /// is the worse trade here. Rfc2898DeriveBytes.Pbkdf2,
        /// RandomNumberGenerator.GetBytes and CryptographicOperations
        /// .FixedTimeEquals are all in-box on net6.0.
        /// </summary>
        public static string HashPassword(string password)
        {
            if (string.IsNullOrEmpty(password))
            {
                throw new ArgumentException(
                    "Refusing to hash an empty password.", nameof(password));
            }

            byte[] salt = RandomNumberGenerator.GetBytes(SaltBytes);

            byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password),
                salt,
                HashIterations,
                HashAlgorithmName.SHA256,
                HashBytes);

            return string.Join(
                "$",
                HashAlgorithm,
                HashPrf,
                HashIterations.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Convert.ToBase64String(salt),
                Convert.ToBase64String(hash));
        }

        /// <summary>
        /// Checks a password against a stored hash string. Returns false for
        /// anything it cannot parse rather than throwing: a corrupt row must
        /// fail one login, not take the login server down.
        ///
        /// The comparison is <see cref="CryptographicOperations.FixedTimeEquals"/>
        /// so that a wrong password does not leak how many leading bytes were
        /// right. That matters less than it sounds over cleartext HTTP, but it
        /// costs one call.
        /// </summary>
        public static bool VerifyPassword(string? password, string? storedHash)
        {
            if (password == null || string.IsNullOrEmpty(storedHash))
            {
                return false;
            }

            string[] parts = storedHash.Split('$');

            if (parts.Length != 5)
            {
                return false;
            }

            if (!string.Equals(parts[0], HashAlgorithm, StringComparison.Ordinal))
            {
                return false;
            }

            if (!string.Equals(parts[1], HashPrf, StringComparison.Ordinal))
            {
                return false;
            }

            if (!int.TryParse(
                    parts[2],
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out int iterations)
                || iterations <= 0)
            {
                return false;
            }

            byte[] salt;
            byte[] expected;

            try
            {
                salt = Convert.FromBase64String(parts[3]);
                expected = Convert.FromBase64String(parts[4]);
            }
            catch (FormatException)
            {
                return false;
            }

            if (salt.Length == 0 || expected.Length == 0)
            {
                return false;
            }

            byte[] actual = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password),
                salt,
                iterations,
                HashAlgorithmName.SHA256,
                expected.Length);

            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }

        /// <summary>
        /// A hash of a fixed dummy password, for burning the same time on an
        /// unknown username as on a known one. Without it, "no such account"
        /// returns in microseconds while "wrong password" takes 100 ms, which
        /// tells anyone who cares exactly which usernames exist.
        /// </summary>
        public static string DummyHash { get; } = HashPassword("this password verifies nothing");

        // ---- sessions ------------------------------------------------------

        /// <summary>
        /// How long a session token stays valid, refreshed on every use.
        ///
        /// Long, and sliding, because of how the client fails: its 28-minute
        /// token refresh re-authenticates Steam-only, and a failed refresh calls
        /// an empty delegate and schedules nothing further. There is no path from
        /// "your token expired" back to a working session inside a running game -
        /// the player just finds that nothing works, with no message. So tokens
        /// must not expire inside a session, and the cheapest way to guarantee
        /// that is to make expiry something a playing player never reaches.
        /// </summary>
        public static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(30);

        /// <summary>Bytes of entropy in a session token.</summary>
        public const int SessionTokenBytes = 32;

        /// <summary>
        /// Mints a session token: 32 bytes from the CSPRNG, base64url, unpadded.
        ///
        /// Not a JWT and not HMAC-signed. The server holds the session table
        /// anyway, so a signature would buy nothing and would dress this up as
        /// something stronger than it is. It IS a bearer credential - whoever
        /// sees it is that account until it expires - so it is called a session
        /// token and kept out of logs.
        ///
        /// base64url because the value travels in a header and, in the pairing
        /// flow, through a config file a human copies: '+' and '/' survive
        /// neither reliably.
        /// </summary>
        public static string NewSessionToken()
        {
            byte[] bytes = RandomNumberGenerator.GetBytes(SessionTokenBytes);

            return Convert.ToBase64String(bytes)
                .Replace('+', '-')
                .Replace('/', '_')
                .TrimEnd('=');
        }

        /// <summary>
        /// When a session used at <paramref name="now"/> should expire. The
        /// sliding half of the expiry rule, kept pure so the repository has no
        /// arithmetic of its own.
        /// </summary>
        public static DateTimeOffset ExpiryFrom(DateTimeOffset now)
        {
            return now + SessionLifetime;
        }

        /// <summary>
        /// Whether a session with this expiry is spent. Exclusive at the boundary
        /// - a token is valid up to and including the instant it expires - so
        /// that a clock landing exactly on the stamp does not log somebody out.
        /// </summary>
        public static bool IsExpired(DateTimeOffset expiresAt, DateTimeOffset now)
        {
            return now > expiresAt;
        }

        // ---- steam ---------------------------------------------------------

        /// <summary>
        /// Whether a steamCredential.userKey is a real SteamID we may link an
        /// account to.
        ///
        /// A client with no Steam sends the literal string "steamUserId". Linking
        /// on that would make every Steam-less player one shared account, and the
        /// partial unique index would then reject the second of them - a sign-up
        /// that fails for the second friend and nobody else.
        /// </summary>
        public static bool IsRealSteamUserKey(string? userKey)
        {
            if (string.IsNullOrWhiteSpace(userKey))
            {
                return false;
            }

            string trimmed = userKey.Trim();

            if (trimmed.Length != 17)
            {
                return false;
            }

            foreach (char c in trimmed)
            {
                if (c < '0' || c > '9')
                {
                    return false;
                }
            }

            return true;
        }
    }
}
