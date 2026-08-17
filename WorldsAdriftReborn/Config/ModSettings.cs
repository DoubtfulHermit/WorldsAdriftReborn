using BepInEx;
using BepInEx.Configuration;
using System.Runtime.InteropServices;

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
        public static ConfigEntry<string> NTPServerUrl { get; set; }
        public static ConfigEntry<string> localAssetPath { get; set; }
        public static ConfigEntry<string> gameServerHost { get; set; }
        public static ConfigEntry<string> gameServerPort { get; set; }
        public static ConfigEntry<int> perfSpikeThresholdMs { get; set; }
        public static ConfigEntry<string> stationPickupKey { get; set; }
        public static ConfigEntry<float> impostorFollowAngleDegrees { get; set; }
        public static ConfigEntry<float> impostorRebakeAngleDegrees { get; set; }
        public static ConfigEntry<float> impostorRebakeSeconds { get; set; }

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
            restServerDeploymentUrl = modConfig.Bind<string>("REST",
                                                    "REST_ServerDeploymentUrl",
                                                    "http://127.0.0.1:8080/deploymentStatus",
                                                    "Sets the URL for the REST server that the game queries once the main menu is reached. It is the endpoint where server status informations are retrieved from.");

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

            impostorFollowAngleDegrees = modConfig.Bind<float>("Impostors",
                                                    "Impostor_FollowAngleDegrees",
                                                    0f,
                                                    "How far (degrees) a distant island's impostor billboard may turn to face you before it holds still, i.e. ImpostersHandler.minAngleToStopLookAtCamera. Retail ships 30 against a 2.5 degree re-bake trigger, so a late re-bake lets the island visibly swing. 0 means 'follow the re-bake angle', which bounds the swing to what a timely re-bake already produces. Set 30 to restore retail exactly. Retail range is 0..45.");
            impostorRebakeAngleDegrees = modConfig.Bind<float>("Impostors",
                                                    "Impostor_RebakeAngleDegrees",
                                                    0f,
                                                    "Overrides ImposterController.errorCameraAngle on island impostors: how far you may move around an island before its billboard texture is re-rendered. 0 keeps retail's 2.5. Smaller is sharper and costs more bakes; every bake is two full camera renders of the island.");
            impostorRebakeSeconds = modConfig.Bind<float>("Impostors",
                                                    "Impostor_RebakeSeconds",
                                                    0f,
                                                    "Overrides ImposterController.timeInterval on island impostors, the backstop re-bake that fires regardless of camera movement. 0 keeps retail's 10 seconds.");
        }
    }
}
