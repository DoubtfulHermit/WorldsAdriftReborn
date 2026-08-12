using WorldsAdriftReborn.Storage.Policy;

namespace WorldsAdriftServer.Admin
{
    /// <summary>
    /// The one place the admin credential and the live session set are held.
    /// Thin glue: it reads <see cref="AdminAuthPolicy.ConfigVariable"/> once,
    /// turns whatever the operator installed into a (username, stored hash) pair,
    /// and owns the process-wide <see cref="AdminSessions"/>. Every actual
    /// decision - how to split the value, whether a credential is already a hash,
    /// how to verify - belongs to <see cref="AdminAuthPolicy"/>.
    ///
    /// If the env var is unset the panel is OFF: <see cref="IsConfigured"/> is
    /// false and the handler refuses every /admin route. That is deliberate - a
    /// panel with no configured credential must not fall open, and an operator
    /// who has not installed one has not asked for the panel.
    /// </summary>
    internal static class AdminConfig
    {
        private static readonly object gate = new object();
        private static bool initialized;
        private static string? username;
        private static string? storedHash;

        /// <summary>Process-wide live admin sessions.</summary>
        internal static AdminSessions Sessions { get; } = new AdminSessions();

        /// <summary>Whether an admin credential is installed and usable.</summary>
        internal static bool IsConfigured
        {
            get
            {
                Ensure();
                return username != null && storedHash != null;
            }
        }

        /// <summary>The configured admin username, or null if the panel is off.</summary>
        internal static string? Username
        {
            get { Ensure(); return username; }
        }

        /// <summary>The stored PBKDF2 hash, or null if the panel is off.</summary>
        internal static string? StoredHash
        {
            get { Ensure(); return storedHash; }
        }

        /// <summary>
        /// Verifies a login attempt against the configured admin. False when the
        /// panel is unconfigured, so an unset credential cannot be logged into.
        /// </summary>
        internal static bool Verify(string? attemptUsername, string? attemptPassword)
        {
            Ensure();
            if (username == null || storedHash == null)
            {
                return false;
            }

            return AdminAuthPolicy.Verify(attemptUsername, attemptPassword, username, storedHash);
        }

        private static void Ensure()
        {
            if (initialized)
            {
                return;
            }

            lock (gate)
            {
                if (initialized)
                {
                    return;
                }

                Load(Environment.GetEnvironmentVariable(AdminAuthPolicy.ConfigVariable));
                initialized = true;
            }
        }

        private static void Load(string? configured)
        {
            if (!AdminAuthPolicy.TrySplitConfig(configured, out string user, out string credential))
            {
                if (!string.IsNullOrWhiteSpace(configured))
                {
                    Console.WriteLine("[warning] " + AdminAuthPolicy.ConfigVariable
                        + " is set but not in the form username:credential; the admin panel is disabled.");
                }
                else
                {
                    Console.WriteLine("[info] admin panel is off (" + AdminAuthPolicy.ConfigVariable
                        + " is unset). Set it to username:hash to enable /admin.");
                }
                return;
            }

            username = user;

            if (AdminAuthPolicy.LooksLikeStoredHash(credential))
            {
                storedHash = credential;
            }
            else
            {
                // Convenience: the operator installed a plaintext password. Hash
                // it in memory so nothing downstream ever sees the plaintext, and
                // tell them how to install the hardened form. The plaintext still
                // lives in their root-owned env file - never in source or logs.
                storedHash = AccountPolicy.HashPassword(credential);
                Console.WriteLine("[warning] " + AdminAuthPolicy.ConfigVariable
                    + " carries a plaintext password. It works, but install the hash instead: "
                    + username + ":" + storedHash);
            }

            Console.WriteLine("[info] admin panel enabled for '" + username + "' at /admin.");
        }
    }
}
