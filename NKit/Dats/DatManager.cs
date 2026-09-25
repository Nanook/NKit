using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public enum DatCollectionType { Manual, Redump, Tosec, NoIntro }

namespace Nanook.NKit.Dats
{
    public class DatManager
    {
        private Dictionary<DatCollectionType, string> _registeredCollections;
        private Dictionary<SystemType, string> _registeredSystemDats;

        private Dictionary<DatCollectionType, Dictionary<SystemType, string>> _collectionMasks;
        private List<Dat> _datFiles;

        public DatManager()
        {
            _registeredCollections = new Dictionary<DatCollectionType, string>();
            _registeredSystemDats = new Dictionary<SystemType, string>();

            _collectionMasks = new Dictionary<DatCollectionType, Dictionary<SystemType, string>>() {
                {
                    DatCollectionType.Redump, new Dictionary<SystemType, string>()
                    {
                        { SystemType.PcEngine, "NEC - PC Engine CD & TurboGrafx CD - Datfile*.dat" },
                        { SystemType.GameCube, "Nintendo - GameCube - Datfile*.dat" },
                        { SystemType.Wii, "Nintendo - Wii - Datfile*.dat" },
                        { SystemType.WiiU, "Nintendo - Wii U - Datfile*.dat" },
                        { SystemType.CDi, "Philips - CD-i - Datfile*.dat" },
                        { SystemType.Dreamcast, "Sega - Dreamcast - Datfile*.dat" },
                        { SystemType.SegaCD, "Sega - Mega CD & Sega CD - Datfile*.dat" },
                        { SystemType.Saturn, "Sega - Saturn - Datfile*.dat" },
                        { SystemType.PS1, "Sony - PlayStation - Datfile*.dat" },
                        { SystemType.PS2, "Sony - PlayStation 2 - Datfile*.dat" },
                        { SystemType.PS3, "Sony - PlayStation 3 - Datfile*.dat" },
                        { SystemType.PSP, "Sony - PlayStation Portable - Datfile*.dat" }
                    }
                },
                {
                    DatCollectionType.Tosec, new Dictionary<SystemType, string>()
                    {
                        { SystemType.PcEngine, "NEC PC-Engine CD & TurboGrafx-16 CD - *.dat" },
                        { SystemType.GameCube, "Nintendo GameCube - *.dat" },
                        { SystemType.CDi, "Philips CD-i - *.dat" },
                        { SystemType.Dreamcast, "Sega Dreamcast - *.dat" },
                        { SystemType.SegaCD, "Sega Mega-CD & Sega CD - *.dat" },
                        { SystemType.Saturn, "Sega Saturn - *.dat" },
                        { SystemType.PS1, "Sony PlayStation - *.dat" },
                        { SystemType.PS2, "Sony PlayStation 2 - *.dat" },
                        { SystemType.PSP, "Sony PlayStation Portable - *.dat" }
                    }
                },
                {
                    DatCollectionType.NoIntro, new Dictionary<SystemType, string>()
                    {
                        { SystemType.WiiU, "Nintendo - Wii U *.dat" },
                    }
                }
            };
            _datFiles = new List<Dat>();
        }

        public override string ToString() => $"{_datFiles.Count} File{_datFiles.Count.s()}";

        public void Register(string collectionType, string path)
        {
            DatCollectionType dct;
            if (!Enum.TryParse(collectionType, true, out dct))
                return; //not recognised

            if (!_collectionMasks.ContainsKey(dct))
                return; //we have no masks

            if (!_registeredCollections.ContainsKey(dct))
                _registeredCollections.Add(dct, path);
            else
                _registeredCollections[dct] = path;
        }

        public void Register(SystemType systemType, string path)
        {
            if (!_registeredSystemDats.ContainsKey(systemType))
                _registeredSystemDats.Add(systemType, path);
            else
                _registeredSystemDats[systemType] = path;
        }

        public void LoadDats(SystemType system)
        {
            foreach (KeyValuePair<DatCollectionType, string> collection in _registeredCollections)
            {
                Dictionary<SystemType, string> masks = _collectionMasks[collection.Key]; //get the system masks
                string collectionPath = collection.Value;

                if (!string.IsNullOrWhiteSpace(collectionPath))
                {
                    if (!masks.ContainsKey(system))
                        continue;

                    FileMask datMask = FileMask.CreateLocalMask(Directory.Exists(collectionPath) ? Path.Combine(collectionPath, masks[system]) : $"{collectionPath}//{masks[system]}", false);
                    foreach (FileItem fi in SourceFileSystem.GetLocalArchiveFiles(datMask, true, null, null).Where(a => a.IsMatch)) //checks within archives
                    {
                        if (!_datFiles.Exists(a => a.CollectionType == collection.Key && a.SystemType == system && a.FileName == fi.FileName))
                        {
                            using (SourceFileSystemReader rdr = SourceFileSystem.CreateReader(fi, null, null))
                                _datFiles.Add(Dat.ReadLogixDat(collection.Key, system, rdr.OpenRead(fi), fi.FileName)); //return first matching
                        }
                    }
                }
            }

            if (_registeredSystemDats.ContainsKey(system) && !string.IsNullOrWhiteSpace(_registeredSystemDats[system]))
            {
                FileMask datMask = FileMask.CreateLocalMask(_registeredSystemDats[system], false);
                foreach (FileItem fi in SourceFileSystem.GetLocalArchiveFiles(datMask, true, null, null).Where(a => a.IsMatch)) //checks within archives
                {
                    if (!_datFiles.Exists(a => a.CollectionType == DatCollectionType.Manual && a.SystemType == system && a.FileName == fi.FileName))
                    {
                        using (SourceFileSystemReader rdr = SourceFileSystem.CreateReader(fi, null, null))
                            _datFiles.Add(Dat.ReadLogixDat(DatCollectionType.Manual, system, rdr.OpenRead(fi), fi.FileName)); //return first matching
                    }
                }
            }

        }

        public DatItem FindMatch(Predicate<DatItem> match)
        {
            foreach (Dat dat in _datFiles)
            {
                DatItem item = dat.Items.FirstOrDefault(a => match(a));
                if (item != null)
                    return item;
            }
            return null;
        }

        public DatItem FindImageMatch(uint crc, long size) => this.FindMatch(a => a.Bins[0].Checksums.Crc == crc && a.Size == size);
        public DatItem FindImageMatch(uint crc) => this.FindMatch(a => a.Bins[0].Checksums.Crc == crc);

        internal DatItem FindByName(string name)
        {
            foreach (Dat dat in _datFiles)
            {
                DatItem item = dat.GetItem(name);
                if (item != null)
                    return item;
            }
            return null;
        }

        internal int ItemsCount() => _datFiles.Sum(a => a.Items.Count);
    }
}