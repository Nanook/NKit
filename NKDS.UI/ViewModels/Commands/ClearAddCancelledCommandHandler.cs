using NkdsUi.Models;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Signals;

namespace NkdsUi.ViewModels.Commands;

/// <summary>
/// Handles the "Clear" context menu action for AddCancelled rows.
/// Removes selected AddCancelled rows from the ImageListViewModel.
/// </summary>
public class ClearAddCancelledCommandHandler : ICommandHandler
{
    public IObservable<RxVoid> Execute(OperationContext ctx)
    {
        return Signal.FromAsync<RxVoid>(async () =>
        {
            // Collect selected AddCancelled rows
            List<ImageRowViewModel> selectedRows = ctx.ImageList.SelectedImages
                .Where(r => r.ProcessingStatus == ImageProcessingStatus.AddCancelled)
                .ToList();

            if (selectedRows.Count == 0) return RxVoid.Default;

            // Build a set of keys to remove for efficient lookup
            HashSet<(string SetName, long Id)> keysToRemove = selectedRows
                .Select(r => (r.SetName, r.Id))
                .ToHashSet();

            // Remove matching rows from the image list
            ctx.ImageList.RemoveRowsBatch(row =>
                keysToRemove.Contains((row.SetName, row.Id)));

            await Task.CompletedTask;

            return RxVoid.Default;
        });
    }
}