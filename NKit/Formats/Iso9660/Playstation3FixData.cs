using Nanook.NKit.Settings;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace Nanook.NKit.Iso.Iso9660
{
    internal class Playstation3FixDataFile
    {
        public byte[] Md5;
        public string FullFileName;
        public long Size;
    }

    internal class Playstation3FixData : IFixData
    {
        private Dictionary<object, object> _yaml;
        private string _filesPath;
        private List<FixIrd> _irds;
        private Dictionary<long, List<Playstation3FixDataFile>> _cache; //size, file, md5, 
        private Dictionary<uint, long> _layerbreaks;

        public bool YamlLoaded { get; private set; }

        public void Load(SystemType system, FileInfo info, string filesPath)
        {
            _filesPath = filesPath;
            _irds = new List<FixIrd>();
            _cache = new Dictionary<long, List<Playstation3FixDataFile>>();

            if (info?.Exists ?? false)
            {
                try
                {
                    string yamlContent = File.ReadAllText(info.FullName);
                    _yaml = AotYamlDeserializer.DeserializeFixFile(yamlContent);
                }
                catch (Exception ex)
                {
                    throw new HandledException(ex, $"Failed to read fixData: {info.FullName}");
                }

                if (_yaml != null && _yaml.ContainsKey("layerbreaks"))
                {
                    Dictionary<object, object> types = (Dictionary<object, object>)_yaml["layerbreaks"];
                    _layerbreaks = new Dictionary<uint, long>();
                    foreach (KeyValuePair<object, object> kv in types)
                        _layerbreaks.Add(uint.Parse((string)kv.Key, NumberStyles.HexNumber), long.Parse((string)kv.Value));
                }
                this.YamlLoaded = true;
            }
        }

        public void LoadFixFiles()
        {
            if (Directory.Exists(_filesPath))
            {
                DirectoryInfo fixFolder = new DirectoryInfo(_filesPath);
                foreach (FileInfo fi in fixFolder.EnumerateFiles("*", SearchOption.AllDirectories))
                {
                    if (fi.Directory.FullName == fixFolder.FullName)
                    {
                        if (string.Compare(fi.Extension, ".ird", true) == 0)
                        {
                            FixIrd ird = FixIrd.Read(fi.FullName);
                            if (ird != null)
                                _irds.Add(ird);
                        }
                    }
                    else
                    {
                        List<Playstation3FixDataFile> files;
                        if (!_cache.TryGetValue(fi.Length, out files))
                            _cache.Add(fi.Length, files = new List<Playstation3FixDataFile>());

                        Playstation3FixDataFile file = files.FirstOrDefault(a => a.FullFileName == fi.FullName);
                        if (file == null)
                            files.Add(new Playstation3FixDataFile() { FullFileName = fi.FullName, Size = fi.Length });
                    }
                }

            }
        }

        public byte[] LookupFixFile(long size, byte[] md5, out string fixFileName)
        {
            List<Playstation3FixDataFile> files;
            fixFileName = null;
            if (!_cache.TryGetValue(size, out files))
                return null;

            foreach (Playstation3FixDataFile file in files)
            {
                byte[] data = null;
                if (file.Md5 == null)
                {
                    data = File.ReadAllBytes(file.FullFileName);
                    using (MD5 m = MD5.Create())
                        file.Md5 = m.ComputeHash(data);
                }

                if (file.Md5.Equals(0, md5, 0, file.Md5.Length))
                {
                    fixFileName = file.FullFileName;
                    return data ?? File.ReadAllBytes(file.FullFileName);
                }
            }
            return null;
        }

        public void SetDisc(uint headerCrc)
        {
        }
        /*  From Deterous:

            How to map PS3 files to IRD:
            - Read \\PS3_DISC.SFB retrieve TITLE_ID field from it, remove the hyphen from it BLES-12345 -> BLES12345
            - Read \\PS3_GAME\\PARAM.SFO, retrieve TITLE_ID fileld from it (e.g. BLES12345), if it differs from PS3_DISC.SFB then keep both of these serials.
            - Iterate through every IRD file, compare the above IDs against the IRD's 6th to 14th byte (ASCII). If it matches one of the above title IDs, then it is a candidate IRD.
            - If there are more than one unique* candidate IRDs, read the ExtraConfig field of the IRD to see if it is a ISOTools IRD (ExtraConfig == 0x00) or irdkit IRD (ExtraConfig == 0x01).
              - If it is ISOTools IRD, make sure the TITLE_ID matches the \\PS3_GAME\\PARAM.SFO TITLE_ID. You can then compare \\PS3_GAME\\PARAM.SFO's VERSION against the IRD's DiscVersion.
              - If it is an irdkit IRD, make sure the TITLE_ID matches the \\PS3_DISC.SFB TITLE_ID. If the \\PS3_DISC.SFB contains a VERSION field, then the IRD's DiscVersion should match that version. If it does not, then the IRD's DiscVersion should match \\PS3_GAME\\PARAM.SFO's VERSION.
            - If you still have more than one candidate, you can also compare PARAM.SFO's TITLE, PS3_SYSTEM_VER and APP_VER with IRD's Title, SystemVersion, and AppVersion fields.
            - If still more than one unique* candidate IRD, then you'll need to look at the other files, not just SFB/SFO

            *Note: IRDs can be different but refer to the same ISO, so you'll have to parse the IRD fields and compare them all to see if there are any functional differences. Specifically you have to compare DiscKey (Data1Key), Header/HeaderLength, Footer/FooterLength, RegionCount/RegionHashes, FileCount/FileOffsets/FileHashes. 
         */

        public FixIrd[] MatchIrds(string sfbTitleId, string sfbVersion, string sfoTitleId, string sfoTitle, string sfoVersion, string sfoSysVersion, string sfoAppVersion)
        {
            sfbTitleId = sfbTitleId.Replace("-", "");
            sfoSysVersion = sfoSysVersion.Trim('0');
            List<FixIrd> matches = _irds.Where(a => a.TitleId == sfbTitleId || a.TitleId == sfoTitleId)
                                        .OrderBy(a =>
                                        {
                                            bool extraMatch = a.SysVersion == sfoSysVersion && a.AppVersion == sfoAppVersion;
                                            if (a.ExtraConfig == 1) //irdkit, more accurate values
                                            {
                                                if (a.TitleId == sfbTitleId)
                                                {
                                                    if ((sfbVersion ?? sfoVersion) == a.DiscVersion)
                                                        return extraMatch ? 0 : 4; //best match - SFB TitleId / DiscVersion / SysVersion / AppVersion
                                                    else
                                                        return extraMatch ? 2 : 6;
                                                }
                                            }
                                            //else //isotools etc
                                            //{
                                            if (a.TitleId == sfoTitleId)
                                            {
                                                if (sfoVersion == a.DiscVersion)
                                                    return extraMatch ? 1 : 5; //good match - SFO TitleId / DiscVersion / SysVersion / AppVersion
                                                else
                                                    return extraMatch ? 3 : 7;
                                            }
                                            //}
                                            return 10;
                                        }).ToList();

            return matches.ToArray();
        }

        public long? GetLayerbreak(uint crc)
        {
            if (_layerbreaks.TryGetValue(crc, out long layerbreak))
                return layerbreak;

            return null;
        }

    }

}