using Avalonia.Threading;
using Nanook.NKit;
using NKit.Ui.Helpers;
using NKitDataStore;
using ReactiveUI;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using YamlDotNet.Serialization;

namespace NKit.Ui.Models
{
    public class SourceFileRecord : ReactiveObject
    {
        // Notify listeners when an instance's details visibility changes so UI can enforce
        // a single-expanded-item policy across the list.
        public static event Action<SourceFileRecord, bool> DetailsVisibilityChanged;
        private bool _sleepHack;
        [YamlIgnore]
        public SourceFile SourceFile { get; set; }
        public string Name { get; set; }
        public string Filepath { get; set; }
        public SourceImageType? ImageType { get; set; }
        public SourceArchiveType? ArchiveType { get; set; }
        public long Length { get; set; }
        public ObservableCollection<SourceFileRecord> ImageAndArchiveFiles { get; set; } = new ObservableCollection<SourceFileRecord>();
        public string SourceFileDetails { get; set; }
        private StringBuilder _processingOutput { get; set; } = new();
        public string ConsoleOutput
        {
            get => _processingOutput.ToString(); set => _processingOutput = new StringBuilder(value);
        }

        public SourceFileRecord() { }

        public SourceFileRecord(SourceFile sourceFile)
        {
            SourceFile = sourceFile;
            Name = sourceFile.Name;
            ImageType = sourceFile.ImageType;
            ArchiveType = sourceFile.ArchiveType;
            Filepath = getFilePath(sourceFile);

            Length = sourceFile.Length;
            ImageAndArchiveFiles = new();
            Progress = 0;
            DisplayDetailsIcon = "ChevronRight";
            ProcessingStatus = ProcessingStatus.Queued;

            SourceFileDetails = getSourceFileDetails(sourceFile);
        }

        private string getFilePath(SourceFile sourceFile)
        {
            if (sourceFile.IsArchived)
                return sourceFile.FriendlyFullPath;
            else if (sourceFile.IndexFile != null)
                return Path.Combine(sourceFile.IndexFile.Path, sourceFile.IndexFile.FileName);
            else
                return Path.Combine(sourceFile.BasePath, sourceFile.ImageFiles[0].FileName);
        }

        public void ResetProcessingMessages()
        {
            _processingOutput.Clear();
            this.RaisePropertyChanged(nameof(ConsoleOutput));

            // for feedback
            //SourceFileDetails = GetSourceFileDetails(SourceFile);
            //this.RaisePropertyChanged(nameof(SourceFileDetails));
        }

        public void RefreshConsoleOutput() => this.RaisePropertyChanged(nameof(ConsoleOutput));

        private string getSourceFileDetails(SourceFile sf)
        {
            List<string> sfd = new List<string>();

            string path = sf.ArchiveFiles?[0]?.Path ??
                       sf.IndexFile?.Path ??
                       sf.ImageFiles?[0]?.Path;

            // Use simplified display path conversion
            string displayPath = PathsHelper.ToDisplayPath(path);
            sfd.Add($"Path: {displayPath}");

            if (sf.IndexFile != null)
            {
                if (sf?.IndexFile?.Items?.Length > 0)
                {
                    sfd.Add($"Parts:");

                    if (!string.IsNullOrEmpty(sf.IndexFile?.FileName))
                    {
                        sfd.Add($"  - {sf.IndexFile.FileName}");
                        // Length += sf.IndexFile.Size;
                    }

                    foreach (SourceFileTrack item in sf.IndexFile.Items)
                    {
                        sfd.Add($"  - {item.FileName}");
                        Length += item.Size;
                    }
                }
                else if (!string.IsNullOrEmpty(sf.IndexFile?.FileName))
                {

                }
            }
            else if (sf.ImageFiles?.Length > 0)
            {
                sfd.Add("Image Files:");

                Length = 0;
                foreach (SourceFileItem img in sf.ImageFiles)
                {
                    sfd.Add($"  - {img.FileName}");
                    Length += img.Size;
                }
            }

            if (sf.ArchiveFiles?.Length > 0)
            {
                sfd.Add(string.Empty);
                sfd.Add("Archive Files");

                foreach (SourceFileItem arc in sf.ArchiveFiles)
                {
                    sfd.Add($"  - {arc.FileName}");
                    //Nanook: Size of image not arhive?? Length += arc.Size;
                }
            }

            return string.Join(Environment.NewLine, sfd);
        }

        // Cap on the per-row processing transcript. Every row lives in the session-long FileQueue,
        // so an unbounded per-file transcript is retained for the whole run. The details pane only
        // needs the recent tail, so bound it: once it exceeds the cap, drop the oldest half. This
        // keeps a completed row's retained text bounded regardless of how verbose its verify was.
        private const int MaxProcessingOutputChars = 64 * 1024;

        public void AppendProcessingMessage(string message) //async
        {
            //await Dispatcher.UIThread.InvokeAsync(() =>
            //{
            _processingOutput.Append(message);
            if (_processingOutput.Length > MaxProcessingOutputChars)
                _processingOutput.Remove(0, _processingOutput.Length - (MaxProcessingOutputChars / 2));
            this.RaisePropertyChanged(nameof(ConsoleOutput));
            //});
        }

        private string _currentTask = "Queued";
        public string CurrentTask
        {
            get => _currentTask;
            internal set => this.RaiseAndSetIfChanged(ref _currentTask, value);
        }

        private double _progress;
        public double Progress
        {
            get => _progress;
            internal set
            {
                _progress = value;

                if (value == 0)
                {
                    StatusIcon = "ClockTimeThreeOutline";
                }
                else if (value < 100)
                {
                    StatusIcon = "Autorenew";
                }

                this.RaisePropertyChanged(nameof(Progress));
                this.RaisePropertyChanged(nameof(ProgressPercentage));
            }
        }

        [YamlIgnore]
        public string ProgressPercentage => $"{(int)Progress}%";

        private string _outFileName;
        public string OutFileName
        {
            get => PathsHelper.ToDisplayPath(_outFileName);
            internal set => this.RaiseAndSetIfChanged(ref _outFileName, value);
        }

        private VerifyResult _verifyResultStatus;
        public VerifyResult VerifyResultStatus
        {
            get => _verifyResultStatus;
            set => this.RaiseAndSetIfChanged(ref _verifyResultStatus, value);
        }

        private string _verifyResultMessage;
        public string VerifyResultMessage
        {
            get => _verifyResultMessage;
            internal set => this.RaiseAndSetIfChanged(ref _verifyResultMessage, value);
        }

        private ProcessingStatus _processingStatus;
        public ProcessingStatus ProcessingStatus
        {
            get => _processingStatus;
            set
            {
                _processingStatus = value;

                switch (value)
                {
                    case ProcessingStatus.Queued:
                        StatusIcon = "ClockTimeThreeOutline";
                        StatusIconColour = "White";
                        StatusIconToolTip = "Queued";
                        CurrentTask = "Queued";
                        break;
                    case ProcessingStatus.Skipped:
                        StatusIcon = "HelpCircleOutline";
                        StatusIconColour = "Orange";
                        StatusIconToolTip = "Skipped";
                        CurrentTask = "Skipped";
                        VerifyResultStatus = (VerifyResult)(-1);
                        break;
                    case ProcessingStatus.Processing:
                        StatusIcon = "Autorenew";
                        StatusIconColour = "LightBlue";
                        StatusIconToolTip = "Processing";
                        break;
                    case ProcessingStatus.Completed:
                        StatusIcon = "CheckCircleOutline";
                        StatusIconColour = "Green";
                        StatusIconToolTip = "Completed";
                        CurrentTask = "Completed";
                        break;
                    case ProcessingStatus.Failed:
                        StatusIcon = "AlertCircleOutline";
                        StatusIconColour = "Red";
                        StatusIconToolTip = "Failed";
                        CurrentTask = "Failed";
                        break;
                    case ProcessingStatus.AlreadyExists:
                        StatusIcon = "ContentDuplicate";
                        StatusIconColour = "Orange";
                        StatusIconToolTip = "Already in set";
                        CurrentTask = "Already Exists";
                        VerifyResultStatus = (VerifyResult)(-1);
                        break;
                    case ProcessingStatus.Cancelled:
                        Progress = 50;
                        this.RaisePropertyChanged(nameof(ProgressPercentage));
                        StatusIcon = "Cancel";
                        StatusIconColour = "Red";
                        StatusIconToolTip = "Cancelled";
                        CurrentTask = "Cancelled";
                        VerifyResultStatus = (VerifyResult)(-1);
                        VerifyResultMessage = null;
                        VerifyResultIcon = null;
                        VerifyResultIconColour = null;
                        break;
                    default:
                        StatusIcon = "ClockTimeThreeOutline";
                        StatusIconColour = "White";
                        StatusIconToolTip = string.Empty;
                        break;
                }

                this.RaisePropertyChanged(nameof(ProcessingStatus));
            }
        }

        private NKitTaskResults _result;
        [YamlIgnore]
        public NKitTaskResults Result
        {
            get => _result;
            set
            {
                _result = value;

                if (value is null)
                {
                    VerifyResultMessage = null;
                    VerifyResultIcon = null;
                    VerifyResultIconColour = null;
                    return;
                }

                VerifyResultStatus = value?.VerifyResult ?? VerifyResult.Unverified;

                switch (VerifyResultStatus)
                {
                    case VerifyResult.Unverified:
                        VerifyResultMessage = $"Unverified ({value.VerifyType})";
                        VerifyResultIcon = "HelpCircleOutline";
                        VerifyResultIconColour = "Orange";
                        break;
                    case VerifyResult.VerifySuccess:
                        VerifyResultMessage = $"Verified ({value.VerifyType})";
                        VerifyResultIcon = "CheckCircleOutline";
                        VerifyResultIconColour = "Green";
                        break;
                    case VerifyResult.VerifyFailed:
                        VerifyResultMessage = $"Verify Failed ({value.VerifyType})";
                        VerifyResultIcon = "AlertCircleOutline";
                        VerifyResultIconColour = "Red";
                        break;
                    case VerifyResult.Error:
                        VerifyResultMessage = Result.ErrorMsg;
                        VerifyResultIcon = "AlertCircleOutline";
                        VerifyResultIconColour = "Red";
                        break;
                    default:
                        VerifyResultMessage = string.Empty;
                        VerifyResultIcon = "";
                        VerifyResultIconColour = "";
                        break;
                }
            }
        }

        private string _verifyResultIcon;
        public string VerifyResultIcon
        {
            get => _verifyResultIcon;
            set => this.RaiseAndSetIfChanged(ref _verifyResultIcon, value);
        }

        private string _verifyResultIconColour;
        public string VerifyResultIconColour
        {
            get => _verifyResultIconColour;
            set => this.RaiseAndSetIfChanged(ref _verifyResultIconColour, value);
        }

        private string _statusIcon;
        public string StatusIcon
        {
            get => _statusIcon;
            set => this.RaiseAndSetIfChanged(ref _statusIcon, value);
        }

        private string _displayDetailsIcon;
        public string DisplayDetailsIcon
        {
            get => DisplayContainingFiles ? _displayDetailsIcon : string.Empty;

            set
            {
                if (DisplayContainingFiles)
                {
                    _displayDetailsIcon = value;
                }

                this.RaisePropertyChanged(nameof(DisplayDetailsIcon));
            }
        }

        private bool _isDetailsVisible;
        public bool IsDetailsVisible
        {
            get => _isDetailsVisible;
            set
            {
                this.RaiseAndSetIfChanged(ref _isDetailsVisible, value);
                try { System.Diagnostics.Trace.WriteLine($"SourceFileRecord.IsDetailsVisible changed: name={Name} value={value}"); } catch { }
                try
                {
                    DetailsVisibilityChanged?.Invoke(this, value);
                }
                catch { }
                // Keep display icon in sync for UI
                try
                {
                    DisplayDetailsIcon = value ? "ChevronDown" : "ChevronRight";
                }
                catch { }
            }
        }

        private string _statusIconColour;
        public string StatusIconColour
        {
            get => _statusIconColour;
            set => this.RaiseAndSetIfChanged(ref _statusIconColour, value);
        }

        private string _statusIconToolTip;
        public string StatusIconToolTip
        {
            get => _statusIconToolTip;
            set => this.RaiseAndSetIfChanged(ref _statusIconToolTip, value);
        }

        [YamlIgnore]
        public string IsImageOrArchiveType => isDataStoreSource() ? "DataStore" :
                   ((ArchiveType is null || ArchiveType == SourceArchiveType.None) ? "Image" : "Archive");

        [YamlIgnore]
        public bool DisplayContainingFiles => true;//IsImageOrArchiveType == "Archive";

        [YamlIgnore]
        public string FileTypeIcon => IsImageOrArchiveType switch
        {
            "Image" => "Disc",
            "DataStore" => "DatabaseOutline",
            "Archive" => "FolderZipOutline",
            _ => "FileOutline"
        };

        [YamlIgnore]
        public string GetImageOrArchiveType => isDataStoreSource() ? "DATASTORE" : (ImageType?.ToString() ?? ArchiveType.ToString()).ToUpper();

        private bool isDataStoreSource() =>
            SourceFile?.IsDataStore == true ||
            ArchiveType == SourceArchiveType.DataStore ||
            (SourceFiles.TrySplitArchivePath(Filepath, out string archivePath, out _) &&
             string.Equals(SourceFiles.GetKnownFileExtension(archivePath), DataStore.DatabaseFileExtension, StringComparison.OrdinalIgnoreCase));

        [YamlIgnore]
        public string GetFileSize
        {
            get
            {
                long bytes = Length == 0 ? ImageAndArchiveFiles.Sum(x => x.Length) : Length;

                int unit = 1024;
                if (bytes < unit) { return $"{bytes} B"; }

                int exp = (int)(Math.Log(bytes) / Math.Log(unit));
                return $"{bytes / Math.Pow(unit, exp):F2} {"KMGTPE"[exp - 1]}B";
            }
        }

        public void UpdateProgress(object s, ProgressEventArgs e)
        {
            if (_sleepHack && e.Progress != 0f && e.Progress != 1f)
                return;
            _sleepHack = true;

            Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    Progress = e.ProgressTotal * 100;
                    CurrentTask = e.Steps[e.Step];

                    this.RaisePropertyChanged(nameof(CurrentTask));
                    this.RaisePropertyChanged(nameof(Progress));
                    this.RaisePropertyChanged(nameof(ProgressPercentage));

                    Dispatcher.UIThread.RunJobs(DispatcherPriority.MaxValue);
                }
                finally
                {
                    _sleepHack = false;
                }
            }, DispatcherPriority.MaxValue);
        }

        public override string ToString() => Name.Trim();

        // newing base properties to ignore them in yaml serialisation
        [YamlIgnore]
        public new IObservable<IReactivePropertyChangedEventArgs<IReactiveObject>> Changing => base.Changing;

        [YamlIgnore]
        public new IObservable<IReactivePropertyChangedEventArgs<IReactiveObject>> Changed => base.Changed;

        [YamlIgnore]
        public new IObservable<Exception> ThrownExceptions => base.ThrownExceptions;
    }
}