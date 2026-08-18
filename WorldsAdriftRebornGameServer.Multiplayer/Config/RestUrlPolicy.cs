using System;

namespace WorldsAdriftRebornGameServer.Multiplayer.Config
{
    /// <summary>
    /// How the client's REST-family URLs resolve against each other.
    ///
    /// Retail really did run these as separate services - ConfigKeys.AlliancesUrl
    /// and ConfigKeys.RestServerUrl are distinct keys pointing at distinct hosts -
    /// so an operator who splits them must keep being able to. Ours does not split
    /// them: WorldsAdriftServer answers REST, deploymentStatus and the whole social
    /// API on one origin.
    ///
    /// The old implementation expressed "same origin" as a hardcoded literal
    /// duplicate of the REST default. That is not the same thing, and it broke
    /// every player: REST_AlliancesUrl was a NEW key, BepInEx materialises a new key
    /// into every EXISTING config file using the shipped default, and the shipped
    /// default was a development localhost. Players who had long since pointed
    /// REST_ServerUrl at production silently got an alliances host of
    /// http://127.0.0.1:8080 written into their config, so the Social Sheet's CREW
    /// and ALLIANCE fetches went to a closed port and the sheet failed whole
    /// (SocialCharacterSheet.TriggerAllianceExceptionHandler covers both tabs).
    ///
    /// So "same origin" is expressed as a SENTINEL instead: a blank setting means
    /// "follow REST_ServerUrl", and it keeps following it if REST_ServerUrl later
    /// moves. A non-blank setting is an explicit operator override and is used
    /// verbatim - that is the split retail had.
    ///
    /// Kept pure and linked into the net35 client so this is unit tested without
    /// Unity. Keep it net35 / C# 7.3 clean.
    /// </summary>
    public static class RestUrlPolicy
    {
        /// <summary>
        /// A blank setting means "same origin as REST_ServerUrl". Used as the
        /// shipped default so the derived URLs can never drift from their parent.
        /// </summary>
        public const string FollowRestServerUrl = "";

        /// <summary>
        /// The exact literal that shipped as the REST_AlliancesUrl default and
        /// broke every existing install. Matched byte-for-byte by the heal below;
        /// see ShouldHealAlliancesUrl for why nothing looser is safe.
        /// </summary>
        public const string LegacyAlliancesDevDefault = "http://127.0.0.1:8080";

        /// <summary>The path REST_ServerDeploymentUrl adds to the REST origin.</summary>
        public const string DeploymentStatusPath = "/deploymentStatus";

        /// <summary>Ledger id for the one-time heal of the localhost alliances default.</summary>
        public const string AlliancesHealMigrationId = "alliances-url-follows-rest";

        /// <summary>
        /// True when a setting is empty and therefore means "same origin as
        /// REST_ServerUrl" rather than an explicit host.
        /// </summary>
        public static bool FollowsRestServerUrl(string setting)
        {
            return setting == null || setting.Trim().Length == 0;
        }

        /// <summary>
        /// Strips trailing slashes. The client joins the alliances origin with
        /// "/" + endpoint (SocialRequest.cs:69), so a trailing slash produces
        /// "host//crew". Applied to the RESOLVED value, so an operator who pastes a
        /// URL with a trailing slash gets the documented behaviour rather than a
        /// silently broken one.
        /// </summary>
        public static string TrimTrailingSlashes(string url)
        {
            if (url == null)
            {
                return string.Empty;
            }

            string trimmed = url.Trim();
            while (trimmed.Length > 0 && trimmed[trimmed.Length - 1] == '/')
            {
                trimmed = trimmed.Substring(0, trimmed.Length - 1);
            }

            return trimmed;
        }

        /// <summary>
        /// The social/alliances origin the client should actually call: the
        /// explicit setting when there is one, otherwise REST_ServerUrl.
        /// </summary>
        public static string ResolveAlliancesUrl(string alliancesSetting, string restServerUrl)
        {
            return TrimTrailingSlashes(
                FollowsRestServerUrl(alliancesSetting) ? restServerUrl : alliancesSetting);
        }

        /// <summary>
        /// The deploymentStatus endpoint: the explicit setting when there is one,
        /// otherwise REST_ServerUrl + /deploymentStatus.
        ///
        /// Same bug class as the alliances key - the shipped default was a
        /// hardcoded localhost duplicate of REST_ServerUrl with a path glued on,
        /// which cannot track REST_ServerUrl. It never hurt an existing player only
        /// because it is an OLD key that installs already had set; a new key with
        /// that shape is exactly what broke the Social Sheet.
        /// </summary>
        public static string ResolveDeploymentUrl(string deploymentSetting, string restServerUrl)
        {
            if (!FollowsRestServerUrl(deploymentSetting))
            {
                return deploymentSetting.Trim();
            }

            return TrimTrailingSlashes(restServerUrl) + DeploymentStatusPath;
        }

        /// <summary>
        /// True when a URL names a host only the player's own machine can reach, or
        /// no host at all. Anything unparseable counts as "not a real remote host",
        /// so callers that gate on a REMOTE url fail closed.
        /// </summary>
        public static bool IsLoopbackOrUnroutable(string url)
        {
            string host = HostOf(url);
            if (host.Length == 0)
            {
                return true;
            }

            if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
                || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (host == "::1" || host == "[::1]" || host == "0.0.0.0")
            {
                return true;
            }

            // The whole 127.0.0.0/8 block, not just 127.0.0.1.
            return host.StartsWith("127.", StringComparison.Ordinal);
        }

        /// <summary>
        /// Whether to rewrite an already-written localhost alliances URL back to
        /// "follow REST_ServerUrl".
        ///
        /// BepInEx will not rewrite a value that already exists in a config file, so
        /// fixing the shipped default alone leaves every install that already took
        /// the bad default broken forever. This heals them - once.
        ///
        /// The hard part is not clobbering a developer who deliberately points
        /// alliances at their own machine. That cannot be distinguished with
        /// certainty: a deliberate "http://127.0.0.1:8080" is byte-identical to the
        /// bad default. So this takes the conservative option and requires all
        /// three:
        ///
        ///  1. the value is the shipped legacy literal EXACTLY (ordinal, modulo
        ///     surrounding whitespace and trailing slashes) - not merely "some
        ///     loopback URL". A developer running a local server points at the port
        ///     it actually listens on, which is not the stale 8080 in that literal;
        ///  2. REST_ServerUrl is a real REMOTE host. A local dev has REST on
        ///     loopback too, and is left completely alone. The healed combination -
        ///     production REST plus a loopback social host - is not a deployment
        ///     anyone runs, because one server answers both;
        ///  3. the heal has never run before. After it runs the ledger records it,
        ///     so a developer who then deliberately types that exact literal back
        ///     keeps it.
        ///
        /// Residual risk, stated rather than hidden: a developer whose FIRST launch
        /// after this change has production REST plus a hand-typed
        /// http://127.0.0.1:8080 loses that one setting, once, with a warning
        /// logged. Re-entering it is permanent.
        /// </summary>
        public static bool ShouldHealAlliancesUrl(
            string alliancesSetting,
            string restServerUrl,
            bool migrationAlreadyApplied)
        {
            if (migrationAlreadyApplied || alliancesSetting == null)
            {
                return false;
            }

            if (!string.Equals(TrimTrailingSlashes(alliancesSetting),
                               LegacyAlliancesDevDefault,
                               StringComparison.Ordinal))
            {
                return false;
            }

            return !IsLoopbackOrUnroutable(restServerUrl);
        }

        /// <summary>The host of an absolute http(s) URL, or "" if it is not one.</summary>
        private static string HostOf(string url)
        {
            if (url == null || url.Trim().Length == 0)
            {
                return string.Empty;
            }

            Uri parsed;
            if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out parsed))
            {
                return string.Empty;
            }

            if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
            {
                return string.Empty;
            }

            return parsed.Host ?? string.Empty;
        }
    }
}
