using Nanook.NKit.Nintendo.WiiGc;
using Nanook.NKit.Nintendo.WiiU;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;

namespace Nanook.NKit
{
    public class Utf8StringWriter : StringWriter { public Utf8StringWriter(StringBuilder sb) : base(sb) { } public override Encoding Encoding => Encoding.UTF8; }

    public class ScanParser
    {

        private ScanParser()
        {
        }

        public static Scan Parse(string filePathName)
        {
            using (Stream s = File.OpenRead(filePathName))
                return Parse(s, Path.GetFileNameWithoutExtension(filePathName));
        }

        public static Scan Parse(Stream scanData, string name)
        {
            Scan scan = new Scan(SystemType.Default, name);

            XmlReaderSettings xmlSet = new XmlReaderSettings() { IgnoreComments = true, IgnoreWhitespace = true };
            using (XmlReader reader = XmlReader.Create(scanData, xmlSet))
            {
                if (!reader.Read())
                    return null;
                if (reader.Name == "xml" && !reader.Read())
                    return null;
                if (reader.Name != "NKitScan")
                    return null;

                FsFile fsFile = null;
                recurseObject(0, null, reader, scan, null, ref fsFile, null, null, null, null, new Dictionary<string, object>());
            }

            //build folder hierarchy
            foreach (ScanArea area in scan.Areas)
            {
                if (area.Type == AreaType.FileSystem)
                {
                    FsFolder root = new FsFolder() { Folders = new List<IFsFolder>(), Files = new List<IFsFile>(), Name = "", Parent = null };
                    ((FsFileSystem)area.FsInfo.FileSystem).Root = root;

                    foreach (FsFile f in area.FsInfo.FileSystem.Files)
                    {
                        string[] parents = f.FullName.Split('/');
                        f.Name = parents[parents.Length - 1];

                        FsFolder fld = root;
                        for (int i = 1; i < parents.Length - 1; i++)
                        {
                            FsFolder child = (FsFolder)fld.Folders.FirstOrDefault(a => a.Name == parents[i]);
                            if (child == null)
                            {
                                fld = new FsFolder() { Folders = new List<IFsFolder>(), Files = new List<IFsFile>(), Name = parents[i], Parent = fld };
                                fld.Parent.Folders.Add(fld);
                            }
                            else
                                fld = child;
                        }

                        f.Parent = fld;
                        f.Path = fld.Path;
                        fld.Files.Add(f);
                    }
                }
            }

            return scan;
        }

        private static bool recurseObject(int depth, string objectName, XmlReader reader, Scan scan, ScanArea area, ref FsFile fsFile, ScanSection section, SectionItem item, SectionData fsLine, SectionData gapInfo, Dictionary<string, object> properties)
        {
            //bool isArray = false;
            while (objectName == null || reader.Read())
            {
                objectName = reader.Name;
                string propertyName = objectName;

                if (reader.NodeType == XmlNodeType.EndElement)
                {
                    switch (objectName)
                    {
                        case "AreaInfo":
                            //area.AreaInfo.Properties = getAreaProperties(properties, area);
                            break;
                        case "Areas":
                            if (area.Type == AreaType.FileSystem)
                                ((FsFileSystemData)area.FsInfo).Size = area.Size;
                            //area.AreaInfo.Properties = getProperties(properties, _scan, area);
                            break;
                        case "FileSystem":
                            //    area.Sections = 
                            break;
                        default:
                            break;
                    }
                    return true;
                }
                else //if (reader.NodeType == XmlNodeType.Element)
                {
                    switch (objectName)
                    {
                        case "AreaInfo":
                            properties = new Dictionary<string, object>();
                            break;
                        case "Area":
                            scan.Areas.Add(new ScanArea(scan) { AreaInfo = new AreaInfo(0, AreaType.None, scan.Areas.Count) }); //Area In set later
                            fsFile = null;
                            break;
                        case "Section":
                            area.Sections.Add(new ScanSection(area));
                            area.Sections[area.Sections.Count - 1].Items = new SectionItems();
                            if (area.Type == AreaType.FileSystem)
                                fsFile = new FsFile();
                            break;
                        case "File":
                        case "Gap":
                            fsLine = new SectionData();
                            break;
                        case "Info":
                            if (section.Items.Count == 0) //not present when Type == other
                                section.Items.Add(new SectionItem(section.ImageOffset, section.AreaOffset, 0, null));
                            section.Items[section.Items.Count - 1].GapInfo.Add(gapInfo = new SectionData());
                            break;
                        default:
                            break;
                    }
                    bool isEmpty = reader.IsEmptyElement;
                    readProperties(objectName, reader, scan, scan.Areas?.LastOrDefault(), fsFile, area?.Sections?.LastOrDefault(), (SectionItem)section?.Items?.LastOrDefault(), ref fsLine, gapInfo, properties);
                    if (!isEmpty)
                        recurseObject(depth + 1, propertyName, reader, scan, scan.Areas?.LastOrDefault(), ref fsFile, area?.Sections?.LastOrDefault(), (SectionItem)section?.Items?.LastOrDefault(), fsLine, gapInfo, properties);
                }
            }
            return false; //no more
        }

        private static void readProperties(string objectName, XmlReader reader, Scan scan, ScanArea area, FsFile fsFile, ScanSection section, SectionItem item, ref SectionData fsLine, SectionData gapInfo, Dictionary<string, object> properties)
        {
            switch (objectName)
            {
                case "AreaInfo":
                    if (reader.HasAttributes)
                    {
                        reader.MoveToFirstAttribute();
                        do
                        {
                            properties.Add(reader.Name, reader.Value);
                        } while (reader.MoveToNextAttribute());
                    }
                    break;
                case "File":
                case "Gap":
                    if (reader.HasAttributes)
                    {
                        reader.MoveToFirstAttribute();
                        do
                        {
                            switch (reader.Name)
                            {
                                case "ImageOffset":
                                    fsLine.ImageOffset = long.Parse((string)reader.Value, System.Globalization.NumberStyles.HexNumber);
                                    fsLine.AreaOffset = fsLine.ImageOffset; //default
                                    if (area.AreaInfo.BlockSize == area.AreaInfo.BlockFsSize) //there won't be an fsOffset
                                        fsLine.AreaFsOffset = fsLine.AreaOffset;
                                    break;
                                case "AreaOffset":
                                    fsLine.AreaOffset = long.Parse((string)reader.Value, System.Globalization.NumberStyles.HexNumber);
                                    if (area.AreaInfo.BlockSize == area.AreaInfo.BlockFsSize) //there won't be an fsOffset
                                        fsLine.AreaFsOffset = fsLine.AreaOffset;
                                    break;
                                case "SectionOffset":
                                    fsLine.Offset = long.Parse((string)reader.Value, System.Globalization.NumberStyles.HexNumber);
                                    if (area.AreaInfo.BlockSize == area.AreaInfo.BlockFsSize) //there won't be an fsOffset
                                        fsLine.FsOffset = fsLine.Offset;
                                    break;
                                case "AreaFsOffset":
                                    fsLine.AreaFsOffset = long.Parse((string)reader.Value, System.Globalization.NumberStyles.HexNumber);
                                    break;
                                case "SectionFsOffset":
                                    fsLine.FsOffset = long.Parse((string)reader.Value, System.Globalization.NumberStyles.HexNumber);
                                    break;
                                case "Position":
                                    fsLine.OffsetInItem = long.Parse((string)reader.Value, System.Globalization.NumberStyles.HexNumber);
                                    break;
                                case "FsSize":
                                case "Size":
                                    fsLine.FsSize = long.Parse((string)reader.Value, System.Globalization.NumberStyles.HexNumber);
                                    break;
                                case "CRC":
                                    fsLine.Crc = uint.Parse((string)reader.Value, System.Globalization.NumberStyles.HexNumber);
                                    break;
                                case "FullSize":
                                    fsLine.FullSize = long.Parse((string)reader.Value, System.Globalization.NumberStyles.HexNumber);
                                    break;
                                case "FullCRC":
                                    fsLine.FullCrc = uint.Parse((string)reader.Value, System.Globalization.NumberStyles.HexNumber);
                                    break;
                                case "XxHash":
                                    fsLine.XxHash = ulong.Parse((string)reader.Value, System.Globalization.NumberStyles.HexNumber);
                                    break;
                                case "DataType":
                                    fsLine.SetParseType((string)reader.Value, false);
                                    break;
                                case "File":
                                    fsLine.IsFile = true;
                                    List<IFsFile> files = area.FsInfo.FileSystem.Files;
                                    if (fsLine.OffsetInItem == 0)
                                    {
                                        fsFile = new FsFile() { Crc = fsLine.FullCrc, FsOffset = fsLine.AreaFsOffset, FsSize = fsLine.FullSize, FullName = (string)reader.Value, IsSystemFile = fsLine.SystemFile, XxHash = fsLine.XxHash, PostGapFsOffset = fsLine.AreaFsOffset + fsLine.FullSize };
                                        files.Add(fsFile);
                                    }
                                    else
                                        fsFile = (FsFile)files[files.Count - 1];
                                    section.Items.Add(new SectionItem(fsLine.ImageOffset, fsLine.AreaOffset, 0, fsFile) { File = fsLine, FileIndex = files.Count - 1 });
                                    break;
                                case "Gap":
                                    fsLine.IsFile = false;
                                    int idx = area.FsInfo.FileSystem.Files.Count - 1;
                                    fsFile = idx < 0 ? null : (FsFile)area.FsInfo.FileSystem.Files[idx];
                                    if (item == null || item.Gap != null)
                                        section.Items.Add(new SectionItem(fsLine.ImageOffset, fsLine.AreaOffset, 0, fsFile) { Gap = fsLine, FileIndex = idx });
                                    else
                                        item.Gap = fsLine;
                                    if (fsLine.OffsetInItem == 0 && fsFile != null)
                                    {
                                        fsFile.PostGapSize = fsLine.FullSize; //offset is set based on file offset + size
                                        fsFile.GapCrc = fsLine.FullCrc;
                                    }
                                    break;
                            }
                        } while (reader.MoveToNextAttribute());
                    }
                    break;
                case "Area":
                    if (reader.HasAttributes)
                    {
                        reader.MoveToFirstAttribute();
                        do
                        {
                            switch (reader.Name)
                            {
                                case "ImageOffset":
                                    area.ImageOffset = long.Parse((string)reader.Value, System.Globalization.NumberStyles.HexNumber);
                                    break;
                                case "Type":
                                    area.Type = (AreaType)Enum.Parse(typeof(AreaType), (string)reader.Value);
                                    getAreaProperties(scan, properties, area);
                                    area.AreaInfo.Setup(area.ImageOffset, area.Type);
                                    break;
                                case "Size":
                                    area.Size = long.Parse((string)reader.Value, System.Globalization.NumberStyles.HexNumber);
                                    break;
                                case "CRC":
                                    area.Crc = uint.Parse((string)reader.Value, System.Globalization.NumberStyles.HexNumber);
                                    break;
                                case "DecryptedCRC":
                                    area.CrcDecrypted = uint.Parse((string)reader.Value, System.Globalization.NumberStyles.HexNumber);
                                    break;
                            }
                        } while (reader.MoveToNextAttribute());
                    }
                    break;
                case "Section":
                    if (reader.HasAttributes)
                    {
                        reader.MoveToFirstAttribute();
                        do
                        {
                            switch (reader.Name)
                            {
                                case "ImageOffset":
                                    section.ImageOffset = long.Parse((string)reader.Value, System.Globalization.NumberStyles.HexNumber);
                                    break;
                                case "AreaOffset":
                                    section.AreaOffset = long.Parse((string)reader.Value, System.Globalization.NumberStyles.HexNumber);
                                    break;
                                case "Size":
                                    section.Size = long.Parse((string)reader.Value, System.Globalization.NumberStyles.HexNumber);
                                    break;
                                case "FsSize":
                                    section.FsSize = long.Parse((string)reader.Value, System.Globalization.NumberStyles.HexNumber);
                                    break;
                                case "CRC":
                                    section.Crc = uint.Parse((string)reader.Value, System.Globalization.NumberStyles.HexNumber);
                                    break;
                                case "DecryptedCRC":
                                    section.CrcDecrypted = uint.Parse((string)reader.Value, System.Globalization.NumberStyles.HexNumber);
                                    break;
                                case "XxHash":
                                    section.XxHash = ulong.Parse((string)reader.Value, System.Globalization.NumberStyles.HexNumber);
                                    break;
                                case "Valid":
                                    section.HashesValid = bool.Parse(reader.Value);
                                    break;
                                case "Creatable":
                                    section.IsCreatable = bool.Parse(reader.Value);
                                    break;
                                case "State":
                                    section.State = new BitState(((string)reader.Value).HexToBytes());
                                    break;
                                case "SeekIv":
                                    section.SeekIv = ((string)reader.Value).HexToBytes();
                                    break;
                                case "DataType": //used for Other areas where data should be NJunk or nulls etc
                                    SectionData gap = new SectionData(section.ImageOffset, area.AreaInfo, 0, section.Size) { Crc = section.Crc, XxHash = section.XxHash };
                                    gap.SetParseType((string)reader.Value, false);
                                    section.Items.Add(new SectionItem(section.ImageOffset, section.AreaOffset, 0, null) { Gap = gap });
                                    break;
                            }
                        } while (reader.MoveToNextAttribute());
                    }
                    break;
                case "NKitScan":
                    if (reader.HasAttributes)
                    {
                        scan.Properties["Format"] = reader.Name;
                        reader.MoveToFirstAttribute();
                        do
                        {
                            switch (reader.Name)
                            {
                                case "System":
                                    scan.Properties["System"] = (string)reader.Value;
                                    scan.SystemType = (SystemType)Enum.Parse(typeof(SystemType), (string)scan.Properties["System"]);
                                    break;
                                case "Media":
                                    scan.Properties["Media"] = (string)reader.Value;
                                    break;
                                case "Size":
                                    scan.Properties["Size"] = ulong.Parse((string)reader.Value, System.Globalization.NumberStyles.HexNumber);
                                    scan.Size = (long)(ulong)scan.Properties["Size"];
                                    break;
                                case "Type":
                                    scan.Properties["Type"] = (string)reader.Value;
                                    break;
                                case "CRC":
                                    scan.Properties["CRC"] = uint.Parse((string)reader.Value, System.Globalization.NumberStyles.HexNumber);
                                    scan.Crc = (uint)scan.Properties["CRC"];
                                    break;
                                case "DecryptedCRC":
                                    scan.Properties["DecryptedCRC"] = uint.Parse((string)reader.Value, System.Globalization.NumberStyles.HexNumber);
                                    scan.CrcDecrypted = (uint)scan.Properties["DecryptedCRC"];
                                    break;
                            }
                        } while (reader.MoveToNextAttribute());
                    }
                    break;
                case "Info":
                    if (reader.HasAttributes)
                    {
                        reader.MoveToFirstAttribute();
                        do
                        {
                            switch (reader.Name)
                            {
                                case "ImageOffset":
                                    gapInfo.ImageOffset = long.Parse((string)reader.Value, System.Globalization.NumberStyles.HexNumber);
                                    break;
                                case "AreaOffset":
                                    gapInfo.AreaOffset = long.Parse((string)reader.Value, System.Globalization.NumberStyles.HexNumber);
                                    if (area.AreaInfo.BlockSize == area.AreaInfo.BlockFsSize)
                                        gapInfo.AreaFsOffset = gapInfo.AreaOffset;
                                    break;
                                case "AreaFsOffset":
                                    gapInfo.AreaFsOffset = long.Parse((string)reader.Value, System.Globalization.NumberStyles.HexNumber);
                                    if (area.AreaInfo.BlockSize == area.AreaInfo.BlockFsSize)
                                        gapInfo.AreaOffset = gapInfo.AreaFsOffset;
                                    break;
                                case "SectionOffset":
                                    gapInfo.Offset = long.Parse((string)reader.Value, System.Globalization.NumberStyles.HexNumber);
                                    if (area.AreaInfo.BlockSize == area.AreaInfo.BlockFsSize)
                                        gapInfo.FsOffset = gapInfo.Offset;
                                    break;
                                case "SectionFsOffset":
                                    gapInfo.FsOffset = long.Parse((string)reader.Value, System.Globalization.NumberStyles.HexNumber);
                                    if (area.AreaInfo.BlockSize == area.AreaInfo.BlockFsSize)
                                        gapInfo.Offset = gapInfo.FsOffset;
                                    break;
                                case "Size":
                                case "FsSize":
                                    gapInfo.FsSize = long.Parse((string)reader.Value, System.Globalization.NumberStyles.HexNumber);
                                    break;
                                case "CRC":
                                    gapInfo.Crc = uint.Parse((string)reader.Value, System.Globalization.NumberStyles.HexNumber);
                                    break;
                                case "XxHash":
                                    gapInfo.XxHash = ulong.Parse((string)reader.Value, System.Globalization.NumberStyles.HexNumber);
                                    break;
                                case "DataType":
                                    gapInfo.SetParseType((string)reader.Value, true);
                                    break;
                            }
                        } while (reader.MoveToNextAttribute());
                    }
                    break;
                default:
                    break;
            }
        }

        private static void getAreaProperties(Scan scan, Dictionary<string, object> props, ScanArea sra)
        {
            //get this all typed rather than a dict :S
            if (sra.AreaInfo.Properties == null)
                sra.AreaInfo.Properties = new Properties(props.Keys.ToArray());

            if (sra.Type == AreaType.FileSystem && sra.FsInfo == null)
                sra.FsInfo = new FsFileSystemData() { ImageOffset = sra.ImageOffset, FileSystem = new FsFileSystem() { Files = new List<IFsFile>() } };

            int sectionSize = 0x8000 * 0x40; //new addition to scan so might need to calculate it

            switch (scan.SystemType)
            {
                case SystemType.GameCube:
                case SystemType.Wii:
                    switch (sra.Type)
                    {
                        case AreaType.ImageHeader:
                            sra.AreaInfo.Properties["ID"] = (string)props["ID"];
                            sra.AreaInfo.Properties["DiscNo"] = int.Parse((string)props["DiscNo"]);
                            sra.AreaInfo.Properties["Revision"] = int.Parse((string)props["Revision"]);
                            sra.AreaInfo.Properties["Region"] = (string)props["Region"];
                            sra.AreaInfo.Properties["Title"] = (string)props["Title"];
                            sra.AreaInfo.Properties["Partitions"] = int.Parse((string)props["Partitions"]);
                            break;
                        case AreaType.PartitionHeader:
                            sra.AreaInfo.Properties["Partition"] = int.Parse((string)props["Partition"]);
                            sra.AreaInfo.Properties["PartitionType"] = (string)props["PartitionType"];
                            sra.AreaInfo.Properties["ContentSha"] = (string)props["ContentSha"];
                            sra.AreaInfo.Properties["CommonKeyCrc"] = !props.ContainsKey("CommonKeyCrc") ? 0 : uint.Parse((string)props["CommonKeyCrc"], System.Globalization.NumberStyles.HexNumber);
                            sra.AreaInfo.Properties["TitleKeyCrc"] = !props.ContainsKey("TitleKeyCrc") ? 0 : uint.Parse((string)props["TitleKeyCrc"], System.Globalization.NumberStyles.HexNumber);
                            sra.AreaInfo.Properties["Signed"] = (string)props["Signed"];
                            break;
                        case AreaType.FileSystem:
                            if (scan.SystemType == SystemType.Wii)
                            {
                                sra.AreaInfo.Properties["Partition"] = int.Parse((string)props["Partition"]);
                                sra.AreaInfo.Properties["ID"] = (string)props["ID"];
                                sra.AreaInfo.Properties["DiscNo"] = int.Parse((string)props["DiscNo"]);
                                sra.AreaInfo.Properties["Revision"] = int.Parse((string)props["Revision"]);
                                sra.AreaInfo.Properties["Title"] = (string)props["Title"];
                                sra.AreaInfo.Properties["Encrypted"] = bool.Parse((string)props["Encrypted"]);
                                sra.AreaInfo.Properties["BlockSize"] = uint.Parse((string)props["BlockSize"], System.Globalization.NumberStyles.HexNumber);
                                sra.AreaInfo.Properties["HashSize"] = uint.Parse((string)props["HashSize"], System.Globalization.NumberStyles.HexNumber);
                                bool hasHashes = (int)(uint)sra.AreaInfo.Properties["HashSize"] != 0;
                                sra.AreaInfo.Properties["HasFileSystem"] = bool.Parse((string)props["HasFileSystem"]);
                                if ((bool)sra.AreaInfo.Properties["HasFileSystem"])
                                {
                                    sra.AreaInfo.Properties["SystemDataCrc"] = uint.Parse((string)props["SystemDataCrc"], System.Globalization.NumberStyles.HexNumber);
                                    sra.AreaInfo.Properties["JunkID"] = (string)props["JunkID"];
                                    sra.AreaInfo.Properties["JunkLeadingNulls"] = ulong.Parse((string)props["JunkLeadingNulls"], System.Globalization.NumberStyles.HexNumber);
                                    sra.AreaInfo.Properties["JunkEndNullsOffset"] = ulong.Parse((string)props["JunkEndNullsOffset"], System.Globalization.NumberStyles.HexNumber);
                                }
                                else
                                    ((FsFileSystemData)sra.FsInfo).InvalidFileSystem = false;
                                string type = (string)scan.Areas[sra.AreaInfo.AreaNo - 1].AreaInfo.Properties["PartitionType"];
                                if (Enum.IsDefined(typeof(PartitionType), type))
                                    ((FsFileSystemData)sra.FsInfo).Type = (PartitionType)Enum.Parse(typeof(PartitionType), type);
                                else
                                    ((FsFileSystemData)sra.FsInfo).Type = (PartitionType)Encoding.ASCII.GetBytes(type).ReadUInt32B(0);
                                if (props.ContainsKey("SectionSize"))
                                    sectionSize = (int)(uint)sra.AreaInfo.Properties["SectionSize"];
                                sra.AreaInfo.SetBlock((int)(uint)sra.AreaInfo.Properties["BlockSize"], (int)(uint)sra.AreaInfo.Properties["HashSize"], (int)(uint)sra.AreaInfo.Properties["BlockSize"] - (int)(uint)sra.AreaInfo.Properties["HashSize"], sectionSize);
                                sra.AreaInfo.SetSecurity((bool)sra.AreaInfo.Properties["Encrypted"], (string)scan.Properties["Type"] != "RVT-H", hasHashes);
                            }
                            else
                            {
                                sra.AreaInfo.Properties["ID"] = (string)props["ID"];
                                sra.AreaInfo.Properties["DiscNo"] = int.Parse((string)props["DiscNo"]);
                                sra.AreaInfo.Properties["Revision"] = int.Parse((string)props["Revision"]);
                                sra.AreaInfo.Properties["Region"] = (string)props["Region"];
                                sra.AreaInfo.Properties["Title"] = (string)props["Title"];
                                sra.AreaInfo.Properties["SystemDataCrc"] = uint.Parse((string)props["SystemDataCrc"], System.Globalization.NumberStyles.HexNumber);
                                sra.AreaInfo.Properties["JunkID"] = (string)props["JunkID"];
                                sra.AreaInfo.Properties["JunkLeadingNulls"] = ulong.Parse((string)props["JunkLeadingNulls"], System.Globalization.NumberStyles.HexNumber);
                                ((FsFileSystemData)sra.FsInfo).Type = PartitionType.Game;
                                ((FsFileSystemData)sra.FsInfo).InvalidFileSystem = false; sra.AreaInfo.SetBlock(WiiConsts.WiiSectorSize, 0, WiiConsts.WiiSectorSize, (int)WiiConsts.WiiGroupSize);
                                sra.AreaInfo.SetSecurity(false, false, false);
                            }
                            break;
                        case AreaType.Other:
                            sra.AreaInfo.Properties["UpdatePartitionRemoved"] = !props.ContainsKey("UpdatePartitionRemoved") ? false : bool.Parse((string)props["UpdatePartitionRemoved"]);
                            sra.AreaInfo.Properties["Partition"] = !props.ContainsKey("Partition") ? 0 : int.Parse((string)props["Partition"]);
                            if (props.ContainsKey("JunkID"))
                                sra.AreaInfo.Properties["JunkID"] = (string)props["JunkID"];
                            break;
                        default:
                            break;
                    }
                    break;
                case SystemType.WiiU:
                    switch (sra.Type)
                    {
                        case AreaType.ImageHeader:
                            sra.AreaInfo.Properties["ID"] = (string)props["ID"];
                            break;
                        case AreaType.PartitionTable:
                            sra.AreaInfo.Properties["Partitions"] = int.Parse((string)props["Partitions"]);
                            sra.AreaInfo.Properties["Encrypted"] = bool.Parse((string)props["Encrypted"]);
                            if (props.ContainsKey("KeyCrc"))
                                sra.AreaInfo.Properties["KeyCrc"] = uint.Parse((string)props["KeyCrc"], System.Globalization.NumberStyles.HexNumber);
                            sra.AreaInfo.SetSecurity((bool)sra.AreaInfo.Properties["Encrypted"], true, false);
                            break;
                        case AreaType.PartitionHeader:
                            sra.AreaInfo.Properties["Partition"] = int.Parse((string)props["Partition"]);
                            sra.AreaInfo.Properties["PartitionType"] = (string)props["PartitionType"];
                            sra.AreaInfo.Properties["VolumeId"] = (string)props["VolumeId"];
                            if (props.ContainsKey("TitleId"))
                                sra.AreaInfo.Properties["TitleId"] = (string)props["TitleId"];
                            if (props.ContainsKey("Signed"))
                                sra.AreaInfo.Properties["Signed"] = (string)props["Signed"];
                            break;
                        case AreaType.FstBlock:
                            if (props.ContainsKey("Partition"))
                                sra.AreaInfo.Properties["Partition"] = int.Parse((string)props["Partition"]);
                            if (props.ContainsKey("TmdVersion"))
                                sra.AreaInfo.Properties["TmdVersion"] = (string)props["App"];
                            sra.AreaInfo.Properties["ContentHeaders"] = int.Parse((string)props["ContentHeaders"]);
                            if (props.ContainsKey("App"))
                                sra.AreaInfo.Properties["App"] = (string)props["App"];
                            sra.AreaInfo.Properties["Encrypted"] = bool.Parse((string)props["Encrypted"]);
                            sra.AreaInfo.SetSecurity((bool)sra.AreaInfo.Properties["Encrypted"], true, false);
                            if ((bool)sra.AreaInfo.Properties["Encrypted"])
                            {
                                if (props.ContainsKey("TitleKeyCrc"))
                                    sra.AreaInfo.Properties["TitleKeyCrc"] = uint.Parse((string)props["TitleKeyCrc"], System.Globalization.NumberStyles.HexNumber);
                                if (props.ContainsKey("TitleKeyMissing"))
                                    sra.AreaInfo.Properties["TitleKeyMissing"] = bool.Parse((string)props["TitleKeyMissing"]);
                                if (props.ContainsKey("CommonKeyCrc"))
                                    sra.AreaInfo.Properties["CommonKeyCrc"] = uint.Parse((string)props["CommonKeyCrc"], System.Globalization.NumberStyles.HexNumber);
                            }
                            if (props.ContainsKey("TitleId"))
                                sra.AreaInfo.Properties["TitleId"] = (string)props["TitleId"];
                            if (props.ContainsKey("Signed"))
                                sra.AreaInfo.Properties["Signed"] = (string)props["Signed"];
                            break;
                        case AreaType.FileSystem:
                            sra.AreaInfo.Properties["Partition"] = int.Parse((string)props["Partition"]);
                            sra.AreaInfo.Properties["ContentIndex"] = int.Parse((string)props["ContentIndex"]);
                            if (props.ContainsKey("App"))
                                sra.AreaInfo.Properties["App"] = (string)props["App"];
                            sra.AreaInfo.Properties["Encrypted"] = bool.Parse((string)props["Encrypted"]);
                            sra.AreaInfo.Properties["BlockSize"] = uint.Parse((string)props["BlockSize"], System.Globalization.NumberStyles.HexNumber);
                            sra.AreaInfo.Properties["HashSize"] = uint.Parse((string)props["HashSize"], System.Globalization.NumberStyles.HexNumber);
                            bool hasHashes = (int)(uint)sra.AreaInfo.Properties["HashSize"] != 0;
                            if (hasHashes)
                                sra.AreaInfo.Properties["HashRoot"] = (string)props["HashRoot"];
                            if ((bool)sra.AreaInfo.Properties["Encrypted"])
                            {
                                if (props.ContainsKey("TitleKeyCrc"))
                                    sra.AreaInfo.Properties["TitleKeyCrc"] = uint.Parse((string)props["TitleKeyCrc"], System.Globalization.NumberStyles.HexNumber);
                                if (props.ContainsKey("CommonKeyCrc"))
                                    sra.AreaInfo.Properties["CommonKeyCrc"] = uint.Parse((string)props["CommonKeyCrc"], System.Globalization.NumberStyles.HexNumber);
                                if (props.ContainsKey("KeyCrc"))
                                    sra.AreaInfo.Properties["KeyCrc"] = uint.Parse((string)props["KeyCrc"], System.Globalization.NumberStyles.HexNumber);
                                sra.AreaInfo.SetSecurity((bool)sra.AreaInfo.Properties["Encrypted"], (bool)sra.AreaInfo.Properties["Encrypted"], hasHashes);
                                if (props.ContainsKey("SectionSize"))
                                    sectionSize = props.ContainsKey("SectionSize") ? (int)(uint)sra.AreaInfo.Properties["SectionSize"] : WiiUConsts.DefaultSectionSize;
                                sra.AreaInfo.SetBlock((int)(uint)sra.AreaInfo.Properties["BlockSize"], (int)(uint)sra.AreaInfo.Properties["HashSize"], (int)((uint)sra.AreaInfo.Properties["BlockSize"] - (uint)sra.AreaInfo.Properties["HashSize"]), sectionSize);
                            }
                            break;
                        case AreaType.Other:
                            sra.AreaInfo.Properties["Partition"] = int.Parse((string)props["Partition"]);
                            sra.AreaInfo.Properties["RepeatedContentIndex"] = int.Parse((string)props["RepeatedContentIndex"]);
                            sra.AreaInfo.Properties["Encrypted"] = bool.Parse((string)props["Encrypted"]);
                            sra.AreaInfo.Properties["BlockSize"] = uint.Parse((string)props["BlockSize"], System.Globalization.NumberStyles.HexNumber);
                            sra.AreaInfo.Properties["HashSize"] = uint.Parse((string)props["HashSize"], System.Globalization.NumberStyles.HexNumber);
                            bool hasHashes2 = (int)(uint)sra.AreaInfo.Properties["HashSize"] != 0;
                            sra.AreaInfo.SetSecurity((bool)sra.AreaInfo.Properties["Encrypted"], (bool)sra.AreaInfo.Properties["Encrypted"], hasHashes2);
                            sectionSize = props.ContainsKey("SectionSize") ? (int)(uint)sra.AreaInfo.Properties["SectionSize"] : WiiUConsts.DefaultSectionSize;
                            sra.AreaInfo.SetBlock((int)(uint)sra.AreaInfo.Properties["BlockSize"], (int)(uint)sra.AreaInfo.Properties["HashSize"], (int)((uint)sra.AreaInfo.Properties["BlockSize"] - (uint)sra.AreaInfo.Properties["HashSize"]), sectionSize);
                            break;
                        default:
                            break;
                    }
                    break;
                default:
                    switch (sra.Type)
                    {
                        case AreaType.FileSystem:
                            break;
                        case AreaType.Audio:
                            break;
                        case AreaType.Other:
                            break;
                        default:
                            break;
                    }
                    break;
            }

            //Properties p = new Properties(props.Keys.ToArray());
            //foreach (var kv in props)
            //    p[kv.Key] = kv.Value;
            //return p;
        }

        internal static string DataTypeToString(ISectionData itm, bool isFile, bool isSysFile)
        {
            if (isFile)
            {
                if (isSysFile)
                    return "SysData";
                else
                    return itm.DataType == DataType.NJunk ? string.Format(@"NJunk-{0}", itm.DataNulls.ToString("X2")) :
                           (itm.DataType == DataType.Fill && itm.FillByte == 0 ? @"Nulls" :
                           (itm.DataType == DataType.Fill ? string.Format(@"Fill-{0}", itm.FillByte.ToString("X2")) :
                           (itm.DataType == DataType.Mode2Fm2 ? @"Mode2Fm2" :
                             @"Data")));
            }
            else
                return itm.DataType == DataType.NJunk ? string.Format(@"NJunk-{0}", itm.DataNulls.ToString("X2")) :
                       (itm.DataType == DataType.Fill && itm.FillByte == 0 ? @"Nulls" :
                       (itm.DataType == DataType.Fill ? string.Format(@"Fill-{0}", itm.FillByte.ToString("X2")) :
                       @"Other"));
        }

        public static string ResultToString(Scan scan)
        {
            XmlWriterSettings xmlSet = new XmlWriterSettings() { Indent = true, NewLineChars = "\n" };

            using (StringWriter sbx = new Utf8StringWriter(new StringBuilder(0x400 * 0x400)))
            {
                using (XmlWriter xw = XmlWriter.Create(sbx, xmlSet))
                {

                    xw.WriteStartDocument();

                    xw.WriteStartElement("NKitScan");
                    xw.WriteAttributeString("Version", "1.0");

                    foreach (string key in scan.Properties.Keys)
                    {
                        object val = scan.Properties[key];
                        if (val != null)
                            xw.WriteAttributeString(key, val.ToXmlValue());
                    }

                    foreach (ScanArea sra in scan.Areas)
                    {
                        IFileSystemData fsData = sra.FsInfo;
                        ScanSection lastSrs = sra.Sections.LastOrDefault();

                        if ((sra.AreaInfo.Properties?.Keys?.Length ?? 0) != 0)
                        {
                            xw.WriteStartElement("AreaInfo");
                            xw.WriteAttributeString("Type", scan.SystemType.ToString());
                            foreach (string key in sra.AreaInfo.Properties.Keys)
                            {
                                object val = sra.AreaInfo.Properties[key];
                                if (val != null)
                                    xw.WriteAttributeString(key, val.ToXmlValue());
                            }
                            xw.WriteEndElement(); //AreaInfo
                        }

                        xw.WriteStartElement("Area");
                        xw.WriteAttributeString("ImageOffset", sra.ImageOffset.ToString("X9"));
                        xw.WriteAttributeString("Type", sra.Type.ToString());
                        xw.WriteAttributeString("Size", sra.Size.ToString("X9"));
                        xw.WriteAttributeString("CRC", sra.Crc.ToString("X8"));
                        if (sra.AreaInfo.IsEncrypted)
                            xw.WriteAttributeString("DecryptedCRC", sra.CrcDecrypted.ToString("X8"));

                        if (lastSrs != null) //has children
                        {
                            foreach (ScanSection srs in sra.Sections)
                            {
                                xw.WriteStartElement("Section");
                                ISectionItem lastSrf = srs.Items?.LastOrDefault();
                                SectionToWriter(xw, srs, lastSrf);

                                if (sra.Type == AreaType.FileSystem && lastSrf != null) //has children
                                {
                                    foreach (ISectionItem srf in srs.Items)
                                    {
                                        if (srf.File != null)
                                        {
                                            xw.WriteStartElement("File");
                                            ScanParser.SectionItemToWriter(xw, srf, srf.FsFile, true, sra.AreaInfo.BlockSize != sra.AreaInfo.BlockFsSize, sra.AreaInfo);
                                            xw.WriteEndElement(); //File
                                        }

                                        if (srf.Gap != null)
                                        {
                                            xw.WriteStartElement("Gap");
                                            ScanParser.SectionItemToWriter(xw, srf, srf.FsFile, false, sra.AreaInfo.BlockSize != sra.AreaInfo.BlockFsSize, sra.AreaInfo);

                                            ISectionData lastGapData = srf.GapInfo?.LastOrDefault();
                                            if (lastGapData != null && srf.Gap.DataType == DataType.Other && srf.GapInfo.Count > 1)
                                            {
                                                foreach (ISectionData gd in srf.GapInfo)
                                                {
                                                    xw.WriteStartElement("Info");
                                                    ScanParser.GapInfoToWriter(xw, gd, sra.AreaInfo);
                                                    xw.WriteEndElement(); //Info
                                                }
                                            }
                                            xw.WriteEndElement(); //Gap
                                        }
                                    }
                                }
                                else if (sra.Type == AreaType.Other && srs.Items.Count == 1 && lastSrf.GapInfo.Count > 1) //non filesystem with items
                                {
                                    ISectionData lastGapData = lastSrf.GapInfo?.LastOrDefault();
                                    foreach (ISectionData gd in lastSrf.GapInfo)
                                    {
                                        xw.WriteStartElement("Info");
                                        ScanParser.GapInfoToWriter(xw, gd, sra.AreaInfo);
                                        xw.WriteEndElement(); //Info
                                    }
                                }

                                xw.WriteEndElement(); //Section
                            }
                            xw.WriteEndElement(); //Area
                        }
                    }
                    xw.WriteEndElement(); //NKitScan
                    xw.WriteEndDocument();
                    xw.Dispose();
                }
                return sbx.ToString();
            }
        }

        internal static void SectionToWriter(XmlWriter xw, ScanSection srs, ISectionItem item)
        {
            xw.WriteAttributeString("ImageOffset", srs.ImageOffset.ToString("X9"));
            xw.WriteAttributeString("AreaOffset", srs.AreaOffset.ToString("X9"));
            xw.WriteAttributeString("Size", srs.Size.ToString("X8"));
            if (srs.ParentArea.AreaInfo.BlockSize != srs.ParentArea.AreaInfo.BlockFsSize)
                xw.WriteAttributeString("FsSize", srs.FsSize.ToString("X8"));
            xw.WriteAttributeString("CRC", srs.Crc.ToString("X8"));
            if (srs.ParentArea.AreaInfo.IsEncrypted)
                xw.WriteAttributeString("DecryptedCRC", srs.CrcDecrypted.ToString("X8"));
            xw.WriteAttributeString("XxHash", srs.XxHash.ToString("X16"));

            if (srs.ParentArea.Type != AreaType.FileSystem && srs.Items.Count == 1)
                xw.WriteAttributeString("DataType", ScanParser.DataTypeToString(srs.Items[0].Gap, false, false));

            if (srs.ParentArea.AreaInfo.BlockSize != srs.ParentArea.AreaInfo.BlockFsSize)
            {
                xw.WriteAttributeString("Valid", srs.HashesValid ? "true" : "false");
                xw.WriteAttributeString("Creatable", srs.IsCreatable ? "true" : "false");
            }
            if (srs.State != null && !srs.State.IsClear())
                xw.WriteAttributeString("State", srs.State.ToString());
            if (srs.SeekIv != null)
                xw.WriteAttributeString("SeekIv", srs.SeekIv.ToHexString());
        }

        internal static void SectionItemToWriter(XmlWriter xw, ISectionItem item, IFsFile fsFile, bool file, bool fsSizes, AreaInfo areaInfo)
        {
            ISectionData itm = file ? item.File : item.Gap;

            xw.WriteAttributeString("ImageOffset", itm.ImageOffset.ToString("X9"));
            if (item.AreaOffset != item.ImageOffset)
                xw.WriteAttributeString("AreaOffset", itm.AreaOffset.ToString("X9"));
            xw.WriteAttributeString("SectionOffset", itm.Offset.ToString("X8"));
            if (fsSizes)
            {
                xw.WriteAttributeString("AreaFsOffset", Buffer.OffsetToFsOffset(itm.AreaOffset, areaInfo.BlockSize, areaInfo.BlockFsOffset, areaInfo.BlockFsSize).ToString("X9"));
                xw.WriteAttributeString("SectionFsOffset", itm.FsOffset.ToString("X8"));
            }
            xw.WriteAttributeString("Position", itm.OffsetInItem.ToString("X9"));
            xw.WriteAttributeString(fsSizes ? "FsSize" : "Size", itm.FsSize.ToString("X8"));
            xw.WriteAttributeString("CRC", itm.Crc.ToString("X8"));

            bool writeXxHash = !file && item.Gap.DataType == DataType.Other && item.GapInfo.Count == 0; //not child items when 1 info group and the xxhash for validation/dedupe

            if (fsFile != null && itm.OffsetInItem == 0)
            {
                xw.WriteAttributeString("FullSize", (file ? fsFile.FsSize : fsFile.PostGapSize).ToString("X9"));
                xw.WriteAttributeString("FullCRC", (file ? fsFile.Crc : fsFile.GapCrc).ToString("X8"));
                if (file)
                    xw.WriteAttributeString("XxHash", fsFile.XxHash.ToString("X16"));
            }
            if (writeXxHash)
                xw.WriteAttributeString("XxHash", item.Gap.XxHash.ToString("X16"));

            string dt = ScanParser.DataTypeToString(itm, file, fsFile?.IsSystemFile ?? false);
            xw.WriteAttributeString("DataType", dt);

            xw.WriteAttributeString(file ? "File" : "Gap", fsFile?.FullName ?? "");

            if (file && item.FileSystems.Count != 0)
                xw.WriteAttributeString("FS", string.Join(", ", item.FileSystems));
            if (file && fsFile != null && itm.OffsetInItem == 0 && fsFile.SplitParts != null) //only on first item
            {
                xw.WriteAttributeString("SplitIndex", fsFile.SplitIndex.ToString());
                xw.WriteAttributeString("SplitParts", fsFile.SplitParts.Parts.Count.ToString());
                xw.WriteAttributeString("SplitPosition", fsFile.SplitParts.Parts[fsFile.SplitIndex].OffsetInFile.ToString("X9"));
                xw.WriteAttributeString("CombinedSize", fsFile.SplitParts.Size.ToString("X9"));
                xw.WriteAttributeString("CombinedCRC", fsFile.SplitParts.Crc.ToString("X8"));
                xw.WriteAttributeString("CombinedXxHash", fsFile.SplitParts.XxHash.ToString("X16"));
            }
        }

        internal static void GapInfoToWriter(XmlWriter xw, ISectionData data, AreaInfo areaInfo)
        {
            bool isFs = areaInfo.BlockSize != areaInfo.BlockFsSize;

            xw.WriteAttributeString("ImageOffset", data.ImageOffset.ToString("X9"));
            if (data.AreaOffset != data.ImageOffset)
                xw.WriteAttributeString("AreaOffset", data.AreaOffset.ToString("X9"));

            xw.WriteAttributeString("SectionOffset", data.Offset.ToString("X9"));
            if (isFs)
            {
                xw.WriteAttributeString("AreaFsOffset", Buffer.OffsetToFsOffset(data.AreaOffset, areaInfo.BlockSize, areaInfo.BlockFsOffset, areaInfo.BlockFsSize).ToString("X9"));
                xw.WriteAttributeString("SectionFsOffset", data.FsOffset.ToString("X8"));
            }
            xw.WriteAttributeString(isFs ? "FsSize" : "Size", data.FsSize.ToString("X8"));

            if (data.DataType == DataType.Other)
            {
                xw.WriteAttributeString("CRC", data.Crc.ToString("X8"));
                xw.WriteAttributeString("XxHash", data.XxHash.ToString("X8"));
            }

            xw.WriteAttributeString("DataType", ScanParser.DataTypeToString(data, false, false));
        }

        public static bool Equal(Scan scanA, Scan scanB)
        {
            if (scanA.Crc != scanB.Crc)
                return false;
            if (scanA.Areas.Count != scanB.Areas.Count)
                return false;
            for (int i = 0; i < scanA.Areas.Count; i++)
            {
                ScanArea areaA = scanA.Areas[i];
                ScanArea areaB = scanB.Areas[i];
                if (areaA.Crc != areaB.Crc)
                    return false;
                if (areaA.Sections.Count != areaB.Sections.Count)
                    return false;
                for (int s = 0; s < areaA.Sections.Count; s++)
                {
                    if (areaA.Sections[s].Crc != areaB.Sections[s].Crc)
                        return false;
                }
            }
            return true;
        }
    }

    internal class FsFolder : IFsFolder
    {
        public List<IFsFile> Files { get; set; }
        public List<IFsFolder> Folders { get; set; }
        public string Name { get; set; }
        public IFsFolder Parent { get; set; }
        public string Path
        {
            get
            {
                if (this.Parent == null || this.Parent.Parent == null)
                    return "/" + this.Name;
                return this.Parent?.Path + "/" + this.Name;
            }
        }
        public override string ToString() => Name ?? "";
    }

    internal class FsFile : IFsFile
    {
        public bool IsMissing => throw new NotImplementedException();
        public bool IsLastFile { get; set; }
        public string Name { get; set; }
        public string FullName { get; set; }
        public IFsFolder Parent { get; set; }
        public string Path { get; set; }
        public long FsSize { get; set; }
        public ulong XxHash { get; set; }
        public uint Crc { get; set; }
        public uint GapCrc { get; set; }
        public bool IsSystemFile { get; set; }
        public long FsOffset { get; set; }
        public long PostGapSize { get; set; }
        public long PostGapFsOffset { get; set; }
        public int SplitIndex { get; set; }
        public IFsFileParts SplitParts { get; set; }
        public IFsFile Clone() => throw new NotImplementedException();
        public override string ToString() => string.Format("{0} : {1} : {2}", FsOffset.ToString("X8"), FsSize.ToString("X8"), Name);
    }

    internal class FsFileSystemData : IFileSystemData
    {
        public long ImageOffset { get; set; }
        public long Size { get; set; }
        public IFileSystem FileSystem { get; set; }
        public bool InvalidFileSystem { get; set; }
        public PartitionType Type { get; set; }
        public bool AllFoldersParsed { get; set; }
        public FidelityFileList FidelityFiles { get; set; }

        private IAreaFileSystemView _areaView;
        public IAreaFileSystemView AreaView => _areaView ??= AreaFileSystemView.TryBuild(this);
    }
    internal class FsFileSystem : IFileSystem
    {
        public List<IFsFile> Files { get; set; }
        public IFsFolder Root { get; set; }
        public List<IFsFile> CloneFiles() => throw new NotImplementedException();
    }
}