namespace EU4SaveTool
{
    using System;

    public sealed class EU4SaveGameVersion
    {
        public int First { get; set; }
        public int Second { get; set; }
        public int Third { get; set; }
        public int Fourth { get; set; }
        public string Name { get; set; }

        public string ToString(string format)
        {
            string output = $"{First}.{Second}.{Third}.{Fourth}";
            if ("l".Equals(format, StringComparison.OrdinalIgnoreCase))
            {
                output += " \"{Name}\"";
            }
            return output;
        }

        public override string ToString()
        {
            return ToString("l");
        }
    }
}
