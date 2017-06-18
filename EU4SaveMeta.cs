namespace EU4SaveTool
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;

    public sealed class EU4SaveMeta
    {
        private static readonly Encoding _encoding = CodePagesEncodingProvider.Instance.GetEncoding(1252);

        public EU4SaveType SaveType { get; set; } = EU4SaveType.Binary;
        public EU4Date Date { get; set; } = EU4Date.StartingDate;
        public string SaveGame { get; set; }
        public string PlayerTag { get; set; }
        public EU4CountryColors CountryColors { get; set; }
        public string PlayerCountryName { get; set; }
        public EU4SaveGameVersion SaveGameVersion { get; set; }
        public List<string> SaveGameVersions { get; set; }
        public List<string> DlcEnabled { get; set; }
        public List<string> ModEnabled { get; set; }
        public bool IronMan { get; set; } = false;
        public bool MultiPlayer { get; set; }
        public bool NotObserver { get; set; }
        public string CheckSum { get; set; }

        private static object ReadData(BinaryReader reader)
        {
            const ushort intTypeId = 0x000c;
            const ushort boolTypeId = 0x000e;
            const ushort stringTypeId = 0x000f;
            const ushort uintTypeId = 0x0014;

            const ushort openGroupId = 0x0003;
            const ushort closeGroupId = 0x0004;

            ushort typeId = reader.ReadUInt16();
            switch (typeId)
            {
                case openGroupId:
                    {
                        var result = new List<object>();
                        object item = null;
                        do
                        {
                            item = ReadData(reader);
                            if (item == null) break;
                            result.Add(item);
                        } while (true);
                        return result;
                    }
                case closeGroupId:
                    return null;
                case intTypeId:
                    return reader.ReadInt32();
                case uintTypeId:
                    return reader.ReadUInt32();
                case boolTypeId:
                    {
                        byte value = reader.ReadByte();
                        return value != 0x00;
                    }
                case stringTypeId:
                    {
                        ushort length = reader.ReadUInt16();
                        byte[] content = reader.ReadBytes(length);
                        return _encoding.GetString(content);
                    }

                default:
                    {
                        ushort equals = reader.ReadUInt16();
                        if (equals != 0x0001)
                        {
                            return null;
                        }

                        object data = ReadData(reader);
                        return (typeId, data);
                    }
            }
        }

        private static (ushort, object) ReadField(BinaryReader reader)
        {
            const ushort equalsId = 0x0001;

            ushort fieldId = reader.ReadUInt16();
            ushort equals = reader.ReadUInt16();
            if (!equals.Equals(equalsId))
            {
                return (0, null);
            }

            return (fieldId, ReadData(reader));
        }

        public static EU4SaveMeta Load(Stream stream)
        {
            const ushort dateId = 0x284d;
            const ushort saveGameId = 0x2c69;
            const ushort playerTagId = 0x2a38;
            const ushort countryColorsId = 0x3116;
            const ushort flagId = 0x2d52;
            const ushort colorId = 0x0056;
            const ushort symbolIndexId = 0x34f5;
            const ushort flagColorsId = 0x311a;
            const ushort playerCountryNameId = 0x32b8;
            const ushort saveGameVersionId = 0x2ec9;
            const ushort saveGameVersionsId = 0x314b;
            const ushort dlcEnabledId = 0x2ee1;
            const ushort modEnabledId = 0x2ee0;
            const ushort ironManId = 0x3589;
            const ushort multiPlayerId = 0x3329;
            const ushort notObserverId = 0x3317;
            const ushort checkSumId = 0x0179;

            byte[] _magic = { 0x45, 0x55, 0x34, 0x62, 0x69, 0x6e };

            var result = new EU4SaveMeta();

            using (var reader = new BinaryReader(stream, _encoding, true))
            {
                byte[] magic = reader.ReadBytes(_magic.Length);
                if (!magic.SequenceEqual(_magic))
                {
                    return null;
                }

                while (stream.Position < stream.Length)
                {
                    (ushort fieldId, object data) = ReadField(reader);
                    if (data == null)
                    {
                        // Parsing failed. We have no way of knowing what lay ahead other that dozens of irrelevant
                        // errors. Bail out.
                        Console.Error.WriteLine($"<PARSE> Null data for {fieldId:X}");
                        break;
                    }

                    switch (fieldId)
                    {
                        case dateId:
                            result.Date = EU4Date.FromInt((int)data);
                            break;
                        case saveGameId:
                            result.SaveGame = (string)data;
                            break;
                        case playerTagId:
                            result.PlayerTag = (string)data;
                            break;
                        case countryColorsId:
                            result.CountryColors = new EU4CountryColors();
                            foreach ((ushort, object) item in ((List<object>)data).Cast<(ushort, object)>())
                            {
                                switch (item.Item1)
                                {
                                    case flagId:
                                        result.CountryColors.Flag = (int)item.Item2;
                                        break;
                                    case colorId:
                                        result.CountryColors.Color = (int)item.Item2;
                                        break;
                                    case symbolIndexId:
                                        result.CountryColors.SymbolIndex = (int)item.Item2;
                                        break;
                                    case flagColorsId:
                                        result.CountryColors.FlagColors = new List<int>(
                                            ((List<object>)item.Item2).Cast<int>());
                                        break;
                                }
                            }
                            break;
                        case playerCountryNameId:
                            result.PlayerCountryName = (string)data;
                            break;
                        case saveGameVersionId:
                            {
                                List<(ushort, object)> list = ((List<object>)data).Cast<(ushort, object)>().ToList();
                                result.SaveGameVersion = new EU4SaveGameVersion
                                {
                                    First = (int)list.First(x => x.Item1 == 0x28e2).Item2,
                                    Second = (int)list.First(x => x.Item1 == 0x28e3).Item2,
                                    Third = (int)list.First(x => x.Item1 == 0x2ec7).Item2,
                                    Fourth = (int)list.First(x => x.Item1 == 0x2ec8).Item2,
                                    Name = (string)list.First(x => x.Item1 == 0x001b).Item2
                                };
                            }
                            break;
                        case saveGameVersionsId:
                            result.SaveGameVersions = ((List<object>)data).Cast<string>().ToList();
                            break;
                        case dlcEnabledId:
                            result.DlcEnabled = ((List<object>)data).Cast<string>().ToList();
                            break;
                        case modEnabledId:
                            result.ModEnabled = ((List<object>)data).Cast<string>().ToList();
                            break;
                        case ironManId:
                            result.IronMan = (bool)data;
                            break;
                        case multiPlayerId:
                            result.MultiPlayer = (bool)data;
                            break;
                        case notObserverId:
                            result.NotObserver = (bool)data;
                            break;
                        case checkSumId:
                            result.CheckSum = (string)data;
                            break;
                    }
                }
            }

            return result;
        }

    }
}
