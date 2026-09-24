using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using IO = System.IO;
using RX = System.Text.RegularExpressions;

namespace Nanook.NKit
{
    public class FileMask
    {
        private Regex _regex;
        private Regex _regexArc; //file in archive

        /// <summary>
        /// Mask must have a valid path or resolve to a local folder
        /// </summary>
        public static FileMask CreateLocalMask(string mask, bool isRecursive)
        {
            string path = SanitisePath(mask);
            string msk;
            string arc = null;
            int midx = path.LastIndexOf("//");
            if (midx != -1 && midx != 0) //can't start with // (might be unc) 
            {
                arc = path.Substring(midx + 2);
                path = path.Substring(0, midx);
            }

            path = ExpandPath(path);

            if (!path.Contains("/"))
            {
                if (Directory.Exists(path))
                    msk = "*";
                else
                {
                    path = SanitisePath(Directory.GetCurrentDirectory());
                    msk = mask;
                }
            }
            else
            {
                if (path == "/" || Directory.Exists(path))
                    msk = "*";
                else
                {
                    midx = path.LastIndexOf("/");
                    msk = path.Substring(midx + 1);
                    path = path.Substring(0, Math.Max(1, midx));
                }
            }

            if (!path.StartsWith("/"))
            {
                if (Environment.OSVersion.Platform == PlatformID.Win32NT && path.Length == 2 && path[1] == ':') //e.g. - c:
                    path += "/";
                else
                    path = SanitisePath(IO.Path.GetFullPath(path));
            }

            //force recursion OFF as there's no wildcard
            if (isRecursive && !msk.Any(c => c == '*' || c == '?'))
                isRecursive = false;

            return new FileMask(mask, path, msk, arc, true, false, isRecursive);
        }

        public static FileMask CreateImageFsMask(string mask)
        {
            string path = SanitisePath(mask);
            bool isPath = mask.Length != path.Length; //trailing / removed
            string msk;
            string arc = null;
            int midx = path.LastIndexOf("//");
            if (midx != -1)
            {
                arc = path.Substring(midx + 2);
                path = path.Substring(0, midx);
                isPath = path.EndsWith("/");
                if (path != "/")
                    path = path.TrimEnd('/');
            }

            if (isPath)
                msk = ""; //blank will get all files and not sub folders
            else if (path.Contains("/"))
            {
                midx = path.LastIndexOf("/");
                msk = path.Substring(midx + 1);
                path = path.Substring(0, Math.Max(1, midx));
            }
            else
            {
                msk = path;
                path = "";
            }

            if (!path.StartsWith("/") && !path.StartsWith("*"))
                path = "*" + path;

            return new FileMask(mask, path, msk, arc, false, false, false);
        }

        public static string ExpandPath(string path)
        {
            if (path.StartsWith("~"))
                path = IO.Path.Combine(AppSettings.GetUserDirectory(), path.Substring(1));

            return SanitisePath(Environment.ExpandEnvironmentVariables(path));
        }

        public static string SanitisePath(string path)
        {
            // Always normalise backslashes to forward slashes — all internal paths and masks use
            // forward slashes on every platform. The Windows-only guard was an oversight; on Linux
            // user-supplied masks like "temp3\*.zip" were never normalised, breaking regex matching.
            path = path.Replace("\\", "/");
            if (path == "/")
                return path;
            return path.TrimEnd('/');
        }

        public FileMask(string regex, bool caseSensitive)
        {
            this.OriginalMask = regex;
            this.CaseSensitive = caseSensitive;
            this.Regex = regex;

            //all regex must be / based paths. The user is only allowed to specify these for File Extraction which is all / based. Scan paths are all masks so safe to swap
            _regex = new Regex(this.Regex, RegexOptions.Compiled | (caseSensitive ? 0 : RegexOptions.IgnoreCase));
        }

        public static string MaskToRegex(string mask) => maskToRegex(mask, false);

        public static string MaskToArchiveRegex(string mask) => maskToRegex(mask, true);

        private static string maskToRegex(string mask, bool recursive)
        {
            bool addBraces = mask.Contains('|');
            string rMask = recursive ? ".*" : "[^/]*";
            return $"{(addBraces ? "(" : "")}{RX.Regex.Replace(mask, @"([^*?|]+)", m => RX.Regex.Escape(m.Value)).Replace("?", ".").Replace("*", rMask)}{(addBraces ? ")" : "")}";
        }

        private FileMask(string originalMask, string path, string mask, string arcMask, bool isFs, bool caseSensitive, bool isRecursive)
        {
            this.OriginalMask = originalMask;
            this.CaseSensitive = caseSensitive;
            this.Recursive = isRecursive;
            this.Path = path = path ?? "";
            this.Mask = mask;
            this.MaskInArc = arcMask;

            string sep = (path == "/" ? "" : "/") + (isRecursive ? "(.*/)?" : "");

            if (Environment.OSVersion.Platform == PlatformID.Win32NT && path.Length == 3 && path.EndsWith(":/")) //e.g. - c:/
                path = path.Substring(0, 2);

            if (isFs)
                this.Regex = $"^{RX.Regex.Escape(path)}{sep}{maskToRegex(mask, isRecursive)}$";
            else
            {
                if (mask == "")
                    this.Regex = $"^{maskToRegex(path, true)}{sep}[^/]*$"; //no mask so get all files in dir
                else
                    this.Regex = $"^{maskToRegex(path, true)}{sep}{maskToRegex(mask, mask == "*")}$"; //path is recursive, mask is not
            }

            _regex = new Regex(this.Regex, RegexOptions.Compiled | (caseSensitive ? 0 : RegexOptions.IgnoreCase));

            if (!string.IsNullOrEmpty(arcMask))
            {
                this.RegexInArc = $"^{MaskToArchiveRegex(arcMask)}$";
                _regexArc = new Regex(this.RegexInArc, RegexOptions.Compiled | (caseSensitive ? 0 : RegexOptions.IgnoreCase));
            }
        }

        public bool CaseSensitive { get; }
        public bool Recursive { get; }
        public string OriginalMask { get; }
        public string Path { get; }
        public string Mask { get; }
        public string Regex { get; }

        public string MaskInArc { get; }
        public string RegexInArc { get; }

        public bool IsMatch(string pathFileName) => isMatch(pathFileName, _regex);

        public bool IsArcMatch(string pathFileName) => isMatch(pathFileName, _regexArc);

        private bool isMatch(string path, Regex rx)
        {
            // Always normalise backslashes to forward slashes — the internal contract is forward-slash
            // paths on all platforms. On Linux the guard was previously skipped, so Windows-style
            // paths passed in (e.g. from test data or user input) never matched the regex.
            path = path.Replace("\\", "/");
            bool match = rx == null ? true : rx.IsMatch(path);
            return match;
        }
    }
}