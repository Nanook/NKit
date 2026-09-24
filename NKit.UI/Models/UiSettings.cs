using Newtonsoft.Json;
using NKit.Ui.Services;
using ReactiveUI;
using System;
using System.Collections.Generic;
using YamlDotNet.Serialization;

namespace NKit.Ui.Models
{
    [JsonObject(MemberSerialization.OptIn)]
    public class UiSettings : ReactiveObject
    {
        public static EventHandler RowFiltersChangedEvent;
        public static EventHandler WrapTextChangedEvent;

        public static UiSettings GetDefaultSettings()
        {
            return new UiSettings()
            {
                ShowQueued = true,
                ShowSkipped = true,
                ShowProcessing = true,
                ShowFailed = true,
                ShowCompleted = true,
                ShowCancelled = true,
                ConsoleOutputAutoScroll = true,
                ConsoleOutputBuffer = 100,
                ConsoleOutputWrapText = true,
                ReprocessCompletedFiles = false,
                ReprocessFailedFiles = false,
                ReprocessSkippedFiles = false,
                PersistFileQueue = true,
                ShowTooltips = true
            };
        }

        private bool _showQueued;
        [JsonProperty]
        public bool ShowQueued
        {
            get => _showQueued;
            set
            {
                if (_showQueued != value)
                {
                    _showQueued = value;
                    this.RaisePropertyChanged(nameof(ShowQueued));
                    RowFiltersChangedEvent?.Invoke(this, null);
                }
            }
        }

        private bool _showSkipped;
        [JsonProperty]
        public bool ShowSkipped
        {
            get => _showSkipped;
            set
            {
                if (_showSkipped != value)
                {
                    _showSkipped = value;
                    this.RaisePropertyChanged(nameof(ShowSkipped));
                    RowFiltersChangedEvent?.Invoke(this, null);
                }
            }
        }

        private bool _showProcessing;
        [JsonProperty]
        public bool ShowProcessing
        {
            get => _showProcessing;
            set
            {
                if (_showProcessing != value)
                {
                    _showProcessing = value;
                    this.RaisePropertyChanged(nameof(ShowProcessing));
                    RowFiltersChangedEvent?.Invoke(this, null);
                }
            }
        }

        private bool _showCompleted;
        [JsonProperty]
        public bool ShowCompleted
        {
            get => _showCompleted;
            set
            {
                if (_showCompleted != value)
                {
                    _showCompleted = value;
                    this.RaisePropertyChanged(nameof(ShowCompleted));
                    RowFiltersChangedEvent?.Invoke(this, null);

                }
            }
        }

        private bool _showFailed;
        [JsonProperty]
        public bool ShowFailed
        {
            get => _showFailed;
            set
            {
                if (_showFailed != value)
                {
                    _showFailed = value;
                    this.RaisePropertyChanged(nameof(ShowFailed));
                    RowFiltersChangedEvent?.Invoke(this, null);
                }
            }
        }

        private bool _showCancelled;
        [JsonProperty]
        public bool ShowCancelled
        {
            get => _showCancelled;
            set
            {
                if (_showCancelled != value)
                {
                    _showCancelled = value;
                    this.RaisePropertyChanged(nameof(ShowCancelled));
                    RowFiltersChangedEvent?.Invoke(this, null);
                }
            }
        }

        [JsonIgnore]
        [YamlIgnore]
        public Func<object, bool> RowFilter
        {
            get
            {
                List<ProcessingStatus> shownStatuses = new List<ProcessingStatus>();
                if (ShowQueued) { shownStatuses.Add(ProcessingStatus.Queued); }
                ;
                if (ShowSkipped) { shownStatuses.Add(ProcessingStatus.Skipped); }
                ;
                if (ShowProcessing) { shownStatuses.Add(ProcessingStatus.Processing); }
                ;
                if (ShowFailed) { shownStatuses.Add(ProcessingStatus.Failed); }
                ;
                if (ShowCompleted) { shownStatuses.Add(ProcessingStatus.Completed); }
                ;
                if (ShowCancelled) { shownStatuses.Add(ProcessingStatus.Cancelled); }
                ;

                return value => shownStatuses.Contains(((SourceFileRecord)value).ProcessingStatus);
            }
        }

        private int _consoleOutputBuffer;
        [JsonProperty]
        public int ConsoleOutputBuffer
        {
            get => _consoleOutputBuffer;
            set
            {
                if (_consoleOutputBuffer != value)
                {
                    _consoleOutputBuffer = value;
                    this.RaisePropertyChanged(nameof(ConsoleOutputBuffer));
                }
            }
        }

        private bool _consoleOutputAutoScroll;
        [JsonProperty]
        public bool ConsoleOutputAutoScroll
        {
            get => _consoleOutputAutoScroll;
            set
            {
                if (_consoleOutputAutoScroll != value)
                {
                    _consoleOutputAutoScroll = value;
                    this.RaisePropertyChanged(nameof(ConsoleOutputAutoScroll));
                }

                if (value)
                {
                    ConsoleOutput.ConsoleTextChangedEvent?.Invoke(this, null);
                }
            }
        }

        private bool _consoleOutputWrapText;
        [JsonProperty]
        public bool ConsoleOutputWrapText
        {
            get => _consoleOutputWrapText;
            set
            {
                if (value)
                {
                    ConsoleOutputWrapMode = "Wrap";
                    ConsoleOutputHorizontalScrollbarVisibility = "Disabled";
                }
                else
                {
                    ConsoleOutputWrapMode = "NoWrap";
                    ConsoleOutputHorizontalScrollbarVisibility = "Visible";
                }

                _consoleOutputWrapText = value;
                this.RaisePropertyChanged(nameof(ConsoleOutputWrapText));

                WrapTextChangedEvent?.Invoke(this, null);
            }
        }

        private string _consoleOutputWrapMode;
        public string ConsoleOutputWrapMode
        {
            get => _consoleOutputWrapMode;
            set
            {
                _consoleOutputWrapMode = value;
                this.RaisePropertyChanged(nameof(ConsoleOutputWrapMode));
            }
        }

        private string _consoleOutputHorizontalScrollbarVisibility;

        public string ConsoleOutputHorizontalScrollbarVisibility
        {
            get => _consoleOutputHorizontalScrollbarVisibility;
            set => this.RaiseAndSetIfChanged(ref _consoleOutputHorizontalScrollbarVisibility, value);
        }

        private bool _isBusy;

        public bool IsBusy
        {
            get => _isBusy; set => this.RaiseAndSetIfChanged(ref _isBusy, value);
        }

        private bool _reprocessCompletedFiles;
        [JsonProperty]
        public bool ReprocessCompletedFiles
        {
            get => _reprocessCompletedFiles; set => this.RaiseAndSetIfChanged(ref _reprocessCompletedFiles, value);
        }

        private bool _reprocessSkippedFiles;
        [JsonProperty]
        public bool ReprocessSkippedFiles
        {
            get => _reprocessSkippedFiles; set => this.RaiseAndSetIfChanged(ref _reprocessSkippedFiles, value);
        }

        private bool _reprocessFailedFiles;
        [JsonProperty]
        public bool ReprocessFailedFiles
        {
            get => _reprocessFailedFiles; set => this.RaiseAndSetIfChanged(ref _reprocessFailedFiles, value);
        }

        [YamlIgnore]
        public new IObservable<IReactivePropertyChangedEventArgs<IReactiveObject>> Changing => base.Changing;

        [YamlIgnore]
        public new IObservable<IReactivePropertyChangedEventArgs<IReactiveObject>> Changed => base.Changed;

        [YamlIgnore]
        public new IObservable<Exception> ThrownExceptions => base.ThrownExceptions;

        private bool _persistFileQueue;
        [JsonProperty]
        public bool PersistFileQueue
        {
            get => _persistFileQueue; set => this.RaiseAndSetIfChanged(ref _persistFileQueue, value);
        }

        private bool _showTooltips;
        [JsonProperty]
        public bool ShowTooltips
        {
            get => _showTooltips;
            set
            {
                if (_showTooltips != value)
                {
                    _showTooltips = value;
                    this.RaisePropertyChanged(nameof(ShowTooltips));
                }
            }
        }
    }
}