﻿namespace EU4SaveTool
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.IO;
    using System.IO.Compression;
    using System.Linq;
    using System.Runtime;
    using System.Runtime.InteropServices;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.RegularExpressions;
    using System.Threading;
    using System.Threading.Tasks;

    using Microsoft.Win32;

    public static class Program
    {
        private const StringComparison _ignoreCaseCmp = StringComparison.OrdinalIgnoreCase;
        private const int _eu4AppId = 236850;

        private static readonly Encoding _encoding = CodePagesEncodingProvider.Instance.GetEncoding(1252);
        private static readonly object _writeLock = new object();

        private static FileSystemWatcher _watcher = null;

        private static string _eu4InstallPath = string.Empty;
        private static string _localSavesPath = string.Empty;
        private static string _cloudSavesPath = string.Empty;

        private static string _loadedFilePath = string.Empty;

        public static int Main(string[] args)
        {
            var test = new List<BackupData>
            {
                new BackupData
                {
                    Hash = "123abc",
                    SaveName = "test.eu4",
                    SaveLocation = EU4SaveLocation.Local
                }
            };
            string output = test.ToYaml();
            Out("%YAML 1.2");
            Out("---");
            Out(output);
            Out("...");

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            var savedInputEncoding = Console.InputEncoding;
            var savedOutputEncoding = Console.OutputEncoding;

            try
            {
                Console.InputEncoding = _encoding;
                Console.OutputEncoding = _encoding;

                return Execute(args);
            }
            finally
            {
                Console.InputEncoding = savedInputEncoding;
                Console.OutputEncoding = savedOutputEncoding;
            }
        }

        private static int Execute(string[] args)
        {
            SteamAppManifest eu4AppManifest = GetEu4AppManifest();
            string steamInstallPath = Steam.InstallPath;
            uint activeUserId = Steam.ActiveUserId;
            string steamUserDataPath = Path.Combine(steamInstallPath, "userdata", activeUserId.ToString());
            string eu4UserDataPath = Path.Combine(steamUserDataPath, _eu4AppId.ToString());

            string _eu4InstallPath = Path.GetFullPath(eu4AppManifest.InstallDir);
            string _localSavesPath = Path.GetFullPath(Path.Combine(GetMyDocumentsPath(), "Paradox Interactive", eu4AppManifest.Name, "save games"));
            string _cloudSavesPath = Path.GetFullPath(Path.Combine(eu4UserDataPath, "remote", "save games"));

            Out("EU4 Install Path: " + _eu4InstallPath);
            Out("Local Saves: " + _localSavesPath);
            Out("Cloud Saves: " + _cloudSavesPath);
            Out(string.Empty);

            if (args.Length > 0)
            {
                Load(args[0]);
            }

            Out("Type 'help' or '?' for help.");

            do
            {
                bool isLoaded = !string.IsNullOrWhiteSpace(_loadedFilePath)
                    && File.Exists(_loadedFilePath);
                string prompt = isLoaded ? Path.GetFileNameWithoutExtension(_loadedFilePath) : "--not loaded--";

                string input = ReadInput($"{prompt}> ");
                if (string.IsNullOrWhiteSpace(input))
                {
                    continue;
                }

                string[] cmdArgs = ParseInput(input);
                if (cmdArgs.Length < 1)
                {
                    continue;
                }

                string cmd = cmdArgs.First();
                string[] cmdParams = cmdArgs.Skip(1).ToArray();

                if ("help".Equals(cmd, _ignoreCaseCmp)
                    || "?".Equals(cmd, _ignoreCaseCmp))
                {
                    ShowHelp();
                }
                else if ("tag".Equals(cmd, _ignoreCaseCmp)
                    && cmdParams.Length >= 2
                    && cmdParams[0].Length == 3
                    && !string.IsNullOrWhiteSpace(cmdParams[1]))
                {
                    string tag = cmdParams[0];
                    string newCountryName = cmdParams[1];

                    ChangeTag(tag, newCountryName);
                }
                else if ("clean".Equals(cmd, _ignoreCaseCmp)
                    && cmdParams.Length >= 1)
                {
                    Clean(cmdParams);
                }
                else if ("load".Equals(cmd, _ignoreCaseCmp)
                    && cmdParams.Length >= 1)
                {
                    string filePath = cmdParams[0];
                    Load(filePath);
                }
                else if ("backup".Equals(cmd, _ignoreCaseCmp))
                {
                    Backup();
                }
                else if ("restore".Equals(cmd, _ignoreCaseCmp)
                    && cmdParams.Length >= 1
                    && !string.IsNullOrEmpty(cmdParams[0]))
                {
                    string hash = cmdParams[0];
                    Restore(hash);
                }
                else if ("note".Equals(cmd, _ignoreCaseCmp))
                {
                    
                }
                else if ("print".Equals(cmd, _ignoreCaseCmp))
                {
                    Print();
                }
                else if ("show".Equals(cmd, _ignoreCaseCmp)
                    && cmdParams.Length >= 1)
                {
                    string action = cmdParams[0];
                    Show(action);
                }
                else if ("q".Equals(cmd, _ignoreCaseCmp)
                    || "quit".Equals(cmd, _ignoreCaseCmp))
                {
                    break;
                }
                else
                {
                    Error("Unrecognized input.");
                }
            } while (true);

            if (_watcher != null)
            {
                _watcher.Dispose();
            }

            return 0;
        }

        private static void Error(string message)
        {
            Console.Error.WriteLine(message);
        }

        private static void Out(string message)
        {
            Console.WriteLine(message);
        }

        private static void ShowHelp(params string[] args)
        {
            Out("Commands:");
            Out("  tag <new tag> <new country name>");
            Out("    Changes the player tag; modifies the loaded save file.");
            Out("  load <file-path.eu4>");
            Out("    Load a different save file.");
            Out("  show <command>");
            Out("    backups");
            Out("      Show all available backups for the loaded save file.");
            Out("    saves");
            Out("      List all saves files.");
            Out("  clean <command>");
            Out("    backups [keep=10]");
            Out("      Clean backups for loaded save file, keeping the latest [keep] (defualt: 10).");
            Out("    all backups");
            Out("      Erases all backups for all save files.");
            Out("  backup");
            Out("    Back up the current save file.");
            Out("  restore <hash | 'latest'>");
            Out("    Restore backup from the given hash identifier. Partial hash is OK.");
            Out("    Use 'show backups' for list of existing backups.");
            Out("  print");
            Out("    Output all meta data for the loaded save file.");
            Out("  quit");
            Out("    Exit " + nameof(EU4SaveTool) + ".");
        }

        private static string GetShellFolder(string key)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                throw new NotSupportedException("Must be run on Windows.");
            }

            const string subKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders";

            using (var regKey = Registry.CurrentUser.OpenSubKey(subKey))
            {
                if (regKey == null)
                {
                    throw new InvalidOperationException("Unable to open HKCU\\" + subKey);
                }

                return regKey.GetValue(key).ToString();
            }
        }

        private static string GetAppDataPath()
        {
            string localAppData = GetShellFolder("Local AppData");
            if (string.IsNullOrWhiteSpace(localAppData) || !Directory.Exists(localAppData))
            {
                throw new InvalidOperationException("Unable to find local app data folder.");
            }

            string appDataPath = Path.Combine(localAppData, nameof(EU4SaveTool));
            Directory.CreateDirectory(appDataPath);

            return appDataPath;
        }

        private static string GetMyDocumentsPath()
        {
            return GetShellFolder("Personal");
        }

        private static string GetBaseBackupPath()
        {
            string backupPath = Path.Combine(GetAppDataPath(), "backups");
            Directory.CreateDirectory(backupPath);
            return backupPath;
        }

        private static string GetBackupPath(string saveFilePath)
        {
            string fullPath = Path.GetFullPath(saveFilePath);
            string basename = Path.GetFileNameWithoutExtension(fullPath);

            string backupPath = Path.Combine(GetBaseBackupPath(), basename);
            Directory.CreateDirectory(backupPath);

            return backupPath;
        }

        private static SteamAppManifest GetEu4AppManifest()
        {
            uint steamActiveUserId = Steam.ActiveUserId;
            if (steamActiveUserId == 0)
            {
                Error("Steam must be running.");
                return null;
            }

            string steamInstallPath = Steam.InstallPath;
            var installedApps = Steam.GetInstalledApps();
            var eu4AppManifest = installedApps.FirstOrDefault(x => x.AppId == _eu4AppId);
            if (eu4AppManifest == null)
            {
                Error("Could not find EU4 install path.");
            }

            return eu4AppManifest;
        }

        private static void OnChanged(object source, FileSystemEventArgs e)
        {
            if (e.FullPath.Equals(_loadedFilePath))
            {
                Task.Delay(TimeSpan.FromSeconds(1.0)).ContinueWith(_ => Backup());
            }
        }

        private static void OnWatcherError(object source, ErrorEventArgs e)
        {
            Out("-> OnWatcherError: " + e.GetException().ToString());

            _watcher.EnableRaisingEvents = false;
            string path = _watcher.Path;
            string filter = _watcher.Filter;

            if (_watcher != null)
            {
                _watcher.Dispose();
            }
            _watcher = null;

            Load(Path.Combine(path, filter));
        }

        private static string ReadInput(string prompt)
        {
            Out(string.Empty);
            Console.Write(prompt);
            string input = Console.ReadLine().Trim();
            Out(string.Empty);
            return input;
        }

        private static string[] ParseInput(string input)
        {
            string[] parsed = Regex.Split(
                input,
                "(?<=^[^\"]*(?:\"[^\"]*\"[^\"]*)*) (?=(?:[^\"]*\"[^\"]*\")*[^\"]*$)");
            return parsed.Select(x => x.Trim('"')).ToArray();
        }

        private static string GetHash(string file)
        {
            using (var md5 = MD5.Create())
            {
                byte[] contents = File.ReadAllBytes(file);
                byte[] hash = md5.ComputeHash(contents);
                return BitConverter.ToString(hash).ToLowerInvariant().Replace("-", "");
            }
        }

        private static void Load(params string[] args)
        {
            _loadedFilePath = Path.GetFullPath(args[0]);

            Backup();

            string directory = Path.GetDirectoryName(_loadedFilePath);
            string fileName = Path.GetFileName(_loadedFilePath);
            string ext = Path.GetExtension(_loadedFilePath);

            Out("Loaded " + _loadedFilePath);

            lock (_writeLock)
            {
                if (_watcher != null)
                {
                    _watcher.Dispose();
                }

                _watcher = new FileSystemWatcher(directory, fileName);
                _watcher.Changed += OnChanged;
                _watcher.Created += OnChanged;
                _watcher.Deleted += OnChanged;
                _watcher.Renamed += OnChanged;
                _watcher.Error += OnWatcherError;
                _watcher.EnableRaisingEvents = true;
            }
        }

        private static void Backup()
        {
            if (!File.Exists(_loadedFilePath))
            {
                return;
            }

            lock (_writeLock)
            {
                string ext = Path.GetExtension(_loadedFilePath);
                string hash = GetHash(_loadedFilePath);

                string backupDirName = GetBackupPath(_loadedFilePath);
                string backupFileName = $"{hash}{ext}";
                string backupFilePath = Path.Combine(backupDirName, backupFileName);

                if (!File.Exists(backupFilePath))
                {
                    Directory.CreateDirectory(backupDirName);
                    File.Copy(_loadedFilePath, backupFilePath, true);

                    Out("Backup created: " + hash);
                }
            }
        }

        private static void Clean(params string[] args)
        {
            string action = args.First();

            if ("backups".Equals(action, _ignoreCaseCmp))
            {
                int amountToKeep = 10;
                if (args.Length >= 2
                    && int.TryParse(args[1], out int parsed)
                    && parsed >= 0)
                {
                    amountToKeep = parsed;
                }

                CleanBackups(amountToKeep);
            }
            else if ("all".Equals(action, _ignoreCaseCmp)
                && args.Length >= 2
                && "backups".Equals(args[1], _ignoreCaseCmp))
            {
                CleanAllBackups();
            }
        }

        private static void CleanBackups(int amountToKeep)
        {
            string ext = Path.GetExtension(_loadedFilePath);
            string backupPath = GetBackupPath(_loadedFilePath);

            string[] allBackups
                = Directory.EnumerateFiles(backupPath, $"*{ext}", SearchOption.AllDirectories)
                    .OrderByDescending(x => new FileInfo(x).LastWriteTimeUtc)
                    .Skip(amountToKeep)
                    .ToArray();

            foreach (string path in allBackups)
            {
                string hash = Path.GetFileNameWithoutExtension(path);
                File.Delete(path);
                Out($"Removed {hash}");
            }
        }

        private static void CleanAllBackups()
        {
            string backupPath = GetBaseBackupPath();
            Directory.Delete(backupPath, true);
            Out($"All backups removed from {backupPath}.");
        }

        private static void ChangeTag(string tag, string name)
        {
            byte[] tagContext = { 0x38, 0x2a, 0x01, 0x00, 0x0f, 0x00 };
            byte[] nameContext = { 0xb8, 0x32, 0x01, 0x00, 0x0f, 0x00 };

            lock (_writeLock)
            {
                using (var zipArchive = ZipFile.Open(_loadedFilePath, ZipArchiveMode.Update))
                {
                    var entry = zipArchive.GetEntry("meta");
                    byte[] metaBytes = new byte[entry.Length];

                    using (var entryStream = entry.Open())
                    {
                        entryStream.Read(metaBytes, 0, metaBytes.Length);
                    }

                    byte[] buffer;

                    entry.Delete();
                    var newMetaEntry = zipArchive.CreateEntry("meta");

                    using (var reader = new BinaryReader(new MemoryStream(metaBytes, false)))
                    using (var newEntry = new BinaryWriter(newMetaEntry.Open(), _encoding))
                    {
                        int index = GetIndexOf(metaBytes, tagContext);
                        reader.BaseStream.Seek(0, SeekOrigin.Begin);
                        buffer = reader.ReadBytes(index + tagContext.Length);
                        newEntry.Write(buffer);

                        string existingTag = ReadString(reader, _encoding);
                        WriteString(newEntry, tag, _encoding);

                        Out("Tag: " + existingTag + " -> " + tag);
                        index = GetIndexOf(metaBytes, nameContext);
                        reader.BaseStream.Seek(nameContext.Length, SeekOrigin.Current);
                        newEntry.Write(nameContext);

                        string existingName = ReadString(reader, _encoding);
                        WriteString(newEntry, name, _encoding);
                        Out("Name: " + existingName + " -> " + name);

                        reader.BaseStream.CopyTo(newEntry.BaseStream);
                    }
                }
            }
        }

        private static void Restore(string hash)
        {
            string backupFilePath = GetBackupPath(_loadedFilePath);
            string ext = Path.GetExtension(_loadedFilePath);

            if ("last".Equals(hash, _ignoreCaseCmp)
                || "latest".Equals(hash, _ignoreCaseCmp))
            {
                IEnumerable<string> backups = Directory.EnumerateFileSystemEntries(backupFilePath, $"*{ext}");
                string latestBackup = backups.OrderByDescending(x => new FileInfo(x).LastWriteTimeUtc).FirstOrDefault();
                if (latestBackup != null)
                {
                    lock (_writeLock)
                    {
                        File.Copy(latestBackup, _loadedFilePath, true);
                        Out($"Restored {Path.GetFileName(latestBackup)}");
                    }
                }
            }
            else if (!string.IsNullOrEmpty(hash))
            {
                var matches = Directory.EnumerateFileSystemEntries(backupFilePath, $"{hash}*{ext}").ToList();
                if (matches.Count == 0)
                {
                    return;
                }

                if (matches.Count > 1)
                {
                    Error($"More than one backup matches '{hash}'.");
                    Error("Provide more context to select a single backup.");
                    Error($"Backups matching '{hash}':");
                    foreach (string match in matches)
                    {
                        string filename = Path.GetFileNameWithoutExtension(match);
                        Error("  " + filename);
                    }
                    return;
                }

                string restorePath = Path.Combine(backupFilePath, matches.Single());
                if (File.Exists(restorePath))
                {
                    lock (_writeLock)
                    {
                        File.Copy(restorePath, _loadedFilePath, true);
                        Out($"Restored '{Path.GetFileNameWithoutExtension(restorePath)}' to '{_loadedFilePath}");
                    }
                }
            }
        }

        private static void Print()
        {
            using (Stream contentStream = OpenMeta(_loadedFilePath))
            {
                var saveData = EU4SaveMeta.Load(contentStream);
                Out($"SaveType: {saveData.SaveType}");
                Out($"  date={saveData.Date}");
                Out($"  save_game={saveData.SaveGame}");
                Out($"  player={saveData.PlayerTag}");
                Out($"  displayed_country_name={saveData.PlayerCountryName}");
                Out($"  savegame_version={{");
                Out($"    first={saveData.SaveGameVersion.First}");
                Out($"    second={saveData.SaveGameVersion.Second}");
                Out($"    third={saveData.SaveGameVersion.Third}");
                Out($"    forth={saveData.SaveGameVersion.Fourth}");
                Out($"    name={saveData.SaveGameVersion.Name}");
                Out($"  }}");
                Out($"  savegame_versions={{");
                foreach (string versionString in saveData.SaveGameVersions)
                {
                    Out($"    {versionString}");
                }
                Out($"  }}");
                Out($"  dlc_enabled={{");
                foreach (string dlcName in saveData.DlcEnabled)
                {
                    Out($"    {dlcName}");
                }
                Out($"  }}");
                Out($"  mod_enabled={{");
                foreach (string modName in saveData.ModEnabled)
                {
                    Out($"    {modName}");
                }
                Out($"  }}");
                Out($"  iron_man={BoolYesNo(saveData.IronMan)}");
                Out($"  multi_player={BoolYesNo(saveData.MultiPlayer)}");
                Out($"  not_observer={BoolYesNo(saveData.NotObserver)}");
                Out($"  checksum={saveData.CheckSum}");
            }
        }

        private static void Show(string action)
        {
            if ("backups".Equals(action, _ignoreCaseCmp))
            {
                ShowBackups();
            }
            else if ("saves".Equals(action, _ignoreCaseCmp))
            {
                ShowSaves();
            }
        }

        private static void ShowBackups()
        {
            string backupDirName = GetBackupPath(_loadedFilePath);
            string ext = Path.GetExtension(_loadedFilePath);
            IEnumerable<string> backupFiles = Directory.EnumerateFileSystemEntries(backupDirName, $"*{ext}");
            IOrderedEnumerable<string> sorted = backupFiles.OrderByDescending(x => new FileInfo(x).LastWriteTimeUtc);
            int i = 1;

            Out($"Backup Path: {backupDirName}");
            Out(string.Empty);
            Out("|     | Date       | Tag | IronMan | Hash                             |");
            Out("| --- | ---------- | --- | ------- | -------------------------------- |");
            foreach (string backupFilePath in sorted)
            {
                using (var stream = OpenMeta(backupFilePath))
                {
                    EU4SaveMeta save = EU4SaveMeta.Load(stream);
                    string hash = Path.GetFileNameWithoutExtension(backupFilePath);
                    Out($"| {i,-3} | {save.Date.ToString().PadRight(10)} | {save.PlayerTag} | {BoolYesNo(save.IronMan),-7} | {hash} |");
                }
                ++i;
            }
        }

        private static void ShowSaves()
        {
            Out("Local Saves:");
            foreach (string path in Directory.EnumerateFiles(_localSavesPath, "*.eu4", SearchOption.AllDirectories))
            {
                Out("  " + Path.GetFileNameWithoutExtension(path));
            }

            Out("Cloud Saves:");
            foreach (string path in Directory.EnumerateFiles(_cloudSavesPath, "*.eu4", SearchOption.AllDirectories))
            {
                Out("  " + Path.GetFileNameWithoutExtension(path));
            }
        }

        private static Stream OpenMeta(string fullPath)
        {
            lock (_writeLock)
            {
                using (var zipArchive = ZipFile.OpenRead(fullPath))
                {
                    var entry = zipArchive.GetEntry("meta");
                    byte[] content = new byte[entry.Length];

                    using (Stream entryStream = entry.Open())
                    {
                        int bytesRead = entryStream.Read(content, 0, content.Length);
                    }

                    return new MemoryStream(content, false);
                }
            }
        }

        private static string BoolYesNo(bool value)
        {
            return value ? "yes" : "no";
        }

        private static string ReadString(BinaryReader reader, Encoding encoding)
        {
            ushort length = reader.ReadUInt16();
            byte[] stringBytes = reader.ReadBytes(length);
            return encoding.GetString(stringBytes);
        }

        private static void WriteString(BinaryWriter writer, string value, Encoding encoding)
        {
            ushort length = (ushort)value.Length;
            writer.Write(length);
            byte[] valueBytes = encoding.GetBytes(value);
            writer.Write(valueBytes);
        }

        private static int GetIndexOf(byte[] input, byte[] context)
        {
            if (input.Length < context.Length)
            {
                return -1;
            }

            for (int i = 0; i < input.Length - context.Length; ++i)
            {
                bool found = true;
                for (int j = 0; j < context.Length; ++j)
                {
                    if (input[i + j] != context[j])
                    {
                        found = false;
                        break;
                    }
                }

                if (found)
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
