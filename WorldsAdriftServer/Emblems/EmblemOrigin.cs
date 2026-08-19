namespace WorldsAdriftServer.Emblems
{
    /// <summary>
    /// Which origin an alliance crest URL must name, given the request that is
    /// being answered.
    ///
    /// WHY THIS EXISTS AT ALL - the emblem never appeared in the game, and the
    /// reason was the SCHEME. The crest URL is minted into the alliance payload
    /// and then fetched by <c>Travellers.UI.Framework.SpriteDownloader</c>, which
    /// hands it to BestHTTP. BestHTTP in this client has
    /// <c>HTTPManager.UseAlternateSSLDefaultValue = false</c>, so an https request
    /// does NOT go through its bundled BouncyCastle stack - it goes through
    /// <c>System.Net.Security.SslStream</c>, which in Unity 5.6's Mono is
    /// implemented by <c>Mono.Security.Protocol.Tls</c>. That assembly's
    /// <c>SecurityProtocolType</c> enum has exactly three members: Ssl2, Ssl3 and
    /// Tls - TLS 1.0. There is no TLS 1.2 code path in the client at all.
    ///
    /// The public host is behind a modern reverse proxy that answers a TLS 1.0
    /// ClientHello with a fatal <c>protocol_version</c> alert (measured: alert
    /// level 2, description 70) and holds an ECDSA certificate the client's
    /// legacy RSA cipher list could not use even if the version matched. So an
    /// https crest URL can never be fetched by this game client, the promise in
    /// <c>AllianceClient.GetEmblem</c> is rejected, and
    /// <c>YourAllianceTitleSegment</c> keeps the placeholder hexagon it set before
    /// the download - which is exactly the reported symptom.
    ///
    /// THE FIX IS TO ECHO BACK THE ORIGIN THE CALLER ALREADY REACHED US ON. The
    /// alliance payload is minted while answering the game client's own request to
    /// this server, and that request arrived over PLAIN HTTP on this process's own
    /// listener (the game is configured with a direct <c>http://host:port</c>
    /// REST url; the proxy is for browsers). A URL built from that request's Host
    /// is reachable by construction - the client just used it - and needs no
    /// configuration, so it cannot rot when the address changes.
    ///
    /// The configured <see cref="EmblemImages.BaseUrlVariable"/> stays as the
    /// fallback for a request that carries no Host at all (HTTP/1.0, or a unit
    /// test), and as an operator override for a deployment where the reachable
    /// origin genuinely is not the one the request came in on.
    ///
    /// Pure: strings in, one string out. No environment, no request object.
    /// </summary>
    internal static class EmblemOrigin
    {
        private const string Http = "http://";
        private const string Https = "https://";

        /// <summary>
        /// The origin to build crest URLs from for the request described by these
        /// headers, or <paramref name="fallback"/> when they describe nothing
        /// usable.
        /// </summary>
        /// <param name="host">The request's <c>Host</c> header.</param>
        /// <param name="forwardedHost">
        /// <c>X-Forwarded-Host</c>, present only when a reverse proxy is in front
        /// of us. When it is, <paramref name="host"/> is the proxy's view of
        /// itself and NOT something a client can dial, so the forwarded pair wins.
        /// </param>
        /// <param name="forwardedProto">
        /// <c>X-Forwarded-Proto</c>. Only meaningful alongside
        /// <paramref name="forwardedHost"/>; anything other than the two schemes
        /// we serve is treated as absent.
        /// </param>
        /// <param name="fallback">
        /// The configured base URL. Returned verbatim (minus trailing slashes)
        /// when the headers give us nothing.
        /// </param>
        internal static string For(
            string? host, string? forwardedHost, string? forwardedProto, string? fallback)
        {
            string proxied = FirstHost(forwardedHost);
            if (proxied.Length > 0)
            {
                // Behind a proxy the scheme is NOT ours to guess: we are speaking
                // plain HTTP to the proxy no matter what the client used. Default
                // to https when the proxy did not say, because a proxy that
                // terminates TLS is the only reason this header is here.
                string scheme = string.Equals(Trim(forwardedProto), "http", StringComparison.OrdinalIgnoreCase)
                    ? Http
                    : Https;

                return scheme + proxied;
            }

            string direct = FirstHost(host);
            if (direct.Length > 0)
            {
                // http, not https, and not a guess: this listener speaks plain
                // HTTP and the caller reached it, so this exact origin is known
                // good. Minting https here is precisely the bug this module is
                // named after.
                return Http + direct;
            }

            return TrimTrailingSlashes(Trim(fallback));
        }

        /// <summary>
        /// One host[:port] out of a header value, or empty when there is nothing
        /// safe to use.
        ///
        /// <c>X-Forwarded-Host</c> is allowed to be a comma-separated list when a
        /// request crossed more than one proxy; the FIRST entry is the one the
        /// client actually asked for. Everything else here is a rejection: a value
        /// carrying a scheme, a path, a space or a control character is not a host
        /// and must not be pasted into a URL we hand to a client - the header is
        /// attacker-controlled, and the string ends up in a payload other players
        /// read.
        /// </summary>
        private static string FirstHost(string? value)
        {
            string text = Trim(value);
            if (text.Length == 0) return string.Empty;

            int comma = text.IndexOf(',');
            if (comma >= 0) text = text.Substring(0, comma).Trim();

            if (text.Length == 0) return string.Empty;

            foreach (char c in text)
            {
                if (char.IsWhiteSpace(c) || char.IsControl(c)) return string.Empty;
                if (c == '/' || c == '\\' || c == '?' || c == '#' || c == '@') return string.Empty;
            }

            // A host is host[:port]. A colon is only legal as the port separator
            // here - we never serve on an IPv6 literal - so more than one means
            // this is not a host we minted a listener for.
            int colons = 0;
            foreach (char c in text)
            {
                if (c == ':') colons++;
            }
            if (colons > 1) return string.Empty;

            return text;
        }

        private static string Trim(string? value) => (value ?? string.Empty).Trim();

        private static string TrimTrailingSlashes(string text)
        {
            while (text.EndsWith("/", StringComparison.Ordinal))
            {
                text = text.Substring(0, text.Length - 1);
            }
            return text;
        }
    }
}
