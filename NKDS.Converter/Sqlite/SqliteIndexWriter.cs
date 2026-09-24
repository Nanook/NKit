using Microsoft.Data.Sqlite;
using NKitDataStore;
using NKitDataStore.Binary;

namespace NKDS.Converter.Sqlite;

/// <summary>
/// Writes data to a SQLite index file with the standard schema.
/// Uses transactions and batch inserts for performance.
/// </summary>
public class SqliteIndexWriter : IDisposable
{
    private readonly SqliteConnection _connection;
    private SqliteTransaction _transaction;

    public SqliteIndexWriter(string filePath)
    {
        string connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = filePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString();

        _connection = new SqliteConnection(connectionString);
        _connection.Open();
        _transaction = _connection.BeginTransaction();
    }

    /// <summary>
    /// Creates the standard SQLite schema with all tables, primary keys, and indexes.
    /// </summary>
    public void CreateSchema()
    {
        using SqliteCommand cmd = _connection.CreateCommand();
        cmd.Transaction = _transaction;
        cmd.CommandText = @"
            CREATE TABLE info (
                version INTEGER NOT NULL,
                shard_size INTEGER NOT NULL,
                block_size INTEGER NOT NULL DEFAULT 65536,
                max_offset_blocks INTEGER NOT NULL
            );

            CREATE TABLE image (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                size INTEGER NOT NULL,
                crc32 INTEGER NOT NULL,
                xxhash64 INTEGER NOT NULL,
                system_id INTEGER,
                format_id INTEGER,
                rollback_file_id INTEGER DEFAULT NULL,
                rollback_offset INTEGER DEFAULT NULL,
                removed INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE area (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                image_id INTEGER NOT NULL,
                offset INTEGER NOT NULL,
                size INTEGER NOT NULL,
                stride_block_size INTEGER,
                stride_data_offset INTEGER,
                stride_data_length INTEGER,
                section_size INTEGER,
                crc32 INTEGER NOT NULL,
                xxhash64 INTEGER NOT NULL,
                metadata BLOB,
                FOREIGN KEY(image_id) REFERENCES image(id) ON DELETE CASCADE
            );

            CREATE TABLE offset (
                image_id INTEGER NOT NULL,
                offset INTEGER NOT NULL,
                size INTEGER NOT NULL,
                type_id INTEGER NOT NULL,
                offset_start INTEGER NOT NULL DEFAULT 0,
                blocks BLOB,
                PRIMARY KEY(image_id, offset)
            );

            CREATE TABLE file (
                image_id INTEGER NOT NULL,
                name TEXT NOT NULL,
                file_id INTEGER NOT NULL,
                offset INTEGER NOT NULL,
                size INTEGER NOT NULL,
                uncompressed_size INTEGER NOT NULL,
                is_system INTEGER NOT NULL DEFAULT 0,
                PRIMARY KEY(image_id, name)
            ) WITHOUT ROWID;

            CREATE TABLE block (
                hash BLOB PRIMARY KEY,
                file_id INTEGER NOT NULL,
                offset INTEGER NOT NULL,
                size INTEGER NOT NULL
            ) WITHOUT ROWID;
        ";
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Writes the info record with version, shard_size, block_size, and max_offset_blocks.
    /// </summary>
    public void WriteInfo(InfoRecord info)
    {
        using SqliteCommand cmd = _connection.CreateCommand();
        cmd.Transaction = _transaction;
        cmd.CommandText = @"INSERT INTO info (version, shard_size, block_size, max_offset_blocks) 
                            VALUES (@version, @shardSize, @blockSize, @maxOffsetBlocks)";
        cmd.Parameters.AddWithValue("@version", info.Version);
        cmd.Parameters.AddWithValue("@shardSize", info.ShardSize);
        cmd.Parameters.AddWithValue("@blockSize", info.BlockSize);
        cmd.Parameters.AddWithValue("@maxOffsetBlocks", info.MaxOffsetBlocks);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Writes an image record with explicit id insertion to preserve original IDs.
    /// Performs unchecked ulong→long cast for xxhash64 (Req 9.6).
    /// </summary>
    public void WriteImage(ImageRecord image)
    {
        using SqliteCommand cmd = _connection.CreateCommand();
        cmd.Transaction = _transaction;
        cmd.CommandText = @"INSERT INTO image (id, name, size, crc32, xxhash64, system_id, format_id, 
                                              rollback_file_id, rollback_offset, removed) 
                            VALUES (@id, @name, @size, @crc32, @xxhash64, @systemId, @formatId, 
                                    @rollbackFileId, @rollbackOffset, @removed)";
        cmd.Parameters.AddWithValue("@id", image.Id);
        cmd.Parameters.AddWithValue("@name", image.Name);
        cmd.Parameters.AddWithValue("@size", image.Size);
        cmd.Parameters.AddWithValue("@crc32", (long)image.Crc32);
        cmd.Parameters.AddWithValue("@xxhash64", unchecked((long)image.XxHash64));
        cmd.Parameters.AddWithValue("@systemId", image.System != null
            ? (object)(int)Enum.Parse<NKitDataStore.System>(image.System)
            : DBNull.Value);
        cmd.Parameters.AddWithValue("@formatId", (int)image.Format);
        cmd.Parameters.AddWithValue("@rollbackFileId", image.RollbackFileId.HasValue
            ? (object)image.RollbackFileId.Value
            : DBNull.Value);
        cmd.Parameters.AddWithValue("@rollbackOffset", image.RollbackOffset.HasValue
            ? (object)image.RollbackOffset.Value
            : DBNull.Value);
        cmd.Parameters.AddWithValue("@removed", image.Removed ? 1 : 0);
        cmd.ExecuteNonQuery();
    }

    /// <summary>
    /// Writes area records for an image. Empty/null AreaMetadata → NULL in the column (Req 9.4).
    /// </summary>
    public void WriteAreas(long imageId, IEnumerable<AreaRecord> areas)
    {
        using SqliteCommand cmd = _connection.CreateCommand();
        cmd.Transaction = _transaction;
        cmd.CommandText = @"INSERT INTO area (image_id, offset, size, stride_block_size, stride_data_offset, 
                                             stride_data_length, section_size, crc32, xxhash64, metadata) 
                            VALUES (@imageId, @offset, @size, @strideBlockSize, @strideDataOffset, 
                                    @strideDataLength, @sectionSize, @crc32, @xxhash64, @metadata)";

        SqliteParameter pImageId = cmd.Parameters.Add("@imageId", SqliteType.Integer);
        SqliteParameter pOffset = cmd.Parameters.Add("@offset", SqliteType.Integer);
        SqliteParameter pSize = cmd.Parameters.Add("@size", SqliteType.Integer);
        SqliteParameter pStrideBlockSize = cmd.Parameters.Add("@strideBlockSize", SqliteType.Integer);
        SqliteParameter pStrideDataOffset = cmd.Parameters.Add("@strideDataOffset", SqliteType.Integer);
        SqliteParameter pStrideDataLength = cmd.Parameters.Add("@strideDataLength", SqliteType.Integer);
        SqliteParameter pSectionSize = cmd.Parameters.Add("@sectionSize", SqliteType.Integer);
        SqliteParameter pCrc32 = cmd.Parameters.Add("@crc32", SqliteType.Integer);
        SqliteParameter pXxHash64 = cmd.Parameters.Add("@xxhash64", SqliteType.Integer);
        SqliteParameter pMetadata = cmd.Parameters.Add("@metadata", SqliteType.Blob);

        foreach (AreaRecord area in areas)
        {
            pImageId.Value = imageId;
            pOffset.Value = area.Offset;
            pSize.Value = area.Size;
            pStrideBlockSize.Value = area.StrideBlockSize != 0 ? (object)area.StrideBlockSize : DBNull.Value;
            pStrideDataOffset.Value = area.StrideDataOffset != 0 ? (object)area.StrideDataOffset : DBNull.Value;
            pStrideDataLength.Value = area.StrideDataLength != 0 ? (object)area.StrideDataLength : DBNull.Value;
            pSectionSize.Value = area.SectionSize != 0 ? (object)area.SectionSize : DBNull.Value;
            pCrc32.Value = (long)area.Crc32;
            pXxHash64.Value = unchecked((long)area.XxHash64);

            // Empty/null AreaMetadata → write NULL to the column (Req 9.4)
            byte[] metadataBlob = area.Metadata.ToBlob();
            pMetadata.Value = metadataBlob.Length > 0 ? (object)metadataBlob : DBNull.Value;

            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Writes offset records for an image. Uses BlocksBlob.Encode for the blocks BLOB (Req 8.2, 8.6).
    /// </summary>
    public void WriteOffsets(long imageId, IEnumerable<OffsetRecord> offsets)
    {
        using SqliteCommand cmd = _connection.CreateCommand();
        cmd.Transaction = _transaction;
        cmd.CommandText = @"INSERT INTO offset (image_id, offset, size, type_id, offset_start, blocks) 
                            VALUES (@imageId, @offset, @size, @typeId, @offsetStart, @blocks)";

        SqliteParameter pImageId = cmd.Parameters.Add("@imageId", SqliteType.Integer);
        SqliteParameter pOffset = cmd.Parameters.Add("@offset", SqliteType.Integer);
        SqliteParameter pSize = cmd.Parameters.Add("@size", SqliteType.Integer);
        SqliteParameter pTypeId = cmd.Parameters.Add("@typeId", SqliteType.Integer);
        SqliteParameter pOffsetStart = cmd.Parameters.Add("@offsetStart", SqliteType.Integer);
        SqliteParameter pBlocks = cmd.Parameters.Add("@blocks", SqliteType.Blob);

        foreach (OffsetRecord offset in offsets)
        {
            pImageId.Value = imageId;
            pOffset.Value = offset.Offset;
            pSize.Value = offset.Size;
            pTypeId.Value = (int)offset.Type;
            pOffsetStart.Value = offset.OffsetStart;

            // Empty list → null BLOB (Req 8.6)
            byte[]? blocksBlob = BlocksBlob.Encode(offset.Blocks);
            pBlocks.Value = blocksBlob != null ? (object)blocksBlob : DBNull.Value;

            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Writes file records for an image, preserving is_system (Req 11.2).
    /// </summary>
    public void WriteFiles(long imageId, IEnumerable<FileRecord> files)
    {
        using SqliteCommand cmd = _connection.CreateCommand();
        cmd.Transaction = _transaction;
        cmd.CommandText = @"INSERT INTO file (image_id, name, file_id, offset, size, uncompressed_size, is_system) 
                            VALUES (@imageId, @name, @fileId, @offset, @size, @uncompressedSize, @isSystem)";

        SqliteParameter pImageId = cmd.Parameters.Add("@imageId", SqliteType.Integer);
        SqliteParameter pName = cmd.Parameters.Add("@name", SqliteType.Text);
        SqliteParameter pFileId = cmd.Parameters.Add("@fileId", SqliteType.Integer);
        SqliteParameter pOffset = cmd.Parameters.Add("@offset", SqliteType.Integer);
        SqliteParameter pSize = cmd.Parameters.Add("@size", SqliteType.Integer);
        SqliteParameter pUncompressedSize = cmd.Parameters.Add("@uncompressedSize", SqliteType.Integer);
        SqliteParameter pIsSystem = cmd.Parameters.Add("@isSystem", SqliteType.Integer);

        foreach (FileRecord file in files)
        {
            pImageId.Value = imageId;
            pName.Value = file.Name;
            pFileId.Value = file.FileId;
            pOffset.Value = file.Offset;
            pSize.Value = file.Size;
            pUncompressedSize.Value = file.UncompressedSize;
            pIsSystem.Value = file.IsSystem ? 1 : 0;

            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Writes block index entries in batches within a transaction for performance.
    /// Uses HashBlob.Encode to convert BlockKey to 12-byte BLOB (Req 8.4).
    /// </summary>
    internal void WriteBlocks(IEnumerable<BlockIndexEntry> blocks, int batchSize = 10_000)
    {
        using SqliteCommand cmd = _connection.CreateCommand();
        cmd.Transaction = _transaction;
        cmd.CommandText = @"INSERT OR IGNORE INTO block (hash, file_id, offset, size) 
                            VALUES (@hash, @fileId, @offset, @size)";

        SqliteParameter pHash = cmd.Parameters.Add("@hash", SqliteType.Blob);
        SqliteParameter pFileId = cmd.Parameters.Add("@fileId", SqliteType.Integer);
        SqliteParameter pOffset = cmd.Parameters.Add("@offset", SqliteType.Integer);
        SqliteParameter pSize = cmd.Parameters.Add("@size", SqliteType.Integer);

        int count = 0;
        foreach (BlockIndexEntry block in blocks)
        {
            pHash.Value = HashBlob.Encode(block.Key);
            pFileId.Value = block.FileId;
            pOffset.Value = block.Offset;
            pSize.Value = block.Size;

            cmd.ExecuteNonQuery();
            count++;
        }
    }

    /// <summary>
    /// Commits the transaction.
    /// </summary>
    public void Commit() => _transaction.Commit();

    public void Dispose()
    {
        _transaction.Dispose();
        _connection.Dispose();
    }
}