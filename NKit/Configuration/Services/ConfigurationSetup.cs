using Nanook.NKit.Configuration.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Nanook.NKit.Configuration.Services
{
    internal interface IConfigurationSetup
    {
        SetupResult EnsureConfiguration(ConfigurationContext context, IFileSystemService fileSystem);
    }

    internal class ConfigurationSetup : IConfigurationSetup
    {
        private static readonly string[] RequiredConfigDirectories = getRequiredConfigDirectories();

        private static readonly string[] UserDirectories = {
            ConfigSettingsConstants.DirectoryNameOut,
            ConfigSettingsConstants.DirectoryNameScans,
            ConfigSettingsConstants.DirectoryNameTemp,
            ConfigSettingsConstants.DirectoryNameLogs,
            ConfigSettingsConstants.DirectoryNameDedupe
        };

        private static string[] getRequiredConfigDirectories()
        {
            List<string> directories = new List<string>
            {
                ConfigSettingsConstants.DirectoryNameDats,
                ConfigSettingsConstants.DirectoryNameKeys,
                ConfigSettingsConstants.DirectoryNameFix,
                ConfigSettingsConstants.DirectoryNameScans
            };

            // All directory names are lowercase — $system$ is lowercased during path expansion
            // so keys/wiiu, dats/ps3 etc. are consistent on all platforms including Linux.
            string[] datsSubdirs = { "cdi", "default", "dreamcast", "gamecube", "pcengine", "ps1", "ps2", "ps3", "psp", "saturn", "segacd", "wii", "wiiu", "xbox", "xbox360" };
            foreach (string subdir in datsSubdirs)
                directories.Add($"{ConfigSettingsConstants.DirectoryNameDats}/{subdir}");

            string[] keysSubdirs = { "ps3", "wiiu" };
            foreach (string subdir in keysSubdirs)
                directories.Add($"{ConfigSettingsConstants.DirectoryNameKeys}/{subdir}");

            string[] fixSubdirs = { "gamecube", "ps3", "wii" };
            foreach (string subdir in fixSubdirs)
                directories.Add($"{ConfigSettingsConstants.DirectoryNameFix}/{subdir}");

            return directories.ToArray();
        }

        public SetupResult EnsureConfiguration(ConfigurationContext context, IFileSystemService fileSystem)
        {
            try
            {
                // Early startup marker so failures before bundled-copy are still visible on disk
                try
                {
                    List<string> startupCandidates = new List<string>();
                    startupCandidates.Add(Path.GetTempPath());

                    foreach (string dir in startupCandidates)
                    {
                        try
                        {
                            if (string.IsNullOrEmpty(dir)) continue;
                            try { if (!fileSystem.DirectoryExists(dir)) fileSystem.CreateDirectory(dir); } catch { }
                            break; // written successfully
                        }
                        catch { }
                    }
                }
                catch { }

                int directoriesCreated = createRequiredDirectories(context, fileSystem);
                bool configFileCreated = ensureConfigFile(context, fileSystem);
                int bundledFilesCopied = copyBundledFiles(context, fileSystem);

                return new SetupResult
                {
                    Success = true,
                    DirectoriesCreated = directoriesCreated,
                    ConfigFileCreated = configFileCreated,
                    FixFilesCopied = bundledFilesCopied,
                    ConfigSymbolicLinkCreated = false,
                    UserReadmeCreated = false
                };
            }
            catch (Exception ex)
            {
                return new SetupResult { Success = false, Error = ex.Message };
            }
        }

        private static int createRequiredDirectories(ConfigurationContext context, IFileSystemService fileSystem)
        {
            int created = 0;

            foreach (string directory in RequiredConfigDirectories)
            {
                string[] parts = directory.Split('/');
                string fullPath = fileSystem.CombinePath(new[] { context.ConfigDirectory }.Concat(parts).ToArray());
                if (!fileSystem.DirectoryExists(fullPath))
                {
                    try { fileSystem.CreateDirectory(fullPath); created++; } catch { }
                }
            }

            string userBaseDir = context.IsPortableMode
                ? context.UserDataDirectory
                : fileSystem.CombinePath(context.UserDataDirectory, ConfigSettingsConstants.ApplicationDirectoryName);

            if (!context.IsPortableMode && !fileSystem.DirectoryExists(userBaseDir))
            {
                try { fileSystem.CreateDirectory(userBaseDir); created++; } catch { }
            }

            foreach (string directory in UserDirectories)
            {
                string fullPath = fileSystem.CombinePath(userBaseDir, directory);
                if (!fileSystem.DirectoryExists(fullPath))
                {
                    try { fileSystem.CreateDirectory(fullPath); created++; } catch { }
                }
            }

            return created;
        }

        private static bool ensureConfigFile(ConfigurationContext context, IFileSystemService fileSystem)
        {
            string targetConfigPath = fileSystem.CombinePath(context.ConfigDirectory, context.ConfigFileName);
            if (fileSystem.FileExists(targetConfigPath))
                return false;

            if (context.ConfigSource != ConfigSource.None && !string.IsNullOrEmpty(context.ConfigFile) && fileSystem.FileExists(context.ConfigFile))
                return false;

            try
            {
                // Copy the default config if a defaults/{configFileName} source exists.
                // Previously gated to CLI only (nkit.yaml), but nkit-ui.yaml and nkds-ui.yaml
                // also ship a defaults/ config and need the same first-run copy behaviour.
                return tryCopyFromDefaults(context, fileSystem, targetConfigPath);
            }
            catch
            {
                return false;
            }
        }

        private static bool tryCopyFromDefaults(ConfigurationContext context, IFileSystemService fileSystem, string targetConfigPath)
        {
            List<string> candidatePaths = new List<string>();

            if (!string.IsNullOrEmpty(context.ExecutableDirectory))
                candidatePaths.Add(fileSystem.CombinePath(context.ExecutableDirectory, ConfigSettingsConstants.DirectoryNameDefaults, context.ConfigFileName));

            if (!string.IsNullOrEmpty(AppContext.BaseDirectory))
                candidatePaths.Add(fileSystem.CombinePath(AppContext.BaseDirectory, ConfigSettingsConstants.DirectoryNameDefaults, context.ConfigFileName));

            try { candidatePaths.Add(fileSystem.CombinePath(Directory.GetCurrentDirectory(), ConfigSettingsConstants.DirectoryNameDefaults, context.ConfigFileName)); } catch { }

            foreach (string sourcePath in candidatePaths)
            {
                try
                {
                    if (fileSystem.FileExists(sourcePath))
                    {
                        string targetDir = Path.GetDirectoryName(targetConfigPath);
                        if (!string.IsNullOrEmpty(targetDir) && !fileSystem.DirectoryExists(targetDir))
                            fileSystem.CreateDirectory(targetDir);

                        fileSystem.CopyFile(sourcePath, targetConfigPath);
                        return true;
                    }
                }
                catch { }
            }

            return false;
        }

        private static int copyBundledFiles(ConfigurationContext context, IFileSystemService fileSystem)
        {
            try
            {
                List<string> candidates = new List<string>();

                // Add executable adjacent defaults/fix
                if (!string.IsNullOrEmpty(context.ExecutableDirectory))
                {
                    // If running inside a macOS .app bundle, the executable lives in
                    // .app/Contents/MacOS and the Resources are in .app/Contents/Resources.
                    // Do NOT check [MacOS]/defaults or [MacOS]/fix � those do not exist on mac bundles.
                    if (context.IsMacOSBundle)
                    {
                        try
                        {
                            string exeDir = context.ExecutableDirectory.Replace('\\', '/');
                            int appIdx = exeDir.IndexOf(".app", StringComparison.OrdinalIgnoreCase);
                            if (appIdx >= 0)
                            {
                                string bundleRoot = exeDir.Substring(0, appIdx + 4);
                                candidates.Add(fileSystem.CombinePath(bundleRoot, "Contents", "Resources", ConfigSettingsConstants.DirectoryNameDefaults));
                                candidates.Add(fileSystem.CombinePath(bundleRoot, "Contents", "Resources", ConfigSettingsConstants.DirectoryNameFix));
                            }
                            else
                            {
                                // Fallback: try base directory to derive bundle root
                                string baseDir = AppContext.BaseDirectory?.Replace('\\', '/');
                                if (!string.IsNullOrEmpty(baseDir))
                                {
                                    int appIdx2 = baseDir.IndexOf(".app", StringComparison.OrdinalIgnoreCase);
                                    if (appIdx2 >= 0)
                                    {
                                        string bundleRoot = baseDir.Substring(0, appIdx2 + 4);
                                        candidates.Add(fileSystem.CombinePath(bundleRoot, "Contents", "Resources", ConfigSettingsConstants.DirectoryNameDefaults));
                                        candidates.Add(fileSystem.CombinePath(bundleRoot, "Contents", "Resources", ConfigSettingsConstants.DirectoryNameFix));
                                    }
                                }
                            }
                        }
                        catch { }
                    }
                    else
                    {
                        // Non-mac bundles: check next to the executable for defaults/fix
                        candidates.Add(fileSystem.CombinePath(context.ExecutableDirectory, ConfigSettingsConstants.DirectoryNameDefaults));
                        candidates.Add(fileSystem.CombinePath(context.ExecutableDirectory, ConfigSettingsConstants.DirectoryNameFix));
                    }
                }

                // Add base directory defaults/fix
                if (!string.IsNullOrEmpty(AppContext.BaseDirectory))
                {
                    candidates.Add(fileSystem.CombinePath(AppContext.BaseDirectory, ConfigSettingsConstants.DirectoryNameDefaults));
                    candidates.Add(fileSystem.CombinePath(AppContext.BaseDirectory, ConfigSettingsConstants.DirectoryNameFix));
                }

                // Add current working directory
                try
                {
                    candidates.Add(fileSystem.CombinePath(Directory.GetCurrentDirectory(), ConfigSettingsConstants.DirectoryNameDefaults));
                    candidates.Add(fileSystem.CombinePath(Directory.GetCurrentDirectory(), ConfigSettingsConstants.DirectoryNameFix));
                }
                catch { }

                int copied = 0;
                HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (string candidate in candidates)
                {
                    try
                    {
                        if (string.IsNullOrEmpty(candidate) || seen.Contains(candidate))
                            continue;

                        seen.Add(candidate);

                        if (!fileSystem.DirectoryExists(candidate))
                            continue;

                        List<string> files = fileSystem.GetFiles(candidate, "*", SearchOption.AllDirectories)?.ToList() ?? new List<string>();

                        foreach (string src in files)
                        {
                            try
                            {
                                string rel = Path.GetRelativePath(candidate, src).Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
                                string dest;

                                bool isFix = rel.StartsWith(ConfigSettingsConstants.DirectoryNameFix + "/", StringComparison.OrdinalIgnoreCase) || candidate.EndsWith(ConfigSettingsConstants.DirectoryNameFix, StringComparison.OrdinalIgnoreCase);

                                if (isFix)
                                {
                                    string inner = rel.StartsWith(ConfigSettingsConstants.DirectoryNameFix + "/", StringComparison.OrdinalIgnoreCase) ? rel.Substring(ConfigSettingsConstants.DirectoryNameFix.Length + 1) : rel;
                                    string[] parts = inner.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                                    List<string> args = new List<string> { context.ConfigDirectory, ConfigSettingsConstants.DirectoryNameFix };
                                    args.AddRange(parts);
                                    dest = fileSystem.CombinePath(args.ToArray());
                                }
                                else
                                {
                                    string[] parts = rel.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                                    List<string> args = new List<string> { context.ConfigDirectory };
                                    args.AddRange(parts);
                                    dest = fileSystem.CombinePath(args.ToArray());
                                }

                                string destDir = Path.GetDirectoryName(dest) ?? context.ConfigDirectory;
                                if (!fileSystem.DirectoryExists(destDir))
                                    fileSystem.CreateDirectory(destDir);

                                if (!fileSystem.FileExists(dest))
                                {
                                    fileSystem.CopyFile(src, dest);
                                    copied++;
                                }
                            }
                            catch (Exception)
                            {
                            }
                        }
                    }
                    catch (Exception)
                    {
                    }
                }

                return copied;
            }
            catch
            {
                return 0;
            }
        }

        private static bool isPathEndingWith(string path, string dirName)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(dirName)) return false;
            path = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return path.EndsWith(Path.DirectorySeparatorChar + dirName, StringComparison.OrdinalIgnoreCase) || path.EndsWith(Path.AltDirectorySeparatorChar + dirName, StringComparison.OrdinalIgnoreCase) || Path.GetFileName(path).Equals(dirName, StringComparison.OrdinalIgnoreCase);
        }
    }
}
