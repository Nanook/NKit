using CUETools.Codecs.Flake;
using Nanook.GrindCore;
using Nanook.GrindCore.DeflateZLib;
using Nanook.GrindCore.Lzma;
using Nanook.GrindCore.ZStd;
using System;

namespace Nanook.NKit.Chd
{
    internal delegate ChdError ChdDecompress(byte[] buffIn, int buffInLength, byte[] buffOut, int buffOutLength, ChdCodec codec);


    internal static class ChdReaders
    {
        private static byte[] _lzmaProperties;

        static ChdReaders()
        {
            _lzmaProperties = new byte[] { 0x5d, 0x80, 0x49, 0, 0 };
        }

        internal static ChdDecompress Get(ChdCodecType type)
        {
            switch (type)
            {
                case ChdCodecType.CHD_CODEC_ZLIB: return ChdReaders.Zlib;
                case ChdCodecType.CHD_CODEC_ZSTD: return ChdReaders.Zstd;
                case ChdCodecType.CHD_CODEC_LZMA: return ChdReaders.Lzma;
                case ChdCodecType.CHD_CODEC_HUFFMAN: return ChdReaders.Huffman;
                case ChdCodecType.CHD_CODEC_FLAC: return ChdReaders.Flac;
                case ChdCodecType.CHD_CODEC_CD_ZLIB: return ChdReaders.CdZlib;
                case ChdCodecType.CHD_CODEC_CD_ZSTD: return ChdReaders.CdZstd;
                case ChdCodecType.CHD_CODEC_CD_LZMA: return ChdReaders.CdLzma;
                case ChdCodecType.CHD_CODEC_CD_FLAC: return ChdReaders.CdFlac;
                case ChdCodecType.CHD_CODEC_AVHUFF: return ChdReaders.AvHuff;
                default: return null;
            }
        }


        internal static ChdError Zlib(byte[] buffIn, int buffInLength, byte[] buffOut, int buffOutLength, ChdCodec codec) => Zlib(buffIn, 0, buffInLength, buffOut, buffOutLength);
        private static ChdError Zlib(byte[] buffIn, int buffInStart, int buffInLength, byte[] buffOut, int buffOutLength)
        {
            using (DeflateBlock block = new DeflateBlock(new CompressionOptions() { BlockSize = buffOutLength }))
            {
                if (block.Decompress(buffIn, buffInStart, buffInLength, buffOut, 0, ref buffOutLength) != CompressionResultCode.Success)
                    return ChdError.CHDERR_INVALID_DATA;
                return ChdError.CHDERR_NONE;
            }
        }

        internal static ChdError Zstd(byte[] buffIn, int buffInLength, byte[] buffOut, int buffOutLength, ChdCodec codec) => Zstd(buffIn, 0, buffInLength, buffOut, buffOutLength);
        private static ChdError Zstd(byte[] buffIn, int buffInStart, int buffInLength, byte[] buffOut, int buffOutLength)
        {
            using (ZStdBlock block = new ZStdBlock(new CompressionOptions() { BlockSize = buffOutLength }))
            {
                if (block.Decompress(buffIn, buffInStart, buffInLength, buffOut, 0, ref buffOutLength) != CompressionResultCode.Success)
                    return ChdError.CHDERR_INVALID_DATA;
                return ChdError.CHDERR_NONE;
            }
        }

        internal static ChdError Lzma(byte[] buffIn, int buffInLength, byte[] buffOut, int buffOutLength, ChdCodec codec) => Lzma(buffIn, 0, buffInLength, buffOut, buffOutLength, codec);
        private static ChdError Lzma(byte[] buffIn, int buffInStart, int compsize, byte[] buffOut, int buffOutLength, ChdCodec codec)
        {
            using (LzmaBlock block = new LzmaBlock(new CompressionOptions() { BlockSize = buffOutLength, InitProperties = _lzmaProperties }))
            {
                if (block.Decompress(buffIn, buffInStart, compsize, buffOut, 0, ref buffOutLength) != CompressionResultCode.Success)
                    return ChdError.CHDERR_INVALID_DATA;
                return ChdError.CHDERR_NONE;
            }
        }





        internal static ChdError Huffman(byte[] buffIn, int buffInLength, byte[] buffOut, int buffOutLength, ChdCodec codec)
        {
            if (codec.Huffman == null)
                codec.Huffman = new ushort[1 << 16];

            BitStream bitbuf = new BitStream(buffIn, 0, buffInLength);
            Huffman hd = new Huffman(256, 16, bitbuf, codec.Huffman);

            if (hd.ImportTreeHuffman() != HuffmanError.HUFFERR_NONE)
                return ChdError.CHDERR_INVALID_DATA;

            for (int j = 0; j < buffOutLength; j++)
            {
                buffOut[j] = (byte)hd.DecodeOne();
            }
            return ChdError.CHDERR_NONE;
        }




        internal static ChdError Flac(byte[] buffIn, int buffInLength, byte[] buffOut, int buffOutLength, ChdCodec codec)
        {
            byte endianType = buffIn[0];
            //CHD adds a leading char to indicate endian. Not part of the flac format.
            bool swapEndian = endianType == 'B'; //'L'ittle / 'B'ig
            return Flac(buffIn, 1, buffInLength, buffOut, buffOutLength, swapEndian, codec, out _);
        }

        private static ChdError Flac(byte[] buffIn, int buffInStart, int buffInLength, byte[] buffOut, int buffOutLength, bool swapEndian, ChdCodec codec, out int srcPos)
        {
            //codec.FLAC_settings ??= new AudioPCMConfig(16, 2, 44100);
            //codec.FLAC_audioDecoder ??= new AudioDecoder(codec.FLAC_settings);
            //codec.FLAC_audioBuffer ??= new AudioBuffer(codec.FLAC_settings, buffOutLength); //audio CiBuffer to take decoded samples and read them to bytes.


            srcPos = buffInStart;
            int dstPos = 0;
            //this may require some error handling. Hopefully the while condition is reliable
            while (dstPos < buffOutLength)
            {
                int read = codec.FlacAudioDecoder.DecodeFrame(buffIn, srcPos, buffInLength - srcPos);
                codec.FlacAudioDecoder.Read(codec.FlacAudioBuffer, (int)codec.FlacAudioDecoder.Remaining);
                Array.Copy(codec.FlacAudioBuffer.Bytes, 0, buffOut, dstPos, codec.FlacAudioBuffer.ByteLength);
                dstPos += codec.FlacAudioBuffer.ByteLength;
                srcPos += read;
            }

            //Nanook - hack to support 16bit byte flipping - tested passes hunk CRC test
            if (swapEndian)
            {
                byte tmp;
                for (int i = 0; i < buffOutLength; i += 2)
                {
                    tmp = buffOut[i];
                    buffOut[i] = buffOut[i + 1];
                    buffOut[i + 1] = tmp;
                }
            }
            return ChdError.CHDERR_NONE;
        }



        /******************* CD decoders **************************/



        private const int CD_MAX_SECTOR_DATA = 2352;
        private const int CD_MAX_SUBCODE_DATA = 96;
        private static readonly int CD_FRAME_SIZE = CD_MAX_SECTOR_DATA + CD_MAX_SUBCODE_DATA;

        private static readonly byte[] s_cd_sync_header = new byte[] { 0x00, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0x00 };

        internal static ChdError CdZlib(byte[] buffIn, int buffInLength, byte[] buffOut, int buffOutLength, ChdCodec codec) => cdDecompress(ChdCodecType.CHD_CODEC_ZLIB, buffIn, buffInLength, buffOut, buffOutLength, codec);
        internal static ChdError CdLzma(byte[] buffIn, int buffInLength, byte[] buffOut, int buffOutLength, ChdCodec codec) => cdDecompress(ChdCodecType.CHD_CODEC_LZMA, buffIn, buffInLength, buffOut, buffOutLength, codec);
        internal static ChdError CdZstd(byte[] buffIn, int buffInLength, byte[] buffOut, int buffOutLength, ChdCodec codec) => cdDecompress(ChdCodecType.CHD_CODEC_ZSTD, buffIn, buffInLength, buffOut, buffOutLength, codec);

        private static ChdError cdDecompress(ChdCodecType compType, byte[] buffIn, int buffInLength, byte[] buffOut, int buffOutLength, ChdCodec codec)
        {
            /* determine header bytes */
            int frames = buffOutLength / CD_FRAME_SIZE;
            int complen_bytes = (buffOutLength < 65536) ? 2 : 3;
            int ecc_bytes = (frames + 7) / 8;
            int header_bytes = ecc_bytes + complen_bytes;

            /* extract compressed length of base */
            int complen_base = (buffIn[ecc_bytes + 0] << 8) | buffIn[ecc_bytes + 1];
            if (complen_bytes > 2)
                complen_base = (complen_base << 8) | buffIn[ecc_bytes + 2];

            codec.Sector ??= new byte[frames * CD_MAX_SECTOR_DATA];
            codec.Subcode ??= new byte[frames * CD_MAX_SUBCODE_DATA];

            if (compType == ChdCodecType.CHD_CODEC_ZLIB)
            {
                ChdError err = Zlib(buffIn, (int)header_bytes, complen_base, codec.Sector, frames * CD_MAX_SECTOR_DATA);
                if (err != ChdError.CHDERR_NONE)
                    return err;

                err = Zlib(buffIn, header_bytes + complen_base, buffInLength - header_bytes - complen_base, codec.Subcode, frames * CD_MAX_SUBCODE_DATA);
                if (err != ChdError.CHDERR_NONE)
                    return err;
            }
            else if (compType == ChdCodecType.CHD_CODEC_LZMA)
            {
                ChdError err = Lzma(buffIn, header_bytes, complen_base, codec.Sector, frames * CD_MAX_SECTOR_DATA, codec);
                if (err != ChdError.CHDERR_NONE)
                    return err;

                err = Zlib(buffIn, header_bytes + complen_base, buffInLength - header_bytes - complen_base, codec.Subcode, frames * CD_MAX_SUBCODE_DATA);
                if (err != ChdError.CHDERR_NONE)
                    return err;
            }
            else if (compType == ChdCodecType.CHD_CODEC_ZSTD)
            {
                ChdError err = Zstd(buffIn, (int)header_bytes, complen_base, codec.Sector, frames * CD_MAX_SECTOR_DATA);
                if (err != ChdError.CHDERR_NONE)
                    return err;

                err = Zstd(buffIn, header_bytes + complen_base, buffInLength - header_bytes - complen_base, codec.Subcode, frames * CD_MAX_SUBCODE_DATA);
                if (err != ChdError.CHDERR_NONE)
                    return err;
            }

            /* reassemble the data */
            for (int framenum = 0; framenum < frames; framenum++)
            {
                Array.Copy(codec.Sector, framenum * CD_MAX_SECTOR_DATA, buffOut, framenum * CD_FRAME_SIZE, CD_MAX_SECTOR_DATA);
                Array.Copy(codec.Subcode, framenum * CD_MAX_SUBCODE_DATA, buffOut, (framenum * CD_FRAME_SIZE) + CD_MAX_SECTOR_DATA, CD_MAX_SUBCODE_DATA);

                // reconstitute the ECC data and sync header 
                int sectorStart = framenum * CD_FRAME_SIZE;
                if ((buffIn[framenum / 8] & (1 << (framenum % 8))) != 0)
                {
                    Array.Copy(s_cd_sync_header, 0, buffOut, sectorStart, s_cd_sync_header.Length);
                    CdRom.ecc_generate(buffOut, sectorStart);
                }
            }
            return ChdError.CHDERR_NONE;
        }

        internal static ChdError CdFlac(byte[] buffIn, int buffInLength, byte[] buffOut, int buffOutLength, ChdCodec codec)
        {
            int frames = buffOutLength / CD_FRAME_SIZE;

            codec.Sector ??= new byte[frames * CD_MAX_SECTOR_DATA];
            codec.Subcode ??= new byte[frames * CD_MAX_SUBCODE_DATA];

            ChdError err = Flac(buffIn, 0, buffInLength, codec.Sector, frames * CD_MAX_SECTOR_DATA, true, codec, out int pos);
            if (err != ChdError.CHDERR_NONE)
                return err;

            err = Zlib(buffIn, pos, buffInLength - pos, codec.Subcode, frames * CD_MAX_SUBCODE_DATA);
            if (err != ChdError.CHDERR_NONE)
                return err;

            /* reassemble the data */
            for (int framenum = 0; framenum < frames; framenum++)
            {
                Array.Copy(codec.Sector, framenum * CD_MAX_SECTOR_DATA, buffOut, framenum * CD_FRAME_SIZE, CD_MAX_SECTOR_DATA);
                Array.Copy(codec.Subcode, framenum * CD_MAX_SUBCODE_DATA, buffOut, (framenum * CD_FRAME_SIZE) + CD_MAX_SECTOR_DATA, CD_MAX_SUBCODE_DATA);
            }
            return ChdError.CHDERR_NONE;
        }

        /*
        Source input CiBuffer structure:

         Header:
         00     =  Size of the Meta Data to be put into the output CiBuffer right after the header.
         01     =  Number of Audio Channel.
         02,03  =  Number of Audio sampled values per chunk.
         04,05  =  width in pixels of image.
         06,07  =  height in pixels of image.
         08,09  =  Size of the source data for the audio channels huffman trees. (set to 0xffff is using FLAC.)

         10,11  =  size of compressed audio channel 1
         12,13  =  size of compressed audio channel 2
         .
         .         (Max audio channels coded to 16)
         Total Header size = 10 + 2 * Number of Audio Channels.


         Meta Data: (Size from header 00)

         Audio Huffman Tree: (Size from header 08,09)

         Audio Compressed Data Channels: (Repeated for each Audio Channel, Size from Header starting at 10,11)

         Video Compressed Data:   Rest of Input Chuck.
        */

        internal static ChdError AvHuff(byte[] buffIn, int buffInLength, byte[] buffOut, int buffOutLength, ChdCodec codec)
        {
            // extract info from the header
            if (buffInLength < 8)
                return ChdError.CHDERR_INVALID_DATA;
            uint metaDataLength = buffIn[0];
            uint audioChannels = buffIn[1];
            uint audioSamplesPerBlock = buffIn.ReadUInt16B(2);
            uint videoWidth = buffIn.ReadUInt16B(4);
            uint videoHeight = buffIn.ReadUInt16B(6);

            uint sourceTotalSize = 10 + (2 * audioChannels);
            // validate that the sizes make sense
            if (buffInLength < sourceTotalSize)
                return ChdError.CHDERR_INVALID_DATA;

            sourceTotalSize += metaDataLength;

            uint audioHuffmanTreeSize = buffIn.ReadUInt16B(8);
            if (audioHuffmanTreeSize != 0xffff)
                sourceTotalSize += audioHuffmanTreeSize;

            uint?[] audioChannelCompressedSize = new uint?[16];
            for (int chnum = 0; chnum < audioChannels; chnum++)
            {
                audioChannelCompressedSize[chnum] = buffIn.ReadUInt16B(10 + (2 * chnum));
                sourceTotalSize += (uint)audioChannelCompressedSize[chnum];
            }

            if (sourceTotalSize >= buffInLength)
                return ChdError.CHDERR_INVALID_DATA;

            // starting offsets of source data
            uint buffInIndex = 10 + (2 * audioChannels);


            uint destOffset = 0;
            // create a header
            buffOut[0] = (byte)'c';
            buffOut[1] = (byte)'h';
            buffOut[2] = (byte)'a';
            buffOut[3] = (byte)'v';
            buffOut[4] = (byte)metaDataLength;
            buffOut[5] = (byte)audioChannels;
            buffOut[6] = (byte)(audioSamplesPerBlock >> 8);
            buffOut[7] = (byte)audioSamplesPerBlock;
            buffOut[8] = (byte)(videoWidth >> 8);
            buffOut[9] = (byte)videoWidth;
            buffOut[10] = (byte)(videoHeight >> 8);
            buffOut[11] = (byte)videoHeight;
            destOffset += 12;



            uint metaDestStart = destOffset;
            if (metaDataLength > 0)
            {
                Array.Copy(buffIn, (int)buffInIndex, buffOut, (int)metaDestStart, (int)metaDataLength);
                buffInIndex += metaDataLength;
                destOffset += metaDataLength;
            }

            uint?[] audioChannelDestStart = new uint?[16];
            for (int chnum = 0; chnum < audioChannels; chnum++)
            {
                audioChannelDestStart[chnum] = destOffset;
                destOffset += 2 * audioSamplesPerBlock;
            }
            uint videoDestStart = destOffset;


            // decode the audio channels
            if (audioChannels > 0)
            {
                // decode the audio
                ChdError err = AvHuffDecodeAudio(audioChannels, audioSamplesPerBlock, buffIn, buffInIndex, audioHuffmanTreeSize, audioChannelCompressedSize, buffOut, audioChannelDestStart, codec);
                if (err != ChdError.CHDERR_NONE)
                    return err;

                // advance the pointers past the data
                if (audioHuffmanTreeSize != 0xffff)
                    buffInIndex += audioHuffmanTreeSize;
                for (int chnum = 0; chnum < audioChannels; chnum++)
                    buffInIndex += (uint)audioChannelCompressedSize[chnum];
            }

            // decode the video data
            if (videoWidth > 0 && videoHeight > 0)
            {
                uint videostride = 2 * videoWidth;
                // decode the video
                ChdError err = AvHuffDecodeVideo(videoWidth, videoHeight, buffIn, buffInIndex, (uint)buffInLength - buffInIndex, buffOut, videoDestStart, videostride, codec);
                if (err != ChdError.CHDERR_NONE)
                    return err;
            }

            uint videoEnd = videoDestStart + (videoWidth * videoHeight * 2);
            for (uint index = videoEnd; index < buffOutLength; index++)
                buffOut[index] = 0;

            return ChdError.CHDERR_NONE;
        }


        private static ChdError AvHuffDecodeAudio(uint channels, uint samples, byte[] buffIn, uint buffInOffset, uint treesize, uint?[] audioChannelCompressedSize, byte[] buffOut, uint?[] audioChannelDestStart, ChdCodec codec)
        {
            // if the tree size is 0xffff, the streams are FLAC-encoded
            if (treesize == 0xffff)
            {
                int blockSize = (int)samples * 2;

                // loop over channels
                for (int channelNumber = 0; channelNumber < channels; channelNumber++)
                {
                    // extract the size of this channel
                    uint sourceSize = audioChannelCompressedSize[channelNumber] ?? 0;

                    uint? curdest = audioChannelDestStart[channelNumber];
                    if (curdest != null)
                    {
                        codec.AvHuffSettings ??= new AudioPCMConfig(16, 1, 48000);
                        codec.AvHuffAudioDecoder ??= new AudioDecoder(codec.AvHuffSettings); //read the data and decode it in to a 1D array of samples - the CiBuffer seems to want 2D :S
                        AudioBuffer audioBuffer = new AudioBuffer(codec.AvHuffSettings, blockSize); //audio CiBuffer to take decoded samples and read them to bytes.
                        int read;
                        int inPos = (int)buffInOffset;
                        int outPos = (int)audioChannelDestStart[channelNumber];

                        while (outPos < blockSize + audioChannelDestStart[channelNumber])
                        {
                            if ((read = codec.AvHuffAudioDecoder.DecodeFrame(buffIn, inPos, (int)sourceSize)) == 0)
                                break;
                            if (codec.AvHuffAudioDecoder.Remaining != 0)
                            {
                                codec.AvHuffAudioDecoder.Read(audioBuffer, (int)codec.AvHuffAudioDecoder.Remaining);
                                Array.Copy(audioBuffer.Bytes, 0, buffOut, outPos, audioBuffer.ByteLength);
                                outPos += audioBuffer.ByteLength;
                            }
                            inPos += read;
                        }

                        byte tmp;
                        for (int i = (int)audioChannelDestStart[channelNumber]; i < blockSize + audioChannelDestStart[channelNumber]; i += 2)
                        {
                            tmp = buffOut[i];
                            buffOut[i] = buffOut[i + 1];
                            buffOut[i + 1] = tmp;
                        }

                    }

                    // advance to the next channel's data
                    buffInOffset += sourceSize;
                }
                return ChdError.CHDERR_NONE;
            }


            // if we have a non-zero tree size, extract the trees
            Huffman m_audiohi_decoder = null;
            Huffman m_audiolo_decoder = null;
            if (treesize != 0)
            {
                BitStream bitbuf = new BitStream(buffIn, (int)buffInOffset, (int)treesize);

                if (codec.HuffmanHi == null) codec.HuffmanHi = new ushort[1 << 16];
                if (codec.HuffmanLo == null) codec.HuffmanLo = new ushort[1 << 16];

                m_audiohi_decoder = new Huffman(256, 16, bitbuf, codec.HuffmanHi);
                m_audiolo_decoder = new Huffman(256, 16, bitbuf, codec.HuffmanLo);

                HuffmanError hufferr = m_audiohi_decoder.ImportTreeRle();
                if (hufferr != HuffmanError.HUFFERR_NONE)
                    return ChdError.CHDERR_INVALID_DATA;
                bitbuf.flush();
                hufferr = m_audiolo_decoder.ImportTreeRle();
                if (hufferr != HuffmanError.HUFFERR_NONE)
                    return ChdError.CHDERR_INVALID_DATA;
                if (bitbuf.flush() != treesize)
                    return ChdError.CHDERR_INVALID_DATA;
                buffInOffset += treesize;
            }

            // loop over channels
            for (int chnum = 0; chnum < channels; chnum++)
            {
                // only process if the data is requested
                uint? curdest = audioChannelDestStart[chnum];
                if (curdest != null)
                {
                    int prevsample = 0;

                    // if no huffman length, just copy the data
                    if (treesize == 0)
                    {
                        uint cursource = buffInOffset;
                        for (int sampnum = 0; sampnum < samples; sampnum++)
                        {
                            int delta = (buffIn[cursource + 0] << 8) | buffIn[cursource + 1];
                            cursource += 2;

                            int newsample = prevsample + delta;
                            prevsample = newsample;

                            buffOut[(uint)curdest + 0] = (byte)(newsample >> 8);
                            buffOut[(uint)curdest + 1] = (byte)newsample;
                            curdest += 2;
                        }
                    }

                    // otherwise, Huffman-decode the data
                    else
                    {
                        BitStream bitbuf = new BitStream(buffIn, (int)buffInOffset, (int)audioChannelCompressedSize[chnum]);
                        m_audiohi_decoder.AssignBitStream(bitbuf);
                        m_audiolo_decoder.AssignBitStream(bitbuf);
                        for (int sampnum = 0; sampnum < samples; sampnum++)
                        {
                            short delta = (short)(m_audiohi_decoder.DecodeOne() << 8);
                            delta |= (short)m_audiolo_decoder.DecodeOne();

                            int newsample = prevsample + delta;
                            prevsample = newsample;

                            buffOut[(uint)curdest + 0] = (byte)(newsample >> 8);
                            buffOut[(uint)curdest + 1] = (byte)newsample;
                            curdest += 2;
                        }
                        if (bitbuf.overflow())
                            return ChdError.CHDERR_INVALID_DATA;
                    }
                }

                // advance to the next channel's data
                buffInOffset += (uint)audioChannelCompressedSize[chnum];
            }
            return ChdError.CHDERR_NONE;
        }



        private static ChdError AvHuffDecodeVideo(uint width, uint height, byte[] buffIn, uint buffInOffset, uint buffInLength, byte[] buffOut, uint buffOutOffset, uint dstride, ChdCodec codec)
        {
            // if the high bit of the first byte is set, we decode losslessly
            if ((buffIn[buffInOffset] & 0x80) == 0)
                return ChdError.CHDERR_INVALID_DATA;

            // skip the first byte
            BitStream bitbuf = new BitStream(buffIn, (int)buffInOffset, (int)buffInLength);
            bitbuf.read(8);

            if (codec.HuffmanY == null) codec.HuffmanY = new ushort[1 << 16];
            if (codec.HuffmanCB == null) codec.HuffmanCB = new ushort[1 << 16];
            if (codec.HuffmanCR == null) codec.HuffmanCR = new ushort[1 << 16];

            HuffmanDecoderRLE m_ycontext = new HuffmanDecoderRLE(256 + 16, 16, bitbuf, codec.HuffmanY);
            HuffmanDecoderRLE m_cbcontext = new HuffmanDecoderRLE(256 + 16, 16, bitbuf, codec.HuffmanCB);
            HuffmanDecoderRLE m_crcontext = new HuffmanDecoderRLE(256 + 16, 16, bitbuf, codec.HuffmanCR);

            // import the tables
            HuffmanError hufferr = m_ycontext.ImportTreeRle();
            if (hufferr != HuffmanError.HUFFERR_NONE)
                return ChdError.CHDERR_INVALID_DATA;
            bitbuf.flush();
            hufferr = m_cbcontext.ImportTreeRle();
            if (hufferr != HuffmanError.HUFFERR_NONE)
                return ChdError.CHDERR_INVALID_DATA;
            bitbuf.flush();
            hufferr = m_crcontext.ImportTreeRle();
            if (hufferr != HuffmanError.HUFFERR_NONE)
                return ChdError.CHDERR_INVALID_DATA;
            bitbuf.flush();

            // decode to the destination
            m_ycontext.Reset();
            m_cbcontext.Reset();
            m_crcontext.Reset();

            for (int dy = 0; dy < height; dy++)
            {
                uint row = buffOutOffset + ((uint)dy * dstride);
                for (int dx = 0; dx < width / 2; dx++)
                {
                    buffOut[row + 0] = (byte)m_ycontext.DecodeOne();
                    buffOut[row + 1] = (byte)m_cbcontext.DecodeOne();
                    buffOut[row + 2] = (byte)m_ycontext.DecodeOne();
                    buffOut[row + 3] = (byte)m_crcontext.DecodeOne();
                    row += 4;
                }
                m_ycontext.FlushRLE();
                m_cbcontext.FlushRLE();
                m_crcontext.FlushRLE();
            }

            // check for errors if we overflowed or decoded too little data
            if (bitbuf.overflow() || bitbuf.flush() != buffInLength)
                return ChdError.CHDERR_INVALID_DATA;
            return ChdError.CHDERR_NONE;
        }


    }
}