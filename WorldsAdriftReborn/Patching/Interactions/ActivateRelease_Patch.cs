using System;
using System.Collections.Generic;
using System.Reflection;
using Bossa.Prototype.Character.Observer;
using Bossa.Travellers.Controls;
using Bossa.Travellers.Interact;
using HarmonyLib;
using Improbable;
using UnityEngine;

namespace WorldsAdriftReborn.Patching.Interactions
{
    /// <summary>
    /// Completes a wire lifecycle the retail client left implicit. The client sends
    /// 1211 InteractWithObject when an Activate completes, but unlike exclusive UI
    /// interactions it sends no event when the Interact key is released. A server
    /// therefore cannot distinguish a duplicate completion from the next press.
    ///
    /// Record only successfully issued Activate interactions and emit the protocol's
    /// native ReleaseInteraction on the matching physical key-up. Man/Inventory/Craft
    /// are deliberately untouched. This is an input edge, not a debounce or timer.
    /// </summary>
    internal static class ActivateReleaseLifecycle
    {
        private static readonly FieldInfo InputField =
            AccessTools.Field(typeof(InteractAgentObserver), "_input");
        private static readonly FieldInfo WriterField =
            AccessTools.Field(typeof(InteractAgentObserver), "interactWriter");
        private static readonly Dictionary<InteractAgentObserver, EntityId> Active =
            new Dictionary<InteractAgentObserver, EntityId>();
        private static bool _failureLogged;

        internal static bool Ready => InputField != null && WriterField != null;

        internal static void Began(InteractAgentObserver observer, EntityId target, InteractVerb verb)
        {
            if (observer != null && verb == InteractVerb.Activate && target.IsValid())
            {
                Active[observer] = target;
            }
        }

        internal static void Tick(InteractAgentObserver observer)
        {
            if (observer == null || !Active.TryGetValue(observer, out EntityId target)) return;
            try
            {
                var input = InputField.GetValue(observer) as InputSink;
                if (input == null || !input.GetButtonUp(InputButtons.Interact)) return;

                var writer = WriterField.GetValue(observer) as InteractAgentStateWriter;
                if (writer == null) throw new InvalidOperationException("1211 writer is unavailable");
                writer.Update.TriggerReleaseInteraction(target).FinishAndSend();
                Active.Remove(observer);
                Debug.Log("[WAR][interact] Activate lifecycle released target " + target.Id + ".");
            }
            catch (Exception e)
            {
                Active.Remove(observer);
                if (!_failureLogged)
                {
                    _failureLogged = true;
                    Debug.LogWarning("[WAR][interact] Activate release bridge disabled after error: " + e);
                }
            }
        }

        internal static void Forget(InteractAgentObserver observer)
        {
            if (observer != null) Active.Remove(observer);
        }
    }

    [HarmonyPatch(typeof(InteractAgentObserver), "IssueInteraction")]
    internal static class ActivateIssueLifecycle_Patch
    {
        private static bool Prepare()
        {
            if (ActivateReleaseLifecycle.Ready) return true;
            Debug.LogWarning("[WAR][interact] Activate release fields were not resolvable; patch skipped.");
            return false;
        }

        private static void Postfix(InteractAgentObserver __instance, EntityId entityId, InteractVerb verb) =>
            ActivateReleaseLifecycle.Began(__instance, entityId, verb);
    }

    [HarmonyPatch(typeof(InteractAgentObserver), "Update")]
    internal static class ActivateKeyUpLifecycle_Patch
    {
        private static bool Prepare() => ActivateReleaseLifecycle.Ready;

        private static void Postfix(InteractAgentObserver __instance) =>
            ActivateReleaseLifecycle.Tick(__instance);
    }

    [HarmonyPatch(typeof(InteractAgentObserver), "OnDisable")]
    internal static class ActivateDisableLifecycle_Patch
    {
        private static void Postfix(InteractAgentObserver __instance) =>
            ActivateReleaseLifecycle.Forget(__instance);
    }
}
