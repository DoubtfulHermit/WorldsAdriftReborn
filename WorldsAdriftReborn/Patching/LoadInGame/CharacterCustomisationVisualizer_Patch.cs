using Assets.Scripts.Player;
using Bossa.Travellers.Player;
using HarmonyLib;
using Improbable.Collections;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace WorldsAdriftReborn.Patching.LoadInGame
{
    [HarmonyPatch(typeof(CharacterCustomisationVisualizer))]
    internal class CharacterCustomisationVisualizer_Patch
    {
        private static bool appearancePublished;

        /// <summary>Re-arms the one-shot publish; called when re-entering the world.</summary>
        internal static void ResetAppearancePublished()
        {
            appearancePublished = false;
        }

        /*
         * Appearance flow (see docs/multiplayer.md in the server repo):
         *
         * LOCAL rig ("Traveller@Player <id>"): inject the locally saved character
         * data if missing (original behaviour), and PUBLISH it once as a normal
         * PlayerPropertiesState (1088) update. The game itself never writes 1088
         * client-side; the component Impl implements Writer and its Send() has no
         * authority gate, so the update reaches the server, which records it
         * (PlayerPropertiesState_Handler) and relays it to other clients live.
         *
         * REMOTE rig ("Traveller <id>"): NEVER inject local data - that is what
         * made every avatar look like the local player. If the real key is
         * missing (seeded defaults, owner not yet published), skip the callback;
         * it fires again when the relayed/seeded real data arrives.
         */
        [HarmonyPrefix]
        [HarmonyPatch(typeof(CharacterCustomisationVisualizer), "OnCustomisationUpdated")]
        public static bool OnCustomisationUpdated_Prefix( CharacterCustomisationVisualizer __instance, ref Map<string, string> obj )
        {
            bool isLocalRig = __instance.transform.root.name.StartsWith("Traveller@Player");

            if (!isLocalRig)
            {
                if (!obj.ContainsKey("bossaNetCharacterData"))
                {
                    Debug.Log("[WAReborn] remote rig customisation without character data yet, waiting for the owner's publish.");
                    return false;
                }

                Debug.Log("[WAReborn] applying published appearance to remote rig '" + __instance.transform.root.name + "'.");
                return true;
            }

            if (!obj.ContainsKey("bossaNetCharacterData"))
            {
                JObject o = (JObject)JToken.FromObject(CharacterDataLoader.Load().ToArray()[0]);
                obj.Add("bossaNetCharacterData", o.ToString());
            }

            PublishOwnAppearance(__instance, obj);
            return true;
        }

        private static void PublishOwnAppearance( CharacterCustomisationVisualizer visualizer, Map<string, string> customisation )
        {
            if (appearancePublished)
            {
                return;
            }

            try
            {
                object reader = AccessTools.Field(typeof(CharacterCustomisationVisualizer), "_properties")?.GetValue(visualizer);
                PlayerPropertiesState.Writer writer = reader as PlayerPropertiesState.Writer;
                if (writer == null)
                {
                    Debug.LogWarning("[WAReborn] cannot publish appearance: _properties is not a Writer (" + (reader == null ? "null" : reader.GetType().FullName) + ")");
                    return;
                }

                PlayerPropertiesState.Update update = new PlayerPropertiesState.Update();
                update.SetCustomisation(new Map<string, string>(customisation));
                writer.Send(update);

                appearancePublished = true;
                Debug.Log("[WAReborn] published own appearance (" + customisation.Count + " keys) via 1088 update.");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[WAReborn] appearance publish failed: " + e.Message);
            }
        }
    }
}
