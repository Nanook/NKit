using Nanook.NKit.Steps.Shared;
using System.Text.RegularExpressions;

namespace Nanook.NKit
{
    /// <summary>
    /// Pure, immutable normalisation of "what am I actually processing?" — built ONCE, before the
    /// <c>_StepsDefs</c> lookup in <see cref="NKitTaskContext"/>. It absorbs the source-fact logic
    /// that used to be smeared across <c>CreateSteps</c> (the config-string selection and the
    /// dual-format split) and the whole of the former <c>CalculateConfig</c> per-system switch.
    ///
    /// The routing table still does the routing; this type just gives it clean inputs so no value is
    /// substituted after the lookup. See NKitVault "TaskContext Firewall Simplification" for the
    /// rule-by-rule mapping of the ten former workarounds.
    /// </summary>
    internal sealed class SourceProfile
    {
        /// <summary>"image" or "folderindex" — the <c>SrcType</c> discriminator for the table.</summary>
        public string SrcType { get; }

        /// <summary>The normalised <c>Config</c> token used as the table discriminator (was the result of CalculateConfig).</summary>
        public string Config { get; }

        /// <summary>The (possibly dual-format-split) config string passed through to a step's stepConfig.</summary>
        public string TaskConfig { get; }

        /// <summary>True when this is a Dreamcast GD-ROM source.</summary>
        public bool IsGdRom { get; }

        /// <summary>True when the source is a DataStore (.nkds) entry. Drives the verify-method swap (Stage 2: becomes a table column).</summary>
        public bool IsDataStore { get; }

        /// <summary>True when the source presents a folder-index (CUE/GDI/CHD-CD) layout.</summary>
        public bool IsFolderIndex { get; }

        private SourceProfile(string srcType, string config, string taskConfig, bool isGdRom, bool isDataStore, bool isFolderIndex)
        {
            this.SrcType = srcType;
            this.Config = config;
            this.TaskConfig = taskConfig;
            this.IsGdRom = isGdRom;
            this.IsDataStore = isDataStore;
            this.IsFolderIndex = isFolderIndex;
        }

        /// <summary>
        /// Normalise a source into the discriminators the step table needs. Pure: depends only on its
        /// arguments (no mutation, no ambient state) so it is directly unit-testable.
        /// </summary>
        public static SourceProfile From(TaskType task, SystemType system, SourceFile sourceFile, IImageInfo imageInfo, SystemSettings settings)
        {
            bool isGdRom = system == SystemType.Dreamcast && GdRomWriter.IsGdrom(system, imageInfo, sourceFile);

            // A source is "folder index" (multi-track / CUE / GDI layout) when it has an external index
            // file OR when the reader derived a folder-index layout from the image itself. The latter is
            // the ONLY signal for a CHD: a CHD deliberately has no external IndexFile (its track layout
            // lives in ChdMetaData -> ImageInfo.IsFolderIndex), so a CD-style CHD is folder-index with
            // IndexFile == null. Both the SrcType and the dual-format selection MUST use this combined
            // signal — keying either on IndexFile alone misroutes a CHD.
            bool isFolderIndex = sourceFile.IndexFile != null || (imageInfo?.IsFolderIndex ?? false);
            bool isDataStore = sourceFile?.IsDataStore == true;

            // #1 base config-string selection (was a nested ternary in CreateSteps).
            // Wipe outputs the SAME formats as Expand (it is expand-plus-content/key-wiping), so it
            // MUST resolve its config-string and output container identically to Expand — never from
            // settings.Convert. Both mirror the source container.
            bool expandLike = task == TaskType.Expand || task == TaskType.Wipe;
            string configString =
                task == TaskType.Extract ? settings.Extract :
                ((task == TaskType.Convert || (expandLike && isDataStore)) ? settings.Convert :
                (expandLike && imageInfo?.ContainerType == ContainerType.TmdApp ? "" :
                (isGdRom ? "cue" :
                "")));

            // #2 A dual format ("imageFmt/folderIndexFmt") carries a shape-dependent choice. We already
            // know the source shape (isFolderIndex, detected above), so target the correct half up front
            // — the table then only ever sees a single concrete format, never a "/".
            configString = DualFormat.Parse(configString).For(isFolderIndex);

            string srcType = (isFolderIndex ? OutputType.FolderIndex : OutputType.Image).ToString().ToLower();
            string config = CalculateConfig(task, system, configString, isGdRom, isFolderIndex, sourceFile);

            return new SourceProfile(srcType, config, configString, isGdRom, isDataStore, isFolderIndex);
        }

        // The former NKitTaskContext.CalculateConfig — moved here verbatim (behaviour identical), now
        // pure over its arguments. Absorbs workarounds #6-#10. Exposed so the test harness (which
        // supplies its own pre-selected configString + folder-index fact) exercises the same logic.
        public static string CalculateConfig(TaskType task, SystemType system, string configString, bool isGdRom, bool isFolderIndex, SourceFile sourceFile)
        {
            string config = "";
            string[] fmt = new[] { "" };
            switch (task)
            {
                case TaskType.Convert:
                    fmt = (configString ?? "").ToLower().Split(':');
                    bool isLossy = (system == SystemType.WiiU && fmt[0] == "app")
                                || ((system == SystemType.Wii || system == SystemType.GameCube) && fmt.Length > 1 && (fmt[0] == "wbfs" || fmt[0] == "ciso") && fmt[1] == "n");
                    switch (system)
                    {
                        case SystemType.Wii:
                        case SystemType.GameCube:
                            config = (fmt[0] == "iso" || isLossy) ? fmt[0] : $"{fmt[0]}[nkit]"; // #6
                            break;
                        case SystemType.WiiU:
                            config = fmt[0] == "app" ? "apptmd" : fmt[0];
                            break;
                        case SystemType.Dreamcast:
                            config = isGdRom ? "GdRom" + fmt[0] : fmt[0];
                            break;
                        default:
                            config = fmt[0];
                            break;
                    }
                    break;
                case TaskType.Wipe:
                    // Wipe mirrors Expand's OUTPUT FORMAT (via the expand-like configString selection in
                    // From()), but its routing-table Config DISCRIMINATOR is the "none" sentinel (meaning
                    // "no format config") exactly as on main — NOT the Expand format value. The only
                    // special cases are Dreamcast GD-ROM CHD and XBox/360 archive sources.
                    config = "none";
                    if (system == SystemType.Dreamcast && isGdRom && sourceFile?.ImageType == SourceImageType.Chd) // #8
                        config = "ChdGdRomcue";
                    // The "arc\t" prefix is for genuine compressed archives (zip/rar/7z/gzip) whose XBox
                    // image must be extracted before wiping. A DataStore (.nkds) is not a compressed
                    // archive (it is a random-access block store), so it must NOT get the arc prefix.
                    else if ((system == SystemType.XBox || system == SystemType.XBox360)
                        && (sourceFile?.ArchiveFiles?.Length ?? 0) != 0 && !(sourceFile?.IsDataStore ?? false))
                        config = $"arc\t{config}";
                    break;
                case TaskType.Expand:
                    fmt = (configString ?? "").ToLower().Split(':');
                    if (sourceFile?.ImageType == SourceImageType.TmdApp) // #7
                    {
                        config = "apptmd";
                        break;
                    }
                    switch (system)
                    {
                        case SystemType.Dreamcast:
                            if (isGdRom) // #8
                                config = sourceFile?.ImageType == SourceImageType.Chd ? "ChdGdRomcue" : "GdRom" + fmt[0];
                            break;
                        case SystemType.WiiU:
                            config = (fmt[0] == "app" || fmt[0] == "tmd") ? "apptmd" : configString;
                            break;
                        case SystemType.XBox:
                        case SystemType.XBox360:
                            config = fmt[0];
                            break;
                        default: // #10 — folderindex sources only pass cue/toc through
                            if (isFolderIndex)
                                config = (fmt[0] == "cue" || fmt[0] == "toc") ? fmt[0] : "";
                            else
                                config = fmt[0];
                            break;
                    }
                    break;
                case TaskType.Extract:
                    Match m = Regex.Match(configString ?? "ri:.*", "^([a-z]*):(.*)$");
                    if (m.Success)
                    {
                        if (m.Groups[1].Value.Contains('f'))
                            config = "Forensic";
                    }
                    else if (config == "f")
                        config = "Forensic";
                    break;
                case TaskType.Fix:
                    if (system == SystemType.PS3 && sourceFile?.ImageFiles?[0].Extension.ToLower() == ".sfb") // #9
                        config = "sfb";
                    break;
                case TaskType.Scan:
                    config = "scan";
                    if (system == SystemType.Dreamcast && isGdRom && sourceFile?.ImageType == SourceImageType.Chd) // #8
                        config = "ChdGdRomcue";
                    break;
                case TaskType.Verify:
                    config = "none";
                    if (system == SystemType.Dreamcast && isGdRom && sourceFile?.ImageType == SourceImageType.Chd) // #8
                        config = "ChdGdRomnone";
                    break;
            }
            return config;
        }
    }

    /// <summary>
    /// A convert/expand format string that may carry a shape-dependent choice as
    /// "imageFormat/folderIndexFormat" (e.g. "iso/cue"). A single format (no '/') means the same
    /// output for both shapes. Because the source shape is known before the table lookup, we resolve
    /// the correct half up front so the routing table only ever sees one concrete format.
    /// </summary>
    internal readonly struct DualFormat
    {
        public string Image { get; }
        public string FolderIndex { get; }

        private DualFormat(string image, string folderIndex)
        {
            this.Image = image;
            this.FolderIndex = folderIndex;
        }

        public static DualFormat Parse(string configString)
        {
            string s = configString ?? "";
            if (!s.Contains('/'))
                return new DualFormat(s, s);
            // Match the historical Split('/')[0|1] semantics exactly (image = part[0], folderIndex = part[1]).
            string[] parts = s.Split('/');
            return new DualFormat(parts[0], parts.Length > 1 ? parts[1] : parts[0]);
        }

        /// <summary>Return the format for the detected source shape.</summary>
        public string For(bool isFolderIndex) => isFolderIndex ? this.FolderIndex : this.Image;
    }
}
