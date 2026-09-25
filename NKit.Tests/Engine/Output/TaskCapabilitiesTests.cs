using Nanook.NKit;
using System.Collections.Generic;
using Xunit;


namespace NKit.Tests.Engine.Output
{
    /// <summary>Tests the capability derivation from the step-definition table.</summary>
    [Trait("Area", "Engine")]
    [Trait("Group", "Output")]
    public class TaskCapabilitiesTests
    {
        [Fact]
        public void Convert_SupportedForWiiAndGameCube()
        {
            Assert.True(TaskCapabilities.IsSupported(TaskType.Convert, SystemType.Wii));
            Assert.True(TaskCapabilities.IsSupported(TaskType.Convert, SystemType.GameCube));
        }

        [Fact]
        public void Fix_SupportedForWiiGcPs3_NotForPsp()
        {
            Assert.True(TaskCapabilities.IsSupported(TaskType.Fix, SystemType.Wii));
            Assert.True(TaskCapabilities.IsSupported(TaskType.Fix, SystemType.GameCube));
            Assert.True(TaskCapabilities.IsSupported(TaskType.Fix, SystemType.PS3));
            // Fix has only wii/gamecube/ps3 real rows; everything else hits NotSet-NotSupported.
            Assert.False(TaskCapabilities.IsSupported(TaskType.Fix, SystemType.PSP));
            Assert.False(TaskCapabilities.IsSupported(TaskType.Fix, SystemType.PS2));
        }

        [Fact]
        public void FixExtract_OnlyWiiGcAndPs3()
        {
            IReadOnlyList<SystemType> sys = TaskCapabilities.SupportedSystems(TaskType.FixExtract);
            Assert.Contains(SystemType.Wii, sys);
            Assert.Contains(SystemType.GameCube, sys);
            Assert.Contains(SystemType.PS3, sys);
            Assert.DoesNotContain(SystemType.PSP, sys);
            Assert.DoesNotContain(SystemType.Saturn, sys);
        }

        [Fact]
        public void Extract_SupportedBroadly()
        {
            // Extract has wii/gamecube, wiiu, xbox, and _IsoFsSystems rows — broad support.
            Assert.True(TaskCapabilities.IsSupported(TaskType.Extract, SystemType.Wii));
            Assert.True(TaskCapabilities.IsSupported(TaskType.Extract, SystemType.PS2));
            Assert.True(TaskCapabilities.IsSupported(TaskType.Extract, SystemType.XBox));
        }

        [Fact]
        public void SupportedTasksForWii_IncludesConvertFixExtract()
        {
            IReadOnlyList<TaskType> tasks = TaskCapabilities.SupportedTasks(SystemType.Wii);
            Assert.Contains(TaskType.Convert, tasks);
            Assert.Contains(TaskType.Fix, tasks);
            Assert.Contains(TaskType.Extract, tasks);
            Assert.Contains(TaskType.Dedupe, tasks);
        }
    }
}