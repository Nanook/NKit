using Spectre.Console;
using Spectre.Console.Rendering;
using System;
using System.Text;

namespace Nanook.NKit.App
{
    /// <summary>
    /// Console sink for nkit that, in a dynamic (interactive + ANSI) terminal, pins a progress
    /// footer with core stats (percentage, elapsed, throughput) at the bottom while normal log
    /// lines scroll ABOVE it. The footer is a Spectre <see cref="LiveDisplay"/> region, so Spectre
    /// owns all cursor / wrap / scroll reconciliation — there is no hand-rolled escape handling.
    ///
    /// <para>
    /// Capability-gated. The dynamic footer is only used when the output is a real interactive
    /// terminal that supports ANSI. In EVERY other case — redirected, piped, a file, a CI log, or a
    /// terminal without ANSI — output falls back to the EXACT legacy behaviour: raw
    /// <see cref="Console.Write(string)"/>, so the ".1.2.3.4..." progress dots pass straight through
    /// unchanged (no core stats), and scripted runs / <c>Select-String</c> piping stay byte-for-byte
    /// identical.
    /// </para>
    ///
    /// <para>
    /// Usage in dynamic mode: wrap the processing run in <see cref="Run"/>. The work happens inside
    /// the live scope; log lines routed through the <see cref="Sink"/> are written above the footer
    /// via <c>AnsiConsole.Write</c>, and progress from NKitProcessor.ProgressEvent updates the
    /// footer via <see cref="ReportProgress"/>. <see cref="LiveDisplay"/> is not thread safe, so all
    /// console access is serialised through one lock.
    /// </para>
    /// </summary>
    internal sealed class LiveConsole
    {
        // Dynamic mode requires an interactive terminal AND ANSI support. Anything less → legacy dots.
        private readonly bool _dynamic;
        private readonly object _lock = new object();

        // The console's configured verbosity. Drives Info-summary-prefix handling: at Info (or less
        // verbose) the [Title]/[InParam]/[OutParam] prefixes are stripped from the display and the
        // line is coloured; at Detail/Trace they are left in place and not recoloured. Set once by
        // the host after settings are parsed; defaults to Info.
        private LogLevel _consoleLevel = LogLevel.Info;

        private LiveDisplayContext _ctx; // non-null only while inside Run's live scope

        private DateTime _stepStart;
        private float _pct;
        private int _step;
        private int _stepTotal;
        private string _stepName = "";
        private double _mibPerSec;

        // ── Wandering ghost (dynamic footer only, cosmetic) ─────────────────────────────
        // A little Pac-Man-style ghost that paces left/right on the line under the progress bar,
        // randomly reversing, and wrapping around the window edges (off one side → back the other).
        // stepGhost() is called on every footer refresh, but the ghost only advances when at least
        // GhostFrameMs milliseconds have elapsed since the last advance — preventing it from
        // becoming a blur when the engine refreshes the footer very frequently.
        // Never shown outside dynamic mode.
        private readonly Random _ghostRnd = new Random();
        private int _ghostCol = -1;       // current column; -1 = not yet initialised
        private int _ghostDir = 1;        // +1 = moving right, -1 = moving left
        private int _ghostTurn = 0;       // >0 = mid-turn frames remaining (eyes swing), pose upright
        private bool _ghostTurnEyesLeft;  // which upright eye pose to show during a turn
        private int _ghostSinceTurn = 0;  // steps travelled since the last turn
        private int _ghostRunTarget = 0;  // steps to wander this leg before turning (random, >= 10)
        private int _ghostTurnHold = 0;   // refreshes remaining before the current turn frame swaps
        private string[] _ghostLastPose;  // last rendered pose — returned unchanged when throttled
        private int _ghostLastTrack = -1; // track width used last frame — reset ghost on resize
        private int _lastWindowWidth = -1; // used to detect terminal resize inside the live scope
        private readonly System.Diagnostics.Stopwatch _ghostSw = System.Diagnostics.Stopwatch.StartNew();
        private const int GhostTurnHold = 4;   // hold each turn/eye frame this many advances (slower look)
        private const int GhostFrameMs  = 150; // minimum milliseconds between ghost advances

        // Latest engine telemetry (stage load + workers). Updated from NKitProcessor.StatsEvent.
        private EngineStats _stats;

        public LiveConsole()
        {
            _dynamic = AnsiConsole.Profile.Out.IsTerminal
                    && AnsiConsole.Profile.Capabilities.Ansi;
        }

        /// <summary>True when running in the rich, in-place footer mode.</summary>
        public bool IsDynamic => _dynamic;

        /// <summary>
        /// The console's configured verbosity. Set by the host once settings are parsed. Controls
        /// whether the Info-summary prefixes (<see cref="InfoPrefix"/>) are stripped and coloured
        /// (Info or less verbose) or left verbatim (Detail/Trace).
        /// </summary>
        public LogLevel ConsoleLevel
        {
            get => _consoleLevel;
            set => _consoleLevel = value;
        }

        /// <summary>The text-log sink to hand to AppSettings.GetLog / NKitProcessor.</summary>
        public Action<string, LogLevel> Sink => write;

        /// <summary>
        /// Run <paramref name="work"/> with the pinned progress footer active (dynamic mode). Log
        /// lines emitted through <see cref="Sink"/> during the work scroll above the footer. In
        /// non-dynamic mode this just invokes <paramref name="work"/> with no footer.
        /// </summary>
        public void Run(Action work)
        {
            if (!_dynamic)
            {
                work();
                return;
            }

            AnsiConsole.Live(buildFooter())
                .AutoClear(true)
                .Overflow(VerticalOverflow.Visible)
                .Start(ctx =>
                {
                    lock (_lock)
                        _ctx = ctx;
                    try
                    {
                        work();
                    }
                    finally
                    {
                        lock (_lock)
                            _ctx = null;
                    }
                });
        }

        /// <summary>Begin a new step's footer. Resets the elapsed timer.</summary>
        public void BeginStep(int step, int stepTotal, string stepName)
        {
            if (!_dynamic)
                return;
            lock (_lock)
            {
                _step = step;
                _stepTotal = stepTotal;
                _stepName = stepName ?? "";
                _pct = 0;
                _mibPerSec = 0;
                _stepStart = DateTime.UtcNow;
                refresh();
            }
        }

        /// <summary>Feed a fresh percentage (0..1) and throughput from NKitProcessor.ProgressEvent.</summary>
        public void ReportProgress(int step, int stepTotal, float pct, double mibPerSec)
        {
            if (!_dynamic)
                return;
            lock (_lock)
            {
                _step = step;
                _stepTotal = stepTotal;
                _pct = pct;
                if (mibPerSec > 0)
                    _mibPerSec = mibPerSec;
                refresh();
            }
        }

        /// <summary>Feed pipeline telemetry (stage load, workers) from NKitProcessor.StatsEvent.</summary>
        public void ReportStats(EngineStats stats)
        {
            if (!_dynamic || stats == null)
                return;
            lock (_lock)
            {
                _stats = stats;
                refresh();
            }
        }

        private void write(string message, LogLevel level)
        {
            if (!_dynamic)
            {
                // Legacy path — no ANSI colour (redirected / piped / non-ANSI terminal), but the
                // Info-summary prefixes must STILL be removed at Info verbosity so redirected and
                // scripted output does not show the raw "[Title]"/"[InParam]"/"[OutParam]" tokens.
                // The ".1.2.3..." progress dots and every other line pass through unchanged.
                Console.Write(stripInfoPrefixPlain(message));
                return;
            }

            lock (_lock)
            {
                // NOTE: in dynamic mode Log.Progress never emits the ".1.2.3..." dot stream (it is
                // gated on !Dynamic), so there is nothing to suppress here — the footer owns the
                // live progress.

                // Emit the log text ABOVE the footer. The footer must have ZERO height at the
                // instant the line is committed — otherwise Spectre repaints the still-populated
                // footer onto the SAME physical line as the text, colliding on screen
                // ("Caching 2723MiB... - 5.1% - ... Stats [Read 0%]"). So blank the live target,
                // commit the line into the now-empty region, then restore the footer below it —
                // the exact blank-then-write-then-restore sequence CompleteStep already proves. Do
                // NOT zero _pct here (unlike CompleteStep): the footer must keep the current
                // progress when it is restored.
                if (_ctx != null)
                {
                    _ctx.UpdateTarget(new Text(string.Empty));
                    _ctx.Refresh();
                    // Colour only the leading [..] tags (percent + scope); the message stays plain.
                    // Errors and exceptions are shown entirely in red so they stand out.
                    AnsiConsole.MarkupLine(colorizeTags(message.TrimEnd('\r', '\n'), level, _consoleLevel));
                    refresh(); // restore the footer (buildFooter from retained _pct/_stats)
                    _ctx.Refresh();
                }
                else
                {
                    // Outside the live scope (before/after a run) — no footer to protect, but still
                    // colour the tags so the startup banner / results match the in-run output.
                    AnsiConsole.MarkupLine(colorizeTags(message.TrimEnd('\r', '\n'), level, _consoleLevel));
                }
            }
        }

        // Colour only the leading bracketed tags of a log line, leaving the message text plain.
        // Input shape: [optional leading spaces][ "[NN%]" ][ "[Scope:Cat]" ] message...
        //   [NN%]      -> bold white
        //   stages:     [In]=aqua, [Pre]=yellow, [Out]=blue, [Proc#N]=fuchsia
        //   categories: [Core]=purple; [Input]/[Params]/[Config]/[Results]/[Progress]/[NKit]=gold
        // Error-level lines are rendered ENTIRELY red (tags + message) so failures stand out.
        // Returns a Spectre markup string (the message portion is Markup.Escape'd, uncoloured).
        private static string colorizeTags(string line, LogLevel level = LogLevel.Info,
            LogLevel consoleLevel = LogLevel.Info)
        {
            if (string.IsNullOrEmpty(line))
                return string.Empty;

            // Error lines: whole line red (reserved colour), no per-tag colouring.
            if (level == LogLevel.Error)
                return "[red]" + Markup.Escape(line) + "[/]";

            // Info-summary prefixes ([Title]/[InParam]/[OutParam]) baked into the message by the
            // library. These are a CLASSIFICATION token, not a scope. Contract:
            //   - At Info (or less verbose) verbosity: strip the prefix from the display and colour
            //     the line by which prefix it was.
            //   - At Detail/Trace verbosity: leave the prefix verbatim and do not recolour, so the
            //     raw tagged text is visible alongside the stage tags. (Falls through to passthrough.)
            //   - A line without one of these exact prefixes: passthrough, uncoloured.
            if ((int)consoleLevel <= (int)LogLevel.Info)
            {
                string summary = colorizeInfoSummary(line);
                if (summary != null)
                    return summary;
                // No recognised Info prefix → leave the line exactly as-is, uncoloured.
                return Markup.Escape(line);
            }

            int i = 0;
            StringBuilder sb = new System.Text.StringBuilder(line.Length + 32);

            // Preserve any leading indentation (e.g. the Trace "  > " prefix) verbatim.
            while (i < line.Length && line[i] == ' ') { sb.Append(' '); i++; }

            // Consume consecutive leading "[...]" tags, colouring each. Stop at the first token that
            // is not a bracket tag — everything from there on is the message (escaped, uncoloured).
            while (i < line.Length && line[i] == '[')
            {
                int close = line.IndexOf(']', i);
                if (close < 0) break; // unterminated — treat the rest as message
                string inner = line.Substring(i + 1, close - i - 1); // tag text without the brackets
                string colour = tagColour(inner);
                // In Spectre markup, literal brackets are escaped by doubling ("[[" and "]]"), and the
                // inner text is escaped for any stray markup. A null colour means "emit the tag with no
                // colour span" (e.g. the "[Task/System]" section title, which is not a scope).
                if (colour != null)
                {
                    sb.Append('[').Append(colour).Append("]");        // open colour span
                    sb.Append("[[").Append(Markup.Escape(inner)).Append("]]"); // literal [inner]
                    sb.Append("[/]");                                  // close colour span
                }
                else
                {
                    sb.Append("[[").Append(Markup.Escape(inner)).Append("]]"); // literal [inner], uncoloured
                }
                i = close + 1;
                // Skip a single spacer between tags so it isn't coloured.
                if (i < line.Length && line[i] == ' ') { sb.Append(' '); i++; }
            }

            // Remainder = the message, plain (escaped so any [] in it are literal).
            if (i < line.Length)
                sb.Append(Markup.Escape(line.Substring(i)));

            return sb.ToString();
        }

        // Plain-text (no colour) counterpart of the Info-summary handling, for the non-dynamic path.
        // At Info verbosity a recognised leading prefix is removed (keeping any trailing newline);
        // otherwise the message is returned unchanged. At Detail/Trace the prefix is left in place.
        private string stripInfoPrefixPlain(string message)
        {
            if ((int)_consoleLevel > (int)LogLevel.Info || string.IsNullOrEmpty(message))
                return message;

            // Preserve the trailing newline the legacy path relies on.
            string newline = "";
            string body = message;
            if (body.EndsWith("\r\n")) { newline = "\r\n"; body = body.Substring(0, body.Length - 2); }
            else if (body.EndsWith("\n")) { newline = "\n"; body = body.Substring(0, body.Length - 1); }

            if (tryStripPrefix(body, InfoPrefix.Title, out string t)) return t + newline;
            if (tryStripPrefix(body, InfoPrefix.InParam, out string i)) return i + newline;
            if (tryStripPrefix(body, InfoPrefix.OutParam, out string o)) return o + newline;
            return message;
        }

        // Info-summary colouring. Recognise one of the exact InfoPrefix tokens at the very START of
        // the line, strip it (plus the single following space), and colour the remainder. Returns
        // null when the line has no recognised prefix (caller then passes it through). Colours are
        // neutral placeholders for now (tuned later); green/red stay reserved for success/error.
        private static string colorizeInfoSummary(string line)
        {
            if (tryStripPrefix(line, InfoPrefix.Title, out string titleBody))
                return colorizeTitle(titleBody);
            // Input params: "Label : Value" — label white, value cyan.
            if (tryStripPrefix(line, InfoPrefix.InParam, out string inBody))
                return colorizeKeyValue(inBody, White, "cyan");
            // Output params: "Label : Value" — label white, value purple. Exception: on the Verify
            // result line only the outcome WORD is recoloured (VerifySuccess = green, any other
            // Verify* word = red); the rest of the value stays purple like every other OutParam.
            if (tryStripPrefix(line, InfoPrefix.OutParam, out string outBody))
                return colorizeKeyValue(outBody, White, Pink, verifyOutcomeHighlight: true);
            return null;
        }

        // If the value begins with a "Verify…" outcome word, return the markup for JUST that word
        // (green for VerifySuccess, red otherwise). Returns null when the value is not a verify
        // outcome, so the caller colours the whole value normally.
        private static string verifyWordMarkup(string value, out int wordLen)
        {
            wordLen = 0;
            if (!value.StartsWith("Verify", StringComparison.Ordinal))
                return null;
            // The outcome word runs to the first space (or end): "VerifySuccess", "VerifyFailed"…
            int end = value.IndexOf(' ');
            string word = end < 0 ? value : value.Substring(0, end);
            wordLen = word.Length;
            string colour = word.Equals("VerifySuccess", StringComparison.Ordinal) ? "green" : "red";
            return string.Concat("[", colour, "]", Markup.Escape(word), "[/]");
        }

        // "White" summary text uses BRIGHT white. Spectre's bare "white" maps to the terminal's
        // standard (dimmer) white; "bold white" renders as genuine bright white. Single constants so
        // the shades are easy to tune in one place.
        private const string White = "bold white";
        // Bright purple (was "pink"). Spectre named bright purple.
        private const string Pink = "mediumpurple1";
        // Title parts: the "[Task/System]" bracket yellow, the name green.
        private const string TitleTaskSystemColour = "yellow";
        private const string TitleNameColour = "green";

        // Title: "[Task/System]  Name  [X/Y]" → "[Task/System]" yellow, the name green, and the
        // trailing "[X/Y]" counter white. The literal brackets are kept. Falls back to green if the
        // expected leading "[...]" is not present.
        private static string colorizeTitle(string body)
        {
            if (body.Length != 0 && body[0] == '[')
            {
                int close = body.IndexOf(']');
                if (close > 0)
                {
                    string taskSystem = body.Substring(1, close - 1); // between the first brackets
                    string rest = body.Substring(close + 1);          // "  Name  [X/Y]"
                    StringBuilder sb = new System.Text.StringBuilder(body.Length + 48);
                    sb.Append("[").Append(TitleTaskSystemColour).Append("][[")
                      .Append(Markup.Escape(taskSystem)).Append("]][/]");

                    // Split the remainder into the name and the trailing "[X/Y]" counter (if any).
                    // The counter is the last bracketed token; everything before it is the name.
                    int counter = rest.LastIndexOf('[');
                    if (counter >= 0 && rest.IndexOf(']', counter) > counter)
                    {
                        string name = rest.Substring(0, counter);   // "  Name  "
                        string count = rest.Substring(counter);     // "[X/Y]"
                        if (name.Length != 0)
                            sb.Append("[").Append(TitleNameColour).Append("]")
                              .Append(Markup.Escape(name)).Append("[/]");
                        sb.Append("[").Append(White).Append("]")
                          .Append(Markup.Escape(count)).Append("[/]");
                    }
                    else if (rest.Length != 0)
                        sb.Append("[").Append(TitleNameColour).Append("]")
                          .Append(Markup.Escape(rest)).Append("[/]");
                    return sb.ToString();
                }
            }
            return "[" + TitleNameColour + "]" + Markup.Escape(body) + "[/]";
        }

        // "Label    : Value" → label in labelColour, value in valueColour, and the ":" separator
        // (plus the padding/spaces around it) left UNCOLOURED. Splits on the FIRST ":" so values
        // containing ":" (paths, hex) stay intact. If there is no ":", the whole line is labelColour.
        private static string colorizeKeyValue(string body, string labelColour, string valueColour,
            bool verifyOutcomeHighlight = false)
        {
            int colon = body.IndexOf(':');
            if (colon < 0)
                return string.Concat("[", labelColour, "]", Markup.Escape(body), "[/]");

            // Label = text up to the trailing padding before the colon; the padding spaces, the ":",
            // and the following space form the uncoloured separator; the value is the remainder.
            int labelEnd = colon;
            while (labelEnd > 0 && body[labelEnd - 1] == ' ') labelEnd--;
            int valueStart = colon + 1;
            while (valueStart < body.Length && body[valueStart] == ' ') valueStart++;

            string label = body.Substring(0, labelEnd);              // "InFile"
            string separator = body.Substring(labelEnd, valueStart - labelEnd); // "    : "
            string value = body.Substring(valueStart);               // "game.iso"

            StringBuilder sb = new System.Text.StringBuilder(body.Length + 48);
            if (label.Length != 0)
                sb.Append("[").Append(labelColour).Append("]").Append(Markup.Escape(label)).Append("[/]");
            sb.Append(Markup.Escape(separator)); // ':' and surrounding spaces — uncoloured
            if (value.Length != 0)
            {
                // On the Verify line, colour ONLY the leading outcome word (green/red); the rest of
                // the value stays in valueColour like any other OutParam.
                int wLen = 0;
                string verifyWord = verifyOutcomeHighlight ? verifyWordMarkup(value, out wLen) : null;
                if (verifyWord != null)
                {
                    sb.Append(verifyWord);
                    string tail = value.Substring(wLen); // " (InChecksums [...])"
                    if (tail.Length != 0)
                        sb.Append("[").Append(valueColour).Append("]").Append(Markup.Escape(tail)).Append("[/]");
                }
                else
                    sb.Append("[").Append(valueColour).Append("]").Append(Markup.Escape(value)).Append("[/]");
            }
            return sb.ToString();
        }

        // Exact prefix match at the very start of the line. On success returns the body with the
        // prefix token and a single following space removed.
        private static bool tryStripPrefix(string line, string prefix, out string body)
        {
            if (line.StartsWith(prefix, StringComparison.Ordinal))
            {
                int start = prefix.Length;
                if (start < line.Length && line[start] == ' ')
                    start++;
                body = line.Substring(start);
                return true;
            }
            body = null;
            return false;
        }

        private static string tagColour(string inner)
        {
            // Percent tag: "12%".
            if (inner.Length != 0 && inner[inner.Length - 1] == '%')
                return "bold white";

            // Section title "[Task/System]" (e.g. "[Scan/GameCube]") is NOT a scope/component tag —
            // it only happens to use brackets. Detect it by the '/' separator (no real scope/tag
            // contains one) and leave it UNCOLOURED so it doesn't render like a gold scope. Colour
            // scheme for the section title is TBD.
            if (inner.IndexOf('/') >= 0)
                return null;

            // Tags are now EITHER a pipeline stage ([In]/[Pre]/[Proc#N]/[Out]) OR a category
            // ([Input]/[Params]/[Config]/[Core]/[Results]/[Progress]) — never combined. NB: green
            // and red are reserved (success / error), so neither is used here.

            // Pipeline STAGES — exact tokens ([In]/[Pre]/[Out]) or the Proc# prefix. Exact matches
            // so a CATEGORY like [Input] (which starts with "In") is NOT mistaken for the In stage.
            // All colours are standard ANSI names Spectre renders on any terminal; green/red reserved.
            if (inner.Equals("In", StringComparison.OrdinalIgnoreCase)) return "aqua";
            if (inner.Equals("Pre", StringComparison.OrdinalIgnoreCase)) return "yellow";
            if (inner.Equals("Out", StringComparison.OrdinalIgnoreCase)) return "blue";
            if (inner.StartsWith("Proc", StringComparison.OrdinalIgnoreCase)) return "fuchsia";

            // Cross-cutting CATEGORIES: [Core] is purple; all the rest (and [NKit] root) are gold.
            if (inner.Equals("Core", StringComparison.OrdinalIgnoreCase)) return "purple";
            return "gold3";
        }

        // Build the coloured, width-padded step label: "Step N/M: " white, the step TYPE yellow,
        // and the trailing pad (to width 24) white so columns still line up. Returns "" when there
        // is no step (matching the previous empty-label behaviour).
        private string buildStepLabelMarkup()
        {
            if (_stepTotal <= 0)
                return "";
            string prefix = $"Step {_step + 1}/{_stepTotal}: ";
            string type = _stepName ?? "";
            int width = 24;
            int used = prefix.Length + type.Length;
            string pad = used < width ? new string(' ', width - used) : "";
            return string.Concat(
                "[", White, "]", Markup.Escape(prefix), "[/]",
                "[yellow]", Markup.Escape(type), "[/]",
                "[", White, "]", pad, "[/]");
        }

        // Refresh the footer target. Caller must hold _lock.
        private void refresh()
        {
            if (_ctx == null)
                return;
            // On terminal resize, blank the live region first so Spectre resets its internal
            // line-count from zero. Without this, narrowing the window causes Spectre to
            // over-clear — it clears based on the old (shorter) line count and overwrites
            // previously committed log lines above the footer.
            handleResizeIfNeeded();
            _ctx.UpdateTarget(buildFooter());
        }

        // If the terminal width changed since the last refresh, blank the live target and
        // commit a refresh immediately so Spectre's internal region-height counter resets to
        // zero before we repopulate. Must be called inside _lock with _ctx != null.
        private void handleResizeIfNeeded()
        {
            int w = 0;
            try { w = Console.WindowWidth; } catch { }
            if (w <= 0) return;
            if (_lastWindowWidth < 0) { _lastWindowWidth = w; return; }
            if (w == _lastWindowWidth) return;

            _lastWindowWidth = w;
            // Blank the region so Spectre learns its height is now zero before we redraw.
            _ctx.UpdateTarget(new Text(string.Empty));
            _ctx.Refresh();
        }

        // The ghost poses (3 rows each). Column 0 = head, 1 = body(eyes), 2 = feet/sheet.
        // These are the user's four original poses: right-drift, upright(eyes-right),
        // upright(eyes-left), left-drift.
        // Each pose is 3 rows on a FIXED 6-column cell so the block never jitters. The per-row
        // offsets bake in the "lean": the drift poses have the head, body and feet staggered
        // (head furthest in the travel direction, feet trailing), the upright poses are square.
        private static readonly string[] GhostRight   = { "  .-. ", " / OO)", "/~~~/ " };
        private static readonly string[] GhostEyesR   = { " .-.  ", "| OO| ", "|~~~| " };
        private static readonly string[] GhostEyesL   = { " .-.  ", "|OO | ", "|~~~| " };
        private static readonly string[] GhostLeft    = { " .-.  ", "(OO \\ ", " \\~~~\\" };
        private const int GhostWidth = 6; // all pose rows are 6 chars wide

        // Track width for the ghost: the current window width, less the ghost's own width so it can
        // sit fully on-screen; small floor so it still works in a narrow terminal.
        private int ghostTrackWidth()
        {
            int w = 40;
            try { if (Console.WindowWidth > 0) w = Console.WindowWidth; } catch { }
            return Math.Max(GhostWidth + 2, w - GhostWidth);
        }

        // Advance the ghost one step and return its current 3-row pose. Called once per footer
        // refresh. Wraps around the window edges Pac-Man style; occasionally reverses direction, and a
        // reversal plays four upright "turn" frames (eyes swinging) so the about-face reads slowly.
        private string[] stepGhost()
        {
            int track = ghostTrackWidth();

            // If the terminal was resized, reset the ghost so it re-seeds within the new bounds.
            // This prevents the ghost from rendering off-screen or with a stale column position
            // after the window narrows.
            if (track != _ghostLastTrack && _ghostLastTrack >= 0)
            {
                _ghostCol = -1;
                _ghostLastPose = null;
            }
            _ghostLastTrack = track;

            if (_ghostCol < 0)
            {
                _ghostCol = _ghostRnd.Next(track);
                _ghostDir = _ghostRnd.Next(2) == 0 ? -1 : 1;
                _ghostRunTarget = _ghostRnd.Next(60) + 10;
                _ghostLastPose = null;
                _ghostSw.Restart();
            }

            // Throttle: if not enough time has elapsed, return the last pose unchanged.
            if (_ghostLastPose != null && _ghostSw.ElapsedMilliseconds < GhostFrameMs)
                return _ghostLastPose;
            _ghostSw.Restart();

            string[] pose;
            if (_ghostTurn > 0)
            {
                // Mid-turn: hold position and show an upright pose. Each eye frame LINGERS for
                // GhostTurnHold refreshes before swinging, so the "looking around" is slow and
                // deliberate rather than a frantic head-shake when progress refreshes rapidly.
                pose = _ghostTurnEyesLeft ? GhostEyesL : GhostEyesR;
                if (_ghostTurnHold > 0)
                {
                    _ghostTurnHold--; // still dwelling on this eye frame
                }
                else
                {
                    _ghostTurnEyesLeft = !_ghostTurnEyesLeft; // swing to the other side
                    _ghostTurn--;
                    _ghostTurnHold = GhostTurnHold;
                    if (_ghostTurn == 0 && _ghostRnd.Next(2) == 0)
                        _ghostDir = -_ghostDir; // 50/50: reverse, or carry on the same way after the look
                }
            }
            else
            {
                // Moving: he wanders a random distance (10..41 steps) before turning, so each leg is
                // a good long, unhurried wander with natural variety — never an erratic quick turn.
                if (_ghostSinceTurn >= _ghostRunTarget)
                {
                    _ghostTurn = 4;                       // four upright frames — a slow about-face
                    _ghostTurnEyesLeft = _ghostDir > 0;   // start eyes toward current travel
                    pose = _ghostTurnEyesLeft ? GhostEyesL : GhostEyesR;
                    _ghostTurnHold = GhostTurnHold;       // linger on this first eye frame
                    _ghostSinceTurn = 0;                  // reset the run counter for the next leg
                    _ghostRunTarget = _ghostRnd.Next(60) + 10; // pick a fresh wander distance
                }
                else
                {
                    _ghostCol += _ghostDir;
                    _ghostSinceTurn++;
                    // Wrap-around (screen tunnel): off the right edge → back on the left, and vice-versa.
                    if (_ghostCol >= track) _ghostCol = 0;
                    else if (_ghostCol < 0) _ghostCol = track - 1;
                    pose = _ghostDir > 0 ? GhostRight : GhostLeft;
                }
            }

            // Render each of the 3 rows as (leading spaces)(pose row), in the logo green.
            string indent = new string(' ', _ghostCol);
            string[] rows = new string[3];
            for (int r = 0; r < 3; r++)
                rows[r] = $"[green]{indent}{Markup.Escape(pose[r])}[/]";
            _ghostLastPose = rows;
            return rows;
        }

        /// <summary>
        /// Commit a one-line completion summary (100% + total elapsed + last stage load / workers)
        /// as a permanent LOG line ABOVE the footer, then clear the footer so nothing is left as a
        /// live/updatable region. Call this at step completion (ProgressEvent.IsComplete) from
        /// INSIDE the live scope. In non-dynamic mode it is a no-op — the legacy per-step "~Xm Ys"
        /// line already reports completion.
        /// </summary>
        public void CompleteStep(string completeMessage = null)
        {
            if (!_dynamic)
                return;
            lock (_lock)
            {
                // Blank the footer FIRST so the live region is empty, THEN commit the summary line.
                // Doing it the other way round lets Spectre repaint the (still-populated) stats
                // footer right after the committed line, colliding on screen ("[CRC ...][Read ...]").
                _pct = 0f;
                if (_ctx != null)
                {
                    _ctx.UpdateTarget(new Text(string.Empty));
                    _ctx.Refresh();
                }

                string summary = buildStatusMarkup(completed: true, completeMessage);
                _stats = null;
                AnsiConsole.MarkupLine(summary);
            }
        }

        // Build the footer renderable from the current progress + telemetry state. When nothing is
        // in progress the footer is empty so it occupies no visible line. While a step is running the
        // footer is the status line with a small wandering ghost pacing on the lines beneath it.
        private IRenderable buildFooter()
        {
            if (_pct <= 0f || _pct >= 1f)
            {
                _ghostCol = -1; // reset the ghost between steps so it re-seeds a fresh wander
                _ghostSinceTurn = 0;
                _ghostLastPose = null;
                _ghostLastTrack = -1;
                return new Text(string.Empty);
            }

            Markup status = new Markup(buildStatusMarkup(completed: false, null));
            string[] ghost = stepGhost();
            return new Rows(
                status,
                new Markup(ghost[0]),
                new Markup(ghost[1]),
                new Markup(ghost[2]));
        }

        // The single status line, shared by the live footer and the committed completion summary:
        //   in progress: Step 1/2: Convert - 42.5% - 20.1s - Stats [Read 3%] [Process 42% - 1/2 Workers] [Output 53%]
        //   completed:   Step 1/2: Convert - 50.8s - [CRC 4C7A0149]
        // In progress, stage load maps the engine's four measured stages to three shown labels:
        // Read = In + Pre (section create + pre-process handoff), Process, Output; the busiest
        // (bottleneck) stage is highlighted red. On completion the percentage AND live stats are
        // dropped, leaving just the elapsed and the engine's completion detail (CRC / Partial Read /
        // Failed) — matching the non-dynamic console.
        private string buildStatusMarkup(bool completed, string completeMessage)
        {
            TimeSpan ts = DateTime.UtcNow - _stepStart;
            // Match the non-dynamic console's padded time columns: "~ 0m  5s".
            string elapsed = $"~{(int)ts.TotalMinutes,2}m {ts.Seconds,2:D2}s";
            // Coloured, width-padded "Step N/M: Task" label: the "Step N/M:" scaffolding is white,
            // the step TYPE (task) is yellow, and the trailing pad keeps column alignment.
            string labelMarkup = buildStepLabelMarkup();

            // On completion: no percentage, no stats — just "Step N/M: Task - elapsed - [detail]".
            if (completed)
            {
                string done = $"{labelMarkup} - [{White}]{elapsed}[/]";
                string msg = (completeMessage ?? "").Trim();
                // Strip the leading "~Xm Ys  " timing (elapsed is already shown) — keep the bracketed
                // detail like "[CRC 4C7A0149]". If parsing is uncertain, show the whole message.
                int b = msg.IndexOf('[');
                if (b >= 0)
                    msg = msg.Substring(b);
                // The completion detail ([CRC ...] / [Failed!] / [Partial Read]) is pink.
                return string.IsNullOrEmpty(msg) ? done : $"{done} - [{Pink}]{Markup.Escape(msg)}[/]";
            }

            // In-progress line uses the scheme's cyan + yellow (no red/green). Percentage in cyan,
            // elapsed in white.
            string head = $"{labelMarkup} - " +
                          $"[cyan]{_pct * 100f,5:0.0}%[/] - " +
                          $"[{White}]{elapsed}[/]";

            if (_stats == null)
                return head;

            EngineStats s = _stats;
            double read = s.InFraction + s.PreFraction; // Read = section create + pre-process handoff
            double proc = s.ProcFraction;
            double outp = s.OutFraction;
            double max = Math.Max(read, Math.Max(proc, outp));
            // Stage load: cyan by default, the busiest (bottleneck) stage highlighted in yellow.
            string c(double v) => (v >= max && max > 0) ? "yellow" : "cyan";

            string stats =
                $" - Stats " +
                $"[[[{c(read)}]Read {read:P0}[/]]] " +
                $"[[[{c(proc)}]Process {proc:P0}[/] - [yellow]{s.BusyWorkers}/{s.TotalWorkers} Workers[/]]] " +
                $"[[[{c(outp)}]Output {outp:P0}[/]]]";

            return head + stats;
        }
    }
}