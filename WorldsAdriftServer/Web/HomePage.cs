using System.Text;

namespace WorldsAdriftServer.Web
{
    /// <summary>
    /// The public front door. Unlike the account, download and operator pages,
    /// this page contains no session data and exposes no privileged payloads.
    /// Its only live dependency is the same public map route any visitor can
    /// open directly.
    /// </summary>
    internal static class HomePage
    {
        internal const string ContentType = "text/html; charset=utf-8";

        internal static readonly string Html = Build();

        private static string Build()
        {
            StringBuilder page = new StringBuilder(32_000);
            page.Append(@"<!DOCTYPE html>
<html lang=""en""><head>
<meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1"">
<meta name=""theme-color"" content=""#07131b"">
<meta name=""description"" content=""WAReborn is a fan-run revival rebuilding a persistent shared sky, one recovered system at a time."">
<meta property=""og:type"" content=""website"">
<meta property=""og:title"" content=""WAReborn — The sky remembers"">
<meta property=""og:description"" content=""A fan-run Worlds Adrift revival with persistent ships, real island topology, a live world map, and public development notes."">
<meta name=""twitter:card"" content=""summary_large_image"">
<meta name=""twitter:title"" content=""WAReborn — The sky remembers"">
<meta name=""twitter:description"" content=""Rebuilding a persistent shared sky, one recovered system at a time."">
<title>WAReborn — The sky remembers</title>
<script>document.documentElement.classList.add('js')</script>
<style>");
            page.Append(WebAssets.Read("home.css"));
            page.Append("</style></head><body>\n");
            page.Append(WebAssets.Read("home-body.html"));
            page.Append("<script>\n");
            page.Append(WebAssets.Read("home.js"));
            page.Append("</script></body></html>");
            return page.ToString();
        }
    }
}
