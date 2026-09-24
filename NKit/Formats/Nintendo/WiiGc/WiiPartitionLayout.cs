using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Nanook.NKit.Nintendo.WiiGc
{
    /// <summary>
    /// The proven Wii partition-table layout fixes, factored out of WiiGc.Image so both the Image
    /// (legacy in-pipeline path) and WiiFixAsIso (up-front layout decorator) share the exact same
    /// algorithm — no duplication, no divergence.
    ///
    /// These operate only on an ImageHeader + FixData (partition-table entries and FixPartition
    /// recovery references); they never read source disc bytes. The caller supplies the source
    /// image size (for the truncation check) and, for the data-partition offset walk, the Game
    /// partition offset/size.
    /// </summary>
    internal static class WiiPartitionLayout
    {
        /// <summary>
        /// Move the Game partition to 0xF800000 and add any missing channel/VC partitions from the
        /// fix data (matched by disc Id8). Mirrors WiiGc.Image.applyWiiPartitionTableFixes.
        /// origHeaderBytes is the source header (used only to remove channels from a truncated
        /// image's original table, matching the legacy behaviour).
        /// </summary>
        public static void ApplyPartitionTableFixes(ImageHeader header, FixData fixData, long imgSize, byte[] origHeaderBytes, ILogScope log)
        {
            List<FixPartition> channels = fixData?.WiiChannels?.Where(a => a.Id == header.Id8).ToList() ?? new List<FixPartition>();

            //assumes that no partitions have the same type (vc is always different types). Channels are before data, vc is after data in table 1
            int chanCount = header.Partitions.Count(a => a.Type != PartitionType.Update && a.Type != PartitionType.Game);
            bool truncated = header.Partitions.Any(a => a.ImageOffset >= imgSize); //partitions after file ends

            int reqChannels = fixData?.ChannelCount ?? 0;

            PartitionInfo game = header.Partitions.FirstOrDefault(a => a.Type == PartitionType.Game);
            if (game != null && game.ImageOffset < WiiConsts.WiiDefaultDataPtnOffset)
            {
                log?.Info(() => $"Game Partition moved from {game.ImageOffset:X9} moved to {WiiConsts.WiiDefaultDataPtnOffset:X9}");
                game.ImageOffset = WiiConsts.WiiDefaultDataPtnOffset;
            }

            if (reqChannels > channels.Count)
            {
                log?.Info(() => $"Required partitions mismatch - {reqChannels} required, {channels.Count} found - Add all '{header.Id8}_*' to the fixFiles folder");

                //missing channels
                if (truncated) //remove any channels after the end of the iso
                {
                    log?.Info(() => $"Truncated image, removed channels/VC partition entries");
                    header.RemovePartitionChannels();
                    if (origHeaderBytes != null)
                    {
                        ImageHeader orig = new ImageHeader(origHeaderBytes, true);
                        orig.RemovePartitionChannels();
                    }
                }
            }
            else if (chanCount == 0 || truncated)
            {
                if (truncated)
                {
                    log?.Info(() => $"Truncated Image, removed channels/VC partitions");
                    header.RemovePartitionChannels();
                }
                bool after = header.Partitions.FirstOrDefault(a => a.Type == PartitionType.Game)?.ImageOffset == WiiConsts.WiiDefaultDataPtnOffset;

                if (channels.Count == 1)
                {
                    log?.Info(() => $"Added channel/VC partition - '{channels[0].DisplayName}_*'");
                    header.AddPartitionPlaceHolder(new PartitionInfo(PartitionType.Channel, after ? 0 : WiiConsts.WiiDefaultDataPtnOffset, 0, 0) { IsPlaceholder = true, FixPartition = channels[0] }); //ensure update is first
                }
                else if (channels.Count > 1)
                {
                    foreach (FixPartition part in channels)
                    {
                        PartitionType type = (PartitionType)Encoding.ASCII.GetBytes(part.SubId).ReadUInt32B(0);
                        if (!header.Partitions.Any(a => a.Type == type))
                            header.AddPartitionPlaceHolder(new PartitionInfo(type, 0, 1, 0) { IsPlaceholder = true, FixPartition = part }); //0 offset until we get the data WipePartition
                    }
                    log?.Info(() => $"Added {channels.Count} channel/VC partitions - '{header.Id8}_*'");
                }
            }
        }

        /// <summary>
        /// The disc-ID swap quirk only: 010E disc ID with a RELS game partition -> swap the leading
        /// disc-ID byte to '4'. No partition-table rewrite. Shared so WiiFixAsIso can apply just
        /// this without the RSB table move (which it achieves by re-adding VC in table 1).
        /// gamePartitionId is the resolved Game partition content ID (may be null); used only for
        /// the 010E->4 RELS check.
        /// </summary>
        public static bool ApplyIdSwap(ImageHeader header, string gamePartitionId, ILogScope log)
        {
            if (header.Data.ReadString(0, 4) == "010E" && gamePartitionId != null && gamePartitionId.StartsWith("RELS"))
            {
                log?.Info(() => $"Disc ID swapped from {header.Id} to 4{header.Id.Substring(1)}");
                header.Data[0] = (byte)'4';
                return true;
            }
            return false;
        }

        /// <summary>
        /// Disc-ID / partition-table quirk fixes. Mirrors FixWiiGcStep.applyIdBasedFixes:
        ///  - 010E disc ID with a RELS game partition -> swap the leading disc-ID byte to '4'.
        ///  - RSB (Super Smash Bros. Brawl): channel/VC partitions wrongly in table 0 (WBM bug)
        ///    are moved to table 1, then the table is rewritten.
        /// </summary>
        public static bool ApplyIdBasedFixes(ImageHeader header, string gamePartitionId, ILogScope log)
        {
            string hdrId = header.Data.ReadString(0, 4);
            bool changed = ApplyIdSwap(header, gamePartitionId, log);

            if (hdrId.StartsWith("RSB")) //Super Smash Bros. Brawl
            {
                bool moved = false;
                foreach (PartitionInfo part in header.Partitions.Where(a => a.Type != PartitionType.Update && a.Type != PartitionType.Game))
                {
                    if (part.Table == 0)
                    {
                        part.Table = 1; //WBM swaps this for some reason
                        log?.Info(() => $"Partition {hdrId} moved from table 0 to 1 (WBM bug)");
                        moved = true;
                    }
                }
                if (moved)
                {
                    header.UpdateRepair();
                    changed = true;
                }
            }

            return changed;
        }

        /// <summary>
        /// Align/place partitions to their final offsets: channels before the Game partition, the
        /// Game partition at dataPtnOffset, VC after it. Placeholders (added channels/VC) are placed
        /// at the running offset and advance it by their FixPartition length. Mirrors
        /// WiiGc.Image.applyDataPartitionFixes. Returns true if any offset changed.
        /// </summary>
        public static bool ApplyDataPartitionFixes(ImageHeader header, long dataPtnOffset, long dataPtnSize, ILogScope log)
        {
            bool changed = false;

            long offset = 0;
            foreach (PartitionInfo part in header.Partitions)
            {
                if (part.ImageOffset != 0)
                    offset = part.ImageOffset;
                if (offset % WiiConsts.WiiPtnAlign != 0) //align
                    offset += WiiConsts.WiiPtnAlign - (offset % WiiConsts.WiiPtnAlign);

                if (part.IsPlaceholder && part.ImageOffset == 0) //added when we didn't have any data WipePartition length info
                {
                    part.ImageOffset = offset;
                    offset += part.FixPartition.Length;
                    changed = true;
                }
                else if (offset != part.ImageOffset)
                {
                    long from = part.ImageOffset;
                    long to = offset;
                    log?.Info(() => $"Partition at offset {from:X9} moved to {to:X9}");
                    offset = part.ImageOffset; //match legacy: existing partitions keep their offset here (usually one vc missing)
                    changed = true;
                }
                if (part.ImageOffset == dataPtnOffset)
                    offset = dataPtnOffset + dataPtnSize;
            }
            if (changed)
                header.UpdateRepair();

            return changed;
        }
    }
}