using System.Collections.Concurrent;

namespace WorldsAdriftServer.Emblems
{
    /// <summary>
    /// The rendered-PNG cache and the one piece of configuration the emblem
    /// feature has.
    ///
    /// The cache is in memory and nothing else: an emblem is a pure function of
    /// twelve characters, so a cold cache costs one render (single-digit
    /// milliseconds) and a lost cache costs nothing at all. Writing these to disk
    /// would have bought a few milliseconds in exchange for a directory that grows
    /// forever and a second place the truth can live - and "no image bytes are
    /// stored anywhere" is one of the reasons this design was chosen over an
    /// upload in the first place.
    /// </summary>
    internal static class EmblemImages
    {
        /// <summary>
        /// The public origin the client will fetch emblems from.
        ///
        /// It has to be configured rather than derived from the request, because
        /// the URL is minted while answering the GAME client's social call and
        /// baked into a payload the client keeps - and the game client reaches this
        /// process through Caddy, so the Host header it sends is not something to
        /// build a durable URL out of. Same default as the patcher's manifest URL
        /// (tools/patcher/WAPatch/PatchConfig.cs), overridable for a dev box or a
        /// test exactly like WAREBORN_DATA_DIR and WAREBORN_PATCH_DIR.
        /// </summary>
        internal const string BaseUrlVariable = "WAREBORN_PUBLIC_BASE_URL";

        internal const string DefaultBaseUrl = "https://wareborn.ratlabs.cc";

        internal static string BaseUrl
        {
            get
            {
                string? configured = Environment.GetEnvironmentVariable(BaseUrlVariable);
                return string.IsNullOrWhiteSpace(configured) ? DefaultBaseUrl : configured!;
            }
        }

        /// <summary>
        /// How many distinct crests to keep encoded. The vocabulary can express
        /// millions of them, so this is bounded and dropped wholesale when it
        /// fills: an LRU would be more code for a cache whose miss costs one
        /// render, and a server with hundreds of alliances never reaches the lid.
        /// </summary>
        private const int MaxEntries = 512;

        private static readonly ConcurrentDictionary<string, byte[]> Cache =
            new ConcurrentDictionary<string, byte[]>(StringComparer.Ordinal);

        /// <summary>The PNG for a spec, rendered on first ask.</summary>
        internal static byte[] Png(EmblemSpec spec)
        {
            string code = spec.ToCode();

            if (Cache.TryGetValue(code, out byte[]? cached)) return cached;

            byte[] png = PngWriter.Encode(
                EmblemPainter.Render(spec), EmblemPainter.Size, EmblemPainter.Size);

            if (Cache.Count >= MaxEntries) Cache.Clear();
            Cache[code] = png;

            return png;
        }

        /// <summary>
        /// The ETag for a spec.
        ///
        /// This is load-bearing rather than polite. BestHTTP caches GET responses
        /// to disk by default and the game never disables it; on a revalidation it
        /// sends <c>If-None-Match</c> with whatever we last gave it
        /// (HTTPCacheFileInfo.SetUpRevalidationHeaders). An emblem route with no
        /// ETag would make every revalidation a full re-download of bytes that
        /// never change - the URL carries the code, so a given URL's picture is
        /// immutable by construction.
        /// </summary>
        internal static string ETag(EmblemSpec spec) => "\"e1-" + spec.ToCode() + "\"";
    }
}
