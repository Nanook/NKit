using NkdsUi.Models;
using NkdsUi.ViewModels;

namespace NKitDataStore.Tests;

/// <summary>
/// Unit tests for AssociationEntryViewModel derived properties.
/// Validates: Requirements 9.2, 9.3, 9.4
/// </summary>
public class AssociationEntryViewModelTests
{
    private static AssociationEntryViewModel CreateViewModel() =>
        new(new AssociationEntry
        {
            Id = "test-entry",
            Label = "Test Entry",
            Category = AssociationCategory.DirectoryContextMenu,
            CommandArgTemplate = "--test \"{0}\""
        });

    // --- CanToggle tests (Requirement 9.4) ---

    [Fact]
    public void CanToggle_ReturnsFalse_WhenIsLoadingIsTrue()
    {
        AssociationEntryViewModel vm = CreateViewModel();
        vm.IsLoading = true;

        Assert.False(vm.CanToggle);
    }

    [Fact]
    public void CanToggle_ReturnsFalse_WhenIsOperationInProgressIsTrue()
    {
        AssociationEntryViewModel vm = CreateViewModel();
        vm.IsOperationInProgress = true;

        Assert.False(vm.CanToggle);
    }

    [Fact]
    public void CanToggle_ReturnsFalse_WhenErrorMessageIsSet()
    {
        AssociationEntryViewModel vm = CreateViewModel();
        vm.ErrorMessage = "Something went wrong";

        Assert.False(vm.CanToggle);
    }

    [Fact]
    public void CanToggle_ReturnsTrue_WhenNoLoadingNoOperationNoError()
    {
        AssociationEntryViewModel vm = CreateViewModel();

        Assert.True(vm.CanToggle);
    }

    [Fact]
    public void CanToggle_ReturnsFalse_WhenMultipleBlockingConditionsAreSet()
    {
        AssociationEntryViewModel vm = CreateViewModel();
        vm.IsLoading = true;
        vm.IsOperationInProgress = true;
        vm.ErrorMessage = "Error";

        Assert.False(vm.CanToggle);
    }

    [Fact]
    public void CanToggle_ReturnsTrue_AfterClearingAllBlockingConditions()
    {
        AssociationEntryViewModel vm = CreateViewModel();
        vm.IsLoading = true;
        vm.ErrorMessage = "Error";

        vm.IsLoading = false;
        vm.ErrorMessage = null;

        Assert.True(vm.CanToggle);
    }

    // --- IsRegistered tests (Requirement 9.2) ---

    [Fact]
    public void IsRegistered_ReturnsTrue_WhenStateIsRegistered()
    {
        AssociationEntryViewModel vm = CreateViewModel();
        vm.State = AssociationState.Registered;

        Assert.True(vm.IsRegistered);
    }

    [Fact]
    public void IsRegistered_ReturnsFalse_WhenStateIsNotRegistered()
    {
        AssociationEntryViewModel vm = CreateViewModel();
        vm.State = AssociationState.NotRegistered;

        Assert.False(vm.IsRegistered);
    }

    [Fact]
    public void IsRegistered_ReturnsFalse_WhenStateIsStale()
    {
        AssociationEntryViewModel vm = CreateViewModel();
        vm.State = AssociationState.Stale;

        Assert.False(vm.IsRegistered);
    }

    [Fact]
    public void IsRegistered_ReturnsFalse_WhenStateIsUnknown()
    {
        AssociationEntryViewModel vm = CreateViewModel();
        vm.State = AssociationState.Unknown;

        Assert.False(vm.IsRegistered);
    }

    // --- IsStale tests (Requirement 9.2) ---

    [Fact]
    public void IsStale_ReturnsTrue_WhenStateIsStale()
    {
        AssociationEntryViewModel vm = CreateViewModel();
        vm.State = AssociationState.Stale;

        Assert.True(vm.IsStale);
    }

    [Fact]
    public void IsStale_ReturnsFalse_WhenStateIsRegistered()
    {
        AssociationEntryViewModel vm = CreateViewModel();
        vm.State = AssociationState.Registered;

        Assert.False(vm.IsStale);
    }

    [Fact]
    public void IsStale_ReturnsFalse_WhenStateIsNotRegistered()
    {
        AssociationEntryViewModel vm = CreateViewModel();
        vm.State = AssociationState.NotRegistered;

        Assert.False(vm.IsStale);
    }

    [Fact]
    public void IsStale_ReturnsFalse_WhenStateIsUnknown()
    {
        AssociationEntryViewModel vm = CreateViewModel();
        vm.State = AssociationState.Unknown;

        Assert.False(vm.IsStale);
    }
}