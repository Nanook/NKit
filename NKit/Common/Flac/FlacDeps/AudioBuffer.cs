using System;

namespace CUETools.Codecs.Flake
{
    internal class AudioBuffer
    {
        // ======= Static Methods =======

        public static unsafe void FLACSamplesToBytes_16(int[,] inSamples, int inSampleOffset,
            byte* outSamples, int sampleCount, int channelCount)
        {
            int loopCount = sampleCount * channelCount;

            if (inSamples.GetLength(0) - inSampleOffset < sampleCount)
                throw new IndexOutOfRangeException();

            fixed (int* pInSamplesFixed = &inSamples[inSampleOffset, 0])
            {
                int* pInSamples = pInSamplesFixed;
                short* pOutSamples = (short*)outSamples;
                for (int i = 0; i < loopCount; i++)
                    pOutSamples[i] = (short)pInSamples[i];
                //*(pOutSamples++) = (short)*(pInSamples++);
            }
        }

        public static unsafe void FLACSamplesToBytes_16(int[,] inSamples, int inSampleOffset,
            byte[] outSamples, int outByteOffset, int sampleCount, int channelCount)
        {
            int loopCount = sampleCount * channelCount;

            if (inSamples.GetLength(0) - inSampleOffset < sampleCount ||
                outSamples.Length - outByteOffset < loopCount * 2)
            {
                throw new IndexOutOfRangeException();
            }

            fixed (byte* pOutSamplesFixed = &outSamples[outByteOffset])
                FLACSamplesToBytes_16(inSamples, inSampleOffset, pOutSamplesFixed, sampleCount, channelCount);
        }

        public static unsafe void FLACSamplesToBytes_24(int[,] inSamples, int inSampleOffset,
            byte[] outSamples, int outByteOffset, int sampleCount, int channelCount, int wastedBits)
        {
            int loopCount = sampleCount * channelCount;

            if (inSamples.GetLength(0) - inSampleOffset < sampleCount ||
                outSamples.Length - outByteOffset < loopCount * 3)
            {
                throw new IndexOutOfRangeException();
            }

            fixed (int* pInSamplesFixed = &inSamples[inSampleOffset, 0])
            {
                fixed (byte* pOutSamplesFixed = &outSamples[outByteOffset])
                {
                    int* pInSamples = pInSamplesFixed;
                    byte* pOutSamples = pOutSamplesFixed;

                    for (int i = 0; i < loopCount; i++)
                    {
                        uint sample_out = (uint)*pInSamples++ << wastedBits;
                        *pOutSamples++ = (byte)(sample_out & 0xFF);
                        sample_out >>= 8;
                        *pOutSamples++ = (byte)(sample_out & 0xFF);
                        sample_out >>= 8;
                        *pOutSamples++ = (byte)(sample_out & 0xFF);
                    }
                }
            }
        }

        public static unsafe void FloatToBytes_16(float[,] inSamples, int inSampleOffset,
            byte[] outSamples, int outByteOffset, int sampleCount, int channelCount)
        {
            int loopCount = sampleCount * channelCount;

            if (inSamples.GetLength(0) - inSampleOffset < sampleCount ||
                outSamples.Length - outByteOffset < loopCount * 2)
            {
                throw new IndexOutOfRangeException();
            }

            fixed (float* pInSamplesFixed = &inSamples[inSampleOffset, 0])
            {
                fixed (byte* pOutSamplesFixed = &outSamples[outByteOffset])
                {
                    float* pInSamples = pInSamplesFixed;
                    short* pOutSamples = (short*)pOutSamplesFixed;

                    for (int i = 0; i < loopCount; i++)
                    {
                        *pOutSamples++ = (short)(32758 * *pInSamples++);
                    }
                }
            }
        }

        public static unsafe void FloatToBytes(float[,] inSamples, int inSampleOffset,
            byte[] outSamples, int outByteOffset, int sampleCount, int channelCount, int bitsPerSample)
        {
            if (bitsPerSample == 16)
                FloatToBytes_16(inSamples, inSampleOffset, outSamples, outByteOffset, sampleCount, channelCount);
            //else if (bitsPerSample > 16 && bitsPerSample <= 24)
            //    FLACSamplesToBytes_24(inSamples, inSampleOffset, outSamples, outByteOffset, sampleCount, channelCount, 24 - bitsPerSample);
            else if (bitsPerSample == 32)
                Buffer.BlockCopy(inSamples, inSampleOffset * 4 * channelCount, outSamples, outByteOffset, sampleCount * 4 * channelCount);
            else
                throw new Exception("Unsupported bitsPerSample value");
        }

        public static unsafe void FLACSamplesToBytes(int[,] inSamples, int inSampleOffset,
            byte[] outSamples, int outByteOffset, int sampleCount, int channelCount, int bitsPerSample)
        {
            if (bitsPerSample == 16)
                FLACSamplesToBytes_16(inSamples, inSampleOffset, outSamples, outByteOffset, sampleCount, channelCount);
            else if (bitsPerSample > 16 && bitsPerSample <= 24)
                FLACSamplesToBytes_24(inSamples, inSampleOffset, outSamples, outByteOffset, sampleCount, channelCount, 24 - bitsPerSample);
            else
                throw new Exception("Unsupported bitsPerSample value");
        }

        public static unsafe void FLACSamplesToBytes(int[,] inSamples, int inSampleOffset,
            byte* outSamples, int sampleCount, int channelCount, int bitsPerSample)
        {
            if (bitsPerSample == 16)
                FLACSamplesToBytes_16(inSamples, inSampleOffset, outSamples, sampleCount, channelCount);
            else
                throw new Exception("Unsupported bitsPerSample value");
        }

        public static unsafe void Bytes16ToFloat(byte[] inSamples, int inByteOffset,
            float[,] outSamples, int outSampleOffset, int sampleCount, int channelCount)
        {
            int loopCount = sampleCount * channelCount;

            if (inSamples.Length - inByteOffset < loopCount * 2 || outSamples.GetLength(0) - outSampleOffset < sampleCount)
                throw new IndexOutOfRangeException();

            fixed (byte* pInSamplesFixed = &inSamples[inByteOffset])
            {
                fixed (float* pOutSamplesFixed = &outSamples[outSampleOffset, 0])
                {
                    short* pInSamples = (short*)pInSamplesFixed;
                    float* pOutSamples = pOutSamplesFixed;
                    for (int i = 0; i < loopCount; i++)
                        *pOutSamples++ = *pInSamples++ / 32768.0f;
                }
            }
        }

        public static unsafe void BytesToFLACSamples_16(byte[] inSamples, int inByteOffset,
            int[,] outSamples, int outSampleOffset, int sampleCount, int channelCount)
        {
            int loopCount = sampleCount * channelCount;

            if (inSamples.Length - inByteOffset < loopCount * 2 ||
                outSamples.GetLength(0) - outSampleOffset < sampleCount)
            {
                throw new IndexOutOfRangeException();
            }

            fixed (byte* pInSamplesFixed = &inSamples[inByteOffset])
            {
                fixed (int* pOutSamplesFixed = &outSamples[outSampleOffset, 0])
                {
                    short* pInSamples = (short*)pInSamplesFixed;
                    int* pOutSamples = pOutSamplesFixed;

                    for (int i = 0; i < loopCount; i++)
                    {
                        *pOutSamples++ = *pInSamples++;
                    }
                }
            }
        }

        public static unsafe void BytesToFLACSamples_24(byte[] inSamples, int inByteOffset,
            int[,] outSamples, int outSampleOffset, int sampleCount, int channelCount, int wastedBits)
        {
            int loopCount = sampleCount * channelCount;

            if (inSamples.Length - inByteOffset < loopCount * 3 || outSamples.GetLength(0) - outSampleOffset < sampleCount)
                throw new IndexOutOfRangeException();

            fixed (byte* pInSamplesFixed = &inSamples[inByteOffset])
            {
                fixed (int* pOutSamplesFixed = &outSamples[outSampleOffset, 0])
                {
                    byte* pInSamples = pInSamplesFixed;
                    int* pOutSamples = pOutSamplesFixed;
                    for (int i = 0; i < loopCount; i++)
                    {
                        int sample = *pInSamples++;
                        sample += *pInSamples++ << 8;
                        sample += *pInSamples++ << 16;
                        *pOutSamples++ = sample << 8 >> (8 + wastedBits);
                    }
                }
            }
        }

        public static unsafe void BytesToFLACSamples(byte[] inSamples, int inByteOffset,
            int[,] outSamples, int outSampleOffset, int sampleCount, int channelCount, int bitsPerSample)
        {
            if (bitsPerSample == 16)
                BytesToFLACSamples_16(inSamples, inByteOffset, outSamples, outSampleOffset, sampleCount, channelCount);
            else if (bitsPerSample > 16 && bitsPerSample <= 24)
                BytesToFLACSamples_24(inSamples, inByteOffset, outSamples, outSampleOffset, sampleCount, channelCount, 24 - bitsPerSample);
            else
                throw new Exception("Unsupported bitsPerSample value");
        }

        // ======= End Static Methods =======

        private int[,] _samples;
        private float[,] _fsamples;
        private byte[] _bytes;
        private int _length;
        private int _size;
        private AudioPCMConfig _pcm;
        private bool _dataInSamples = false;
        private bool _dataInBytes = false;
        private bool _dataInFloat = false;

        public int Length
        {
            get => _length; set => _length = value;
        }

        public int Size => _size;

        public AudioPCMConfig PCM => _pcm;

        public int ByteLength => _length * _pcm.BlockAlign;

        public int[,] Samples
        {
            get
            {
                if (_samples == null || _samples.GetLength(0) < _length)
                    _samples = new int[_size, _pcm.ChannelCount];
                if (!_dataInSamples && _dataInBytes && _length != 0)
                    BytesToFLACSamples(_bytes, 0, _samples, 0, _length, _pcm.ChannelCount, _pcm.BitsPerSample);
                _dataInSamples = true;
                return _samples;
            }
        }

        public float[,] Float
        {
            get
            {
                if (_fsamples == null || _fsamples.GetLength(0) < _length)
                    _fsamples = new float[_size, _pcm.ChannelCount];
                if (!_dataInFloat && _dataInBytes && _length != 0)
                {
                    if (_pcm.BitsPerSample == 16)
                        Bytes16ToFloat(_bytes, 0, _fsamples, 0, _length, _pcm.ChannelCount);
                    //else if (pcm.BitsPerSample > 16 && PCM.BitsPerSample <= 24)
                    //    BytesToFLACSamples_24(bytes, 0, fsamples, 0, length, pcm.ChannelCount, 24 - pcm.BitsPerSample);
                    else if (_pcm.BitsPerSample == 32)
                        Buffer.BlockCopy(_bytes, 0, _fsamples, 0, _length * 4 * _pcm.ChannelCount);
                    else
                        throw new Exception("Unsupported bitsPerSample value");
                }
                _dataInFloat = true;
                return _fsamples;
            }
        }

        public byte[] Bytes
        {
            get
            {
                if (_bytes == null || _bytes.Length < _length * _pcm.BlockAlign)
                    _bytes = new byte[_size * _pcm.BlockAlign];
                if (!_dataInBytes && _length != 0)
                {
                    if (_dataInSamples)
                        FLACSamplesToBytes(_samples, 0, _bytes, 0, _length, _pcm.ChannelCount, _pcm.BitsPerSample);
                    else if (_dataInFloat)
                        FloatToBytes(_fsamples, 0, _bytes, 0, _length, _pcm.ChannelCount, _pcm.BitsPerSample);
                }
                _dataInBytes = true;
                return _bytes;
            }
        }

        public AudioBuffer(AudioPCMConfig _pcm, int _size)
        {
            this._pcm = _pcm;
            this._size = _size;
            _length = 0;
        }

        public AudioBuffer(AudioPCMConfig _pcm, int[,] _samples, int _length)
        {
            this._pcm = _pcm;
            // assert _samples.GetLength(1) == pcm.ChannelCount
            Prepare(_samples, _length);
        }

        public AudioBuffer(AudioPCMConfig _pcm, byte[] _bytes, int _length)
        {
            this._pcm = _pcm;
            Prepare(_bytes, _length);
        }

        public AudioBuffer(IAudioSource source, int _size)
        {
            _pcm = source.PCM;
            this._size = _size;
        }

        public void Prepare(IAudioDest dest)
        {
            //if (dest.Settings.PCM.ChannelCount != pcm.ChannelCount || dest.Settings.PCM.BitsPerSample != pcm.BitsPerSample)
            //    throw new Exception("AudioBuffer format mismatch");
        }

        public void Prepare(IAudioSource source, int maxLength)
        {
            if (source.PCM.ChannelCount != _pcm.ChannelCount || source.PCM.BitsPerSample != _pcm.BitsPerSample)
                throw new Exception("AudioBuffer format mismatch");
            _length = _size;
            if (maxLength >= 0)
                _length = Math.Min(_length, maxLength);
            if (source.Remaining >= 0)
                _length = (int)Math.Min(_length, source.Remaining);
            _dataInBytes = false;
            _dataInSamples = false;
            _dataInFloat = false;
        }

        public void Prepare(int maxLength)
        {
            _length = _size;
            if (maxLength >= 0)
                _length = Math.Min(_length, maxLength);
            _dataInBytes = false;
            _dataInSamples = false;
            _dataInFloat = false;
        }

        public void Prepare(int[,] _samples, int _length)
        {
            this._length = _length;
            _size = _samples.GetLength(0);
            this._samples = _samples;
            _dataInSamples = true;
            _dataInBytes = false;
            _dataInFloat = false;
            if (this._length > this._size)
                throw new Exception("Invalid length");
        }

        public void Prepare(byte[] _bytes, int _length)
        {
            this._length = _length;
            _size = _bytes.Length / PCM.BlockAlign;
            this._bytes = _bytes;
            _dataInSamples = false;
            _dataInBytes = true;
            _dataInFloat = false;
            if (this._length > this._size)
                throw new Exception("Invalid length");
        }

        internal unsafe void Load(int dstOffset, AudioBuffer src, int srcOffset, int copyLength)
        {
            if (_dataInBytes)
                Buffer.BlockCopy(src.Bytes, srcOffset * _pcm.BlockAlign, Bytes, dstOffset * _pcm.BlockAlign, copyLength * _pcm.BlockAlign);
            if (_dataInSamples)
                Buffer.BlockCopy(src.Samples, srcOffset * _pcm.ChannelCount * 4, Samples, dstOffset * _pcm.ChannelCount * 4, copyLength * _pcm.ChannelCount * 4);
            if (_dataInFloat)
                Buffer.BlockCopy(src.Float, srcOffset * _pcm.ChannelCount * 4, Float, dstOffset * _pcm.ChannelCount * 4, copyLength * _pcm.ChannelCount * 4);
        }

        public unsafe void Prepare(AudioBuffer _src, int _offset, int _length)
        {
            this._length = Math.Min(this._size, _src.Length - _offset);
            if (_length >= 0)
                this._length = Math.Min(this._length, _length);
            _dataInBytes = false;
            _dataInFloat = false;
            _dataInSamples = false;
            if (_src._dataInBytes)
                _dataInBytes = true;
            else if (_src._dataInSamples)
                _dataInSamples = true;
            else if (_src._dataInFloat)
                _dataInFloat = true;
            this.Load(0, _src, _offset, this._length);
        }

        public void Swap(AudioBuffer buffer)
        {
            if (_pcm.BitsPerSample != buffer.PCM.BitsPerSample || _pcm.ChannelCount != buffer.PCM.ChannelCount)
                throw new Exception("AudioBuffer format mismatch");

            int[,] samplesTmp = _samples;
            float[,] floatsTmp = _fsamples;
            byte[] bytesTmp = _bytes;

            _fsamples = buffer._fsamples;
            _samples = buffer._samples;
            _bytes = buffer._bytes;
            _length = buffer._length;
            _size = buffer._size;
            _dataInSamples = buffer._dataInSamples;
            _dataInBytes = buffer._dataInBytes;
            _dataInFloat = buffer._dataInFloat;

            buffer._samples = samplesTmp;
            buffer._bytes = bytesTmp;
            buffer._fsamples = floatsTmp;
            buffer._length = 0;
            buffer._dataInSamples = false;
            buffer._dataInBytes = false;
            buffer._dataInFloat = false;
        }

        unsafe public void Interlace(int pos, int* src1, int* src2, int n)
        {
            if (PCM.ChannelCount != 2)
            {
                throw new Exception("Must be stereo");
            }
            if (PCM.BitsPerSample == 16)
            {
                fixed (byte* bs = Bytes)
                {
                    int* res = (int*)bs + pos;
                    for (int i = n; i > 0; i--)
                        *res++ = (*src1++ & 0xffff) ^ (*src2++ << 16);
                }
            }
            else if (PCM.BitsPerSample == 24)
            {
                fixed (byte* bs = Bytes)
                {
                    byte* res = bs + (pos * 6);
                    for (int i = n; i > 0; i--)
                    {
                        uint sample_out = (uint)*src1++;
                        *res++ = (byte)(sample_out & 0xFF);
                        sample_out >>= 8;
                        *res++ = (byte)(sample_out & 0xFF);
                        sample_out >>= 8;
                        *res++ = (byte)(sample_out & 0xFF);
                        sample_out = (uint)*src2++;
                        *res++ = (byte)(sample_out & 0xFF);
                        sample_out >>= 8;
                        *res++ = (byte)(sample_out & 0xFF);
                        sample_out >>= 8;
                        *res++ = (byte)(sample_out & 0xFF);
                    }
                }
            }
            else
            {
                throw new Exception("Unsupported BPS");
            }
        }

        //public void Clear()
        //{
        //    length = 0;
        //}
    }
}
