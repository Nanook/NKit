using Nanook.NKit.Settings;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Nanook.NKit.Iso.Iso9660
{

    internal class DreamcastFixData : IFixData
    {
        private Dictionary<object, object> _yaml;

        public DreamcastAudioOffsets Image { get; private set; }
        public bool YamlLoaded { get; private set; }

        public void Load(SystemType system, FileInfo info, string filesPath)
        {
            if (!(info?.Exists ?? false))
                return;

            try
            {
                string yamlContent = File.ReadAllText(info.FullName);
                _yaml = AotYamlDeserializer.DeserializeFixFile(yamlContent);
            }
            catch (Exception ex)
            {
                throw new HandledException(ex, $"Failed to read fixData: {info.FullName}");
            }
            this.YamlLoaded = true;
        }

        public void SetDisc(byte[] header, int offset, uint? headerCrc)
        {
            string h = Encoding.ASCII.GetString(header, offset, 0x50);
            string id = $"{h.Substring(0x40, 0xa).TrimEnd()}_{h.Substring(0x4a, 0x6)}_{(headerCrc == null ? h.Substring(0x2b, 0x1) : headerCrc.Value.ToString("X8"))}";
            this.Image = new DreamcastAudioOffsets(id); //no discs
            try
            {
                if (_yaml != null && _yaml.ContainsKey("audioOffsets"))
                {
                    Dictionary<object, object> types = (Dictionary<object, object>)_yaml["audioOffsets"];
                    foreach (KeyValuePair<object, object> kv in types)
                        readOffsetItems((string)kv.Key, (Dictionary<object, object>)kv.Value, id);
                }
            }
            catch (Exception ex)
            {
                throw new HandledException(ex, $"Failed to read fixData for: {id}");
            }
        }

        private void readOffsetItems(string name, Dictionary<object, object> parent, string id)
        {
            try
            {
                if (parent.TryGetValue(id, out object v))
                {
                    foreach (Dictionary<object, object> x in (List<object>)v)
                    {
                        Dictionary<int, int> discs = new Dictionary<int, int>();
                        if (name == "tosec")
                            this.Image.TosecDiscs.Add(discs);
                        else if (name == "redump")
                            this.Image.RedumpDiscs.Add(discs);
                        foreach (KeyValuePair<object, object> kv in x)
                            discs.Add(int.Parse((string)kv.Key), int.Parse((string)kv.Value, NumberStyles.HexNumber));
                    }
                    return;
                }
            }
            catch { }
        }

    }

    internal class DreamcastAudioOffsets
    {
        public string Id { get; private set; }
        public List<Dictionary<int, int>> TosecDiscs { get; private set; }
        public List<Dictionary<int, int>> RedumpDiscs { get; private set; }

        public DreamcastAudioOffsets(string id)
        {
            this.Id = id;
            this.TosecDiscs = new List<Dictionary<int, int>>();
            this.RedumpDiscs = new List<Dictionary<int, int>>();
        }

        public int FindDisc(int trackNumber, int offset, bool tosec)
        {
            List<Dictionary<int, int>> discs = tosec ? this.TosecDiscs : this.RedumpDiscs;
            if (discs.Count == 0)
                return -1;

            for (int i = 0; i < discs.Count; i++)
            {
                if (this.GetTrackOffset(i, trackNumber, tosec) == offset)
                    return i; //matching disc
            }
            return 0; //disc 0
        }

        public int GetTrackOffset(int discIdx, int trackNumber, bool tosec)
        {
            List<Dictionary<int, int>> discs = tosec ? this.TosecDiscs : this.RedumpDiscs;

            if (discs == null || discs.Count == 0)
                return 0;

            if (discs[discIdx].ContainsKey(trackNumber))
                return discs[discIdx][trackNumber];
            else if (discs[discIdx].ContainsKey(0))
                return discs[discIdx][0];
            else
                return 0; //default audio offset
        }

    }
}