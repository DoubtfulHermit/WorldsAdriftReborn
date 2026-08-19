using System.Collections.Concurrent;
using System.Globalization;

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
        /// Prints what the fallback origin is, and warns when it is one the game
        /// client provably cannot fetch.
        ///
        /// Called at boot. The warning is the point: an https base URL costs
        /// nothing at startup and produces no error anywhere - the payload is
        /// well formed, the route serves the PNG to a browser, and the only
        /// symptom is that every player's alliance panel keeps its placeholder.
        /// See <see cref="EmblemOrigin"/> for why https cannot work here.
        ///
        /// Takes the sink so it can be asserted in a test rather than only seen
        /// in a journal.
        /// </summary>
        internal static void ReportConfiguration(Action<string> write) =>
            ReportConfiguration(Environment.GetEnvironmentVariable(BaseUrlVariable), write);

        /// <summary>
        /// The same report, with the configured value handed in rather than read
        /// off the environment - so a test can drive both branches without
        /// mutating process state that other tests running beside it can see.
        /// </summary>
        internal static void ReportConfiguration(string? configuredValue, Action<string> write)
        {
            string configured = configuredValue ?? string.Empty;
            string effective = string.IsNullOrWhiteSpace(configured) ? DefaultBaseUrl : configured;

            write("[info] alliance crest fallback origin: " + effective
                + (string.IsNullOrWhiteSpace(configured)
                    ? " (" + BaseUrlVariable + " unset; built-in default)"
                    : " (from " + BaseUrlVariable + ")"));

            if (effective.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                write("[warn] that origin is https, and the GAME CLIENT CANNOT FETCH https"
                    + " - its Mono TLS stack stops at TLS 1.0. Any alliance payload that"
                    + " falls back to it will show the placeholder crest instead of the"
                    + " alliance's own. Set " + BaseUrlVariable
                    + " to the plain-http host:port the game connects to.");
            }
        }

        /// <summary>
        /// How many distinct crests to keep encoded. The vocabulary can express
        /// millions of them, so this is bounded and dropped wholesale when it
        /// fills: an LRU would be more code for a cache whose miss costs one
        /// render, and a server with hundreds of alliances never reaches the lid.
        /// </summary>
        private const int MaxEntries = 512;

        /// <summary>
        /// The second lid, and the reason there are two.
        ///
        /// A crest is about 56 kB, so counting ENTRIES was a fine proxy for
        /// counting memory while every entry was one size. It stopped being one
        /// when the same crest became downloadable at 1024, where the PNG is a
        /// quarter of a megabyte: five hundred of those is 130 MB of a process
        /// that otherwise sits in tens, and an unauthenticated stranger picks
        /// which addresses go in it. This lid is on the BYTES, so the ceiling
        /// stays where it is no matter which sizes get asked for.
        /// </summary>
        private const int MaxBytes = 32 * 1024 * 1024;

        private static readonly ConcurrentDictionary<string, byte[]> Cache =
            new ConcurrentDictionary<string, byte[]>(StringComparer.Ordinal);

        private static int _cachedBytes;

        /// <summary>The PNG for an emblem at the crest size - what the game gets.</summary>
        internal static byte[] Png(EmblemArtwork artwork) => Png(artwork, EmblemPainter.Size);

        /// <summary>
        /// The PNG for an emblem at one edge length, rendered on first ask.
        ///
        /// THE SIZE IS PART OF THE KEY, not a detail of the render. The whole
        /// design is content-addressed - the picture at an address can never
        /// change, which is what earns the immutable Cache-Control - and a cache
        /// keyed on the code alone would hand a 1024 body to the next caller who
        /// asked for 256 at the same code, which is the same bug one layer down.
        /// </summary>
        internal static byte[] Png(EmblemArtwork artwork, int size)
        {
            string key = artwork.ToCode() + "@" + size.ToString(CultureInfo.InvariantCulture);

            if (Cache.TryGetValue(key, out byte[]? cached)) return cached;

            byte[] png = PngWriter.Encode(artwork.RenderPixels(size), size, size);

            if (Cache.Count >= MaxEntries || _cachedBytes >= MaxBytes)
            {
                Cache.Clear();
                Interlocked.Exchange(ref _cachedBytes, 0);
            }

            Cache[key] = png;
            Interlocked.Add(ref _cachedBytes, png.Length);

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
        ///
        /// The format is IN the tag because the two formats of one crest are two
        /// different bodies at two URLs that differ only by extension, and a shared
        /// tag would let a cache answer one with the other. The <c>e2</c> prefix
        /// moved with the emblem code version: a client holding a tag minted before
        /// the device table changed must not be told its cached copy is still good,
        /// because for a version 1 code it is now a different picture.
        ///
        /// THE SIZE IS IN IT for exactly the same reason the format is: one crest
        /// now has three PNG bodies, at three addresses that differ only by a
        /// query parameter. A shared tag would let any cache between here and a
        /// player answer a 1024 request with the 256 it already had, and the
        /// symptom - a blurry download - would look like a rendering bug rather
        /// than a caching one. Only the PNG carries it: an SVG has no pixels, so a
        /// size in a vector's tag would split one document across three entries
        /// that are byte-identical.
        /// </summary>
        internal static string ETag(EmblemArtwork artwork, EmblemUrlPolicy.Format format, int size) =>
            "\"e2-" + (format == EmblemUrlPolicy.Format.Svg
                ? "s"
                : "p" + size.ToString(CultureInfo.InvariantCulture))
            + "-" + artwork.ToCode() + "\"";

        /// <summary>The tag for the crest the game downloads.</summary>
        internal static string ETag(EmblemArtwork artwork, EmblemUrlPolicy.Format format) =>
            ETag(artwork, format, EmblemPainter.Size);
    }
}
