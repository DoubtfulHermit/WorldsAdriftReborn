using System;
using BepInEx;
using System.Reflection;
using UnityEngine;
using HarmonyLib;
using WorldsAdriftReborn.Config;
using System.Windows.Forms;
using System.IO;
using System.Linq;

namespace WorldsAdriftReborn
{
    [BepInPlugin("com.WAR.WorldsAdriftReborn", "WorldsAdriftReborn", "0.0.1")]
    internal class WorldsAdriftReborn : BaseUnityPlugin
    {
        private void Awake()
        {
            // Adapted from: https://github.com/ManlyMarco/KK_GamepadSupport/blob/master/Core_GamepadSupport/GamepadSupportPlugin.cs
            try
            {
                // NEED to load the native dll BEFORE any class with DllImport is touched or the dll won't be found
                DependencyLoader.LoadDependencies();

                // Verify game assembly compatibility
                Assembly assembly = AppDomain.CurrentDomain.GetAssemblies().SingleOrDefault(innerAssembly => innerAssembly.GetName().Name == "Assembly-CSharp");
                string moduleVersionId = assembly.ManifestModule.ModuleVersionId.ToString();
                string expectedModuleVersionId = "70f2ca59-e029-4973-b4e9-e0098e0ad02d";
                if (moduleVersionId != expectedModuleVersionId)
                {
                    throw new IOException(
                        $"Referenced {assembly.ManifestModule.Name} assembly does match the expected ModuleVersionId, most likely a incompatible version of Worlds Adrift is used. " +
                        $"Please refer to the WorldsAdriftReborn readme on how to obtain the correct version of the game.\n" +
                        $"(Provided ModuleVersionId: {moduleVersionId} does not match the expected ModuleVersionId: {expectedModuleVersionId})"
                    );
                }
                
            }
            catch (Exception ex)
            {
                string errorMsg = $"WorldsAdriftReborn plugin failed to load:\n{ex.Message}";

                Debug.LogError(errorMsg);

                MessageBox.Show(
                    errorMsg, 
                    "WorldsAdriftReborn Error",
                    MessageBoxButtons.OK, 
                    MessageBoxIcon.Error
                );
                System.Diagnostics.Process.GetCurrentProcess().Kill();
                return;
            }

            ModSettings.InitConfig();
            QuietenOrdinaryLogStackTraces();
            InitPatches();

            // Disables cameras and audio listeners on mirrored remote player rigs;
            // see RemoteRigSweeper for why Harmony patches cannot cover this.
            gameObject.AddComponent<Patching.Multiplayer.RemoteRigSweeper>();
            gameObject.AddComponent<Patching.Multiplayer.LocalPlayerTelemetry>();

            // F9 disconnects in-process. The game's own ESC -> Logout is dead
            // here (LogoutBehaviour.RequestLogout NREs), and this is the only
            // way to test whether ENet survives deinit/init inside one process,
            // which the whole reconnect design depends on. Diagnostic only.
            gameObject.AddComponent<Patching.Multiplayer.ReconnectProbe>();

            // F10 recovers the LOCAL player to Haven's spawn, client side, by the
            // same path the server's teleport uses (set transform + PlayerMove.Respawn).
            // This is the MANUAL replacement for the server's old automatic
            // fall-yank, which is now off by default so a ship flying below the
            // island is not snatched home. See ManualRecoveryProbe.
            gameObject.AddComponent<Patching.Multiplayer.ManualRecoveryProbe>();

            // Reports which IDetermineOriginStrategy is live and what OffsetOrigin
            // is. The choice is scene-serialized so the decompile cannot answer it,
            // and every candidate behaves identically while the island sits at the
            // world origin - so this must be read BEFORE the island moves to 17 km.
            // Read-only; F10 forces a report, but it also reports by itself.
            gameObject.AddComponent<Patching.Multiplayer.OriginStrategyProbe>();

            // Always-on stutter attribution: one grep-able "[WAR][perf] spike"
            // line per frame hitch naming its cause (entity adds, GC, SpatialOS
            // slice), a 30 s heartbeat, and the activation timestamp that proves
            // whether the loading barrier held. Allocation-free between spikes.
            gameObject.AddComponent<Patching.Performance.StutterProbe>();

            // Hold X (configurable: [Interact] Interact_StationPickupKey) while
            // looking at a placed Shipyard / Assembly Station to send the server a
            // 1211 PickUp and pack it back into inventory - the client half of the
            // non-retail station-pickup extension. The E/Craft flow is untouched.
            gameObject.AddComponent<Patching.Interactions.StationPickupSender>();

            // Ground-truth orientation probe: logs the RENDERED hull/helm/deck
            // world rotations every 5 s ("[WAR][orient]"). Exists because every
            // server-side orientation check ran against our own reimplementation
            // of the hull mesh - if that decode is axis-swapped relative to what
            // the client actually draws, only the rendered numbers can show it.
            gameObject.AddComponent<Patching.Flight.OrientationProbe>();
        }

        /// <summary>
        /// Stops Unity attaching a full stack trace to every ORDINARY log line.
        ///
        /// Measured on a real session: the log was 92 MB / 1,559,219 lines, and
        /// 1,014,755 of those lines - 65% of the file - were "   at ..." frames
        /// hanging off routine BossaECS informational logs, roughly 200 frames
        /// per line. BossaECS.Core.System.SystemBase.TryExecute alone appeared
        /// 341,810 times. That dwarfs even the 60,427-entry NRE loop that
        /// ChararacterDrunk_Patch just fixed.
        ///
        /// This is not cosmetic. Every one of those frames is walked, formatted
        /// and written synchronously on the main thread. The same class of
        /// mistake on the server side - 1,207 lines/second through journald on
        /// the ENet thread - is what made two-player sessions desync, so the
        /// cost of log volume in this project is measured, not theoretical.
        ///
        /// SAFETY: this is per-LogType and only touches LogType.Log, i.e. plain
        /// Debug.Log. Warnings, errors, exceptions and asserts keep their full
        /// traces, so nothing that matters for diagnosis is lost - the traces
        /// being removed are the ones attached to lines like "system executed".
        /// </summary>
        private static void QuietenOrdinaryLogStackTraces()
        {
            try
            {
                // Fully qualified: this file also imports System.Windows.Forms,
                // which has its own Application.
                UnityEngine.Application.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
                Debug.Log("[WAReborn] stack traces disabled for ordinary Debug.Log lines "
                    + "(65% of a real session's log was such frames). Warnings, errors and "
                    + "exceptions keep theirs.");
            }
            catch (Exception e)
            {
                // Never worth failing the mod's load over a logging preference.
                Debug.LogWarning("[WAReborn] could not set the log stack-trace type: " + e.Message);
            }
        }

        /// <summary>
        /// Applies every patch class in this assembly, one at a time, so that a
        /// single broken class cannot take the rest of the mod down with it.
        ///
        /// This used to be one <c>Harmony.CreateAndPatchAll(assembly)</c> call.
        /// Harmony processes each class through a PatchClassProcessor and lets
        /// an exception from any of them propagate out of PatchAll, so the
        /// classes it had not reached yet were never patched - silently, because
        /// the catch below turned it into a single log line that reads like a
        /// warning rather than "most of the mod did not load".
        ///
        /// It was not hypothetical. A real session log shows
        ///
        ///     AccessTools.Method: Could not find method for type WAConfig and
        ///     name GetOrDefault and parameters (string, string)
        ///     Unhandled exception occurred while patching the game: ...
        ///         WAConfig_Patch+GetOrDefault_String_WithFallback::GetTargetMethod()
        ///
        /// and no "Patching completed successfully" line anywhere in the file.
        /// Everything Harmony had not yet processed was dead, and which patches
        /// those were depended on nothing more meaningful than the order
        /// Assembly.GetTypes() happened to return.
        ///
        /// The lookup that threw is fixed too (see WAConfig_Patch), but the
        /// structural problem is worth fixing on its own: a patch that cannot
        /// find its target should cost that one patch, and it should say so at
        /// ERROR level with the class name in it.
        /// </summary>
        private static void InitPatches()
        {
            Debug.Log("Patching Worlds Adrift...");
            Debug.Log("Applying patches from WorldsAdriftReborn 0.0.1");

            Harmony harmony = new Harmony("com.WAR.com");
            int patchedClasses = 0;
            int patchedMethods = 0;
            int failedClasses = 0;

            foreach (Type type in AccessTools.GetTypesFromAssembly(Assembly.GetExecutingAssembly()))
            {
                try
                {
                    var patched = harmony.CreateClassProcessor(type).Patch();
                    if (patched != null && patched.Count > 0)
                    {
                        patchedClasses++;
                        patchedMethods += patched.Count;
                    }
                }
                catch (Exception e)
                {
                    failedClasses++;
                    // LogError, not Log: this is a feature of the mod not
                    // loading. The old code logged the whole assembly's failure
                    // at Info level and it went unnoticed for an unknown number
                    // of releases.
                    Debug.LogError("[WAReborn] patch class " + type.FullName
                        + " FAILED and was skipped; the rest of the mod still applied. " + e);
                }
            }

            if (failedClasses > 0)
            {
                Debug.LogError("[WAReborn] patching finished with " + failedClasses
                    + " FAILED patch class(es). " + patchedMethods + " method(s) patched across "
                    + patchedClasses + " class(es).");
            }
            else
            {
                Debug.Log("Patching completed successfully: " + patchedMethods
                    + " method(s) patched across " + patchedClasses + " class(es).");
            }
        }
    }
}
