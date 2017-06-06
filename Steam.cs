namespace EU4SaveTool
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Text.RegularExpressions;

    using Microsoft.Win32;

    internal static class Steam
    {
        /// <summary>
        /// UTF-8 without BOM. <see cref="System.Text.Encoding.UTF8"/> includes the BOM.
        /// It's not important for reading but very important for writing (writing out the BOM
        /// when it's not expected could corrupt the file for poorly-written file readers).
        /// </summary>
        public static readonly Encoding DefaultFileEncoding = new UTF8Encoding(false);

        public static uint ActiveUserId => GetActiveUserInternal();
        public static string InstallPath => GetInstallPathInternal();

        public static List<string> GetLibraryFolders()
        {
            var result = new List<string>();

            string installPath = GetInstallPathInternal();
            if (string.IsNullOrEmpty(installPath)
                || !Directory.Exists(installPath))
            {
                return result;
            }

            string libraryFoldersFilePath = Path.Combine(installPath, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(libraryFoldersFilePath))
            {
                return result;
            }

            // This parser is fragile b/c I CBF to write a good one and the format is unlikely
            // to change in a meaningful way.
            using (TextReader fileReader = new StreamReader(File.OpenRead(libraryFoldersFilePath), DefaultFileEncoding))
            {
                var kvpRegex = new Regex(@"^\s*""(?<key>[^""]+)""\s*""(?<value>[^""]+)""\s*$");
                string header = fileReader.ReadLine().Trim();
                if (!"\"LibraryFolders\"".Equals(header))
                {
                    return result;
                }

                string line;
                while ((line = fileReader.ReadLine()) != null)
                {
                    Match kvpMatch = kvpRegex.Match(line);
                    if (!kvpMatch.Success)
                    {
                        continue;
                    }

                    string keyString = kvpMatch.Groups[1].Value;
                    string valueString = Regex.Unescape(kvpMatch.Groups[2].Value);

                    if (int.TryParse(keyString, out int key)
                        && Directory.Exists(valueString))
                    {
                        result.Add(valueString);
                    }
                }
            }

            return result;
        }

        public static List<SteamAppManifest> GetInstalledApps()
        {
            var result = new List<SteamAppManifest>();

            string installPath = GetInstallPathInternal();
            List<string> libraryFolders = GetLibraryFolders();
            foreach (string libraryFolder in libraryFolders)
            {
                string steamAppsFolder = Path.Combine(libraryFolder, "steamapps");
                string commonFolder = Path.Combine(steamAppsFolder, "common");
                IEnumerable<string> appManifestFilePaths = Directory.EnumerateFiles(steamAppsFolder, "appmanifest_*.acf");
                foreach (string appManifestFilePath in appManifestFilePaths)
                {
                    using (TextReader reader = new StreamReader(File.OpenRead(appManifestFilePath), DefaultFileEncoding))
                    {
                        var appManifest = SteamAppManifest.Load(reader);
                        if (appManifest == null)
                        {
                            continue;
                        }

                        string fullInstallPath = Path.Combine(commonFolder, appManifest.InstallDir);
                        if (!Directory.Exists(fullInstallPath))
                        {
                            continue;
                        }

                        appManifest.InstallDir = fullInstallPath;
                        result.Add(appManifest);
                    }
                }
            }

            return result.OrderBy(x => x.Name).ToList();
        }

        private static RegistryKey FindSteamRegistryKey()
        {
            const string steamSubKeyName = @"SOFTWARE\Valve\Steam";

            // Steam is a 32-bit app
            using (var hklm32 = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32))
            {
                var steamKey = hklm32?.OpenSubKey(steamSubKeyName);
                if (steamKey != null) return steamKey;
            }

            // But it could be ported, one day...
            using (var hklm64 = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
            {
                var steamKey = hklm64?.OpenSubKey(steamSubKeyName);
                // Pass or fail, return this.
                return steamKey;
            }
        }

        private static string GetInstallPathInternal()
        {
            using (var steamKey = FindSteamRegistryKey())
            {
                return steamKey?.GetValue("InstallPath")?.ToString();
            }
        }

        private static uint GetActiveUserInternal()
        {
            using (var steamKey = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Valve\Steam\ActiveProcess"))
            {
                object activeUserValue = steamKey.GetValue("ActiveUser");
                if (activeUserValue != null)
                {
                    return Convert.ToUInt32(activeUserValue);
                }
            }

            return 0;
        }
    }
}
