using Avalonia.Threading;
using NKDS;
using NKDS.Models;
using NkdsUi.Models;
using NKitDataStore;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Signals;

namespace NkdsUi.ViewModels.Commands;

/// <summary>
/// Handles the Create Set toolbar operation.
/// Lifecycle: session resolution → show CreateSetDialog → NkdsOperations.CreateSet
///            → switch session to file mode → refresh set names.
/// </summary>
public class CreateSetCommandHandler : ICommandHandler
{
    public IObservable<RxVoid> Execute(OperationContext ctx)
    {
        return Signal.FromAsync<RxVoid>(async () =>
        {
            // 1. Resolve session path (if any) — folder selection happens inside the dialog
            IReadOnlyList<ImageSessionModel> sessions;
            try
            {
                sessions = await ctx.DataStoreService.Sessions.FirstAsync();
            }
            catch (Exception ex)
            {
                ctx.ErrorNotification.PublishOperationError("Create Set failed: " + ex.Message);
                return RxVoid.Default;
            }

            string? dataStorePath = null;

            if (sessions.Count > 0)
            {
                ImageSessionModel session = sessions[0];
                dataStorePath = SessionResolver.ResolveDataStorePath(session).dataStorePath;
            }

            // 2. Show CreateSet dialog
            CreateSetDialogViewModel dialogVm = new CreateSetDialogViewModel(dataStorePath, ctx.ConfigService);
            bool confirmed;
            try
            {
                confirmed = await ctx.ShowCreateSetDialog.Handle(dialogVm);
            }
            catch (Exception ex)
            {
                ctx.ErrorNotification.PublishOperationError("Create Set failed: " + ex.Message);
                return RxVoid.Default;
            }

            if (!confirmed) return RxVoid.Default;

            // Use the folder path from the dialog (inline folder selection)
            dataStorePath = dialogVm.FolderPath;

            // 3. Create progress tracking
            OperationProgressViewModel progressVm = new OperationProgressViewModel { OperationName = "Create Set" };
            ctx.ActiveOperations.Add(progressVm);
            ctx.SharedState.OperationInProgressOnSet = dialogVm.SetName;

            try
            {
                // 4. Create the set (no exclusive access needed — creates new files)
                using NkdsOperations ops = new NkdsOperations();
                OperationResult result = ops.CreateSet(dataStorePath, dialogVm.SetName, dialogVm.ParsedShardSize, dialogVm.ParsedBlockSize);

                if (result.Success)
                {
                    // If aux mode is enabled and no existing aux was found, create the aux set
                    if (dialogVm.IsAuxMode && dialogVm.IsAuxEditable && !string.IsNullOrWhiteSpace(dialogVm.AuxSetName))
                    {
                        string auxSetName = dialogVm.AuxSetName;
                        if (auxSetName.EndsWith(DataStore.DatabaseFileExtension, StringComparison.OrdinalIgnoreCase))
                            auxSetName = auxSetName[..^DataStore.DatabaseFileExtension.Length];

                        OperationResult auxResult = ops.CreateSet(dataStorePath, auxSetName, dialogVm.ParsedShardSize, dialogVm.ParsedBlockSize);
                        if (!auxResult.Success)
                        {
                            ctx.ErrorNotification.PublishOperationError(
                                $"Aux set creation failed: {auxResult.Errors?.FirstOrDefault()?.Reason ?? "Unknown"}");
                        }
                    }

                    // 5. Switch session to file mode for the new set
                    string setFilePath = Path.Combine(dataStorePath, dialogVm.SetName + DataStore.DatabaseFileExtension);
                    ctx.ImageList.SuppressFilters();
                    try
                    {
                        await ctx.SessionManager.OpenFileAsync(setFilePath);

                        // Allow any queued reactive emissions to drain
                        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
                    }
                    finally
                    {
                        ctx.ImageList.ResumeFilters();
                    }

                    // 6. Refresh set names and select the new set
                    await ctx.SyncService.RefreshAvailableSetNamesAsync();
                    ctx.SharedState.SelectedSetName = dialogVm.SetName;
                }
                else
                {
                    ctx.ErrorNotification.PublishOperationError(
                        $"Create Set failed: {result.Errors?.FirstOrDefault()?.Reason ?? "Unknown"}");
                }
            }
            catch (OperationCanceledException)
            {
                ctx.ErrorNotification.PublishCancellation("Create Set");
            }
            catch (Exception ex)
            {
                ctx.ErrorNotification.PublishOperationError(
                    $"Create Set failed: {ex.Message}");
            }
            finally
            {
                ctx.ActiveOperations.Remove(progressVm);
                ctx.SharedState.OperationInProgressOnSet = null;
                progressVm.Dispose();
            }

            return RxVoid.Default;
        });
    }
}