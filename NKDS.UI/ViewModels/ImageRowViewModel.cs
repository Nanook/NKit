using NKDS.Models;
using NkdsUi.Models;
using NKitDataStore;
using ReactiveUI;
using System.Text;

namespace NkdsUi.ViewModels;

/// <summary>
/// Wraps an ImageRecord with observable stats properties for DataGrid binding.
/// Provides passthrough properties for image metadata, formatted hex display,
/// and nullable stats properties that populate progressively during background computation.
/// </summary>
public class ImageRowViewModel : ViewModelBase
{
    private long? _uniqueRawSize;
    private long? _uniqueCompressedSize;
    private long? _sharedRawSize;
    private long? _sharedCompressedSize;
    private long? _savedSize;
    private double? _reducedBy;

    public ImageRowViewModel(ImageRecord image, string sessionId)
    {
        Image = image;
        SessionId = sessionId;
    }

    /// <summary>
    /// The underlying ImageRecord from the domain model.
    /// </summary>
    public ImageRecord Image { get; }

    /// <summary>
    /// Identifies which session owns this image, used for clearing stats on session close.
    /// </summary>
    public string SessionId { get; }

    /// <summary>
    /// The original candidate source data for this row, used to re-process AddCancelled rows.
    /// Null for rows loaded from session or committed normally.
    /// </summary>
    public CandidateImage? CandidateSource { get; init; }

    // Passthrough properties for DataGrid binding
    public string Name => Image.Name;
    public string? System => Image.System;
    public string SetName => Image.SetName;
    public long Size => Image.Size;
    public ImageFormat Format => Image.Format;
    public string FormatDisplay => Image.Format == ImageFormat.Unknown ? string.Empty : Image.Format.ToString();
    public bool Removed => Image.Removed;
    public string RemovedDisplay => Image.Removed ? "Yes" : string.Empty;

    /// <summary>
    /// Marks this image as removed and raises property changed notifications.
    /// </summary>
    public void MarkRemoved()
    {
        Image.Removed = true;
        this.RaisePropertyChanged(nameof(Removed));
        this.RaisePropertyChanged(nameof(RemovedDisplay));
    }

    /// <summary>
    /// Marks this image as restored (not removed) and raises property changed notifications.
    /// </summary>
    public void MarkRestored()
    {
        Image.Removed = false;
        this.RaisePropertyChanged(nameof(Removed));
        this.RaisePropertyChanged(nameof(RemovedDisplay));
    }
    public uint Crc32 => Image.Crc32;
    public ulong XxHash64 => Image.XxHash64;
    public long Id => Image.Id;

    // Formatted hex properties (no "0x" prefix, zero-padded to fixed width)
    // Returns empty string when value is zero (image still being processed/added)
    public string Crc32Hex => Image.Crc32 == 0 ? string.Empty : Image.Crc32.ToString("X8");
    public string XxHash64Hex => Image.XxHash64 == 0 ? string.Empty : Image.XxHash64.ToString("X16");

    // Stats properties (nullable — null means not yet computed)
    public long? UniqueRawSize
    {
        get => _uniqueRawSize;
        set
        {
            this.RaiseAndSetIfChanged(ref _uniqueRawSize, value);
            this.RaisePropertyChanged(nameof(UniqueRawSizeDisplay));
            this.RaisePropertyChanged(nameof(HasStats));
        }
    }
    public string UniqueRawSizeDisplay => _uniqueRawSize.HasValue ? FormatBytes(_uniqueRawSize.Value) : string.Empty;

    public long? UniqueCompressedSize
    {
        get => _uniqueCompressedSize;
        set
        {
            this.RaiseAndSetIfChanged(ref _uniqueCompressedSize, value);
            this.RaisePropertyChanged(nameof(UniqueCompressedSizeDisplay));
        }
    }
    public string UniqueCompressedSizeDisplay => _uniqueCompressedSize.HasValue ? FormatBytes(_uniqueCompressedSize.Value) : string.Empty;

    public long? SharedRawSize
    {
        get => _sharedRawSize;
        set
        {
            this.RaiseAndSetIfChanged(ref _sharedRawSize, value);
            this.RaisePropertyChanged(nameof(SharedRawSizeDisplay));
        }
    }
    public string SharedRawSizeDisplay => _sharedRawSize.HasValue ? FormatBytes(_sharedRawSize.Value) : string.Empty;

    public long? SharedCompressedSize
    {
        get => _sharedCompressedSize;
        set
        {
            this.RaiseAndSetIfChanged(ref _sharedCompressedSize, value);
            this.RaisePropertyChanged(nameof(SharedCompressedSizeDisplay));
        }
    }
    public string SharedCompressedSizeDisplay => _sharedCompressedSize.HasValue ? FormatBytes(_sharedCompressedSize.Value) : string.Empty;

    public long? SavedSize
    {
        get => _savedSize;
        set
        {
            this.RaiseAndSetIfChanged(ref _savedSize, value);
            this.RaisePropertyChanged(nameof(SavedSizeDisplay));
        }
    }
    public string SavedSizeDisplay => _savedSize.HasValue ? FormatBytes(_savedSize.Value) : string.Empty;

    public double? ReducedBy
    {
        get => _reducedBy;
        set
        {
            this.RaiseAndSetIfChanged(ref _reducedBy, value);
            this.RaisePropertyChanged(nameof(ReducedByDisplay));
        }
    }

    // Formatted display strings for DataGrid columns
    public string ReducedByDisplay => _reducedBy.HasValue ? $"{_reducedBy.Value:F1}%" : string.Empty;
    public string SizeDisplay => Image.Size == 0 ? string.Empty : FormatBytes(Image.Size);

    internal static string FormatBytes(long bytes)
    {
        if (bytes < 0) return $"-{FormatBytes(-bytes)}";
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
        if (bytes < 1024L * 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        return $"{bytes / (1024.0 * 1024 * 1024 * 1024):F2} TB";
    }

    // Bar segment ratios (fractions of Size, for the mini stacked bar column)
    private double _barStoredRatio;
    private double _barCompressionRatio;
    private double _barDedupRatio;
    private double _barSharedCompressedRatio;
    private double _barRemoveableRatio;

    public double BarStoredRatio
    {
        get => _barStoredRatio;
        set => this.RaiseAndSetIfChanged(ref _barStoredRatio, value);
    }
    public double BarCompressionRatio
    {
        get => _barCompressionRatio;
        set => this.RaiseAndSetIfChanged(ref _barCompressionRatio, value);
    }
    public double BarDedupRatio
    {
        get => _barDedupRatio;
        set => this.RaiseAndSetIfChanged(ref _barDedupRatio, value);
    }
    public double BarSharedCompressedRatio
    {
        get => _barSharedCompressedRatio;
        set => this.RaiseAndSetIfChanged(ref _barSharedCompressedRatio, value);
    }
    public double BarRemoveableRatio
    {
        get => _barRemoveableRatio;
        set => this.RaiseAndSetIfChanged(ref _barRemoveableRatio, value);
    }

    /// <summary>
    /// Indicates whether stats have been computed for this image.
    /// </summary>
    public bool HasStats => _uniqueRawSize.HasValue || _uniqueCompressedSize.HasValue || _sharedRawSize.HasValue || _sharedCompressedSize.HasValue;

    // Processing status
    private ImageProcessingStatus _processingStatus = ImageProcessingStatus.None;
    private string? _statusReason;

    public ImageProcessingStatus ProcessingStatus
    {
        get => _processingStatus;
        set
        {
            this.RaiseAndSetIfChanged(ref _processingStatus, value);
            this.RaisePropertyChanged(nameof(StatusDisplay));
            this.RaisePropertyChanged(nameof(StatusColor));
        }
    }

    public string? StatusReason
    {
        get => _statusReason;
        set => this.RaiseAndSetIfChanged(ref _statusReason, value);
    }

    // Per-step progress
    private string? _currentStepName;
    private float _currentStepProgress;
    private float _overallProgress;

    public string? CurrentStepName
    {
        get => _currentStepName;
        set => this.RaiseAndSetIfChanged(ref _currentStepName, value);
    }

    public float CurrentStepProgress
    {
        get => _currentStepProgress;
        set => this.RaiseAndSetIfChanged(ref _currentStepProgress, value);
    }

    public float OverallProgress
    {
        get => _overallProgress;
        set => this.RaiseAndSetIfChanged(ref _overallProgress, value);
    }

    public string ProgressDisplay => CurrentStepName != null
        ? $"{CurrentStepName}: {CurrentStepProgress:P0}"
        : string.Empty;

    // Verify result
    private VerifyResultStatus _verifyResult = VerifyResultStatus.None;
    private string? _verifyMethod;

    public VerifyResultStatus VerifyResult
    {
        get => _verifyResult;
        set
        {
            this.RaiseAndSetIfChanged(ref _verifyResult, value);
            this.RaisePropertyChanged(nameof(StatusDisplay));
            this.RaisePropertyChanged(nameof(StatusColor));
        }
    }

    public string? VerifyMethod
    {
        get => _verifyMethod;
        set => this.RaiseAndSetIfChanged(ref _verifyMethod, value);
    }

    // Text output buffer
    private readonly StringBuilder _outputBuffer = new();
    private string _outputText = string.Empty;

    public string OutputText
    {
        get => _outputText;
        private set => this.RaiseAndSetIfChanged(ref _outputText, value);
    }

    public bool HasOutput => _outputBuffer.Length > 0;

    public void AppendOutput(string text)
    {
        _outputBuffer.AppendLine(text);
        OutputText = _outputBuffer.ToString();
        this.RaisePropertyChanged(nameof(HasOutput));
    }

    /// <summary>
    /// Combined status display text for the Status column.
    /// Shows processing status during Add operations, verify result when verified.
    /// </summary>
    public string StatusDisplay => _processingStatus switch
    {
        ImageProcessingStatus.Pending => "Add Pending",
        ImageProcessingStatus.Processing => CandidateSource != null ? "Add Processing" : "Verifying",
        ImageProcessingStatus.AddCancelled => "Add Cancelled",
        ImageProcessingStatus.Cancelled => "Cancelled",
        ImageProcessingStatus.Failed => "Failed",
        ImageProcessingStatus.Skipped => "Skipped",
        ImageProcessingStatus.AlreadyExists => "Already Exists",
        _ => _verifyResult switch
        {
            VerifyResultStatus.VerifySuccess => "\u2713 Verified",
            VerifyResultStatus.VerifyFailed => "\u2717 Verify Failed",
            _ => ""
        }
    };

    // Static shared brushes for StatusColor to avoid per-access allocation
    private static readonly Avalonia.Media.IBrush BrushPending = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#9E9E9E"));
    private static readonly Avalonia.Media.IBrush BrushProcessing = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#2196F3"));
    private static readonly Avalonia.Media.IBrush BrushAddCancelled = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#FFC107"));
    private static readonly Avalonia.Media.IBrush BrushFailed = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#F44336"));
    private static readonly Avalonia.Media.IBrush BrushSkipped = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#FF9800"));
    private static readonly Avalonia.Media.IBrush BrushVerifySuccess = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#4CAF50"));
    private static readonly Avalonia.Media.IBrush BrushVerifyFailed = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#F44336"));

    /// <summary>
    /// Foreground colour for the Status column text.
    /// </summary>
    public Avalonia.Media.IBrush StatusColor => _processingStatus switch
    {
        ImageProcessingStatus.Pending => BrushPending,
        ImageProcessingStatus.Processing => BrushProcessing,
        ImageProcessingStatus.AddCancelled => BrushAddCancelled,
        ImageProcessingStatus.Cancelled => BrushFailed,
        ImageProcessingStatus.Failed => BrushFailed,
        ImageProcessingStatus.Skipped => BrushSkipped,
        ImageProcessingStatus.AlreadyExists => BrushSkipped,
        _ => _verifyResult switch
        {
            VerifyResultStatus.VerifySuccess => BrushVerifySuccess,
            VerifyResultStatus.VerifyFailed => BrushVerifyFailed,
            _ => Avalonia.Media.Brushes.Transparent
        }
    };

    /// <summary>
    /// Returns true if this row has UI-only state that should be preserved across session rebuilds.
    /// This includes verify results, non-default processing status, or computed stats.
    /// </summary>
    public bool HasMeaningfulState =>
        _verifyResult != VerifyResultStatus.None ||
        _processingStatus != ImageProcessingStatus.None;
}