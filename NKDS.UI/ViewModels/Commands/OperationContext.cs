using NKDS.Mount;
using NkdsUi.Services;
using ReactiveUI;
using ReactiveUI.Primitives;
using System.Collections.ObjectModel;

namespace NkdsUi.ViewModels.Commands;

/// <summary>
/// Provides command handlers with access to shared services and UI state
/// needed to execute toolbar operations.
/// </summary>
public record OperationContext(
    IDataStoreService DataStoreService,
    SessionManager SessionManager,
    ExclusiveAccessManager ExclusiveAccessManager,
    IConfigService ConfigService,
    ImageListViewModel ImageList,
    ToolbarViewModel Toolbar,
    IImageListSyncService SyncService,
    ErrorNotificationService ErrorNotification,
    SharedObservableState SharedState,
    MountOrchestrator MountOrchestrator,
    ObservableCollection<OperationProgressViewModel> ActiveOperations,
    // Dialog interactions — passed from MainWindowViewModel
    Interaction<AddImagesDialogViewModel, bool> ShowAddImagesDialog,
    Interaction<ExportDialogViewModel, bool> ShowExportDialog,
    Interaction<CompactDialogViewModel, bool> ShowCompactDialog,
    Interaction<RollbackDialogViewModel, bool> ShowRollbackDialog,
    Interaction<MountDialogViewModel, bool> ShowMountDialog,
    Interaction<Add1GmrDialogViewModel, bool> ShowAdd1GmrDialog,
    Interaction<CreateSetDialogViewModel, bool> ShowCreateSetDialog,
    Interaction<GroupingWindowViewModel, RxVoid> ShowGroupingWindow,
    Interaction<RxVoid, string?> ShowFolderPicker,
    Interaction<RxVoid, string?> ShowFilePicker,
    Interaction<RxVoid, string?> ShowSaveFilePicker
);