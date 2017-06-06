namespace EU4SaveTool
{
    using System;
    using System.Diagnostics;
    using System.IO;
    using System.Text.RegularExpressions;

    [DebuggerDisplay("{Name} ({AppId})")]
    internal sealed class SteamAppManifest
    {
        public int AppId { get; set; }
        public string Name { get; set; }
        public string InstallDir { get; set; }

        public static SteamAppManifest Load(TextReader reader)
        {
            var kvpRegex = new Regex(@"^\s*""(?<key>[^""]+)""\s*""(?<value>[^""]+)""\s*$");
            string header = reader.ReadLine();
            if (!"\"AppState\"".Equals(header?.Trim()))
            {
                return null;
            }

            int appId = -1;
            string name = null;
            string installDir = null;

            const StringComparison keyComparison = StringComparison.OrdinalIgnoreCase;

            string line;
            while ((line = reader.ReadLine()) != null)
            {
                Match kvpMatch = kvpRegex.Match(line);
                if (!kvpMatch.Success)
                {
                    continue;
                }

                string key = Regex.Unescape(kvpMatch.Groups[1].Value);
                string value = Regex.Unescape(kvpMatch.Groups[2].Value);

                if ("appid".Equals(key, keyComparison))
                {
                    int.TryParse(value, out appId);
                }
                else if ("name".Equals(key, keyComparison)
                    && !string.IsNullOrWhiteSpace(value))
                {
                    name = value;
                }
                else if ("installdir".Equals(key, keyComparison)
                    && !string.IsNullOrWhiteSpace(value))
                {
                    installDir = value;
                }
            }

            if (appId >= 0
                && !string.IsNullOrWhiteSpace(name)
                && !string.IsNullOrWhiteSpace(installDir))
            {
                return new SteamAppManifest
                {
                    AppId = appId,
                    Name = name,
                    InstallDir = installDir
                };
            }

            return null;
        }
    }
}
