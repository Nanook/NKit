using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Nanook.NKit
{
    internal class ChecksumsResult
    {
        public uint Crc { get; set; }
        public byte[] Md5 { get; set; }
        public byte[] Sha1 { get; set; }
    }

    internal static class ExtensionMethods
    {
        private static byte[] _SeekBuff = new byte[0x10000]; //never read. Just used to copy to 

#if NETSTANDARD2_0
        public static bool TryAdd(this Dictionary<string, string> dict, string key, string value)
        {
            if (dict.ContainsKey(key))
                return false;
            dict.Add(key, value);
            return true;
        }
#endif
        [DebuggerNonUserCode]
        public static void SafeSeek(this Stream stream, long offset, SeekOrigin origin) => stream.SafeSeek(offset, origin, null);

        //seek if possible, move forward if not
        [DebuggerNonUserCode]
        public static void SafeSeek(this Stream stream, long offset, SeekOrigin origin, CancellationToken? cancel)
        {
            // A BufferStream is always positionable: backward seeks are served from its retained
            // cache and forward seeks by read/advance, even over a forward-only archive source
            // (where CanSeek is false). The CanSeek-gated logic below would otherwise throw on a
            // backward move for such a source, so delegate straight to its own buffered Seek.
            if (stream is BufferStream bs)
            {
                bs.Seek(offset, origin);
                return;
            }

            if (origin == SeekOrigin.Current) //super safe mode. No need to get position (which crashes with archives)
            {
                if (stream.CanSeek || offset < 0)
                    stream.Seek(offset, SeekOrigin.Current);
                else
                {
                    while (offset > 0)
                    {
                        int r = stream.Read(_SeekBuff, 0, (int)Math.Min(_SeekBuff.Length, offset));
                        if (r == 0)
                            break; //not moving
                        offset -= r;
                        if (cancel?.IsCancellationRequested ?? false)
                            throw new HandledException("SafeSeek cancelled");
                    }
                }
                return;
            }

            // NOT SURE ABOUT THIS. IT WAS ADDED BEFORE 2025 AND BREAKS CURRENT TESTS
            //// For non-current seeks, make sure the stream supports seeking. Accessing Position/Length on
            //// a non-seekable stream can throw for some archive streams (Wii, etc.). Guard that here.
            //if (!stream.CanSeek)
            //    throw new HandledException("SafeSeek: absolute seeks are not supported on non-seekable streams");

            long pos = stream.Position;
            switch (origin)
            {
                case SeekOrigin.Begin: pos = offset; break;
                case SeekOrigin.Current: pos += offset; break;
                case SeekOrigin.End: pos = stream.Length + offset; break;
            }
            if (stream.Position == pos)
                return;
            else if (stream.CanSeek)
                stream.Seek(pos, SeekOrigin.Begin);
            else if (stream.Position < pos)
            {
                long p = stream.Position;
                while (stream.Position < pos)
                {
                    stream.Read(_SeekBuff, 0, (int)Math.Min(_SeekBuff.Length, pos - stream.Position));
                    if (stream.Position == p)
                        break; //not moving
                    p = stream.Position;
                    if (cancel?.IsCancellationRequested ?? false)
                        throw new HandledException("SafeSeek cancelled");
                }
            }
            else
                throw new Exception(string.Format("Unable to seek from 0x{0} to 0x{1}", stream.Position.ToString("X"), pos.ToString("X")));
        }

        //For ISO9660 - 0x58 converts to 58
        [DebuggerNonUserCode]
        public static byte ToDecimal(this byte base10) => (byte)(((base10 >> 4) * 10) + (base10 & 0xf));

        [DebuggerNonUserCode]
        public static DataType ToDataType(this MetaDataType s)
        {
            switch (s)
            {
                case MetaDataType.Data:
                    return DataType.Data;
                case MetaDataType.NJunkFile:
                case MetaDataType.NJunk:
                    return DataType.NJunk;
                case MetaDataType.Fill:
                    return DataType.Fill;
                default:
                    throw new Exception($"{s.ToString()} is not a supported MetaDataType");
            }
        }

        [DebuggerNonUserCode]
        public static string ToJsonValue(this object s)
        {
            switch (s)
            {
                case bool b:
                    return b ? "true" : "false";
                case uint ui:
                    return string.Concat("\"", ui.ToString("X8"), "\"");
                case ulong ul:
                    return string.Concat("\"", ul.ToString("X9"), "\"");
                case int i:
                    return i.ToString();
                case long l:
                    return l.ToString();
                case TimeSpan ts:
                    return ts.ToString("\\\"hh\\:mm\\:ss\\.fff\\\"");
                default:
                    return string.Concat("\"", s.ToString().ToJsonString(), "\"");
            }
        }
        [DebuggerNonUserCode]
        public static string s(this int num) => num != 1 ? "s" : "";
        [DebuggerNonUserCode]
        public static string es(this int num) => num != 1 ? "es" : "";

        [DebuggerNonUserCode]
        public static string ToXmlValue(this object s)
        {
            switch (s)
            {
                case bool b:
                    return b ? "true" : "false";
                case uint ui:
                    return ui.ToString("X8");
                case ulong ul:
                    return ul.ToString("X9");
                case int i:
                    return i.ToString();
                case long l:
                    return l.ToString();
                case TimeSpan ts:
                    return ts.ToString("hh\\:mm\\:ss\\.fff");
                default:
                    string val = (s ?? "").ToString();
                    StringBuilder buff = new StringBuilder(val.Length);

                    foreach (char c in val)
                    {
                        if (c == 0x9 || c == 0xA || c == 0xD || (c >= 0x20 && c <= 0xD7FF) || (c >= 0xE000 && c <= 0xFFFD) || (c >= 0x10000 && c <= 0x10FFFF))
                            buff.Append(c);
                        else
                            buff.Append($"[0x{(int)c:X}]");
                    }

                    return buff.ToString();
            }
        }
        [DebuggerNonUserCode]
        public static string ToJsonString(this string s)
        {
            if (s == null || s.Length == 0)
                return "";

            char c;
            int i;
            int len = s.Length;
            StringBuilder sb = new StringBuilder(len + 0x10);
            for (i = 0; i < len; i += 1)
            {
                c = s[i];
                switch (c)
                {
                    case '\\':
                    case '"':
                        sb.Append('\\');
                        sb.Append(c);
                        break;
                    //case '/':
                    //    sb.Append('\\');
                    //    sb.Append(c);
                    //    break;
                    case '\b':
                        sb.Append("\\b");
                        break;
                    case '\t':
                        sb.Append("\\t");
                        break;
                    case '\n':
                        sb.Append("\\n");
                        break;
                    case '\f':
                        sb.Append("\\f");
                        break;
                    case '\r':
                        sb.Append("\\r");
                        break;
                    default:
                        if (c < ' ')
                            sb.Append("\\u" + ((int)c).ToString("X4"));
                        else
                            sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        [DebuggerNonUserCode]
        public static byte[] ToBytesBE(this uint val)
        {
            byte[] b = new byte[4];
            b.WriteUInt32B(0, val);
            return b;
        }

        [DebuggerNonUserCode]
        public static byte[] ToBytesBE(this ulong val)
        {
            byte[] b = new byte[8];
            b.WriteUInt64B(0, val);
            return b;
        }

        [DebuggerNonUserCode]
        public static byte[] ToBytesLE(this uint val)
        {
            byte[] b = new byte[4];
            b.WriteUInt32L(0, val);
            return b;
        }

        [DebuggerNonUserCode]
        public static byte[] ToBytesLE(this ulong val)
        {
            byte[] b = new byte[8];
            b.WriteUInt64L(0, val);
            return b;
        }

        [DebuggerNonUserCode]
        public static string ToHexString(this byte[] bytes)
        {
            if (bytes == null)
                return null;
            return BitConverter.ToString(bytes).Replace("-", "");
        }

        [DebuggerNonUserCode]
        public static byte[] HexToBytes(this string hex) => hex.HexToBytes(0, hex.Length);

        [DebuggerNonUserCode]
        public static byte[] HexToBytes(this string hex, int offset, int size)
        {
            if (size % 2 == 1)
                throw new Exception("Hex cannot have an odd number of digits");

            byte[] arr = new byte[size >> 1];

            for (int i = 0; i < size >> 1; ++i)
            {
                int hi = hex[(offset + i) << 1];
                int lo = hex[offset + (i << 1) + 1];
                arr[i] = (byte)(((hi - (hi < 58 ? 48 : (hi < 97 ? 55 : 87))) << 4) + (lo - (lo < 58 ? 48 : (lo < 97 ? 55 : 87))));
            }

            return arr;
        }

        [DebuggerNonUserCode]
        public static byte[] HexToBytes(this byte[] hex) => hex.HexToBytes(0, hex.Length);

        public static string Rot3Hex(this string filename)
        {
            string ext = Path.GetExtension(filename);
            if (ext.Length > 5) //includes .
                ext = "";
            char[] fn = (ext.Length == 0 ? filename : Path.GetFileNameWithoutExtension(filename)).ToCharArray();
            int n;

            for (int i = 0; i < fn.Length; i++)
            {
                char c = fn[i];
                n = c;
                if (n >= 'a' && n <= 'f')
                    fn[i] = (char)(n + (n > 'c' ? -3 : 3));
                else if (n >= 'A' && n <= 'F')
                    fn[i] = (char)(n + (n > 'C' ? -3 : 3));
                else if (n >= '0' && n <= '9')
                    fn[i] = (char)(n + (n > '4' ? -5 : 5));
            }

            return new string(fn) + ext;
        }

        public static string Rot13Words(this string filename)
        {
            string ext = Path.GetExtension(filename);
            if (ext.Length > 5) //includes .
                ext = "";
            char[] fn = (ext.Length == 0 ? filename : Path.GetFileNameWithoutExtension(filename)).ToCharArray();
            int n;

            for (int i = 0; i < fn.Length; i++)
            {
                char c = fn[i];
                n = c;
                if (n >= 'a' && n <= 'z')
                    fn[i] = (char)(n + (n > 'm' ? -13 : 13));
                else if (n >= 'A' && n <= 'Z')
                    fn[i] = (char)(n + (n > 'M' ? -13 : 13));
                else if (n >= '0' && n <= '9')
                    fn[i] = (char)(n + (n > '4' ? -5 : 5));
            }

            return new string(fn) + ext;
        }

        [DebuggerNonUserCode]
        public static byte[] HexToBytes(this byte[] hex, int offset, int size)
        {
            if (size % 2 == 1)
                throw new Exception("Hex cannot have an odd number of digits");

            byte[] arr = new byte[size >> 1];

            for (int i = 0; i < size >> 1; ++i)
            {
                int hi = hex[(offset + i) << 1];
                int lo = hex[offset + (i << 1) + 1];
                arr[i] = (byte)(((hi - (hi < 58 ? 48 : (hi < 97 ? 55 : 87))) << 4) + (lo - (lo < 58 ? 48 : (lo < 97 ? 55 : 87))));
            }

            return arr;
        }

        [DebuggerNonUserCode]
        public static bool Equals(this byte[] data1, int offset1, byte[] data2, int offset2, int size)
        {
            if (data1.Length - offset1 < size || data2.Length - offset2 < size)
                return false;
            for (int i = 0; i < size; i++)
            {
                if (data1[i + offset1] != data2[i + offset2])
                    return false;
            }
            return true;
        }

        [DebuggerNonUserCode]
        public static void Clear(this byte[] source, int offset, int count, byte value) //, Action<long, long> progress)
        {
            for (int i = 0; i < count; i++)
                source[offset + i] = value;
        }

        [DebuggerNonUserCode]
        public static bool Equals(this byte[] source, int offset, int count, byte value) //, Action<long, long> progress)
        {
            for (int i = 0; i < count; i++)
            {
                if (source[offset + i] != value)
                    return false;
            }
            return true;
        }

        [DebuggerNonUserCode]
        public static long Copy(this Stream source, Stream target, long amount) //, Action<long, long> progress)
        {
            int len = 0x200000; //arbitrary
            byte[] buffer = new byte[len];
            byte[] buffer2 = new byte[len]; //double buffered
            byte[] tmp;
            int read = 0;
            int read2 = 0;
            long total = amount;
            long prg = 0;
            Task t = null;
            while (prg < total)
            {
                read2 = 0;
                prg += read;
                if (prg < total)
                {
                    t = Task.Run(() => read2 = source.Read(buffer2, 0, (int)Math.Min(len, total - prg)));
                    t.ConfigureAwait(false);
                }
                else
                    t = null;

                target.Write(buffer, 0, read);
                if (t != null && !t.IsCompleted)
                    t.Wait();

                tmp = buffer2;
                buffer2 = buffer;
                buffer = tmp;
                if (read == 0 && read2 == 0)
                    throw new Exception("Could not read from stream");

                read = read2;
            }

            return prg;
        }
    }

    [DebuggerNonUserCode]
    internal static class Bytes
    {
        public static byte[] ReadBytes(this Stream stream, long size)
        {
            byte[] b = new byte[size];
            stream.Read(b, 0, b.Length);
            return b;
        }

        [DebuggerNonUserCode]
        public static byte Read8(this byte[] data, int offset) => data[offset];
        [DebuggerNonUserCode]
        public static ushort ReadUInt16B(this byte[] data, int offset) => bigEndian(BitConverter.ToUInt16(data, offset));
        [DebuggerNonUserCode]
        public static uint ReadUInt32B(this byte[] data, int offset) => bigEndian(BitConverter.ToUInt32(data, offset));
        [DebuggerNonUserCode]
        public static ulong ReadUInt64B(this byte[] data, int offset) => bigEndian(BitConverter.ToUInt64(data, offset));
        [DebuggerNonUserCode]
        public static ushort ReadUInt16L(this byte[] data, int offset) => littleEndian(BitConverter.ToUInt16(data, offset));
        [DebuggerNonUserCode]
        public static uint ReadUInt32L(this byte[] data, int offset) => littleEndian(BitConverter.ToUInt32(data, offset));
        [DebuggerNonUserCode]
        public static ulong ReadUInt64L(this byte[] data, int offset) => littleEndian(BitConverter.ToUInt64(data, offset));
        [DebuggerNonUserCode]
        public static string ReadString(this byte[] data, int offset, int length) => Encoding.ASCII.GetString(data, offset, length);
        [DebuggerNonUserCode]
        public static string ReadString(this byte[] data, int offset, int length, Encoding encoding) => encoding.GetString(data, offset, length);
        [DebuggerNonUserCode]
        public static string ReadStringToNull(this byte[] data, int offset, Encoding encoding) => data.ReadStringToNull(encoding, offset, -1);
        [DebuggerNonUserCode]
        public static string ReadStringToNull(this byte[] data, int offset) => data.ReadStringToNull(Encoding.ASCII, offset, -1);
        [DebuggerNonUserCode]
        public static string ReadStringToNull(this byte[] data, int offset, int maxLength) => data.ReadStringToNull(Encoding.ASCII, offset, maxLength);
        [DebuggerNonUserCode]
        public static byte[] Read(this byte[] data, int offset, int length)
        {
            byte[] buffer = new byte[length];
            Array.Copy(data, offset, buffer, 0, length);
            return buffer;
        }
        [DebuggerNonUserCode]
        public static void Write8(this byte[] data, int offset, byte value) => data[offset] = value;
        [DebuggerNonUserCode]
        public static void WriteUInt16B(this byte[] data, int offset, ushort value) => BitConverter.GetBytes(bigEndian(value)).CopyTo(data, offset);
        [DebuggerNonUserCode]
        public static void WriteUInt32B(this byte[] data, int offset, uint value) => BitConverter.GetBytes(bigEndian(value)).CopyTo(data, offset);
        [DebuggerNonUserCode]
        public static void WriteUInt64B(this byte[] data, int offset, ulong value) => BitConverter.GetBytes(bigEndian(value)).CopyTo(data, offset);
        [DebuggerNonUserCode]
        public static void WriteUInt16L(this byte[] data, int offset, ushort value) => BitConverter.GetBytes(littleEndian(value)).CopyTo(data, offset);
        [DebuggerNonUserCode]
        public static void WriteUInt32L(this byte[] data, int offset, uint value) => BitConverter.GetBytes(littleEndian(value)).CopyTo(data, offset);
        [DebuggerNonUserCode]
        public static void WriteUInt64L(this byte[] data, int offset, ulong value) => BitConverter.GetBytes(littleEndian(value)).CopyTo(data, offset);
        [DebuggerNonUserCode]
        public static void WriteString(this byte[] data, int offset, int length, string value) => Array.Copy(Encoding.ASCII.GetBytes(value), 0, data, offset, length);
        [DebuggerNonUserCode]
        public static void WriteString(this byte[] data, int offset, int length, string value, Encoding encoding)
        {
            value = value.Substring(0, Math.Min(value.Length, length));
            byte[] b = encoding.GetBytes(value);
            Array.Copy(b, 0, data, offset, b.Length);
        }


        [DebuggerNonUserCode]
        public static void Write(this byte[] data, int offset, byte[] buffer) => Array.Copy(buffer, 0, data, offset, buffer.Length);
        [DebuggerNonUserCode]
        public static void Write(this byte[] data, int offset, byte[] buffer, int length) => Array.Copy(buffer, 0, data, offset, length);

        [DebuggerNonUserCode]
        public static void Write(this byte[] data, int offset, byte[] buffer, int bufferOffset, int length) => Array.Copy(buffer, bufferOffset, data, offset, length);

        [DebuggerNonUserCode]
        private static uint bigEndian(uint x)
        {
            if (!BitConverter.IsLittleEndian) //don't swap on big endian CPUs
                return x;

            x = (x >> 16) | (x << 16);
            return ((x & 0xFF00FF00) >> 8) | ((x & 0x00FF00FF) << 8);
        }
        [DebuggerNonUserCode]
        private static ulong bigEndian(ulong x)
        {
            if (!BitConverter.IsLittleEndian) //don't swap on big endian CPUs
                return x;

            return (ulong)(((bigEndian((uint)x) & 0xffffffffL) << 32) | (bigEndian((uint)(x >> 32)) & 0xffffffffL));
        }

        [DebuggerNonUserCode]
        private static uint littleEndian(uint x)
        {
            if (BitConverter.IsLittleEndian) //don't swap on big endian CPUs
                return x;

            x = (x >> 16) | (x << 16);
            return ((x & 0xFF00FF00) >> 8) | ((x & 0x00FF00FF) << 8);
        }

        [DebuggerNonUserCode]
        private static ulong littleEndian(ulong x)
        {
            if (BitConverter.IsLittleEndian) //don't swap on big endian CPUs
                return x;

            return (ulong)(((littleEndian((uint)x) & 0xffffffffL) << 32) | (littleEndian((uint)(x >> 32)) & 0xffffffffL));
        }

        [DebuggerNonUserCode]
        private static ushort bigEndian(ushort x) => !BitConverter.IsLittleEndian ? x : (ushort)((x >> 8) | (x << 8));
        [DebuggerNonUserCode]
        private static ushort littleEndian(ushort x) => BitConverter.IsLittleEndian ? x : (ushort)((x >> 8) | (x << 8));

        [DebuggerNonUserCode]
        public static string ReadStringToNull(this byte[] data, Encoding encoding, int offset, int maxLength)
        {
            try
            {
                if (offset < 0 || offset >= data.Length)
                    return string.Empty;

                byte b;
                int i = offset;
                int l = 0;

                while ((maxLength == -1 || l <= maxLength) && i < data.Length && (b = data[i++]) != '\0')
                    l++;

                return encoding.GetString(data, offset, l);
            }
            catch (Exception ex)
            {
                throw new HandledException(ex, "NStream.readStringToNull failure");
            }
        }

        public static void Set(this Dictionary<string, string> dict, string key, string value)
        {
            if (!dict.ContainsKey(key))
                dict.Add(key, value);
            else
                dict[key] = value;


        }
    }
}