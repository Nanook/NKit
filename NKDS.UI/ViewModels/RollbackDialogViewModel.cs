using ReactiveUI;
using ReactiveUI.Primitives;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace NkdsUi.ViewModels;

/// <summary>
/// Item displayed in the rollback dialog image list.
/// </summary>
public class RollbackImageItem : INotifyPropertyChanged
{
    private bool _isAfterSelected;

    public long Id { get; init; }
    public string Name { get; init; } = "";
    public string SetName { get; init; } = "";

    /// <summary>
    /// True when this item comes after the selected rollback target and will be deleted.
    /// </summary>
    public bool IsAfterSelected
    {
        get => _isAfterSelected;
        set
        {
            if (_isAfterSelected == value) return;
            _isAfterSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsAfterSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// ViewModel for the Rollback dialog. Displays a set dropdown (excluding "All") and
/// images in the selected set ordered by ID. Allows the user to select a target image
/// ID to rollback to, and shows a confirmation warning indicating how many images will
/// be permanently deleted.
/// </summary>
public class RollbackDialogViewModel : ViewModelBase
{
    private string? _selectedSetName;
    private RollbackImageItem? _selectedImage;
    private long? _selectedTargetId;
    private int _imagesAfterTarget;

    /// <summary>
    /// All available set names (excluding "All").
    /// </summary>
    public IReadOnlyList<string> AvailableSetNames { get; }

    /// <summary>
    /// The currently selected set name. When changed, filters the image list.
    /// </summary>
    public string? SelectedSetName
    {
        get => _selectedSetName;
        set => this.RaiseAndSetIfChanged(ref _selectedSetName, value);
    }

    /// <summary>
    /// All images across all sets (unfiltered source).
    /// </summary>
    private IReadOnlyList<RollbackImageItem> AllImages { get; }

    /// <summary>
    /// Images filtered to the currently selected set, ordered by ID ascending.
    /// </summary>
    public ObservableCollection<RollbackImageItem> Images { get; } = new();

    /// <summary>
    /// The currently selected image in the list. When changed, updates SelectedTargetId
    /// and computes ImagesAfterTarget.
    /// </summary>
    public RollbackImageItem? SelectedImage
    {
        get => _selectedImage;
        set => this.RaiseAndSetIfChanged(ref _selectedImage, value);
    }

    /// <summary>
    /// The target image ID to rollback to. All images with ID greater than this will be deleted.
    /// Derived from SelectedImage.
    /// </summary>
    public long? SelectedTargetId
    {
        get => _selectedTargetId;
        private set => this.RaiseAndSetIfChanged(ref _selectedTargetId, value);
    }

    /// <summary>
    /// The count of images that will be permanently deleted (images with ID > target).
    /// </summary>
    public int ImagesAfterTarget
    {
        get => _imagesAfterTarget;
        private set => this.RaiseAndSetIfChanged(ref _imagesAfterTarget, value);
    }

    /// <summary>
    /// Warning text displayed to the user. Shows how many images will be deleted.
    /// </summary>
    public string WarningText => SelectedTargetId.HasValue
        ? $"All {ImagesAfterTarget} image(s) added after ID {SelectedTargetId.Value} will be permanently deleted"
        : "";

    /// <summary>
    /// Whether a target is selected and there are images to rollback.
    /// </summary>
    public bool HasWarning => SelectedTargetId.HasValue && ImagesAfterTarget > 0;

    /// <summary>
    /// Command that confirms the rollback. Only executable when a target image is selected.
    /// Returns true as the dialog result.
    /// </summary>
    public ReactiveCommand<RxVoid, bool> ConfirmCommand { get; }

    /// <summary>
    /// Command that cancels the dialog. Always executable. Returns false as the dialog result.
    /// </summary>
    public ReactiveCommand<RxVoid, bool> CancelCommand { get; }

    /// <summary>
    /// Creates a RollbackDialogViewModel with the given images and set names.
    /// </summary>
    /// <param name="images">Images across all sets.</param>
    /// <param name="availableSetNames">Set names available (will be filtered to exclude "All").</param>
    /// <param name="activeSetName">The currently active set name from the toolbar (pre-selected).</param>
    public RollbackDialogViewModel(
        IEnumerable<RollbackImageItem> images,
        IReadOnlyList<string> availableSetNames,
        string? activeSetName)
    {
        AllImages = images.OrderBy(i => i.Id).ToList();
        AvailableSetNames = availableSetNames
            .Where(n => !string.Equals(n, "All", StringComparison.OrdinalIgnoreCase))
            .ToList();

        // canExecute: a target must be selected
        IObservable<bool> canConfirm = this.WhenAnyValue(x => x.SelectedTargetId)
            .Select(id => id.HasValue)
            .DistinctUntilChanged();

        ConfirmCommand = ReactiveCommand.Create(() => true, canConfirm);
        CancelCommand = ReactiveCommand.Create(() => false);

        // When SelectedSetName changes, filter the image list
        this.WhenAnyValue(x => x.SelectedSetName)
            .Subscribe(setName =>
            {
                Images.Clear();
                SelectedImage = null;

                if (setName == null) return;

                IOrderedEnumerable<RollbackImageItem> filtered = AllImages
                    .Where(i => i.SetName == setName)
                    .OrderBy(i => i.Id);

                foreach (RollbackImageItem? img in filtered)
                    Images.Add(img);
            });

        // When SelectedImage changes, update SelectedTargetId and ImagesAfterTarget
        this.WhenAnyValue(x => x.SelectedImage)
            .Subscribe(image =>
            {
                if (image == null)
                {
                    SelectedTargetId = null;
                    ImagesAfterTarget = 0;
                    foreach (RollbackImageItem img in Images)
                        img.IsAfterSelected = false;
                }
                else
                {
                    SelectedTargetId = image.Id;
                    ImagesAfterTarget = Images.Count(i => i.Id > image.Id);
                    foreach (RollbackImageItem img in Images)
                        img.IsAfterSelected = img.Id > image.Id;
                }

                this.RaisePropertyChanged(nameof(WarningText));
                this.RaisePropertyChanged(nameof(HasWarning));
            });

        // Pre-select the active set (or first available)
        if (activeSetName != null && AvailableSetNames.Contains(activeSetName))
            SelectedSetName = activeSetName;
        else if (AvailableSetNames.Count > 0)
            SelectedSetName = AvailableSetNames[0];
    }

    /// <summary>
    /// Parameterless constructor for design-time support.
    /// </summary>
    public RollbackDialogViewModel()
        : this(Array.Empty<RollbackImageItem>(), Array.Empty<string>(), null)
    {
    }
}