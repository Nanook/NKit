using System.Collections.Generic;

namespace Nanook.NKit
{
    /// <summary>
    /// Public, read-only query over which tasks are supported for which systems, derived from the
    /// internal step-definition table (which stays private). Used by the CLI/interactive builder to
    /// offer only valid task/system combinations, and available to UIs for the same purpose.
    /// <para>
    /// This reflects the COARSE, always-knowable axis: whether a (task, system) pair has ANY real
    /// processing path. Finer routing (source format, folderindex vs image, dats present) is
    /// resolved at run time from the actual file and is intentionally not modelled here.
    /// </para>
    /// </summary>
    public static class TaskCapabilities
    {
        // The systems a user can meaningfully target (excludes NotSet; Default is the ISO fallback).
        private static readonly SystemType[] _selectableSystems =
        {
            SystemType.GameCube, SystemType.Wii, SystemType.WiiU,
            SystemType.PS1, SystemType.PS2, SystemType.PS3, SystemType.PSP,
            SystemType.Dreamcast, SystemType.Saturn, SystemType.SegaCD, SystemType.CDi,
            SystemType.PcEngine, SystemType.XBox, SystemType.XBox360, SystemType.Default,
        };

        private static readonly TaskType[] _selectableTasks =
        {
            TaskType.Convert, TaskType.Expand, TaskType.Extract, TaskType.Fix,
            TaskType.FixExtract, TaskType.Scan, TaskType.Verify, TaskType.Dedupe,
        };

        /// <summary>True when the (task, system) pair has a real processing path.</summary>
        public static bool IsSupported(TaskType task, SystemType system)
            => NKitTaskContext.IsTaskSupportedInternal(task, system);

        /// <summary>Systems that support the given task.</summary>
        public static IReadOnlyList<SystemType> SupportedSystems(TaskType task)
        {
            List<SystemType> list = new List<SystemType>();
            foreach (SystemType s in _selectableSystems)
                if (IsSupported(task, s))
                    list.Add(s);
            return list;
        }

        /// <summary>Tasks that are supported for the given system.</summary>
        public static IReadOnlyList<TaskType> SupportedTasks(SystemType system)
        {
            List<TaskType> list = new List<TaskType>();
            foreach (TaskType t in _selectableTasks)
                if (IsSupported(t, system))
                    list.Add(t);
            return list;
        }

        /// <summary>All user-selectable systems (for building a full list before filtering).</summary>
        public static IReadOnlyList<SystemType> SelectableSystems => _selectableSystems;

        /// <summary>All user-selectable tasks.</summary>
        public static IReadOnlyList<TaskType> SelectableTasks => _selectableTasks;
    }
}