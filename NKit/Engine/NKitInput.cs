using Nanook.NKit.Container;
using System;
using System.IO;
using System.Text.RegularExpressions;

namespace Nanook.NKit
{
    internal class NKitInput : IInput, IDisposable
    {
        private IImageContext _context;
        private IAsIso _iso;
        private BufferStream _isoStream; // Layer-B view handed to the Image; disposed in Close()
        private bool _closed;

        public long Position { get; set; }

        public NKitInput(IImageContext context)
        {
            _context = context;
        }

        public IImage Image { get; private set; }

        internal static IAsIso DetectImage(Stream stream, IImageContext context)
        {

            byte[] header = new byte[0x400 * 0x400]; //1 MiB
            stream.Read(header, 0, -header.Length); //read and rewind

            IAsIso iso = NKitAsIso.Create(header) ??
                         DataStoreAsIso.Create(header, context) ??
                         WuxAsIso.Create(header) ??
                         TmdAppAsIso.Create(header, context.SourceFile.IndexFile) ??
                         WiaAsIso.Create(header) ??
                         RvzAsIso.Create(header) ??
                         CisoAsIso.Create(header) ??
                         JsoAsIso.Create(header) ??
                         DaxAsIso.Create(header) ??
                         CsoZsoAsIso.Create(header) ??
                         WbfsAsIso.Create(header) ??
                         IsoDecAsIso.Create(header) ??
                         GczAsIso.Create(header) ??
                         ChdAsIso.Create(header, context.SourceFile.Name, true) ??
                         DefaultAsIso.Create(header, context.SourceFile.IndexFile?.FileType ?? IndexFileType.None);

            return iso;
        }

        // Container detection chain used by Open (the production read path). The order is the
        // detection precedence; each Create returns null when the header does not match.
        private IAsIso createImageContainer(byte[] id, bool canUseCustomChkSum)
        {
            return NKitAsIso.Create(id) ??
                   WuxAsIso.Create(id) ??
                   TmdAppAsIso.Create(id, _context.SourceFile.IndexFile) ??
                   WiaAsIso.Create(id) ??
                   RvzAsIso.Create(id) ??
                   CisoAsIso.Create(id) ??
                   JsoAsIso.Create(id) ??
                   DaxAsIso.Create(id) ??
                   CsoZsoAsIso.Create(id) ??
                   WbfsAsIso.Create(id) ??
                   IsoDecAsIso.Create(id) ??
                   GczAsIso.Create(id) ??
                   DataStoreAsIso.Create(id, _context) ??
                   ChdAsIso.Create(id, _context.SourceFile.Name, canUseCustomChkSum) ??
                   DefaultAsIso.Create(id, _context.SourceFile.IndexFile?.FileType ?? IndexFileType.None);
        }

        public bool Open(Stream stream, bool canUseCustomChkSum, SystemType filterSystem, SystemType defaultSystem, out SystemType detectedSystem)
        {
            Position = 0;

            //needs tidy up and unit tests
            detectedSystem = SystemType.NotSet;

            try
            {
                byte[] id = new byte[0x400 * 0x400]; //1 MiB
                stream.Read(id, 0, -id.Length); //read and rewind

                _iso = createImageContainer(id, canUseCustomChkSum);

                int requestedBuffSize = _iso.Construct(stream, _iso is WiaAsIso);

                // Detail: which container decoder claimed this source (winning entry of the 15-deep
                // detection chain) + its reported format. Answers "what did NKit decide this file is".
                ILogScope inputScope = _context.Log?.ScopeFor(LogScopes.Input);
                if (inputScope != null && inputScope.IsEnabled(LogLevel.Detail))
                {
                    inputScope.Log(LogLevel.Detail,
                        $"Container [{_iso.GetType().Name}] format [{_iso.Format}] buffer [{requestedBuffSize}]");
                    // Per-format internals summary (header/table offsets, block sizes, version …).
                    string fmt = _iso.FormatSummary;
                    if (!string.IsNullOrEmpty(fmt))
                        inputScope.Log(LogLevel.Detail, fmt);
                }

                _isoStream = new BufferStream((Stream)_iso, _context.Log);

                // An NKitAsIso source already knows its own system from its header (set in
                // Construct), so we do NOT need to read decoded bytes to detect it. Reading here
                // would force NKitAsIso's full structure parse (including update-partition
                // reinsertion) before the Wii Image has had a chance to wire in the recovery
                // FixData. So for NKit sources take the known system and defer the first read to
                // the Image (which wires recovery data in its constructor, then reads the header).
                byte[] temp;
                if (_iso is NKitAsIso)
                    temp = new byte[0];
                else
                {
                    temp = new byte[Math.Min(_iso.Size, 0x20000)];
                    _isoStream.Read(temp, 0, -temp.Length); //cache and rewind
                }

                //if (requestedBuffSize > _cache.Size)
                //    _cache.SetNewSize(requestedBuffSize);

                //_iso.Read(-1, 0x20 - (int)_iso.Position, _cache); //first 4 bytes of plain iso format has already been read
                string fileext = _context.SourceFile.ImageFiles[0].Extension.ToLower(); //last resort

                if (_iso is NKitAsIso nkitSys && (nkitSys.IsWii || nkitSys.IsGameCube))
                    detectedSystem = nkitSys.IsWii ? SystemType.Wii : SystemType.GameCube;
                else if (_iso.Format == ContainerType.Ps3Jb)
                    detectedSystem = SystemType.PS3;
                else if (temp.Length >= 4 && temp.ReadUInt32B(0x1c) == 0xc2339f3d)
                    detectedSystem = SystemType.GameCube;
                else if (temp.Length >= 4 && temp.ReadUInt32B(0x18) == 0x5d1c9ea3)
                    detectedSystem = SystemType.Wii;
                else if (_iso.Format == ContainerType.TmdApp || _iso is TmdAppAsIso || (temp.Length >= Nintendo.WiiU.WiiUConsts.HeaderIdOffset + 4 && temp.ReadUInt32B(Nintendo.WiiU.WiiUConsts.HeaderIdOffset) == Nintendo.WiiU.WiiUConsts.HeaderId))
                    detectedSystem = SystemType.WiiU;
                else if (temp.Length >= Nintendo.WiiGc.WiiConsts.AppLoaderOffset + 0x0a && temp.ReadUInt32B(0x0) != 0 && temp.ReadUInt32B(0x20) != 0 && Regex.IsMatch(temp.ReadString(Nintendo.WiiGc.WiiConsts.AppLoaderOffset, 0x0a), @"[0-9]{4}(/[0-9]{2}){2}$")) //No wii or gc magic, but has a title - v1.1 BugFix for Dodger Demo_shrunk.gcm / ind-nddemo.iso
                    detectedSystem = SystemType.GameCube;

                if (detectedSystem == SystemType.NotSet && _context.SystemType != SystemType.NotSet)
                    detectedSystem = _context.SystemType;

                // Detail: the detected system.
                if (inputScope != null && inputScope.IsEnabled(LogLevel.Detail))
                    inputScope.Log(LogLevel.Detail,
                        $"System detected [{detectedSystem}]");

                if (filterSystem != SystemType.NotSet && detectedSystem != SystemType.NotSet && filterSystem != detectedSystem) //check what we have so far
                {
                    // Warning: a filtered-out image otherwise vanishes silently.
                    if (inputScope != null)
                        inputScope.Log(LogLevel.Warning,
                            $"Skipped [{_context.SourceFile.Name}] - system filter [{filterSystem}] != detected [{detectedSystem}]");
                    return false; //not to be processed
                }

                if (detectedSystem != SystemType.NotSet)
                    _context.SetSystemType(detectedSystem);

                // Fix task: wrap the source in an up-front Fix decorator so the Image reads an
                // already-corrected image on its first read (before the Image constructor parses
                // the header). Each Fix decorator owns its own wrap decision, FixData resolution
                // and target size via its Create() (same convention as the other AsIso containers),
                // returning null when it does not apply. PS3/XBox Fix decorators can slot into this
                // chain the same way later without changing NKitInput.
                IAsIso fixWrapped = Container.GcFixAsIso.Create(_iso, detectedSystem, _context)
                                 ?? Container.WiiFixAsIso.Create(_iso, detectedSystem, _context)
                                 ?? Container.Ps3FixAsIso.Create(_iso, detectedSystem, _context);
                if (fixWrapped != null)
                {
                    // The Fix decorator now WRAPS the previous _iso and owns reading it (WiiFixAsIso
                    // reads its inner through its own BufferStream). Do NOT dispose the pre-wrap
                    // detection _isoStream here: its manager wraps the SAME inner container, and
                    // BufferStream disposal cascades to dispose the underlying source — which would
                    // tear the inner (e.g. a decoded NKitAsIso) out from under the decorator. The
                    // inner's lifetime is owned by the decorator + the final Close()/Dispose path.
                    _iso = fixWrapped;
                    _isoStream = new BufferStream((Stream)_iso, _context.Log);
                    if (inputScope != null && inputScope.IsEnabled(LogLevel.Detail))
                        inputScope.Log(LogLevel.Detail,
                            $"Fix decorator [{_iso.GetType().Name}] wrapping source");
                }

                if (detectedSystem == SystemType.GameCube)
                    Image = new Nintendo.WiiGc.Image(_context, _iso, _isoStream, true); //gamecube
                else if (detectedSystem == SystemType.Wii)
                    Image = new Nintendo.WiiGc.Image(_context, _iso, _isoStream, false);
                else if (detectedSystem == SystemType.WiiU)
                    Image = new Nintendo.WiiU.Image(_context, _iso, _isoStream);
                else if (_iso.Format == ContainerType.Ps3Jb)
                    // PS3-JB (SFB) is always the Fix task, wrapped by the Ps3FixAsIso decorator: it
                    // rebuilds the full ISO from the IRD on demand, so the standard Iso9660.Image
                    // consumes it like any PS3 disc (up-front FS resolve, gap discovery, scan,
                    // sections). The decorator serves DECRYPTED bytes; the SectionProcessor encrypts
                    // the encrypted regions in the parallel stage (like Wii), which is faster than
                    // the old serial ImagePs3IrdFix full-IImage that this replaced.
                    Image = new Iso.Iso9660.Image(_context, _iso, _isoStream, SystemType.PS3);
                else if ((detectedSystem = Microsoft.XBox.Image.IsXBox(_context, _iso, _isoStream, defaultSystem)) != SystemType.NotSet)
                {
                    Image = new Microsoft.XBox.Image(_context, _iso, _isoStream, defaultSystem);
                    //detectedSystem = Image.SystemType;
                }

                else
                {
                    Image = new Iso.Iso9660.Image(_context, _iso, _isoStream, defaultSystem);
                    detectedSystem = Image.SystemType;
                    if (filterSystem != SystemType.NotSet && filterSystem != detectedSystem) //check iso9660, once we've had a look
                        return false; //not to be processed
                    else if (fileext == "." + NKitTask.ScanExt && !(_iso is DataStoreAsIso))
                        return false; //.nkit files must use this container
                }

                if (detectedSystem != SystemType.NotSet)
                    _context.SetSystemType(detectedSystem);

                // Detail: which IImage reader was constructed for this source.
                if (inputScope != null && Image != null && inputScope.IsEnabled(LogLevel.Detail))
                    inputScope.Log(LogLevel.Detail,
                        $"Reader [{Image.GetType().FullName}] system [{detectedSystem}]");

                return true;
            }
            catch (HandledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new HandledException(ex, "NKitInput.Open");
            }
        }

        public void Setup() => Image.Setup();

        public int Read(IBuffer buffer, out IFileSystemInfo fsInfo)
        {
            Image.Read(buffer, out fsInfo);
            Position += buffer.Size;
            return buffer.Size;
        }

        public void Close()
        {
            // Idempotent: Close() is called explicitly on the success path (so _iso.Complete()
            // finalises before results are read) and again via Dispose() in the caller's finally
            // (so the exception path still releases the decoder + Layer-B manager deterministically).
            if (_closed)
                return;
            _closed = true;

            try
            {
                _iso?.Complete();
            }
            catch
            {
            }
            // Disposing the Layer-B view detaches its manager, which disposes the decoder (_iso)
            // when the last view detaches. The Layer-A raw source view is owned/closed separately
            // by the caller (NKitProcessor). Fall back to disposing _iso directly if there is no
            // Layer-B view (e.g. an early failure before it was created).
            if (_isoStream != null)
            {
                try { _isoStream.Dispose(); } catch { }
            }
            else
            {
                try { _iso?.Dispose(); } catch { }
            }
        }

        public void Dispose() => Close();
    }
}