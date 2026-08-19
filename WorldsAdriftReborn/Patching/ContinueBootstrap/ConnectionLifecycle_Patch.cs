using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace WorldsAdriftReborn.Patching.Dynamic.ContinueBootstrap
{
    /// <summary>
    /// Keeps the client on the LOCATOR connect path, which is the only one our
    /// CoreSdk actually implements.
    ///
    /// THE BUG THIS FIXES. Pressing PLAY loaded forever, with no packet ever
    /// leaving the machine and nothing at all in the game server's journal - not
    /// even a rejected connection. The client logged
    /// "!SpatialOS.IsConnected - not creating ECS!" and then sat at 120 fps with
    /// ents=0/ops=0 while the world never arrived.
    ///
    /// The SDK picks between two connect paths in
    /// acs/Improbable.Unity.Core/ConnectionLifecycle.cs:
    ///
    ///     private static bool ShouldGetDeploymentList()
    ///     {
    ///         return !string.IsNullOrEmpty(SpatialOS.Configuration.LoginToken)
    ///             || !string.IsNullOrEmpty(SpatialOS.Configuration.SteamToken);
    ///     }
    ///
    /// True takes the LOCATOR path - GetDeploymentListAsync, then
    /// WorkerProtocol_Locator_ConnectAsync. That is the path
    /// WorldsAdriftRebornCoreSdk implements: Locator::ConnectAsync calls
    /// ENet_Initialize, builds an ENetHost, reads the configured game-server port
    /// and hands a real client into ConnectionFuture.
    ///
    /// False takes the RECEPTIONIST path, straight to
    /// WorkerProtocol_ConnectAsync - which in Exports.cpp is still the stub it
    /// has always been:
    ///
    ///     return new ConnectionFuture(hostname, port, parameters, NULL);
    ///                                                             ^^^^
    ///
    /// and Connection::Connection only dials when its ENetHost is non-null:
    ///
    ///     if (this->client != NULL && this->hostname != NULL && this->port != 0) {
    ///         Logger::Debug("Trying to connect to game server at " + ...);
    ///
    /// With a NULL host that whole block is skipped. No ENet_Connect, no socket,
    /// no log line, no error - the ConnectionFuture just resolves to a Connection
    /// whose peer is NULL, IsConnected() returns false, and the client waits for
    /// a world that was never asked for. A silent no-op is the worst possible
    /// shape for this failure and it is why the symptom looked like a network or
    /// server fault when nothing had left the box.
    ///
    /// HOW WE FELL OFF THE LOCATOR PATH. Both of those tokens used to be
    /// populated for free. LobbySystem.ConnectToGameServer
    /// (acs/Travellers.UI.Login/LobbySystem.cs:138-154) reads:
    ///
    ///     if (SteamChecker.IsUsingSteam) {
    ///         LoginMetadata loginMetadata = LoginMetadata.SteamMetadata();
    ///         if (WAConfig.Get&lt;bool&gt;(ConfigKeys.SendSteamToken))
    ///             _improbableBootstrap.WorkerConfigurationData.Networking.SteamToken
    ///                 = loginMetadata.Credentials.ToUpper();
    ///         CompleteConnect(isSpectator, loginMetadata, userName);
    ///     } else {
    ///         LoginMetadata metadata = LoginMetadata.TestingMetadata(userName);
    ///         CompleteConnect(isSpectator, metadata, userName);
    ///     }
    ///
    /// The Steam branch is the ONLY place anything ever assigns SteamToken, and
    /// LoginToken has always been empty here - we have no Improbable login
    /// service. So the Locator path was being selected by a side effect of the
    /// Steam branch, not by anything that meant to select it.
    ///
    /// Turning Steam off (WAConfig_Patch.ForcedBool forces Bootstrap.UseSteam
    /// false, so a delisted 2019 game stops requiring a running store client)
    /// correctly moved the client to the else branch. That change reasoned about
    /// the metadata it swaps - UserId, Credentials, Platform - and concluded
    /// nothing reads them, which is true. What it missed is that the branch does
    /// not only build different metadata: it is also the only writer of
    /// SteamToken, and SteamToken is not merely metadata. It is half the
    /// predicate above. Losing it silently flipped the connect path.
    ///
    /// WHY PATCH HERE AND NOT PUT A TOKEN BACK. Faking a Steam ticket just to
    /// keep a predicate true would leave the real invariant unstated and one
    /// refactor away from breaking again the same way. The invariant is not
    /// "we have credentials"; it is "our CoreSdk speaks Locator and nothing
    /// else". Stating that directly is what makes this fix hold no matter what
    /// happens to Steam, to login tokens, or to which metadata branch the lobby
    /// takes. Our Locator ignores the credentials in LocatorParameters entirely
    /// (Locator::Locator keeps only the hostname), so there is nothing for a
    /// token to be right or wrong about.
    ///
    /// The deployment list it now asks for is served by our own stub, which
    /// returns exactly one deployment, so OnDeploymentListReceived picks it
    /// without a chooser and the connect proceeds.
    ///
    /// ConnectionLifecycle is internal in Assembly-CSharp and the publicizer
    /// leaves it that way, so the type cannot be named in a
    /// [HarmonyPatch(typeof(...))] attribute from here - hence the reflected
    /// target, the same shape WAConfig_Patch uses for WAConfig. If the type or
    /// the method is ever renamed this throws out of GetTargetMethod, which
    /// costs this one patch class and nothing else, and says so in the log
    /// rather than quietly going back to the silent no-connect.
    /// </summary>
    [HarmonyPatch()]
    internal static class ConnectionLifecycle_Patch
    {
        private static bool announced;

        [HarmonyTargetMethod]
        public static MethodBase GetTargetMethod()
        {
            Type lifecycle = AccessTools.TypeByName("Improbable.Unity.Core.ConnectionLifecycle");
            if (lifecycle == null)
            {
                throw new InvalidOperationException(
                    "[WAReborn] Improbable.Unity.Core.ConnectionLifecycle not found; cannot force "
                    + "the Locator connect path. PLAY would hang forever without this.");
            }

            MethodBase target = AccessTools.Method(lifecycle, "ShouldGetDeploymentList");
            if (target == null)
            {
                throw new InvalidOperationException(
                    "[WAReborn] ConnectionLifecycle.ShouldGetDeploymentList not found; cannot force "
                    + "the Locator connect path. PLAY would hang forever without this.");
            }

            return target;
        }

        [HarmonyPrefix]
        public static bool ShouldGetDeploymentList_Prefix(ref bool __result)
        {
            // Once, not per connect: this is the line that tells us from a log
            // which of the two paths a session took, and the silent version of
            // this decision is exactly what made the outage hard to see.
            if (!announced)
            {
                announced = true;
                Debug.Log("[WAReborn] connect path: LOCATOR (forced). The receptionist path "
                    + "in WorkerProtocol_ConnectAsync never opens a socket, so it is never "
                    + "the right answer here regardless of which credentials exist.");
            }

            __result = true;
            return false;
        }
    }
}
