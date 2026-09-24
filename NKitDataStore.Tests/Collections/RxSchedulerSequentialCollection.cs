// Ensure tests that modify global RxSchedulers state run sequentially.
[CollectionDefinition("RxScheduler Sequential Tests", DisableParallelization = true)]
public class RxSchedulerSequentialCollection
{
    // Intentionally empty - this type only defines the collection behavior for xUnit.
}