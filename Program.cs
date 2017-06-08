﻿namespace EU4SaveTool
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.IO.Compression;
    using System.Linq;
    using System.Runtime.InteropServices;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.RegularExpressions;

    public static class Program
    {
        private const StringComparison _ignoreCaseCmp = StringComparison.OrdinalIgnoreCase;

        private static readonly Encoding _encoding = CodePagesEncodingProvider.Instance.GetEncoding(1252);
        private static readonly object _writeLock = new object();

        private static FileSystemWatcher _watcher = null;

        public static int Main(string[] args)
        {
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
            if (args.Length < 1)
            {
                Error("Usage: EU4SaveTool <file.eu4>");
                return -1;
            }

            string fullPath = Load(args[0]);

            Out("Type 'help' or '?' for help.");

            do
            {
                string directory = Path.GetDirectoryName(fullPath);
                string fileName = Path.GetFileName(fullPath);
                string baseName = Path.GetFileNameWithoutExtension(fullPath);

                string input = ReadInput($"{baseName}> ");
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
                    Out("Commands:");
                    Out("  tag <new tag> <new country name>");
                    Out("    Changes the player tag; modifies the loaded save file.");
                    Out("  load <file-path.eu4>");
                    Out("    Load a different save file.");
                    Out("  show <command>");
                    Out("    backups");
                    Out("      Show all available backups for the loaded save file.");
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
                else if ("tag".Equals(cmd, _ignoreCaseCmp)
                    && cmdParams.Length >= 2
                    && cmdParams[0].Length == 3
                    && !string.IsNullOrWhiteSpace(cmdParams[1]))
                {
                    ChangeTag(fullPath, cmdParams[0], cmdParams[1]);
                }
                else if ("clean".Equals(cmd, _ignoreCaseCmp)
                    && cmdParams.Length >= 1)
                {
                    Clean(fullPath, cmdParams);
                }
                else if ("load".Equals(cmd, _ignoreCaseCmp)
                    && cmdParams.Length >= 1)
                {
                    fullPath = Load(cmdParams[0]);
                }
                else if ("backup".Equals(cmd, _ignoreCaseCmp))
                {
                    Backup(fullPath);
                }
                else if ("restore".Equals(cmd, _ignoreCaseCmp)
                    && cmdParams.Length >= 1
                    && !string.IsNullOrEmpty(cmdParams[0]))
                {
                    Restore(fullPath, cmdParams[0]);
                }
                else if ("print".Equals(cmd, _ignoreCaseCmp))
                {
                    Print(fullPath);
                }
                else if ("show".Equals(cmd, _ignoreCaseCmp)
                    && cmdParams.Length >= 1)
                {
                    Show(fullPath, cmdParams[0]);
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

        private static string GetAppDataPath()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                throw new NotSupportedException("Must be run on Windows.");
            }

            string localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA");
            if (string.IsNullOrWhiteSpace(localAppData) || !Directory.Exists(localAppData))
            {
                throw new InvalidOperationException("Unable to find local app data folder.");
            }

            string appDataPath = Path.Combine(localAppData, nameof(EU4SaveTool));
            Directory.CreateDirectory(appDataPath);

            return appDataPath;
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

        private static void OnChanged(object source, FileSystemEventArgs e)
        {
            if (File.Exists(e.FullPath))
            {
                Backup(e.FullPath);
            }
        }

        private static void OnWatcherError(object source, ErrorEventArgs e)
        {
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

        private static string Load(string filePath)
        {
            string fullPath = Path.GetFullPath(filePath);

            Backup(fullPath);

            string directory = Path.GetDirectoryName(fullPath);
            string fileName = Path.GetFileName(fullPath);

            Out("Loaded " + fullPath);

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
                _watcher.Error += OnWatcherError;
                _watcher.EnableRaisingEvents = true;
            }

            return fullPath;
        }

        private static void Backup(string filePath)
        {
            if (!File.Exists(filePath))
            {
                return;
            }

            lock (_writeLock)
            {
                string fullPath = Path.GetFullPath(filePath);
                string ext = Path.GetExtension(fullPath);
                string hash = GetHash(fullPath);

                string backupDirName = GetBackupPath(fullPath);
                string backupFileName = $"{hash}{ext}";
                string backupFilePath = Path.Combine(backupDirName, backupFileName);

                Directory.CreateDirectory(backupDirName);
                File.Copy(fullPath, backupFilePath, true);
            }
        }

        private static void Clean(string filePath, params string[] args)
        {
            string fullPath = Path.GetFullPath(filePath);
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

                CleanBackups(fullPath, amountToKeep);
            }
            else if ("all".Equals(action, _ignoreCaseCmp)
                && args.Length >= 2
                && "backups".Equals(args[1], _ignoreCaseCmp))
            {
                CleanAllBackups();
            }
        }

        private static void CleanBackups(string filePath, int amountToKeep)
        {
            Out("Not yet implemented.");
        }

        private static void CleanAllBackups()
        {
            string backupPath = GetBaseBackupPath();
            Directory.Delete(backupPath, true);
            Out($"All backups removed from {backupPath}.");
        }

        private static void ChangeTag(string file, string tag, string name)
        {
            byte[] tagContext = { 0x38, 0x2a, 0x01, 0x00, 0x0f, 0x00 };
            byte[] nameContext = { 0xb8, 0x32, 0x01, 0x00, 0x0f, 0x00 };

            lock (_writeLock)
            {
                using (var zipArchive = ZipFile.Open(file, ZipArchiveMode.Update))
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

        private static void Restore(string filePath, string hash)
        {
            string fullPath = Path.GetFullPath(filePath);
            string backupFilePath = GetBackupPath(fullPath);
            string ext = Path.GetExtension(fullPath);

            if ("last".Equals(hash, _ignoreCaseCmp)
                || "latest".Equals(hash, _ignoreCaseCmp))
            {
                IEnumerable<string> backups = Directory.EnumerateFileSystemEntries(backupFilePath, $"*{ext}");
                string latestBackup = backups.OrderByDescending(x => new FileInfo(x).LastWriteTimeUtc).FirstOrDefault();
                if (latestBackup != null)
                {
                    lock (_writeLock)
                    {
                        File.Copy(latestBackup, fullPath, true);
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
                        File.Copy(restorePath, fullPath, true);
                        Out($"Restored '{Path.GetFileNameWithoutExtension(restorePath)}' to '{fullPath}");
                    }
                }
            }
        }

        private static void Print(string fullPath)
        {
            using (Stream contentStream = OpenMeta(fullPath))
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

        private static void Show(string fullPath, string action)
        {
            if ("backups".Equals(action, _ignoreCaseCmp))
            {
                ShowBackups(fullPath);
            }
        }

        private static void ShowBackups(string fullPath)
        {
            string backupDirName = GetBackupPath(fullPath);
            string ext = Path.GetExtension(fullPath);
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
