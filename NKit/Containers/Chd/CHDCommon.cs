using CUETools.Codecs.Flake;
using System;

namespace Nanook.NKit.Chd
{
    internal class ChdCodec
    {
        internal AudioPCMConfig FlacSettings = null;
        internal AudioDecoder FlacAudioDecoder = null;
        internal AudioBuffer FlacAudioBuffer = null;

        internal AudioPCMConfig AvHuffSettings = null;
        internal AudioDecoder AvHuffAudioDecoder = null;

        internal byte[] Sector = null;
        internal byte[] Subcode = null;

        internal byte[] Lzma = null;

        internal ushort[] Huffman = null;

        internal ushort[] HuffmanHi = null;
        internal ushort[] HuffmanLo = null;

        internal ushort[] HuffmanY = null;
        internal ushort[] HuffmanCB = null;
        internal ushort[] HuffmanCR = null;
    }

    internal static class ChdCommon
    {

        internal static ChdCodecType CompTypeConv(uint ct)
        {
            switch (ct)
            {
                case 1: return ChdCodecType.CHD_CODEC_ZLIB;
                case 2: return ChdCodecType.CHD_CODEC_ZLIB;
                case 3: return ChdCodecType.CHD_CODEC_AVHUFF;
                default:
                    return ChdCodecType.CHD_CODEC_ERROR;
            }
        }

        /* Converts V3 & V4 mapFlags to V5 compression_type */
        internal static ChdCompressionType ConvMapFlagstoCompressionType(ChdFlags mapFlags)
        {
            switch (mapFlags & ChdFlags.MAP_ENTRY_FLAG_TYPE_MASK)
            {
                case ChdFlags.MAP_ENTRY_TYPE_INVALID: return ChdCompressionType.COMPRESSION_ERROR;
                case ChdFlags.MAP_ENTRY_TYPE_COMPRESSED: return ChdCompressionType.COMPRESSION_TYPE_0;
                case ChdFlags.MAP_ENTRY_TYPE_UNCOMPRESSED: return ChdCompressionType.COMPRESSION_NONE;
                case ChdFlags.MAP_ENTRY_TYPE_MINI: return ChdCompressionType.COMPRESSION_MINI;
                case ChdFlags.MAP_ENTRY_TYPE_SELF_HUNK: return ChdCompressionType.COMPRESSION_SELF;
                case ChdFlags.MAP_ENTRY_TYPE_PARENT_HUNK: return ChdCompressionType.COMPRESSION_PARENT;
                default:
                    return ChdCompressionType.COMPRESSION_ERROR;
            }
        }

    }




    internal enum ChdCodecType
    {
        CHD_CODEC_NONE = 0,
        CHD_CODEC_ZLIB = 0x7A6C6962, // zlib
        CHD_CODEC_ZSTD = 0x7A737464, // zstd
        CHD_CODEC_LZMA = 0x6C7A6D61, // lzma
        CHD_CODEC_HUFFMAN = 0x68756666, // huff
        CHD_CODEC_FLAC = 0x666C6163, // flac
        CHD_CODEC_CD_ZLIB = 0x63647A6C, // cdzl
        CHD_CODEC_CD_ZSTD = 0x63647A73, // cdzl
        CHD_CODEC_CD_LZMA = 0x63646C7A, // cdlz
        CHD_CODEC_CD_FLAC = 0x6364666C, // cdfl
        CHD_CODEC_AVHUFF = 0x61766875, // avhu
        CHD_CODEC_ERROR = 0x0eeeeeee
    }



    [Flags]
    internal enum ChdFlags
    {
        MAP_ENTRY_FLAG_TYPE_MASK = 0x000f,      /* what type of hunk */
        MAP_ENTRY_FLAG_NO_CRC = 0x0010,         /* no CRC is present */

        MAP_ENTRY_TYPE_INVALID = 0x0000,        /* invalid type */
        MAP_ENTRY_TYPE_COMPRESSED = 0x0001,     /* standard compression */
        MAP_ENTRY_TYPE_UNCOMPRESSED = 0x0002,   /* uncompressed data */
        MAP_ENTRY_TYPE_MINI = 0x0003,           /* mini: use offset as raw data */
        MAP_ENTRY_TYPE_SELF_HUNK = 0x0004,      /* same as another hunk in this file */
        MAP_ENTRY_TYPE_PARENT_HUNK = 0x0005     /* same as a hunk in the parent file */
    }

    internal enum ChdCompressionType
    {
        /* codec #0
         * these types are live when running */
        COMPRESSION_TYPE_0 = 0,
        /* codec #1 */
        COMPRESSION_TYPE_1 = 1,
        /* codec #2 */
        COMPRESSION_TYPE_2 = 2,
        /* codec #3 */
        COMPRESSION_TYPE_3 = 3,
        /* no compression; implicit length = hunkbytes */
        COMPRESSION_NONE = 4,
        /* same as another block in this chd */
        COMPRESSION_SELF = 5,
        /* same as a hunk's worth of units in the parent chd */
        COMPRESSION_PARENT = 6,

        /* start of small RLE run (4-bit length)
         * these additional pseudo-types are used for compressed encodings: */
        COMPRESSION_RLE_SMALL = 7,
        /* start of large RLE run (8-bit length) */
        COMPRESSION_RLE_LARGE = 8,
        /* same as the last COMPRESSION_SELF block */
        COMPRESSION_SELF_0 = 9,
        /* same as the last COMPRESSION_SELF block + 1 */
        COMPRESSION_SELF_1 = 10,
        /* same block in the parent */
        COMPRESSION_PARENT_SELF = 11,
        /* same as the last COMPRESSION_PARENT block */
        COMPRESSION_PARENT_0 = 12,
        /* same as the last COMPRESSION_PARENT block + 1 */
        COMPRESSION_PARENT_1 = 13,



        /* ADDED HERE: used in CHD V3 and V4 */
        COMPRESSION_MINI = 100,
        /* ADDED HERE: as an internal error state */
        COMPRESSION_ERROR = 101
    };



    enum ChdError
    {
        CHDERR_NONE,
        CHDERR_NO_INTERFACE,
        CHDERR_OUT_OF_MEMORY,
        CHDERR_INVALID_FILE,
        CHDERR_INVALID_PARAMETER,
        CHDERR_INVALID_DATA,
        CHDERR_FILE_NOT_FOUND,
        CHDERR_REQUIRES_PARENT,
        CHDERR_FILE_NOT_WRITEABLE,
        CHDERR_READ_ERROR,
        CHDERR_WRITE_ERROR,
        CHDERR_CODEC_ERROR,
        CHDERR_INVALID_PARENT,
        CHDERR_HUNK_OUT_OF_RANGE,
        CHDERR_DECOMPRESSION_ERROR,
        CHDERR_COMPRESSION_ERROR,
        CHDERR_CANT_CREATE_FILE,
        CHDERR_CANT_VERIFY,
        CHDERR_NOT_SUPPORTED,
        CHDERR_METADATA_NOT_FOUND,
        CHDERR_INVALID_METADATA_SIZE,
        CHDERR_UNSUPPORTED_VERSION,
        CHDERR_VERIFY_INCOMPLETE,
        CHDERR_INVALID_METADATA,
        CHDERR_INVALID_STATE,
        CHDERR_OPERATION_PENDING,
        CHDERR_NO_ASYNC_OPERATION,
        CHDERR_UNSUPPORTED_FORMAT
    };
}
