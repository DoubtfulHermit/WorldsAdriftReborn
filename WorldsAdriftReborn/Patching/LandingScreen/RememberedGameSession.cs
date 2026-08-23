using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using BepInEx;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace WorldsAdriftReborn.Patching.Dynamic.LandingScreen
{
    /// <summary>
    /// Stores the server-issued game session for unattended local restarts.
    /// The player's password is never retained.  The bearer token is protected
    /// with the current Wine/Windows user DPAPI key before it reaches disk.
    /// </summary>
    internal static class RememberedGameSession
    {
        internal const string CredentialPrefix = "wareborn-session-v1:";
        private const string FileName = "wareborn-remembered-session-v1.json";
        private const int TokenLength = 43;
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes(
            "WAReborn remembered game session v1");

        private static string PathName
        {
            get { return Path.Combine(Paths.ConfigPath, FileName); }
        }

        internal static void Save(string username, string token)
        {
            if (IsBlank(username) || !IsToken(token))
            {
                Debug.LogError("[WAReborn] refusing to remember a malformed game session.");
                return;
            }

            try
            {
                byte[] plain = Encoding.UTF8.GetBytes(token);
                byte[] protectedToken = ProtectedData.Protect(
                    plain, Entropy, DataProtectionScope.CurrentUser);

                JObject record = new JObject();
                record["version"] = 1;
                record["username"] = username.Trim();
                record["protectedToken"] = Convert.ToBase64String(protectedToken);

                string path = PathName;
                string temp = path + ".tmp";
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(temp, record.ToString(), Encoding.UTF8);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
                File.Move(temp, path);
                Debug.Log("[WAReborn] this device will reuse the remembered game session; "
                    + "no password was stored.");
            }
            catch (Exception e)
            {
                Debug.LogError("[WAReborn] could not protect the remembered game session; "
                    + "automatic login remains disabled: " + e.Message);
            }
        }

        internal static bool TryLoad(out string username, out string credential)
        {
            username = null;
            credential = null;

            try
            {
                string path = PathName;
                if (!File.Exists(path) || new FileInfo(path).Length > 4096)
                {
                    return false;
                }

                JObject record = JObject.Parse(File.ReadAllText(path, Encoding.UTF8));
                if ((int)record["version"] != 1)
                {
                    return false;
                }

                string storedUsername = (string)record["username"];
                string protectedText = (string)record["protectedToken"];
                if (IsBlank(storedUsername)
                    || storedUsername.Length > 64
                    || IsBlank(protectedText)
                    || protectedText.Length > 1024)
                {
                    return false;
                }

                byte[] protectedToken = Convert.FromBase64String(protectedText);
                byte[] plain = ProtectedData.Unprotect(
                    protectedToken, Entropy, DataProtectionScope.CurrentUser);
                string token = Encoding.UTF8.GetString(plain);
                if (!IsToken(token))
                {
                    return false;
                }

                username = storedUsername.Trim();
                credential = CredentialPrefix + token;
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[WAReborn] remembered game session is unreadable; "
                    + "showing the normal login form: " + e.Message);
                return false;
            }
        }

        internal static void Forget()
        {
            try
            {
                if (File.Exists(PathName))
                {
                    File.Delete(PathName);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[WAReborn] could not remove the rejected remembered session: "
                    + e.Message);
            }
        }

        private static bool IsToken(string token)
        {
            if (token == null || token.Length != TokenLength)
            {
                return false;
            }

            for (int i = 0; i < token.Length; i++)
            {
                char c = token[i];
                if (!((c >= 'a' && c <= 'z')
                    || (c >= 'A' && c <= 'Z')
                    || (c >= '0' && c <= '9')
                    || c == '-' || c == '_'))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsBlank(string value)
        {
            return value == null || value.Trim().Length == 0;
        }
    }
}
