using Nanook.NKit.Dats;
using System;
using System.IO;

namespace Nanook.NKit
{
    //provides dats and key lookups without needing passing full sets of settings around
    internal interface IDataProvider
    {
        string DedupePath { get; }
        Scan GetNKitScan(string imagePathFileName);
        void LoadFixData(IFixData data);
        T FixData<T>() where T : class, IFixData;
        FileInfo GetFixFile();
        byte[] GetKey(uint crc);
        byte[] GetKey(string imageName, string fallbackPath);
        byte[][] AllKeys { get; }
        DatItem GetDat(Predicate<DatItem> match);
    }
}