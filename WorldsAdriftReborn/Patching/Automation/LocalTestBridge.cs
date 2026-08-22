using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Bossa.Travellers.Controls;
using HarmonyLib;
using Improbable;
using Improbable.Unity.Core;
using Travellers.UI.Login;
using UnityEngine;
using LandingScreenType = Travellers.UI.Login.LandingScreen;

namespace WorldsAdriftReborn.Patching.Automation
{
    /// <summary>
    /// Opt-in, loopback-only control seam for visual acceptance automation.
    ///
    /// The bridge deliberately translates semantic commands on Unity's main
    /// thread instead of injecting desktop input through Wayland/Wine. It is
    /// inert unless WAREBORN_TEST_BRIDGE=1 and a non-empty per-run token are
    /// both present. Normal players therefore do not open a listener.
    /// </summary>
    internal sealed class LocalTestBridge : MonoBehaviour
    {
        private const int DefaultPort = 47631;
        private const int MaxLineLength = 512;
        private const int MaxCommandsPerFrame = 8;

        private readonly Queue<PendingCommand> _pending = new Queue<PendingCommand>();
        private TcpListener _listener;
        private Thread _listenerThread;
        private volatile bool _running;
        private string _token;

        internal static bool ShouldStart()
        {
            return string.Equals(Environment.GetEnvironmentVariable("WAREBORN_TEST_BRIDGE"),
                "1", StringComparison.Ordinal);
        }

        private void Awake()
        {
            _token = Environment.GetEnvironmentVariable("WAREBORN_TEST_BRIDGE_TOKEN");
            if (string.IsNullOrEmpty(_token))
            {
                Debug.LogError("[WAR][test-bridge] requested without WAREBORN_TEST_BRIDGE_TOKEN; disabled.");
                Destroy(this);
                return;
            }

            int port = ParsePort(Environment.GetEnvironmentVariable("WAREBORN_TEST_BRIDGE_PORT"));
            try
            {
                _listener = new TcpListener(IPAddress.Loopback, port);
                _listener.Start(1);
                SyntheticInput.Enable();
                _running = true;
                _listenerThread = new Thread(ListenLoop)
                {
                    IsBackground = true,
                    Name = "WAReborn local test bridge"
                };
                _listenerThread.Start();
                Debug.Log("[WAR][test-bridge] listening on 127.0.0.1:" + port
                    + " (semantic commands; token required; local test mode only).");
            }
            catch (Exception e)
            {
                Debug.LogError("[WAR][test-bridge] failed to start: " + e);
                StopBridge();
                Destroy(this);
            }
        }

        private void Update()
        {
            SyntheticInput.Tick();
            for (int i = 0; i < MaxCommandsPerFrame; i++)
            {
                PendingCommand command;
                lock (_pending)
                {
                    if (_pending.Count == 0)
                        return;
                    command = _pending.Dequeue();
                }

                try
                {
                    if (command.Cancelled)
                    {
                        command.Response = Error("cancelled", "request timed out before execution");
                        continue;
                    }
                    command.Response = Execute(command.Command);
                }
                catch (Exception e)
                {
                    command.Response = Error("command_failed", e.GetType().Name + ": " + e.Message);
                }
                finally
                {
                    command.Completed.Set();
                }
            }
        }

        private string Execute(string command)
        {
            string[] parts = command.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return Error("bad_request", "command is empty");

            if (parts[0] == "input.tap" && parts.Length == 2)
            {
                InputButtons button;
                if (!TryParseEnum(parts[1], out button))
                    return Error("bad_button", parts[1]);
                SyntheticInput.Tap(button);
                return Ok(command);
            }
            if (parts[0] == "input.hold" && parts.Length == 3)
            {
                InputButtons button;
                bool held;
                if (!TryParseEnum(parts[1], out button))
                    return Error("bad_button", parts[1]);
                if (!bool.TryParse(parts[2], out held))
                    return Error("bad_value", parts[2]);
                SyntheticInput.Hold(button, held);
                return Ok(command);
            }
            if (parts[0] == "axis.set" && parts.Length == 3)
            {
                InputAxes axis;
                float value;
                if (!TryParseEnum(parts[1], out axis))
                    return Error("bad_axis", parts[1]);
                if (!float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                    || float.IsNaN(value) || float.IsInfinity(value) || value < -1f || value > 1f)
                    return Error("bad_value", parts[2]);
                SyntheticInput.SetAxis(axis, value);
                return Ok(command);
            }
            if (parts[0] == "axis.clear" && parts.Length == 2)
            {
                InputAxes axis;
                if (!TryParseEnum(parts[1], out axis))
                    return Error("bad_axis", parts[1]);
                SyntheticInput.ClearAxis(axis);
                return Ok(command);
            }
            if (parts[0] == "input.clear" && parts.Length == 1)
            {
                SyntheticInput.Clear();
                return Ok(command);
            }

            switch (command)
            {
                case "ping":
                    return Ok("pong");
                case "state":
                    return StateJson();
                case "menu.play":
                {
                    LandingScreenType screen = FindActive<LandingScreenType>();
                    if (screen == null)
                        return Error("wrong_state", "active LandingScreen not found");
                    screen.Play();
                    return Ok("menu.play");
                }
                case "menu.continue":
                {
                    SplashScreen screen = FindActive<SplashScreen>();
                    if (screen == null)
                        return Error("wrong_state", "active SplashScreen not found");
                    object welcome = AccessTools.Field(typeof(SplashScreen), "_welcomeParent").GetValue(screen);
                    GameObject welcomeRoot = welcome as GameObject;
                    string method = welcomeRoot != null && welcomeRoot.activeSelf
                        ? "ShowServerInformation"
                        : "OnSplashScreenButtonClicked";
                    AccessTools.Method(typeof(SplashScreen), method).Invoke(screen, null);
                    return Ok("menu.continue");
                }
                case "menu.enter-world":
                {
                    CharacterSelectionScreen screen = FindActive<CharacterSelectionScreen>();
                    if (screen == null)
                        return Error("wrong_state", "active CharacterSelectionScreen not found");
                    screen.EnterWorld();
                    return Ok("menu.enter-world");
                }
                default:
                    return Error("unknown_command", command);
            }
        }

        private string StateJson()
        {
            SplashScreen splash = FindActive<SplashScreen>();
            GameObject splashWelcome = splash == null ? null
                : AccessTools.Field(typeof(SplashScreen), "_welcomeParent").GetValue(splash) as GameObject;
            bool localPlayer = LocalPlayer.Exists;
            bool connected = global::Improbable.Unity.Core.SpatialOS.IsConnected;
            string phase = splash != null
                ? (splashWelcome != null && splashWelcome.activeSelf ? "splash-welcome" : "splash-server-info")
                : FindActive<LandingScreenType>() != null
                    ? "landing"
                    : FindActive<CharacterSelectionScreen>() != null
                        ? "character-selection"
                        : localPlayer ? "world" : connected ? "connected-transition" : "transition";

            string playerFields = string.Empty;
            if (localPlayer)
            {
                Improbable.Math.Vector3d position = LocalPlayer.GlobalPosition;
                playerFields = ",\"playerPosition\":{"
                    + "\"x\":" + JsonNumber(position.X)
                    + ",\"y\":" + JsonNumber(position.Y)
                    + ",\"z\":" + JsonNumber(position.Z) + "}";
            }

            bool helmAttached = false;
            long hullEntityId = 0;
            float throttle = 0f;
            float vertical = 0f;
            ShipControlsBehaviour controls = ShipControlsBehaviour.Instance;
            if (controls != null && Patching.Flight.LocalHelmFeedback_Patch.PilotField != null)
            {
                PilotStateReader pilot = Patching.Flight.LocalHelmFeedback_Patch.PilotField
                    .GetValue(controls) as PilotStateReader;
                helmAttached = pilot != null && EntityId.IsValidEntityId(pilot.DrivingEntityId);
                if (helmAttached)
                {
                    hullEntityId = pilot.DrivingEntityId.Id;
                    throttle = ShipControlsBehaviour.Throttle;
                    vertical = ShipControlsBehaviour.Vertical;
                }
            }
            return "{\"ok\":true,\"phase\":\"" + JsonEscape(phase)
                + "\",\"scene\":\"" + JsonEscape(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name)
                + "\",\"connected\":" + JsonBool(connected)
                + ",\"localPlayer\":" + JsonBool(localPlayer)
                + playerFields
                + ",\"helmAttached\":" + JsonBool(helmAttached)
                + ",\"hullEntityId\":" + hullEntityId.ToString(CultureInfo.InvariantCulture)
                + ",\"throttle\":" + JsonNumber(throttle)
                + ",\"vertical\":" + JsonNumber(vertical)
                + ",\"frame\":" + Time.frameCount.ToString(CultureInfo.InvariantCulture)
                + ",\"realtime\":" + Time.realtimeSinceStartup.ToString("0.000", CultureInfo.InvariantCulture)
                + "}";
        }

        private static T FindActive<T>() where T : Component
        {
            UnityEngine.Object[] objects = Resources.FindObjectsOfTypeAll(typeof(T));
            for (int i = 0; i < objects.Length; i++)
            {
                T component = objects[i] as T;
                if (component != null && component.gameObject.activeInHierarchy)
                    return component;
            }
            return null;
        }

        private void ListenLoop()
        {
            while (_running)
            {
                TcpClient client = null;
                try
                {
                    client = _listener.AcceptTcpClient();
                    client.ReceiveTimeout = 12000;
                    client.SendTimeout = 12000;
                    HandleClient(client);
                }
                catch (SocketException)
                {
                    if (_running)
                        Debug.LogWarning("[WAR][test-bridge] listener socket interrupted.");
                }
                catch (Exception e)
                {
                    if (_running)
                        Debug.LogWarning("[WAR][test-bridge] request failed: " + e.Message);
                }
                finally
                {
                    if (client != null)
                        client.Close();
                }
            }
        }

        private void HandleClient(TcpClient client)
        {
            using (NetworkStream stream = client.GetStream())
            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, false, 512))
            using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false), 512))
            {
                writer.AutoFlush = true;
                string line = reader.ReadLine();
                if (line == null || line.Length > MaxLineLength)
                {
                    writer.WriteLine(Error("bad_request", "one bounded command line required"));
                    return;
                }

                int split = line.IndexOf(' ');
                if (split <= 0 || !FixedTimeEquals(line.Substring(0, split), _token))
                {
                    writer.WriteLine(Error("unauthorized", "invalid test bridge token"));
                    return;
                }

                string commandText = line.Substring(split + 1).Trim();
                PendingCommand command = new PendingCommand(commandText);
                lock (_pending)
                    _pending.Enqueue(command);

                if (!command.Completed.WaitOne(10000, false))
                {
                    command.Cancelled = true;
                    writer.WriteLine(Error("timeout", "Unity main thread did not complete command"));
                    return;
                }
                writer.WriteLine(command.Response);
            }
        }

        private static bool FixedTimeEquals(string actual, string expected)
        {
            if (actual == null || expected == null)
                return false;
            int difference = actual.Length ^ expected.Length;
            int length = Math.Min(actual.Length, expected.Length);
            for (int i = 0; i < length; i++)
                difference |= actual[i] ^ expected[i];
            return difference == 0;
        }

        private static int ParsePort(string text)
        {
            int port;
            return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out port)
                && port >= 1024 && port <= 65535 ? port : DefaultPort;
        }

        private static bool TryParseEnum<T>(string text, out T value) where T : struct
        {
            try
            {
                if (!Enum.IsDefined(typeof(T), text))
                {
                    value = default(T);
                    return false;
                }
                value = (T)Enum.Parse(typeof(T), text, false);
                return true;
            }
            catch
            {
                value = default(T);
                return false;
            }
        }

        private static string Ok(string action)
        {
            return "{\"ok\":true,\"action\":\"" + JsonEscape(action) + "\"}";
        }

        private static string Error(string code, string message)
        {
            return "{\"ok\":false,\"error\":\"" + JsonEscape(code)
                + "\",\"message\":\"" + JsonEscape(message) + "\"}";
        }

        private static string JsonBool(bool value)
        {
            return value ? "true" : "false";
        }

        private static string JsonNumber(double value)
        {
            return value.ToString("0.######", CultureInfo.InvariantCulture);
        }

        private static string JsonEscape(string value)
        {
            if (value == null)
                return string.Empty;
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\r", "\\r").Replace("\n", "\\n");
        }

        private void OnDestroy()
        {
            StopBridge();
        }

        private void StopBridge()
        {
            _running = false;
            SyntheticInput.Disable();
            if (_listener != null)
            {
                try { _listener.Stop(); }
                catch { }
                _listener = null;
            }
        }

        private sealed class PendingCommand
        {
            internal readonly string Command;
            internal readonly ManualResetEvent Completed = new ManualResetEvent(false);
            internal volatile bool Cancelled;
            internal string Response;

            internal PendingCommand(string command)
            {
                Command = command;
            }
        }
    }
}
