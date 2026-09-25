using Nanook.NKit;
using NKit.Ui.Models;
using System;
using System.Collections.ObjectModel;
using System.IO;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace NKit.Ui.Helpers.Yaml
{
    /// <summary>
    /// AOT-compatible YAML serializer for file queue persistence using emit-based serialization.
    /// This serializer handles the SourceFileRecord queue without reflection dependencies.
    /// </summary>
    public static class AotQueueSerializer
    {
        /// <summary>
        /// Serializes a file queue to YAML using emit-based serialization
        /// </summary>
        public static void Serialize(TextWriter writer, ObservableCollection<SourceFileRecord> fileQueue)
        {
            Emitter emitter = new Emitter(writer);
            emitter.Emit(new StreamStart());
            emitter.Emit(new DocumentStart());

            SerializeFileQueue(emitter, fileQueue);

            emitter.Emit(new DocumentEnd(true));
            emitter.Emit(new StreamEnd());
        }

        /// <summary>
        /// Deserializes a file queue from YAML using parser-based deserialization
        /// </summary>
        public static ObservableCollection<SourceFileRecord> Deserialize(TextReader reader)
        {
            Parser parser = new Parser(reader);
            parser.Consume<StreamStart>();
            parser.Consume<DocumentStart>();

            ObservableCollection<SourceFileRecord> fileQueue = DeserializeFileQueue(parser);

            parser.Consume<DocumentEnd>();
            parser.Consume<StreamEnd>();

            return fileQueue;
        }

        /// <summary>
        /// Serializes the file queue as a YAML sequence
        /// </summary>
        private static void SerializeFileQueue(IEmitter emitter, ObservableCollection<SourceFileRecord> fileQueue)
        {
            emitter.Emit(new MappingStart());

            // Add metadata header
            emitter.Emit(new Scalar("version"));
            emitter.Emit(new Scalar("1.0"));

            emitter.Emit(new Scalar("generated"));
            emitter.Emit(new Scalar(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")));

            emitter.Emit(new Scalar("count"));
            emitter.Emit(new Scalar(fileQueue?.Count.ToString() ?? "0"));

            // Serialize the actual queue
            emitter.Emit(new Scalar("queue"));

            if (fileQueue == null || fileQueue.Count == 0)
            {
                emitter.Emit(new SequenceStart(null, null, true, SequenceStyle.Block));
                emitter.Emit(new SequenceEnd());
            }
            else
            {
                emitter.Emit(new SequenceStart(null, null, false, SequenceStyle.Block));

                foreach (SourceFileRecord record in fileQueue)
                {
                    SerializeSourceFileRecord(emitter, record);
                }

                emitter.Emit(new SequenceEnd());
            }

            emitter.Emit(new MappingEnd());
        }

        /// <summary>
        /// Serializes a single SourceFileRecord
        /// </summary>
        private static void SerializeSourceFileRecord(IEmitter emitter, SourceFileRecord record)
        {
            emitter.Emit(new MappingStart());

            // Essential properties for queue restoration
            emitter.Emit(new Scalar("name"));
            emitter.Emit(new Scalar(record.Name ?? ""));

            emitter.Emit(new Scalar("filepath"));
            emitter.Emit(new Scalar(record.Filepath ?? ""));

            if (record.ImageType.HasValue)
            {
                emitter.Emit(new Scalar("imageType"));
                emitter.Emit(new Scalar(record.ImageType.Value.ToString()));
            }

            if (record.ArchiveType.HasValue)
            {
                emitter.Emit(new Scalar("archiveType"));
                emitter.Emit(new Scalar(record.ArchiveType.Value.ToString()));
            }

            emitter.Emit(new Scalar("length"));
            emitter.Emit(new Scalar(record.Length.ToString()));

            // Processing status and progress
            emitter.Emit(new Scalar("processingStatus"));
            emitter.Emit(new Scalar(record.ProcessingStatus.ToString()));

            emitter.Emit(new Scalar("progress"));
            emitter.Emit(new Scalar(record.Progress.ToString("F2")));

            // Only serialize non-empty strings to keep file clean
            if (!string.IsNullOrEmpty(record.SourceFileDetails))
            {
                emitter.Emit(new Scalar("sourceFileDetails"));
                emitter.Emit(new Scalar(record.SourceFileDetails));
            }

            if (!string.IsNullOrEmpty(record.OutFileName))
            {
                emitter.Emit(new Scalar("outFileName"));
                emitter.Emit(new Scalar(record.OutFileName));
            }

            if (!string.IsNullOrEmpty(record.VerifyResultMessage))
            {
                emitter.Emit(new Scalar("verifyResultMessage"));
                emitter.Emit(new Scalar(record.VerifyResultMessage));
            }

            emitter.Emit(new MappingEnd());
        }

        /// <summary>
        /// Deserializes the file queue from YAML
        /// </summary>
        private static ObservableCollection<SourceFileRecord> DeserializeFileQueue(IParser parser)
        {
            ObservableCollection<SourceFileRecord> fileQueue = new ObservableCollection<SourceFileRecord>();

            parser.Consume<MappingStart>();

            while (!parser.Accept<MappingEnd>(out _))
            {
                string key = parser.Consume<Scalar>().Value;

                switch (key)
                {
                    case "version":
                        // Skip version for now
                        parser.Consume<Scalar>();
                        break;
                    case "generated":
                        // Skip generation timestamp
                        parser.Consume<Scalar>();
                        break;
                    case "count":
                        // Skip count for now
                        parser.Consume<Scalar>();
                        break;
                    case "queue":
                        DeserializeQueueItems(parser, fileQueue);
                        break;
                    default:
                        SkipValue(parser);
                        break;
                }
            }

            parser.Consume<MappingEnd>();
            return fileQueue;
        }

        /// <summary>
        /// Deserializes the queue items sequence
        /// </summary>
        private static void DeserializeQueueItems(IParser parser, ObservableCollection<SourceFileRecord> fileQueue)
        {
            parser.Consume<SequenceStart>();

            while (!parser.Accept<SequenceEnd>(out _))
            {
                SourceFileRecord record = DeserializeSourceFileRecord(parser);
                if (record != null)
                {
                    fileQueue.Add(record);
                }
            }

            parser.Consume<SequenceEnd>();
        }

        /// <summary>
        /// Deserializes a single SourceFileRecord
        /// </summary>
        private static SourceFileRecord DeserializeSourceFileRecord(IParser parser)
        {
            SourceFileRecord record = new SourceFileRecord();

            parser.Consume<MappingStart>();

            while (!parser.Accept<MappingEnd>(out _))
            {
                string key = parser.Consume<Scalar>().Value;

                switch (key)
                {
                    case "name":
                        record.Name = parser.Consume<Scalar>().Value;
                        break;
                    case "filepath":
                        record.Filepath = parser.Consume<Scalar>().Value;
                        break;
                    case "imageType":
                        if (Enum.TryParse<SourceImageType>(parser.Consume<Scalar>().Value, out SourceImageType imageType))
                            record.ImageType = imageType;
                        break;
                    case "archiveType":
                        if (Enum.TryParse<SourceArchiveType>(parser.Consume<Scalar>().Value, out SourceArchiveType archiveType))
                            record.ArchiveType = archiveType;
                        break;
                    case "length":
                        if (long.TryParse(parser.Consume<Scalar>().Value, out long length))
                            record.Length = length;
                        break;
                    case "processingStatus":
                        if (Enum.TryParse<ProcessingStatus>(parser.Consume<Scalar>().Value, out ProcessingStatus status))
                            record.ProcessingStatus = status;
                        else
                            record.ProcessingStatus = ProcessingStatus.Queued; // Default fallback
                        break;
                    case "progress":
                        if (double.TryParse(parser.Consume<Scalar>().Value, out double progress))
                            record.Progress = progress;
                        break;
                    case "sourceFileDetails":
                        record.SourceFileDetails = parser.Consume<Scalar>().Value;
                        break;
                    case "outFileName":
                        record.OutFileName = parser.Consume<Scalar>().Value;
                        break;
                    case "verifyResultMessage":
                        record.VerifyResultMessage = parser.Consume<Scalar>().Value;
                        break;
                    default:
                        SkipValue(parser);
                        break;
                }
            }

            parser.Consume<MappingEnd>();
            return record;
        }

        /// <summary>
        /// Skips unknown values in the YAML stream
        /// </summary>
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