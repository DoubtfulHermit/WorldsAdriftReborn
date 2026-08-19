using System.Globalization;

namespace WorldsAdriftServer.Portal
{
    /// <summary>
    /// Which tab of the account portal is being looked at.
    ///
    /// THE TABS ARE NAVIGATION, NOT SCRIPT. Each one is a real URL - <c>?tab=</c> -
    /// and only the panel it names is rendered. That is not a preference:
    /// <list type="bullet">
    /// <item>the portal had grown to an account block, the patcher, a full sheet
    ///   per character, a crew, an alliance roster and an emblem editor with fifty
    ///   objects in it. Rendering all of that and hiding most of it means every
    ///   visit about a password pays for the emblem editor;</item>
    /// <item>a tab you can LINK to is a tab a player can bookmark and a maintainer
    ///   can be pointed at, and it survives a browser with script off;</item>
    /// <item>and it is the only way to get the thing that actually breaks the feel
    ///   of a tabbed page right - after a POST, landing back where you were. The
    ///   redirect carries the tab, so saving a crest returns to the crest.
    ///   <see cref="AfterPost"/> derives that from the route rather than from a
    ///   hidden field, so a form cannot forget to carry it.</item>
    /// </list>
    ///
    /// Pure: strings in, strings out. No view, no request, no session.
    /// </summary>
    internal static class PortalTabs
    {
        /// <summary>The query-string key. Short, like the notice's.</summary>
        internal const string Field = "tab";

        internal const string Account = "account";
        internal const string Patcher = "patcher";
        internal const string Alliance = "alliance";
        internal const string Emblem = "emblem";

        /// <summary>
        /// A character's tab id.
        ///
        /// The uid with no hyphens, prefixed, so it is a valid HTML id and a valid
        /// query value with nothing to escape at either end - the same reason the
        /// page's anchors have always used the N format.
        /// </summary>
        internal static string CharacterId(Guid uid) =>
            "c" + uid.ToString("N", CultureInfo.InvariantCulture);

        /// <summary>
        /// The tabs this view has, in order.
        ///
        /// A tab with nothing behind it is not drawn at all. A player in no
        /// alliance has no Alliance tab and no Emblem tab, rather than two tabs
        /// that explain their own emptiness - the portal already tells them they
        /// are in no alliance, once, on the character it belongs to.
        /// </summary>
        internal static IReadOnlyList<PortalTab> For(PortalView view)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));

            List<PortalTab> tabs = new List<PortalTab>
            {
                new PortalTab(Account, "Account"),
                new PortalTab(Patcher, "Patcher"),
            };

            foreach (CharacterCard card in view.Characters)
            {
                tabs.Add(new PortalTab(CharacterId(card.Sheet.Uid), card.Sheet.Name));
            }

            bool anyAlliance = false;
            foreach (CharacterCard card in view.Characters)
            {
                if (card.Alliance != null) anyAlliance = true;
            }

            if (anyAlliance)
            {
                tabs.Add(new PortalTab(Alliance, "Alliance"));
                tabs.Add(new PortalTab(Emblem, "Emblem"));
            }

            return tabs;
        }

        /// <summary>
        /// The tab a URL asks for, or null. Bounded and character-checked, so a
        /// hand-made query string hands the page nothing it has to think about:
        /// what comes back is either one of ours or nothing.
        /// </summary>
        internal static string? Requested(string? url)
        {
            if (url == null) return null;

            int q = url.IndexOf('?');
            if (q < 0) return null;

            string? found = null;

            foreach (string pair in url.Substring(q + 1).Split('&'))
            {
                int eq = pair.IndexOf('=');
                if (eq <= 0) continue;
                if (!string.Equals(pair.Substring(0, eq), Field, StringComparison.Ordinal)) continue;

                string value = pair.Substring(eq + 1);
                found = IsWellFormed(value) ? value : null;
            }

            return found;
        }

        /// <summary>
        /// The tab to draw, given what was asked for and what exists.
        ///
        /// A tab that is not there falls back to the FIRST one rather than
        /// refusing. An old bookmark to a character that has since been deleted, or
        /// to the alliance of an alliance somebody left, should open the portal -
        /// not an error page about a tab.
        /// </summary>
        internal static string Resolve(string? requested, IReadOnlyList<PortalTab> tabs)
        {
            if (tabs == null || tabs.Count == 0) return Account;

            if (requested != null)
            {
                foreach (PortalTab tab in tabs)
                {
                    if (string.Equals(tab.Id, requested, StringComparison.Ordinal)) return tab.Id;
                }
            }

            return tabs[0].Id;
        }

        /// <summary>
        /// Which tab a POST to this route should come back to.
        ///
        /// Derived from the ROUTE, not from a field the form carries. A hidden
        /// field is one more thing every form has to remember, and the day one
        /// forgets it the player is silently dumped on the Account tab after
        /// saving a crest - which is exactly the kind of small wrongness that makes
        /// a page feel broken.
        /// </summary>
        internal static string AfterPost(string? path)
        {
            string route = LastSegment(path);

            return route switch
            {
                "alliance-emblem" => Emblem,
                "alliance-details" => Alliance,
                "alliance-member" => Alliance,
                "alliance-request" => Alliance,
                _ => Account,
            };
        }

        /// <summary>The portal URL for a tab, optionally carrying a notice code.</summary>
        internal static string Url(string page, string tab, string? notice = null) =>
            page + "?" + Field + "=" + tab
            + (notice == null ? string.Empty : "&" + PortalNotices.Field + "=" + notice);

        private static string LastSegment(string? path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;

            int slash = path!.LastIndexOf('/');
            return slash < 0 ? path : path.Substring(slash + 1);
        }

        /// <summary>
        /// Whether a value could be one of our tab ids at all: short, and letters
        /// and digits only. Every id this file mints is either a lower-case word or
        /// <c>c</c> followed by 32 hex digits.
        /// </summary>
        private static bool IsWellFormed(string value)
        {
            if (value.Length == 0 || value.Length > 40) return false;

            foreach (char c in value)
            {
                bool ascii = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
                if (!ascii) return false;
            }

            return true;
        }
    }

    /// <summary>One tab: the id a URL carries and the word a player reads.</summary>
    internal readonly record struct PortalTab(string Id, string Label);
}
