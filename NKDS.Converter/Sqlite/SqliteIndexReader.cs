using Microsoft.Data.Sqlite;
using NKitDataStore;
using NKitDataStore.Binary;

namespace NKDS.Converter.Sqlite;

/// <summary>
/// Reads data from a SQLite index file using Microsoft.Data.Sqlite.
/// Implements streaming/batched reads for large datasets.
/// </summary>
public class SqliteIndexReader : IDisposable
{
    private readonly SqliteConnection _connection;

    public SqliteIndexReader(string filePath)
    {
        string connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = filePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false
        }.ToString();

        _connection = new SqliteConnection(connectionString);
        _connection.Open();
    }

    /// <summary>
    /// Reads the info table record.
    /// </summary>
    public InfoRecord ReadInfo()
    {
        using SqliteCommand cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT version, shard_size, block_size, max_offset_blocks FROM info LIMIT 1";

        using SqliteDataReader reader = cmd.ExecuteReader();
        if (!reader.Read())
            throw new InvalidOperationException("No info record found in database.");

        return new InfoRecord
        {
            Version = reader.GetInt64(0),
            ShardSize = reader.GetInt64(1),
            BlockSize = reader.GetInt32(2),
            MaxOffsetBlocks = reader.GetInt32(3)
        };
    }

    /// <summary>
    /// Reads all images ordered by id ASC.
    /// </summary>
    public IEnumerable<ImageRecord> ReadAllImages()
    {
        using SqliteCommand cmd = _connection.CreateCommand();
        cmd.CommandText = @"SELECT id, name, size, crc32, xxhash64, system_id, format_id, 
                                   rollback_file_id, rollback_offset, removed 
                            FROM image ORDER BY id ASC";

        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            yield return ReadImageRecord(reader);
        }
    }

    /// <summary>
    /// Reads all areas for a specific image.
    /// </summary>
    public IEnumerable<AreaRecord> ReadAreasForImage(long imageId)
    {
        using SqliteCommand cmd = _connection.CreateCommand();
        cmd.CommandText = @"SELECT id, image_id, offset, size, stride_block_size, stride_data_offset, 
                                   stride_data_length, section_size, crc32, xxhash64, metadata 
                            FROM area WHERE image_id = @imageId ORDER BY id ASC";
        cmd.Parameters.AddWithValue("@imageId", imageId);

        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            yield return ReadAreaRecord(reader);
        }
    }

    /// <summary>
    /// Reads all offsets for a specific image, decoding the blocks BLOB via BlocksBlob.Decode.
    /// </summary>
    public IEnumerable<OffsetRecord> ReadOffsetsForImage(long imageId)
    {
        using SqliteCommand cmd = _connection.CreateCommand();
        cmd.CommandText = @"SELECT image_id, offset, size, type_id, offset_start, blocks 
                            FROM offset WHERE image_id = @imageId ORDER BY offset ASC";
        cmd.Parameters.AddWithValue("@imageId", imageId);

        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            yield return ReadOffsetRecord(reader);
        }
    }

    /// <summary>
    /// Reads all file records for a specific image.
    /// </summary>
    public IEnumerable<FileRecord> ReadFilesForImage(long imageId)
    {
        using SqliteCommand cmd = _connection.CreateCommand();
        cmd.CommandText = @"SELECT image_id, name, file_id, offset, size, uncompressed_size, is_system 
                            FROM file WHERE image_id = @imageId ORDER BY name ASC";
        cmd.Parameters.AddWithValue("@imageId", imageId);

        using SqliteDataReader reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            yield return ReadFileRecord(reader);
        }
    }

    /// <summary>
    /// Streams all block rows in batches for memory efficiency.
    /// Uses LIMIT/OFFSET pagination to avoid loading all rows at once.
    /// </summary>
    internal IEnumerable<BlockIndexEntry> ReadAllBlocks(int batchSize = 100_000)
    {
        long offset = 0;

        while (true)
        {
            int count = 0;

            using SqliteCommand cmd = _connection.CreateCommand();
            cmd.CommandText = @"SELECT hash, file_id, offset, size FROM block LIMIT @limit OFFSET @offset";
            cmd.Parameters.AddWithValue("@limit", batchSize);
            cmd.Parameters.AddWithValue("@offset", offset);

            using SqliteDataReader reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                byte[] hashBlob = (byte[])reader["hash"];
                BlockKey key = HashBlob.Decode(hashBlob);

                yield return new BlockIndexEntry
                {
                    Key = key,
                    FileId = reader.GetInt32(1),
                    Offset = reader.GetInt64(2),
                    Size = reader.GetInt32(3)
                };

                count++;
            }

            if (count < batchSize)
                break;

            offset += batchSize;
        }
    }

    /// <summary>
    /// Gets the total number of images.
    /// </summary>
    public long GetImageCount()
    {
        using SqliteCommand cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM image";
        return (long)cmd.ExecuteScalar()!;
    }

    /// <summary>
    /// Gets the total number of blocks.
    /// </summary>
    public long GetBlockCount()
    {
        using SqliteCommand cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM block";
        return (long)cmd.ExecuteScalar()!;
    }

    private static ImageRecord ReadImageRecord(SqliteDataReader reader)
    {
        return new ImageRecord
        {
            Id = reader.GetInt64(0),
            Name = reader.GetString(1),
            Size = reader.GetInt64(2),
            Crc32 = (uint)(long)reader["crc32"],
            XxHash64 = (ulong)(long)reader["xxhash64"],
            System = reader.IsDBNull(5) ? null : ((NKitDataStore.System)reader.GetInt32(5)).ToString(),
            Format = reader.IsDBNull(6) ? ImageFormat.Unknown : (ImageFormat)reader.GetInt32(6),
            RollbackFileId = reader.IsDBNull(7) ? null : reader.GetInt32(7),
            RollbackOffset = reader.IsDBNull(8) ? null : reader.GetInt64(8),
            Removed = reader.GetInt64(9) != 0
        };
    }

    private static AreaRecord ReadAreaRecord(SqliteDataReader reader)
    {
        // Handle null metadata BLOB → empty AreaMetadata (Req 9.3)
        byte[]? metadataBlob = reader.IsDBNull(10) ? null : (byte[])reader["metadata"];

        return new AreaRecord
        {
            Id = reader.GetInt64(0),
            ImageId = reader.GetInt64(1),
            Offset = reader.GetInt64(2),
            Size = reader.GetInt64(3),
            StrideBlockSize = reader.IsDBNull(4) ? 0 : reader.GetInt32(4),
            StrideDataOffset = reader.IsDBNull(5) ? 0 : reader.GetInt32(5),
            StrideDataLength = reader.IsDBNull(6) ? 0 : reader.GetInt32(6),
            SectionSize = reader.IsDBNull(7) ? 0 : reader.GetInt32(7),
            Crc32 = (uint)(long)reader["crc32"],
            XxHash64 = (ulong)(long)reader["xxhash64"],
            Metadata = AreaMetadata.FromBlob(metadataBlob)
        };
    }

    private static OffsetRecord ReadOffsetRecord(SqliteDataReader reader)
    {
        // Decode blocks BLOB using BlocksBlob.Decode
        byte[]? blocksBlob = reader.IsDBNull(5) ? null : (byte[])reader["blocks"];

        return new OffsetRecord
        {
            ImageId = reader.GetInt64(0),
            Offset = reader.GetInt64(1),
            Size = reader.GetInt64(2),
            Type = (BlockType)reader.GetInt32(3),
            OffsetStart = reader.GetInt64(4),
            Blocks = BlocksBlob.Decode(blocksBlob)
        };
    }

    private static FileRecord ReadFileRecord(SqliteDataReader reader)
    {
        return new FileRecord
        {
            ImageId = reader.GetInt64(0),
            Name = reader.GetString(1),
            FileId = reader.GetInt32(2),
            Offset = reader.GetInt64(3),
            Size = reader.GetInt64(4),
            UncompressedSize = reader.GetInt64(5),
            IsSystem = reader.GetInt64(6) != 0
        };
    }

    public void Dispose() => _connection.Dispose();
}