using global::NKit.Ui.Services;
using Nanook.NKit;
using System.Collections.ObjectModel;
using Xunit;


namespace NKit.Tests.Configuration.UI
{
    [Trait("Area", "Configuration")]
    [Trait("Group", "UI")]
    public class MainWindowServiceTests
    {
        private readonly MainWindowService _service = new MainWindowService();

        [Fact]
        public void GetTaskTypes_GameCube_ReturnsExpectedTasks()
        {
            ObservableCollection<TaskType> result = _service.GetTaskTypes(SystemType.GameCube);

            Assert.Contains(TaskType.Convert, result);
            Assert.Contains(TaskType.Extract, result);
            Assert.Contains(TaskType.Fix, result);
            Assert.Contains(TaskType.Scan, result);
            Assert.Contains(TaskType.Verify, result);
            Assert.Contains(TaskType.Dedupe, result);
        }

        [Fact]
        public void DoesSystemHaveTask_GameCubeWithConvert_ReturnsTrue()
        {
            bool result = _service.DoesSystemHaveTask(SystemType.GameCube, TaskType.Convert);

            Assert.True(result);
        }

        [Fact]
        public void DoesSystemHaveTask_PS1WithFix_ReturnsFalse()
        {
            bool result = _service.DoesSystemHaveTask(SystemType.PS1, TaskType.Fix);

            Assert.False(result);
        }

        [Fact]
        public void DetermineTaskWithPreference_PreferredTaskSupported_ReturnsPreferredTask()
        {
            TaskType result = _service.DetermineTaskWithPreference(SystemType.GameCube, TaskType.Fix);

            Assert.Equal(TaskType.Fix, result);
        }

        [Fact]
        public void DetermineTaskWithPreference_PreferredTaskNotSupported_ReturnsConvert()
        {
            TaskType result = _service.DetermineTaskWithPreference(SystemType.PS1, TaskType.Fix);

            Assert.Equal(TaskType.Convert, result);
        }

        [Fact]
        public void GetDefaultTaskForSystem_SystemWithConvert_ReturnsConvert()
        {
            TaskType result = _service.GetDefaultTaskForSystem(SystemType.GameCube);

            Assert.Equal(TaskType.Convert, result);
        }

        [Fact]
        public void GetAllSystemTypes_ReturnsExpectedSystems()
        {
            ObservableCollection<SystemType> result = _service.GetAllSystemTypes();

            Assert.Contains(SystemType.GameCube, result);
            Assert.Contains(SystemType.Wii, result);
            Assert.Contains(SystemType.PS3, result);
            Assert.True(result.Count > 10);
        }
    }
}