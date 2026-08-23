using System.Text;

namespace WorldsAdriftServer.Web
{
    /// <summary>
    /// Shared public navigation and visual assets for every player-facing page.
    /// The operator console deliberately does not use this shell: it is an
    /// authenticated instrument, while these routes form the public website.
    /// </summary>
    internal static class PublicSiteChrome
    {
        internal static readonly string Style =
            "<style>" + WebAssets.Read("site-shell.css") + "</style>";

        internal static readonly string PlayerStyle =
            "<style>" + WebAssets.Read("site-player.css") + "</style>";

        internal static string Header(string current, bool signedIn)
        {
            string accountLabel = signedIn ? "Crew portal" : "Sign in";
            string accountHref = signedIn ? "/account" : "/login";

            return @"<header class=""wa-sitebar"">
  <a class=""wa-brand"" href=""/"" aria-label=""WAReborn home""><span aria-hidden=""true"">W</span><strong>WAReborn</strong></a>
  <nav class=""wa-nav"" aria-label=""Site"">" + Links(current, accountLabel, accountHref) + @"</nav>
  <details class=""wa-mobile-nav""><summary>Menu</summary><nav aria-label=""Mobile site navigation"">" + Links(current, accountLabel, accountHref) + @"</nav></details>
</header>";
        }

        private static string Links(string current, string accountLabel, string accountHref)
        {
            StringBuilder links = new StringBuilder();
            Link(links, current, "home", "/", "Home");
            Link(links, current, "map", "/map", "Live world");
            Link(links, current, "build", "/patchnotes", "Build log");
            Link(links, current, "account", accountHref, accountLabel);
            Link(links, current, "download", "/download", "Download");
            if (!string.Equals(accountHref, "/account", StringComparison.Ordinal))
            {
                Link(links, current, "signup", "/signup", "Create account", true);
            }

            return links.ToString();
        }

        private static void Link(
            StringBuilder links, string current, string id, string href, string label,
            bool action = false)
        {
            bool active = string.Equals(current, id, StringComparison.Ordinal);
            links.Append("<a href=\"").Append(href).Append("\"");
            // The account portal has its own tab strip whose one current tab is
            // explicitly aria-current=page. The site-wide shell is the visitor's
            // LOCATION within the larger site, so it uses the matching ARIA value
            // and does not masquerade as a second current portal tab.
            if (active) links.Append(" aria-current=\"location\"");
            if (action) links.Append(" class=\"wa-nav-action\"");
            links.Append('>').Append(label).Append("</a>");
        }
    }
}
