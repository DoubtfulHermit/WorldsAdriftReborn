using System;
using System.Collections.Generic;

namespace WorldsAdriftRebornGameServer.Multiplayer.Config
{
    /// <summary>
    /// The set of one-time config migrations that have already run, stored as one
    /// comma-separated config value.
    ///
    /// A migration that heals a bad value has to be able to tell "the player never
    /// got this fix" from "the player got it and then chose that value on purpose".
    /// Without a record it cannot, so it re-clobbers the deliberate choice on every
    /// launch. This is that record, kept as a plain string so it lives in the
    /// player's normal config file with no extra state to lose.
    ///
    /// Kept pure and linked into the net35 client so it is unit tested without
    /// Unity. Keep it net35 / C# 7.3 clean.
    /// </summary>
    public static class ConfigMigrationLedger
    {
        private const char Separator = ',';

        /// <summary>Whether <paramref name="id"/> has already been recorded.</summary>
        public static bool Contains(string ledger, string id)
        {
            if (id == null || id.Trim().Length == 0)
            {
                return false;
            }

            string wanted = id.Trim();
            foreach (string entry in Entries(ledger))
            {
                if (string.Equals(entry, wanted, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// The ledger with <paramref name="id"/> recorded. Returns it unchanged when
        /// the id is already present, so writing back is idempotent and does not
        /// grow the value on every launch.
        /// </summary>
        public static string Add(string ledger, string id)
        {
            string normalised = Normalise(ledger);
            if (id == null || id.Trim().Length == 0 || Contains(ledger, id))
            {
                return normalised;
            }

            string wanted = id.Trim();
            return normalised.Length == 0 ? wanted : normalised + Separator + wanted;
        }

        /// <summary>The recorded ids, in order, with blanks and padding dropped.</summary>
        public static List<string> Entries(string ledger)
        {
            List<string> entries = new List<string>();
            if (ledger == null)
            {
                return entries;
            }

            foreach (string raw in ledger.Split(Separator))
            {
                string trimmed = raw.Trim();
                if (trimmed.Length > 0)
                {
                    entries.Add(trimmed);
                }
            }

            return entries;
        }

        private static string Normalise(string ledger)
        {
            return string.Join(Separator.ToString(), Entries(ledger).ToArray());
        }
    }
}
