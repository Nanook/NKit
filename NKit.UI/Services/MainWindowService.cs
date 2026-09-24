using Nanook.NKit;
using NKit.Ui.Helpers;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace NKit.Ui.Services
{
    public class MainWindowService
    {
        public ObservableCollection<SystemType> GetAllSystemTypes() =>
            new ObservableCollection<SystemType>()
            {
                // Nintendo
                SystemType.GameCube,
                SystemType.Wii,
                SystemType.WiiU,
                // Sony
                SystemType.PS1,
                SystemType.PS2,
                SystemType.PS3,
                SystemType.PSP,
                // Microsoft
                SystemType.XBox,
                SystemType.XBox360,
                // Sega
                SystemType.Dreamcast,
                SystemType.Saturn,
                SystemType.SegaCD,
                // NEC
                SystemType.PcEngine,
                // Phillips
                SystemType.CDi,
                // Other
                SystemType.Default, // TODO - ISO 9660 - need image
                // Not currently supported
                //SystemType.Jaguar,
                //SystemType.PS4,
            };

        public (SystemType, ObservableCollection<SystemType>) GetFilteredSystemTypes(string systemFilter, ObservableCollection<SystemType> systems, SystemType currentSystem)
        {
            SystemType newSystem = SystemType.NotSet;
            ObservableCollection<SystemType> newSystemTypes = null;

            if (string.IsNullOrWhiteSpace(systemFilter))
            {
                newSystemTypes = GetAllSystemTypes();
                newSystem = currentSystem;
            }
            else
            {
                ObservableCollection<SystemType> filteredSystems = SystemFilterHelper.GetSystemsForSearchTerm(GetAllSystemTypes(), systemFilter);

                if (filteredSystems.Count == 0)
                {
                    newSystemTypes = systems;
                    newSystem = currentSystem;
                }
                else
                {
                    newSystemTypes = filteredSystems;
                    newSystem = filteredSystems.Contains(currentSystem) ? currentSystem : filteredSystems.First();
                }
            }

            return (newSystem, newSystemTypes);
        }

        public ObservableCollection<TaskType> GetTaskTypes(SystemType systemType) => systemType switch
        {
            // CONVERT, EXTRACT, FIX, SCAN, VERIFY, DEDUPE

            //NEC
            SystemType.PcEngine => new ObservableCollection<TaskType> { TaskType.Convert, TaskType.Extract, TaskType.Scan, TaskType.Verify, },
            //Nintendo
            SystemType.GameCube => new ObservableCollection<TaskType> { TaskType.Convert, TaskType.Extract, TaskType.Fix, TaskType.Scan, TaskType.Verify, TaskType.Dedupe, }, //TaskType.Expand, TaskType.FixExtract 
            SystemType.Wii => new ObservableCollection<TaskType> { TaskType.Convert, TaskType.Extract, TaskType.Fix, TaskType.Scan, TaskType.Verify, TaskType.Dedupe, }, //TaskType.Expand, TaskType.FixExtract 
            SystemType.WiiU => new ObservableCollection<TaskType> { TaskType.Convert, TaskType.Extract, TaskType.Scan, TaskType.Verify, TaskType.Dedupe, }, //TaskType.Expand, 
            //Phillips
            SystemType.CDi => new ObservableCollection<TaskType> { TaskType.Convert, TaskType.Extract, TaskType.Scan, TaskType.Verify, },
            //Sega
            SystemType.Dreamcast => new ObservableCollection<TaskType> { TaskType.Convert, TaskType.Extract, TaskType.Scan, TaskType.Verify, },
            SystemType.Saturn => new ObservableCollection<TaskType> { TaskType.Convert, TaskType.Extract, TaskType.Scan, TaskType.Verify, },
            SystemType.SegaCD => new ObservableCollection<TaskType> { TaskType.Convert, TaskType.Extract, TaskType.Scan, TaskType.Verify, },
            //Sony
            SystemType.PS1 => new ObservableCollection<TaskType> { TaskType.Convert, TaskType.Extract, TaskType.Scan, TaskType.Verify, },
            SystemType.PS2 => new ObservableCollection<TaskType> { TaskType.Convert, TaskType.Extract, TaskType.Scan, TaskType.Verify, },
            SystemType.PS3 => new ObservableCollection<TaskType> { TaskType.Convert, TaskType.Extract, TaskType.Fix, TaskType.Scan, TaskType.Verify, }, //TaskType.Dedupe not yet supported
            SystemType.PSP => new ObservableCollection<TaskType> { TaskType.Convert, TaskType.Extract, TaskType.Scan, TaskType.Verify, }, //TaskType.Expand,
            //Microsoft
            SystemType.XBox => new ObservableCollection<TaskType> { TaskType.Convert, TaskType.Extract, TaskType.Scan, TaskType.Verify, },
            SystemType.XBox360 => new ObservableCollection<TaskType> { TaskType.Convert, TaskType.Extract, TaskType.Scan, TaskType.Verify, },
            //Other
            SystemType.Default => new ObservableCollection<TaskType> { TaskType.Convert, TaskType.Extract, TaskType.Scan, },
            // Not Currently Supported
            //SystemType.NotSet => new ObservableCollection<TaskType> { TaskType.Scan },
            //SystemType.Jaguar => new ObservableCollection<TaskType> { TaskType.Scan },
            //SystemType.PS4 => new ObservableCollection<TaskType> { TaskType.Scan },
            _ => throw new ArgumentException("Unknown SystemType"),
        };

        public bool DoesSystemHaveTask(SystemType systemType, TaskType taskType)
        {
            ObservableCollection<TaskType> tasksForSystem = GetTaskTypes(systemType);
            return tasksForSystem.Contains(taskType);
        }

        public TaskType DetermineTaskWithPreference(SystemType systemType, TaskType preferredTask)
        {
            ObservableCollection<TaskType> availableTasks = GetTaskTypes(systemType);

            if (availableTasks.Contains(preferredTask))
                return preferredTask;

            if (availableTasks.Contains(TaskType.Convert))
                return TaskType.Convert;

            return availableTasks.First();
        }

        public TaskType GetDefaultTaskForSystem(SystemType systemType)
        {
            ObservableCollection<TaskType> tasks = GetTaskTypes(systemType);
            return tasks.Contains(TaskType.Convert) ? TaskType.Convert : tasks.First();
        }
    }
}