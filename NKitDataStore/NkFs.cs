using System.Buffers.Binary;
using System.Text;

namespace NKitDataStore
{
    /// <summary>
    /// The 12-byte header at the start of the NkFs binary format.
    /// 
    /// Binary layout (big-endian):
    ///   Offset 0x00 (4 bytes): Magic number 0x4E4B4653 ("NKFS")
    ///   Offset 0x04 (2 bytes): Format version (currently 1)
    ///   Offset 0x06 (2 bytes): Reserved (must be 0)
    ///   Offset 0x08 (4 bytes): Total entry count (including root)
    /// 
    /// StringTableOffset is computed: 12 + EntryCount * 12 (no field in header).
    /// </summary>
    internal readonly struct NkFsHeader
    {
        public const int HeaderSize = 12;
        public const uint MagicValue = 0x4E4B4653; // "NKFS"
        public const ushort CurrentVersion = 1;

        /// <summary>Magic number identifying the NkFs format (0x4E4B4653 = "NKFS").</summary>
        public uint Magic { get; }

        /// <summary>Format version number (currently 1).</summary>
        public ushort Version { get; }

        /// <summary>Reserved field, must be 0.</summary>
        public ushort Reserved { get; }

        /// <summary>Total number of entries in the entry table (including root).</summary>
        public int EntryCount { get; }

        /// <summary>
        /// Creates an NkFsHeader with the given field values.
        /// </summary>
        public NkFsHeader(uint magic, ushort version, ushort reserved, int entryCount)
        {
            Magic = magic;
            Version = version;
            Reserved = reserved;
            EntryCount = entryCount;
        }

        /// <summary>
        /// Creates an NkFsHeader with the standard magic, current version, and zero reserved field.
        /// </summary>
        public NkFsHeader(int entryCount)
        {
            Magic = MagicValue;
            Version = CurrentVersion;
            Reserved = 0;
            EntryCount = entryCount;
        }

        /// <summary>
        /// Encodes an NkFsHeader into a 12-byte big-endian binary representation.
        /// </summary>
        public static byte[] Encode(NkFsHeader header)
        {
            byte[] buffer = new byte[HeaderSize];

            BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(0), header.Magic);
            BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(4), header.Version);
            BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(6), header.Reserved);
            BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(8), header.EntryCount);

            return buffer;
        }

        /// <summary>
        /// Decodes a 12-byte big-endian binary representation into an NkFsHeader.
        /// </summary>
        public static NkFsHeader Decode(ReadOnlySpan<byte> data)
        {
            if (data.Length < HeaderSize)
                throw new ArgumentException($"Header data must be at least {HeaderSize} bytes, got {data.Length}", nameof(data));

            uint magic = BinaryPrimitives.ReadUInt32BigEndian(data);
            ushort version = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(4));
            ushort reserved = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(6));
            int entryCount = BinaryPrimitives.ReadInt32BigEndian(data.Slice(8));

            return new NkFsHeader(magic, version, reserved, entryCount);
        }
    }

    /// <summary>
    /// A single 12-byte entry in the NkFs entry table, representing either a file or a directory.
    /// 
    /// Binary layout (big-endian):
    ///   Offset 0x00 (4 bytes): High 3 bits = Flags, low 29 bits = NameOffset (×2 for actual byte offset)
    ///   Offset 0x04 (8 bytes): File → image offset (int64), Directory → 4-byte parent index (int32) + 4-byte next-entry index (int32)
    /// 
    /// Flags (3 bits, high 3 bits of byte 0):
    ///   bit 0: is_directory (1 = directory, 0 = file)
    ///   bit 1: system_flag (system marker)
    ///   bit 2: image_file
    /// </summary>
    public readonly struct NkFsEntry
    {
        public const int EntrySize = 12;

        /// <summary>3-bit flags field (bits 0–2).</summary>
        public byte Flags { get; }

        /// <summary>29-bit encoded offset into the string table (multiply by 2 for actual byte offset).</summary>
        public int NameOffset { get; }

        /// <summary>
        /// For files: the image offset (int64 at 0x04).
        /// For directories: not used directly (use ParentIndex/NextEntryIndex).
        /// </summary>
        public long FileOffset { get; }

        /// <summary>For directories: the parent entry index (int32, first 4 bytes at 0x04).</summary>
        public int ParentIndex { get; }

        /// <summary>For directories: the index of the first entry not in this subtree (int32, 4 bytes at 0x08).</summary>
        public int NextEntryIndex { get; }

        // Flag accessors
        /// <summary>True when this entry is a directory (flag bit 0).</summary>
        public bool IsDirectory => (Flags & 0x01) != 0;

        /// <summary>True when this entry is a system node (flag bit 1).</summary>
        public bool SystemFlag => (Flags & 0x02) != 0;

        /// <summary>True when this entry is an image file (flag bit 2).</summary>
        public bool IsImageFile => (Flags & 0x04) != 0;

        /// <summary>True when this entry is a file (not a directory).</summary>
        public bool IsFile => !IsDirectory;

        /// <summary>
        /// Creates an NkFsEntry for a file.
        /// </summary>
        /// <param name="flags">3-bit flags (only low 3 bits used).</param>
        /// <param name="nameOffset">29-bit encoded string table offset (actual byte offset / 2).</param>
        /// <param name="fileOffset">File image offset (int64).</param>
        public NkFsEntry(byte flags, int nameOffset, long fileOffset)
        {
            Flags = (byte)(flags & 0x07);
            NameOffset = nameOffset & 0x1FFFFFFF;
            FileOffset = fileOffset;
            ParentIndex = 0;
            NextEntryIndex = 0;
        }

        /// <summary>
        /// Creates an NkFsEntry for a directory.
        /// </summary>
        /// <param name="flags">3-bit flags (only low 3 bits used).</param>
        /// <param name="nameOffset">29-bit encoded string table offset (actual byte offset / 2).</param>
        /// <param name="parentIndex">Parent directory entry index (int32).</param>
        /// <param name="nextEntryIndex">Next-entry index (int32).</param>
        public NkFsEntry(byte flags, int nameOffset, int parentIndex, int nextEntryIndex)
        {
            Flags = (byte)(flags & 0x07);
            NameOffset = nameOffset & 0x1FFFFFFF;
            FileOffset = 0;
            ParentIndex = parentIndex;
            NextEntryIndex = nextEntryIndex;
        }

        /// <summary>
        /// Encodes an NkFsEntry into a 12-byte big-endian binary representation.
        /// </summary>
        public static byte[] Encode(NkFsEntry entry)
        {
            byte[] buffer = new byte[EntrySize];

            // Pack flags (high 3 bits) and name offset (low 29 bits) into 4 bytes
            uint flagsAndOffset = ((uint)(entry.Flags & 0x07) << 29) | ((uint)entry.NameOffset & 0x1FFFFFFFU);
            BinaryPrimitives.WriteUInt32BigEndian(buffer.AsSpan(0), flagsAndOffset);

            if (entry.IsDirectory)
            {
                // Directory: 4-byte parent index + 4-byte next-entry index
                BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(4), entry.ParentIndex);
                BinaryPrimitives.WriteInt32BigEndian(buffer.AsSpan(8), entry.NextEntryIndex);
            }
            else
            {
                // File: 8-byte image offset
                BinaryPrimitives.WriteInt64BigEndian(buffer.AsSpan(4), entry.FileOffset);
            }

            return buffer;
        }

        /// <summary>
        /// Decodes a 12-byte big-endian binary representation into an NkFsEntry.
        /// </summary>
        public static NkFsEntry Decode(ReadOnlySpan<byte> data)
        {
            if (data.Length < EntrySize)
                throw new ArgumentException($"Entry data must be at least {EntrySize} bytes, got {data.Length}", nameof(data));

            // Unpack flags (high 3 bits) and name offset (low 29 bits)
            uint flagsAndOffset = BinaryPrimitives.ReadUInt32BigEndian(data);
            byte flags = (byte)(flagsAndOffset >> 29);
            int nameOffset = (int)(flagsAndOffset & 0x1FFFFFFFU);

            bool isDirectory = (flags & 0x01) != 0;

            if (isDirectory)
            {
                int parentIndex = BinaryPrimitives.ReadInt32BigEndian(data.Slice(4));
                int nextEntryIndex = BinaryPrimitives.ReadInt32BigEndian(data.Slice(8));
                return new NkFsEntry(flags, nameOffset, parentIndex, nextEntryIndex);
            }
            else
            {
                long fileOffset = BinaryPrimitives.ReadInt64BigEndian(data.Slice(4));
                return new NkFsEntry(flags, nameOffset, fileOffset);
            }
        }
    }

    /// <summary>
    /// Represents a binary filesystem in the NkFs format.
    /// Holds raw byte arrays (entry table + string table) and reads entries on demand.
    /// No in-memory tree is built — navigation operates directly on the flat data.
    /// </summary>
    public class NkFs
    {
        private readonly byte[] _entryTableBytes;
        private readonly byte[] _stringTableBytes;
        private readonly int _entryCount;

        /// <summary>
        /// Returns the total number of entries (including root).
        /// </summary>
        public int EntryCount => _entryCount;

        /// <summary>
        /// Creates an NkFs instance from raw binary data.
        /// </summary>
        /// <param name="entryTableBytes">Raw bytes of the entry table (N × 12 bytes).</param>
        /// <param name="stringTableBytes">Raw bytes of the string table.</param>
        /// <param name="entryCount">Total number of entries in the entry table.</param>
        internal NkFs(byte[] entryTableBytes, byte[] stringTableBytes, int entryCount)
        {
            _entryTableBytes = entryTableBytes;
            _stringTableBytes = stringTableBytes;
            _entryCount = entryCount;
        }

        /// <summary>
        /// Returns the entry at the given index, decoded from the raw entry table.
        /// </summary>
        /// <param name="index">Zero-based entry index.</param>
        /// <returns>The decoded <see cref="NkFsEntry"/>.</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when index is outside [0, EntryCount).</exception>
        public NkFsEntry GetEntry(int index)
        {
            if (index < 0 || index >= _entryCount)
                throw new ArgumentOutOfRangeException(nameof(index), index, $"Entry index must be in [0, {_entryCount}), got {index}");

            int offset = index * NkFsEntry.EntrySize;
            return NkFsEntry.Decode(_entryTableBytes.AsSpan(offset, NkFsEntry.EntrySize));
        }

        /// <summary>
        /// Serializes the complete binary representation (header + entry table + string table)
        /// to a single byte array.
        /// 
        /// Layout: [Header (12 bytes)] [Entry Table (N × 12 bytes)] [String Table (variable)]
        /// Header fields: magic 0x4E4B4653, version 1, reserved 0, entry count.
        /// StringTableOffset is computed: 12 + EntryCount * 12.
        /// All multi-byte integers are big-endian.
        /// </summary>
        /// <returns>The serialized byte array.</returns>
        public byte[] ToBytes()
        {
            NkFsHeader header = new NkFsHeader(_entryCount);
            byte[] headerBytes = NkFsHeader.Encode(header);

            byte[] result = new byte[headerBytes.Length + _entryTableBytes.Length + _stringTableBytes.Length];
            Buffer.BlockCopy(headerBytes, 0, result, 0, headerBytes.Length);
            Buffer.BlockCopy(_entryTableBytes, 0, result, headerBytes.Length, _entryTableBytes.Length);
            Buffer.BlockCopy(_stringTableBytes, 0, result, headerBytes.Length + _entryTableBytes.Length, _stringTableBytes.Length);

            return result;
        }

        /// <summary>
        /// Imports an NkFs from an existing FsYaml object.
        /// Equivalent to Build(fsYaml.FileSystems, fsYaml.ImageFileSystems).
        /// </summary>
        /// <param name="fsYaml">The FsYaml object to import.</param>
        /// <returns>A new NkFs instance.</returns>
        public static NkFs FromFsYaml(FsYaml fsYaml) => Build(fsYaml.FileSystems, fsYaml.ImageFileSystems);

        // --- Width encoding helpers for variable-width fields in string table prefix ---

        /// <summary>
        /// Encodes a value into a 2-bit width code: 0=0 bytes, 1=2 bytes, 2=4 bytes, 3=8 bytes.
        /// </summary>
        internal static byte EncodeWidth(long value)
        {
            if (value == 0) return 0;          // 0 bytes
            if (value <= 0xFFFF) return 1;     // 2 bytes
            if (value <= 0xFFFFFFFFL) return 2; // 4 bytes
            return 3;                           // 8 bytes
        }

        /// <summary>
        /// Converts a 2-bit width code to the number of bytes: 0→0, 1→2, 2→4, 3→8.
        /// </summary>
        internal static int WidthToBytes(int width) => width switch { 0 => 0, 1 => 2, 2 => 4, 3 => 8, _ => 0 };

        /// <summary>
        /// Writes a value in big-endian using the specified width (0, 2, 4, or 8 bytes).
        /// </summary>
        private static void WriteVariableWidth(List<byte> target, long value, int widthCode)
        {
            int byteCount = WidthToBytes(widthCode);
            if (byteCount == 0) return;

            byte[] buf = new byte[byteCount];
            switch (byteCount)
            {
                case 2:
                    BinaryPrimitives.WriteUInt16BigEndian(buf, (ushort)value);
                    break;
                case 4:
                    BinaryPrimitives.WriteUInt32BigEndian(buf, (uint)value);
                    break;
                case 8:
                    BinaryPrimitives.WriteInt64BigEndian(buf, value);
                    break;
            }
            target.AddRange(buf);
        }

        /// <summary>
        /// Reads a variable-width big-endian value from the string table.
        /// </summary>
        private long ReadVariableWidth(int pos, int widthCode)
        {
            int byteCount = WidthToBytes(widthCode);
            if (byteCount == 0) return 0;

            return byteCount switch
            {
                2 => BinaryPrimitives.ReadUInt16BigEndian(_stringTableBytes.AsSpan(pos, 2)),
                4 => BinaryPrimitives.ReadUInt32BigEndian(_stringTableBytes.AsSpan(pos, 4)),
                8 => BinaryPrimitives.ReadInt64BigEndian(_stringTableBytes.AsSpan(pos, 8)),
                _ => 0
            };
        }

        /// <summary>
        /// Ensures the string table list is 2-byte aligned by adding a pad byte if needed.
        /// </summary>
        private static void Align2(List<byte> stringTable)
        {
            if (stringTable.Count % 2 != 0)
                stringTable.Add(0);
        }

        /// <summary>
        /// Computes the encoded NameOffset value for a given byte offset in the string table.
        /// The byte offset must be even (2-byte aligned). The encoded value is byteOffset / 2.
        /// </summary>
        private static int EncodeNameOffset(int byteOffset) => byteOffset / 2;

        /// <summary>
        /// Decodes a NameOffset value to the actual byte offset in the string table.
        /// The actual byte offset is encodedOffset * 2.
        /// </summary>
        private static int DecodeNameOffset(int encodedOffset) => encodedOffset * 2;

        /// <summary>
        /// Builds an NkFs from FsYamlNode filesystem roots and optional IFS entries.
        /// 
        /// Format:
        ///   - 12-byte entries (3-bit flags + 29-bit NameOffset, file: 8-byte image offset, dir: int32 parent + int32 next)
        ///   - File size, checksums, and image ID stored in string table prefix byte with variable-width encoding
        ///   - 2-byte aligned string table entries
        ///   - Directory name deduplication (no prefix byte for directories)
        /// </summary>
        public static NkFs Build(List<FsYamlNode> fileSystems, List<FsYamlIfsEntry>? imageFileSystems = null)
        {
            // Working lists for entries and string table
            List<NkFsEntry> entries = new List<NkFsEntry>();
            List<byte> stringTable = new List<byte>();

            // 1. Create root entry (index 0) — placeholder, will be backpatched
            entries.Add(new NkFsEntry(0x01, 0, 0, 0)); // is_directory=1, placeholder

            // Write root name (empty string) to string table — directories have no prefix byte
            int rootNameByteOffset = stringTable.Count;
            stringTable.Add(0); // null terminator for empty root name
            Align2(stringTable); // 2-byte align

            // Directory name deduplication map: maps directory name strings (case-sensitive)
            // to their existing encoded NameOffset values.
            Dictionary<string, int> dirNameOffsets = new Dictionary<string, int>();

            // 2. Depth-first pre-order traversal of each filesystem root
            foreach (FsYamlNode fsRoot in fileSystems)
            {
                if (fsRoot.Children == null)
                    continue;

                for (int i = 0; i < fsRoot.Children.Count; i++)
                {
                    FsYamlNode child = fsRoot.Children[i];

                    // Detect multi-extent chains at root level: consecutive file children with the same name.
                    bool childHasMoreExtents = false;
                    if (child.IsFile && i + 1 < fsRoot.Children.Count)
                    {
                        FsYamlNode nextChild = fsRoot.Children[i + 1];
                        if (nextChild.IsFile && nextChild.Name == child.Name)
                        {
                            childHasMoreExtents = true;
                        }
                    }

                    buildNode(child, 0, entries, stringTable, dirNameOffsets, childHasMoreExtents);
                }
            }

            // 6. Append FsYamlIfsEntry items as file entries under root with image_file flag
            if (imageFileSystems != null)
            {
                foreach (FsYamlIfsEntry ifsEntry in imageFileSystems)
                {
                    int byteOffset = stringTable.Count;
                    int encodedOffset = EncodeNameOffset(byteOffset);

                    // File string table entry: prefix byte + variable-width fields + name\0 + pad
                    // IFS entries: has_checksums=0, size_width for ifsEntry.Size, imageid_width for ifsEntry.ImageId
                    byte sizeWidth = EncodeWidth(ifsEntry.Size);
                    byte imageidWidth = EncodeWidth(ifsEntry.ImageId);
                    byte prefixByte = (byte)((sizeWidth << 1) | (imageidWidth << 3));
                    // has_checksums = 0 (bit 0 = 0)

                    stringTable.Add(prefixByte);

                    // size_width bytes: file size
                    WriteVariableWidth(stringTable, ifsEntry.Size, sizeWidth);

                    // No checksums (has_checksums = 0)

                    // imageid_width bytes: image index
                    WriteVariableWidth(stringTable, ifsEntry.ImageId, imageidWidth);

                    // Null-terminated UTF-8 filename
                    stringTable.AddRange(Encoding.UTF8.GetBytes(ifsEntry.FileName));
                    stringTable.Add(0);
                    Align2(stringTable);

                    // Flags: image_file (bit 2) = 0x04
                    byte flags = 0x04;
                    entries.Add(new NkFsEntry(flags, encodedOffset, 0L));
                }
            }

            // 7. Set root NextEntryIndex = total entry count
            int totalEntries = entries.Count;
            int rootEncodedOffset = EncodeNameOffset(rootNameByteOffset);
            entries[0] = new NkFsEntry(0x01, rootEncodedOffset, 0, totalEntries);

            // Build raw entry table bytes
            byte[] entryTableBytes = new byte[totalEntries * NkFsEntry.EntrySize];
            for (int i = 0; i < totalEntries; i++)
            {
                byte[] encoded = NkFsEntry.Encode(entries[i]);
                Buffer.BlockCopy(encoded, 0, entryTableBytes, i * NkFsEntry.EntrySize, NkFsEntry.EntrySize);
            }

            return new NkFs(entryTableBytes, stringTable.ToArray(), totalEntries);
        }

        /// <summary>
        /// Recursively processes a single FsYamlNode in depth-first pre-order,
        /// appending entries and string table data.
        /// </summary>
        /// <param name="hasMoreExtents">When true, sets bit 5 (Has_More_Extents) in the file prefix byte,
        /// indicating this entry is followed by a continuation extent with the same name.</param>
        private static void buildNode(FsYamlNode node, int parentIndex, List<NkFsEntry> entries, List<byte> stringTable, Dictionary<string, int> dirNameOffsets, bool hasMoreExtents = false)
        {
            int currentIndex = entries.Count;

            if (node.IsDirectory)
            {
                // Directory entry — no prefix byte in string table, just name\0 + pad
                byte flags = 0x01; // is_directory
                if (node.IsSystem)
                    flags |= 0x02; // system_flag

                string name = node.Name;

                // Directory name deduplication: reuse existing encoded offset if name already present
                int encodedOffset;
                if (dirNameOffsets.TryGetValue(name, out int existingEncodedOffset))
                {
                    encodedOffset = existingEncodedOffset;
                }
                else
                {
                    int byteOffset = stringTable.Count;
                    encodedOffset = EncodeNameOffset(byteOffset);
                    stringTable.AddRange(Encoding.UTF8.GetBytes(name));
                    stringTable.Add(0); // null terminator
                    Align2(stringTable);
                    dirNameOffsets[name] = encodedOffset;
                }

                // Add directory entry with placeholder NextEntryIndex (will be backpatched)
                entries.Add(new NkFsEntry(flags, encodedOffset, parentIndex, 0));

                // Recurse into children (depth-first pre-order)
                if (node.Children != null)
                {
                    for (int i = 0; i < node.Children.Count; i++)
                    {
                        FsYamlNode child = node.Children[i];

                        // Detect multi-extent chains: consecutive file children with the same name.
                        // Set Has_More_Extents on all but the last entry in each same-name sequence.
                        bool childHasMoreExtents = false;
                        if (child.IsFile && i + 1 < node.Children.Count)
                        {
                            FsYamlNode nextChild = node.Children[i + 1];
                            if (nextChild.IsFile && nextChild.Name == child.Name)
                            {
                                childHasMoreExtents = true;
                            }
                        }

                        buildNode(child, currentIndex, entries, stringTable, dirNameOffsets, childHasMoreExtents);
                    }
                }

                // Backpatch NextEntryIndex
                NkFsEntry dirEntry = entries[currentIndex];
                entries[currentIndex] = new NkFsEntry(dirEntry.Flags, dirEntry.NameOffset, (int)dirEntry.ParentIndex, entries.Count);
            }
            else
            {
                // File entry — prefix byte + variable-width fields + name\0 + pad
                byte flags = 0x00; // file
                if (node.IsSystem)
                    flags |= 0x02; // system_flag

                int byteOffset = stringTable.Count;
                int encodedOffset = EncodeNameOffset(byteOffset);

                // Build prefix byte
                bool hasChecksums = node.XxHash64 != 0 || node.Crc32 != 0;
                byte sizeWidth = EncodeWidth(node.Size);

                byte prefixByte = 0;
                if (hasChecksums) prefixByte |= 0x01; // bit 0: has_checksums
                prefixByte |= (byte)(sizeWidth << 1);  // bits 1-2: size_width
                // bits 3-4: imageid_width = 0 for regular files
                if (hasMoreExtents) prefixByte |= 0x20; // bit 5: has_more_extents
                // bits 6-7: reserved = 0

                stringTable.Add(prefixByte);

                // size_width bytes: file size
                WriteVariableWidth(stringTable, node.Size, sizeWidth);

                // Checksums (if has_checksums): 8 bytes xxHash64 + 4 bytes CRC32
                if (hasChecksums)
                {
                    byte[] checksumBytes = new byte[12];
                    BinaryPrimitives.WriteUInt64BigEndian(checksumBytes.AsSpan(0), node.XxHash64);
                    BinaryPrimitives.WriteUInt32BigEndian(checksumBytes.AsSpan(8), node.Crc32);
                    stringTable.AddRange(checksumBytes);
                }

                // No image ID for regular files (imageid_width = 0)

                // Null-terminated UTF-8 filename
                string name = node.Name;
                stringTable.AddRange(Encoding.UTF8.GetBytes(name));
                stringTable.Add(0);
                Align2(stringTable);

                entries.Add(new NkFsEntry(flags, encodedOffset, node.Offset));
            }
        }

        /// <summary>
        /// Deserializes a byte array into an NkFs instance.
        /// </summary>
        public static NkFs FromBytes(byte[] data)
        {
            if (data.Length < NkFsHeader.HeaderSize)
                throw new ArgumentException(
                    $"Data is truncated: expected at least {NkFsHeader.HeaderSize} bytes for the header, got {data.Length}.",
                    nameof(data));

            uint magic = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(0));
            ushort version = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(4));

            if (magic != NkFsHeader.MagicValue)
                throw new InvalidDataException(
                    $"Invalid magic number: expected 0x{NkFsHeader.MagicValue:X8}, got 0x{magic:X8}.");

            if (version != NkFsHeader.CurrentVersion)
                throw new InvalidDataException(
                    $"Unsupported version: expected {NkFsHeader.CurrentVersion}, got {version}.");

            NkFsHeader header = NkFsHeader.Decode(data);

            int entryTableSize = header.EntryCount * NkFsEntry.EntrySize;
            int requiredLength = NkFsHeader.HeaderSize + entryTableSize;

            if (data.Length < requiredLength)
                throw new ArgumentException(
                    $"Data is truncated: expected at least {requiredLength} bytes (header + {header.EntryCount} entries), got {data.Length}.",
                    nameof(data));

            byte[] entryTableBytes = new byte[entryTableSize];
            Buffer.BlockCopy(data, NkFsHeader.HeaderSize, entryTableBytes, 0, entryTableSize);

            int stringTableOffset = NkFsHeader.HeaderSize + entryTableSize;
            int stringTableLength = data.Length - stringTableOffset;
            byte[] stringTableBytes = new byte[stringTableLength];
            Buffer.BlockCopy(data, stringTableOffset, stringTableBytes, 0, stringTableLength);

            return new NkFs(entryTableBytes, stringTableBytes, header.EntryCount);
        }

        /// <summary>
        /// Lists the immediate children of the directory at the given index.
        /// Uses the Next_Entry_Index skip pattern to yield only direct children.
        /// </summary>
        public IEnumerable<(int index, NkFsEntry entry)> GetChildren(int directoryIndex)
        {
            NkFsEntry dir = GetEntry(directoryIndex);
            if (!dir.IsDirectory)
                throw new ArgumentException(
                    $"Entry at index {directoryIndex} is not a directory.", nameof(directoryIndex));

            int nextEntryIndex = dir.NextEntryIndex;
            int i = directoryIndex + 1;
            while (i < nextEntryIndex)
            {
                NkFsEntry child = GetEntry(i);
                yield return (i, child);

                if (child.IsDirectory)
                    i = child.NextEntryIndex; // skip entire subdirectory subtree
                else
                    i++;
            }
        }

        /// <summary>
        /// Resolves a path string to the entry index, or -1 if not found.
        /// </summary>
        public int ResolvePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return 0;

            string[] segments = path.Split('/');
            int current = 0;

            foreach (string segment in segments)
            {
                if (segment.Length == 0)
                    continue;

                bool found = false;
                foreach ((int childIndex, NkFsEntry childEntry) in GetChildren(current))
                {
                    string childName = GetEntryName(childIndex);
                    if (childName == segment)
                    {
                        current = childIndex;
                        found = true;
                        break;
                    }
                }

                if (!found)
                    return -1;
            }

            return current;
        }

        /// <summary>
        /// Reads the file size from the string table prefix for a file entry.
        /// File size is stored in the string table prefix byte with variable-width encoding.
        /// </summary>
        /// <param name="index">Zero-based entry index.</param>
        /// <returns>The file size.</returns>
        public long GetFileSize(int index)
        {
            NkFsEntry entry = GetEntry(index);
            if (entry.IsDirectory)
                throw new InvalidOperationException($"Entry at index {index} is a directory, not a file.");

            int byteOffset = DecodeNameOffset(entry.NameOffset);
            byte prefixByte = _stringTableBytes[byteOffset];
            int sizeWidth = (prefixByte >> 1) & 0x03;
            // Size follows immediately after the prefix byte
            return ReadVariableWidth(byteOffset + 1, sizeWidth);
        }

        /// <summary>
        /// Reads the Has_More_Extents flag (bit 5) from the string table prefix byte for a file entry.
        /// When true, indicates the next sibling entry with the same name is a continuation extent.
        /// </summary>
        /// <param name="index">Zero-based entry index.</param>
        /// <returns>True if the Has_More_Extents flag is set.</returns>
        public bool HasMoreExtents(int index)
        {
            NkFsEntry entry = GetEntry(index);
            if (entry.IsDirectory)
                throw new InvalidOperationException($"Entry at index {index} is a directory, not a file.");

            int byteOffset = DecodeNameOffset(entry.NameOffset);
            byte prefixByte = _stringTableBytes[byteOffset];
            return (prefixByte & 0x20) != 0;
        }

        /// <summary>
        /// Returns all extents for the file at the given entry index.
        /// For single-extent files, returns one (offset, size) pair.
        /// For multi-extent files, resolves the full chain regardless of
        /// which entry in the chain is passed.
        /// </summary>
        /// <param name="fileIndex">Zero-based entry index of any extent in the chain.</param>
        /// <returns>An ordered list of (offset, size) pairs representing all extents.</returns>
        /// <exception cref="InvalidOperationException">Thrown when the entry is a directory.</exception>
        /// <exception cref="InvalidDataException">Thrown when Has_More_Extents is set but no valid continuation exists.</exception>
        public IReadOnlyList<(long offset, long size)> GetExtents(int fileIndex)
        {
            NkFsEntry entry = GetEntry(fileIndex);
            if (entry.IsDirectory)
                throw new InvalidOperationException($"Entry at index {fileIndex} is a directory, not a file.");

            // Find the parent directory by scanning backward for the enclosing directory
            int parentDirIndex = findParentDirectory(fileIndex);

            // Get all siblings as a list for indexed access
            List<(int index, NkFsEntry entry)> siblings = new List<(int index, NkFsEntry entry)>();
            foreach ((int index, NkFsEntry entry) child in GetChildren(parentDirIndex))
                siblings.Add(child);

            // Find the position of our entry in the sibling list
            int posInSiblings = -1;
            for (int i = 0; i < siblings.Count; i++)
            {
                if (siblings[i].index == fileIndex)
                {
                    posInSiblings = i;
                    break;
                }
            }

            if (posInSiblings == -1)
                throw new InvalidDataException($"Entry at index {fileIndex} not found among children of its parent directory.");

            // Get the name of the target entry for comparison
            string targetName = GetEntryName(fileIndex);

            // Walk backward to find the first extent in the chain
            int firstExtentPos = posInSiblings;
            while (firstExtentPos > 0)
            {
                int prevPos = firstExtentPos - 1;
                (int index, NkFsEntry entry) prevSibling = siblings[prevPos];

                // A preceding sibling is part of the chain if:
                // 1. It's a file (not a directory)
                // 2. It has the same name
                // 3. It has HasMoreExtents set (indicating continuation follows)
                if (prevSibling.entry.IsDirectory)
                    break;

                string prevName = GetEntryName(prevSibling.index);
                if (prevName != targetName)
                    break;

                if (!HasMoreExtents(prevSibling.index))
                    break;

                firstExtentPos = prevPos;
            }

            // Walk forward from the first extent, collecting (offset, size) pairs
            List<(long offset, long size)> extents = new List<(long offset, long size)>();
            int currentPos = firstExtentPos;

            while (currentPos < siblings.Count)
            {
                (int index, NkFsEntry entry) current = siblings[currentPos];

                // Only include file entries with matching name
                if (current.entry.IsDirectory)
                    break;

                string currentName = GetEntryName(current.index);
                if (currentName != targetName)
                {
                    // If we haven't collected anything yet at first position, this shouldn't happen
                    break;
                }

                long offset = current.entry.FileOffset;
                long size = GetFileSize(current.index);
                extents.Add((offset, size));

                if (!HasMoreExtents(current.index))
                {
                    // This is the last extent in the chain
                    break;
                }

                // HasMoreExtents is set — there must be a valid continuation
                currentPos++;

                // Validate that a valid continuation exists
                if (currentPos >= siblings.Count)
                {
                    throw new InvalidDataException(
                        $"Entry at index {current.index} has Has_More_Extents set but no subsequent sibling exists in the same parent directory.");
                }

                (int index, NkFsEntry entry) nextSibling = siblings[currentPos];
                if (nextSibling.entry.IsDirectory)
                {
                    throw new InvalidDataException(
                        $"Entry at index {current.index} has Has_More_Extents set but the next sibling at index {nextSibling.index} is a directory, not a file continuation.");
                }

                string nextName = GetEntryName(nextSibling.index);
                if (nextName != targetName)
                {
                    throw new InvalidDataException(
                        $"Entry at index {current.index} has Has_More_Extents set but no subsequent sibling with the same name '{targetName}' exists (next sibling has name '{nextName}').");
                }
            }

            return extents;
        }

        /// <summary>
        /// Returns the total logical file size for a file entry by summing all extent sizes in the chain.
        /// For single-extent files, this returns the same value as GetFileSize.
        /// For multi-extent files, this returns the sum of all extent sizes.
        /// </summary>
        /// <param name="index">Zero-based entry index of any extent in the chain.</param>
        /// <returns>The total file size across all extents.</returns>
        public long GetTotalFileSize(int index)
        {
            IReadOnlyList<(long offset, long size)> extents = GetExtents(index);
            long total = 0;
            for (int i = 0; i < extents.Count; i++)
                total += extents[i].size;
            return total;
        }

        /// <summary>
        /// Returns the entry indices for all extents of the file at the given entry index.
        /// For single-extent files, returns a list with one index.
        /// For multi-extent files, resolves the full chain regardless of which entry in the chain is passed.
        /// </summary>
        /// <param name="fileIndex">Zero-based entry index of any extent in the chain.</param>
        /// <returns>An ordered list of entry indices for each extent in the chain.</returns>
        public IReadOnlyList<int> GetExtentEntryIndices(int fileIndex)
        {
            NkFsEntry entry = GetEntry(fileIndex);
            if (entry.IsDirectory)
                throw new InvalidOperationException($"Entry at index {fileIndex} is a directory, not a file.");

            int parentDirIndex = findParentDirectory(fileIndex);

            List<(int index, NkFsEntry entry)> siblings = new List<(int index, NkFsEntry entry)>();
            foreach ((int index, NkFsEntry entry) child in GetChildren(parentDirIndex))
                siblings.Add(child);

            int posInSiblings = -1;
            for (int i = 0; i < siblings.Count; i++)
            {
                if (siblings[i].index == fileIndex)
                {
                    posInSiblings = i;
                    break;
                }
            }

            if (posInSiblings == -1)
                throw new InvalidDataException($"Entry at index {fileIndex} not found among children of its parent directory.");

            string targetName = GetEntryName(fileIndex);

            // Walk backward to find the first extent in the chain
            int firstExtentPos = posInSiblings;
            while (firstExtentPos > 0)
            {
                int prevPos = firstExtentPos - 1;
                (int index, NkFsEntry entry) prevSibling = siblings[prevPos];

                if (prevSibling.entry.IsDirectory)
                    break;

                string prevName = GetEntryName(prevSibling.index);
                if (prevName != targetName)
                    break;

                if (!HasMoreExtents(prevSibling.index))
                    break;

                firstExtentPos = prevPos;
            }

            // Walk forward from the first extent, collecting entry indices
            List<int> indices = new List<int>();
            int currentPos = firstExtentPos;

            while (currentPos < siblings.Count)
            {
                (int index, NkFsEntry entry) current = siblings[currentPos];

                if (current.entry.IsDirectory)
                    break;

                string currentName = GetEntryName(current.index);
                if (currentName != targetName)
                    break;

                indices.Add(current.index);

                if (!HasMoreExtents(current.index))
                    break;

                currentPos++;
            }

            return indices;
        }

        /// <summary>
        /// Finds the parent directory index for a given entry by scanning backward.
        /// The parent is the nearest preceding directory whose NextEntryIndex is greater than the given index.
        /// </summary>
        private int findParentDirectory(int entryIndex)
        {
            for (int i = entryIndex - 1; i >= 0; i--)
            {
                NkFsEntry candidate = GetEntry(i);
                if (candidate.IsDirectory && candidate.NextEntryIndex > entryIndex)
                    return i;
            }
            // Should not happen in a valid NkFs — root directory (index 0) covers all entries
            throw new InvalidDataException($"No parent directory found for entry at index {entryIndex}.");
        }

        /// <summary>
        /// Reads the xxHash64 and CRC32 checksums from the string table prefix for a file entry.
        /// Returns (0, 0) if the file has no checksums (has_checksums bit is 0).
        /// </summary>
        /// <param name="index">Zero-based entry index.</param>
        /// <returns>A tuple of (xxHash64, crc32).</returns>
        public (ulong xxHash64, uint crc32) GetChecksums(int index)
        {
            NkFsEntry entry = GetEntry(index);
            if (entry.IsDirectory)
                throw new InvalidOperationException($"Entry at index {index} is a directory, not a file.");

            int byteOffset = DecodeNameOffset(entry.NameOffset);
            byte prefixByte = _stringTableBytes[byteOffset];
            bool hasChecksums = (prefixByte & 0x01) != 0;
            if (!hasChecksums)
                return (0, 0);

            int sizeWidth = (prefixByte >> 1) & 0x03;
            int pos = byteOffset + 1 + WidthToBytes(sizeWidth);

            ulong xxHash64 = BinaryPrimitives.ReadUInt64BigEndian(_stringTableBytes.AsSpan(pos, 8));
            uint crc32 = BinaryPrimitives.ReadUInt32BigEndian(_stringTableBytes.AsSpan(pos + 8, 4));
            return (xxHash64, crc32);
        }

        /// <summary>
        /// Reads the image index from the string table prefix for an image file entry.
        /// </summary>
        public long GetImageIndex(int index)
        {
            NkFsEntry entry = GetEntry(index);
            if (!entry.IsImageFile)
                throw new InvalidOperationException("Entry is not an image file.");

            int byteOffset = DecodeNameOffset(entry.NameOffset);
            byte prefixByte = _stringTableBytes[byteOffset];
            bool hasChecksums = (prefixByte & 0x01) != 0;
            int sizeWidth = (prefixByte >> 1) & 0x03;
            int imageidWidth = (prefixByte >> 3) & 0x03;

            int pos = byteOffset + 1 + WidthToBytes(sizeWidth);
            if (hasChecksums) pos += 12;

            return ReadVariableWidth(pos, imageidWidth);
        }

        /// <summary>
        /// Reads the name string for the entry at the given index from the string table.
        /// For files: skips the prefix byte and variable-width fields.
        /// For directories: reads directly (no prefix byte).
        /// </summary>
        public string GetEntryName(int index)
        {
            NkFsEntry entry = GetEntry(index);
            int byteOffset = DecodeNameOffset(entry.NameOffset);

            int pos;
            if (entry.IsDirectory)
            {
                // Directories have no prefix byte — name starts directly at byteOffset
                pos = byteOffset;
            }
            else
            {
                // Files have a prefix byte followed by variable-width fields
                pos = getFileNameStart(byteOffset);
            }

            // Read null-terminated UTF-8 string
            int start = pos;
            while (pos < _stringTableBytes.Length && _stringTableBytes[pos] != 0)
                pos++;

            return Encoding.UTF8.GetString(_stringTableBytes, start, pos - start);
        }

        /// <summary>
        /// Computes the byte position where the filename starts for a file entry,
        /// after the prefix byte and all variable-width fields.
        /// </summary>
        private int getFileNameStart(int byteOffset)
        {
            byte prefixByte = _stringTableBytes[byteOffset];
            bool hasChecksums = (prefixByte & 0x01) != 0;
            int sizeWidth = (prefixByte >> 1) & 0x03;
            int imageidWidth = (prefixByte >> 3) & 0x03;

            int pos = byteOffset + 1; // skip prefix byte
            pos += WidthToBytes(sizeWidth); // skip file size
            if (hasChecksums) pos += 12; // skip xxHash64 + CRC32
            pos += WidthToBytes(imageidWidth); // skip image ID

            return pos;
        }

        /// <summary>
        /// Exports this NkFs to an FsYaml object with equivalent FsYamlNode tree and IFS entries.
        /// </summary>
        public FsYaml ToFsYaml()
        {
            FsYaml result = new FsYaml();
            FsYamlNode root = result.AddFileSystem(".", 0);

            foreach ((int childIndex, NkFsEntry childEntry) in GetChildren(0))
            {
                if (childEntry.IsImageFile)
                {
                    long imageId = GetImageIndex(childIndex);
                    string name = GetEntryName(childIndex);
                    long size = GetFileSize(childIndex);
                    result.AddIfsEntry(name, imageId, size);
                }
                else if (childEntry.IsDirectory)
                {
                    exportDirectory(childIndex, childEntry, root);
                }
                else
                {
                    exportFile(childIndex, childEntry, root);
                }
            }

            return result;
        }

        private void exportDirectory(int dirIndex, NkFsEntry dirEntry, FsYamlNode parent)
        {
            string name = GetEntryName(dirIndex);
            bool isSystem = dirEntry.SystemFlag;

            FsYamlNode dirNode = parent.AddDirectory(name, isSystem);

            foreach ((int childIndex, NkFsEntry childEntry) in GetChildren(dirIndex))
            {
                if (childEntry.IsDirectory)
                {
                    exportDirectory(childIndex, childEntry, dirNode);
                }
                else
                {
                    exportFile(childIndex, childEntry, dirNode);
                }
            }
        }

        private void exportFile(int fileIndex, NkFsEntry fileEntry, FsYamlNode parent)
        {
            string name = GetEntryName(fileIndex);
            bool isSystem = fileEntry.SystemFlag;
            long fileSize = GetFileSize(fileIndex);
            (ulong xxHash64, uint crc32) = GetChecksums(fileIndex);

            parent.AddFile(name, fileEntry.FileOffset, fileSize, xxHash64, crc32, isSystem);
        }
    }
}