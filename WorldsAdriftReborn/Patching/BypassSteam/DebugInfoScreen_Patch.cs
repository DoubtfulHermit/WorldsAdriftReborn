using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using Travellers.UI.DebugDisplay;
using UnityEngine;

namespace WorldsAdriftReborn.Patching.Dynamic.BypassSteam
{
    /// <summary>
    /// Shows the build number without waiting for Steam.
    ///
    /// The original is a coroutine with an unbounded wait on a Steam flag
    /// (acs/Travellers.UI.DebugDisplay/DebugInfoScreen.cs:74-81):
    ///
    ///     private IEnumerator SetVersionAndBranchDisplay()
    ///     {
    ///         while (!SteamManager.Initialized) { yield return null; }
    ///         _version.SetText(GetBuildDisplayState().GetBuildNumber());
    ///     }
    ///
    /// With Steam bypassed, SteamManager.Initialized is deliberately false
    /// forever - that is what keeps UGCManager, SteamWorkshopFileList and
    /// CreateAuthRequestPayload from making Steam calls of their own - so this
    /// loop would spin for the life of the process and the version label would
    /// stay blank. Small, but it is a regression introduced by the bypass rather
    /// than one the bypass inherited, and a blank field with no error is the
    /// kind of thing nobody diagnoses.
    ///
    /// GetBuildDisplayState() itself is safe to call: it reads
    /// SteamChecker.IsSteamBranchPTS(), which with Bootstrap.UseSteam false
    /// returns an empty branch name without going near SteamApps, and the
    /// comparison against PublicTestServerSteamBranchName ("beta") is then
    /// false. That is already the answer the player gets today - their log shows
    /// "[SteamChecker] ... it is " with nothing after it - so the label reads
    /// exactly as it reads now.
    /// </summary>
    [HarmonyPatch(typeof(DebugInfoScreen))]
    internal static class DebugInfoScreen_Patch
    {
        [HarmonyPrefix]
        [HarmonyPatch("SetVersionAndBranchDisplay")]
        public static bool SetVersionAndBranchDisplay_Prefix(DebugInfoScreen __instance,
                                                             ref IEnumerator __result)
        {
            __result = SetVersionNow(__instance);
            return false;
        }

        private static IEnumerator SetVersionNow(DebugInfoScreen screen)
        {
            // One frame, to keep the original's shape. The caller starts this
            // from ProtectedInit, so the serialized fields are already assigned,
            // but yielding once costs nothing and keeps this a real coroutine.
            yield return null;

            // The work is in its own method because a C# iterator cannot have a
            // yield inside a try/catch, and this needs the catch.
            ApplyVersionText(screen);
        }

        private static void ApplyVersionText(DebugInfoScreen screen)
        {
            try
            {
                MethodInfo getState = AccessTools.Method(typeof(DebugInfoScreen),
                    "GetBuildDisplayState");
                FieldInfo versionField = AccessTools.Field(typeof(DebugInfoScreen), "_version");

                if (getState == null || versionField == null)
                {
                    Debug.LogWarning("[WAReborn] DebugInfoScreen has changed shape "
                        + "(GetBuildDisplayState or _version missing); the build-number label "
                        + "will stay blank.");
                    return;
                }

                BuildInfoDisplayState state = getState.Invoke(screen, null) as BuildInfoDisplayState;
                TextStylerTextMeshPro version = versionField.GetValue(screen) as TextStylerTextMeshPro;

                if (state == null || version == null)
                {
                    Debug.LogWarning("[WAReborn] DebugInfoScreen build state or version label was "
                        + "null; the build-number label will stay blank.");
                    return;
                }

                version.SetText(state.GetBuildNumber());
            }
            catch (Exception e)
            {
                Debug.LogWarning("[WAReborn] could not set the build-number label: " + e);
            }
        }
    }
}
