using System;
using System.Reflection;
using UnityEngine;

namespace WorldsAdriftReborn.Patching.Multiplayer
{
    /*
     * F9 disconnects from the game server, in-process.
     *
     * This exists because the game's own ESC -> Logout button is dead here:
     * LogoutBehaviour.RequestLogout throws a NullReferenceException before it
     * can reach SpatialOS.Disconnect(), so the countdown panel never appears.
     *
     * What it is for: the whole reconnect design rests on one untested
     * assumption - that ENet_Deinitialize followed by ENet_Initialize works
     * inside a single Wine process. Nothing else can answer that; a fresh
     * launch tests a fresh process and proves nothing. Pressing F9 and then
     * re-entering the world exercises exactly that cycle:
     *
     *     SpatialOS.Disconnect()
     *       -> ConnectionLifecycle.Dispose()
     *       -> ~Connection -> ENet_Deinitialize
     *     ... character select ...
     *     Enter World
     *       -> Locator::ConnectAsync -> ENet_Initialize -> new ENetHost
     *
     * If the second connect reports SUCCESS in CoreSdk_OutputLog.txt, the
     * reconnect work is a confident build. If it reports FAILED, the ENet
     * lifetime needs a refcount (or we simply never deinitialize) before any
     * of it is worth writing.
     *
     * SpatialOS.Disconnect sets the reason "Disconnect was called by the user.",
     * which InGameState routes to a SILENT return to character select rather
     * than the RETRY/QUIT dialog. That is correct and wanted here - this probe
     * tests the ENet lifecycle, not the error UI.
     *
     * Diagnostic only. Resolved by reflection so a missing or renamed method
     * logs instead of breaking the mod's load.
     */
    internal class ReconnectProbe : MonoBehaviour
    {
        private const KeyCode DisconnectKey = KeyCode.F9;

        private void Update()
        {
            if (!Input.GetKeyDown(DisconnectKey))
            {
                return;
            }

            try
            {
                Type spatialOs = AccessToolsTypeByName("Improbable.Unity.Core.SpatialOS");
                if (spatialOs == null)
                {
                    Debug.LogWarning("[WAReborn] reconnect probe: SpatialOS type not found.");
                    return;
                }

                PropertyInfo isConnected = spatialOs.GetProperty("IsConnected",
                    BindingFlags.Public | BindingFlags.Static);
                object connected = isConnected != null ? isConnected.GetValue(spatialOs, null) : null;

                MethodInfo disconnect = spatialOs.GetMethod("Disconnect",
                    BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
                if (disconnect == null)
                {
                    Debug.LogWarning("[WAReborn] reconnect probe: SpatialOS.Disconnect() not found.");
                    return;
                }

                Debug.Log("[WAReborn] reconnect probe: F9 pressed, IsConnected=" + connected
                    + " - calling SpatialOS.Disconnect(). Watch CoreSdk_OutputLog.txt for the"
                    + " next 'Trying to connect' after you re-enter the world.");
                disconnect.Invoke(null, null);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[WAReborn] reconnect probe: disconnect threw: " + e);
            }
        }

        private static Type AccessToolsTypeByName(string fullName)
        {
            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type t = asm.GetType(fullName, false);
                if (t != null)
                {
                    return t;
                }
            }
            return null;
        }
    }
}
