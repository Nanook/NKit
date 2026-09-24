using System;

namespace Nanook.NKit
{
    internal class TheOverseer
    {
        private bool _enabled;
        private NKitInput _input;
        private NKitStepContext _stepContext;
        private IImage _image;
        private IImageInfo _info;

        //Section processing checks
        private int _area0BlockSize;
        private int _lastAreaNumber;
        private long _lastImageOffset;
        private long _lastSize;
        private long _lastAreaImageOffset;
        private AreaType _lastAreaType;
        private AreaInfo _lastArea;

        /// <summary>
        /// Tests processing state to detect development errors. Won't be required to be enabled in release. To compliment the UnitTests as they are added
        /// </summary>
        public TheOverseer(IImageInfo info, NKitStepContext stepContext)
        {
            _enabled = false;
#if DEBUG
            if (stepContext.CancelToken != null)
                _enabled = true;
            // NOTE: previously this registered a callback on stepContext.CancelToken to flip
            // _enabled off on cancellation:
            //     stepContext.CancelToken.Value.Register(() => _enabled = false);
            // That CancellationToken is the HOST's app-lifetime source (one CancellationTokenSource
            // reused for every image in the UI/CLI loop). The registration's CancellationTokenRegistration
            // was discarded (never disposed), and its closure captured this TheOverseer -> _stepContext
            // -> Scan (the whole ScanArea/ScanSection/FST graph). So every image left one overseer (and
            // its entire scan graph) rooted on the never-cancelled token FOREVER — a ~5 MB/image leak in
            // Debug builds that survived the per-image forced GC. We now read the token DIRECTLY per
            // section (see SectionProcessed) instead of registering a callback: same behaviour (checks
            // stop when cancelled), no rooting, no leak.
#endif
            _stepContext = stepContext;
            _lastAreaNumber = -1;
            _info = info;
            _area0BlockSize = 0;
            _lastImageOffset = 0;
            _lastSize = 0;
            _lastAreaImageOffset = 0;
            _lastArea = null;
            _lastAreaType = AreaType.None;
        }

        internal void SetInput(NKitInput input)
        {
            if (!_enabled)
                return;
            _input = input;
        }

        internal void SetImage(IImage image)
        {
            if (!_enabled)
                return;
            _image = image;
        }

        internal void SectionProcessed(ISectionProcessor ns)
        {
            if (!_enabled)
                return;

            // Direct, allocation-free cancellation check (replaces the old CancellationToken.Register
            // callback that leaked this overseer's scan graph onto the host's app-lifetime token).
            if (_stepContext?.CancelToken?.IsCancellationRequested ?? false)
            {
                _enabled = false;
                return;
            }

            AreaInfo ai = ns.Buffer.AreaInfo;
            IBuffer buff = ns.Buffer;

            if (ns.Buffer == null || ns.Buffer.AreaInfo == null)
                throw new Exception("Section missing Buffer or AreaInfo");

            if (buff.AreaOffset == 0)
                _area0BlockSize = buff.Size;
            else if (!(_image.Type == ImageType.WiiU && buff.Type == AreaType.Other) && _lastSize != _area0BlockSize) //only the very last CiBuffer can differ in size - wiiU other sections repeat the previous CiBuffer. Ignore them
                throw new Exception("Bad Buffer Size");

            if (buff.ImageOffset != _lastImageOffset + _lastSize)
                throw new Exception("Bad ImageOffset");

            if (buff.AreaInfo.ImageOffset != _lastAreaImageOffset && buff.AreaOffset != 0)
                throw new Exception("Bad Area ImageOffset");

            if (ai.Type != _lastAreaType && buff.AreaOffset != 0)
                throw new Exception("Bad Area Type");

            if (buff.AreaOffset == 0 && ai.AreaNo != _lastAreaNumber + 1)
                throw new Exception("Bad New Area Number");

            if (buff.AreaOffset != 0 && !Object.ReferenceEquals(ai, _lastArea))
                throw new Exception("Area object has changed");

            if (buff.AreaOffset == 0 && Object.ReferenceEquals(ai, _lastArea))
                throw new Exception("Area object has not changed");

            if (buff.AreaOffset != 0 && ai.AreaNo == _lastAreaNumber + 1)
                throw new Exception("Bad Area Number");

            if (buff.ImageOffset - ai.ImageOffset != buff.AreaOffset)
                throw new Exception("Bad Area Buffer.AreaOffset or Area.ImageOffset");


            switch (_image.Type)
            {
                case ImageType.GameCube:
                    break;
                case ImageType.Wii:
                    wiiTests(ai, ns, buff);
                    break;
                case ImageType.WiiU:
                    wiiUTests(ai, ns, buff);
                    break;
                case ImageType.Iso9660:
                    break;
                default:
                    break;
            }

            _lastArea = buff.AreaInfo;
            _lastImageOffset = buff.ImageOffset;
            _lastSize = buff.Size;
            _lastAreaImageOffset = ai.ImageOffset;
            _lastAreaType = ai.Type;
            _lastAreaNumber = ai.AreaNo;
        }

        private void wiiTests(AreaInfo ai, ISectionProcessor ns, IBuffer buff)
        {
            switch (ai.Type)
            {
                case AreaType.ImageHeader:
                case AreaType.PartitionHeader:
                case AreaType.Other:
                    if (ai.IsEncrypted || ai.IsEncryptionSupported)
                        throw new Exception("Does not support encryption");
                    break;
                case AreaType.FileSystem:
                    if (ai.IsEncryptionSupported != _info.OutputEncryption)
                        throw new Exception("Bad Encryption setting");
                    // The area must already be encrypted at this point ONLY when the source itself
                    // carries encryption. For a decoded/plaintext source (e.g. a decoded NKit or RVZ,
                    // SourceHasEncryption == false) the FileSystem area is legitimately plaintext here
                    // and encryption is applied downstream in the SectionProcessor, so IsEncrypted
                    // being false is expected — do not flag it. (Patching also legitimately leaves it
                    // unencrypted.)
                    if (_info.OutputEncryption && _info.SourceHasEncryption && !ai.IsEncrypted && !buff.PatchInfo.MarkForPatching)
                        throw new Exception("Bad Encryption setting");
                    else if (!_info.OutputEncryption && ai.IsEncrypted)
                        throw new Exception("Bad Encryption setting");
                    break;
                case AreaType.PartitionTable:
                case AreaType.FstBlock:
                case AreaType.None:
                case AreaType.Audio:
                default:
                    throw new Exception("Bad Area.Type");
            }
        }

        private void wiiUTests(AreaInfo ai, ISectionProcessor ns, IBuffer buff)
        {
            switch (ai.Type)
            {
                case AreaType.ImageHeader:
                case AreaType.PartitionHeader:
                case AreaType.RawKeyMissing:
                    if (ai.IsEncrypted || ai.IsEncryptionSupported)
                        throw new Exception("Does not support encryption");
                    break;
                case AreaType.Other:
                case AreaType.FileSystem:
                case AreaType.PartitionTable:
                case AreaType.FstBlock:
                    if (!ai.IsEncryptionSupported && !ai.IsEncrypted)
                        throw new Exception("Bad Encryption setting");
                    break;
                case AreaType.None:
                case AreaType.Audio:
                default:
                    throw new Exception("Bad Area.Type");
            }
        }

    }
}