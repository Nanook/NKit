using NKDS.Models;
using NKDS.Mount;
using NkdsUi.Models;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Signals;
using System.Collections.Concurrent;

namespace NkdsUi.ViewModels.Commands;

/// <summary>
/// Handles the Mount/Unmount toolbar operation.
/// Lifecycle: resolve session → show MountDialog → MountOrchestrator.MountAsync → update tracking/toolbar.
/// Toggle behavior: if the selected set is already mounted, unmounts it instead.
/// </summary>
public class MountCommandHandler : ICommandHandler
{
    private readonly ConcurrentDictionary<string, MountEntry> _activeMounts = new(StringComparer.Ordinal);
    private readonly IMountService _mountService = new MountService();

    public IObservable<RxVoid> Execute(OperationContext ctx)
    {
        return Signal.FromAsync<RxVoid>(async () =>
        {
            string? selectedSet = ctx.Toolbar.SelectedSetName;
            if (selectedSet == null) return RxVoid.Default;

            // Reconcile the local UI mirror against the orchestrator's authoritative state. A mount
            // can be torn down without going through this handler (e.g. SessionManager.Close unmounts
            // everything before closing a set). Dropping mirror entries the orchestrator no longer
            // knows about prevents the toggle below from trying to "unmount" a mount that is gone.
            reconcileActiveMounts(ctx);

            // If the selected set still has an active mount, unmount it (toggle behavior)
            if (_activeMounts.ContainsKey(selectedSet))
            {
                await performUnmountAsync(selectedSet, ctx);
                return RxVoid.Default;
            }

            // Otherwise, show the mount dialog
            (string dataStorePath, string originalPath)? resolved = await resolveActiveSessionAsync(ctx);
            if (resolved == null) return RxVoid.Default;

            MountDialogViewModel dialogVm = new MountDialogViewModel(_mountService, ctx.ConfigService,
                _activeMounts.Values.Select(m => m.MountPoint).ToList().AsReadOnly())
            { SetName = selectedSet };
            bool confirmed = await ctx.ShowMountDialog.Handle(dialogVm);
            if (!confirmed || string.IsNullOrWhiteSpace(dialogVm.MountPoint)) return RxVoid.Default;

            MountOptions mountOptions = new MountOptions
            {
                ShowImage = dialogVm.ShowImage,
                ShowFileSystem = dialogVm.ShowFileSystem,
                ShowSystem = dialogVm.ShowSystem,
                UpdateMode = dialogVm.UpdateMode
            };

            await performMountAsync(
                resolved.Value.dataStorePath,
                resolved.Value.originalPath,
                selectedSet,
                dialogVm.MountPoint,
                mountOptions,
                dialogVm.AllowOther,
                dialogVm.ParsedUid,
                dialogVm.ParsedGid,
                ctx);

            // Persist Linux mount options for next time
            ctx.ConfigService.SetMountLinuxOptions(dialogVm.UidText, dialogVm.GidText, dialogVm.AllowOther);

            return RxVoid.Default;
        });
    }

    /// <summary>
    /// Performs the mount operation using <see cref="MountOrchestrator"/>: constructs a
    /// <see cref="MountRequest"/>, delegates to the orchestrator, and updates tracking/toolbar state.
    /// </summary>
    private async Task performMountAsync(
        string dataStorePath, string originalPath, string setName, string mountPoint,
        MountOptions options, bool allowOther, uint? uid, uint? gid,
        OperationContext ctx)
    {
        // 1. Collect all open session paths
        IReadOnlyList<ImageSessionModel> sessions = await ctx.DataStoreService.Sessions.FirstAsync();
        List<string> allSessionPaths = sessions.Select(s => s.Path).ToList();

        // 2. Set OperationInProgressOnSet to prevent mutating operations while mounted
        ctx.SharedState.OperationInProgressOnSet = setName;

        try
        {
            string[] mountPaths = allSessionPaths
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            // 3. Construct MountRequest for the orchestrator
            MountRequest request = new MountRequest
            {
                DataStorePaths = mountPaths,
                MountPoint = mountPoint,
                SetName = setName,
                Options = options,
                AllowOther = allowOther,
                Uid = uid,
                Gid = gid
            };

            // 4. Create MountEntry for UI tracking
            MountEntry entry = new MountEntry
            {
                SetName = setName,
                MountPoint = mountPoint,
                DataStorePath = dataStorePath,
                OriginalPath = originalPath,
                AllSessionPaths = allSessionPaths
            };

            // 5. Delegate to MountOrchestrator
            OperationResult result = await ctx.MountOrchestrator.MountAsync(request);

            if (!result.Success)
            {
                string errorMessage = result.Errors?.FirstOrDefault()?.Reason ?? "Mount failed.";
                System.Diagnostics.Debug.WriteLine($"[Mount] Startup failure: {errorMessage}");
                ctx.SharedState.OperationInProgressOnSet = null;
                updateMountToolbarState(ctx);
                return;
            }

            // 6. Add MountEntry to _activeMounts
            _activeMounts[setName] = entry;

            // 7. Persist mount path to MRU history
            ctx.ConfigService.AddMountPath(mountPoint);

            // 8. Update toolbar state
            updateMountToolbarState(ctx);
        }
        catch (OperationCanceledException)
        {
            ctx.ErrorNotification.PublishCancellation("Mount");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Mount Error] Mount failed: {ex.Message}");
            ctx.ErrorNotification.PublishOperationError($"Mount failed: {ex.Message}");
        }
        finally
        {
            // Only clear OperationInProgressOnSet if mount didn't succeed
            // (successful mounts keep it set to prevent mutating operations)
            if (!_activeMounts.ContainsKey(setName))
            {
                ctx.SharedState.OperationInProgressOnSet = null;
            }
        }
    }

    /// <summary>
    /// Unmounts the active mount for the specified set via <see cref="MountOrchestrator"/>
    /// and restores toolbar state.
    /// </summary>
    private Task performUnmountAsync(string setName, OperationContext ctx)
    {
        // 1. Retrieve MountEntry from _activeMounts
        if (!_activeMounts.TryRemove(setName, out MountEntry? entry))
            return Task.CompletedTask;

        // 2. Delegate unmount to MountOrchestrator
        OperationResult result = ctx.MountOrchestrator.Unmount(entry.MountPoint);
        if (!result.Success)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Mount Warning] Unmount of '{setName}': {result.Errors?.FirstOrDefault()?.Reason ?? "Unknown error"}");
        }

        // 3. Clear OperationInProgressOnSet
        ctx.SharedState.OperationInProgressOnSet = null;

        // 4. Update toolbar state (IsChecked=false, reset tooltip)
        updateMountToolbarState(ctx);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Updates the Mount toolbar item's visual state (IsChecked, Label, Tooltip) based on whether the
    /// currently selected set has an active mount entry.
    /// </summary>
    private void updateMountToolbarState(OperationContext ctx)
    {
        string? selectedSet = ctx.Toolbar.SelectedSetName;
        ToolbarItemViewModel? mountItem = ctx.Toolbar.Items.FirstOrDefault(i => i.Kind == ToolbarOperationKind.Mount);
        if (mountItem == null) return;

        if (selectedSet != null && _activeMounts.TryGetValue(selectedSet, out MountEntry? entry))
        {
            ctx.SharedState.IsMountActive = true;
            mountItem.IsChecked = true;
            mountItem.Label = "Unmount";
            mountItem.Tooltip = $"Mounted at {entry.MountPoint}";
        }
        else
        {
            ctx.SharedState.IsMountActive = false;
            mountItem.IsChecked = false;
            mountItem.Label = "Mount";
            mountItem.Tooltip = "Mount - Mount the listed images as a virtual filesystem";
        }
    }

    /// <summary>
    /// Drops any local <see cref="_activeMounts"/> entries whose mount point is no longer active in
    /// the orchestrator. Keeps this handler's UI mirror in sync when a mount is torn down elsewhere
    /// (notably SessionManager.Close, which unmounts all mounts before closing a set).
    /// </summary>
    private void reconcileActiveMounts(OperationContext ctx)
    {
        HashSet<string> live = new HashSet<string>(
            ctx.MountOrchestrator.GetActiveMounts().Select(m => m.MountPoint),
            StringComparer.Ordinal);

        foreach (KeyValuePair<string, MountEntry> kvp in _activeMounts.ToArray())
        {
            if (!live.Contains(kvp.Value.MountPoint))
                _activeMounts.TryRemove(kvp.Key, out _);
        }
    }

    /// <summary>
    /// Resolves the active session's DataStore path and original open path.
    /// Returns null if no sessions are open.
    /// </summary>
    private static async Task<(string dataStorePath, string originalPath)?> resolveActiveSessionAsync(OperationContext ctx)
    {
        IReadOnlyList<ImageSessionModel> sessions = await ctx.DataStoreService.Sessions.FirstAsync();
        if (sessions.Count == 0)
            return null;

        return SessionResolver.ResolveDataStorePath(sessions[0]);
    }
}