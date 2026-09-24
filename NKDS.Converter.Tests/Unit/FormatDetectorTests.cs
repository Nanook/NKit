using static NKDS.Converter.FormatDetector;

namespace NKDS.Converter.Tests.Unit;

public class FormatDetectorTests : IDisposable
{
    private readonly string _tempDir;

    public FormatDetectorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "FormatDetectorTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private string CreateTempFile(byte[] content)
    {
        string path = Path.Combine(_tempDir, Guid.NewGuid().ToString("N") + ".nkds");
        File.WriteAllBytes(path, content);
        return path;
    }

    [Fact]
    public void Detect_FileNotFound_ThrowsFileNotFoundException()
    {
        string nonExistentPath = Path.Combine(_tempDir, "does_not_exist.nkds");
        Assert.Throws<FileNotFoundException>(() => FormatDetector.Detect(nonExistentPath));
    }

    [Fact]
    public void Detect_SqliteMagic_ReturnsSqlite()
    {
        // "SQLite format 3\0" followed by some extra bytes
        byte[] content = "SQLite format 3\0"u8.ToArray().Concat(new byte[100]).ToArray();
        string path = CreateTempFile(content);

        IndexFormat result = FormatDetector.Detect(path);

        Assert.Equal(IndexFormat.Sqlite, result);
    }

    [Fact]
    public void Detect_NkdsBinaryMagic_ReturnsBinary()
    {
        // 0x4E4B4453 ("NKDS") followed by some data
        byte[] content = new byte[] { 0x4E, 0x4B, 0x44, 0x53 }.Concat(new byte[100]).ToArray();
        string path = CreateTempFile(content);

        IndexFormat result = FormatDetector.Detect(path);

        Assert.Equal(IndexFormat.Binary, result);
    }

    [Fact]
    public void Detect_EmbeddedFooterMagic_ReturnsEmbeddedBinary()
    {
        // Random header (not SQLite or NKDS) with NKDS footer
        byte[] content = new byte[100];
        content[0] = 0xFF; // Ensure it doesn't match other magics
        // Write NKDS magic at the end
        content[96] = 0x4E;
        content[97] = 0x4B;
        content[98] = 0x44;
        content[99] = 0x53;
        string path = CreateTempFile(content);

        IndexFormat result = FormatDetector.Detect(path);

        Assert.Equal(IndexFormat.EmbeddedBinary, result);
    }

    [Fact]
    public void Detect_UnknownFormat_ReturnsUnknown()
    {
        // Random bytes that don't match any magic
        byte[] content = new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08,
                                   0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F, 0x10,
                                   0x11, 0x12, 0x13, 0x14 };
        string path = CreateTempFile(content);

        IndexFormat result = FormatDetector.Detect(path);

        Assert.Equal(IndexFormat.Unknown, result);
    }

    [Fact]
    public void Detect_FileTooSmall_ReturnsUnknown()
    {
        // Only 3 bytes - too small for any magic check
        byte[] content = new byte[] { 0x01, 0x02, 0x03 };
        string path = CreateTempFile(content);

        IndexFormat result = FormatDetector.Detect(path);

        Assert.Equal(IndexFormat.Unknown, result);
    }

    [Fact]
    public void Detect_EmptyFile_ReturnsUnknown()
    {
        string path = CreateTempFile([]);

        IndexFormat result = FormatDetector.Detect(path);

        Assert.Equal(IndexFormat.Unknown, result);
    }

    [Fact]
    public void Detect_ExactlyFourBytes_NkdsMagic_ReturnsBinary()
    {
        byte[] content = new byte[] { 0x4E, 0x4B, 0x44, 0x53 };
        string path = CreateTempFile(content);

        IndexFormat result = FormatDetector.Detect(path);

        Assert.Equal(IndexFormat.Binary, result);
    }

    [Fact]
    public void Detect_SqliteMagicTakesPriorityOverFooter()
    {
        // SQLite magic at start AND NKDS footer at end - SQLite should win
        byte[] sqliteMagic = "SQLite format 3\0"u8.ToArray();
        byte[] content = new byte[100];
        Array.Copy(sqliteMagic, content, 16);
        // Also put NKDS at the end
        content[96] = 0x4E;
        content[97] = 0x4B;
        content[98] = 0x44;
        content[99] = 0x53;
        string path = CreateTempFile(content);

        IndexFormat result = FormatDetector.Detect(path);

        Assert.Equal(IndexFormat.Sqlite, result);
    }

    [Fact]
    public void Detect_BinaryMagicTakesPriorityOverFooter()
    {
        // NKDS magic at start AND NKDS footer at end - Binary should win (checked first)
        byte[] content = new byte[100];
        content[0] = 0x4E;
        content[1] = 0x4B;
        content[2] = 0x44;
        content[3] = 0x53;
        // Also put NKDS at the end
        content[96] = 0x4E;
        content[97] = 0x4B;
        content[98] = 0x44;
        content[99] = 0x53;
        string path = CreateTempFile(content);

        IndexFormat result = FormatDetector.Detect(path);

        Assert.Equal(IndexFormat.Binary, result);
    }
}