using Nanook.NKit.Nintendo.WiiGc;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Nanook.NKit
{
    internal class JunkStream : Stream
    {

        private long _position;
        private readonly long _length;
        private readonly long _junkLength;
        private readonly byte[] _id;
        private readonly int _disc;
        private byte[] _junk;
        private byte[] _junk2;
        private int _currentJunkIndex;
        private int _nextJunkIndex;
        private readonly uint[] _numArray;

        public object _lock;
        public int _status; //-1 idle, 0=started, 1=stop
        private Task _task;

        public string Id => Encoding.ASCII.GetString(_id);
        public override long Length => _length;
        public long JunkLength => _junkLength;

        public override bool CanRead => true;
        public override bool CanSeek => true;
        public override bool CanWrite => false;
        public override void Flush() => throw new NotImplementedException();
        public override long Seek(long offset, SeekOrigin origin)
        {
            switch (origin)
            {
                case SeekOrigin.Begin: Position = offset; break;
                case SeekOrigin.Current: Position += offset; break;
                case SeekOrigin.End: Position = Length + offset; break;
            }
            return Position;
        }
        public override void SetLength(long value) => throw new NotImplementedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotImplementedException();

        public JunkStream(byte[] id, int disc, long length)
        {
            _numArray = new uint[0x824];
            _id = id;
            _disc = disc;
            _length = length;
            _junkLength = length - (length % WiiConsts.WiiSectorSize);

            _junk = new byte[0x40000];
            _junk2 = new byte[0x40000];

            _status = -1;
            _lock = new object();
            _currentJunkIndex = -1;
            _nextJunkIndex = -1;
        }


        public JunkStream(string id, int disc, long length) : this(Encoding.ASCII.GetBytes(id), disc, length)
        {
        }

        public override long Position
        {
            get => _position;
            set
            {

                uint block = (uint)(value / _junk.Length);

                lock (_lock)
                {
                    _position = value;
                    getJunkBlock(block, _id, (byte)_disc, false); //async
                }
            }
        }

        internal bool CurrentBlock(long position, long length) => position >= _position && position + length <= _position + _junk.Length;

        public int Compare(byte[] buffer, int offset, int length, int leadingNulls)
        {
            int size = length;

            while (size > 0)
            {
                uint junkIndex = (uint)(_position / _junk.Length);
                int junkOffset = (int)(_position % _junk.Length);
                int junkCopySize = Math.Min(size, _junk.Length - junkOffset);
                lock (_lock)
                {
                    getJunkBlock(junkIndex, _id, (byte)_disc, true);
                    for (int i = 0; i < junkCopySize; i++)
                    {
                        if (leadingNulls > 0)
                        {
                            if (buffer[offset + i] != 0)
                            {
                                return length - size + i;
                            }

                            leadingNulls--;
                        }
                        else if (buffer[offset + i] != _junk[junkOffset + i])
                        {
                            return length - size + i;
                        }
                    }
                    offset += junkCopySize;
                    size -= junkCopySize;
                    _position += junkCopySize;
                }

            }
            return length;
        }

        public override int Read(byte[] buffer, int offset, int length)
        {
            int size = length;

            while (size > 0)
            {
                uint junkIndex = (uint)(_position / _junk.Length);
                int junkOffset = (int)(_position % _junk.Length);
                int junkCopySize = Math.Min(size, _junk.Length - junkOffset);
                lock (_lock)
                {
                    getJunkBlock(junkIndex, _id, (byte)_disc, true);
                    Array.Copy(_junk, junkOffset, buffer, offset, junkCopySize);
                    offset += junkCopySize;
                    size -= junkCopySize;
                    _position += junkCopySize;
                }
            }
            return length;
        }

        public string BruteForceId(byte discNo, byte[] junkTest, long junkOffset)
        {
            byte[] id = new byte[4];
            string[] dict = new string[4];
            byte[] buffer = new byte[0x40000];
            uint[] numArray = new uint[0x824];

            dict[0] = "RAGDPU_";
            //dict[0] = "ABCDEFGHIJKLMNOPQRSTUVWXYZ_";
            dict[1] = "ABCDEFGHIJKLMNOPQRSTUVWXYZ_";
            dict[2] = "ABCDEFGHIJKLMNOPQRSTUVWXYZ_";
            dict[3] = "ABCDEFGHIJKLMNOPQRSTUVWXYZ_";

            uint block = (uint)(junkOffset / buffer.Length);
            uint offset = (uint)(junkOffset % buffer.Length);

            if (offset + junkTest.Length > buffer.Length)
            {
                throw new Exception("Junk test goes beyond end of block");
            }

            for (int d0 = 0; d0 < dict[0].Length; d0++)
            {
                id[0] = (byte)dict[0][d0];
                for (int d1 = 0; d1 < dict[1].Length; d1++)
                {
                    id[1] = (byte)dict[1][d1];
                    for (int d2 = 0; d2 < dict[2].Length; d2++)
                    {
                        id[2] = (byte)dict[2][d2];

                        for (int d3 = 0; d3 < dict[3].Length; d3++)
                        {
                            id[3] = (byte)dict[3][d3];

                            fillBlock(block, id, discNo, buffer);

                            bool res = true;
                            for (int t = 0; t < junkTest.Length; t++)
                            {
                                if (!(res = buffer[offset + t] == junkTest[t]))
                                {
                                    break;
                                }
                            }
                            if (res)
                            {
                                return Encoding.ASCII.GetString(id);
                            }
                        }
                    }
                }
            }

            return null;
        }


        private void getJunkBlock(uint block, byte[] id, byte disc, bool wait)
        {
            if (block == _currentJunkIndex)
            {
                return;
            }
            else if (block == _nextJunkIndex)
            {
                if (_status == 1)
                {
                    _task.Wait();
                }

                swap();
                genNext(block + 1, id, disc);
            }
            else // if (block != _nextJunkIndex) //started
            {
                _currentJunkIndex = -1;

                if (_status == 1)
                {
                    _status = 2;
                    _task.Wait();
                }
                if (wait)
                {
                    genNext(block, id, disc).Wait();
                    swap();
                }
                else
                {
                    genNext(block, id, disc);
                }
            }
        }

        private Task genNext(uint block, byte[] id, byte disc)
        {
            _nextJunkIndex = (int)block;
            _status = 1;
            return _task = Task.Run(() =>
            {
                fillBlock(block, id, disc, _junk2);
                _status = -1;
            });
        }

        private void swap()
        {
            byte[] buffer = _junk;
            _junk = _junk2;
            _junk2 = buffer;
            _currentJunkIndex = _nextJunkIndex;
            _nextJunkIndex = -1;
        }

        private void fillBlock(uint block, byte[] id, byte disc, byte[] buffer)
        {
            uint blk = block;

            Array.Clear(_numArray, 0, _numArray.Length);
            int num2 = 0;
            uint sample = 0;
            block = block * 8 * 0x1ef29123;
            for (int i = 0; i < 0x40000; i += 4)
            {
                if (_status == 2)
                    return;

                if ((i & 0x7fff) == 0)
                {
                    sample = (uint)((((id[2] << 8) | id[1]) << 0x10) | ((id[3] + id[2]) << 8) | (id[0] + id[1]));
                    sample = ((sample ^ disc) * 0x260bcd5) ^ block;
                    a10002710(sample, _numArray);
                    if (_status == 2)
                        return;

                    num2 = 520;
                    block += 0x1ef29123;
                }
                num2++;
                if (num2 == 0x209)
                {
                    a100026e0(_numArray);
                    if (_status == 2)
                        return;

                    num2 = 0;
                }
                buffer[i] = (byte)(_numArray[num2] >> 0x18);
                buffer[i + 1] = (byte)(_numArray[num2] >> 0x12);
                buffer[i + 2] = (byte)(_numArray[num2] >> 8);
                buffer[i + 3] = (byte)_numArray[num2];
            }

            int junkSize = (int)Math.Min(Math.Max(0, _junkLength - (blk * buffer.Length)), buffer.Length);
            if (buffer.Length - junkSize != 0)
                Array.Clear(buffer, junkSize, buffer.Length - junkSize);
        }

        private void a10002710(uint sample, uint[] buffer)
        {
            int num2;
            uint num = 0;
            for (num2 = 0; num2 != 0x11; num2++)
            {
                for (int i = 0; i < 0x20; i++)
                {
                    if (_status == 2)
                        return;

                    sample *= 0x5d588b65;
                    num = (num >> 1) | (++sample & 0x80000000);
                }
                buffer[num2] = num;
            }
            buffer[0x10] ^= (buffer[0] >> 9) ^ (buffer[0x10] << 0x17);
            for (num2 = 1; num2 != 0x1f9; num2++)
                buffer[num2 + 0x10] = (buffer[num2 - 1] << 0x17) ^ (buffer[num2] >> 9) ^ buffer[num2 + 15];

            for (num2 = 0; num2 < 3; num2++)
                a100026e0(buffer);
        }

        private void a100026e0(uint[] buffer)
        {
            int index = 0;
            while (index != 0x20)
            {
                if (_status == 2)
                    return;

                buffer[index] ^= buffer[index + 0x1e9];
                index++;
            }
            while (index != 0x209)
            {
                if (_status == 2)
                    return;

                buffer[index] ^= buffer[index - 0x20];
                index++;
            }
        }

    }
}