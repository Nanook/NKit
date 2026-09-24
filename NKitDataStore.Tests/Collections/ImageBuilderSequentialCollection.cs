// Ensure tests that rely on shared ImageBuilder buffer/cache state run sequentially.
[CollectionDefinition("ImageBuilder Sequential Tests", DisableParallelization = true)]
public class ImageBuilderSequentialCollection
{
    // Intentionally empty - this type only defines the collection behavior for xUnit.
}