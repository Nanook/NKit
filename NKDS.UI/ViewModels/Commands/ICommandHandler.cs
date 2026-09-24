using ReactiveUI.Primitives;

namespace NkdsUi.ViewModels.Commands;

/// <summary>
/// Defines the contract for a toolbar operation handler.
/// Each handler encapsulates the full lifecycle of a single operation.
/// </summary>
public interface ICommandHandler
{
    /// <summary>
    /// Executes the operation using the provided context.
    /// Returns an observable that completes when the operation finishes.
    /// </summary>
    IObservable<RxVoid> Execute(OperationContext context);
}