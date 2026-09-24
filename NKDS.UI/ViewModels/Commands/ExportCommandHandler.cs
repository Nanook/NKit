using NKDS;
using NKDS.Models;
using NkdsUi.Models;
using NkdsUi.Services;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Signals;

namespace NkdsUi.ViewModels.Commands;

/// <summary>
/// Handles the Export toolbar operation.
/// Lifecycle: resolve selected images → show ExportDialog → create progress →
/// exclusive access per set group → NkdsOperations.ExportAsync → reconcile.
/// </summary>
public class ExportCommandHandler : ICommandHandler
{
    public IObservable<RxVoid> Execute(OperationContext ctx)
    {
        return Signal.FromAsync<RxVoid>(async () =>
        {
            // 1. Resolve selected images
            List<ImageRowViewModel> selectedRows = ctx.ImageList.SelectedImages.ToList();
            if (selectedRows.Count == 0) return RxVoid.Default;

            // 2. Show ExportDialog
            ExportDialogViewModel dialogVm = new ExportDialogViewModel(ctx.ConfigService) { SelectedImageCount = selectedRows.Count };
            dialogVm.InitializeFromImages(selectedRows);

            bool confirmed = await ctx.ShowExportDialog.Handle(dialogVm);
            if (!confirmed || string.IsNullOrWhiteSpace(dialogVm.OutputDirectory)) return RxVoid.Default;

            Dictionary<long, string> formatMapping = dialogVm.GetFormatMapping(selectedRows);

            // 2b. Validate Dreamcast fix info for CUE↔GDI cross-format conversion
            if (RequiresDreamcastFixInfo(selectedRows, formatMapping))
            {
                string fixInfoPath = ctx.ConfigService.KeysAndFixPaths.DreamcastFixInfoPath;
                string? resolvedPath = string.IsNullOrWhiteSpace(fixInfoPath)
                    ? null
                    : PathResolver.ToAbsolute(fixInfoPath);

                if (string.IsNullOrWhiteSpace(resolvedPath) || !File.Exists(resolvedPath))
                {
                    ctx.ErrorNotification.PublishOperationError(
                        "Dreamcast fix info file is required for CUE↔GDI conversion but is not configured or does not exist. " +
                        "Please configure the Dreamcast fix info path in Settings → Keys & Fix Files.");
                    return RxVoid.Default;
                }
            }

            // Resolve key/fix paths from config for the NKit pipeline
            ResolvedKeyFixPaths keyFixPaths = KeyFixPathsResolver.Resolve(ctx.ConfigService);

            // Group by set for multi-set support
            List<IGrouping<string, ImageRowViewModel>> setGroups = selectedRows.GroupBy(r => r.Image.SetName).ToList();

            // 3. Create progress tracking
            OperationProgressViewModel progressVm = new OperationProgressViewModel { OperationName = "Export" };
            ctx.ActiveOperations.Add(progressVm);
            ctx.SharedState.OperationInProgressOnSet = setGroups.First().Key;

            int totalImages = selectedRows.Count;
            int cumulativeProcessed = 0;
            progressVm.TotalItems = totalImages;

            try
            {
                // 4. Exclusive access → export → reconcile (per set group)
                ctx.ImageList.SuppressFilters();
                foreach (IGrouping<string, ImageRowViewModel> setGroup in setGroups)
                {
                    string setName = setGroup.Key;
                    IReadOnlyList<ImageSessionModel> currentSessions = await ctx.DataStoreService.Sessions.FirstAsync();
                    ImageSessionModel? session = SessionResolver.FindSessionForSet(currentSessions, setName);
                    if (session == null) continue;
                    string dataStorePath = SessionResolver.ResolveDataStorePath(session).dataStorePath;

                    await ctx.ExclusiveAccessManager.WithExclusiveAccessAsync(
                        dataStorePath,
                        async path =>
                        {
                            using NkdsOperations ops = new NkdsOperations();
                            // Group images by resolved format and export each group
                            foreach (IGrouping<string, ImageRowViewModel> formatGroup in setGroup.GroupBy(r => formatMapping.GetValueOrDefault(r.Id, "iso")))
                            {
                                string formatString = formatGroup.Key;
                                List<long> imageIds = formatGroup.Select(r => r.Image.Id).Distinct().ToList();
                                int formatGroupOffset = cumulativeProcessed;

                                Progress<OperationProgress> groupProgress = new Progress<OperationProgress>(p =>
                                {
                                    progressVm.ItemsProcessed = formatGroupOffset + p.ItemsProcessed;
                                    progressVm.TotalItems = totalImages;
                                    progressVm.Elapsed = p.Elapsed;
                                });

                                OperationResult result = await ops.ExportAsync(path, setName, imageIds,
                                    dialogVm.OutputDirectory, formatString,
                                    groupProgress, progressVm.CancellationToken,
                                    keyFixPaths,
                                    onImageProgress: evt =>
                                    {
                                        string imageName = ctx.ImageList.FindRow(evt.SetName, evt.ImageId)?.Name ?? $"Image {evt.ImageId}";
                                        progressVm.CurrentItem = $"{imageName} \u2014 {evt.StepName}";
                                        // Show per-image progress (0-100%) rather than batch progress
                                        progressVm.Percentage = (int)(evt.OverallProgress * 100);
                                    });

                                if (!result.Success && result.Errors.Count > 0)
                                {
                                    foreach (OperationErrorEntry? error in result.Errors)
                                    {
                                        ctx.ErrorNotification.PublishOperationError(
                                            $"Export failed for '{error.ItemName}' (format: {formatString}): {error.Reason}");
                                    }
                                }

                                cumulativeProcessed += imageIds.Count;
                            }
                        });
                }
                ctx.ImageList.ResumeFilters();
            }
            catch (OperationCanceledException)
            {
                ctx.ImageList.ResumeFilters(applyNow: false);
                ctx.ErrorNotification.PublishCancellation("Export");
            }
            catch (Exception ex)
            {
                ctx.ImageList.ResumeFilters(applyNow: false);
                ctx.ErrorNotification.PublishOperationError($"Export failed: {ex.Message}");
            }
            finally
            {
                await progressVm.WaitForMinimumDisplayTimeAsync();
                ctx.ActiveOperations.Remove(progressVm);
                ctx.SharedState.OperationInProgressOnSet = null;
                progressVm.Dispose();
            }

            return RxVoid.Default;
        });
    }

    /// <summary>
    /// Determines whether any selected image needs the Dreamcast fix yaml (audio-offset) info for
    /// export. True when a Dreamcast image is converted CUE↔GDI, or when the source is a CHD
    /// (GD-ROM stored verbatim) being exported to cue or gdi — both drive the GdRomWriter path
    /// that consults the fix yaml for audio alignment.
    /// </summary>
    private static bool RequiresDreamcastFixInfo(
        IReadOnlyList<ImageRowViewModel> selectedRows,
        Dictionary<long, string> formatMapping)
    {
        foreach (ImageRowViewModel row in selectedRows)
        {
            if (!string.Equals(row.System, "dreamcast", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!formatMapping.TryGetValue(row.Id, out string? targetFormat))
                continue;

            string sourceFormat = row.Format.ToString().ToLowerInvariant();
            string targetBase = targetFormat.Split(':')[0].ToLowerInvariant();

            // Cross-format: CUE→GDI or GDI→CUE
            if ((sourceFormat == "cue" && targetBase == "gdi") ||
                (sourceFormat == "gdi" && targetBase == "cue"))
            {
                return true;
            }

            // A CHD source (GD-ROM stored verbatim) synthesises cue OR gdi on export via the
            // GdRomWriter, which uses the Dreamcast fix yaml for audio alignment — so make the
            // fix info available for either target, exactly as for a cross-format conversion.
            if (sourceFormat == "chd" && (targetBase == "cue" || targetBase == "gdi"))
                return true;
        }
        return false;
    }
}