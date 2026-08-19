namespace WorldsAdriftServer.PatchNotes
{
    /// <summary>What a request under the notes prefix asks for.</summary>
    internal enum PatchNotesRoute
    {
        /// <summary>Not ours. The router must go on looking.</summary>
        None,

        /// <summary>The page itself.</summary>
        Page,

        /// <summary>The raw source, for anyone who would rather read it as text.</summary>
        Source,

        /// <summary>Inside our prefix but not a route we have. Ours to 404.</summary>
        NotFound,
    }

    /// <summary>
    /// The routing decision for <c>/patchnotes</c>, pure and separately tested.
    ///
    /// It CLAIMS THE WHOLE PREFIX. That is not tidiness: this server answers a
    /// request it does not recognise by not answering at all, so the socket sits
    /// open until the browser gives up. A visitor who types <c>/patchnotes/2026</c>
    /// must get a 404 page, and the only way to guarantee that is for one handler
    /// to own every URL beneath the prefix and never return "not mine" from
    /// inside it.
    ///
    /// <c>/patchnotesomething</c> is NOT inside the prefix - it only shares a
    /// leading string - and is correctly none of our business.
    /// </summary>
    internal static class PatchNotesRoutes
    {
        internal const string Prefix = "/patchnotes";

        internal static PatchNotesRoute Match(string? method, string? url)
        {
            string path = PathOf(url);
            if (path.Length == 0)
            {
                return PatchNotesRoute.None;
            }

            bool ours = string.Equals(path, Prefix, StringComparison.Ordinal)
                || path.StartsWith(Prefix + "/", StringComparison.Ordinal);
            if (!ours)
            {
                return PatchNotesRoute.None;
            }

            // Inside the prefix from here on: every answer below is ours to give.
            bool readable = method == "GET" || method == "HEAD";
            if (!readable)
            {
                return PatchNotesRoute.NotFound;
            }

            if (path == Prefix || path == Prefix + "/")
            {
                return PatchNotesRoute.Page;
            }

            if (path == Prefix + "/source" || path == Prefix + "/source/")
            {
                return PatchNotesRoute.Source;
            }

            return PatchNotesRoute.NotFound;
        }

        /// <summary>The path, with any query string or fragment cut off.</summary>
        private static string PathOf(string? url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return string.Empty;
            }

            int cut = url!.IndexOfAny(new[] { '?', '#' });
            return cut < 0 ? url : url.Substring(0, cut);
        }
    }
}
