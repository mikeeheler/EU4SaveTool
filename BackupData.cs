namespace EU4SaveTool
{
    using System;

    internal sealed class BackupData
    {
        public string Hash { get; set; }
        public string SaveName { get; set; }
        public EU4SaveLocation SaveLocation { get; set; }
        public string Annotation { get; set; }
    }
}
