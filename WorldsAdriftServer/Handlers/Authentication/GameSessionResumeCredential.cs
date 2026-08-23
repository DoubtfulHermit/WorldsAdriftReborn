using WorldsAdriftReborn.Storage.Policy;

namespace WorldsAdriftServer.Handlers.Authentication
{
    /// <summary>
    /// The credential envelope used when a trusted client remembers an already
    /// issued game session.  The value after the prefix is still a bearer token:
    /// this type only distinguishes it from a password and validates its exact
    /// server-issued shape.  It never logs, hashes, or transforms the token.
    /// </summary>
    internal static class GameSessionResumeCredential
    {
        internal const string Prefix = "wareborn-session-v1:";

        internal static bool TryParse(string? secret, out string token)
        {
            token = string.Empty;

            if (secret == null || !secret.StartsWith(Prefix, StringComparison.Ordinal))
            {
                return false;
            }

            string candidate = secret.Substring(Prefix.Length);

            // 32 random bytes, base64url without padding, is always 43 chars.
            int expectedLength = ((AccountPolicy.SessionTokenBytes * 8) + 5) / 6;
            if (candidate.Length != expectedLength)
            {
                return false;
            }

            for (int i = 0; i < candidate.Length; i++)
            {
                char c = candidate[i];
                bool valid =
                    (c >= 'a' && c <= 'z') ||
                    (c >= 'A' && c <= 'Z') ||
                    (c >= '0' && c <= '9') ||
                    c == '-' || c == '_';

                if (!valid)
                {
                    return false;
                }
            }

            token = candidate;
            return true;
        }
    }
}
