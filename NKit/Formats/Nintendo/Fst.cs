using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Nanook.NKit.Nintendo
{
    /// <summary>Outcome of parsing an FST, letting callers handle a known-bad FST without exceptions.</summary>
    internal enum FstParseStatus
    {
        Ok = 0,
        /// <summary>An entry pointed to offset 0 (a hacked/invalid FST, e.g. the FreeLoader disc).</summary>
        InvalidEntry = 1,
    }

    internal class Fst : IFileSystem
    {
        private Fst(FstFolder root)
        {
            Root = root;
            Files = new List<IFsFile>(recurseFolders(root, new List<IFsFile>()).OrderBy(a => ((FstFile)a).WiiUAppIndex).ThenBy(a => ((FstFile)a).FsOffset).ThenBy(a => a.FsSize).ToList());
            if (Files.Count != 0)
                ((FstFile)Files.Last()).IsLastFile = true;
        }

        public static List<IFsFile> CloneFiles(IEnumerable<IFsFile> files)
        {
            if (files == null)
                return null;

            return new List<IFsFile>(files.Select(a => a.Clone()).ToList());
        }

        public List<IFsFile> CloneFiles() => CloneFiles(Files);

        public IFsFolder Root { get; private set; }
        public List<IFsFile> Files { get; private set; }

        private List<IFsFile> recurseFolders(FstFolder folder, List<IFsFile> files)
        {
            files.AddRange(folder.Files);

            foreach (FstFolder fl in folder.Folders)
                recurseFolders(fl, files);

            return files;
        }

        public static Fst ParseWiiU(byte[] fstData, int baseOffset, long multiplier)
        {
            FstFolder fld = new FstFolder(null, "");

            //if (systemFiles != null)
            //{
            //    foreach (FstFile f in systemFiles)
            //        fld.Files.Add(f);
            //}

            long nFiles = fstData.ReadUInt32B(baseOffset + 0x8);
            if (0x10 * nFiles > fstData.Length)
                return null;

            bool invalid = false;
            recurseFst(fstData, fld, 0x10 * nFiles, baseOffset, 0, multiplier, 0x10, true, ref invalid);

            return new Fst(fld);
        }

        /// <summary>
        /// Parse an FST. Throws on a known-bad FST (an entry pointing to offset 0). Retained for
        /// callers that treat that as an exceptional condition; new callers that want to continue
        /// should use the <see cref="FstParseStatus"/> overload instead.
        /// </summary>
        public static Fst Parse(byte[] fstData, int baseOffset, IEnumerable<IFsFile> systemFiles, long multiplier)
        {
            Fst fst = Parse(fstData, baseOffset, systemFiles, multiplier, out FstParseStatus status);
            if (status == FstParseStatus.InvalidEntry)
                throw new Exception("Invalid fst entry pointing to offset 0"); //legit error with Freeloader USA to set the fst invalid
            return fst;
        }

        /// <summary>
        /// Parse an FST, reporting a <see cref="FstParseStatus"/> instead of throwing on a
        /// known-bad FST. On <see cref="FstParseStatus.InvalidEntry"/> the returned Fst contains
        /// the files parsed up to the bad entry; callers decide whether to treat the filesystem as
        /// invalid and continue. Returns null when the entry count is impossible for the data size.
        /// </summary>
        public static Fst Parse(byte[] fstData, int baseOffset, IEnumerable<IFsFile> systemFiles, long multiplier, out FstParseStatus status)
        {
            status = FstParseStatus.Ok;

            FstFolder fld = new FstFolder(null, "");

            if (systemFiles != null)
            {
                foreach (IFsFile f in systemFiles)
                    fld.Files.Add(f);
            }

            long nFiles = fstData.ReadUInt32B(baseOffset + 0x8);
            if (0x0c * nFiles > fstData.Length)
                return null;

            bool invalid = false;
            recurseFst(fstData, fld, 0x0c * nFiles, baseOffset, 0, multiplier, 0x0c, false, ref invalid);
            if (invalid)
                status = FstParseStatus.InvalidEntry;

            return new Fst(fld);
        }


        private static uint recurseFst(byte[] fstData, FstFolder folder, long names, int baseOffset, uint i, long multiplier, int entryLen, bool isWiiU, ref bool invalid)
        {
            uint j;
            uint hdr = fstData.ReadUInt32B((int)(baseOffset + (entryLen * i)));
            long name = names + (hdr & 0x00ffffffL);
            int type = (int)(hdr >> 24);
            string nm = fstData.ReadStringToNull(baseOffset + (int)name, Encoding.GetEncoding("Shift-JIS"));
            uint size = fstData.ReadUInt32B((int)(baseOffset + ((entryLen * i) + 0x8)));
            ushort wiiUPermission = 0;
            ushort wiiUSectionNo = 0;
            bool wiiUNotInNus = (type & 0x80) == 0x80;
            if (isWiiU)
            {
                wiiUPermission = fstData.ReadUInt16B((int)(baseOffset + ((entryLen * i) + 0x0c)));
                wiiUSectionNo = fstData.ReadUInt16B((int)(baseOffset + ((entryLen * i) + 0x0e)));
            }

            if ((type & 0x01) == 1)
            {
                FstFolder f = i == 0 ? folder : new FstFolder(folder, nm, wiiUPermission, wiiUSectionNo, wiiUNotInNus);
                if (i != 0)
                    folder.Folders.Add(f);

                for (j = i + 1; j < size;)
                    j = recurseFst(fstData, f, names, baseOffset, j, multiplier, entryLen, isWiiU, ref invalid);

                return size;
            }
            else if (!wiiUNotInNus)
            {
                long off = fstData.ReadUInt32B((int)(baseOffset + ((entryLen * i) + 0x4))) * multiplier; //offset in data
                if (off == 0 && !isWiiU)
                {
                    // A file at offset 0 is a legit hacked-FST marker (e.g. the FreeLoader disc):
                    // flag the FST as invalid and stop adding entries. Callers using the
                    // status-returning Parse overload continue with the filesystem marked invalid;
                    // the throwing Parse overload turns this flag into the historical exception.
                    invalid = true;
                    return i + 1;
                }

                folder.Files.Add(new FstFile(folder, nm, off, size, (int)((entryLen * i) + 0x4), isWiiU, wiiUPermission, wiiUSectionNo, wiiUNotInNus, false, false));
            }
            return i + 1;
        }

        internal void SetJunkFile(int outFileIdx)
        {
            FstFile outF = (FstFile)Files[outFileIdx];

            FstFile lastF;
            int lastIdx = outFileIdx;
            while ((lastF = (FstFile)Files[--lastIdx]).IsMissing)
            {
                ;  //will never fail as fst cannot be missing
            }

            int nulls = (int)Math.Max(0, WiiGc.WiiConsts.DataNullsCount + lastF.Analysis.Aligning - (outF.FsOffset - lastF.PostGapFsOffset));

            outF.IsMissing = true;
            outF.FsSize = outF.Analysis.JunkFileSize;
            outF.Analysis.FsOffset += outF.Analysis.JunkFileSize;

            outF.Analysis.JunkNulls = (int)Math.Min(outF.FsSize, nulls);
            outF.Analysis.ExpectedNulls = Math.Min(outF.Analysis.ExpectedNulls, Math.Max(0, nulls - outF.Analysis.JunkNulls)); //Expected will be 0 if big gap and not fst
            outF.Analysis.Aligning = (int)(outF.FsSize % 4L == 0L ? 0L : (4L - (outF.FsSize % 4L)));
        }
    }
}