namespace NkdsUi.ViewModels
{
    /// <summary>
    /// Comparer that sorts nullable values with nulls always last,
    /// regardless of sort direction.
    /// </summary>
    public class NullsLastComparer<T>(bool descending) : IComparer<T?> where T : IComparable<T>
    {
        private readonly bool _descending = descending;

        public int Compare(T? x, T? y)
        {
            if (x == null && y == null) return 0;
            if (x == null) return 1;
            if (y == null) return -1;
            int result = x.CompareTo(y);
            return _descending ? -result : result;
        }
    }
}