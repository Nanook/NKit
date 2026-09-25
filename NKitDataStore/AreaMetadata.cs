using System.Buffers.Binary;
using System.Collections;
using System.Text;

namespace NKitDataStore
{
    /// <summary>
    /// Represents a collection of metadata key-value pairs for a disc image area.
    /// Provides dictionary-like access while efficiently encoding to/from compact binary format.
    /// Binary format: [count: byte][type_id: byte][value_len: ushort][value_bytes]...
    /// </summary>
    public class AreaMetadata : IEnumerable<KeyValuePair<AreaValueType, string>>
    {
        private readonly Dictionary<AreaValueType, string> _values;

        /// <summary>
        /// Creates an empty metadata collection.
        /// </summary>
        public AreaMetadata()
        {
            _values = new Dictionary<AreaValueType, string>();
        }

        /// <summary>
        /// Creates a metadata collection with initial values.
        /// </summary>
        public AreaMetadata(Dictionary<AreaValueType, string> values)
        {
            _values = new Dictionary<AreaValueType, string>(values);
        }

        /// <summary>
        /// Gets or sets the value for a specific metadata type.
        /// </summary>
        public string? this[AreaValueType type]
        {
            get => _values.TryGetValue(type, out string? value) ? value : null;
            set
            {
                if (value == null)
                    _values.Remove(type);
                else
                    _values[type] = value;
            }
        }

        /// <summary>
        /// Gets the number of metadata entries.
        /// </summary>
        public int Count => _values.Count;

        /// <summary>
        /// Checks if a metadata type exists.
        /// </summary>
        public bool ContainsKey(AreaValueType type) => _values.ContainsKey(type);

        /// <summary>
        /// Tries to get a value for the specified type.
        /// </summary>
        public bool TryGetValue(AreaValueType type, out string? value) => _values.TryGetValue(type, out value);

        /// <summary>
        /// Adds or updates a metadata value.
        /// </summary>
        public void Set(AreaValueType type, string value) => _values[type] = value ?? throw new ArgumentNullException(nameof(value));

        /// <summary>
        /// Adds or updates a metadata value (numeric).
        /// </summary>
        public void Set(AreaValueType type, long value) => _values[type] = value.ToString();

        /// <summary>
        /// Adds or updates a metadata value (boolean).
        /// </summary>
        public void Set(AreaValueType type, bool value) => _values[type] = value ? "true" : "false";

        /// <summary>
        /// Removes a metadata entry.
        /// </summary>
        public bool Remove(AreaValueType type) => _values.Remove(type);

        /// <summary>
        /// Clears all metadata.
        /// </summary>
        public void Clear() => _values.Clear();

        /// <summary>
        /// Gets a value as a string.
        /// </summary>
        public string? GetString(AreaValueType type) => this[type];

        /// <summary>
        /// Gets a value as a long, or null if not present or not parseable.
        /// </summary>
        public long? GetLong(AreaValueType type)
        {
            string? value = this[type];
            return value != null && long.TryParse(value, out long result) ? result : null;
        }

        /// <summary>
        /// Gets a value as a boolean, or null if not present.
        /// </summary>
        public bool? GetBool(AreaValueType type)
        {
            string? value = this[type];
            if (value == null) return null;
            if (value.Equals("true", StringComparison.OrdinalIgnoreCase)) return true;
            if (value.Equals("false", StringComparison.OrdinalIgnoreCase)) return false;
            return null;
        }

        /// <summary>
        /// Encodes the metadata into compact binary format.
        /// Format: [count: byte][type_id: byte][value_len: ushort][value_bytes]...
        /// </summary>
        public byte[] ToBlob()
        {
            if (_values.Count == 0)
                return Array.Empty<byte>();

            if (_values.Count > 255)
                throw new InvalidOperationException("Maximum 255 metadata entries per area");

            using MemoryStream ms = new MemoryStream();
            using BinaryWriter bw = new BinaryWriter(ms);

            bw.Write((byte)_values.Count);

            // Sort by key for deterministic output
            foreach ((AreaValueType type, string? value) in _values.OrderBy(kvp => kvp.Key))
            {
                bw.Write((byte)type); // Enum value (0-255)

                byte[] valueBytes = Encoding.UTF8.GetBytes(value);
                if (valueBytes.Length > ushort.MaxValue)
                    throw new InvalidOperationException($"Value too large for {type}: {valueBytes.Length} bytes (max {ushort.MaxValue})");

                // Write length (big-endian)
                byte[] lenBytes = new byte[2];
                BinaryPrimitives.WriteUInt16BigEndian(lenBytes, (ushort)valueBytes.Length);
                bw.Write(lenBytes);

                bw.Write(valueBytes);
            }

            return ms.ToArray();
        }

        /// <summary>
        /// Decodes metadata from binary format.
        /// </summary>
        public static AreaMetadata FromBlob(byte[]? blob)
        {
            if (blob == null || blob.Length == 0)
                return new AreaMetadata();

            AreaMetadata result = new AreaMetadata();

            using MemoryStream ms = new MemoryStream(blob);
            using BinaryReader br = new BinaryReader(ms);

            byte count = br.ReadByte();

            for (int i = 0; i < count; i++)
            {
                AreaValueType type = (AreaValueType)br.ReadByte();

                // Read length (big-endian)
                byte[] lenBytes = br.ReadBytes(2);
                ushort length = BinaryPrimitives.ReadUInt16BigEndian(lenBytes);

                byte[] valueBytes = br.ReadBytes(length);
                string value = Encoding.UTF8.GetString(valueBytes);

                result._values[type] = value;
            }

            return result;
        }

        /// <summary>
        /// Gets a single value from a blob without decoding the entire structure.
        /// Useful for efficient single-value lookups.
        /// </summary>
        public static string? GetValueFromBlob(byte[]? blob, AreaValueType type)
        {
            if (blob == null || blob.Length == 0)
                return null;

            using MemoryStream ms = new MemoryStream(blob);
            using BinaryReader br = new BinaryReader(ms);

            byte count = br.ReadByte();

            for (int i = 0; i < count; i++)
            {
                AreaValueType currentType = (AreaValueType)br.ReadByte();

                // Read length (big-endian)
                byte[] lenBytes = br.ReadBytes(2);
                ushort length = BinaryPrimitives.ReadUInt16BigEndian(lenBytes);

                if (currentType == type)
                {
                    byte[] valueBytes = br.ReadBytes(length);
                    return Encoding.UTF8.GetString(valueBytes);
                }
                else
                {
                    // Skip this value
                    ms.Position += length;
                }
            }

            return null;
        }

        /// <summary>
        /// Creates a copy of this metadata.
        /// </summary>
        public AreaMetadata Clone() => new AreaMetadata(new Dictionary<AreaValueType, string>(_values));

        public IEnumerator<KeyValuePair<AreaValueType, string>> GetEnumerator() => _values.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public override string ToString() => $"AreaMetadata[{_values.Count} entries]";
    }
}