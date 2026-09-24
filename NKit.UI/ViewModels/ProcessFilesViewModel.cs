using Avalonia.Threading;
using Nanook.NKit;
using NKit.Ui.Models;
using NKit.Ui.Services;
using ReactiveUI;
using ReactiveUI.Primitives;
using Splat;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LogLevel = Nanook.NKit.LogLevel;

namespace NKit.Ui.ViewModels
{
    public class ProcessFilesViewModel : ViewModelBase
    {
        public ObservableCollection<SourceFileRecord> FileQueue { get; set; }
        public ObservableCollection<SourceFileRecord> FileQueueFiltered { get; set; }
        private CancellationTokenSource _cancelSource;
        private CancellationToken _cancelToken;
        private Log _log;
        public NKitSettings Settings { get; set; }
        public ISettingsStore SettingsStore { get; set; }
        public ConsoleOutput ConsoleOutput { get; set; }

        public ProcessFilesViewModel()
        {
            Settings = Locator.Current.GetService<NKitSettings>();
            SettingsStore = Locator.Current.GetService<ISettingsStore>();
            ConsoleOutput = Locator.Current.GetService<ConsoleOutput>();
            FileQueue = Locator.Current.GetService<ObservableCollection<SourceFileRecord>>();
            FileQueueFiltered = new ObservableCollection<SourceFileRecord>(Array.Empty<SourceFileRecord>());

            UiSettings.RowFiltersChangedEvent += RefreshRowFilter;
            NKitService.ProcessingProgressChangedEvent += ProcessingProgressChanged;
            IsBusy = false;
            DisplayProcessingStats = StatsProgressPercentage > 0;
            _log = new Log();
            RefreshRowFilter(null, null);
        }

        /// <summary>
        /// Start processing only a subset of files. If forceReprocess is true, reset statuses so
        /// they will be re-queued regardless of UI reprocess settings.
        /// </summary>
        public async Task StartProcessingSubset(IEnumerable<SourceFileRecord> subset, bool forceReprocess)
        {
            if (subset == null)
                return;

            List<SourceFileRecord> files = subset.ToList();
            if (!files.Any())
                return;

            ConsoleOutput.Clear();

            _cancelSource = new CancellationTokenSource();
            _cancelToken = _cancelSource.Token;

            IsBusy = true;
            DisplayProcessingStats = true;
            try
            {
                if (forceReprocess)
                {
                    foreach (SourceFileRecord file in files)
                    {
                        file.ProcessingStatus = ProcessingStatus.Queued;
                        file.Progress = 0;
                        file.Result = null;
                        file.VerifyResultMessage = string.Empty;
                        file.ResetProcessingMessages();
                    }
                }

                // Build a subset collection that shares indices with FileQueue so that
                // any item replacements during processing (persisted queue re-scan) 
                // update the main bound collection directly.
                ObservableCollection<SourceFileRecord> subsetQueue = new ObservableCollection<SourceFileRecord>(files);
                subsetQueue.CollectionChanged += (s, e) =>
                {
                    // When NKitService.Run replaces an item, sync it back to FileQueue. This handler
                    // runs on the BACKGROUND processing thread (Task.Run below), but FileQueue is a
                    // UI-bound ObservableCollection. Mutating it off the UI thread races with the
                    // DataGrid/code-behind enumerating FileQueue and throws "Collection was modified;
                    // enumeration operation may not execute". Marshal the mutation to the UI thread.
                    if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Replace)
                    {
                        List<SourceFileRecord> oldItems = e.OldItems?.OfType<SourceFileRecord>().ToList() ?? new List<SourceFileRecord>();
                        SourceFileRecord newItem = e.NewItems?.OfType<SourceFileRecord>().FirstOrDefault();
                        if (newItem == null || oldItems.Count == 0)
                            return;

                        void apply()
                        {
                            foreach (SourceFileRecord oldItem in oldItems)
                            {
                                int mainIdx = FileQueue.IndexOf(oldItem);
                                if (mainIdx >= 0)
                                    FileQueue[mainIdx] = newItem;
                            }
                        }

                        if (Dispatcher.UIThread.CheckAccess())
                            apply();
                        else
                            Dispatcher.UIThread.Post(apply);
                    }
                };

                await Task.Run(() => NKitService.Run(subsetQueue, _cancelToken));
            }
            catch (Exception)
            {
                // ignore - logging already handled in service
            }
            finally
            {
                IsBusy = false;
                _cancelSource.Dispose();
            }
        }

        public void AddFiles(string[] filepaths)
        {
            _log.Initialise(Settings.ConsoleLevel, Settings.LogOutLevel, AppendOutputMessage, null);

            IsBusy = true;

            IEnumerable<SourceFile> images = SourceFiles.Scan(filepaths, true, true, true, _log, null);

            foreach (SourceFile image in images)
            {
                if (FileQueue.Any(x => x.SourceFile is null && x.Name == image.Name))
                {
                    AppendOutputMessage($"File may already be added, please remove '{image.Name}' from File Queue and try re-adding.{Environment.NewLine}", LogLevel.Info);
                }
                else if (FileQueue.Any(x => x.SourceFile?.ToString() == image.ToString()))
                {
                    AppendOutputMessage($"File already added: '{image.ToString()}'.{Environment.NewLine}", LogLevel.Info);
                }
                else
                {
                    FileQueue.Add(new SourceFileRecord(image));
                }
            }

            FileQueue = new ObservableCollection<SourceFileRecord>(FileQueue.OrderBy(x => x.Name));

            IEnumerable<SourceFileRecord> filtered = FileQueue.Where<SourceFileRecord>(SettingsStore.UiSettings.RowFilter);

            FileQueueFiltered.Clear();
            foreach (SourceFileRecord item in filtered)
                FileQueueFiltered.Add(item);

            HasFiles = FileQueue.Any();
            CheckIfAllFilesAreFiltered();

            this.RaisePropertyChanged(nameof(FileQueue));
            this.RaisePropertyChanged(nameof(FileQueueFiltered));
            ProcessingProgressChanged(this, null);

            IsBusy = false;
        }

        private void AppendOutputMessage(string message, LogLevel logLevel)
        {
            ConsoleOutput.Append(message);

            if (!ConsoleOutput.HasFocus)
            {
                ConsoleOutput.HasUnviewed = true;
            }
        }

        public async Task StartTask()
        {
            ConsoleOutput.Clear();

            _cancelSource = new CancellationTokenSource();
            _cancelToken = _cancelSource.Token;

            IsBusy = true;
            DisplayProcessingStats = true;
            try
            {
                await Task.Run(() => NKitService.Run(FileQueue, _cancelToken));
            }
            catch (Exception)
            {
                //AppendOutputMessage($"Processing failed: ", LogCategory.Process, Settings.LogOutLevel);
            }
            finally
            {
                IsBusy = false;
                _cancelSource.Dispose();
            }
        }

        public void CancelTask() => _cancelSource?.Cancel();

        public void ClearFiles()
        {
            HasFiles = false;
            DisplayProcessingStats = false;
            FileQueue.Clear();
            FileQueueFiltered.Clear();

            CheckIfAllFilesAreFiltered();

            this.RaisePropertyChanged(nameof(FileQueue));
            this.RaisePropertyChanged(nameof(FileQueueFiltered));
            ProcessingProgressChanged(this, null);

            ConsoleOutput.Clear();

            ConsoleOutput.HasUnviewed = false;
        }

        internal void RemoveFile(SourceFileRecord file)
        {
            FileQueue.Remove(file);
            FileQueueFiltered.Remove(file);

            CheckIfAllFilesAreFiltered();
            this.RaisePropertyChanged(nameof(FileQueue));
            this.RaisePropertyChanged(nameof(FileQueueFiltered));
            ProcessingProgressChanged(this, null);
        }

        public bool IsBusy
        {
            get => SettingsStore.UiSettings.IsBusy;
            set
            {
                SettingsStore.UiSettings.IsBusy = value;
                this.RaisePropertyChanged(nameof(IsBusy));
            }
        }

        private bool _hasFiles;
        public bool HasFiles
        {
            get => _hasFiles;
            set
            {
                _hasFiles = value;
                this.RaisePropertyChanged(nameof(HasFiles));
            }
        }

        public string GoButtonText => StatsProgressPercentage > 0 && StatsProgressPercentage < 100 ? "Resume" : "Process";

        private bool _displayProcessingStats;
        public bool DisplayProcessingStats
        {
            get => _displayProcessingStats;
            set
            {
                _displayProcessingStats = value;
                this.RaisePropertyChanged(nameof(DisplayProcessingStats));
            }
        }

        private bool _allRowsFilteredOut;
        public bool AllRowsFilteredOut
        {
            get => _allRowsFilteredOut;
            set
            {
                _allRowsFilteredOut = value;
                this.RaisePropertyChanged(nameof(AllRowsFilteredOut));
            }
        }

        private void RefreshRowFilter(object sender, EventArgs e)
        {
            Dispatcher.UIThread.Post(() =>
            {
                IEnumerable<SourceFileRecord> filtered = FileQueue.Where<SourceFileRecord>(SettingsStore.UiSettings.RowFilter);
                FileQueueFiltered.Clear();
                foreach (SourceFileRecord item in filtered)
                    FileQueueFiltered.Add(item);

                CheckIfAllFilesAreFiltered();
            });

            this.RaisePropertyChanged(nameof(FileQueueFiltered));
        }

        private void CheckIfAllFilesAreFiltered()
        {
            if (FileQueueFiltered.Count > 0)
            {
                AllRowsFilteredOut = false;
                HasFiles = true;
            }
            if (FileQueue.Count > 0 && FileQueueFiltered.Count == 0)
            {
                AllRowsFilteredOut = true;
                HasFiles = true;
            }

            if (FileQueue.Count == 0 && FileQueueFiltered.Count == 0)
            {
                AllRowsFilteredOut = false;
                HasFiles = false;
            }

            this.RaisePropertyChanged(nameof(AllRowsFilteredOut));
            this.RaisePropertyChanged(nameof(HasFiles));
        }

        public string StatsTotal => $"{FileQueue.Count()}";

        public string StatsQueued => $"{FileQueue.Count(x => x.ProcessingStatus == ProcessingStatus.Queued)}";

        public string StatsProcessing => $"{FileQueue.Count(x => x.ProcessingStatus == ProcessingStatus.Processing)}";

        public string StatsSkipped => $"{FileQueue.Count(x => x.ProcessingStatus == ProcessingStatus.Skipped || x.ProcessingStatus == ProcessingStatus.AlreadyExists)}";

        public string StatsFailed => $"{FileQueue.Count(x => x.ProcessingStatus == ProcessingStatus.Failed)}";

        public string StatsCompleted => $"{FileQueue.Count(x => x.ProcessingStatus == ProcessingStatus.Completed)}";

        public float StatsProgressPercentage
        {
            get
            {
                ProcessingStatus[] processedStatuses = new ProcessingStatus[4] { ProcessingStatus.Failed, ProcessingStatus.Skipped, ProcessingStatus.Completed, ProcessingStatus.AlreadyExists };

                float processed = FileQueue.Count(x => processedStatuses.Contains(x.ProcessingStatus));
                float totalFiles = FileQueue.Count();

                float progressPercentage = totalFiles == 0 ? 0 : processed / totalFiles * 100;

                return progressPercentage;
            }
        }

        public string StatsProgress
        {
            get
            {
                int processing = FileQueue.Count(x => x.ProcessingStatus == ProcessingStatus.Processing);
                int queued = FileQueue.Count(x => x.ProcessingStatus == ProcessingStatus.Queued);
                int cancelled = FileQueue.Count(x => x.ProcessingStatus == ProcessingStatus.Cancelled);

                ProcessingStatus[] processedStatuses = new ProcessingStatus[4] { ProcessingStatus.Failed, ProcessingStatus.Skipped, ProcessingStatus.Completed, ProcessingStatus.AlreadyExists };
                int processed = FileQueue.Count(x => processedStatuses.Contains(x.ProcessingStatus));

                if (processing == 0 && processed == 0 && cancelled == 0)
                {
                    return $"{queued} queued";
                }

                int remaining = processing + queued + cancelled;
                int totalFiles = FileQueue.Count();

                return $"{StatsProgressPercentage:0.0}% ({processed} / {totalFiles} processed, {remaining} left)";
            }
        }

        public bool IsLinux => OperatingSystem.IsLinux();

        public void ProcessingProgressChanged(object sender, EventArgs e)
        {
            this.RaisePropertyChanged(nameof(StatsTotal));
            this.RaisePropertyChanged(nameof(StatsQueued));
            this.RaisePropertyChanged(nameof(StatsProcessing));
            this.RaisePropertyChanged(nameof(StatsSkipped));
            this.RaisePropertyChanged(nameof(StatsFailed));
            this.RaisePropertyChanged(nameof(StatsCompleted));
            this.RaisePropertyChanged(nameof(StatsProgress));
            this.RaisePropertyChanged(nameof(StatsProgressPercentage));
            this.RaisePropertyChanged(nameof(GoButtonText));
        }
    }
}