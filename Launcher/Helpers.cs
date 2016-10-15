﻿/* This program is free software; you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation; either version 2 of the License, or
 * (at your option) any later version.
 * 
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 * 
 * You should have received a copy of the GNU General Public License along
 * with this program; if not, write to the Free Software Foundation, Inc.,
 * 51 Franklin Street, Fifth Floor, Boston, MA 02110-1301 USA.
 */
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;
using Launcher.Models;
using Microsoft.Win32;

namespace Launcher
{
    public static class Helpers
    {
        //this is a delegate used to access the UI from another thread
        private delegate void SetControlPropertyThreadSafeDelegate(Control control, string propertyName, object propertyValue);
        public static void SetControlPropertyThreadSafe(Control control, string propertyName, object propertyValue)
        {
            if (control.InvokeRequired)
                control.Invoke(new SetControlPropertyThreadSafeDelegate(SetControlPropertyThreadSafe), control, propertyName, propertyValue);
            else
                control.GetType().InvokeMember(propertyName, BindingFlags.SetProperty, null, control, new[] { propertyValue });
        }//end SetControlPropertyThreadSafe

        public static bool LauncherInLineageDirectory(string directory)
        {
            return File.Exists(Path.Combine(directory, "Login.dll"));
        }

        public static bool UpdateConfig(VersionInfo versionInfo)
        {
            var actuallyUpdated = false;

            var configKey = Registry.CurrentUser.OpenSubKey(@"Software\" + versionInfo.ServerName, true);

            if (configKey == null)
            {
                actuallyUpdated = true;
                configKey = Registry.CurrentUser.CreateSubKey(@"Software\" + versionInfo.ServerName);
            }

            if (!actuallyUpdated)
            {
                actuallyUpdated = (configKey.GetValue("Servers") == null || configKey.GetValue("Servers").ToString() != versionInfo.Servers) ||
                                  (configKey.GetValue("VersionInfoUrl") == null || configKey.GetValue("VersionInfoUrl").ToString() != versionInfo.VersionInfoUrl) ||
                                  (configKey.GetValue("VoteUrl") == null || configKey.GetValue("VoteUrl").ToString() != versionInfo.VoteUrl) ||
                                  (configKey.GetValue("WebsiteUrl") == null || configKey.GetValue("WebsiteUrl").ToString() != versionInfo.WebsiteUrl) ||
                                  (configKey.GetValue("UpdaterUrl") == null || configKey.GetValue("UpdaterUrl").ToString() != versionInfo.UpdaterUrl) ||
                                  (configKey.GetValue("LauncherUrl") == null || configKey.GetValue("LauncherUrl").ToString() != versionInfo.LauncherUrl) ||
                                  (configKey.GetValue("UpdaterFilesRoot") == null || configKey.GetValue("UpdaterFilesRoot").ToString() != versionInfo.UpdaterFilesRoot);
            }

            configKey.SetValue("Servers", versionInfo.Servers, RegistryValueKind.String);
            configKey.SetValue("VersionInfoUrl", versionInfo.VersionInfoUrl, RegistryValueKind.String);
            configKey.SetValue("VoteUrl", versionInfo.VoteUrl, RegistryValueKind.String);
            configKey.SetValue("WebsiteUrl", versionInfo.WebsiteUrl, RegistryValueKind.String);
            configKey.SetValue("UpdaterUrl", versionInfo.UpdaterUrl, RegistryValueKind.String);
            configKey.SetValue("LauncherUrl", versionInfo.LauncherUrl, RegistryValueKind.String);
            configKey.SetValue("UpdaterFilesRoot", versionInfo.UpdaterFilesRoot, RegistryValueKind.String);

            return actuallyUpdated;
        }

        public static LauncherConfig GetLauncherConfig(string keyName, string appPath)
        {
            try
            {
                var config = new LauncherConfig(keyName, appPath);
                var configKey = Registry.CurrentUser.OpenSubKey(@"Software\" + keyName, true);
                    
                var servers = configKey.GetValue("Servers").ToString().Split(',');
                config.Servers = new Dictionary<string, Server>();

                foreach (var server in servers)
                {
                    var serverInfo = server.Split(':');

                    config.Servers.Add(serverInfo[0].Trim(), new Server
                    {
                        IpOrDns = serverInfo[1],
                        Port = int.Parse(serverInfo[2])
                    });
                }

                config.UpdaterFilesRoot = new Uri(configKey.GetValue("UpdaterFilesRoot").ToString());
                config.UpdaterUrl = new Uri(configKey.GetValue("UpdaterUrl").ToString());
                config.VersionInfoUrl = new Uri(configKey.GetValue("VersionInfoUrl").ToString());
                config.VoteUrl = new Uri(configKey.GetValue("VoteUrl").ToString());
                config.WebsiteUrl = new Uri(configKey.GetValue("WebsiteUrl").ToString());
                config.PublicKey = configKey.GetValue("PublicKey").ToString();

                return config;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static List<string> GetAssociatedLaunchers(string appPath)
        {
            var associatedLaunchers = new List<string>();
            var settingsKey = Registry.CurrentUser.OpenSubKey(@"Software\LineageLauncher", true);

            if (settingsKey == null)
                return associatedLaunchers;

            foreach(var valueName in settingsKey.GetValueNames())
                if (string.Equals(settingsKey.GetValue(valueName).ToString(), appPath, StringComparison.CurrentCultureIgnoreCase))
                    associatedLaunchers.Add(valueName);

            return associatedLaunchers;
        }

        public static Settings LoadSettings(string keyName)
        {
            try
            {
                var settingsKey = Registry.CurrentUser.OpenSubKey(@"Software\" + keyName, true);

                if (settingsKey == null)
                    return new Settings();

                var ms = new MemoryStream((byte[])((RegistryKey)settingsKey).GetValue("AppSettings"));
                return DeserializeFromStream<Settings>(ms);
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static void SaveSettings(string keyName, Settings settings, string clientDirectory, bool isWin8OrHigher)
        {
            var existingKey = Registry.CurrentUser.GetValue(@"Software\" + keyName);

            if (existingKey == null)
                existingKey = Registry.CurrentUser.CreateSubKey(@"Software\" + keyName);

            ((RegistryKey)existingKey).SetValue("AppSettings", Serialize(settings), RegistryValueKind.Binary);

            //set the windowed flag in the lineage.cfg file
            if (isWin8OrHigher && settings.Windowed)
            {
                var compatRegKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers");
                if (compatRegKey != null)
                    ((RegistryKey)compatRegKey).SetValue(Path.Combine(clientDirectory, settings.ClientBin),
                        "~ DWM8And16BitMitigation 16BITCOLOR WINXPSP3", RegistryValueKind.String);
            }

            var lincfgPath = Path.Combine(clientDirectory, "lineage.cfg");

            if (!File.Exists(lincfgPath))
            {
                MessageBox.Show(@"Lineage.cfg file not found. Unable to update Windowed settings.");
                return;
            }

            using (var cfgFile = File.Open(lincfgPath, FileMode.Open))
            {
                var windowedByte = settings.Windowed ? (byte)0 : (byte)1;

                cfgFile.Seek(0xe4, SeekOrigin.Begin);
                cfgFile.WriteByte(windowedByte);
                cfgFile.Close();
            }

            var musicFilePath = Path.Combine(clientDirectory, "music.cfg");

            if (!File.Exists(musicFilePath))
                using (File.Create(musicFilePath)){ }

            File.WriteAllText(musicFilePath, (settings.MusicType == "Original Midi Music" ? "1" : "0"));
        } //end SaveSettings

        public static byte[] Serialize(object objectToSerialize)
        {
            using (var ms = new MemoryStream())
            {
                var formatter = new BinaryFormatter();
                formatter.Serialize(ms, objectToSerialize);
                return ms.ToArray();
            }
        } //end Serialize

        public static T DeserializeFromStream<T>(MemoryStream stream)
        {
            IFormatter formatter = new BinaryFormatter();
            stream.Seek(0, SeekOrigin.Begin);

            return (T)formatter.Deserialize(stream);
        } //end DeserializeFromStream

        public static VersionInfo GetVersionInfo(Uri versionInfoUrl, string pubKey)
        {
            try
            {
                var rsa = new RSACryptoServiceProvider();
                rsa.FromXmlString(pubKey);

                var request = (HttpWebRequest) WebRequest.Create(versionInfoUrl);
                request.Timeout = 2000;
                request.Proxy = null;
                request.UserAgent = "L1J Launcher";

                var response = request.GetResponse();
                var data = response.GetResponseStream();
                var signature = Convert.FromBase64String(response.Headers["Verify-Hash"]);
                string json;

                using (var sr = new StreamReader(data))
                    json = sr.ReadToEnd();

                //needed to drop this to SHA1 because WinXP doesn't always support higher by default
                var result = rsa.VerifyData(Encoding.UTF8.GetBytes(json), CryptoConfig.MapNameToOID("SHA1"), signature); 

                if (result)
                    return json.JsonDeserialize<VersionInfo>();

                return null;
            }
            catch (Exception)
            {
                return null;
            }
        } //end GetVersionInfo

        public static string GetChecksum(string file)
        {
            if (!File.Exists(file))
                return "";

            using (var stream = File.OpenRead(file))
                using (var md5 = MD5.Create())
                    return BitConverter.ToString(md5.ComputeHash(stream)).Replace("-", "").ToUpper();
        } //end GetChecksum

        public static bool IsWin8OrHigher(out string versionName)
        {
            var win8Version = new Version(6, 2, 9200, 0);
            versionName = "Windows 8";

            if (Environment.OSVersion.Version.Major >= 10)
                versionName = "Windows 10";

            return Environment.OSVersion.Platform == PlatformID.Win32NT &&
                   Environment.OSVersion.Version >= win8Version;
        } //end IsWin8OrHigher

        public static bool IsWin8Orhigher()
        {
            string versionName;
            return IsWin8OrHigher(out versionName);
        } //end IsWin8OrHigher 
    } //end class
} //end namespace
