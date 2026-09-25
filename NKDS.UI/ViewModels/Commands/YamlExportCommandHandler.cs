using NkdsUi.Services;
using ReactiveUI.Primitives;
using ReactiveUI.Primitives.Signals;
using System.Text;

namespace NkdsUi.ViewModels.Commands;

/// <summary>
/// Handles the YAML Export toolbar operation.
/// Lifecycle: show save file picker → snapshot filtered images → write YAML on background thread.
/// </summary>
public class YamlExportCommandHandler : ICommandHandler
{
    public IObservable<RxVoid> Execute(OperationContext ctx)
    {
        return Signal.FromAsync<RxVoid>(async () =>
        {
            try
            {
                // 1. Show save file dialog
                string filePath = await ctx.ShowSaveFilePicker.Handle(RxVoid.Default);
                if (string.IsNullOrEmpty(filePath)) return RxVoid.Default;

                // 2. Snapshot current state on UI thread
                List<ImageRowViewModel> images = ctx.ImageList.FilteredImages.ToList();
                bool includeStats = ctx.ImageList.ShowStatsColumns;
                string source = DetermineSource(images);

                // 3. Write on background thread
                await Task.Run(() =>
                {
                    using FileStream stream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
                    using StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false));
                    YamlImageListWriter.Write(writer, images, includeStats, source);
                });
            }
            catch (OperationCanceledException)
            {
                ctx.ErrorNotification.PublishCancellation("YAML Export");
            }
            catch (Exception ex)
            {
                ctx.ErrorNotification.PublishOperationError(
                    $"YAML Export failed: {ex.Message}");
            }

            return RxVoid.Default;
        });
    }

    /// <summary>
    /// Determines the source label for the YAML export.
    /// Returns the set name if all images belong to the same set, otherwise "All".
    /// </summary>
    private static string DetermineSource(IReadOnlyList<ImageRowViewModel> images)
    {
        if (images.Count == 0) return "All";
        string firstSet = images[0].SetName;
        for (int i = 1; i < images.Count; i++)
        {
            if (!string.Equals(images[i].SetName, firstSet, StringComparison.Ordinal))
                return "All";
        }
        return firstSet;
    }

    /// <summary>
    /// Attempts to delete a partially-written file. Swallows all exceptions.
    /// </summary>
    private static void TryDeletePartialFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}