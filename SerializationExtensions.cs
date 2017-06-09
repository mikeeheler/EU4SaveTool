namespace EU4SaveTool
{
    using System;
    using System.IO;

    internal static class SerializationExtensions
    {
        public static string ToYaml(this object value)
        {
            var serializer = new YamlDotNet.Serialization.SerializerBuilder()
                .EmitDefaults()
                .Build();
            using (TextWriter writer = new StringWriter())
            {
                serializer.Serialize(writer, value);
                return writer.ToString();
            }
        }
    }
}
