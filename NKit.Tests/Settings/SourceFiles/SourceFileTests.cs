using Nanook.NKit;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Xunit;


namespace NKit.Tests.Settings.SourceFiles
{
    [Trait("Area", "Settings")]
    [Trait("Group", "SourceFiles")]
    public class SourceFileTests
    {
        private readonly static string[] _TestPaths = {
            @"<path>\aaa",
            @"<path>\bbb",
            @"<path>\ccc",
            @"<path>\ddd",
            @"<path>\rar",
            @"<path>\rar\FileName.zip",
        };


        private readonly static string[] _TestFiles = {
            @"<path>\aaa\aaa.wbf1",
            @"<path>\bbb\bbb.wbfs",
            @"<path>\bbb\ccc.wbfs",
            @"<path>\aaa\aaa.wbfs",
            @"<path>\ccc\ccc.wbfs",
            @"<path>\aaa\ddd.wbfs",
            @"<path>\ddd\aaa.wbfs",
            @"<path>\rar\FileName.rar",
            @"<path>\rar\FileName.r00",
            @"<path>\rar\FileName.r01",
            @"<path>\rar\FileName.r02",
            @"<path>\rar\FileName2.rar",
            @"<path>\rar\FileName2.r00",
            @"<path>\rar\FileName2.r01",
            @"<path>\rar\FileName2.r02",
            @"<path>\rar\FileName2.z01",
            @"<path>\rar\FileName2.z02",
            @"<path>\rar\FileName3.rar",
            @"<path>\rar\FileName2.z03",
            @"<path>\rar\FileName2.z04",
            @"<path>\rar\filename3.rar",
            @"<path>\rar\FileName2.iso",
            @"<path>\rar\FileName.part01.rar",
            @"<path>\rar\FileName.part02.rar",
            @"<path>\rar\FileName.part03.rar",
            @"<path>\rar\FileName.part04.rar",
            @"<path>\rar\FileName2.part01.rar",
            @"<path>\rar\FileName2.part03.rar",
            @"<path>\rar\FileName2.part04.rar",
            @"<path>\rar\FileName2.part02.rar",
            @"<path>\rar\FileName2.z05",
            @"<path>\rar\FileName.zip\FileName.zip",
            @"<path>\rar\FileName.zip\FileName.rar",
            @"<path>\aaa\aaa.log"
        };


        [Theory]
        [InlineData(0x8000, 10, 4)] //split file in split zips
        [InlineData(0x8000, 10, 1)] //split file in single zip
        [InlineData(0x8000, 1, 4)] //single file in split zips
        public void SplitArchiveAndContainedFilesTests(int containedSize, int parts, int zipParts)
        {
            DirectoryInfo basePath = Directory.CreateDirectory(Path.Combine(".", $"{nameof(SplitArchiveAndContainedFilesTests)}_{Guid.NewGuid():N}"));

            string[] filenames;
            //create a split up zip contianing a split file. The file contains 4 byte offsets written as the content
            FileInfo[] files = TestUtils.CreateZip(Path.Combine(basePath.FullName, "Test.zip"), zipParts, parts, (int)containedSize, out filenames);
            long[] sizes = filenames.Select(a => (long)containedSize).ToArray(); //array of sizes (ours are all the same for ease)

            byte[] data;
            using (Stream s = SourceStream.OpenArchive(files, filenames, sizes))
            {
                using (MemoryStream ms = new MemoryStream())
                {
                    //extract 10 parts as one file
                    s.CopyTo(ms);
                    data = ms.ToArray();
                }
            }
            //check the size
            int sz = (int)(containedSize * parts);
            Assert.Equal(sz, data.Length);
            long p = 0;
            while (p < sz)
            {
                //check all the data is correct
                Assert.Equal(p, (long)data.ReadUInt32B((int)p));
                p += 4;
            }

            basePath.Delete(true);
        }

        [Fact]
        public void MultiSystemProcessTest()
        {
            DirectoryInfo basePath = Directory.CreateDirectory(Path.Combine(".", $"{nameof(SourceFilesManualAddTest)}_{Guid.NewGuid():N}"));
            try
            {
                TestImageBuilder.CreateImageGcBasic(Path.Combine(basePath.FullName, "GcBasic.iso"));
                TestImageBuilder.CreateImageWiiBasic(Path.Combine(basePath.FullName, "WiiBasic.iso"));
                TestImageBuilder.ConvertImage(Path.Combine(basePath.FullName, "GcBasic.iso"), "rvz:zstd:4:32k:1");
                TestImageBuilder.ConvertImage(Path.Combine(basePath.FullName, "WiiBasic.iso"), "rvz:zstd:4:32k:1");
                TestImageBuilder.CreateImageWiiBasic(Path.Combine(basePath.FullName, "WiiBasic.iso"));
                TestImageBuilder.CreateImageGcBasicSingleRar(Path.Combine(basePath.FullName, "GcBasic.rar"));
                TestImageBuilder.CreateImageWiiBasic2PartRar(Path.Combine(basePath.FullName, "WiiBasic.part1.rar"),
                                                             Path.Combine(basePath.FullName, "WiiBasic.part2.rar"));

                SystemPresetSettings presets = new SystemPresetSettings()
                {
                    Task = TaskType.Scan,
                    ScanOut = basePath.FullName,
                    Results = true,
                    ResultsOut = Path.Combine(basePath.FullName, "Results.txt"),
                    LogOut = Path.Combine(basePath.FullName, "Log.txt"),
                    LogOutLevel = LogLevel.Info,
                    R = true,
                    Arc = true
                };
                presets.In.Add(basePath.FullName);
                AppSettings settings = new AppSettings(presets);
                NKitTaskResults results;
                StringBuilder logOutput = new StringBuilder();

                CancellationTokenSource cancel = new CancellationTokenSource();

                using (Log log = settings.GetLog((ms, lv) => Console.Write(ms)))
                {
                    List<SourceFile> images = Nanook.NKit.SourceFiles.Scan(settings.In, settings.R, settings.Arc, true, log, null).OrderBy(a => a.Name).ToList();
                    foreach (SourceFile file in images)
                    {
                        NKitProcessor p = new NKitProcessor(settings, file, (ms, lv) => Trace.Write(logOutput.Append(ms)));

                        // look at results class to see if it was successful
                        results = p.Process(cancel.Token);
                    }
                }

                Assert.True(File.Exists(Path.Combine(basePath.FullName, "GcBasic.nkit.yaml")));
                Assert.True(File.Exists(Path.Combine(basePath.FullName, "WiiBasic.nkit.yaml")));
                Assert.True(File.Exists(Path.Combine(basePath.FullName, "WiiDemoImage.nkit.yaml"))); //name used in the rar

            }
            finally
            {
                basePath.Delete(true);
            }
        }


        [Fact]
        public void SourceFilesManualAddTest()
        {

            DirectoryInfo basePath = Directory.CreateDirectory(Path.Combine(".", $"{nameof(SourceFilesManualAddTest)}_{Guid.NewGuid():N}"));
            try
            {
                TestImageBuilder.CreateImageGcBasic(Path.Combine(basePath.FullName, "GcBasic.iso"));
                TestImageBuilder.CreateImageWiiBasic(Path.Combine(basePath.FullName, "WiiBasic.iso"));
                TestImageBuilder.CreateImageGcBasicSingleRar(Path.Combine(basePath.FullName, "GcBasic.rar"));
                TestImageBuilder.CreateImageWiiBasic2PartRar(Path.Combine(basePath.FullName, "WiiBasic.part1.rar"),
                                                             Path.Combine(basePath.FullName, "WiiBasic.part2.rar"));
                SourceFileSystem sfs = new SourceFileSystem(null);
                FileMask mask = FileMask.CreateLocalMask(basePath.FullName, false);
                TestUtils.RecurseDirectory(new DirectoryInfo(basePath.FullName), f =>
                {
                    sfs.AddFile(f, -1, mask); //-1 use f.Length
                });

                List<FileItem> files = sfs.GetFiles(new FileMask[] { mask }, null);

                Assert.Equal(5, files.Count);
                Assert.Equal(5, files.Count(a => a.IsMatch));

                // Find the two split-rar parts regardless of ordering (Linux dir enumeration
                // does not guarantee the same order as Windows).
                FileItem part1 = files.FirstOrDefault(f => f.FileName.EndsWith(".part1.rar", StringComparison.OrdinalIgnoreCase));
                FileItem part2 = files.FirstOrDefault(f => f.FileName.EndsWith(".part2.rar", StringComparison.OrdinalIgnoreCase));
                FileItem standalone = files.FirstOrDefault(f => !f.FileName.EndsWith(".part1.rar", StringComparison.OrdinalIgnoreCase)
                                                               && !f.FileName.EndsWith(".part2.rar", StringComparison.OrdinalIgnoreCase)
                                                               && f.FileName.EndsWith(".rar", StringComparison.OrdinalIgnoreCase));
                Assert.NotNull(part1);
                Assert.NotNull(part2);
                Assert.NotNull(standalone);
                Assert.True(part1.IsPart(part2));
                Assert.True(part2.IsPart(part1));
                Assert.False(standalone.IsPart(part1));
            }
            finally
            {
                basePath.Delete(true);
            }
        }

        [Fact]
        public void SourceFilesScanTest()
        {
            DirectoryInfo basePath = Directory.CreateDirectory(Path.Combine(".", $"{nameof(SourceFilesScanTest)}_{Guid.NewGuid():N}"));
            try
            {
                TestImageBuilder.CreateImageGcBasic(Path.Combine(basePath.FullName, "GcBasic.iso"));
                TestImageBuilder.CreateImageWiiBasic(Path.Combine(basePath.FullName, "WiiBasic.iso"));
                TestImageBuilder.CreateImageGcBasicSingleRar(Path.Combine(basePath.FullName, "GcBasic.rar"));
                TestImageBuilder.CreateImageWiiBasic2PartRar(Path.Combine(basePath.FullName, "WiiBasic.part1.rar"),
                                                             Path.Combine(basePath.FullName, "WiiBasic.part2.rar"));
                List<SourceFile> files = Nanook.NKit.SourceFiles.Scan(new string[] { basePath.FullName }, true, true, true, null, null).OrderBy(a => a.Name).ToList();

                Assert.Equal(4, files.Count);
                Assert.Contains(files, f => f.Name == "WiiBasic" && f.ImageType == SourceImageType.Iso && !f.IsSplitArchive && f.ArchiveFiles?.Length == null && f.ImageFiles.Length == 1);
                Assert.Contains(files, f => f.Name == "GcBasic" && f.ImageType == SourceImageType.Iso && !f.IsSplitArchive && f.ArchiveFiles?.Length == null && f.ImageFiles.Length == 1);
                Assert.Contains(files, f => f.Name == "WiiDemoImage" && f.ImageType == SourceImageType.Iso && f.IsSplitArchive && f.ArchiveFiles?.Length == 2 && f.ImageFiles.Length == 1);
                Assert.Contains(files, f => f.Name == "GcBasic" && f.ImageType == SourceImageType.Iso && !f.IsSplitArchive && f.ArchiveFiles?.Length == 1 && f.ImageFiles.Length == 1);

            }
            finally
            {
                basePath.Delete(true);
            }
        }

        [Theory]
        [InlineData(1, @"temp4/*.zip", true, @"<path>/temp4/(.*/)?.*\.zip$")]
        [InlineData(2, @"temp4/*.zip", false, @"<path>/temp4/[^/]*\.zip$")]
        [InlineData(3, @"/", true, @"^/(.*/)?.*$")]
        [InlineData(4, @"/", false, @"^/[^/]*$")]
        [InlineData(5, @"/*", true, @"^/(.*/)?.*$")]
        [InlineData(6, @"/*", false, @"^/[^/]*$")]
        [InlineData(7, @"./", true, @"<path>/(.*/)?.*$")]
        [InlineData(8, @"./", false, @"<path>/[^/]*$")]
        [InlineData(9, @"./*", true, @"<path>/(.*/)?.*$")]
        [InlineData(10, @"./*", false, @"<path>/[^/]*$")]
        [InlineData(11, @"./X*|Y*", false, @"<path>/(X[^/]*|Y[^/]*)$")]
        [InlineData(12, @"./X*|Y*//A*|B*", false, @"<path>/(X[^/]*|Y[^/]*)$")]
        [InlineData(13, @"*.tx?", true, @"<path>/(.*/)?.*\.tx.$")]
        [InlineData(14, @"*.tx?", false, @"<path>/[^/]*\.tx.$")]
        public void MaskToRegexTests(int idx, string mask, bool isRecursive, string result)
        {
            try
            {
                Directory.CreateDirectory("temp4");
                result = result.Replace("<path>", "^" + FileMask.MaskToRegex(FileMask.SanitisePath(Directory.GetCurrentDirectory())));

                FileMask m = FileMask.CreateLocalMask(mask, isRecursive);
                Assert.Equal(result, m.Regex);
            }
            finally
            {
                Directory.Delete("temp4");
            }
        }

        [Theory]
        [InlineData(1, true, "*.*", @"/path/test.zip")]
        [InlineData(2, true, "*.zip", @"\temp3\test.zip")]
        [InlineData(3, true, "*.zip", @"/temp3/temp/test.zip")]
        [InlineData(4, false, @"temp3\*.zip", @"/temp3/temp/test.zip")]
        [InlineData(5, true, @"*\temp3\temp\*.zip", @"/temp3/temp/test.zip")]
        [InlineData(6, true, @"\temp3\temp\*.zip", @"/temp3/temp/test.zip")]
        [InlineData(7, true, @"temp3\*.zip", @"\temp3\test.zip")]
        [InlineData(8, true, "*.zip|*.bin", @"\temp3\test.zip")]
        [InlineData(9, true, "*.zip|*.bin", @"/temp3/temp/test.bin")]
        [InlineData(10, false, @"/temp3/*.zip", @"/temp3/temp/test.zip")]
        [InlineData(11, true, @"/temp3/*", @"\temp3\test.zip")]
        [InlineData(12, true, @"/temp3/*", @"/temp3/test.zip")]
        [InlineData(13, true, @"/temp3/*", @"/temp3/xx/test.zip")]
        [InlineData(14, true, @"/temp3/", @"\temp3\test.zip")]
        [InlineData(15, true, @"/temp3/", @"/temp3/test.zip")]
        [InlineData(16, true, @"/temp3/*.zip|*.bin", @"/temp3/test.zip")]
        [InlineData(17, true, @"/temp3/*.zip|*.bin", @"/temp3/test.bin")]
        [InlineData(18, false, @"/temp3/*.zip|*.bin", @"/temp3/xx/test.zip")]
        [InlineData(19, false, @"/temp3/*.zip|*.bin", @"/temp3/xx/test.bin")]
        [InlineData(19, false, @"c*", @"/temp3/xx/ac.txt")]
        [InlineData(21, false, @"/temp3/", @"/temp3/xx/test.zip")]
        [InlineData(22, false, @"/temp3/", @"/temp3/xx/test.zip")]
        [InlineData(23, false, @"/temp3", @"\temp3\test.zip")]
        [InlineData(24, false, @"/temp3", @"/temp3/test.zip")]
        [InlineData(27, false, @"/temp3", @"/temp3/xx/test.zip")]
        [InlineData(29, true, @"/temp3///*.dat", @"\temp3\test.zip")]

        [InlineData(30, true, @"/path/file.txt", @"/path/file.txt")]
        [InlineData(31, false, @"/path/file.txt", @"/temp3/file.txt")]
        [InlineData(32, true, @"file.txt", @"/temp3/file.txt")]
        [InlineData(33, false, @"/file.txt", @"/temp3/file.txt")]
        [InlineData(34, false, @"/path/file.txt", @"/temp3/path/file.txt")]
        [InlineData(35, true, @"*/path/file.txt", @"/temp3/path/file.txt")]

        public void MaskMatchImageFsTests(int idx, bool result, string mask, string path)
        {
            FileMask m = FileMask.CreateImageFsMask(mask);
            Assert.Equal(result, m.IsMatch(path));
        }

        [Theory]
        [InlineData(1, true, "*.*", true, @"<path>\temp3\test.zip")]
        [InlineData(2, true, "*.zip", true, @"<path>\temp3\test.zip")]
        [InlineData(3, true, "*.zip", true, @"<path>/temp3/temp/test.zip")]
        [InlineData(4, false, "*.zip", false, @"<path>\temp3\test.zip")]
        [InlineData(5, false, "*.zip", false, @"<path>/temp3/temp/test.zip")]
        [InlineData(6, false, @"temp3\*.zip", false, @"<path>/temp3/temp/test.zip")]
        [InlineData(7, true, @"temp3\temp\*.zip", false, @"<path>/temp3/temp/test.zip")]
        [InlineData(8, true, @".\temp3\temp\*.zip", false, @"<path>/temp3/temp/test.zip")]
        [InlineData(9, true, @"temp3\*.zip", false, @"<path>\temp3\test.zip")]
        [InlineData(10, true, @"temp3/*.zip", true, @"<path>/temp3/temp/test.zip")]
        [InlineData(11, true, @"./temp3/*.zip", false, @"<path>\temp3\test.zip")]
        [InlineData(12, true, @"./temp3", false, @"<path>\temp3\test.zip")]
        [InlineData(13, true, @"./temp3", true, @"<path>\temp3\test.zip")]
        [InlineData(14, true, @"./temp3", true, @"<path>\temp3\temp\test.zip")]
        [InlineData(15, false, @"./temp3/*.zip", false, @"<path>\temp3\subfolder\test.zip")]
        [InlineData(16, true, @"./temp3/*.zip", true, @"<path>\temp3\subfolder\test.zip")]
        [InlineData(17, false, @"./temp3/*.zip", false, @"<path>\temp3\subfolder\test.zip")]
        [InlineData(18, false, @"e*.zip", true, @"<path>\test.zip")]
        [InlineData(19, false, @"e*.zip", false, @"<path>\test.zip")]
        [InlineData(20, false, @"es*.zip", true, @"<path>\test.zip")]
        [InlineData(21, false, @"es*.zip", false, @"<path>\test.zip")]
        [InlineData(22, false, @"x*.zip", true, @"<path>\test.zip")]
        [InlineData(23, false, @"x*.zip", false, @"<path>\test.zip")]
        public void MaskMatchLocalTests(int idx, bool result, string mask, bool isRecursive, string path)
        {
            try
            {
                Directory.CreateDirectory("temp3/temp");
                path = path.Replace("<path>", Directory.GetCurrentDirectory());

                FileMask m = FileMask.CreateLocalMask(mask, isRecursive);
                Assert.Equal(result, m.IsMatch(path));
            }
            finally
            {
                Directory.Delete("temp3/temp");
                Directory.Delete("temp3");
            }
        }

        [Theory]
        [InlineData(1, @"./temp2/*.dat", "./temp2", "*.dat", null)]
        [InlineData(2, @".", ".", "*", null)]
        [InlineData(3, @"*.dat", ".", "*.dat", null)]
        [InlineData(4, @"temp2/*.dat", @"./temp2", "*.dat", null)]
        [InlineData(5, @"temp2/*.zip//*.dat", @"./temp2", "*.zip", "*.dat")]
        [InlineData(6, @"temp2/Arc.zip//*.dat", @"./temp2", "Arc.zip", "*.dat")]
        [InlineData(7, @"temp2/x/Arc.zip//*.dat", @"./temp2/x", "Arc.zip", "*.dat")]
        [InlineData(8, @"temp2\Arc.zip//*.dat", @"./temp2", "Arc.zip", "*.dat")]
        [InlineData(9, @"temp2\x\Arc.zip//*.dat", @"./temp2/x", "Arc.zip", "*.dat")]
        [InlineData(10, @"temp2", @"./temp2", "*", null)]
        [InlineData(11, @"temp2/", @"./temp2", "*", null)]
        [InlineData(12, @"/", @"/", "*", null)]
        [InlineData(13, @"hello\test.zip//mask*", @"./hello", "test.zip", "mask*")]
        [InlineData(14, @"hello\test.zip//mask*|X*", @"./hello", "test.zip", "mask*|X*")]
        public void MaskMatchSplitLocalTests(int idx, string mask, string resultPath, string resultMask, string resultArcMask)
        {
            try
            {
                Directory.CreateDirectory("temp2");
                FileMask m = FileMask.CreateLocalMask(mask, false);
                Assert.Equal(resultPath, m.Path.Replace(Directory.GetCurrentDirectory().Replace("\\", "/"), "."));
                Assert.Equal(resultMask, m.Mask);
                Assert.Equal(resultArcMask, m.MaskInArc);
            }
            finally
            {
                Directory.Delete("temp2");
            }
        }

        [Theory]
        [InlineData(1, @"/temp1/*.dat", "/temp1", "*.dat", true)]
        [InlineData(1, @"/*.dat", "/", "*.dat", true)]
        [InlineData(1, @"/temp1/2/*.dat", "/temp1/2", "*.dat", true)]
        [InlineData(1, @"/temp1/2/*.zip//*.dat", "/temp1/2", "*.zip", true)]
        public void MaskMatchSplitImageFsTests(int idx, string mask, string resultPath, string resultMask, bool isValid)
        {
            FileMask m = null;

            if (!isValid)
                Assert.Throws<HandledException>(() => m = FileMask.CreateImageFsMask(mask));
            else
                m = FileMask.CreateImageFsMask(mask);

            Assert.Equal(resultPath, m.Path.Replace(Directory.GetCurrentDirectory().Replace("\\", "/"), "."));
            Assert.Equal(resultMask, m.Mask);
        }


        [Theory]
        [InlineData(0x400L, 0x4, 0x300, 0x0L, 0x100L, 0x100L, 0x200L, 0x300L, 0x400L)]
        [InlineData(0x400L, 0x2, 0x600, 0x0L, 0x100L, 0x100L, 0x0L, 0x300L, 0x380L)]
        public void SplitStreamSeekTests(long blockSizes, int blocks, int testSize, params long[] seekOffsets)
        {
            //create some buffers and write the 4 byte location in each 4 byte offset so when we read them back we know we have the correct bytes
            byte[][] buffers = new byte[blocks][]; //10 parts
            long off = 0;
            for (int i = 0; i < blocks; i++)
            {
                buffers[i] = new byte[blockSizes];
                TestUtils.FillBlock(off, buffers[i], (int)blockSizes, 0, (int)blockSizes, 1);
                off += blockSizes;
            }

            //test that when we read the stream it returns all the correct bytes spanning the parts etc
            byte[] test = new byte[testSize];
            using (SourceStream stream = SourceStream.Open(idx => new MemoryStream(buffers[idx]), buffers.Select(a => a.LongLength).ToArray(), true))
            {
                foreach (long offset in seekOffsets)
                {
                    stream.Seek(offset, SeekOrigin.Begin);
                    long sz = Math.Min(test.Length, stream.Length - offset);
                    stream.Read(test, 0, (int)sz);
                    long p = 0;
                    while (p < sz)
                    {
                        Assert.Equal(offset + p, (long)test.ReadUInt32B((int)p));
                        p += 4;
                    }
                }
            }
        }


        [Theory]
        [InlineData(0x124, 0x400L, 0x400L, 0x400L, 0x400L, 0x400L)]
        [InlineData(0x600, 0x400L, 0x400L, 0x400L, 0x400L, 0x400L)]
        [InlineData(0x600, 0x400L, 0x100L, 0x900L, 0x4L, 0x400L)]
        [InlineData(0x1000000, 0x400L, 0x100L)]
        [InlineData(0x4, 0x400L, 0x100L)]
        private void splitStreamReaderTests(int testSize, params long[] sizes)
        {
            //create some buffers and write the 4 byte location in each 4 byte offset so when we read them back we know we have the correct bytes
            byte[][] buffers = new byte[sizes.Length][]; //10 parts
            long offset = 0;
            for (int i = 0; i < sizes.Length; i++)
            {
                buffers[i] = new byte[sizes[i]];
                TestUtils.FillBlock(offset, buffers[i], (int)sizes[i], 0, (int)sizes[i], 1);
                offset += sizes[i];
            }

            //test that when we read the stream it returns all the correct bytes spanning the parts etc
            byte[] test = new byte[testSize];
            offset = 0;
            using (SourceStream stream = SourceStream.Open(idx => new MemoryStream(buffers[idx]), sizes, true))
            {
                while (offset < stream.Length)
                {
                    long sz = Math.Min(test.Length, stream.Length - offset);
                    stream.Read(test, 0, (int)sz);
                    long p = 0;
                    while (p < sz)
                    {
                        Assert.Equal(offset + p, (long)test.ReadUInt32B((int)p));
                        p += 4;
                    }
                    offset += sz;
                }
            }
        }


        [Theory]
        [InlineData(1, @"<path>\aaa\aaa.wbfs", true, false, @"<path>\aaa\", @"aaa", new[] { "aaa.wbfs", "aaa.wbf1" })]
        [InlineData(2, @"<path>\aaa\ddd.wbfs", false, false, @"<path>\aaa\", @"ddd", new[] { "ddd.wbfs" })]
        [InlineData(3, @"<path>\bbb\bbb.wbfs", false, false, @"<path>\bbb\", @"bbb", new[] { "bbb.wbfs" })]
        [InlineData(4, @"<path>\bbb\ccc.wbfs", false, false, @"<path>\bbb\", @"ccc", new[] { "ccc.wbfs" })]
        [InlineData(5, @"<path>\ccc\ccc.wbfs", false, false, @"<path>\ccc\", @"ccc", new[] { "ccc.wbfs" })]
        [InlineData(6, @"<path>\ddd\aaa.wbfs", false, false, @"<path>\ddd\", @"aaa", new[] { "aaa.wbfs" })]
        [InlineData(7, @"<path>\rar\FileName2.iso", false, false, @"<path>\rar\", @"FileName2", new[] { "FileName2.iso" })]
        [InlineData(8, @"<path>\rar\FileName.rar", true, true, @"<path>\rar\", @"FileName", new[] { "FileName.rar", "FileName.r00", "FileName.r01", "FileName.r02" })]
        [InlineData(9, @"<path>\rar\FileName.zip\FileName.zip", false, true, @"<path>\rar\FileName.zip\", @"FileName", new[] { "FileName.zip" })]
        [InlineData(10, @"<path>\rar\FileName.zip\FileName.rar", false, true, @"<path>\rar\FileName.zip\", @"FileName", new[] { "FileName.rar" })]
        [InlineData(11, @"<path>\rar\FileName.part01.rar", true, true, @"<path>\rar\", @"FileName", new[] { "FileName.part01.rar", "FileName.part02.rar", "FileName.part03.rar", "FileName.part04.rar" })]
        [InlineData(12, @"<path>\rar\FileName2.part01.rar", true, true, @"<path>\rar\", @"FileName2", new[] { "FileName2.part01.rar", "FileName2.part02.rar", "FileName2.part03.rar", "FileName2.part04.rar" })]
        [InlineData(13, @"<path>\rar\FileName2.rar", true, true, @"<path>\rar\", @"FileName2", new[] { "FileName2.rar", "FileName2.r00", "FileName2.r01", "FileName2.r02" })]
        [InlineData(14, @"<path>\rar\FileName2.z01", true, true, @"<path>\rar\", @"FileName2", new[] { "FileName2.z01", "FileName2.z02", "FileName2.z03", "FileName2.z04", "FileName2.z05" })]
        [InlineData(15, @"<path>\rar\FileName3.rar", false, true, @"<path>\rar\", @"FileName3", new[] { "FileName3.rar" })]
        [InlineData(16, @"<path>\rar\filename3.rar", false, true, @"<path>\rar\", @"filename3", new[] { "filename3.rar" })]
        public void ScannedFilesGroupTests(int idx, string fn, bool isSplit, bool isArchive, string path, string name, string[] partNames)
        {
            string fld = nameof(ScannedFilesGroupTests);
            //archive files reside in parts at this point. This test is performed before the archives are analysed

            Directory.CreateDirectory(fld);
            try
            {
                foreach (string s in _TestPaths)
                    Directory.CreateDirectory(s.Replace("<path>", fld).Replace("\\", Path.DirectorySeparatorChar.ToString()));

                FileInfo[] files = _TestFiles.Select(a => new FileInfo(a.Replace("<path>", fld).Replace("\\", Path.DirectorySeparatorChar.ToString()))).ToArray();
                FileMask mask = FileMask.CreateLocalMask(fld, true);

                List<SourceFile> sourceFiles = Nanook.NKit.SourceFiles.GroupFiles(files, mask, true, null).OrderBy(a => a.Name).ToList();

                fn = Path.GetFullPath(fn.Replace("<path>", fld).Replace("\\", Path.DirectorySeparatorChar.ToString()));
                path = path.Replace("<path>", Path.GetFullPath(fld)).Replace("\\", Path.DirectorySeparatorChar.ToString()); //preserve path trailing separator

                Assert.Equal(16, sourceFiles.Count); //cound of all collated files

                SourceFile fi = sourceFiles.FirstOrDefault(a => a.ImageFiles[0].Path + a.ImageFiles[0].FileName == fn);

                Assert.Equal(isSplit, partNames.Length > 1);
                Assert.Equal(isArchive, fi.IsArchive);
                Assert.Equal(path, fi.BasePath);
                Assert.Equal(name, fi.Name);

                if (isSplit)
                {
                    Assert.Equal(partNames.Length, fi.ImageFiles.Length);
                    for (int i = 0; i < partNames.Length; i++)
                        Assert.Equal(partNames[i], Path.GetFileName(fi.ImageFiles[i].FileName));
                }
            }
            finally
            {
                Directory.Delete(fld, true);
            }
        }

        [Theory]
        [InlineData(".nkit.iso", "nanook.nkit.iso")]
        [InlineData(".nkit.gcz", "nanook.nkit.gcz")]
        [InlineData(".iso.dec", "nanook.iso.dec")]
        [InlineData(".iso", "nanook.iso")]
        [InlineData(".bin", "nanook.bin")]
        [InlineData(".cue", "nanook.cue")]
        [InlineData(".gdi", "nanook.gdi")]
        [InlineData(".ciso", "nanook.ciso")]
        [InlineData(".wbfs", "nanook.wbfs")]
        [InlineData(".wbf1", "nanook.wbf1")]
        [InlineData(".gcz", "nanook.gcz")]
        [InlineData(".gcm", "nanook.gcm")]
        [InlineData(".wia", "nanook.wia")]
        [InlineData(".rvz", "nanook.rvz")]
        [InlineData(".wud", "nanook.wud")]
        [InlineData(".wux", "nanook.wux")]
        [InlineData(".zip", "nanook.zip")]
        [InlineData(".rar", "nanook.rar")]
        [InlineData(".7z", "nanook.7z")]
        [InlineData(".gz", "nanook.gz")]
        [InlineData(".dax", "nanook.dax")]
        [InlineData(".jso", "nanook.jso")]
        [InlineData(".cso", "nanook.cso")]
        [InlineData(".zso", "nanook.zso")]
        [InlineData("", "nanook.xxx")] //not recognised
        [InlineData(".nkit.iso", "n.anook.nkit.iso")] //extra . in the filename
        [InlineData(".nkit.gcz", "n.anook.nkit.gcz")]
        [InlineData(".iso.dec", "n.anook.iso.dec")]
        [InlineData(".iso", "n.anook.iso")]
        [InlineData(".zip", "n.anook.zip")]
        [InlineData(".rar", "n.anook.rar")]
        [InlineData("", "n.anook.xxx")]
        [InlineData(".nkit.iso", "nanook.iso.nkit.iso")] //attempt to trick it
        [InlineData(".nkit.gcz", "nanook.iso.nkit.gcz")]
        [InlineData(".iso.dec", "nanook.iso.iso.dec")]
        [InlineData(".iso", "nanook.iso.iso")]
        [InlineData(".zip", "nanook.iso.zip")]
        [InlineData(".rar", "nanook.iso.rar")]
        [InlineData("", "nanook.iso.xxx")]
        [InlineData(".nkit.iso", "n.anook.iso.nkit.iso")] //attempt to trick it
        [InlineData(".nkit.gcz", "n.anook.iso.nkit.gcz")]
        [InlineData(".iso.dec", "n.anook.iso.iso.dec")]
        [InlineData(".iso", "n.anook.iso.iso")]
        [InlineData(".zip", "n.anook.iso.zip")]
        [InlineData(".rar", "n.anook.iso.rar")]
        [InlineData("", "n.anook.iso.xxx")]
        //split tests
        [InlineData(".zip.001", "nanook.zip.001")] //this is prefered for split archives (zip would be removed on another call once consolidated
        [InlineData(".part01.rar", "nanook.part01.rar")] //this is prefered for split archives (zip would be removed on another call once consolidated
        [InlineData(".part999.rar", "nanook.part999.rar")] //this is prefered for split archives (zip would be removed on another call once consolidated
        [InlineData(".z01", "nanook.zip.z01")] //this is prefered for split archives (zip would be removed on another call once consolidated
        [InlineData(".z99", "nanook.zip.z99")] //this is prefered for split archives (zip would be removed on another call once consolidated
        [InlineData(".r01", "nanook.zip.r01")] //this is prefered for split archives (zip would be removed on another call once consolidated
        [InlineData(".r99", "nanook.zip.r99")] //this is prefered for split archives (zip would be removed on another call once consolidated
        public void FileNameMatchTests(string ext, string filename) => Assert.Equal(ext, Nanook.NKit.SourceFiles.GetKnownFileExtension(filename));
    }
}