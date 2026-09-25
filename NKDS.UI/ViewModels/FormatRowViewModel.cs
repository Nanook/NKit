
using Nanook.NKit;
using Nanook.NKit.Configuration;
using NkdsUi.Models;
using ReactiveUI;
using ReactiveUI.Primitives;

namespace NkdsUi.ViewModels;
/// <summary>
/// Represents a single row in the export format grid.
/// Each row corresponds to a unique (System, SourceFormat) combination.
/// </summary>
public class FormatRowViewModel : ViewModelBase
{
    private string _selectedTargetFormat;
    private bool _lossless;
    private string _selectedEncoding = "zstd";
    private IReadOnlyList<int> _compressionLevels = Enumerable.Range(1, 22).ToList();
    private int _selectedLevel = 19;
    private string _selectedBlockSize = "128kb";
    private bool _hasFormatOptions;
    private bool _isRvzOptionsVisible;
    private bool _isLevelVisible;
    private bool _isLosslessVisible;

    public FormatRowViewModel(string system, string sourceFormat, IReadOnlyList<string> targetFormats, string? defaultSelection = null)
    {
        System = system;
        SourceFormat = sourceFormat;
        TargetFormats = targetFormats;

        // Use persisted selection if valid, otherwise first item
        _selectedTargetFormat = (defaultSelection != null && targetFormats.Contains(defaultSelection))
            ? defaultSelection
            : targetFormats[0];

        // When SelectedEncoding changes, update CompressionLevels and SelectedLevel
        this.WhenAnyValue(x => x.SelectedEncoding)
            .Subscribe(encoding =>
            {
                switch (encoding)
                {
                    case "zstd":
                        CompressionLevels = Enumerable.Range(1, 22).ToList();
                        SelectedLevel = 19;
                        break;
                    case "lzma":
                        CompressionLevels = Enumerable.Range(1, 9).ToList();
                        SelectedLevel = 5;
                        break;
                    case "none":
                        CompressionLevels = Array.Empty<int>();
                        break;
                }

                // Update level visibility when encoding changes
                IsLevelVisible = IsRvzOptionsVisible && IsEncodingWithLevel(encoding);
            });

        // Initialize visibility based on current values
        _hasFormatOptions = IsFormatWithOptions(_selectedTargetFormat);
        _isRvzOptionsVisible = string.Equals(_selectedTargetFormat, "rvz", StringComparison.OrdinalIgnoreCase);
        _isLevelVisible = _isRvzOptionsVisible && IsEncodingWithLevel(_selectedEncoding);
        _isLosslessVisible = string.Equals(_selectedTargetFormat, "ciso", StringComparison.OrdinalIgnoreCase)
                          || string.Equals(_selectedTargetFormat, "wbfs", StringComparison.OrdinalIgnoreCase);

        // Wire reactive subscriptions for visibility updates on format change
        this.WhenAnyValue(x => x.SelectedTargetFormat)
            .Skip(1) // Skip initial value (already set above)
            .Subscribe(format =>
            {
                HasFormatOptions = IsFormatWithOptions(format);
                IsRvzOptionsVisible = string.Equals(format, "rvz", StringComparison.OrdinalIgnoreCase);
                IsLosslessVisible = string.Equals(format, "ciso", StringComparison.OrdinalIgnoreCase)
                                 || string.Equals(format, "wbfs", StringComparison.OrdinalIgnoreCase);
                // Recompute level visibility when format changes (level only applies to RVZ)
                IsLevelVisible = IsRvzOptionsVisible && IsEncodingWithLevel(SelectedEncoding);
            });
    }

    /// <summary>The system name (e.g., "GameCube", "Wii", "WiiU").</summary>
    public string System { get; }

    /// <summary>The source format (e.g., "iso", "app").</summary>
    public string SourceFormat { get; }

    /// <summary>Display label combining system and source format.</summary>
    public string Label => $"{System} ({SourceFormat})";

    /// <summary>Available target format options for this row.</summary>
    public IReadOnlyList<string> TargetFormats { get; }

    /// <summary>The currently selected target format.</summary>
    public string SelectedTargetFormat
    {
        get => _selectedTargetFormat;
        set => this.RaiseAndSetIfChanged(ref _selectedTargetFormat, value);
    }

    /// <summary>Lossless mode option for CISO/WBFS formats. Default: false.</summary>
    public bool Lossless
    {
        get => _lossless;
        set => this.RaiseAndSetIfChanged(ref _lossless, value);
    }

    // === RVZ Options ===

    /// <summary>Available encoding types for RVZ format.</summary>
    public IReadOnlyList<string> EncodingTypes { get; } = ["none", "zstd", "lzma"];

    /// <summary>The currently selected encoding type. Default: "zstd".</summary>
    public string SelectedEncoding
    {
        get => _selectedEncoding;
        set => this.RaiseAndSetIfChanged(ref _selectedEncoding, value);
    }

    /// <summary>Available compression levels based on the current encoding selection.</summary>
    public IReadOnlyList<int> CompressionLevels
    {
        get => _compressionLevels;
        set => this.RaiseAndSetIfChanged(ref _compressionLevels, value);
    }

    /// <summary>The currently selected compression level. Default: 19 for ZStd, 5 for LZMA.</summary>
    public int SelectedLevel
    {
        get => _selectedLevel;
        set => this.RaiseAndSetIfChanged(ref _selectedLevel, value);
    }

    /// <summary>Available block sizes for RVZ format.</summary>
    public IReadOnlyList<string> BlockSizes { get; } = ["32kb", "64kb", "128kb", "256kb", "512kb", "1mb", "2mb"];

    /// <summary>The currently selected block size. Default: "128kb".</summary>
    public string SelectedBlockSize
    {
        get => _selectedBlockSize;
        set => this.RaiseAndSetIfChanged(ref _selectedBlockSize, value);
    }

    // === Visibility Computed Properties ===

    /// <summary>True when the selected target format supports format-specific options (rvz, ciso, or wbfs).</summary>
    public bool HasFormatOptions
    {
        get => _hasFormatOptions;
        private set => this.RaiseAndSetIfChanged(ref _hasFormatOptions, value);
    }

    /// <summary>True when the selected target format is "rvz".</summary>
    public bool IsRvzOptionsVisible
    {
        get => _isRvzOptionsVisible;
        private set => this.RaiseAndSetIfChanged(ref _isRvzOptionsVisible, value);
    }

    /// <summary>True when the encoding is "zstd" or "lzma" (compression level is applicable).</summary>
    public bool IsLevelVisible
    {
        get => _isLevelVisible;
        private set => this.RaiseAndSetIfChanged(ref _isLevelVisible, value);
    }

    /// <summary>True when the selected target format is "ciso" or "wbfs".</summary>
    public bool IsLosslessVisible
    {
        get => _isLosslessVisible;
        private set => this.RaiseAndSetIfChanged(ref _isLosslessVisible, value);
    }

    // === Format String Generation ===

    /// <summary>
    /// Generates the full format string for the conversion engine.
    /// Uses ConfigSettingsFormatGenerator for RVZ, WBFS, and CISO formats.
    /// Returns the bare format name for formats without options (e.g., iso, wux).
    /// </summary>
    public string GetConvertFormatString()
    {
        string? format = SelectedTargetFormat?.ToLowerInvariant();

        return format switch
        {
            "rvz" => GenerateRvzString(),
            "wbfs" => ConfigSettingsFormatGenerator.GenerateWbfsFormatString(Lossless),
            "ciso" => ConfigSettingsFormatGenerator.GenerateCisoFormatString(Lossless),
            _ => SelectedTargetFormat ?? string.Empty
        };
    }

    private string GenerateRvzString()
    {
        RvzEncodingType encoding = SelectedEncoding?.ToLowerInvariant() switch
        {
            "zstd" => RvzEncodingType.ZStd,
            "lzma" => RvzEncodingType.Lzma,
            _ => RvzEncodingType.None
        };

        // For None encoding, level is not applicable (GenerateRvzFormatString handles this)
        int? level = encoding == RvzEncodingType.None ? null : SelectedLevel;

        return ConfigSettingsFormatGenerator.GenerateRvzFormatString(encoding, level, SelectedBlockSize, 16);
    }

    // === Options Loading ===

    /// <summary>
    /// Loads persisted format options into this view model, validating each value
    /// and falling back to defaults for any invalid entries.
    /// Must be called after construction when persisted options are available.
    /// </summary>
    /// <param name="options">The persisted options entry, or null if no persisted options exist.</param>
    public void LoadOptions(FormatOptionsEntry? options)
    {
        if (options is null)
            return;

        if (IsRvzOptionsVisible)
        {
            // Set encoding FIRST — this triggers the subscription that updates
            // CompressionLevels and resets SelectedLevel to the default for that encoding.
            if (options.Encoding is not null && EncodingTypes.Contains(options.Encoding))
            {
                SelectedEncoding = options.Encoding;
            }

            // After encoding is set (and CompressionLevels updated), validate and apply level.
            if (options.Level.HasValue && CompressionLevels.Contains(options.Level.Value))
            {
                SelectedLevel = options.Level.Value;
            }

            // Validate and apply block size.
            if (options.BlockSize is not null && BlockSizes.Contains(options.BlockSize))
            {
                SelectedBlockSize = options.BlockSize;
            }
        }
        else if (IsLosslessVisible)
        {
            // CISO/WBFS: apply lossless value if present; fall back to false (the default).
            if (options.Lossless.HasValue)
            {
                Lossless = options.Lossless.Value;
            }
        }
    }

    // === Helper Methods ===

    private static bool IsFormatWithOptions(string format) =>
        string.Equals(format, "rvz", StringComparison.OrdinalIgnoreCase)
        || string.Equals(format, "ciso", StringComparison.OrdinalIgnoreCase)
        || string.Equals(format, "wbfs", StringComparison.OrdinalIgnoreCase);

    private static bool IsEncodingWithLevel(string encoding) =>
        string.Equals(encoding, "zstd", StringComparison.OrdinalIgnoreCase)
        || string.Equals(encoding, "lzma", StringComparison.OrdinalIgnoreCase);
}