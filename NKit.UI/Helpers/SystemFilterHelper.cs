using Nanook.NKit;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;

namespace NKit.Ui.Helpers;

internal static class SystemFilterHelper
{
    // Not currently supported
    //SystemType.Jaguar,
    //SystemType.PS4,
    //SystemType.XBox,
    //SystemType.XBox360,

    private static Dictionary<string, List<SystemType>> _associatedValues = new Dictionary<string, List<SystemType>>
    {
        { "Playstation", new List<SystemType> { SystemType.PS1, SystemType.PS2, SystemType.PS3, SystemType.PSP } },
        { "Sony", new List<SystemType> { SystemType.PS1, SystemType.PS2, SystemType.PS3, SystemType.PSP } },
        { "Microsoft", new List<SystemType> { SystemType.XBox, SystemType.XBox360, } },
        { "Nintendo", new List<SystemType> { SystemType.GameCube, SystemType.Wii, SystemType.WiiU } },
        { "Sega", new List<SystemType> { SystemType.Dreamcast, SystemType.Saturn, SystemType.SegaCD } },
        { "NEC", new List<SystemType> { SystemType.PcEngine} },
        { "Phillips", new List<SystemType> { SystemType.CDi } },
        { "ISO", new List<SystemType> { SystemType.Default } },
        { "9660", new List<SystemType> { SystemType.Default } },
        { "Mega", new List<SystemType> { SystemType.SegaCD } },
    };

    public static ObservableCollection<SystemType> GetSystemsForSearchTerm(ObservableCollection<SystemType> systems, string searchTerm)
    {
        OrderedDictionary systemsDictionary = new OrderedDictionary();

        systems.ForEach(x => systemsDictionary.Add(x, false));

        string[] searchTerms = searchTerm.Split(' ');

        foreach (string searchTermPart in searchTerms)
        {
            if (string.IsNullOrWhiteSpace(searchTermPart))
                break;

            foreach (SystemType system in systems.ToList())
            {
                if (system.ToString().Contains(searchTermPart, StringComparison.InvariantCultureIgnoreCase))
                {
                    systemsDictionary[system] = true;
                }
            }

            foreach (string key in _associatedValues.Keys)
            {
                if (key.Contains(searchTermPart, StringComparison.InvariantCultureIgnoreCase))
                {
                    foreach (SystemType system in _associatedValues[key])
                    {
                        systemsDictionary[system] = true;
                    }
                }
            }
        }

        ObservableCollection<SystemType> matchedSystemTypes = new ObservableCollection<SystemType>();

        foreach (DictionaryEntry item in systemsDictionary)
        {
            SystemType systemType = (SystemType)item.Key;
            bool isMatch = (bool)item.Value;

            if (isMatch)
            {
                matchedSystemTypes.Add(systemType);
            }
        }

        return matchedSystemTypes;
    }
}