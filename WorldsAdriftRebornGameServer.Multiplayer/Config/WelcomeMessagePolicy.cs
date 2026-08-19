using System;

namespace WorldsAdriftRebornGameServer.Multiplayer.Config
{
    /// <summary>
    /// The welcome message the client shows on the first splash page, and the
    /// rules for deciding which text actually gets rendered.
    ///
    /// WHY THERE IS A BAKED DEFAULT AT ALL. The message is served by our own
    /// login server so an operator can edit it from the admin panel without a
    /// client rebuild or a patcher release. That means the client is now
    /// depending on a network fetch for a piece of UI that appears seconds after
    /// launch, and a fetch has three ways to not be there in time: the server is
    /// down, the player is offline, or the response simply has not landed yet.
    /// In all three the screen still has to say something, and the one thing it
    /// must never say is Bossa's original copy - a player being told they have
    /// joined "a Community-Crafted MMO" that is "still in the early stages of
    /// development" is being told something that stopped being true in 2019.
    /// So the fallback is OUR text, not the prefab's.
    ///
    /// This is deliberately a duplicate of
    /// WorldsAdriftReborn.Storage.Policy.ServerConfigPolicy.DefaultWelcomeMessage.
    /// The two cannot share a file: that one is net8 server code reached through
    /// Postgres, this one is compiled into a net35 Unity assembly that must work
    /// with no server at all. The server's copy is authoritative whenever the
    /// server answers; this one only ever shows when it does not. If you change
    /// one, change the other - DefaultMessageIsPinned in the tests exists to make
    /// an accidental edit here visible rather than silent.
    ///
    /// Kept pure and linked into the net35 client so it is unit tested without
    /// Unity. Keep it net35 / C# 7.3 clean.
    /// </summary>
    public static class WelcomeMessagePolicy
    {
        /// <summary>
        /// What the splash page says when the server has not answered.
        ///
        /// Newlines are \n, never \r\n: TextMeshPro renders a stray carriage
        /// return as a visible box glyph, and the retail string in the prefab
        /// mixes \r\n and \n, which is where that risk comes from.
        /// </summary>
        public const string DefaultMessage =
            "Greetings Traveller,\n"
            + "\n"
            + "Worlds Adrift closed in 2019. Wareborn is a fan-run server that puts it back online.\n"
            + "\n"
            + "Much of the game is here. Islands, ships, mining, crafting, and the sky between them. "
            + "Some of it is not, and some of it breaks. We fix things as we find them.\n"
            + "\n"
            + "Nothing here is for sale. There is no studio behind it, just people who missed the game.\n"
            + "\n"
            + "See you in the skies.\n"
            + "\n"
            + "- The Wareborn crew";

        /// <summary>The path the welcome message is served on, under REST_ServerUrl.</summary>
        public const string WelcomeMessagePath = "/welcomeMessage";

        /// <summary>
        /// An upper bound on what the client will render.
        ///
        /// The scroll is a fixed-size panel with no scrollbar, so an operator who
        /// pastes an essay does not get a long message, they get one that runs off
        /// the bottom of the parchment with no way to read the rest. Refusing the
        /// text outright and keeping the previous one is worse - they would see no
        /// effect and no reason - so this caps rather than rejects, and the admin
        /// panel enforces the same number where the operator can actually see it.
        /// </summary>
        public const int MaxLength = 4000;

        /// <summary>The endpoint the client should GET, derived from REST_ServerUrl.</summary>
        public static string ResolveUrl(string restServerUrl)
        {
            return RestUrlPolicy.TrimTrailingSlashes(restServerUrl) + WelcomeMessagePath;
        }

        /// <summary>
        /// True when a fetched message is worth showing.
        ///
        /// Blank is not: the server's own storage refuses to hold a blank value,
        /// so a blank arriving here means something went wrong between here and
        /// there - a 404 body, an error page, a truncated read - and the honest
        /// response to that is the baked text rather than an empty scroll.
        /// </summary>
        public static bool IsUsable(string message)
        {
            return message != null && message.Trim().Length > 0;
        }

        /// <summary>
        /// Puts a message into the form TextMeshProUGUI should render: \n line
        /// endings, no leading or trailing blank space, and no more than two
        /// consecutive newlines so a stray run of blank lines in the admin
        /// textarea cannot push the sign-off off the parchment.
        /// </summary>
        public static string Normalize(string message)
        {
            if (message == null)
            {
                return string.Empty;
            }

            string text = message.Replace("\r\n", "\n").Replace("\r", "\n").Trim();

            while (text.IndexOf("\n\n\n", StringComparison.Ordinal) >= 0)
            {
                text = text.Replace("\n\n\n", "\n\n");
            }

            if (text.Length > MaxLength)
            {
                text = text.Substring(0, MaxLength).TrimEnd();
            }

            return text;
        }

        /// <summary>
        /// The text to actually render: the fetched message when there is a
        /// usable one, otherwise the baked default. Always normalised, so the
        /// caller can assign the result to a label without further thought.
        /// </summary>
        public static string Choose(string fetched)
        {
            return IsUsable(fetched) ? Normalize(fetched) : Normalize(DefaultMessage);
        }
    }
}
