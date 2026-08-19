using System;
using System.Collections;
using UnityEngine;
using WorldsAdriftReborn.Config;
using WorldsAdriftRebornGameServer.Multiplayer.Config;

namespace WorldsAdriftReborn.Patching.Dynamic.LandingScreen
{
    /// <summary>
    /// Fetches the operator's welcome message from our own login server, so the
    /// text on the splash page can be edited from the admin panel instead of
    /// requiring a mod rebuild and a patcher release for every wording change.
    ///
    /// WHY THE FETCH CANNOT BE IN THE SCREEN'S OWN CODE PATH. The splash screen
    /// is the first thing a player sees, and a screen that waits on a network
    /// round trip before it draws is a screen that hangs for as long as the
    /// server takes to answer - or for the full TCP timeout when the server is
    /// down, which is the case this has to survive. So nothing here ever blocks:
    /// the request is started at plugin load, seconds before the splash screen
    /// exists, and whatever has landed by the time the screen asks is what it
    /// gets. If nothing has landed the screen draws immediately with the baked
    /// default and this quietly upgrades it in place when the answer arrives.
    ///
    /// That ordering is not an optimisation, it is the whole design. The
    /// alternative - screen asks, then waits - has no failure mode that is not
    /// "the game appears frozen".
    ///
    /// WHY UnityWebRequest AND NOT WWW. Both exist in Unity 5.6 and the game uses
    /// both. WWW cannot report an HTTP status, so a 404 HTML error page reads as
    /// a successful fetch whose body is markup, and that markup would land on the
    /// parchment. UnityWebRequest gives us responseCode, so a non-200 is refused
    /// and the default stands.
    /// </summary>
    internal class WelcomeMessageFetcher : MonoBehaviour
    {
        /// <summary>
        /// The most recent usable message from the server, or null if none has
        /// arrived. Read by SplashScreen_Patch; never blank, because
        /// WelcomeMessagePolicy refuses a blank body.
        /// </summary>
        internal static string Fetched { get; private set; }

        /// <summary>
        /// Raised when a message lands AFTER the screen has already drawn, so the
        /// live screen can redraw itself rather than showing the default until
        /// the player restarts. Never raised before the first screen exists,
        /// because on a healthy server the answer beats it there.
        /// </summary>
        internal static event Action Arrived;

        /// <summary>
        /// The text the screen should render right now: the server's if it has
        /// answered, the baked default otherwise. Always safe to assign.
        /// </summary>
        internal static string Current()
        {
            return WelcomeMessagePolicy.Choose(Fetched);
        }

        /// <summary>
        /// How long to wait before giving up.
        ///
        /// Short on purpose. Nothing depends on this succeeding - the default is
        /// already on screen - so a long timeout buys nothing and holds a socket
        /// open through the whole main menu.
        /// </summary>
        private const int TimeoutSeconds = 8;

        private void Start()
        {
            StartCoroutine(Fetch());
        }

        private IEnumerator Fetch()
        {
            string url;
            try
            {
                url = ModSettings.WelcomeMessageUrl();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[WAReborn] could not work out the welcome-message URL, "
                    + "the splash page keeps its built-in text: " + e.Message);
                yield break;
            }

            UnityEngine.Networking.UnityWebRequest request =
                UnityEngine.Networking.UnityWebRequest.Get(url);
            request.timeout = TimeoutSeconds;

            yield return request.Send();

            // isError covers connection failures; responseCode covers a server
            // that answered with an error PAGE, which is the case that would
            // otherwise put HTML on the parchment.
            if (request.isError || request.responseCode != 200)
            {
                Debug.Log("[WAReborn] welcome message not available from " + url
                    + " (" + (request.isError ? request.error : "HTTP " + request.responseCode)
                    + "); the splash page keeps its built-in text.");
                yield break;
            }

            string message = null;
            try
            {
                message = ReadMessage(request.downloadHandler.text);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[WAReborn] the welcome message from " + url
                    + " could not be read, keeping the built-in text: " + e.Message);
                yield break;
            }

            if (!WelcomeMessagePolicy.IsUsable(message))
            {
                Debug.Log("[WAReborn] the server returned an empty welcome message; "
                    + "the splash page keeps its built-in text.");
                yield break;
            }

            Fetched = message;
            Debug.Log("[WAReborn] welcome message loaded from the server ("
                + WelcomeMessagePolicy.Normalize(message).Length + " characters).");

            Action arrived = Arrived;
            if (arrived != null)
            {
                try
                {
                    arrived();
                }
                catch (Exception e)
                {
                    Debug.LogError("[WAReborn] could not refresh the splash page with the "
                        + "welcome message that just arrived: " + e);
                }
            }
        }

        /// <summary>
        /// Pulls "message" out of the server's JSON.
        ///
        /// Newtonsoft rather than a hand-rolled scan because the message is
        /// multi-line free text an operator types into a textarea: it is full of
        /// \n escapes and may contain quotes and backslashes, and every one of
        /// those is a way a substring search gets it wrong. The library is
        /// already in the game's Managed folder and already referenced here.
        /// </summary>
        private static string ReadMessage(string body)
        {
            if (string.IsNullOrEmpty(body))
            {
                return null;
            }

            Newtonsoft.Json.Linq.JObject parsed = Newtonsoft.Json.Linq.JObject.Parse(body);
            Newtonsoft.Json.Linq.JToken message = parsed["message"];
            return message == null ? null : message.ToString();
        }
    }
}
