using NKit.Ui.Models;
using System.Collections.Generic;
using System.IO;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace NKit.Ui.Helpers.Yaml
{
    public static class AotYamlSerializer
    {
        public static void Serialize(TextWriter writer, UiConfiguration config)
        {
            Emitter emitter = new Emitter(writer);
            emitter.Emit(new StreamStart());
            emitter.Emit(new DocumentStart());

            SerializeUiConfiguration(emitter, config);

            emitter.Emit(new DocumentEnd(true));
            emitter.Emit(new StreamEnd());
        }

        public static UiConfiguration Deserialize(TextReader reader)
        {
            Parser parser = new Parser(reader);
            parser.Consume<StreamStart>();
            parser.Consume<DocumentStart>();

            UiConfiguration config = DeserializeUiConfiguration(parser);

            parser.Consume<DocumentEnd>();
            parser.Consume<StreamEnd>();

            return config;
        }

        private static void SerializeUiConfiguration(IEmitter emitter, UiConfiguration config)
        {
            emitter.Emit(new MappingStart());

            emitter.Emit(new Scalar("version"));
            emitter.Emit(new Scalar(config.Version ?? "1.3"));

            if (config.Global != null)
            {
                emitter.Emit(new Scalar("global"));
                SerializeGlobalSettings(emitter, config.Global);
            }

            if (config.Systems != null)
            {
                emitter.Emit(new Scalar("systems"));
                emitter.Emit(new MappingStart());
                foreach (KeyValuePair<string, UiConfiguration.SystemConfiguration> kvp in config.Systems)
                {
                    emitter.Emit(new Scalar(kvp.Key));
                    SerializeSystemConfiguration(emitter, kvp.Value);
                }
                emitter.Emit(new MappingEnd());
            }

            emitter.Emit(new MappingEnd());
        }

        private static void SerializeGlobalSettings(IEmitter emitter, UiConfiguration.GlobalSettings settings)
        {
            emitter.Emit(new MappingStart());

            emitter.Emit(new Scalar("consoleLevel"));
            emitter.Emit(new Scalar(settings.ConsoleLevel ?? "info"));

            // Persist last selected system and task for UI state restoration
            if (!string.IsNullOrEmpty(settings.LastSystem))
            {
                emitter.Emit(new Scalar("lastSystem"));
                emitter.Emit(new Scalar(settings.LastSystem));
            }
            if (!string.IsNullOrEmpty(settings.LastTask))
            {
                emitter.Emit(new Scalar("lastTask"));
                emitter.Emit(new Scalar(settings.LastTask));
            }

            // Always serialize dats section, even if empty
            emitter.Emit(new Scalar("dats"));
            SerializeDatCollections(emitter, settings.Dats ?? new UiConfiguration.DatCollections());

            if (settings.Ui != null)
            {
                emitter.Emit(new Scalar("ui"));
                SerializeUiOnlySettings(emitter, settings.Ui);
            }

            emitter.Emit(new MappingEnd());
        }

        private static void SerializeUiOnlySettings(IEmitter emitter, UiConfiguration.UiOnlySettings settings)
        {
            emitter.Emit(new MappingStart());

            emitter.Emit(new Scalar("showQueued"));
            emitter.Emit(new Scalar(settings.ShowQueued ? "y" : "n"));

            emitter.Emit(new Scalar("showSkipped"));
            emitter.Emit(new Scalar(settings.ShowSkipped ? "y" : "n"));

            emitter.Emit(new Scalar("showProcessing"));
            emitter.Emit(new Scalar(settings.ShowProcessing ? "y" : "n"));

            emitter.Emit(new Scalar("showCompleted"));
            emitter.Emit(new Scalar(settings.ShowCompleted ? "y" : "n"));

            emitter.Emit(new Scalar("showFailed"));
            emitter.Emit(new Scalar(settings.ShowFailed ? "y" : "n"));

            emitter.Emit(new Scalar("showCancelled"));
            emitter.Emit(new Scalar(settings.ShowCancelled ? "y" : "n"));

            emitter.Emit(new Scalar("consoleOutputAutoScroll"));
            emitter.Emit(new Scalar(settings.ConsoleOutputAutoScroll ? "y" : "n"));

            emitter.Emit(new Scalar("consoleOutputBuffer"));
            emitter.Emit(new Scalar(settings.ConsoleOutputBuffer.ToString()));

            emitter.Emit(new Scalar("consoleOutputWrapText"));
            emitter.Emit(new Scalar(settings.ConsoleOutputWrapText ? "y" : "n"));

            emitter.Emit(new Scalar("reprocessCompletedFiles"));
            emitter.Emit(new Scalar(settings.ReprocessCompletedFiles ? "y" : "n"));

            emitter.Emit(new Scalar("reprocessFailedFiles"));
            emitter.Emit(new Scalar(settings.ReprocessFailedFiles ? "y" : "n"));

            emitter.Emit(new Scalar("reprocessSkippedFiles"));
            emitter.Emit(new Scalar(settings.ReprocessSkippedFiles ? "y" : "n"));

            emitter.Emit(new Scalar("persistFileQueue"));
            emitter.Emit(new Scalar(settings.PersistFileQueue ? "y" : "n"));

            // Serialize showTooltips as well
            emitter.Emit(new Scalar("showTooltips"));
            emitter.Emit(new Scalar(settings.ShowTooltips ? "y" : "n"));

            emitter.Emit(new Scalar("windowDecorationMode"));
            emitter.Emit(new Scalar(settings.WindowDecorationMode ?? "csd"));

            emitter.Emit(new MappingEnd());
        }

        private static void SerializeDatCollections(IEmitter emitter, UiConfiguration.DatCollections dats)
        {
            emitter.Emit(new MappingStart());

            // Always serialize the structure even if paths are empty to ensure proper YAML format
            emitter.Emit(new Scalar("redump"));
            emitter.Emit(new Scalar(dats.Redump ?? ""));

            emitter.Emit(new Scalar("nointro"));
            emitter.Emit(new Scalar(dats.NoIntro ?? ""));

            emitter.Emit(new Scalar("tosec"));
            emitter.Emit(new Scalar(dats.Tosec ?? ""));

            emitter.Emit(new MappingEnd());
        }

        private static void SerializeSystemConfiguration(IEmitter emitter, UiConfiguration.SystemConfiguration config)
        {
            emitter.Emit(new MappingStart());

            // Task options
            if (!string.IsNullOrEmpty(config.Convert))
            {
                emitter.Emit(new Scalar("convert"));
                emitter.Emit(new Scalar(config.Convert));
            }
            if (!string.IsNullOrEmpty(config.Extract))
            {
                emitter.Emit(new Scalar("extract"));
                emitter.Emit(new Scalar(config.Extract));
            }
            if (!string.IsNullOrEmpty(config.Dedupe))
            {
                emitter.Emit(new Scalar("dedupe"));
                emitter.Emit(new Scalar(config.Dedupe));
            }
            if (!string.IsNullOrEmpty(config.OgmrYamlPath))
            {
                emitter.Emit(new Scalar("1gmr"));
                emitter.Emit(new Scalar(config.OgmrYamlPath));
            }

            // Process options
            emitter.Emit(new Scalar("v"));
            emitter.Emit(new Scalar(config.V ?? "y"));

            // Task-specific verify settings (only serialize if different from general V or if general V is missing)
            if (!string.IsNullOrEmpty(config.V_Convert))
            {
                emitter.Emit(new Scalar("v_Convert"));
                emitter.Emit(new Scalar(config.V_Convert));
            }
            if (!string.IsNullOrEmpty(config.V_Scan))
            {
                emitter.Emit(new Scalar("v_Scan"));
                emitter.Emit(new Scalar(config.V_Scan));
            }
            if (!string.IsNullOrEmpty(config.V_Fix))
            {
                emitter.Emit(new Scalar("v_Fix"));
                emitter.Emit(new Scalar(config.V_Fix));
            }
            if (!string.IsNullOrEmpty(config.V_Verify))
            {
                emitter.Emit(new Scalar("v_Verify"));
                emitter.Emit(new Scalar(config.V_Verify));
            }
            if (!string.IsNullOrEmpty(config.V_Dedupe))
            {
                emitter.Emit(new Scalar("v_Dedupe"));
                emitter.Emit(new Scalar(config.V_Dedupe));
            }

            emitter.Emit(new Scalar("r"));
            emitter.Emit(new Scalar(config.R ? "y" : "n"));
            emitter.Emit(new Scalar("arc"));
            emitter.Emit(new Scalar(config.Arc ? "y" : "n"));
            emitter.Emit(new Scalar("results"));
            emitter.Emit(new Scalar(config.Results ? "y" : "n"));
            emitter.Emit(new Scalar("logOutLevel"));
            emitter.Emit(new Scalar(config.LogOutLevel ?? "info"));
            emitter.Emit(new Scalar("deleteProcessed"));
            emitter.Emit(new Scalar(config.DeleteProcessed ? "y" : "n"));
            emitter.Emit(new Scalar("skipIfCompleted"));
            emitter.Emit(new Scalar(config.SkipIfCompleted ? "y" : "n"));
            emitter.Emit(new Scalar("outAsDatMatch"));
            emitter.Emit(new Scalar(config.OutAsDatMatch ? "y" : "n"));

            // System paths
            if (!string.IsNullOrEmpty(config.Out))
            {
                emitter.Emit(new Scalar("out"));
                emitter.Emit(new Scalar(config.Out));
            }
            if (!string.IsNullOrEmpty(config.Tmp))
            {
                emitter.Emit(new Scalar("tmp"));
                emitter.Emit(new Scalar(config.Tmp));
            }
            if (!string.IsNullOrEmpty(config.ScanOut))
            {
                emitter.Emit(new Scalar("scanOut"));
                emitter.Emit(new Scalar(config.ScanOut));
            }
            if (!string.IsNullOrEmpty(config.ScanIn))
            {
                emitter.Emit(new Scalar("scanIn"));
                emitter.Emit(new Scalar(config.ScanIn));
            }
            if (!string.IsNullOrEmpty(config.LogOut))
            {
                emitter.Emit(new Scalar("logOut"));
                emitter.Emit(new Scalar(config.LogOut));
            }
            if (!string.IsNullOrEmpty(config.ResultsOut))
            {
                emitter.Emit(new Scalar("resultsOut"));
                emitter.Emit(new Scalar(config.ResultsOut));
            }
            if (!string.IsNullOrEmpty(config.BaseInPath))
            {
                emitter.Emit(new Scalar("baseInPath"));
                emitter.Emit(new Scalar(config.BaseInPath));
            }
            // Only serialize keys if not null/empty (system-specific handling)
            if (!string.IsNullOrEmpty(config.Keys))
            {
                emitter.Emit(new Scalar("keys"));
                emitter.Emit(new Scalar(config.Keys));
            }
            if (!string.IsNullOrEmpty(config.Dat))
            {
                emitter.Emit(new Scalar("dat"));
                emitter.Emit(new Scalar(config.Dat));
            }
            if (!string.IsNullOrEmpty(config.FixInfo))
            {
                emitter.Emit(new Scalar("fixInfo"));
                emitter.Emit(new Scalar(config.FixInfo));
            }
            if (!string.IsNullOrEmpty(config.FixFiles))
            {
                emitter.Emit(new Scalar("fixFiles"));
                emitter.Emit(new Scalar(config.FixFiles));
            }

            emitter.Emit(new MappingEnd());
        }

        private static UiConfiguration DeserializeUiConfiguration(IParser parser)
        {
            UiConfiguration config = new UiConfiguration();

            parser.Consume<MappingStart>();

            while (!parser.Accept<MappingEnd>(out _))
            {
                string key = parser.Consume<Scalar>().Value;

                switch (key)
                {
                    case "version":
                        config.Version = parser.Consume<Scalar>().Value;
                        break;
                    case "global":
                        config.Global = DeserializeGlobalSettings(parser);
                        break;
                    case "systems":
                        config.Systems = DeserializeSystems(parser);
                        break;
                    default:
                        SkipValue(parser);
                        break;
                }
            }

            parser.Consume<MappingEnd>();
            return config;
        }

        private static UiConfiguration.GlobalSettings DeserializeGlobalSettings(IParser parser)
        {
            UiConfiguration.GlobalSettings settings = new UiConfiguration.GlobalSettings();

            parser.Consume<MappingStart>();

            while (!parser.Accept<MappingEnd>(out _))
            {
                string key = parser.Consume<Scalar>().Value;

                switch (key)
                {
                    case "consoleLevel":
                        settings.ConsoleLevel = parser.Consume<Scalar>().Value;
                        break;
                    case "parallelism":
                        // Retired engine knob (worker count is autoscaled). Consume + ignore so
                        // existing config files that still carry it deserialize without error.
                        parser.Consume<Scalar>();
                        break;
                    case "lastSystem":
                        settings.LastSystem = parser.Consume<Scalar>().Value;
                        break;
                    case "lastTask":
                        settings.LastTask = parser.Consume<Scalar>().Value;
                        break;
                    case "dats":
                        settings.Dats = DeserializeDatCollections(parser);
                        break;
                    case "ui":
                        settings.Ui = DeserializeUiOnlySettings(parser);
                        break;
                    default:
                        SkipValue(parser);
                        break;
                }
            }

            parser.Consume<MappingEnd>();
            return settings;
        }

        private static UiConfiguration.UiOnlySettings DeserializeUiOnlySettings(IParser parser)
        {
            // Initialize with defaults from UiSettings to ensure missing properties get proper values
            UiSettings defaults = UiSettings.GetDefaultSettings();
            UiConfiguration.UiOnlySettings settings = new UiConfiguration.UiOnlySettings
            {
                ShowQueued = defaults.ShowQueued,
                ShowSkipped = defaults.ShowSkipped,
                ShowProcessing = defaults.ShowProcessing,
                ShowCompleted = defaults.ShowCompleted,
                ShowFailed = defaults.ShowFailed,
                ShowCancelled = defaults.ShowCancelled,
                ConsoleOutputAutoScroll = defaults.ConsoleOutputAutoScroll,
                ConsoleOutputBuffer = defaults.ConsoleOutputBuffer,
                ConsoleOutputWrapText = defaults.ConsoleOutputWrapText,
                ReprocessCompletedFiles = defaults.ReprocessCompletedFiles,
                ReprocessFailedFiles = defaults.ReprocessFailedFiles,
                ReprocessSkippedFiles = defaults.ReprocessSkippedFiles,
                PersistFileQueue = defaults.PersistFileQueue // This will now default to true!
            };

            parser.Consume<MappingStart>();

            while (!parser.Accept<MappingEnd>(out _))
            {
                string key = parser.Consume<Scalar>().Value;

                switch (key)
                {
                    case "showQueued":
                        string showQueuedValue = parser.Consume<Scalar>().Value;
                        settings.ShowQueued = showQueuedValue == "y" || showQueuedValue == "true";
                        break;
                    case "showSkipped":
                        string showSkippedValue = parser.Consume<Scalar>().Value;
                        settings.ShowSkipped = showSkippedValue == "y" || showSkippedValue == "true";
                        break;
                    case "showProcessing":
                        string showProcessingValue = parser.Consume<Scalar>().Value;
                        settings.ShowProcessing = showProcessingValue == "y" || showProcessingValue == "true";
                        break;
                    case "showCompleted":
                        string showCompletedValue = parser.Consume<Scalar>().Value;
                        settings.ShowCompleted = showCompletedValue == "y" || showCompletedValue == "true";
                        break;
                    case "showFailed":
                        string showFailedValue = parser.Consume<Scalar>().Value;
                        settings.ShowFailed = showFailedValue == "y" || showFailedValue == "true";
                        break;
                    case "showCancelled":
                        string showCancelledValue = parser.Consume<Scalar>().Value;
                        settings.ShowCancelled = showCancelledValue == "y" || showCancelledValue == "true";
                        break;
                    case "consoleOutputAutoScroll":
                        string consoleAutoScrollValue = parser.Consume<Scalar>().Value;
                        settings.ConsoleOutputAutoScroll = consoleAutoScrollValue == "y" || consoleAutoScrollValue == "true";
                        break;
                    case "consoleOutputBuffer":
                        if (int.TryParse(parser.Consume<Scalar>().Value, out int consoleBufferValue))
                            settings.ConsoleOutputBuffer = consoleBufferValue;
                        break;
                    case "consoleOutputWrapText":
                        string consoleWrapValue = parser.Consume<Scalar>().Value;
                        settings.ConsoleOutputWrapText = consoleWrapValue == "y" || consoleWrapValue == "true";
                        break;
                    case "reprocessCompletedFiles":
                        string reprocessCompletedValue = parser.Consume<Scalar>().Value;
                        settings.ReprocessCompletedFiles = reprocessCompletedValue == "y" || reprocessCompletedValue == "true";
                        break;
                    case "reprocessFailedFiles":
                        string reprocessFailedValue = parser.Consume<Scalar>().Value;
                        settings.ReprocessFailedFiles = reprocessFailedValue == "y" || reprocessFailedValue == "true";
                        break;
                    case "reprocessSkippedFiles":
                        string reprocessSkippedValue = parser.Consume<Scalar>().Value;
                        settings.ReprocessSkippedFiles = reprocessSkippedValue == "y" || reprocessSkippedValue == "true";
                        break;
                    case "persistFileQueue":
                        string persistQueueValue = parser.Consume<Scalar>().Value;
                        settings.PersistFileQueue = persistQueueValue == "y" || persistQueueValue == "true";
                        break;
                    case "showTooltips":
                        string showTooltipsValue = parser.Consume<Scalar>().Value;
                        settings.ShowTooltips = showTooltipsValue == "y" || showTooltipsValue == "true";
                        break;
                    case "windowDecorationMode":
                        settings.WindowDecorationMode = parser.Consume<Scalar>().Value;
                        break;
                    default:
                        SkipValue(parser);
                        break;
                }
            }

            parser.Consume<MappingEnd>();
            return settings;
        }

        private static UiConfiguration.DatCollections DeserializeDatCollections(IParser parser)
        {
            UiConfiguration.DatCollections dats = new UiConfiguration.DatCollections();

            parser.Consume<MappingStart>();

            while (!parser.Accept<MappingEnd>(out _))
            {
                string key = parser.Consume<Scalar>().Value;

                switch (key)
                {
                    case "redump":
                        dats.Redump = parser.Consume<Scalar>().Value;
                        break;
                    case "nointro":
                        dats.NoIntro = parser.Consume<Scalar>().Value;
                        break;
                    case "tosec":
                        dats.Tosec = parser.Consume<Scalar>().Value;
                        break;
                    default:
                        SkipValue(parser);
                        break;
                }
            }

            parser.Consume<MappingEnd>();
            return dats;
        }

        private static System.Collections.Generic.Dictionary<string, UiConfiguration.SystemConfiguration> DeserializeSystems(IParser parser)
        {
            Dictionary<string, UiConfiguration.SystemConfiguration> systems = new System.Collections.Generic.Dictionary<string, UiConfiguration.SystemConfiguration>();

            parser.Consume<MappingStart>();

            while (!parser.Accept<MappingEnd>(out _))
            {
                string key = parser.Consume<Scalar>().Value;
                UiConfiguration.SystemConfiguration config = DeserializeSystemConfiguration(parser);
                systems[key] = config;
            }

            parser.Consume<MappingEnd>();
            return systems;
        }

        private static UiConfiguration.SystemConfiguration DeserializeSystemConfiguration(IParser parser)
        {
            UiConfiguration.SystemConfiguration config = new UiConfiguration.SystemConfiguration();

            parser.Consume<MappingStart>();

            while (!parser.Accept<MappingEnd>(out _))
            {
                string key = parser.Consume<Scalar>().Value;

                switch (key)
                {
                    case "convert":
                        config.Convert = parser.Consume<Scalar>().Value;
                        break;
                    case "extract":
                        config.Extract = parser.Consume<Scalar>().Value;
                        break;
                    case "dedupe":
                        config.Dedupe = parser.Consume<Scalar>().Value;
                        break;
                    case "1gmr":
                        config.OgmrYamlPath = parser.Consume<Scalar>().Value;
                        break;
                    case "v":
                        config.V = parser.Consume<Scalar>().Value;
                        break;
                    case "v_Convert":
                        config.V_Convert = parser.Consume<Scalar>().Value;
                        break;
                    case "v_Scan":
                        config.V_Scan = parser.Consume<Scalar>().Value;
                        break;
                    case "v_Fix":
                        config.V_Fix = parser.Consume<Scalar>().Value;
                        break;
                    case "v_Verify":
                        config.V_Verify = parser.Consume<Scalar>().Value;
                        break;
                    case "v_Dedupe":
                        config.V_Dedupe = parser.Consume<Scalar>().Value;
                        break;
                    case "r":
                        string rValue = parser.Consume<Scalar>().Value;
                        config.R = rValue == "y" || rValue == "true";
                        break;
                    case "arc":
                        string arcValue = parser.Consume<Scalar>().Value;
                        config.Arc = arcValue == "y" || arcValue == "true";
                        break;
                    case "deleteProcessed":
                        string deleteProcessedValue = parser.Consume<Scalar>().Value;
                        config.DeleteProcessed = deleteProcessedValue == "y" || deleteProcessedValue == "true";
                        break;
                    case "skipIfCompleted":
                        string skipIfCompletedValue = parser.Consume<Scalar>().Value;
                        config.SkipIfCompleted = skipIfCompletedValue == "y" || skipIfCompletedValue == "true";
                        break;
                    case "results":
                        string resultsValue = parser.Consume<Scalar>().Value;
                        config.Results = resultsValue == "y" || resultsValue == "true";
                        break;
                    case "logOutLevel":
                        config.LogOutLevel = parser.Consume<Scalar>().Value;
                        break;
                    case "outAsDatMatch":
                        string outAsDatMatchValue = parser.Consume<Scalar>().Value;
                        config.OutAsDatMatch = outAsDatMatchValue == "y" || outAsDatMatchValue == "true";
                        break;
                    case "selectedTabIndex":
                        // Skip selectedTabIndex - not persisted
                        parser.Consume<Scalar>();
                        break;
                    case "out":
                        config.Out = parser.Consume<Scalar>().Value;
                        break;
                    case "tmp":
                        config.Tmp = parser.Consume<Scalar>().Value;
                        break;
                    case "scanOut":
                        config.ScanOut = parser.Consume<Scalar>().Value;
                        break;
                    case "scanIn":
                        config.ScanIn = parser.Consume<Scalar>().Value;
                        break;
                    case "logOut":
                        config.LogOut = parser.Consume<Scalar>().Value;
                        break;
                    case "resultsOut":
                        config.ResultsOut = parser.Consume<Scalar>().Value;
                        break;
                    case "baseInPath":
                        config.BaseInPath = parser.Consume<Scalar>().Value;
                        break;
                    case "keys":
                        config.Keys = parser.Consume<Scalar>().Value;
                        break;
                    case "dat":
                        config.Dat = parser.Consume<Scalar>().Value;
                        break;
                    case "fixInfo":
                        config.FixInfo = parser.Consume<Scalar>().Value;
                        break;
                    case "fixFiles":
                        config.FixFiles = parser.Consume<Scalar>().Value;
                        break;
                    default:
                        SkipValue(parser);
                        break;
                }
            }

            parser.Consume<MappingEnd>();
            return config;
        }

        private static void SkipValue(IParser parser)
        {
            if (parser.Accept<Scalar>(out _))
            {
                parser.MoveNext();
            }
            else if (parser.Accept<MappingStart>(out _))
            {
                parser.Consume<MappingStart>();
                while (!parser.Accept<MappingEnd>(out _))
                {
                    SkipValue(parser); // key
                    SkipValue(parser); // value
                }
                parser.Consume<MappingEnd>();
            }
            else if (parser.Accept<SequenceStart>(out _))
            {
                parser.Consume<SequenceStart>();
                while (!parser.Accept<SequenceEnd>(out _))
                {
                    SkipValue(parser);
                }
                parser.Consume<SequenceEnd>();
            }
        }
    }
}