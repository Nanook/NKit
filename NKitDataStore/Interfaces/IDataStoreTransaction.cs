namespace NKitDataStore.Interfaces
{
    /// <summary>
    /// Represents a database-agnostic transaction for data store operations.
    /// This abstraction allows for different storage backends (SQLite, SQL Server, MongoDB, etc.)
    /// without tying the API to ADO.NET DbTransaction.
    /// </summary>
    public interface IDataStoreTransaction : IDisposable
    {
        /// <summary>
        /// Commits all changes made during this transaction.
        /// </summary>
        /// <exception cref="InvalidOperationException">If transaction has already been committed or rolled back.</exception>
        void Commit();

        /// <summary>
        /// Rolls back all changes made during this transaction.
        /// </summary>
        /// <exception cref="InvalidOperationException">If transaction has already been committed or rolled back.</exception>
        void Rollback();

        /// <summary>
        /// Gets whether the transaction has been completed (committed or rolled back).
        /// </summary>
        bool IsCompleted { get; }

        /// <summary>
        /// Gets the set name this transaction is associated with.
        /// Used for resource management and locking.
        /// </summary>
        string SetName { get; }
    }
}