using NKit.Ui.Models;
using ReactiveUI;
using Splat;
using System;
using System.Collections.Generic;
using System.Linq;
using LogLevel = Nanook.NKit.LogLevel;

namespace NKit.Ui.Services
{
    public class ConsoleOutput : ReactiveObject
    {
        public static EventHandler ConsoleTextChangedEvent;
        private readonly string[] AppendChars = new string[] { ".", "1", "2", "3", "4", "5", "6", "7", "8", "9", "10" };
        private List<string> _allLines;
        private List<string> _fileLines;

        // Hard ceiling on the backing line lists. The UI only ever DISPLAYS the last
        // UiSettings.ConsoleOutputBuffer lines (see Messages), but Append/AppendLine used to grow
        // these lists forever — every progress tick and log line from every processed image was
        // retained for the whole session. Over a large run that is an unbounded climb (the UI-only
        // memory leak). Keep generous headroom over the display buffer so scroll-back is unaffected,
        // then trim the oldest lines so the backing store is bounded.
        private const int MinBackingCap = 5000;
        private int backingCap()
        {
            int display = UiSettings?.ConsoleOutputBuffer ?? 0;
            int cap = display * 4;
            return cap > MinBackingCap ? cap : MinBackingCap;
        }

        private void trimBacking()
        {
            int cap = backingCap();
            if (_allLines.Count > cap)
                _allLines.RemoveRange(0, _allLines.Count - cap);
            if (_fileLines.Count > cap)
                _fileLines.RemoveRange(0, _fileLines.Count - cap);
        }

        public UiSettings UiSettings { get; set; }

        public ConsoleOutput()
        {
            _allLines = new List<string>();
            _fileLines = new List<string>();

            ISettingsStore settingsStore = Locator.Current.GetService<ISettingsStore>();
            UiSettings = settingsStore.UiSettings;

            UiSettings.WrapTextChangedEvent += OutputMessageToForceTextWrapChange;

            this.Append(ConsoleOutputWelcomeText);
        }

        public string Messages
        {
            get
            {
                _allLines.RemoveAll(x => string.IsNullOrWhiteSpace(x));
                return string.Join(Environment.NewLine, _allLines.TakeLast(UiSettings.ConsoleOutputBuffer));
            }
        }

        private void OutputMessageToForceTextWrapChange(object sender, EventArgs e)
        {
            if (UiSettings.ConsoleOutputWrapText)
            {
                AppendLine("=== Text Wrap Enabled ===");
            }
            else
            {
                AppendLine("=== Text Wrap Disabled ===");
            }

            RemoveLine();

            //if (!UiSettings.IsBusy)
            this.RaisePropertyChanged(nameof(Messages));
        }

        public void Append(string message)
        {
            List<string> lines = message.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();

            if (lines[0].Trim().StartsWith("~") || AppendChars.Contains(lines[0]))
            {
                _allLines[^1] = string.Concat(_allLines[^1], lines[0]);
                _fileLines[^1] = string.Concat(_fileLines[^1], lines[0]);
            }
            else
            {
                _allLines.AddRange(lines);
                _fileLines.AddRange(lines);
            }

            trimBacking();

            this.RaisePropertyChanged(nameof(Messages));

            if (UiSettings.ConsoleOutputAutoScroll)
                ConsoleTextChangedEvent?.Invoke(this, null);
        }

        private void RemoveLine()
        {
            _allLines.RemoveAt(_allLines.Count - 1);
            _fileLines.RemoveAt(_fileLines.Count - 1);
        }

        public void Append(string message, LogLevel logLevel) => Append(message);

        public void AppendLine(string message)
        {
            _allLines.Add(message);
            _fileLines.Add(message);
            trimBacking();
            this.RaisePropertyChanged(nameof(Messages));

            if (UiSettings.ConsoleOutputAutoScroll)
                ConsoleTextChangedEvent?.Invoke(this, null);
        }

        public void AppendLine(string message, LogLevel logLevel) => AppendLine(message);

        internal void Clear()
        {
            _allLines = new List<string>();
            _fileLines = new List<string>();

            this.Append(ConsoleOutputWelcomeText);
        }

        private bool _hasFocus;

        public bool HasFocus
        {
            get => _hasFocus;
            set
            {
                _hasFocus = value;
                this.RaisePropertyChanged(nameof(HasFocus));
            }
        }

        private bool _hasUnviewed;

        public bool HasUnviewed
        {
            get => _hasUnviewed;
            set
            {
                _hasUnviewed = value;
                this.RaisePropertyChanged(nameof(HasUnviewed));
            }
        }

        public IList<string> FileMessages
        {
            get
            {
                List<string> fileLines = _fileLines.ToList();
                fileLines.RemoveAll(x => string.IsNullOrWhiteSpace(x));
                _allLines.Add(string.Empty);
                _fileLines.Clear();
                return fileLines;
            }
        }

        //private string _caretIndex;
        //public string CaretIndex
        //{
        //    get => _caretIndex;
        //    set => this.RaiseAndSetIfChanged(ref _caretIndex, value);
        //}

        private string ConsoleOutputWelcomeText =
@"        .                  __        __      __     __           .         
 .            .     .  ___/ |__ .___/ | ___ /  |___/ |_____ .         .       .
     .   __.-.__       \__     \ \__  |/   \\___/\_        \_   __.-.__    
       _/       \_    . /   |   \ ./  |    / ____  \_   ____/ _/       \_  .
 .   _/   _    _  \    /    |    \/       /_/    \  /   \ .  /  _    _   \_ 
  . /    / \  / \  |. /     |     \   |     \     \/     \  |  / \  / \    \   .
   /     \_/  \_/  | /      |      \_ |      \     \_     \ |  \_/  \_/     \
. /               /_/       |_______/ |       \_    /      \_\               \ 
 /_.-._  _._  _  / \        |  \______|        /___/    ____/ \  _  _._  _.-._\  
/------\/---\/-\/---\_______|---------|_______/--\_____|-------\/-\/---\/------\
                   /========================================\  
         -  -- ---(  Multi-platform toolkit for video games  )--- --  -
                   \========================================/
";

    }
}