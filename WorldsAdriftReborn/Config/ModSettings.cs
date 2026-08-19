using BepInEx;
using BepInEx.Configuration;
using System.Runtime.InteropServices;
using WorldsAdriftRebornGameServer.Multiplayer.Config;

namespace WorldsAdriftReborn.Config
{
    internal static class ModSettings
    {
        /// <summary>Native setter for the game server port (see the call site for why an env var will not do).</summary>
        [DllImport("CoreSdkDll", CallingConvention = CallingConvention.Cdecl)]
        private static extern void WAR_SetGamePort(int port);

        public static ConfigFile modConfig { get; set; }
        public static ConfigEntry<string> steamUserId { get; set; }
        public static ConfigEntry<string> steamAppId { get; set; }
        public static ConfigEntry<string> steamBranchName { get; set; }
        public static ConfigEntry<string> restServerUrl { get; set; }
        public static ConfigEntry<string> restServerDeploymentUrl { get; set; }
        public static ConfigEntry<string> alliancesServerUrl { get; set; }
        public static ConfigEntry<string> createAccountUrl { get; set; }
        public static ConfigEntry<string> passwordResetUrl { get; set; }
        public static ConfigEntry<string> mapUrl { get; set; }
        public static ConfigEntry<string> patchNotesUrl { get; set; }
        public static ConfigEntry<string> NTPServerUrl { get; set; }
        public static ConfigEntry<string> localAssetPath { get; set; }
        public static ConfigEntry<string> gameServerHost { get; set; }
        public static ConfigEntry<string> gameServerPort { get; set; }
        public static ConfigEntry<int> perfSpikeThresholdMs { get; set; }
        public static ConfigEntry<string> stationPickupKey { get; set; }
        public static ConfigEntry<string> appliedConfigMigrations { get; set; }

        /// <summary>The social/alliances host to call, with the blank-means-REST default applied.</summary>
        public static string AlliancesUrl()
        {
            return RestUrlPolicy.ResolveAlliancesUrl(alliancesServerUrl.Value, restServerUrl.Value);
        }

        /// <summary>
        /// The welcome-message endpoint. Derived from REST_ServerUrl rather than
        /// being its own setting: it is served by the same WorldsAdriftServer that
        /// answers REST, so a separate key could only ever drift out of step with
        /// it, which is exactly the bug RestUrlPolicy exists to document.
        /// </summary>
        public static string WelcomeMessageUrl()
        {
            return WelcomeMessagePolicy.ResolveUrl(restServerUrl.Value);
        }

        /// <summary>The deploymentStatus endpoint, with the blank-means-REST default applied.</summary>
        public static string DeploymentStatusUrl()
        {
            return RestUrlPolicy.ResolveDeploymentUrl(restServerDeploymentUrl.Value, restServerUrl.Value);
        }

        /// <summary>
        /// Repairs a config that already took the broken localhost alliances
        /// default, once.
        ///
        /// Fixing the shipped default is not enough on its own: BepInEx never
        /// rewrites a key that is already present, so every install that was
        /// updated while the bad default was live keeps the dead
        /// http://127.0.0.1:8080 forever. The decision of whether a stored value
        /// is the accident or a deliberate choice lives in RestUrlPolicy, which
        /// is conservative about it; this is only the glue that applies it and
        /// records that it ran.
        /// </summary>
        private static void HealLocalhostAlliancesUrl()
        {
            bool alreadyApplied = ConfigMigrationLedger.Contains(
                appliedConfigMigrations.Value, RestUrlPolicy.AlliancesHealMigrationId);

            if (!RestUrlPolicy.ShouldHealAlliancesUrl(
                    alliancesServerUrl.Value, restServerUrl.Value, alreadyApplied))
            {
                return;
            }

            string was = alliancesServerUrl.Value;
            alliancesServerUrl.Value = RestUrlPolicy.FollowRestServerUrl;
            appliedConfigMigrations.Value = ConfigMigrationLedger.Add(
                appliedConfigMigrations.Value, RestUrlPolicy.AlliancesHealMigrationId);

            // Explicit, though SaveOnConfigSet is on by default: if the ledger
            // entry did not reach disk the repair would run again next launch and
            // stop being one-time, which is the whole guard against clobbering a
            // deliberate setting. WAConfig_Patch also Reload()s this file every
            // 5 s, so an unsaved in-memory value would simply be read back over.
            modConfig.Save();

            UnityEngine.Debug.LogWarning(
                "[WAReborn] REST_AlliancesUrl was the old localhost default (" + was
                + ") while REST_ServerUrl points at " + restServerUrl.Value
                + ". The Social Sheet cannot reach that, so it has been reset to follow"
                + " REST_ServerUrl. Set it explicitly if you meant to split the two hosts;"
                + " this repair runs only once.");
        }

        public static void InitConfig()
        {
            modConfig = new ConfigFile(Paths.ConfigPath + "\\WorldsAdriftReborn.cfg", true);

            steamUserId = modConfig.Bind<string>("Steam",
                                                    "Steam_UserId",
                                                    "steamId",
                                                    "Sets the Steam User ID that the game uses internally. Its not important for the functionality to set this to a specific value.");
            steamAppId = modConfig.Bind<string>("Steam",
                                                    "Steam_AppId",
                                                    "123456789",
                                                    "Sets the Steam App ID that the game uses internally. Its not important for the functionality to set this to a specific value.");
            steamBranchName = modConfig.Bind<string>("Steam",
                                                    "Steam_BranchName",
                                                    "WorldsAdriftRebornBranch",
                                                    "Sets the Steam Branch name that the game uses internally. Its not important for the functionality to set this to a specific value.");

            restServerUrl = modConfig.Bind<string>("REST",
                                                    "REST_ServerUrl",
                                                    "http://127.0.0.1:8080",
                                                    "Sets the URL for the REST server that the game queries once the main menu is reached.");
            // Blank means REST_ServerUrl + /deploymentStatus. See the alliances
            // key below for why a derived URL must not ship as a hardcoded literal.
            restServerDeploymentUrl = modConfig.Bind<string>("REST",
                                                    "REST_ServerDeploymentUrl",
                                                    RestUrlPolicy.FollowRestServerUrl,
                                                    "Sets the URL for the REST server that the game queries once the main menu is reached. It is the endpoint where server status informations are retrieved from. Leave blank to use REST_ServerUrl with /deploymentStatus appended.");

            // The dead Bossa alliances host, redirected at our login server.
            //
            // Its own entry rather than a reuse of REST_ServerUrl because retail
            // really did run these as two services - ConfigKeys.AlliancesUrl and
            // ConfigKeys.RestServerUrl are separate keys pointing at separate
            // hosts - and an operator who splits them should not have to patch
            // code. Ours does not split them: WorldsAdriftServer serves both.
            //
            // "Same origin" is therefore the BLANK default, not a copy of the
            // REST default. It used to be the literal "http://127.0.0.1:8080",
            // and that broke every player: this is a NEW key, BepInEx writes a
            // new key into every EXISTING config using the shipped default, so
            // players who had long since pointed REST_ServerUrl at production
            // silently got a localhost alliances host. Both Social Sheet tabs
            // fetch through it, and the failure lands in the shared
            // SocialCharacterSheet.TriggerAllianceExceptionHandler, so the whole
            // sheet died with "Can't retrieve alliance or crew data". A blank
            // default cannot drift from REST_ServerUrl the way a copy did.
            //
            // No trailing slash: the client joins this with "/" + endpoint
            // (SocialRequest.cs:69), so a trailing slash produces a double one.
            // RestUrlPolicy strips one rather than trusting the operator to.
            alliancesServerUrl = modConfig.Bind<string>("REST",
                                                    "REST_AlliancesUrl",
                                                    RestUrlPolicy.FollowRestServerUrl,
                                                    "Sets the URL for the social/alliances server - the host that answers the Social Sheet's alliance and CREW requests. Leave blank (the default) to use the same host as REST_ServerUrl, which is what our server does. Set it only to point the social API at a DIFFERENT host. No trailing slash.");

            // The landing screen's three outbound links.
            //
            // All of them pointed at hosts that have been dead for years: the
            // account and password pages at an S3 redirect bucket Bossa took
            // down, and FORUMS at worldsadrift.com. A button that opens a dead
            // page is worse than one that is missing, because the player assumes
            // they did something wrong.
            //
            // These are settings rather than constants because an operator
            // running their own instance has their own site, the same reason
            // REST_ServerUrl is a setting.
            createAccountUrl = modConfig.Bind<string>("Links",
                                                    "Links_CreateAccountUrl",
                                                    "https://wareborn.ratlabs.cc/",
                                                    "Where the CREATE ACCOUNT button on the login screen goes. Opens in the player's browser.");

            // No password reset exists yet, so this goes to the same place as
            // CREATE ACCOUNT rather than to the dead Bossa reset page. Point it
            // at a real reset endpoint once there is one.
            passwordResetUrl = modConfig.Bind<string>("Links",
                                                    "Links_PasswordResetUrl",
                                                    "https://wareborn.ratlabs.cc/",
                                                    "Where the FORGOT PASSWORD button on the login screen goes. There is no self-service reset yet, so this points at the site.");

            mapUrl = modConfig.Bind<string>("Links",
                                                    "Links_MapUrl",
                                                    "https://wareborn.ratlabs.cc/map",
                                                    "Where the MAP button on the login screen goes. This is the button the retail client labelled FORUMS.");

            // PATCH NOTES is NOT a link in the retail client - it toggles an
            // in-panel pane that ChangeLogLoader fills from
            // ConfigKeys.ClientReleaseNotesUrl in Bossa's own "<size=14>version|date</size>"
            // markup. That pane has been fed a dummy string by
            // ChangeLogLoader_Patch for as long as this mod has existed. Pointing
            // it at a normal web page would render the raw HTML in-game, so the
            // button opens the page in a browser instead; see PatchNotesButton_Patch.
            patchNotesUrl = modConfig.Bind<string>("Links",
                                                    "Links_PatchNotesUrl",
                                                    "https://wareborn.ratlabs.cc/patchnotes",
                                                    "Where the PATCH NOTES button on the login screen goes. Opens in the player's browser rather than the retail client's in-game pane, which expects Bossa's own changelog markup.");

            appliedConfigMigrations = modConfig.Bind<string>("Internal",
                                                    "Internal_AppliedMigrations",
                                                    "",
                                                    "Bookkeeping: one-time config repairs that have already been applied, comma separated. Do not edit unless you want one to run again.");

            HealLocalhostAlliancesUrl();

            NTPServerUrl = modConfig.Bind<string>("NTP",
                                                    "NTP_ServerUrl",
                                                    "pool.ntp.org",
                                                    "Set the NTP server that should be used to synchronize time.");

            localAssetPath = modConfig.Bind<string>("AssetLoader",
                                                    "AssetLoader_FilePath",
                                                    "Assets\\",
                                                    "The intermediate part of the Asset folder path. Gets 'unity\\' appended. In some cases the game fails to determine the intermediate path so you can set it here or leave it blank.");

            gameServerPort = modConfig.Bind<string>("GameServer",
                                                    "GameServer_Port",
                                                    "7777",
                                                    "The UDP port of the game server. Change this if the server is hosted somewhere 7777 is already in use.");

            // Hand the port to the native SDK, which opens the connection itself.
            // An environment variable does NOT work: .NET updates the Win32
            // environment, but the native DLL's C runtime reads a snapshot taken
            // when it loaded, so getenv() there never sees it - the client kept
            // connecting to 7777. Call its exported setter instead.
            if (int.TryParse(gameServerPort.Value, out int parsedGamePort))
            {
                try
                {
                    WAR_SetGamePort(parsedGamePort);
                    UnityEngine.Debug.Log("[WAReborn] game server port set to " + parsedGamePort);
                }
                catch (System.Exception e)
                {
                    UnityEngine.Debug.LogWarning("[WAReborn] could not set game port: " + e.Message);
                }
            }

            gameServerHost = modConfig.Bind<string>("GameServer",
                                                    "GameServer_Host",
                                                    "127.0.0.1",
                                                    "The hostname or address of the game server.");

            perfSpikeThresholdMs = modConfig.Bind<int>("Perf",
                                                    "Perf_SpikeThresholdMs",
                                                    100,
                                                    "Frame-time threshold in milliseconds above which the stutter probe logs one '[WAR][perf] spike' attribution line. Minimum 20.");

            stationPickupKey = modConfig.Bind<string>("Interact",
                                                    "Interact_StationPickupKey",
                                                    "X",
                                                    "UnityEngine.KeyCode name of the key held (0.5s) while looking at a placed Shipyard or Assembly Station to pack it back into your inventory. The normal E/Craft interaction is untouched.");
        }
    }
}
