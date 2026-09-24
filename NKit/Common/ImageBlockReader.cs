using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Nanook.NKit
{
    public enum ImageBlockType { Other, Process, Raw, Missing, Compressed }

    internal class ImageBlockInfo<T>
    {
        public ulong Offset;
        public int Size;
        public ImageBlockType Type;
        public int Index;
        public T Item;
        public ulong FullOffset;
        public int FullSize;
        public int RefLastIdx; //last index
        public int RefByCount; //referenced by count
        public int RefIndex; //reference another block
        public byte[] RefSrcBuff; //src CiBuffer reference (only used for parallel shared ref blocks)
        public byte[] Cached;

        public override string ToString() => $"{Index:x} Full({FullOffset:x9} {FullSize:x9}) Src({Offset:x9} {Size:x9}) {Type}";
    }
    internal class ImageBlockBuff<T>
    {
        public ImageBlockBuff(byte[] inBuff)
        {
            this.Buff = inBuff;
            this.BuffSize = 0; //buffered
        }

        public byte[] Buff;
        public byte[] Temp;
        public byte[] Result;
        public int BuffSize;
        public int OutSrcOffset;
        public int OutDstOffset;
        public int OutSize;
        public ImageBlockInfo<T> Info;
    }

    internal class ImageBlockReader<T>
    {
        public delegate void BlockProcess(ImageBlockBuff<T> buff, int threadIdx);
        public delegate void AreaType(int off, int size, ImageBlockType type);

        private bool _requiresTemp;

        public bool UniformBlockSize { get; set; } //block sizes are the same for full file
        public int BlockSize { get; set; }
        public ulong FullImageSize { get; set; }
        public int LastSize { get; set; } //sometimes the last block is not full
        public int LastIdx { get; set; } //stored for quick access
        public bool HasEncodedBuffer { get; set; }

        public List<ImageBlockInfo<T>> Items { get; set; }

        private List<ImageBlockBuff<T>> _encodedBuff;
        private byte[][] _threadTempBuffs;
        private byte[][] _threadResultBuffs;
        private BlockingThreadQueue<int> _queue;
        private ImageBlockBuff<T> _rawBuff;

        private readonly bool _useTempBuffer;
        private readonly int _processThreads;

        private readonly bool _sequentialRead; //enables cached block counters to enable CiBuffer release
        private readonly bool _enableCaching;
        private int _currentIndex; //pointer to next block, allows rvz to skip index from offset lookup

        public ImageBlockReader(bool sequentialRead, bool enableCaching, bool useTempBuffer, int enqueueSize, int processThreads)
        {
            _sequentialRead = sequentialRead;
            _enableCaching = enableCaching;
            _currentIndex = 0;
            _useTempBuffer = useTempBuffer;
            _processThreads = processThreads;
            this.Items = new List<ImageBlockInfo<T>>();
            _queue = new BlockingThreadQueue<int>(enqueueSize, processThreads, false);
        }



        public void AddItem(ulong offset, uint blockSize, ImageBlockType type, T item) => AddItem(offset, 0, blockSize, type, item);

        public void AddItem(ulong offset, int size, uint blockSize, ImageBlockType type, T item) => AddItem(offset, size, (ulong)(this.Items.Count * blockSize), type, item);
        public void AddItem(ulong offset, int size, ulong fullOffset, ImageBlockType type, T item)
        {
            if (type == ImageBlockType.Compressed || type == ImageBlockType.Process)
                this.HasEncodedBuffer = true;

            this.Items.Add(new ImageBlockInfo<T>() { Index = this.Items.Count, Offset = offset, Size = size, FullOffset = fullOffset, Type = type, Item = item });
        }

        public void AddItem(ulong offset, ulong fullOffset, ImageBlockType type, T item) => AddItem(offset, 0, fullOffset, type, item);

        public void CompletedAddItems(bool calcSize, ulong endOffset, int blockSize, ulong imageFullSize, bool requiresTemp)
        {
            this.FullImageSize = imageFullSize;
            this.BlockSize = blockSize;
            _requiresTemp = requiresTemp;

            if (this.Items.Count == 0)
                return;

            bool sameSize = true;

            Dictionary<ulong, int> dupeTest = new Dictionary<ulong, int>();
            dupeTest.Add(this.Items[0].Offset, 0);

            for (int i = 0; i < this.Items.Count - 1; i++)
            {
                if (this.BlockSize != (this.Items[i].FullSize = (int)(this.Items[i + 1].FullOffset - this.Items[i].FullOffset)))
                    sameSize = false;

                if (calcSize)
                    this.Items[i].Size = (int)(this.Items[i + 1].Offset - this.Items[i].Offset);

                if (_enableCaching && this.Items[i + 1].Type != ImageBlockType.Missing)
                {
                    int ptr;
                    if (dupeTest.TryGetValue(this.Items[i + 1].Offset, out ptr)) //is this a dupe
                    {
                        this.Items[i + 1].RefIndex = ptr;
                        this.Items[ptr].RefByCount++;
                        this.Items[ptr].RefLastIdx = i + 1;
                    }
                    else
                        dupeTest.Add(this.Items[i + 1].Offset, i + 1);
                }
            }
            this.UniformBlockSize = sameSize;

            ImageBlockInfo<T> last = this.Items.Last();
            last.FullSize = (int)(imageFullSize - last.FullOffset);
            if (calcSize)
            {
                if (endOffset < last.Offset)
                    throw new HandledException($"endOffet {endOffset:x9} is less than the last block offset {last.Offset:x9}");
                last.Size = (int)(endOffset - last.Offset);
            }

            this.LastIdx = this.Items.Count - 1;
            this.LastSize = (int)(this.FullImageSize - ((ulong)this.BlockSize * (ulong)this.LastIdx));

            int sz = blockSize + (blockSize >> 2);
            if (_useTempBuffer)
            {
                _threadTempBuffs = new byte[_processThreads][];
                for (int i = 0; i < _processThreads; i++)
                    _threadTempBuffs[i] = new byte[sz];
            }
            _threadResultBuffs = new byte[_processThreads][];
            for (int i = 0; i < _processThreads; i++)
                _threadResultBuffs[i] = new byte[sz];

            _rawBuff = new ImageBlockBuff<T>(new byte[sz]);

            _encodedBuff = new List<ImageBlockBuff<T>>();
            _encodedBuff.Add(new ImageBlockBuff<T>(new byte[sz]));

        }

        public ImageBlockInfo<T> GetItem(long offset)
        {
            int readIdx;
            // _currentIndex is a forward-sequential fast-path hint only: when the next expected
            // block matches the requested offset, use it directly. For ANY other offset (a backward
            // or random re-read — e.g. an up-front FST peek followed by the real sequential read of
            // the same group) fall through to the offset lookup. The old code short-circuited to
            // null whenever _currentIndex had reached the end, which broke re-reads after a peek had
            // consumed all blocks (GetItem returned null -> a 0-byte read -> stale buffer bytes).
            if (_currentIndex > 0 && _currentIndex < this.Items.Count && this.Items[_currentIndex].FullOffset == (ulong)offset)
                readIdx = _currentIndex;
            else
            {
                readIdx = this.UniformBlockSize ? (int)(offset / this.BlockSize) : binarySearch((ulong)offset, this.Items, 0, this.Items.Count);
                if (readIdx == -1 || readIdx >= this.Items.Count)
                    return null;
            }
            return this.Items[readIdx];
        }

        public int Read(long imageOffset, Stream stream, byte[] buffer, int buffOffset, int size, AreaType areaType, BlockProcess process, out ImageBlockInfo<T> firstItem) //compData, Size, Dest, offset, size
        {
            firstItem = null;
            if (size == 0)
                return 0;

            int buffRead = this.buffRead(imageOffset, buffer, buffOffset, size, areaType);

            if (buffRead == size)
            {
                firstItem = _rawBuff.Info;
                return buffRead;
            }

            long readOffset = imageOffset + buffRead; //current position
            buffOffset += buffRead;
            int readSize = Math.Min(size - buffRead, (int)(buffer.Length - buffOffset));

            firstItem = this.GetItem(readOffset);
            if (firstItem == null)
                return 0;

            int readIdx = firstItem.Index;
            int blocks = setOutputBuffers(readOffset, readSize, firstItem, buffOffset, areaType);

            _queue.Init();  //reset for reuse

            Task processing = _queue.Process(
                (state, threadIdx) => //parallel process
                {
                    int bi = (int)state;
                    ImageBlockBuff<T> buff = _encodedBuff[bi];

                    buff.Info = this.Items[readIdx + bi];
                    buff.Temp = _threadTempBuffs == null ? null : _threadTempBuffs[threadIdx];
                    buff.Result = _threadResultBuffs[threadIdx];

                    if (_enableCaching)
                    {
                        if (buff.Info.RefByCount != 0 || buff.Info.RefIndex != 0)
                            cachedItem(process, threadIdx, buff);
                        else
                            process(buff, threadIdx);
                        buff.Info.RefSrcBuff = null;
                    }
                    else
                        process(buff, threadIdx);

                    if ((int)bi < blocks)
                        Array.Copy(buff.Result, buff.OutSrcOffset, buffer, buff.OutDstOffset, buff.OutSize);

                    if ((int)bi == blocks - 1)
                    {
                        Array.Copy(buff.Result, _rawBuff.Buff, buff.Info.FullSize);
                        _rawBuff.Info = buff.Info;
                        _rawBuff.BuffSize = buff.Info.FullSize;
                    }
                }, null);

            //add blocks to queue
            ImageBlockInfo<T> block;
            for (int buffIdx = 0; buffIdx < blocks; buffIdx++)
            {
                block = this.Items[readIdx + buffIdx];
                ImageBlockInfo<T> cacheItem = block.RefIndex == 0 ? block : this.Items[block.RefIndex];

                if (!_enableCaching || (_sequentialRead && block.RefIndex == 0) || (cacheItem.RefSrcBuff == null && cacheItem.Cached == null)) //not referencing another block OR first access to a shared block
                {
                    stream.SafeSeek((long)block.Offset, SeekOrigin.Begin);
                    _encodedBuff[buffIdx].BuffSize = stream.Read(_encodedBuff[buffIdx].Buff, 0, this.Items[readIdx + buffIdx].Size);
                    if (_enableCaching)
                        cacheItem.RefSrcBuff = _encodedBuff[buffIdx].Buff;
                }
                else
                    _encodedBuff[buffIdx].BuffSize = this.Items[readIdx + buffIdx].Size;
                _queue.Add(buffIdx);
            }
            _currentIndex = readIdx + blocks;

            _queue.AddComplete();
            processing.Wait();


            //if sequential, remove any cache blocks no longer required
            if (_enableCaching && _sequentialRead) //lockless
            {
                foreach (ImageBlockBuff<T> buff in _encodedBuff.Where(a => a.Info.RefIndex != 0 && a.Info.Index >= this.Items[a.Info.RefIndex].RefLastIdx))
                    this.Items[buff.Info.RefIndex].Cached = null;
            }

            return buffRead + readSize; //resulting size of max buff
        }

        private void cachedItem(BlockProcess process, int threadIdx, ImageBlockBuff<T> buff)
        {
            //cater for sequential and random access
            ImageBlockInfo<T> cacheItem = buff.Info.RefIndex == 0 ? buff.Info : this.Items[buff.Info.RefIndex];
            if (cacheItem.Cached == null) //fast and dirty
            {
                lock (cacheItem)
                {
                    if (cacheItem.RefSrcBuff == null && cacheItem.Cached == null)
                        Monitor.Wait(cacheItem);
                    if (cacheItem.Cached == null) //clean
                    {
                        byte[] temp = buff.Buff; //switch the CiBuffer out for the cacheItem
                        buff.Buff = cacheItem.RefSrcBuff;
                        process(buff, threadIdx);
                        buff.Buff = temp;

                        cacheItem.Cached = (byte[])buff.Result.Clone();
                        Array.Copy(cacheItem.Cached, buff.Result, buff.Info.FullSize);
                        Monitor.PulseAll(cacheItem);
                        cacheItem.RefSrcBuff = null;
                        return;
                    }
                }
            }
            Array.Copy(cacheItem.Cached, buff.Result, buff.Info.FullSize);
        }

        private int setOutputBuffers(long readOffset, int readSize, ImageBlockInfo<T> currentBlock, int currentPos, AreaType areaType)
        {
            int buffIdx = 0;
            _encodedBuff[buffIdx].Info = currentBlock;
            _encodedBuff[buffIdx].OutSrcOffset = (int)((ulong)readOffset - currentBlock.FullOffset); //offset in block
            _encodedBuff[buffIdx].OutSize = Math.Min(readSize, _encodedBuff[buffIdx].Info.FullSize - _encodedBuff[buffIdx].OutSrcOffset);
            _encodedBuff[buffIdx].OutDstOffset = currentPos;
            if (areaType != null)
                areaType(_encodedBuff[buffIdx].OutDstOffset, _encodedBuff[buffIdx].OutSize, _encodedBuff[buffIdx].Info.Type);

            int sz = _encodedBuff[buffIdx].OutSize;
            while (sz < readSize)
            {
                buffIdx++;
                if (_encodedBuff.Count == buffIdx) //grow to accomodate max size
                    _encodedBuff.Add(new ImageBlockBuff<T>(new byte[_rawBuff.Buff.Length]));

                _encodedBuff[buffIdx].Info = this.Items[currentBlock.Index + buffIdx];
                _encodedBuff[buffIdx].OutSize = Math.Min(_encodedBuff[buffIdx].Info.FullSize, readSize - sz);
                _encodedBuff[buffIdx].OutSrcOffset = 0;
                _encodedBuff[buffIdx].OutDstOffset = _encodedBuff[buffIdx - 1].OutDstOffset + _encodedBuff[buffIdx - 1].OutSize;
                if (areaType != null)
                    areaType(_encodedBuff[buffIdx].OutDstOffset, _encodedBuff[buffIdx].OutSize, _encodedBuff[buffIdx].Info.Type);
                sz += _encodedBuff[buffIdx].OutSize;
            }

            return buffIdx + 1; //return the count of the blocks required to process
        }

        private int buffRead(long imageOffset, byte[] buffer, int offset, int size, AreaType areaType)
        {
            int readSize = Math.Min(size, (int)(buffer.Length - offset));

            //check the CiBuffer
            if (_rawBuff.Info != null && imageOffset >= (long)_rawBuff.Info.FullOffset && imageOffset < (long)_rawBuff.Info.FullOffset + _rawBuff.BuffSize)
            {
                int p = (int)((ulong)imageOffset - _rawBuff.Info.FullOffset);
                int read = (int)Math.Min((long)_rawBuff.Info.FullOffset + _rawBuff.BuffSize - imageOffset, readSize);
                Array.Copy(_rawBuff.Buff, p, buffer, offset, read);
                if (areaType != null)
                    areaType(0, read, _rawBuff.Info.Type);
                return read;
            }
            return 0;
        }

        /// <summary>
        /// Release the large per-image buffers this reader holds so they are freed at image
        /// completion rather than lingering until GC. For a .gcz source there is one reader per
        /// image holding: the per-block Items list (one ImageBlockInfo per GCZ block — tens to
        /// hundreds of thousands for a large image, each potentially caching a decompressed block
        /// via Cached/RefSrcBuff), the per-thread temp/result block buffers, and the encoded-buffer
        /// list. None of this is needed once the image is done. Idempotent.
        /// </summary>
        public void Release()
        {
            if (this.Items != null)
            {
                foreach (ImageBlockInfo<T> it in this.Items)
                {
                    it.Cached = null;
                    it.RefSrcBuff = null;
                    it.Item = default;
                }
                this.Items.Clear();
            }
            _threadTempBuffs = null;
            _threadResultBuffs = null;
            _encodedBuff = null;
            _rawBuff = null;
        }

        private int binarySearch(ulong key, List<ImageBlockInfo<T>> array, int low, int high)
        {
            if (low > high)
                return -1;
            int mid = (low + high) / 2;
            int compare = key < array[mid].FullOffset ? -1 : (key >= array[mid].FullOffset + (ulong)array[mid].FullSize ? 1 : 0);
            if (compare == 0)
                return mid;
            if (compare < 0)
                return binarySearch(key, array, low, mid - 1);
            else
                return binarySearch(key, array, mid + 1, high);
        }

    }

}